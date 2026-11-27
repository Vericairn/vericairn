// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using Vericairn.Protocol;
using Vericairn.Records.Authoring;
using Vericairn.Records.Estate;
using Vericairn.Records.Hashing;
using Vericairn.Records.Identity;
using Vericairn.Records.Model;
using Vericairn.Records.Signing;
using Vericairn.Records.Tests;
using Vericairn.Runtime.Service;

namespace Vericairn.Runtime.Tests;

public sealed partial class DeviceSigningTests
{
    [Fact]
    public void PairingAloneCanWriteAndroidEvidenceAndReadersReverifyImportedEvidence()
    {
        using var estate = new TestEstate(3);
        using EstateService service = EstateService.Open(estate.Root);
        PairingAuthority authority = Authority(estate, service);
        using ECDsa key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        string id = Ulid.NewUlid().Value;
        JsonObject deviceBody = DeviceRecords.Body(estate.OperatorId, "Phone", "android", DeviceKeyStores.AndroidKeystoreKind,
            RecordSigning.EncodePublicKey(key.ExportSubjectPublicKeyInfo()), Profiles, 3, "required");
        deviceBody["presenceVerification"] = DeviceRecords.AndroidKeyAttestation;
        estate.SetClock(DateTimeOffset.UtcNow.AddSeconds(-4));
        RecordEnvelope device = estate.Add("device", estate.OperatorId, deviceBody.ToJsonString(), id: id, signer: new FixtureSigner(id, key));

        byte[] invalidCertificate = [1, 2, 3];
        var evidence = new AndroidAttestationEvidence([Base64Url.EncodeToString(invalidCertificate)], ContentHash.Sha256(invalidCertificate),
            PairingProtocol.NewNonce(), "trusted-environment", "trusted-environment", 3, true, "verified", AndroidAttestationEvidence.RemotePackage,
            1, [ContentHash.Sha256([4, 5, 6])], 140000, 202609, null, null);
        var body = new JsonObject
        {
            ["principal"] = estate.OperatorId, ["status"] = "admitted", ["note"] = "Imported admission.",
            [AndroidAttestationEvidence.Field] = JsonNode.Parse(evidence.ToElement().GetRawText()),
        };
        using JsonDocument document = JsonDocument.Parse(body.ToJsonString());
        var request = new NewRecordRequest("admission", estate.OperatorId, document.RootElement, $"device:{id}:{device.ComputedHash}");
        Assert.Contains("only by a verified pairing", service.NewRecord(request).Problem?.Message, StringComparison.Ordinal);
        string requesterKey = EstateModel.BodyString(estate.Load().ById[authority.Device], "publicKey")!;
        using (service.ForDeviceRequest(authority.Device, requesterKey, null))
            Assert.Contains("only by a verified pairing", service.PrepareRecord(request).Problem?.Message, StringComparison.Ordinal);

        var signer = new StoreSigner(DeviceKeyStores.Named("file"), authority.Device);
        estate.SetClock(DateTimeOffset.UtcNow.AddSeconds(1));
        RecordEnvelope imported = estate.Add("admission", estate.OperatorId, body.ToJsonString(), device.Reference, signer: signer);
        EstateModel model = estate.Load();
        Assert.Empty(model.Findings);
        Assert.True(SignatureChecks.Counts(model, imported));
        Assert.Equal("declared", Assert.Single(service.Devices(), row => row.Id == id).Presence);
    }

