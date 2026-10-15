// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using Vericairn.Records.Authoring;
using Vericairn.Records.Estate;
using Vericairn.Records.Identity;
using Vericairn.Records.Model;
using Vericairn.Records.Signing;

namespace Vericairn.Records.Tests;

public class SigningTests : IDisposable
{
    private const string FormatTwo = """ ,"presence":"none","purpose":"signing" """;
    private const string Recovery = """ ,"presence":"none","purpose":"recovery" """;
    private const string OrderBody = """{"key":"W-1","kind":"ordinary","title":"Signed task","intent":"Exercise signatures.","scope":["x"],"exitQuestion":"Done?","inputs":[],"deliverables":["y"]}""";
    private readonly string home = Path.Combine(Path.GetTempPath(), "vericairn-keys-" + Ulid.NewUlid().Value);
    private readonly FileKeyStore store;

    public SigningTests()
    {
        store = new FileKeyStore(home);
    }

    [Fact]
    public void AdmittedDeviceSignsVerifiablyAndPolicyCanRequireIt()
    {
        using var estate = new TestEstate();
        RecordEnvelope operatorDevice = CreateDevice(estate, estate.OperatorId, "Operator laptop");
        RecordEnvelope admission = estate.Add("admission", estate.OperatorId, $$"""{"principal":"{{estate.OperatorId}}","status":"admitted","note":"Root operator's own device."}""", operatorDevice.Reference, signer: Signer(operatorDevice));
        RecordEnvelope builderDevice = CreateDevice(estate, estate.BuilderId, "Build machine");
        estate.Add("admission", estate.OperatorId, $$"""{"principal":"{{estate.BuilderId}}","status":"admitted","note":"Builder's machine."}""", builderDevice.Reference, signer: Signer(operatorDevice));
        RecordEnvelope order = estate.Add("work-order", estate.BuilderId, OrderBody, signer: Signer(builderDevice));
        EstateModel model = estate.Load();
        Assert.False(model.Findings.HasErrors, string.Join("\n", model.Findings));
        Assert.Single(order.Signatures);
        Assert.True(SignatureChecks.IsAdmitted(model, builderDevice));
        Assert.NotNull(admission);

        // Policy switch: the operator supersedes the charter with signatures required, signed by the operator's device.
        string charter = estate.Estate.Body.GetRawText().Replace("\"optional\"", "\"required\"", StringComparison.Ordinal);
        estate.Add("estate", estate.OperatorId, charter, supersedes: estate.Estate.Id, signer: Signer(operatorDevice));
        estate.Add("work-status", estate.BuilderId, """{"status":"active","note":"Unsigned after the policy switch."}""", new RecordReference("work-order", order.Id, null));
        model = estate.Load();
        Assert.Contains(model.Findings, f => f.Code == "signature.required");
        Assert.DoesNotContain(model.Findings, f => f.Code == "signature.required" && f.Location.Contains(order.Id, StringComparison.Ordinal));
    }

    [Fact]
    public void TheOperatorAdmitsItsOwnDeviceOnceAndEveryLaterDeviceUnderAnAdmittedOne()
    {
        using var estate = new TestEstate();
        RecordEnvelope first = CreateDevice(estate, estate.OperatorId, "First laptop");
        RecordEnvelope root = estate.Add("admission", estate.OperatorId, $$"""{"principal":"{{estate.OperatorId}}","status":"admitted","note":"The first authority."}""", first.Reference, signer: Signer(first));
        EstateModel model = estate.Load();
        Assert.False(model.Findings.HasErrors, string.Join("\n", model.Findings));
        Assert.Equal(root.Id, SignatureChecks.FirstAuthority(model, estate.OperatorId)?.Id);

        // Anyone able to add records could mint a device for the operator and admit it with its own key; a second self-admission is refused.
        RecordEnvelope minted = CreateDevice(estate, estate.OperatorId, "Minted");
        RecordEnvelope self = estate.Add("admission", estate.OperatorId, $$"""{"principal":"{{estate.OperatorId}}","status":"admitted","note":"Admits itself."}""", minted.Reference, signer: Signer(minted));
        RecordEnvelope forged = estate.Add("work-order", estate.OperatorId, OrderBody, signer: Signer(minted));
        model = estate.Load();
        Assert.Contains(model.Findings, f => f.Code == "signature.self-admission" && f.Location.Contains(self.Id, StringComparison.Ordinal));
        Assert.Contains(model.Findings, f => f.Severity == Severity.Error && f.Location.Contains(forged.Id, StringComparison.Ordinal));

        // Admitted under the first device's signature, a later device signs like any admitted one.
        RecordEnvelope second = CreateDevice(estate, estate.OperatorId, "Second laptop");
        estate.Add("admission", estate.OperatorId, $$"""{"principal":"{{estate.OperatorId}}","status":"admitted","note":"Admitted by the first device."}""", second.Reference, signer: Signer(first));
        RecordEnvelope order = estate.Add("work-order", estate.OperatorId, OrderBody.Replace("W-1", "W-9", StringComparison.Ordinal), signer: Signer(second));
        model = estate.Load();
        Assert.DoesNotContain(model.Findings, f => f.Severity == Severity.Error && f.Location.Contains(order.Id, StringComparison.Ordinal));
        Assert.Single(model.Findings, f => f.Code == "signature.self-admission");
    }

