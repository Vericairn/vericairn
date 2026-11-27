// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using Vericairn.Adapters.Git;
using Vericairn.Protocol;
using Vericairn.Records.Authoring;
using Vericairn.Records.Estate;
using Vericairn.Records.History;
using Vericairn.Records.Identity;
using Vericairn.Records.Model;
using Vericairn.Records.Signing;
using Vericairn.Records.Tests;
using Vericairn.Runtime.Providers;
using Vericairn.Runtime.Service;

namespace Vericairn.Runtime.Tests;

/// <summary>History checkpoints at the runtime's boundary: writing them, refusing what they forbid, and the check-time git warning.</summary>
public class HistoryCheckpointServiceTests
{
    private static readonly string[] GitIdentity = ["-c", "user.name=Test", "-c", "user.email=test@example.invalid", "-c", "commit.gpgsign=false"];

    [Fact]
    public void TheRuntimeWritesACheckpointOnlyWhenItFollowsEveryHeadAndListsTheTail()
    {
        using var f = new Fixture();
        RecordWritten adoption = Ok(f.Checkpoint(f.Records.Estate.OperatorId, "adoption", f.Unsigned()));
        EstateModel model = f.Api.Session.Load();
        Assert.False(model.Findings.HasErrors, string.Join('\n', model.Findings));
        Assert.Equal(adoption.Id, Assert.Single(SignedHistory.Of(model).Heads).Id);

        // A checkpoint prepared before another record was written no longer lists the tail, and one naming no head skips one.
        CheckpointDraft stale = f.Prepare("interval");
        Ok(f.Api.NewRecord(new NewRecordRequest("work-order", f.Records.Estate.OperatorId, PolicyFixture.Element(JsonNode.Parse(HistoryFixture.Order("HIS-20"))!))));
        WriteOutcome refused = f.Api.NewRecord(new NewRecordRequest("history-checkpoint", f.Records.Estate.BuilderId, stale.Body));
        Assert.False(refused.Ok);
        Assert.Contains("lists every record no head witnesses", refused.Problem!.Message, StringComparison.Ordinal);
        JsonObject skipping = JsonNode.Parse(f.Prepare("interval").Body.GetRawText())!.AsObject();
        skipping["previous"] = new JsonArray();
        skipping["trigger"] = "adoption";
        refused = f.Api.NewRecord(new NewRecordRequest("history-checkpoint", f.Records.Estate.OperatorId, PolicyFixture.Element(skipping)));
        Assert.False(refused.Ok);
        Assert.Contains("follows every head", refused.Problem!.Message, StringComparison.Ordinal);

        // The agent's interval checkpoint witnesses; witnessing grants nothing.
        RecordWritten interval = Ok(f.Checkpoint(f.Records.Estate.BuilderId, "interval"));
        model = f.Api.Session.Load();
        Assert.False(model.Findings.HasErrors, string.Join('\n', model.Findings));
        Assert.Equal(interval.Id, Assert.Single(SignedHistory.Of(model).Heads).Id);
        Assert.True(SignatureChecks.IsAuthentic(model, model.ById[interval.Id]));
    }

    [Fact]
    public void AnAgentsAnchorsAnEarlierTimeAndAnExplicitIdAreRefused()
    {
        using var f = new Fixture();
        f.Require();
        Ok(f.Checkpoint(f.Records.Estate.OperatorId, "adoption", f.Unsigned()));
        RecordEnvelope late = f.Records.Estate.Add("work-order", f.Records.Estate.OperatorId, HistoryFixture.Order("HIS-21", "Late"));
        WriteOutcome agents = f.Checkpoint(f.Records.Estate.BuilderId, "manual", [late]);
        Assert.False(agents.Ok);
        Assert.Contains(agents.Problem!.Findings, x => x.Code == "history.anchor-author");
        File.Delete(Path.Combine(f.Records.Estate.Root, late.ExpectedPath));

        CheckpointDraft draft = f.Prepare("manual");
        WriteOutcome dated = f.Api.NewRecord(new NewRecordRequest("history-checkpoint", f.Records.Estate.OperatorId, draft.Body, At: "2026-10-13T12:00:00Z"));
        Assert.False(dated.Ok);
        Assert.Contains("states no earlier time", dated.Problem!.Message, StringComparison.Ordinal);
        WriteOutcome named = f.Api.NewRecord(new NewRecordRequest("history-checkpoint", f.Records.Estate.OperatorId, draft.Body, Id: Ulid.NewUlid().Value));
        Assert.False(named.Ok);
    }

