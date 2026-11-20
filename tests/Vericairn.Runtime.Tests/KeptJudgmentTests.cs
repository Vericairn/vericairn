// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text;
using System.Text.Json;
using Vericairn.Records.Authoring;
using Vericairn.Records.Estate;
using Vericairn.Records.Model;
using Vericairn.Records.Signing;
using Vericairn.Records.Tests;
using Vericairn.Runtime.Service;
using Vericairn.Runtime.Store;

namespace Vericairn.Runtime.Tests;

/// <summary>
/// From 0.9.4 (FIX-148; design/performance.md §§ 3.1, 3.2, 5, 6): the safety of a one-shot runtime's kept judgment and of the signature
/// verdicts and keys kept for the process. In one runtime, a revocation, a key rotation, a superseded admission and a record replaced at
/// the same id change the next judgment exactly as a fresh judgment in a new runtime with empty tables and no parse cache judges them; a
/// simulated platform refusal of a verification or an import is asked again; and at <see cref="EstateSession.Load"/> itself a load while
/// the session holds the write lock is fresh from any caller and thread, and another writer's record and a non-record write of the
/// runtime are seen. Each runtime counts in a context of its own with fresh tables, so the counts are exact.
/// </summary>
public sealed class KeptJudgmentTests
{
    [Fact]
    public void ARevocationChangesTheNextJudgmentAsAFreshJudgment()
    {
        using var f = new HistoryFixture();
        f.Estate.Add("work-order", f.Estate.BuilderId, HistoryFixture.Order("KEPT-1"), signer: f.BuilderSigner);
        var judgments = new JudgmentContext(new SignatureTables());
        using EstateService service = OneShot(f.Root, judgments);
        EstateModel before = service.Session.Load();
        Assert.Equal(Fresh(f.Root), Describe(before));
        long verified = judgments.Verifications;

        f.Revoke(f.BuilderDevice, f.BuilderAdmission);
        RecordEnvelope late = f.Estate.Add("work-order", f.Estate.BuilderId, HistoryFixture.Order("KEPT-2"), signer: f.BuilderSigner);
        EstateModel after = service.Session.Load();
        Assert.NotSame(before, after);
        Assert.Equal(Fresh(f.Root), Describe(after));
        Assert.Contains(after.Findings, x => x.Code == "signature.revoked" && x.Location.StartsWith(late.ExpectedPath, StringComparison.Ordinal));
        // Only the two new records' signatures were verified: every earlier verdict was the process's.
        Assert.Equal(2, judgments.Verifications - verified);
        Assert.Equal(2, judgments.Judgments);
    }

    [Fact]
    public void AKeyRotationChangesTheNextJudgmentAsAFreshJudgment()
    {
        using var f = new HistoryFixture();
        var judgments = new JudgmentContext(new SignatureTables());
        using EstateService service = OneShot(f.Root, judgments);
        EstateModel before = service.Session.Load();

        (_, StoreSigner rotated) = f.Rotate(f.BuilderDevice, f.BuilderSigner);
        RecordEnvelope stale = f.Estate.Add("work-order", f.Estate.BuilderId, HistoryFixture.Order("KEPT-1"), signer: f.BuilderSigner);
        RecordEnvelope current = f.Estate.Add("work-order", f.Estate.BuilderId, HistoryFixture.Order("KEPT-2"), signer: rotated);
        EstateModel after = service.Session.Load();
        Assert.NotSame(before, after);
        Assert.Equal(Fresh(f.Root), Describe(after));
        Assert.Contains(after.Findings, x => x.Code == "signature.key-time" && x.Location.StartsWith(stale.ExpectedPath, StringComparison.Ordinal));
        Assert.DoesNotContain(after.Findings, x => x.Location.StartsWith(current.ExpectedPath, StringComparison.Ordinal) && x.Severity == Severity.Error);
    }