    [Fact]
    public void AFormatTwoDeviceStatesItsPresenceAndPurposeAndClaimsOnlyWhatItsCustodyEnforces()
    {
        using var estate = new TestEstate(recordFormat: 2);
        CreateDevice(estate, estate.OperatorId, "Workstation", extra: """ ,"presence":"none","purpose":"signing" """);
        Assert.False(estate.Load().Findings.HasErrors, string.Join("\n", estate.Load().Findings));

        RecordEnvelope bare = CreateDevice(estate, estate.OperatorId, "Bare");
        RecordEnvelope fileClaims = CreateDevice(estate, estate.OperatorId, "File claims presence", extra: """ ,"presence":"required","purpose":"signing" """);
        RecordEnvelope agentClaims = CreateDevice(estate, estate.BuilderId, "Agent claims presence", custody: "macos-secure-enclave", extra: """ ,"presence":"required","purpose":"signing" """);
        RecordEnvelope agentRecovery = CreateDevice(estate, estate.BuilderId, "Agent recovery", extra: """ ,"presence":"none","purpose":"recovery" """);
        RecordEnvelope enclave = CreateDevice(estate, estate.OperatorId, "Enclave", custody: "macos-secure-enclave", extra: """ ,"presence":"required","purpose":"signing" """);
        List<Finding> findings = estate.Load().Findings.ToList();
        Assert.Contains(findings, f => f.Code == "body.missing" && f.Location == bare.ExpectedPath + "#body.presence");
        Assert.Contains(findings, f => f.Code == "device.presence" && f.Location == fileClaims.ExpectedPath);
        Assert.Contains(findings, f => f.Code == "device.presence" && f.Location == agentClaims.ExpectedPath);
        Assert.Contains(findings, f => f.Code == "device.purpose" && f.Location == agentRecovery.ExpectedPath);
        Assert.DoesNotContain(findings, f => f.Severity == Severity.Error && f.Location.StartsWith(enclave.ExpectedPath, StringComparison.Ordinal));

        // Format 1 knows no Secure Enclave and no presence.
        using var early = new TestEstate();
        RecordEnvelope old = CreateDevice(early, early.OperatorId, "Old", custody: "macos-secure-enclave");
        Assert.Contains(early.Load().Findings, f => f.Code == "body.enum" && f.Location == old.ExpectedPath + "#body.custody");
    }

    [Fact]
    public void TamperedForeignUnadmittedAndRevokedSignaturesAreRejected()
    {
        using var estate = new TestEstate();
        RecordEnvelope operatorDevice = CreateDevice(estate, estate.OperatorId, "Operator laptop");
        estate.Add("admission", estate.OperatorId, $$"""{"principal":"{{estate.OperatorId}}","status":"admitted","note":"Root."}""", operatorDevice.Reference, signer: Signer(operatorDevice));
        RecordEnvelope builderDevice = CreateDevice(estate, estate.BuilderId, "Build machine");

        // Unadmitted device signing an ordinary record.
        RecordEnvelope early = estate.Add("work-order", estate.BuilderId, OrderBody, signer: Signer(builderDevice));
        Assert.Contains(estate.Load().Findings, f => f.Code == "signature.unadmitted" && f.Location.Contains(early.Id, StringComparison.Ordinal));

        // A device of another principal.
        RecordEnvelope foreign = estate.Add("work-order", estate.BuilderId, OrderBody.Replace("W-1", "W-2", StringComparison.Ordinal), signer: Signer(operatorDevice));
        Assert.Contains(estate.Load().Findings, f => f.Code == "signature.principal" && f.Location.Contains(foreign.Id, StringComparison.Ordinal));

        // Admit, sign, then tamper with the signature bytes on disk.
        RecordEnvelope admission = estate.Add("admission", estate.OperatorId, $$"""{"principal":"{{estate.BuilderId}}","status":"admitted","note":"Builder."}""", builderDevice.Reference, signer: Signer(operatorDevice));
        RecordEnvelope signed = estate.Add("work-order", estate.BuilderId, OrderBody.Replace("W-1", "W-3", StringComparison.Ordinal), signer: Signer(builderDevice));
        string path = Path.Combine(estate.Root, signed.ExpectedPath.Replace('/', Path.DirectorySeparatorChar));
        string text = File.ReadAllText(path);
        string original = signed.Signatures[0].Signature;
        string flipped = (original[0] == 'A' ? "B" : "A") + original[1..];
        File.WriteAllText(path, text.Replace(original, flipped, StringComparison.Ordinal));
        Assert.Contains(estate.Load().Findings, f => f.Code == "signature.invalid" && f.Location.Contains(signed.Id, StringComparison.Ordinal));
        File.WriteAllText(path, text);
        Assert.DoesNotContain(estate.Load().Findings, f => f.Code == "signature.invalid");

        // Revocation: records signed from revokedAt on are invalid, earlier ones stay attributable.
        string revokedAt = RecordParser.FormatTimestamp(estate.Clock.AddSeconds(1));
        estate.Add("admission", estate.OperatorId, $$"""{"principal":"{{estate.BuilderId}}","status":"revoked","note":"Machine lost.","revokedAt":"{{revokedAt}}"}""", builderDevice.Reference, supersedes: admission.Id, signer: Signer(operatorDevice));
        RecordEnvelope late = estate.Add("work-order", estate.BuilderId, OrderBody.Replace("W-1", "W-4", StringComparison.Ordinal), signer: Signer(builderDevice));
        EstateModel model = estate.Load();
        Assert.Contains(model.Findings, f => f.Code == "signature.revoked" && f.Location.Contains(late.Id, StringComparison.Ordinal));
        Assert.DoesNotContain(model.Findings, f => f.Code == "signature.revoked" && f.Location.Contains(signed.Id, StringComparison.Ordinal));
    }

