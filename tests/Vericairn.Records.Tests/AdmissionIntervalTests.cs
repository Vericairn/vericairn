// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jayson Furr

using Vericairn.Records.Estate;
using Vericairn.Records.Identity;
using Vericairn.Records.Model;
using Vericairn.Records.Signing;

namespace Vericairn.Records.Tests;

/// <summary>A device is admitted from its first admission, revoked from revokedAt, admitted again by a later admission; a later re-admission never invalidates earlier signed records.</summary>
public class AdmissionIntervalTests : IDisposable
{
    private const string OrderBody = """{"key":"W-1","kind":"ordinary","title":"Signed","intent":"Intervals.","scope":["x"],"exitQuestion":"?","inputs":[],"deliverables":["y"]}""";
    private readonly string home = Path.Combine(Path.GetTempPath(), "vericairn-intervals-" + Ulid.NewUlid().Value);
    private readonly FileKeyStore store;

    public AdmissionIntervalTests()
    {
        store = new FileKeyStore(home);
    }

    [Fact]
    public void ReAdmissionAfterARevocationDoesNotInvalidateEarlierRecords()
    {
        using var estate = new TestEstate();
        RecordEnvelope operatorDevice = Device(estate, estate.OperatorId, "Operator laptop");
        estate.Add("admission", estate.OperatorId, $$"""{"principal":"{{estate.OperatorId}}","status":"admitted","note":"Root."}""", operatorDevice.Reference, signer: new StoreSigner(store, operatorDevice.Id));
        RecordEnvelope builderDevice = Device(estate, estate.BuilderId, "Build machine");
        RecordEnvelope admission = estate.Add("admission", estate.OperatorId, $$"""{"principal":"{{estate.BuilderId}}","status":"admitted","note":"First."}""", builderDevice.Reference, signer: new StoreSigner(store, operatorDevice.Id));
        RecordEnvelope early = estate.Add("work-order", estate.BuilderId, OrderBody, signer: new StoreSigner(store, builderDevice.Id));

        string revokedAt = RecordParser.FormatTimestamp(estate.Clock.AddSeconds(1));
        RecordEnvelope revocation = estate.Add("admission", estate.OperatorId, $$"""{"principal":"{{estate.BuilderId}}","status":"revoked","note":"Lost.","revokedAt":"{{revokedAt}}"}""", builderDevice.Reference, supersedes: admission.Id, signer: new StoreSigner(store, operatorDevice.Id));
        RecordEnvelope duringRevocation = estate.Add("work-order", estate.BuilderId, OrderBody.Replace("W-1", "W-2", StringComparison.Ordinal), signer: new StoreSigner(store, builderDevice.Id));
        estate.Add("admission", estate.OperatorId, $$"""{"principal":"{{estate.BuilderId}}","status":"admitted","note":"Found again."}""", builderDevice.Reference, supersedes: revocation.Id, signer: new StoreSigner(store, operatorDevice.Id));
        RecordEnvelope afterReadmission = estate.Add("work-order", estate.BuilderId, OrderBody.Replace("W-1", "W-3", StringComparison.Ordinal), signer: new StoreSigner(store, builderDevice.Id));

        EstateModel model = estate.Load();
        Assert.DoesNotContain(model.Findings, f => f.Severity == Severity.Error && f.Location.Contains(early.Id, StringComparison.Ordinal));
        Assert.Contains(model.Findings, f => f.Code == "signature.revoked" && f.Location.Contains(duringRevocation.Id, StringComparison.Ordinal));
        Assert.DoesNotContain(model.Findings, f => f.Severity == Severity.Error && f.Location.Contains(afterReadmission.Id, StringComparison.Ordinal));
        Assert.Equal(SignatureChecks.AdmissionState.Admitted, SignatureChecks.AdmissionStateAt(model, builderDevice, afterReadmission.At));
        Assert.Equal(SignatureChecks.AdmissionState.Revoked, SignatureChecks.AdmissionStateAt(model, builderDevice, duringRevocation.At));
        Assert.Equal(SignatureChecks.AdmissionState.NotYetAdmitted, SignatureChecks.AdmissionStateAt(model, builderDevice, "2000-01-01T00:00:00Z"));
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

    private RecordEnvelope Device(TestEstate estate, string principal, string name)
    {
        string id = Ulid.NewUlid(estate.Clock.AddSeconds(1)).Value;
        string publicKey = RecordSigning.EncodePublicKey(store.Create(id));
        return estate.Add("device", principal, $$"""{"principal":"{{principal}}","name":"{{name}}","platform":"linux","custody":"file-owner-only","algorithm":"ecdsa-p256-sha256","publicKey":"{{publicKey}}","profiles":["embedded"]}""", id: id, signer: new StoreSigner(store, id));
    }
}