    [Theory]
    [InlineData("android", DeviceKeyStores.AndroidKeystoreKind, true)]
    [InlineData("android", DeviceKeyStores.AndroidKeystoreKind, false)]
    [InlineData("ios", DeviceKeyStores.IosSecureEnclaveKind, false)]
    public void APhoneWithoutAttestationCannotSignForAnotherDevice(string platform, string custody, bool forgedNote)
    {
        using var estate = new TestEstate(3);
        using EstateService service = EstateService.Open(estate.Root);
        PairingAuthority authority = Authority(estate, service);
        using ECDsa key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        string encodedKey = RecordSigning.EncodePublicKey(key.ExportSubjectPublicKeyInfo());
        string id = Ulid.NewUlid().Value;
        JsonObject body = DeviceRecords.Body(estate.OperatorId, "Phone", platform, custody, encodedKey, Profiles, 3, "required");
        if (forgedNote) body["presenceVerification"] = DeviceRecords.AndroidKeyAttestation;
        estate.SetClock(DateTimeOffset.UtcNow.AddSeconds(-4));
        RecordEnvelope device = estate.Add("device", estate.OperatorId, body.ToJsonString(), id: id, signer: new FixtureSigner(id, key));
        Assert.Equal("declared", Assert.Single(service.Devices(), row => row.Id == id).Presence);

        string note = forgedNote ? "Jo's phone. The runtime verified Android hardware key attestation for this device record."
            : "Paired over the local network with a pairing code for this principal.";
        WriteOutcome admitted = service.DeviceAdmit(new(id, estate.OperatorId, note));
        Assert.True(admitted.Ok, admitted.Problem?.Message);

        string requesterKey = EstateModel.BodyString(estate.Load().ById[authority.Device], "publicKey")!;
        using JsonDocument draft = JsonDocument.Parse(ResultOutputSchemeTests.OrderBody);
        using (service.ForDeviceRequest(authority.Device, requesterKey, null))
        {
            RecordPrepareResponse prepared = service.PrepareRecord(new("work-order", estate.OperatorId, draft.RootElement.Clone()));
            Assert.True(prepared.Ok, prepared.Problem?.Message);
            var request = new SignedRecordRequest(prepared.Prepared!.Plan,
                Base64Url.EncodeToString(key.SignHash(RecordSigning.Digest(prepared.Prepared.Hash)))) { Device = device.Id };
            WriteOutcome denied = service.NewSignedRecord(request);
            Assert.False(denied.Ok);
            Assert.Contains("no verified per-signature presence", denied.Problem?.Message, StringComparison.Ordinal);
        }

        Assert.Equal("declared", Assert.Single(service.Devices(), row => row.Id == id).Presence);
    }

    [Fact]
    public void APhoneIdentityStaysDeclaredWhenItsRotationClaimsDesktopCustody()
    {
        using var estate = new TestEstate(3);
        using EstateService service = EstateService.Open(estate.Root);
        _ = Authority(estate, service);
        using ECDsa firstKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        string id = Ulid.NewUlid().Value;
        JsonObject body = DeviceRecords.Body(estate.OperatorId, "Phone", "ios", DeviceKeyStores.IosSecureEnclaveKind,
            RecordSigning.EncodePublicKey(firstKey.ExportSubjectPublicKeyInfo()), Profiles, 3, "required");
        estate.SetClock(DateTimeOffset.UtcNow.AddSeconds(-4));
        RecordEnvelope device = estate.Add("device", estate.OperatorId, body.ToJsonString(), id: id, signer: new FixtureSigner(id, firstKey));
        WriteOutcome admitted = service.DeviceAdmit(new(id, estate.OperatorId, "Paired over the local network with a pairing code for this principal."));
        Assert.True(admitted.Ok, admitted.Problem?.Message);

        using ECDsa rotatedKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        body["platform"] = "macos";
        body["custody"] = "macos-secure-enclave";
        body["publicKey"] = RecordSigning.EncodePublicKey(rotatedKey.ExportSubjectPublicKeyInfo());
        estate.SetClock(DateTimeOffset.UtcNow);
        _ = estate.Add("device", estate.OperatorId, body.ToJsonString(), supersedes: device.Id,
            signers: [new FixtureSigner(id, firstKey), new FixtureSigner(id, rotatedKey)]);
        Assert.Empty(estate.Load().Findings);
        Assert.Equal("declared", Assert.Single(service.Devices(), row => row.Id == id).Presence);
    }
}