    [Fact]
    public void ADeviceRecordMustBeSignedByItsOwnKeyAndKeepItsKey()
    {
        using var estate = new TestEstate();
        string id = Ulid.NewUlid(estate.Clock.AddSeconds(1)).Value;
        string publicKey = RecordSigning.EncodePublicKey(store.Create(id));
        RecordEnvelope unsigned = estate.Add("device", estate.BuilderId, $$"""{"principal":"{{estate.BuilderId}}","name":"Unsigned","platform":"linux","custody":"file-owner-only","algorithm":"ecdsa-p256-sha256","publicKey":"{{publicKey}}","profiles":["embedded"]}""", id: id);
        Assert.Contains(estate.Load().Findings, f => f.Code == "device.unsigned" && f.Location.Contains(unsigned.Id, StringComparison.Ordinal));

        RecordEnvelope device = CreateDevice(estate, estate.BuilderId, "Proper");
        string otherKey = RecordSigning.EncodePublicKey(store.Create(Ulid.NewUlid().Value));
        RecordEnvelope renamed = estate.Add("device", estate.BuilderId, device.Body.GetRawText().Replace(RecordSigning.EncodePublicKey(store.PublicKey(device.Id)!), otherKey, StringComparison.Ordinal), supersedes: device.Id, signer: Signer(device));
        Assert.Contains(estate.Load().Findings, f => f.Code == "device.key-change" && f.Location.Contains(renamed.Id, StringComparison.Ordinal));
    }

    [Fact]
    public void AFormatTwoDeviceKeepsItsIdentityHistoryAndAdmissionAcrossARenameAndARotation()
    {
        using var estate = new TestEstate(recordFormat: 2);
        RecordEnvelope operatorDevice = CreateDevice(estate, estate.OperatorId, "Operator laptop", extra: FormatTwo);
        estate.Add("admission", estate.OperatorId, $$"""{"principal":"{{estate.OperatorId}}","status":"admitted","note":"Root."}""", operatorDevice.Reference, signer: Signer(operatorDevice));
        RecordEnvelope device = CreateDevice(estate, estate.BuilderId, "Build machine", extra: FormatTwo);
        estate.Add("admission", estate.OperatorId, $$"""{"principal":"{{estate.BuilderId}}","status":"admitted","note":"Builder."}""", device.Reference, signer: Signer(operatorDevice));
        RecordEnvelope before = estate.Add("work-order", estate.BuilderId, OrderBody, signer: Signer(device));

        // A rename is signed in the name of the device's identity, its first record, with the key it has.
        RecordEnvelope renamed = estate.Add("device", estate.BuilderId, device.Body.GetRawText().Replace("Build machine", "Build machine, renamed", StringComparison.Ordinal), supersedes: device.Id, signer: Signer(device));

        // A rotation is signed by the previous key and the new one, both in the identity's name; then the new key takes the device's place.
        string retired = Ulid.NewUlid().Value;
        File.Copy(KeyFile(device.Id), KeyFile(retired));
        RecordEnvelope rotated = Rotate(estate, renamed, device.Id);
        store.Promote(rotated.Id, device.Id);
        RecordEnvelope after = estate.Add("work-order", estate.BuilderId, OrderBody.Replace("W-1", "W-2", StringComparison.Ordinal), signer: Signer(device));
        EstateModel model = estate.Load();
        Assert.False(model.Findings.HasErrors, string.Join("\n", model.Findings));
        Assert.DoesNotContain(model.Findings, f => f.Code == "record.subject-stale");
        Assert.Equal(device.Id, SignatureChecks.IdentityOf(model, rotated.Id));
        Assert.True(SignatureChecks.IsAdmitted(model, device));
        Assert.Equal(SignatureChecks.AdmissionState.Admitted, SignatureChecks.AdmissionStateAt(model, rotated, after.At));
        Assert.Equal(2, rotated.Signatures.Count);
        Assert.All(rotated.Signatures, s => Assert.Equal(device.Id, s.Device));
        Assert.False(store.Has(rotated.Id));
        Assert.True(RecordSigning.Verify(EstateModel.BodyString(device, "publicKey")!, RecordSigning.Digest(before.ComputedHash), before.Signatures[0].Signature));
        Assert.True(RecordSigning.Verify(EstateModel.BodyString(rotated, "publicKey")!, RecordSigning.Digest(after.ComputedHash), after.Signatures[0].Signature));

        // The replaced key signs nothing after its rotation, and the finding names the revision whose key it was.
        RecordEnvelope late = estate.Add("work-order", estate.BuilderId, OrderBody.Replace("W-1", "W-3", StringComparison.Ordinal), signer: new StoreSigner(store, retired, device.Id));
        Assert.Contains(estate.Load().Findings, f => f.Code == "signature.key-time" && f.Location.Contains(late.Id, StringComparison.Ordinal) && f.Message.Contains(device.Id, StringComparison.Ordinal));

        // Times are whole seconds: a record dated in the rotation's own second cannot be ordered against it, and either key verifies.
        using JsonDocument same = JsonDocument.Parse(OrderBody.Replace("W-1", "W-4", StringComparison.Ordinal));
        AuthoredRecord sameSecond = RecordAuthor.Create(new RecordDraft("work-order", estate.BuilderId, same.RootElement, At: rotated.AtTime, Format: 2), new StoreSigner(store, retired, device.Id));
        RecordAuthor.Write(estate.Root, sameSecond);
        Assert.DoesNotContain(estate.Load().Findings, f => f.Severity == Severity.Error && f.Location.Contains(sameSecond.Envelope.Id, StringComparison.Ordinal));
    }

