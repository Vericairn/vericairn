// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Buffers.Text;
using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Vericairn.Protocol;
using Vericairn.Records.Authoring;
using Vericairn.Records.Estate;
using Vericairn.Records.Signing;
using Vericairn.Sdk;

namespace Vericairn.Daemon.Tests;

[Collection("daemon")]
public class PairingRevocationTests
{
    [Fact]
    public async Task TheAdmissionNamesTheRequestingDeviceEvenWhenAnotherDeviceSignsIt()
    {
        await using LocalNetworkHarness harness = await LocalNetworkHarness.StartAsync();
        (string principal, string device, _) = Issuer(harness, authority: false);
        using VericairnClient loopback = VericairnClient.Loopback(harness.Host.Endpoint, DeviceRequestSigner.Find(device));
        (PairingIssued? issued, _) = loopback.IssuePairing(new PairingIssueRequest(harness.Estate.BuilderId, principal));
        Assert.NotNull(issued);
        RuntimeConnection paired = harness.Connections.Add(harness.Address, issued.Code, new PairingOptions("Phone"));
        var admission = Assert.Single(harness.Estate.Load().About(paired.Device, "admission"));
        Assert.Contains("issued from device " + device, EstateModel.BodyString(admission, "note"), StringComparison.Ordinal);
    }