    [Fact]
    public void ASupersededAdmissionChangesTheNextJudgmentAsAFreshJudgment()
    {
        using var f = new HistoryFixture();
        f.Estate.Add("work-order", f.Estate.BuilderId, HistoryFixture.Order("KEPT-1"), signer: f.BuilderSigner);
        var judgments = new JudgmentContext(new SignatureTables());
        using EstateService service = OneShot(f.Root, judgments);
        EstateModel before = service.Session.Load();

        // The builder's admission is superseded by one that admits the device for the operator, so the builder's records lose their signer.
        f.Estate.Add("admission", f.Estate.OperatorId, HistoryFixture.Admission(f.Estate.OperatorId, "Moved to the operator."), f.BuilderDevice.Reference,
            supersedes: f.BuilderAdmission.Id, signer: f.OperatorSigner);
        f.Estate.Add("work-order", f.Estate.BuilderId, HistoryFixture.Order("KEPT-2"), signer: f.BuilderSigner);
        EstateModel after = service.Session.Load();
        Assert.NotSame(before, after);
        Assert.Equal(Fresh(f.Root), Describe(after));
        Assert.NotEqual(Describe(before), Describe(after));
    }

    [Fact]
    public void ARecordReplacedAtTheSameIdIsJudgedByItsNewBytes()
    {
        using var f = new HistoryFixture();
        RecordEnvelope order = f.Estate.Add("work-order", f.Estate.BuilderId, HistoryFixture.Order("KEPT-1"), signer: f.BuilderSigner);
        var judgments = new JudgmentContext(new SignatureTables());
        using EstateService service = OneShot(f.Root, judgments);
        EstateModel before = service.Session.Load();
        Assert.DoesNotContain(before.Findings, x => x.Severity == Severity.Error);

        // The same id, time and signature over another title: the kept verdict was for the old content hash, so it cannot answer.
        string path = f.PathOf(order);
        string replaced = Encoding.UTF8.GetString(File.ReadAllBytes(path)).Replace("A recorded work order", "A work order replaced at its id", StringComparison.Ordinal);
        File.WriteAllText(path, replaced);
        EstateModel after = service.Session.Load();
        Assert.NotSame(before, after);
        Assert.Equal(Fresh(f.Root), Describe(after));
        Assert.Contains(after.Findings, x => x.Severity == Severity.Error && x.Location.StartsWith(order.ExpectedPath, StringComparison.Ordinal));
    }

    [Fact]
    public void ARefusedVerificationOrImportIsAskedAgain()
    {
        using var f = new HistoryFixture();
        RecordEnvelope order = f.Estate.Add("work-order", f.Estate.BuilderId, HistoryFixture.Order("KEPT-1"), signer: f.BuilderSigner);
        string signature = order.Signatures[0].Signature;
        string builderKey = EstateModel.BodyString(f.BuilderDevice, "publicKey")!;
        var tables = new SignatureTables();
        var judgments = new JudgmentContext(tables) { RefuseVerification = s => s == signature };
        using EstateService service = EstateService.Open(f.Root, null, null, oneShot: false, judgments: judgments);
        EstateModel refused = service.Session.Load();
        Assert.Contains(refused.Findings, x => x.Code == "signature.invalid" && x.Location.StartsWith(order.ExpectedPath, StringComparison.Ordinal));

        // The refusal was not kept: once the platform answers, the next judgment verifies the signature again and it holds.
        judgments.RefuseVerification = null;
        long verified = judgments.Verifications;
        EstateModel asked = service.Session.Load();
        Assert.Equal(1, judgments.Verifications - verified);
        Assert.DoesNotContain(asked.Findings, x => x.Severity == Severity.Error);
        Assert.Equal(Fresh(f.Root), Describe(asked));

        // A refused import keeps no key, and the builder's key is imported again once the platform takes it.
        var imports = new JudgmentContext(new SignatureTables()) { RefuseImport = k => k == builderKey };
        using EstateService other = EstateService.Open(f.Root, null, null, oneShot: false, judgments: imports);
        EstateModel unimported = other.Session.Load();
        Assert.Contains(unimported.Findings, x => x.Code == "signature.invalid" && x.Location.StartsWith(order.ExpectedPath, StringComparison.Ordinal));
        long imported = imports.KeyImports;
        imports.RefuseImport = null;
        EstateModel reimported = other.Session.Load();
        Assert.True(imports.KeyImports - imported >= 1);
        Assert.Equal(Fresh(f.Root), Describe(reimported));
    }