    [Fact]
    public void ARevisionIsSignedInTheIdentitysNameAndARotationByBothKeys()
    {
        // A revision signed in the name of its own id rather than the device's identity.
        using (var estate = new TestEstate(recordFormat: 2))
        {
            RecordEnvelope device = CreateDevice(estate, estate.BuilderId, "Build machine", extra: FormatTwo);
            string own = Ulid.NewUlid(estate.Clock.AddSeconds(1)).Value;
            RecordEnvelope misnamed = estate.Add("device", estate.BuilderId, device.Body.GetRawText().Replace("Build machine", "Misnamed", StringComparison.Ordinal), supersedes: device.Id, id: own, signer: new StoreSigner(store, device.Id, own));
            Assert.Contains(estate.Load().Findings, f => f.Code == "device.unsigned" && f.Location.Contains(misnamed.Id, StringComparison.Ordinal));
        }

        // A rotation signed by the new key alone, and one signed twice by the previous key: both lack the other key.
        using (var estate = new TestEstate(recordFormat: 2))
        {
            RecordEnvelope device = CreateDevice(estate, estate.BuilderId, "Build machine", extra: FormatTwo);
            RecordEnvelope newOnly = Rotate(estate, device, device.Id, revision => [new StoreSigner(store, revision, device.Id)]);
            List<Finding> findings = estate.Load().Findings.ToList();
            Assert.Contains(findings, f => f.Code == "device.rotation" && f.Location.Contains(newOnly.Id, StringComparison.Ordinal));
            Assert.Contains(findings, f => f.Code == "signature.rotation" && f.Location.Contains(newOnly.Id, StringComparison.Ordinal));
        }

        using (var estate = new TestEstate(recordFormat: 2))
        {
            RecordEnvelope device = CreateDevice(estate, estate.BuilderId, "Build machine", extra: FormatTwo);
            RecordEnvelope oldTwice = Rotate(estate, device, device.Id, _ => [Signer(device), Signer(device)]);
            List<Finding> findings = estate.Load().Findings.ToList();
            Assert.DoesNotContain(findings, f => f.Code == "device.rotation");
            Assert.Contains(findings, f => f.Code == "signature.rotation" && f.Location.Contains(oldTwice.Id, StringComparison.Ordinal));
        }

        // Custody changes only with the key.
        using (var estate = new TestEstate(recordFormat: 2))
        {
            RecordEnvelope device = CreateDevice(estate, estate.BuilderId, "Build machine", extra: FormatTwo);
            RecordEnvelope moved = estate.Add("device", estate.BuilderId, device.Body.GetRawText().Replace("file-owner-only", "macos-secure-enclave", StringComparison.Ordinal), supersedes: device.Id, signer: Signer(device));
            Assert.Contains(estate.Load().Findings, f => f.Code == "device.custody" && f.Location.Contains(moved.Id, StringComparison.Ordinal));
        }

        // A device recorded in format 1 is renamed in format 2: it has no presence or purpose yet, which read as none and signing.
        using (var estate = new TestEstate(recordFormat: 2))
        {
            RecordEnvelope device = CreateDevice(estate, estate.BuilderId, "Old machine", format: 1);
            string body = device.Body.GetRawText().Replace("Old machine", "Old machine, renamed", StringComparison.Ordinal);
            estate.Add("device", estate.BuilderId, body.Insert(body.Length - 1, FormatTwo), supersedes: device.Id, signer: Signer(device));
            Assert.False(estate.Load().Findings.HasErrors, string.Join("\n", estate.Load().Findings));

            // A revision keeps the purpose, read as signing where the earlier record states none: a recovery device is a new device.
            RecordEnvelope operatorDevice = CreateDevice(estate, estate.OperatorId, "Operator laptop", format: 1);
            string recovery = operatorDevice.Body.GetRawText();
            RecordEnvelope repurposed = estate.Add("device", estate.OperatorId, recovery.Insert(recovery.Length - 1, FormatTwo.Replace("signing", "recovery", StringComparison.Ordinal)), supersedes: operatorDevice.Id, signer: Signer(operatorDevice));
            Assert.Contains(estate.Load().Findings, f => f.Code == "device.identity" && f.Location.Contains(repurposed.Id, StringComparison.Ordinal));
        }
    }

    [Fact]
    public void ARevisionNeverHidesASelfAdmission()
    {
        using var estate = new TestEstate(recordFormat: 2);
        RecordEnvelope first = CreateDevice(estate, estate.OperatorId, "First laptop", extra: FormatTwo);
        RecordEnvelope root = estate.Add("admission", estate.OperatorId, $$"""{"principal":"{{estate.OperatorId}}","status":"admitted","note":"The first authority."}""", first.Reference, signer: Signer(first));

        // A device minted for the operator, renamed, then admitted by itself: once in the name of its identity, once in its revision's.
        RecordEnvelope minted = CreateDevice(estate, estate.OperatorId, "Minted", extra: FormatTwo);
        RecordEnvelope revised = estate.Add("device", estate.OperatorId, minted.Body.GetRawText().Replace("Minted", "Minted, renamed", StringComparison.Ordinal), supersedes: minted.Id, signer: Signer(minted));
        RecordEnvelope byIdentity = estate.Add("admission", estate.OperatorId, $$"""{"principal":"{{estate.OperatorId}}","status":"admitted","note":"Admits itself."}""", revised.Reference, signer: Signer(minted));
        RecordEnvelope byRevision = estate.Add("admission", estate.OperatorId, $$"""{"principal":"{{estate.OperatorId}}","status":"admitted","note":"Admits itself again."}""", revised.Reference, signer: new StoreSigner(store, minted.Id, revised.Id));
        RecordEnvelope forged = estate.Add("work-order", estate.OperatorId, OrderBody, signer: Signer(minted));
        EstateModel model = estate.Load();
        Assert.Equal(root.Id, SignatureChecks.FirstAuthority(model, estate.OperatorId)?.Id);
        Assert.Contains(model.Findings, f => f.Code == "signature.self-admission" && f.Location.Contains(byIdentity.Id, StringComparison.Ordinal));
        Assert.Contains(model.Findings, f => f.Code == "signature.self-admission" && f.Location.Contains(byRevision.Id, StringComparison.Ordinal));
        Assert.Contains(model.Findings, f => f.Code == "signature.unadmitted" && f.Location.Contains(forged.Id, StringComparison.Ordinal));
    }