    [Fact]
    public async Task RestoringAGrantDoesNotReviveCodesIssuedBeforeItsRevocation()
    {
        await using LocalNetworkHarness harness = await LocalNetworkHarness.StartAsync();
        (string principal, string device, Action revoke) = Issuer(harness, authority: true);
        var original = Assert.Single(harness.Estate.Load().Current("authority"), a => EstateModel.BodyString(a, "grantee") == principal);
        using VericairnClient loopback = VericairnClient.Loopback(harness.Host.Endpoint, DeviceRequestSigner.Find(device));
        (PairingIssued? issued, _) = loopback.IssuePairing(new PairingIssueRequest(harness.Estate.BuilderId, principal));
        Assert.NotNull(issued);
        revoke();
        var revoked = harness.Estate.Load().Latest(original);
        Assert.True(harness.Socket.NewRecord(new NewRecordRequest("authority", harness.Estate.OperatorId, original.Body, Supersedes: revoked.Id)).Ok);
        PairingException refused = Assert.Throws<PairingException>(() => harness.Connections.Add(harness.Address, issued.Code, new PairingOptions("Phone")));
        Assert.Equal("denied", refused.Code);
        Assert.Contains("issuing", refused.Message, StringComparison.Ordinal);
        // Restored authority can issue a new code, without reviving the old one.
        (PairingIssued? fresh, _) = loopback.IssuePairing(new PairingIssueRequest(harness.Estate.BuilderId, principal));
        Assert.NotNull(fresh);
        Assert.Equal(principal, harness.Host.Service.Standing(device)!.Principal);
        Assert.NotNull(harness.Connections.Add(harness.Address, fresh.Code, new PairingOptions("Phone")));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TheEstateWriteRechecksAnIssuerAfterTheRegistryAcceptedItsConfirmation(bool authority)
    {
        await using LocalNetworkHarness harness = await LocalNetworkHarness.StartAsync();
        (string principal, string device, Action revoke) = Issuer(harness, authority);
        using VericairnClient loopback = VericairnClient.Loopback(harness.Host.Endpoint, DeviceRequestSigner.Find(device));
        (PairingIssued? issued, _) = loopback.IssuePairing(new PairingIssueRequest(harness.Estate.OperatorId, principal));
        Assert.NotNull(issued);
        using HttpClient client = PairingTests.PairingClient(harness);
        (PairingHello hello, PairingChallenge challenge) = await PairingTests.Hello(harness, client, issued.Code);
        PairingConfirm confirm = Confirmation(harness, issued.Code, hello, challenge);
        var accepted = harness.Host.Pairings.Confirm(confirm);
        Assert.NotNull(accepted.Pairing);
        Assert.NotNull(accepted.Exchange);
        int devices = harness.Socket.Devices().Count;
        revoke();
        var result = harness.Host.Service.PairDevice(accepted.Exchange.Plan, accepted.Pairing.Profiles, confirm.Device, confirm.Signature, accepted.Pairing.Issuer, "A confirmation held before revocation.");
        Assert.False(result.Device.Ok);
        Assert.Equal("denied", result.Device.Problem?.Code);
        Assert.Equal(devices, harness.Socket.Devices().Count);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task RevokingTheIssuingDeviceOrItsAuthorityEndsTheCodeAndOpenExchanges(bool authority, bool afterHello)
    {
        await using LocalNetworkHarness harness = await LocalNetworkHarness.StartAsync();
        (string principal, string device, Action revoke) = Issuer(harness, authority);
        using VericairnClient loopback = VericairnClient.Loopback(harness.Host.Endpoint, DeviceRequestSigner.Find(device));
        (PairingIssued? issued, Problem? problem) = loopback.IssuePairing(new PairingIssueRequest(harness.Estate.OperatorId, principal));
        Assert.NotNull(issued);
        Assert.Null(problem);
        using HttpClient pairing = PairingTests.PairingClient(harness);
        PairingConfirm? confirm = null;
        if (afterHello)
        {
            (PairingHello hello, PairingChallenge challenge) = await PairingTests.Hello(harness, pairing, issued.Code);
            confirm = Confirmation(harness, issued.Code, hello, challenge);
        }

        int devices = harness.Socket.Devices().Count;
        revoke();
        if (confirm is null)
        {
            PairingException refused = Assert.Throws<PairingException>(() => harness.Connections.Add(harness.Address, issued.Code, new PairingOptions("Unwanted operator device")));
            Assert.Equal("denied", refused.Code);
            Assert.Contains("issuing", refused.Message, StringComparison.Ordinal);
        }
        else
        {
            using HttpResponseMessage response = await pairing.PostAsJsonAsync(new Uri("/v1/pairing/confirm", UriKind.Relative), confirm, ProtocolJson.Options, TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        Assert.Equal(devices, harness.Socket.Devices().Count);
    }

    private static (string Principal, string Device, Action Revoke) Issuer(LocalNetworkHarness harness, bool authority)
    {
        string principal = harness.Estate.OperatorId;
        string? grant = null;
        string? grantBody = null;
        if (authority)
        {
            principal = Write("principal", """{"kind":"human","name":"Pairing administrator","roles":["contributor"]}""");
            string basis = Write("decision", $$"""{"title":"Administration","statement":"Administer devices.","summary":"An administrator.","synthesis":"Grant administration.","effects":["administer"],"affects":[],"decidedBy":"{{harness.Estate.OperatorId}}","decidedAt":"2026-10-15T00:00:00Z","stake":"medium","notAuthorized":[]}""");
            grantBody = $$"""{"grantee":"{{principal}}","scope":["administer"],"reserved":[],"conditions":[],"basis":"{{basis}}"}""";
            grant = Write("authority", grantBody);
        }

        WriteOutcome created = harness.Socket.DeviceCreate(new DeviceCreateRequest(principal, "Pairing issuer", "file"));
        Assert.True(created.Ok, created.Problem?.Message);
        string device = created.Written!.Id;
        Assert.True(harness.Socket.DeviceAdmit(new AdmitRequest(device, harness.Estate.OperatorId, "The code issuer.")).Ok);
        return (principal, device, () =>
        {
            if (authority)
            {
                Write("authority", grantBody!.Replace("administer", "local-engineering", StringComparison.Ordinal), grant);
            }
            else
            {
                Assert.True(harness.Socket.DeviceRevoke(new RevokeRequest(device, harness.Estate.OperatorId, "The issuer was revoked.")).Ok);
            }
        });

        string Write(string type, string json, string? supersedes = null)
        {
            using JsonDocument body = JsonDocument.Parse(json);
            WriteOutcome written = harness.Socket.NewRecord(new NewRecordRequest(type, harness.Estate.OperatorId, body.RootElement, Supersedes: supersedes));
            Assert.True(written.Ok, written.Problem?.Message);
            return written.Written!.Id;
        }
    }

    private static PairingConfirm Confirmation(LocalNetworkHarness harness, string code, PairingHello hello, PairingChallenge challenge)
    {
        Assert.True(PairingProtocol.TryParse(code, out string secret));
        (_, byte[] deviceKey) = PairingProtocol.Keys(secret);
        byte[] publicKey = harness.DeviceKeys.Create(challenge.Record);
        var device = new PairingDevice("New device", "linux", "file-owner-only", RecordSigning.EncodePublicKey(publicKey), "none");
        DateTimeOffset at = DateTimeOffset.ParseExact(challenge.At, "yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);
        var body = DeviceRecords.Body(challenge.Principal, device.Name, device.Platform, device.Custody, device.PublicKey, challenge.Profiles, challenge.Format, device.Presence);
        string hash = DeviceRecords.Hash(DeviceRecords.Draft(challenge.Principal, challenge.Record, at, challenge.Format, body));
        var confirm = new PairingConfirm(challenge.RuntimeNonce, device, Base64Url.EncodeToString(harness.DeviceKeys.Sign(challenge.Record, RecordSigning.Digest(hash))), string.Empty);
        return confirm with { Mac = PairingProtocol.ConfirmMac(deviceKey, hello, challenge, confirm) };
    }
}
