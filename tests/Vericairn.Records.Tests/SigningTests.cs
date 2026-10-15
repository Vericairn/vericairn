// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using Vericairn.Records.Estate;
using Vericairn.Records.Identity;
using Vericairn.Records.Model;
using Vericairn.Records.Signing;

namespace Vericairn.Records.Tests;

public class SigningTests : IDisposable
{
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
        store.Remove(id);
        Assert.False(store.Has(id));
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

    private RecordEnvelope CreateDevice(TestEstate estate, string principal, string name, string custody = "file-owner-only", string extra = "")
    {
        string id = Ulid.NewUlid(estate.Clock.AddSeconds(1)).Value;
        string publicKey = RecordSigning.EncodePublicKey(store.Create(id));
        return estate.Add("device", principal, $$"""{"principal":"{{principal}}","name":"{{name}}","platform":"linux","custody":"{{custody}}","algorithm":"ecdsa-p256-sha256","publicKey":"{{publicKey}}","profiles":["embedded"]{{extra}}}""", id: id, signer: new StoreSigner(store, id));
    }

    private StoreSigner Signer(RecordEnvelope device) => new(store, device.Id);
}