    [Fact]
    public void TheOperatorsRecoveryDeviceSignsOnlyAdmissionsAndEachUseStaysInViewUntilReviewed()
    {
        using var estate = new TestEstate(recordFormat: 2);
        RecordEnvelope laptop = CreateDevice(estate, estate.OperatorId, "Operator laptop", extra: FormatTwo);
        RecordEnvelope laptopAdmission = estate.Add("admission", estate.OperatorId, $$"""{"principal":"{{estate.OperatorId}}","status":"admitted","note":"Root."}""", laptop.Reference, signer: Signer(laptop));
        RecordEnvelope recovery = CreateDevice(estate, estate.OperatorId, "Recovery key", custody: "offline", extra: Recovery);
        estate.Add("admission", estate.OperatorId, $$"""{"principal":"{{estate.OperatorId}}","status":"admitted","note":"The recovery device."}""", recovery.Reference, signer: Signer(laptop));
        List<Finding> findings = estate.Load().Findings.ToList();
        Assert.DoesNotContain(findings, f => f.Severity == Severity.Error);
        Assert.DoesNotContain(findings, f => f.Code == "recovery.unreviewed");

        // Every device is lost: the recovery key admits a replacement and revokes the lost laptop, and the replacement signs again.
        RecordEnvelope replacement = CreateDevice(estate, estate.OperatorId, "Replacement laptop", extra: FormatTwo);
        RecordEnvelope admitted = estate.Add("admission", estate.OperatorId, $$"""{"principal":"{{estate.OperatorId}}","status":"admitted","note":"Replaces the lost laptop."}""", replacement.Reference, signer: Signer(recovery));
        string revokedAt = RecordParser.FormatTimestamp(estate.Clock.AddSeconds(1));
        RecordEnvelope revoked = estate.Add("admission", estate.OperatorId, $$"""{"principal":"{{estate.OperatorId}}","status":"revoked","note":"Laptop lost.","revokedAt":"{{revokedAt}}"}""", laptop.Reference, supersedes: laptopAdmission.Id, signer: Signer(recovery));
        RecordEnvelope order = estate.Add("work-order", estate.OperatorId, OrderBody, signer: Signer(replacement));
        findings = estate.Load().Findings.ToList();
        Assert.DoesNotContain(findings, f => f.Severity == Severity.Error);
        Assert.Contains(findings, f => f.Code == "recovery.unreviewed" && f.Location == admitted.ExpectedPath && f.Message.Contains(recovery.Id, StringComparison.Ordinal));
        Assert.Contains(findings, f => f.Code == "recovery.unreviewed" && f.Location == revoked.ExpectedPath);
        Assert.DoesNotContain(findings, f => f.Location.StartsWith(order.ExpectedPath, StringComparison.Ordinal));

        // A recovery device signs nothing but device records, admissions and revocations.
        RecordEnvelope misuse = estate.Add("work-order", estate.OperatorId, OrderBody.Replace("W-1", "W-2", StringComparison.Ordinal), signer: Signer(recovery));
        Assert.Contains(estate.Load().Findings, f => f.Code == "signature.recovery-scope" && f.Location.Contains(misuse.Id, StringComparison.Ordinal));

        // The operator's review from the replacement takes a use out of view; a review the recovery device signed does not.
        const string Review = """{"independence":"self","outcome":"pass","summary":"The operator's own recovery, after the laptop was lost.","findings":[]}""";
        estate.Add("review", estate.OperatorId, Review, admitted.Reference, signer: Signer(replacement));
        estate.Add("review", estate.OperatorId, Review, revoked.Reference, signer: Signer(recovery));
        findings = estate.Load().Findings.ToList();
        Assert.DoesNotContain(findings, f => f.Code == "recovery.unreviewed" && f.Location == admitted.ExpectedPath);
        Assert.Contains(findings, f => f.Code == "recovery.unreviewed" && f.Location == revoked.ExpectedPath);
    }

    [Fact]
    public void ARecoveryUseLeavesViewOnlyByAReviewThatAnAdmittedDeviceOtherThanARecoveryDeviceSigns()
    {
        using var estate = new TestEstate(recordFormat: 2);
        RecordEnvelope laptop = CreateDevice(estate, estate.OperatorId, "Operator laptop", extra: FormatTwo);
        estate.Add("admission", estate.OperatorId, $$"""{"principal":"{{estate.OperatorId}}","status":"admitted","note":"Root."}""", laptop.Reference, signer: Signer(laptop));
        RecordEnvelope recovery = CreateDevice(estate, estate.OperatorId, "Recovery key", custody: "offline", extra: Recovery);
        estate.Add("admission", estate.OperatorId, $$"""{"principal":"{{estate.OperatorId}}","status":"admitted","note":"The recovery device."}""", recovery.Reference, signer: Signer(laptop));
        RecordEnvelope replacement = CreateDevice(estate, estate.OperatorId, "Replacement laptop", extra: FormatTwo);
        RecordEnvelope use = estate.Add("admission", estate.OperatorId, $$"""{"principal":"{{estate.OperatorId}}","status":"admitted","note":"Replaces the lost laptop."}""", replacement.Reference, signer: Signer(recovery));
        const string Review = """{"independence":"self","outcome":"pass","summary":"The operator's own recovery, after the laptop was lost.","findings":[]}""";

        // While the charter leaves signatures optional, anyone able to add records can write a review in the operator's name
        // without a signature: it is accepted as a record, and it clears nothing.
        estate.Add("review", estate.OperatorId, Review, use.Reference);
        List<Finding> findings = estate.Load().Findings.ToList();
        Assert.DoesNotContain(findings, f => f.Severity == Severity.Error);
        Assert.Contains(findings, f => f.Code == "recovery.unreviewed" && f.Location == use.ExpectedPath);

        // Nor does a review signed by a device of the operator whose admission does not count: an unsigned one, at format 2.
        RecordEnvelope minted = CreateDevice(estate, estate.OperatorId, "Minted", extra: FormatTwo);
        estate.Add("admission", estate.OperatorId, $$"""{"principal":"{{estate.OperatorId}}","status":"admitted","note":"Unsigned."}""", minted.Reference);
        estate.Add("review", estate.OperatorId, Review, use.Reference, signer: Signer(minted));
        Assert.Contains(estate.Load().Findings, f => f.Code == "recovery.unreviewed" && f.Location == use.ExpectedPath);

        // The operator's review signed by the admitted replacement takes the use out of view.
        estate.Add("review", estate.OperatorId, Review, use.Reference, signer: Signer(replacement));
        Assert.DoesNotContain(estate.Load().Findings, f => f.Code == "recovery.unreviewed");
    }