    [Fact]
    public void WhileAWitnessedRecordIsMissingWritesAndReleaseSubmissionAreRefused()
    {
        using var f = new Fixture();
        RecordEnvelope product = f.Records.Add("product", ReleaseLineFixture.ProductBody());
        var provider = new FakeReleaseProvider();
        f.Api.Providers = new ProviderRegistry();
        f.Api.Providers.Register(provider);
        ReleaseGateSource.Prepare(f.Api, "Example/widget", provider.HeadSha);
        var request = new ReleaseSubmitRequest(product.Id, "1.1.0", "Example/widget", f.Records.Estate.OperatorId,
            Provider: provider.Provider, Commit: provider.HeadSha, Expected: product.ComputedHash) { ClientBuild = EstateService.Version };
        Ok(f.Checkpoint(f.Records.Estate.OperatorId, "adoption", f.Unsigned()));
        Assert.DoesNotContain(f.Api.ReleaseCheck(request).Check?.Objections ?? [], o => o.Code.StartsWith("history.", StringComparison.Ordinal));

        string path = Path.Combine(f.Records.Estate.Root, f.Records.Decision.ExpectedPath);
        byte[] bytes = File.ReadAllBytes(path);
        File.Delete(path);
        ReleaseResponse checkedRelease = f.Api.ReleaseCheck(request);
        Assert.False(checkedRelease.Ok);
        Assert.Contains(checkedRelease.Check!.Objections, o => o.Code == "history.removed" && o.Location == f.Records.Decision.ExpectedPath);
        WriteOutcome write = f.Api.NewRecord(new NewRecordRequest("work-order", f.Records.Estate.OperatorId, PolicyFixture.Element(JsonNode.Parse(HistoryFixture.Order("HIS-23"))!)));
        Assert.False(write.Ok);
        Assert.Contains(write.Problem!.Findings, x => x.Code == "history.removed");
        string status = f.Api.Status().Text;
        Assert.True(status.IndexOf("history.removed", StringComparison.Ordinal) < status.IndexOf("Principals:", StringComparison.Ordinal));

        File.WriteAllBytes(path, bytes);
        Assert.DoesNotContain(f.Api.ReleaseCheck(request).Check?.Objections ?? [], o => o.Code.StartsWith("history.", StringComparison.Ordinal));
    }