    [Fact]
    public void ALoadUnderTheWriteLockIsFreshFromAnyCallerAndTheLockDropsTheKeptModel()
    {
        using var f = new HistoryFixture();
        var judgments = new JudgmentContext(new SignatureTables());
        using EstateService service = OneShot(f.Root, judgments);
        EstateSession session = service.Session;
        EstateModel kept = session.Load();
        Assert.Same(kept, session.Load());
        long judged = judgments.Judgments;

        using (session.HoldWriteLock())
        {
            EstateModel locked = session.Load();
            Assert.NotSame(kept, locked);
            Assert.NotSame(locked, session.Load());
            // Another thread's load while this session holds the lock is fresh too, and keeps nothing.
            EstateModel? elsewhere = null;
            var thread = new Thread(() => elsewhere = session.Load());
            thread.Start();
            Assert.True(thread.Join(TimeSpan.FromMinutes(2)));
            Assert.NotNull(elsewhere);
            Assert.NotSame(locked, elsewhere);
        }

        Assert.Equal(3, session.LockedJudgments);
        EstateModel released = session.Load();
        Assert.NotSame(kept, released);
        Assert.Same(released, session.Load());
        Assert.Equal(judged + 4, judgments.Judgments);
    }

    [Fact]
    public void AnotherWritersRecordAndTheRuntimesOwnNonRecordWriteAreSeen()
    {
        using var f = new HistoryFixture();
        var judgments = new JudgmentContext(new SignatureTables());
        using EstateService service = OneShot(f.Root, judgments);
        EstateModel kept = service.Session.Load();
        long opened = judgments.Judgments;

        // Another writer's record: the stamp changes, so the next load judges it.
        RecordEnvelope order = f.Estate.Add("work-order", f.Estate.BuilderId, HistoryFixture.Order("KEPT-1"), signer: f.BuilderSigner);
        EstateModel seen = service.Session.Load();
        Assert.True(seen.ById.ContainsKey(order.Id));
        Assert.Same(seen, service.Session.Load());

        // An object this runtime stores is not a record, and still drops the kept model.
        service.Session.Objects.Put("kept judgment"u8);
        EstateModel afterObject = service.Session.Load();
        Assert.NotSame(seen, afterObject);
        Assert.NotSame(kept, seen);
        Assert.Equal(2, judgments.Judgments - opened);
    }

    [Fact]
    public void ALongLivedRuntimeJudgesAtEveryLoad()
    {
        using var f = new HistoryFixture();
        var judgments = new JudgmentContext(new SignatureTables());
        using EstateService service = EstateService.Open(f.Root, null, null, oneShot: false, judgments: judgments);
        long opened = judgments.Judgments;
        Assert.NotSame(service.Session.Load(), service.Session.Load());
        Assert.Equal(opened + 2, judgments.Judgments);
    }

    private static EstateService OneShot(string root, JudgmentContext judgments) => EstateService.Open(root, null, null, oneShot: true, judgments: judgments);

    // A judgment that shares nothing with the runtime: empty signature tables, no parse cache, the files read now.
    private static string Fresh(string root) => Describe(EstateLoader.Load(Path.GetFullPath(root), EstateLoader.EnumerateRecordFiles(root), null,
        objects: EstateLoader.FileObjects(root), context: new JudgmentContext(new SignatureTables())));

    private static string Describe(EstateModel model)
    {
        var text = new StringBuilder();
        foreach (Finding finding in model.Findings) text.Append(finding.Severity).Append(' ').Append(finding.Code).Append(' ').Append(finding.Location).Append(' ').AppendLine(finding.Message);
        foreach (RecordEnvelope record in model.Records) text.Append(record.Id).Append(model.Counts(record) ? " counts" : " does not count").Append(model.IsCurrent(record) ? " current" : string.Empty).AppendLine();
        using JsonDocument status = JsonDocument.Parse(EstateStatus.RenderJson(model, new FindingList()));
        return text.Append(status.RootElement.GetRawText()).ToString();
    }
}