    [Fact]
    public void OnlyARecoveryDevicesKeyIsOfflineAndARecoveryDeviceIsNeverTheFirstAuthority()
    {
        using var estate = new TestEstate(recordFormat: 2);
        RecordEnvelope offlineSigner = CreateDevice(estate, estate.OperatorId, "Offline signer", custody: "offline", extra: FormatTwo);
        RecordEnvelope fileRecovery = CreateDevice(estate, estate.OperatorId, "Recovery in a file", extra: Recovery);
        List<Finding> findings = estate.Load().Findings.ToList();
        Assert.Contains(findings, f => f.Code == "device.recovery" && f.Location == offlineSigner.ExpectedPath);
        Assert.Contains(findings, f => f.Code == "device.recovery" && f.Location == fileRecovery.ExpectedPath);

        // An admitted device of the operator makes and admits a recovery device; one that admits itself first is no authority.
        using var fresh = new TestEstate(recordFormat: 2);
        RecordEnvelope only = CreateDevice(fresh, fresh.OperatorId, "Recovery first", custody: "offline", extra: Recovery);
        RecordEnvelope self = fresh.Add("admission", fresh.OperatorId, $$"""{"principal":"{{fresh.OperatorId}}","status":"admitted","note":"Admits itself."}""", only.Reference, signer: Signer(only));
        EstateModel model = fresh.Load();
        Assert.Null(SignatureChecks.FirstAuthority(model, fresh.OperatorId));
        Assert.Contains(model.Findings, f => f.Code == "signature.self-admission" && f.Location.Contains(self.Id, StringComparison.Ordinal));
    }

    [Fact]
    public void TheFileStoreHoldsOwnerOnlyKeysAndSignsDeterministicallyVerifiableSignatures()
    {
        string id = Ulid.NewUlid().Value;
        byte[] publicKey = store.Create(id);
        Assert.True(store.Has(id));
        Assert.Equal("file-owner-only", store.Kind);
        if (!OperatingSystem.IsWindows())
        {
            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(Path.Combine(home, "keys", id + ".p8")));
        }

        byte[] digest = RecordSigning.Digest("sha256:" + new string('1', 64));
        byte[] signature = store.Sign(id, digest);
        Assert.Equal(RecordSigning.SignatureBytes, signature.Length);
        Assert.True(RecordSigning.Verify(RecordSigning.EncodePublicKey(publicKey), digest, System.Buffers.Text.Base64Url.EncodeToString(signature)));
        Assert.False(RecordSigning.Verify(RecordSigning.EncodePublicKey(publicKey), RecordSigning.Digest("sha256:" + new string('2', 64)), System.Buffers.Text.Base64Url.EncodeToString(signature)));
        Assert.Throws<InvalidOperationException>(() => store.Create(id));
        Assert.Throws<InvalidOperationException>(() => store.Promote(Ulid.NewUlid().Value, id));
        store.Remove(id);
        Assert.False(store.Has(id));

