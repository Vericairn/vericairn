// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Buffers.Text;
using System.Globalization;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
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
    private static readonly string[] Profiles = ["local-network"];

    [Fact]
    public void RecordedAttestationFixturesDistinguishPerUseFromTimedAuthentication()
    {
        SyntheticAndroidAttestation.RequireNativeTrust();
        string directory = Path.Combine(AppContext.BaseDirectory, "AndroidAttestation");
        using X509Certificate2 root = X509CertificateLoader.LoadCertificateFromFile(Path.Combine(directory, "root.der"));
        var verifier = new AndroidKeyAttestation(new X509Certificate2Collection(root), new FixtureClock(), () => new HashSet<string>());
        string nonce = Base64Url.EncodeToString(Enumerable.Range(0, 32).Select(value => (byte)value).ToArray());
        foreach ((string fixture, bool expected) in new[] { ("per-use.der", true), ("timed.der", false) })
        {
            using X509Certificate2 leaf = X509CertificateLoader.LoadCertificateFromFile(Path.Combine(directory, fixture));
            using ECDsa key = leaf.GetECDsaPublicKey()!;
            Assert.Equal(expected, verifier.Verify([Base64Url.EncodeToString(leaf.RawData), Base64Url.EncodeToString(root.RawData)], nonce, Base64Url.EncodeToString(key.ExportSubjectPublicKeyInfo())) is not null);
        }
    }

    [Fact]
    public void AndroidRecordsRequiredAndItsMethodOnlyAfterVerification()
    {
        SyntheticAndroidAttestation.RequireNativeTrust();
        using var estate = new TestEstate(3);
        using EstateService service = EstateService.Open(estate.Root);
        PairingAuthority authority = Authority(estate, service);
        using var evidence = new SyntheticAndroidAttestation();
        service.PairingAttestation = evidence.Verifier();
        var plan = service.PairingPlan(estate.OperatorId, presence: true);
        using ECDsa requestKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using ECDsa presenceKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        PairingDevice request = Device(requestKey, "android", DeviceKeyStores.AndroidKeystoreKind, "none");
        PairingDevice second = Device(presenceKey, "android", DeviceKeyStores.AndroidKeystoreKind, "required") with
        {
            AndroidAttestation = evidence.Issue(presenceKey.ExportSubjectPublicKeyInfo(), Base64Url.DecodeFromChars(plan.RuntimeNonce)),
        };
        var result = service.PairDevice(plan, Profiles, request, Sign(plan, plan.Record, request, requestKey), authority, "Test pairing.", second, Sign(plan, plan.PresenceRecord!, second, presenceKey));
        Assert.True(result.Device.Ok, result.Device.Problem?.Message);
        Assert.True(result.PresenceDevice?.Ok, result.PresenceDevice?.Problem?.Message);
        RecordEnvelope record = estate.Load().ById[plan.PresenceRecord!];
        Assert.Equal("required", EstateModel.BodyString(record, "presence"));
        Assert.Equal(DeviceRecords.AndroidKeyAttestation, EstateModel.BodyString(record, "presenceVerification"));
        Assert.False(record.Body.TryGetProperty("androidAttestation", out _));
        RecordEnvelope admission = estate.Load().ById[result.PresenceAdmission!.Written!.Id];
        Assert.Equal(record.Reference, admission.Subject);
        Assert.False(estate.Load().ById[result.Admission!.Written!.Id].Body.TryGetProperty("androidAttestation", out _));
        JsonElement retained = admission.Body.GetProperty("androidAttestation");
        Assert.Equal(ContentHash.Sha256(Base64Url.DecodeFromChars(second.AndroidAttestation![0])), retained.GetProperty("leafDigest").GetString());
        Assert.Equal(plan.RuntimeNonce, retained.GetProperty("challenge").GetString());
        Assert.Equal(3, retained.GetProperty("userAuthType").GetInt32());
        Assert.True(retained.GetProperty("perUse").GetBoolean());
        Assert.True(retained.GetProperty("deviceLocked").GetBoolean());
        Assert.Equal("verified", retained.GetProperty("verifiedBootState").GetString());
        Assert.Equal("required", Assert.Single(service.Devices(), device => device.Id == record.Id).Presence);

        // Retaining the marker in a revision cannot make the issued evidence stand for another key.
        using ECDsa rotatedKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        JsonObject rotated = JsonNode.Parse(record.Body.GetRawText())!.AsObject();
        rotated["publicKey"] = RecordSigning.EncodePublicKey(rotatedKey.ExportSubjectPublicKeyInfo());
        using JsonDocument next = JsonDocument.Parse(rotated.ToJsonString());
        DateTimeOffset at = record.AtTime.AddSeconds(1);
        string revision = Ulid.NewUlid(at).Value;
        AuthoredRecord invalidRotation = RecordAuthor.Create(new RecordDraft("device", record.By, next.RootElement.Clone(), Supersedes: record.Id, At: at, Id: revision, Format: 3),
            [new FixtureSigner(record.Id, presenceKey), new FixtureSigner(record.Id, rotatedKey)]);
        RecordAuthor.Write(estate.Root, invalidRotation);
        Assert.Contains(estate.Load().Findings, finding => finding.Code == "device.presence-verification" && finding.Location == invalidRotation.Envelope.ExpectedPath);
        Assert.Equal("declared", Assert.Single(service.Devices(), device => device.Id == record.Id).Presence);
    }

    [Fact]
    public void DeviceListAndAlternateSignerRecheckAndroidRevocation()
    {
        SyntheticAndroidAttestation.RequireNativeTrust();
        using var estate = new TestEstate(3);
        using EstateService service = EstateService.Open(estate.Root);
        PairingAuthority authority = Authority(estate, service);
        using var attestation = new SyntheticAndroidAttestation();
        var revocations = new HashSet<string>();
        service.PairingAttestation = attestation.Verifier(() => revocations);
        PairingPlan plan = service.PairingPlan(estate.OperatorId);
        using ECDsa phoneKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        PairingDevice phone = Device(phoneKey, "android", DeviceKeyStores.AndroidKeystoreKind, "required") with
        {
            AndroidAttestation = attestation.Issue(phoneKey.ExportSubjectPublicKeyInfo(), Base64Url.DecodeFromChars(plan.RuntimeNonce)),
        };
        (WriteOutcome written, WriteOutcome? admission) = service.PairDevice(plan, Profiles, phone, Sign(plan, plan.Record, phone, phoneKey), authority, "Presence upgrade.");
        Assert.True(written.Ok, written.Problem?.Message);
        Assert.True(admission?.Ok, admission?.Problem?.Message);
        Assert.Equal("required", Assert.Single(service.Devices(), row => row.Id == plan.Record).Presence);

        string requesterKey = EstateModel.BodyString(estate.Load().ById[authority.Device], "publicKey")!;
        using JsonDocument order = JsonDocument.Parse(ResultOutputSchemeTests.OrderBody);
        using (service.ForDeviceRequest(authority.Device, requesterKey, null))
        {
            RecordPrepareResponse prepared = service.PrepareRecord(new("work-order", estate.OperatorId, order.RootElement.Clone()));
            Assert.True(prepared.Ok, prepared.Problem?.Message);

            // Change only Google's status since pairing, between preparation and the alternate signature.
            revocations.Add("2");
            var signed = new SignedRecordRequest(prepared.Prepared!.Plan,
                Base64Url.EncodeToString(phoneKey.SignHash(RecordSigning.Digest(prepared.Prepared.Hash)))) { Device = plan.Record };
            WriteOutcome refused = service.NewSignedRecord(signed);
            Assert.False(refused.Ok);
            Assert.Contains("no verified per-signature presence", refused.Problem?.Message, StringComparison.Ordinal);
        }

        using EstateService reopened = EstateService.Open(estate.Root);
        reopened.PairingAttestation = attestation.Verifier(() => revocations);
        Assert.Equal("declared", Assert.Single(reopened.Devices(), row => row.Id == plan.Record).Presence);
    }

    [Theory]
    [InlineData("no-auth")]
    [InlineData("timed")]
    [InlineData("software")]
    [InlineData("software-auth")]
    [InlineData("expired")]
    [InlineData("wrong-root")]
    [InlineData("wrong-key")]
    [InlineData("other-exchange")]
    [InlineData("missing")]
    [InlineData("malformed")]
    [InlineData("revoked")]
    [InlineData("status-unavailable")]
    public void AnUnverifiedAndroidKeyLeavesNoDeviceOrAdmission(string reason)
    {
        if (reason is not ("missing" or "malformed")) SyntheticAndroidAttestation.RequireNativeTrust();
        using var estate = new TestEstate(3);
        using EstateService service = EstateService.Open(estate.Root);
        PairingAuthority authority = Authority(estate, service);
        using var evidence = new SyntheticAndroidAttestation();
        using var otherEvidence = new SyntheticAndroidAttestation();
        service.PairingAttestation = evidence.Verifier(() => reason == "status-unavailable" ? null : reason == "revoked" ? new HashSet<string> { "2" } : new HashSet<string>());
        var plan = service.PairingPlan(estate.OperatorId, presence: true);
        using ECDsa requestKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using ECDsa presenceKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        PairingDevice request = Device(requestKey, "android", DeviceKeyStores.AndroidKeystoreKind, "none");
        byte[] key = (reason == "wrong-key" ? requestKey : presenceKey).ExportSubjectPublicKeyInfo();
        byte[] nonce = Base64Url.DecodeFromChars(reason == "other-exchange" ? PairingProtocol.NewNonce() : plan.RuntimeNonce);
        IReadOnlyList<string>? certificates = reason switch
        {
            "missing" => null,
            "malformed" => [Base64Url.EncodeToString([1, 2, 3])],
            _ => (reason == "wrong-root" ? otherEvidence : evidence).Issue(key, nonce, reason, expired: reason == "expired"),
        };
        PairingDevice second = Device(presenceKey, "android", DeviceKeyStores.AndroidKeystoreKind, "required") with { AndroidAttestation = certificates };
        int before = estate.Load().Records.Count;
        var result = service.PairDevice(plan, Profiles, request, Sign(plan, plan.Record, request, requestKey), authority, "Test pairing.", second, Sign(plan, plan.PresenceRecord!, second, presenceKey));
        Assert.False(result.Device.Ok);
        Assert.Contains("attestation", result.Device.Problem?.Message, StringComparison.Ordinal);
        Assert.Equal(before, estate.Load().Records.Count);
    }

    [Fact]
    public void StandaloneIosKeyIsDeclaredAndItsDeclarationDoesNotBecomeVerified()
    {
        using var estate = new TestEstate(3);
        using EstateService service = EstateService.Open(estate.Root);
        PairingAuthority authority = Authority(estate, service);
        PairingPlan plan = service.PairingPlan(estate.OperatorId);
        using ECDsa presenceKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        PairingDevice declared = Device(presenceKey, "ios", DeviceKeyStores.IosSecureEnclaveKind, "declared");
        (WriteOutcome record, _) = service.PairDevice(plan, Profiles, declared, Sign(plan, plan.Record, declared, presenceKey), authority, "Test pairing.");
        Assert.True(record.Ok, record.Problem?.Message);
        RecordEnvelope saved = estate.Load().ById[plan.Record];
        Assert.Equal("declared", EstateModel.BodyString(saved, "presence"));
        Assert.False(saved.Body.TryGetProperty("presenceVerification", out _));
        Assert.Equal("declared", Assert.Single(service.Devices(), device => device.Id == saved.Id).Presence);

        // Another already-admitted device cannot use the declaration as a verified signature for a prepared record.
        string requester = authority.Device;
        string requestKey = EstateModel.BodyString(estate.Load().ById[requester], "publicKey")!;
        using JsonDocument draft = JsonDocument.Parse(ResultOutputSchemeTests.OrderBody);
        using (service.ForDeviceRequest(requester, requestKey, null))
        {
            RecordPrepareResponse preparedOutcome = service.PrepareRecord(new("work-order", estate.OperatorId, draft.RootElement.Clone()));
            Assert.True(preparedOutcome.Ok, preparedOutcome.Problem?.Message);
            RecordPrepared prepared = preparedOutcome.Prepared!;
            var request = new SignedRecordRequest(prepared.Plan, Base64Url.EncodeToString(presenceKey.SignHash(RecordSigning.Digest(prepared.Hash)))) { Device = saved.Id };
            WriteOutcome denied = service.NewSignedRecord(request);
            Assert.False(denied.Ok);
            Assert.Contains("no verified per-signature presence", denied.Problem?.Message, StringComparison.Ordinal);
        }

        var oldPlan = service.PairingPlan(estate.OperatorId);
        PairingDevice unsupported = declared with { Presence = "required" };
        (WriteOutcome refused, _) = service.PairDevice(oldPlan, Profiles, unsupported, Sign(oldPlan, oldPlan.Record, unsupported, presenceKey), authority, "Test pairing.");
        Assert.False(refused.Ok);
        Assert.DoesNotContain(oldPlan.Record, estate.Load().ById.Keys);
    }

    private sealed class FixtureSigner(string deviceId, ECDsa key) : IRecordSigner
    {
        public string DeviceId => deviceId;
        public RecordSignature Sign(ReadOnlySpan<byte> digest) => new(RecordSigning.Algorithm, DeviceId, Base64Url.EncodeToString(key.SignHash(digest)));
    }

    private sealed class FixtureClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(2030, 1, 1, 0, 0, 0, TimeSpan.Zero);
    }

    private static PairingAuthority Authority(TestEstate estate, EstateService service)
    {
        WriteOutcome created = service.DeviceCreate(new(estate.OperatorId, "Test operator device", "file"));
        Assert.True(created.Ok, created.Problem?.Message);
        WriteOutcome admitted = service.DeviceAdmit(new(created.Written!.Id, estate.OperatorId, "Test first authority."));
        Assert.True(admitted.Ok, admitted.Problem?.Message);
        (PairingAuthority? authority, string? problem) = service.PairingIssuer(estate.OperatorId, estate.OperatorId, Profiles);
        Assert.NotNull(authority);
        Assert.Null(problem);
        return authority;
    }

    private static PairingDevice Device(ECDsa key, string platform, string custody, string presence) =>
        new("Test phone", platform, custody, Base64Url.EncodeToString(key.ExportSubjectPublicKeyInfo()), presence);

    private static string Sign(PairingPlan plan, string record, PairingDevice device, ECDsa key)
    {
        var body = DeviceRecords.Body(plan.Principal, device.Name, device.Platform, device.Custody, device.PublicKey, Profiles, plan.Format, device.Presence);
        if (device.AndroidAttestation is not null) body["presenceVerification"] = DeviceRecords.AndroidKeyAttestation;
        var draft = DeviceRecords.Draft(plan.Principal, record, DateTimeOffset.Parse(plan.At, CultureInfo.InvariantCulture), plan.Format, body);
        return Base64Url.EncodeToString(key.SignHash(RecordSigning.Digest(DeviceRecords.Hash(draft)), DSASignatureFormat.IeeeP1363FixedFieldConcatenation));
    }
}