    [Fact]
    public void RecordFilesDeletedInGitAreWarnedAtCheckTimeAndUnavailableInAShallowClone()
    {
        TestPrerequisites.RequireGit();
        using var f = new Fixture();
        RecordEnvelope order = f.Records.Estate.Add("work-order", f.Records.Estate.OperatorId, HistoryFixture.Order("HIS-24"), signer: f.Records.OperatorSigner);
        string root = f.Records.Estate.Root;
        Git(root, "init", "-q", "-b", "main");
        Git(root, "config", "core.autocrlf", "false");
        File.WriteAllText(Path.Combine(root, ".gitignore"), ".vericairn/\n");
        Git(root, "add", "-A");
        Git(root, [.. GitIdentity, "commit", "-q", "-m", "The estate"]);
        Git(root, "rm", "-q", order.ExpectedPath);
        Git(root, [.. GitIdentity, "commit", "-q", "-m", "Remove a record no checkpoint witnessed"]);
        string deleting = Git(root, "rev-parse", "HEAD");
        f.Api.Providers.Register(new GitCommandAdapter());

        FindingsResponse check = f.Api.Check();
        FindingDto warning = Assert.Single(check.Findings, x => x.Code == "history.deleted-in-git");
        Assert.Equal("warning", warning.Severity);
        Assert.Equal(order.ExpectedPath, warning.Location);
        Assert.Contains(deleting, warning.Message, StringComparison.Ordinal);
        // The warning depends on the clone, so the canonical status does not carry it.
        Assert.DoesNotContain("history.deleted-in-git", f.Api.Status().Json.GetRawText(), StringComparison.Ordinal);

        string shallow = Path.Combine(Path.GetTempPath(), "vericairn-history-shallow-" + Ulid.NewUlid().Value);
        try
        {
            Git(Path.GetTempPath(), "clone", "-q", "--depth", "1", new Uri(root).AbsoluteUri, shallow);
            RecordDeletions? deletions = new GitCommandAdapter().DeletedRecordFiles(shallow);
            Assert.NotNull(deletions);
            Assert.True(deletions.Shallow);
            Assert.Empty(deletions.Deleted);
            RecordDeletions? full = new GitCommandAdapter().DeletedRecordFiles(root);
            Assert.Equal(order.ExpectedPath, Assert.Single(full!.Deleted).Path);
        }
        finally
        {
            foreach (string file in Directory.Exists(shallow) ? Directory.EnumerateFiles(shallow, "*", SearchOption.AllDirectories) : [])
                File.SetAttributes(file, FileAttributes.Normal);
            if (Directory.Exists(shallow)) Directory.Delete(shallow, recursive: true);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AnUnwitnessedDeletionIsAcknowledgedByARecordSignedWithRootPresence(bool fixtureClockAhead)
    {
        TestPrerequisites.RequireGit();
        using var f = new Fixture();
        if (fixtureClockAhead) f.Records.Estate.SetClock(DateTimeOffset.UtcNow.AddMinutes(1));
        PresenceKeyStore presence = f.HoldPresenceKey();
        (RecordEnvelope record, RecordEnvelope other, string commit) = PrepareDeletions(f);
        Assert.Equal(2, f.Api.Check().Findings.Count(x => x.Code == "history.deleted-in-git" && x.Severity == "warning"));
        presence.LastReason = null;

        RecordWritten written = Ok(WriteDeletionAcknowledgment(f, record, commit, "Remove duplicate test evidence."));
        Assert.Contains("acknowledges", presence.LastReason, StringComparison.Ordinal);
        Assert.Contains(record.Id, presence.LastReason, StringComparison.Ordinal);

        FindingsResponse checkedEstate = f.Api.Check();
        FindingDto acknowledged = Assert.Single(checkedEstate.Findings, x => x.Code == "history.deleted-in-git" && x.Location == record.ExpectedPath);
        Assert.Equal("info", acknowledged.Severity);
        Assert.Contains("Acknowledged", acknowledged.Message, StringComparison.Ordinal);
        Assert.Contains(written.Id, acknowledged.Message, StringComparison.Ordinal);
        Assert.Contains(commit, acknowledged.Message, StringComparison.Ordinal);
        Assert.Contains(checkedEstate.Findings, x => x.Code == "history.deleted-in-git" && x.Location == other.ExpectedPath && x.Severity == "warning");
        Assert.False(SignedHistory.Of(f.Api.Session.Load()).IsAcknowledgedRemoval(record.Id));
        Assert.DoesNotContain("history.deleted-in-git", f.Api.Status().Json.GetRawText(), StringComparison.Ordinal);
    }

    [Fact]
    public void ADeletionAcknowledgedByItsBranchCommitStaysAcknowledgedThroughAMerge()
    {
        TestPrerequisites.RequireGit();
        using var f = new Fixture();
        f.HoldPresenceKey();
        (RecordEnvelope record, RecordEnvelope other, string branchCommit) = PrepareDeletions(f, leaveOffMain: true);
        string root = f.Records.Estate.Root;
        Git(root, "switch", "-q", "main");
        Git(root, [.. GitIdentity, "commit", "-q", "--allow-empty", "-m", "Advance main"]);
        Git(root, [.. GitIdentity, "merge", "--no-ff", "--no-edit", "side"]);
        string mergeCommit = Git(root, "rev-parse", "HEAD");
        FindingDto warning = Assert.Single(f.Api.Check().Findings, x => x.Code == "history.deleted-in-git" && x.Location == record.ExpectedPath);
        Assert.Equal("warning", warning.Severity);
        Assert.Contains(mergeCommit, warning.Message, StringComparison.Ordinal);

        RecordWritten written = Ok(WriteDeletionAcknowledgment(f, record, branchCommit, "Remove duplicate test evidence."));
        FindingsResponse check = f.Api.Check();
        FindingDto acknowledged = Assert.Single(check.Findings, x => x.Code == "history.deleted-in-git" && x.Location == record.ExpectedPath);
        Assert.Equal("info", acknowledged.Severity);
        Assert.Contains(written.Id, acknowledged.Message, StringComparison.Ordinal);
        Assert.Contains(mergeCommit, acknowledged.Message, StringComparison.Ordinal);
        Assert.Contains(branchCommit, acknowledged.Message, StringComparison.Ordinal);
        Assert.Contains(check.Findings, x => x.Code == "history.deleted-in-git" && x.Location == other.ExpectedPath && x.Severity == "warning");

        // A later deletion of different contents at the same path still warns.
        f.Records.Estate.SetClock(record.AtTime.AddSeconds(-1));
        RecordEnvelope replacement = f.Records.Add("work-order", JsonNode.Parse(HistoryFixture.Order("HIS-26", "Different contents"))!.AsObject(), id: record.Id);
        Assert.Equal(record.ExpectedPath, replacement.ExpectedPath);
        Assert.NotEqual(record.ComputedHash, replacement.ComputedHash);
        Git(root, "add", record.ExpectedPath);
        Git(root, [.. GitIdentity, "commit", "-q", "-m", "Restore the path with different contents"]);
        Git(root, "rm", "-q", record.ExpectedPath);
        Git(root, [.. GitIdentity, "commit", "-q", "-m", "Delete the changed record"]);
        warning = Assert.Single(f.Api.Check().Findings, x => x.Code == "history.deleted-in-git" && x.Location == record.ExpectedPath);
        Assert.Equal("warning", warning.Severity);
        Assert.Contains(Git(root, "rev-parse", "HEAD"), warning.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AnAcknowledgmentDoesNotSettleALaterDeletionOfTheSameBytes(bool deleteInMerge)
    {
        TestPrerequisites.RequireGit();
        using var f = new Fixture();
        f.HoldPresenceKey();
        (RecordEnvelope record, _, string firstDeletion) = PrepareDeletions(f);
        Ok(WriteDeletionAcknowledgment(f, record, firstDeletion, "Remove duplicate test evidence."));
        Assert.Contains(f.Api.Check().Findings, x => x.Code == "history.deleted-in-git" &&
            x.Location == record.ExpectedPath && x.Severity == "info");

        string root = f.Records.Estate.Root;
        Git(root, "checkout", firstDeletion + "^", "--", record.ExpectedPath);
        Git(root, [.. GitIdentity, "commit", "-q", "-m", "Restore the same record"]);
        Assert.DoesNotContain(f.Api.Check().Findings, x => x.Code == "history.deleted-in-git" && x.Location == record.ExpectedPath);

        if (deleteInMerge)
        {
            // This parent has its own deletion, while the parent that reaches the acknowledged deletion holds the restored record.
            Git(root, "switch", "-q", "-c", "side", firstDeletion + "^");
            Git(root, "rm", "-q", record.ExpectedPath);
            Git(root, [.. GitIdentity, "commit", "-q", "-m", "Delete the record on the side branch"]);
            Git(root, "switch", "-q", "main");
            Git(root, [.. GitIdentity, "merge", "--no-ff", "--no-edit", "side"]);
        }
        else
        {
            Git(root, "rm", "-q", record.ExpectedPath);
            Git(root, [.. GitIdentity, "commit", "-q", "-m", "Delete the restored record"]);
        }
        string laterDeletion = Git(root, "rev-parse", "HEAD");

        FindingDto warning = Assert.Single(f.Api.Check().Findings, x => x.Code == "history.deleted-in-git" && x.Location == record.ExpectedPath);
        Assert.Equal("warning", warning.Severity);
        Assert.Contains(laterDeletion, warning.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("wrong-hash", "exact deletion")]
    [InlineData("unreachable", "main line")]
    [InlineData("non-root", "root operator")]
    [InlineData("missing-reason", "reason")]
    [InlineData("blank-reason", "reason")]
    [InlineData("no-presence", "presence")]
    public void AnUnwitnessedDeletionWithoutExactHistoryOrRootPresenceIsRefused(string defect, string explanation)
    {
        TestPrerequisites.RequireGit();
        using var f = new Fixture();
        (RecordEnvelope record, _, string commit) = PrepareDeletions(f, leaveOffMain: defect == "unreachable");
        if (defect is not ("no-presence" or "non-root")) f.HoldPresenceKey();
        int checkpoints = f.Api.Session.Load().OfType("history-checkpoint").Count();
        int signatures = 0;
        f.Api.BeforeSigningRecord = () => signatures++;
        WriteOutcome refused = WriteDeletionAcknowledgment(f, record, commit, defect switch
        {
            "missing-reason" => null,
            "blank-reason" => "  ",
            _ => "Remove duplicate test evidence.",
        }, defect == "wrong-hash" ? f.Records.Decision.ComputedHash : null,
            defect == "non-root" ? f.Records.Estate.BuilderId : null);

        Assert.False(refused.Ok);
        Assert.Contains(explanation, refused.Problem!.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(checkpoints, f.Api.Session.Load().OfType("history-checkpoint").Count());
        Assert.Equal(0, signatures);
        Assert.Contains(f.Api.Check().Findings, x => x.Code == "history.deleted-in-git" && x.Location == record.ExpectedPath && x.Severity == "warning");
    }

    [Fact]
    public void ADeletionCommitCannotReplaceTheWitnessedRemovalRule()
    {
        TestPrerequisites.RequireGit();
        using var f = new Fixture();
        f.HoldPresenceKey();
        (RecordEnvelope record, _, string commit) = PrepareDeletions(f, witness: true);

        WriteOutcome refused = WriteDeletionAcknowledgment(f, record, commit, "Remove duplicate test evidence.");
        Assert.False(refused.Ok);
        Assert.Contains("witnessed", refused.Problem!.Message, StringComparison.Ordinal);
        Assert.Contains(f.Api.Check().Findings, x => x.Code == "history.removed" && x.Location == record.ExpectedPath);
    }

    private static (RecordEnvelope Record, RecordEnvelope Other, string Commit) PrepareDeletions(Fixture f, bool leaveOffMain = false, bool witness = false)
    {
        if (!witness) Ok(f.Checkpoint(f.Records.Estate.OperatorId, "adoption", f.Unsigned()));
        RecordEnvelope record = f.Records.Add("work-order", JsonNode.Parse(HistoryFixture.Order("HIS-26"))!.AsObject());
        RecordEnvelope other = f.Records.Add("work-order", JsonNode.Parse(HistoryFixture.Order("HIS-27"))!.AsObject());
        if (witness) Ok(f.Checkpoint(f.Records.Estate.OperatorId, "adoption", f.Unsigned()));
        string root = f.Records.Estate.Root;
        Git(root, "init", "-q", "-b", "main");
        Git(root, "config", "core.autocrlf", "false");
        File.WriteAllText(Path.Combine(root, ".gitignore"), ".vericairn/\n");
        Git(root, "add", "-A");
        Git(root, [.. GitIdentity, "commit", "-q", "-m", "The estate"]);
        if (leaveOffMain) Git(root, "switch", "-c", "side");
        Git(root, "rm", "-q", record.ExpectedPath, other.ExpectedPath);
        Git(root, [.. GitIdentity, "commit", "-q", "-m", "Remove two records"]);
        f.Api.Providers.Register(new GitCommandAdapter());
        return (record, other, Git(root, "rev-parse", "HEAD"));
    }

    private static WriteOutcome WriteDeletionAcknowledgment(Fixture f, RecordEnvelope record, string commit, string? reason, string? hash = null, string? by = null)
    {
        JsonObject body = JsonNode.Parse(f.Prepare("manual").Body.GetRawText())!.AsObject();
        var removal = new JsonObject { ["id"] = record.Id, ["hash"] = hash ?? record.ComputedHash, ["commit"] = commit };
        if (reason is not null) removal["reason"] = reason;
        body["removed"] = new JsonArray(removal);
        return f.Api.NewRecord(new NewRecordRequest("history-checkpoint", by ?? f.Records.Estate.OperatorId, PolicyFixture.Element(body)));
    }

    /// <summary>File-backed test key with an observable presence prompt at the store boundary.</summary>
    private sealed class PresenceKeyStore(FileKeyStore keys, string device) : IDeviceKeyStore
    {
        public string Kind => DeviceKeyStores.IosSecureEnclaveKind;
        public string? LastReason { get; set; }
        public bool Has(string deviceId) => deviceId == device && keys.Has(deviceId);
        public byte[] Create(string deviceId) => keys.Create(deviceId);
        public byte[]? PublicKey(string deviceId) => keys.PublicKey(deviceId);
        public byte[] Sign(string deviceId, ReadOnlySpan<byte> digest) => keys.Sign(deviceId, digest);
        public byte[] Sign(string deviceId, ReadOnlySpan<byte> digest, string reason)
        {
            LastReason = reason;
            return Sign(deviceId, digest);
        }

        public void Remove(string deviceId) => keys.Remove(deviceId);
        public void Promote(string keyId, string deviceId) => keys.Promote(keyId, deviceId);
    }

    [Fact]
    public void InventoryObjectsAreReadWithinTheObjectLimitAndNeverThroughALink()
    {
        using var f = new Fixture();
        Ok(f.Checkpoint(f.Records.Estate.OperatorId, "adoption", f.Unsigned()));
        RecordEnvelope adoption = f.Api.Session.Load().OfType("history-checkpoint").Single();
        string hash = adoption.Body.GetProperty("inventory")[0].GetString()!;
        string objects = Path.Combine(f.Records.Estate.Root, EstateLayout.ObjectsDirectory);
        string path = Path.Combine(objects, hash["sha256:".Length..]);
        string outside = Path.Combine(Path.GetTempPath(), "vericairn-history-outside-" + Ulid.NewUlid().Value);
        Directory.CreateDirectory(outside);
        try
        {
            // An object reached through a link is not read, even when the bytes it leads to are the right ones.
            string target = Path.Combine(outside, Path.GetFileName(path));
            File.Move(path, target);
            bool linked = true;
            try { File.CreateSymbolicLink(path, target); }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or PlatformNotSupportedException) { linked = false; }
            if (linked)
                Assert.Contains(f.Api.Session.Load().Findings, x => x.Code == "history.checkpoint-missing" && x.Message.Contains(hash, StringComparison.Ordinal));
            File.Delete(path);
            File.Move(target, path);
            Assert.DoesNotContain(f.Api.Session.Load().Findings, x => x.Code == "history.checkpoint-missing");

            // An object over the limit is not read either.
            byte[] large = new byte[EstateLayout.MaxObjectBytes + 1];
            Array.Fill(large, (byte)'a');
            string largeHash = Records.Hashing.ContentHash.Sha256(large);
            File.WriteAllBytes(Path.Combine(objects, largeHash["sha256:".Length..]), large);
            JsonObject body = JsonNode.Parse(f.Prepare("interval").Body.GetRawText())!.AsObject();
            body["inventory"] = new JsonArray(largeHash);
            f.Records.Add("history-checkpoint", body);
            Assert.Contains(f.Api.Session.Load().Findings, x => x.Code == "history.checkpoint-missing" && x.Message.Contains(largeHash, StringComparison.Ordinal));
        }
        finally { Directory.Delete(outside, recursive: true); }
    }

    [Fact]
    public void AfterAClosingTheRuntimeRefusesWhatTheClosedDeviceSignsAndAnchorsWhatTheClosingRefused()
    {
        using var f = new Fixture();
        f.Require();
        Ok(f.Checkpoint(f.Records.Estate.OperatorId, "adoption", f.Unsigned()));

        // The operator closes the builder's device in the test's key store; a record it signed elsewhere and merged after the closing
        // counts for nothing until the operator anchors it, and a record that counts needs no anchor.
        Ok(f.Checkpoint(f.Records.Estate.OperatorId, "closing", closes: ("device", f.Records.BuilderDevice.Id)));
        RecordEnvelope merged = f.Records.Add("work-order", JsonNode.Parse(HistoryFixture.Order("HIS-30", "Merged after the closing"))!.AsObject(), builder: true);
        EstateModel model = f.Api.Session.Load();
        Assert.False(model.Counts(model.ById[merged.Id]));
        Assert.Contains(model.Findings, x => x.Code == "history.after-closing" && x.Location.StartsWith(merged.ExpectedPath, StringComparison.Ordinal));
        WriteOutcome needless = f.Checkpoint(f.Records.Estate.OperatorId, "manual", [model.ById[f.Records.Decision.Id]]);
        Assert.False(needless.Ok);
        Assert.Contains("needs no anchor", needless.Problem!.Message, StringComparison.Ordinal);
        Ok(f.Checkpoint(f.Records.Estate.OperatorId, "manual", [model.ById[merged.Id]]));
        model = f.Api.Session.Load();
        Assert.True(model.Counts(model.ById[merged.Id]));
        Assert.DoesNotContain(model.Findings, x => x.Code == "history.after-closing");

        // Once the builder's runtime device is closed, the runtime refuses to write what that device would sign.
        RecordEnvelope runtimeDevice = model.OfType("device").Single(d => EstateModel.BodyString(d, "principal") == f.Records.Estate.BuilderId && EstateModel.BodyString(d, "name") == "Runtime's device");
        Ok(f.Checkpoint(f.Records.Estate.OperatorId, "closing", closes: ("device", runtimeDevice.Id)));
        WriteOutcome refused = f.Api.NewRecord(new NewRecordRequest("work-order", f.Records.Estate.BuilderId, PolicyFixture.Element(JsonNode.Parse(HistoryFixture.Order("HIS-31"))!)));
        Assert.False(refused.Ok);
        Assert.Contains(refused.Problem!.Findings, x => x.Code == "history.after-closing");
        Ok(f.Api.NewRecord(new NewRecordRequest("work-order", f.Records.Estate.OperatorId, PolicyFixture.Element(JsonNode.Parse(HistoryFixture.Order("HIS-32"))!))));
    }

    [Fact]
    public void TheRuntimeRefusesAStatedTimeMoreThanFiveMinutesPastItsClock()
    {
        using var f = new Fixture();
        // The runtime's clock an hour after the devices were made, so earlier stated times still follow their admissions.
        DateTimeOffset clock = DateTimeOffset.FromUnixTimeSeconds(DateTimeOffset.UtcNow.ToUnixTimeSeconds()).AddHours(1);
        f.Api.AuthoringClock = new HeldClock(clock);
        WriteOutcome ahead = f.Api.NewRecord(new NewRecordRequest("work-order", f.Records.Estate.OperatorId, PolicyFixture.Element(JsonNode.Parse(HistoryFixture.Order("HIS-33"))!), At: Stated(clock.AddMinutes(5).AddSeconds(1))));
        Assert.False(ahead.Ok);
        Assert.Contains("more than five minutes past this runtime's clock", ahead.Problem!.Message, StringComparison.Ordinal);
        WriteOutcome start = f.Api.Attempt(new AttemptRequest(Ulid.NewUlid().Value, f.Records.Estate.BuilderId, "the workspace", "Starting.", At: Stated(clock.AddHours(1))));
        Assert.False(start.Ok);
        Assert.Contains("more than five minutes past this runtime's clock", start.Problem!.Message, StringComparison.Ordinal);

        // Within five minutes, and any earlier time, a stated time is the author's to give.
        Ok(f.Api.NewRecord(new NewRecordRequest("work-order", f.Records.Estate.OperatorId, PolicyFixture.Element(JsonNode.Parse(HistoryFixture.Order("HIS-34"))!), At: Stated(clock.AddMinutes(5)))));
        Ok(f.Api.NewRecord(new NewRecordRequest("work-order", f.Records.Estate.OperatorId, PolicyFixture.Element(JsonNode.Parse(HistoryFixture.Order("HIS-35"))!), At: Stated(clock.AddMinutes(-30)))));

        static string Stated(DateTimeOffset at) => RecordParser.FormatTimestamp(at);
    }

    private static RecordWritten Ok(WriteOutcome outcome)
    {
        Assert.True(outcome.Ok, JsonSerializer.Serialize(outcome.Problem, ProtocolJson.Options));
        return outcome.Written!;
    }

    private static string Git(string directory, params string[] arguments)
    {
        var info = new ProcessStartInfo("git") { WorkingDirectory = directory, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        foreach (string argument in arguments) info.ArgumentList.Add(argument);
        using Process process = Process.Start(info)!;
        string output = process.StandardOutput.ReadToEnd();
        string error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        Assert.True(process.ExitCode == 0, $"git {string.Join(' ', arguments)}: {error}");
        return output.Trim();
    }

    /// <summary>
    /// A format-3 estate with its operator's first authority and builder held in a disposable key store, and a device of each held by
    /// the runtime, admitted by the operator's first authority, so the runtime signs as either.
    /// </summary>
    private sealed class Fixture : IDisposable
    {
        public Fixture()
        {
            Api = EstateService.Open(Records.Estate.Root);
            foreach (string principal in new[] { Records.Estate.OperatorId, Records.Estate.BuilderId })
            {
                WriteOutcome device = Api.DeviceCreate(new(principal, "Runtime's device", "file"));
                Assert.True(device.Ok, device.Problem?.Message);
                RecordEnvelope held = Api.Session.Load().ById[device.Written!.Id];
                Records.Estate.SetClock(held.AtTime.AddSeconds(-1));
                Records.Add("admission", new() { ["principal"] = principal, ["status"] = "admitted", ["note"] = "The runtime's device." }, subject: held.Reference);
            }
        }

        public PolicyFixture Records { get; } = new();

        public EstateService Api { get; }

        public PresenceKeyStore HoldPresenceKey()
        {
            string id = Ulid.NewUlid().Value;
            var keys = new FileKeyStore(VericairnHome.Path());
            var presence = new PresenceKeyStore(keys, id);
            string principal = Records.Estate.OperatorId;
            string publicKey = RecordSigning.EncodePublicKey(keys.Create(id));
            JsonObject body = DeviceRecords.Body(principal, "Presence test device", "ios", presence.Kind, publicKey, ["embedded"], 3, "required");
            // The fixture advances one second for each record. Both must precede a checkpoint dated by the runtime.
            Records.Estate.SetClock(DateTimeOffset.UtcNow.AddSeconds(-3));
            RecordEnvelope device = Records.Estate.Add("device", principal, body.ToJsonString(), id: id, signer: new StoreSigner(presence, id));
            Records.Add("admission", new JsonObject { ["principal"] = principal, ["status"] = "admitted", ["note"] = "Test presence device." }, subject: device.Reference);
            Api.KeyStores = [presence, keys];
            return presence;
        }

        public RecordEnvelope Require() => Records.Add("estate", new JsonObject
        {
            ["name"] = "Test Estate", ["slug"] = "test-estate", ["purpose"] = "Exercise the record format.", ["operator"] = Records.Estate.OperatorId, ["recordFormat"] = 3,
            ["policies"] = new JsonObject { ["signatures"] = "required", ["releaseAcceptance"] = new JsonArray("operator") },
        }, supersedes: Records.Estate.Estate.Id);

        public IReadOnlyList<RecordEnvelope> Unsigned() => [.. Api.Session.Load().Records.Where(r => r.Signatures.Count == 0)];

        public CheckpointDraft Prepare(string trigger, IEnumerable<RecordEnvelope>? anchored = null, (string Kind, string Id)? closes = null)
        {
            CheckpointDraft draft = SignedHistory.Prepare(Api.Session.Load(), trigger, anchored, closes: closes);
            foreach (byte[] bytes in draft.Objects) Api.Session.Objects.Put(bytes);
            return draft;
        }

        public WriteOutcome Checkpoint(string by, string trigger, IEnumerable<RecordEnvelope>? anchored = null, (string Kind, string Id)? closes = null) =>
            Api.NewRecord(new NewRecordRequest("history-checkpoint", by, Prepare(trigger, anchored, closes).Body));

        public void Dispose()
        {
            Api.Dispose();
            Records.Dispose();
        }
    }
}