        // Removing a key that was never held is nothing, even where the keys directory does not exist yet.
        new FileKeyStore(Path.Combine(home, "never")).Remove(Ulid.NewUlid().Value);
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        try
        {
            Directory.Delete(home, recursive: true);
        }
        catch (IOException)
        {
            // Best effort.
        }
    }

    [Fact]
    public void InRecordFormatTwoAnUnsignedAdmissionAdmitsAndRevokesNothing()
    {
        using var estate = new TestEstate(recordFormat: 2);
        RecordEnvelope first = CreateDevice(estate, estate.OperatorId, "First laptop", extra: FormatTwo);
        RecordEnvelope root = estate.Add("admission", estate.OperatorId, $$"""{"principal":"{{estate.OperatorId}}","status":"admitted","note":"The first authority."}""", first.Reference, signer: Signer(first));
        Assert.False(estate.Load().Findings.HasErrors, string.Join("\n", estate.Load().Findings));

        // While the charter leaves signatures optional, anyone able to add records could write an unsigned admission of a device they
        // made for the operator; in format 2 it admits nothing, and what that device signs is refused.
        RecordEnvelope minted = CreateDevice(estate, estate.OperatorId, "Minted", extra: FormatTwo);
        RecordEnvelope unsigned = estate.Add("admission", estate.OperatorId, $$"""{"principal":"{{estate.OperatorId}}","status":"admitted","note":"Unsigned."}""", minted.Reference);
        RecordEnvelope forged = estate.Add("work-order", estate.OperatorId, OrderBody, signer: Signer(minted));
        EstateModel model = estate.Load();
        Assert.Contains(model.Findings, f => f.Code == "signature.admission-unsigned" && f.Location == unsigned.ExpectedPath);
        Assert.False(SignatureChecks.IsAdmitted(model, minted));
        Assert.Contains(model.Findings, f => f.Severity == Severity.Error && f.Location.Contains(forged.Id, StringComparison.Ordinal));

        // Nor does an unsigned revocation end the first device's admission: it keeps signing.
        string revokedAt = RecordParser.FormatTimestamp(estate.Clock.AddSeconds(1));
        RecordEnvelope revocation = estate.Add("admission", estate.OperatorId, $$"""{"principal":"{{estate.OperatorId}}","status":"revoked","note":"Unsigned.","revokedAt":"{{revokedAt}}"}""", first.Reference, supersedes: root.Id);
        RecordEnvelope later = estate.Add("work-order", estate.OperatorId, OrderBody.Replace("W-1", "W-2", StringComparison.Ordinal), signer: Signer(first));
        model = estate.Load();
        Assert.Contains(model.Findings, f => f.Code == "signature.admission-unsigned" && f.Location == revocation.ExpectedPath);
        Assert.True(SignatureChecks.IsAdmitted(model, first));
        Assert.DoesNotContain(model.Findings, f => f.Severity == Severity.Error && f.Location.Contains(later.Id, StringComparison.Ordinal));
    }

    [Fact]
    public void InAnEstateAtFormatTwoAnUnsignedAdmissionCountsForNothingWhateverFormatOrDateItStates()
    {
        using var estate = new TestEstate(recordFormat: 2);
        RecordEnvelope first = CreateDevice(estate, estate.OperatorId, "First laptop", extra: FormatTwo);
        estate.Add("admission", estate.OperatorId, $$"""{"principal":"{{estate.OperatorId}}","status":"admitted","note":"The first authority."}""", first.Reference, signer: Signer(first));
        DateTimeOffset early = estate.Clock;
        estate.Add("work-order", estate.OperatorId, OrderBody, signer: Signer(first));

        // The author of a record writes its format and its date: an unsigned admission written in format 1 and dated before the
        // records already there admits nothing in an estate created at format 2, which anchors no unsigned admission.
        estate.SetClock(early);
        RecordEnvelope minted = CreateDevice(estate, estate.OperatorId, "Minted", format: 1);
        RecordEnvelope unsigned = estate.Add("admission", estate.OperatorId, $$"""{"principal":"{{estate.OperatorId}}","status":"admitted","note":"Unsigned, in format 1."}""", minted.Reference, format: 1);
        RecordEnvelope forged = estate.Add("work-order", estate.OperatorId, OrderBody.Replace("W-1", "W-2", StringComparison.Ordinal), signer: Signer(minted), format: 1);
        EstateModel model = estate.Load();
        Assert.Contains(model.Findings, f => f.Code == "signature.admission-unsigned" && f.Location == unsigned.ExpectedPath);
        Assert.False(SignatureChecks.IsAdmitted(model, minted));
        Assert.Contains(model.Findings, f => f.Severity == Severity.Error && f.Location.Contains(forged.Id, StringComparison.Ordinal));
        Assert.True(SignatureChecks.IsAdmitted(model, first));
    }

    [Fact]
    public void TheOperatorsSignedMoveToFormatTwoAnchorsTheUnsignedAdmissionsInForceAndNothingElse()
    {
        using var estate = new TestEstate();
        RecordEnvelope laptop = CreateDevice(estate, estate.OperatorId, "Operator laptop");
        estate.Add("admission", estate.OperatorId, $$"""{"principal":"{{estate.OperatorId}}","status":"admitted","note":"The first authority."}""", laptop.Reference, signer: Signer(laptop));
        RecordEnvelope builderDevice = CreateDevice(estate, estate.BuilderId, "Build machine");
        RecordEnvelope unsigned = estate.Add("admission", estate.OperatorId, $$"""{"principal":"{{estate.BuilderId}}","status":"admitted","note":"Admitted unsigned under format 1."}""", builderDevice.Reference);
        estate.Add("work-order", estate.BuilderId, OrderBody, signer: Signer(builderDevice));
        Assert.False(estate.Load().Findings.HasErrors, string.Join("\n", estate.Load().Findings));
        DateTimeOffset beforeMove = estate.Clock;

        // Moved to format 2 with no anchor, the builder's unsigned admission stops counting, and so do the builder's signatures.
        estate.MoveTo(2);
        EstateModel model = estate.Load();
        Assert.Contains(model.Findings, f => f.Code == "signature.admission-unsigned" && f.Location == unsigned.ExpectedPath);
        Assert.False(SignatureChecks.IsAdmitted(model, builderDevice));

        // The operator's signed migration anchors it, by id and hash, and the estate is whole again.
        string body = $$"""{"fromPin":"release 0.4.2","toPin":"release 0.5.2","fromFormat":1,"toFormat":2,"migrations":["record-format-2"],"changes":[],"anchored":[{"id":"{{unsigned.Id}}","hash":"{{unsigned.ComputedHash}}"}]}""";
        estate.Add("migration", estate.OperatorId, body, signer: Signer(laptop));
        model = estate.Load();
        Assert.False(model.Findings.HasErrors, string.Join("\n", model.Findings));
        Assert.True(SignatureChecks.IsAdmitted(model, builderDevice));

        // An unsigned admission written after the move counts for nothing, though it states format 1 and a date before the move.
        estate.SetClock(beforeMove);
        RecordEnvelope minted = CreateDevice(estate, estate.OperatorId, "Minted", format: 1);
        RecordEnvelope backdated = estate.Add("admission", estate.OperatorId, $$"""{"principal":"{{estate.OperatorId}}","status":"admitted","note":"Backdated."}""", minted.Reference, format: 1);
        model = estate.Load();
        Assert.Contains(model.Findings, f => f.Code == "signature.admission-unsigned" && f.Location == backdated.ExpectedPath);
        Assert.False(SignatureChecks.IsAdmitted(model, minted));

        // Nor can another principal's migration anchor it: the list is the operator's or an administrator's.
        string builderBody = $$"""{"fromPin":"release 0.4.2","toPin":"release 0.5.2","fromFormat":1,"toFormat":2,"migrations":["record-format-2"],"changes":[],"anchored":[{"id":"{{backdated.Id}}","hash":"{{backdated.ComputedHash}}"}]}""";
        estate.Add("migration", estate.BuilderId, builderBody, signer: Signer(builderDevice));
        model = estate.Load();
        Assert.Contains(model.Findings, f => f.Code == "migration.anchor-author");
        Assert.False(SignatureChecks.IsAdmitted(model, minted));
    }

    [Fact]
    public void AnotherOperatorsListOfAnchoredAdmissionsIsRefusedUnlessTheyMayAdminister()
    {
        using var estate = new TestEstate();
        RecordEnvelope laptop = CreateDevice(estate, estate.OperatorId, "Operator laptop");
        estate.Add("admission", estate.OperatorId, $$"""{"principal":"{{estate.OperatorId}}","status":"admitted","note":"The first authority."}""", laptop.Reference, signer: Signer(laptop));
        RecordEnvelope second = estate.Add("principal", estate.OperatorId, """{"kind":"human","name":"Second Operator","roles":["operator"]}""", signer: Signer(laptop));
        RecordEnvelope secondLaptop = CreateDevice(estate, second.Id, "Second operator's laptop");
        estate.Add("admission", estate.OperatorId, $$"""{"principal":"{{second.Id}}","status":"admitted","note":"The second operator's laptop."}""", secondLaptop.Reference, signer: Signer(laptop));
        RecordEnvelope builderDevice = CreateDevice(estate, estate.BuilderId, "Build machine");
        RecordEnvelope unsigned = estate.Add("admission", estate.OperatorId, $$"""{"principal":"{{estate.BuilderId}}","status":"admitted","note":"Admitted unsigned under format 1."}""", builderDevice.Reference);
        RecordEnvelope decision = estate.Add("decision", estate.OperatorId, $$"""{"title":"Administration","statement":"The second operator administers the estate.","summary":"Delegation.","synthesis":"The second operator may record principals, authority and admissions.","effects":["second operator administers"],"affects":[],"decidedBy":"{{estate.OperatorId}}","decidedAt":"2026-10-13T12:00:00Z"}""");
        Assert.False(estate.Load().Findings.HasErrors, string.Join("\n", estate.Load().Findings));

        // Anchors count only for the root operator or an administrator: the operator role alone writes a list that would anchor
        // nothing, so it is refused rather than accepted and ignored.
        estate.MoveTo(2);
        string body = $$"""{"fromPin":"release 0.4.2","toPin":"release 0.5.2","fromFormat":1,"toFormat":2,"migrations":["record-format-2"],"changes":[],"anchored":[{"id":"{{unsigned.Id}}","hash":"{{unsigned.ComputedHash}}"}]}""";
        RecordEnvelope migration = estate.Add("migration", second.Id, body, signer: Signer(secondLaptop));
        EstateModel model = estate.Load();
        Assert.Contains(model.Findings, f => f.Code == "migration.anchor-author" && f.Location.StartsWith(migration.ExpectedPath, StringComparison.Ordinal));
        Assert.False(SignatureChecks.IsAdmitted(model, builderDevice));

        // Granted 'administer', the same principal's signed list is valid and is the one that keeps the admission counting.
        estate.Add("authority", estate.OperatorId, $$"""{"grantee":"{{second.Id}}","scope":["administer"],"reserved":[],"conditions":[],"basis":"{{decision.Id}}"}""", signer: Signer(laptop));
        model = estate.Load();
        Assert.False(model.Findings.HasErrors, string.Join("\n", model.Findings));
        Assert.True(SignatureChecks.IsAdmitted(model, builderDevice));
    }

    [Fact]
    public void InRecordFormatOneAnUnsignedAdmissionStillCountsWhileSignaturesAreOptional()
    {
        // Format 1 keeps its own rule: records written under it do not change meaning when a later release reads them.
        using var estate = new TestEstate();
        RecordEnvelope builderDevice = CreateDevice(estate, estate.BuilderId, "Build machine");
        RecordEnvelope admission = estate.Add("admission", estate.OperatorId, $$"""{"principal":"{{estate.BuilderId}}","status":"admitted","note":"Unsigned, under format 1."}""", builderDevice.Reference);
        estate.Add("work-order", estate.BuilderId, OrderBody, signer: Signer(builderDevice));
        EstateModel model = estate.Load();
        Assert.Empty(admission.Signatures);
        Assert.False(model.Findings.HasErrors, string.Join("\n", model.Findings));
        Assert.True(SignatureChecks.IsAdmitted(model, builderDevice));
    }

    private RecordEnvelope CreateDevice(TestEstate estate, string principal, string name, string custody = "file-owner-only", string extra = "", long? format = null)
    {
        string id = Ulid.NewUlid(estate.Clock.AddSeconds(1)).Value;
        string publicKey = RecordSigning.EncodePublicKey(store.Create(id));
        return estate.Add("device", principal, $$"""{"principal":"{{principal}}","name":"{{name}}","platform":"linux","custody":"{{custody}}","algorithm":"ecdsa-p256-sha256","publicKey":"{{publicKey}}","profiles":["embedded"]{{extra}}}""", id: id, signer: new StoreSigner(store, id), format: format);
    }

    /// <summary>A device's next revision with a new key made under the revision's id, signed by the given signers (the previous and the new key unless a test names others).</summary>
    private RecordEnvelope Rotate(TestEstate estate, RecordEnvelope latest, string identity, Func<string, IReadOnlyList<IRecordSigner>>? signers = null)
    {
        string revision = Ulid.NewUlid(estate.Clock.AddSeconds(1)).Value;
        string newKey = RecordSigning.EncodePublicKey(store.Create(revision));
        string body = latest.Body.GetRawText().Replace(EstateModel.BodyString(latest, "publicKey")!, newKey, StringComparison.Ordinal);
        return estate.Add("device", EstateModel.BodyString(latest, "principal")!, body, supersedes: latest.Id, id: revision, signers: signers?.Invoke(revision) ?? [new StoreSigner(store, identity), new StoreSigner(store, revision, identity)]);
    }

    private string KeyFile(string id) => Path.Combine(home, "keys", id + ".p8");

    private StoreSigner Signer(RecordEnvelope device) => new(store, device.Id);
}
