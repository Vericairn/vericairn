// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Buffers.Text;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Vericairn.Protocol;
using Vericairn.Sdk;

namespace Vericairn.Daemon.Tests;

[Collection("daemon")]
public class RequestRevocationTests
{
    [Theory]
    [InlineData(false, "revoke")]
    [InlineData(true, "revoke")]
    [InlineData(false, "rotate")]
    [InlineData(true, "rotate")]
    [InlineData(false, "rename")]
    [InlineData(true, "rename")]
    [InlineData(false, "withdraw-profile")]
    [InlineData(true, "withdraw-profile")]
    public async Task AWriteRechecksTheAuthenticatedDeviceAndKeyUnderTheEstateLock(bool localNetwork, string change)
    {
        await using LocalNetworkHarness harness = await LocalNetworkHarness.StartAsync();
        (DeviceRequestSigner signer, string publicKey) = Device(harness, "Requesting device");
        // Another admitted device of the same principal can still sign records here. Refusal must name the request's device,
        // not merely ask whether its principal has some admitted key left.
        (DeviceRequestSigner backup, _) = Device(harness, "Other device");
        using X509Certificate2 credential = LocalNetworkCredential.Issue(signer, Base64Url.DecodeFromChars(publicKey), DateTimeOffset.UtcNow);
        NewRecordRequest write = Order(harness, "W-RACE");
        int reached = 0;
        harness.Host.BeforeDeviceWrite = () =>
        {
            harness.Host.BeforeDeviceWrite = null;
            Interlocked.Increment(ref reached);
            Assert.Equal("admitted", harness.Host.Service.Standing(signer.DeviceId)!.Admission);
            WriteOutcome changed = change switch
            {
                "revoke" => harness.Socket.DeviceRevoke(new RevokeRequest(signer.DeviceId, harness.Estate.OperatorId, "Revoked after authentication, before the write.")),
                "rotate" => harness.Socket.DeviceRotate(new DeviceRotateRequest(signer.DeviceId)),
                "rename" => harness.Socket.DeviceRename(new DeviceRenameRequest(signer.DeviceId, "A new name, the same key")),
                _ => WithdrawProfile(harness, signer.DeviceId),
            };
            Assert.True(changed.Ok, changed.Problem?.Message);

            // A concurrent local-socket operation has no requesting device. The pending request's scope must not affect it.
            WriteOutcome socket = harness.Socket.NewRecord(Order(harness, "W-SOCKET"));
            Assert.True(socket.Ok, socket.Problem?.Message);
        };

        string response = await SendAsync(harness, signer, localNetwork, credential, "/v1/records", JsonSerializer.SerializeToUtf8Bytes(write, ProtocolJson.Options));
        Assert.Equal(1, reached);
        bool denied = change is "revoke" or "rotate" || (change == "withdraw-profile" && localNetwork);
        Assert.StartsWith(denied ? "HTTP/1.1 401" : "HTTP/1.1 200", response, StringComparison.Ordinal);
        Assert.Equal(!denied, harness.Socket.Records("work-order").Any(r => r.Envelope.GetProperty("body").GetProperty("key").GetString() == "W-RACE"));
        if (denied)
        {
            Assert.Contains("\"code\":\"denied\"", response, StringComparison.Ordinal);
            Assert.Contains(change switch { "revoke" => "revoked", "rotate" => "current key", _ => "no longer permitted" }, response, StringComparison.Ordinal);
        }

        if (localNetwork)
        {
            string live = Path.GetDirectoryName(DaemonHost.EndpointFilePath(harness.Estate.Root))!;
            LocalNetworkWrite entry = Assert.Single(LocalNetworkLedger.Read(live), w => w.Device == signer.DeviceId);
            Assert.Equal(denied ? "failed" : "written", entry.State);
            Assert.Equal(denied, entry.Record is null);
        }

        using VericairnClient other = VericairnClient.Loopback(harness.Host.Endpoint, backup);
        Assert.True(other.NewRecord(Order(harness, "W-OTHER")).Ok);
        Assert.Equal(0, harness.Socket.Check().Errors);
    }

    [Fact]
    public async Task ARevokedRequestCannotStoreAnObjectEither()
    {
        await using LocalNetworkHarness harness = await LocalNetworkHarness.StartAsync();
        (DeviceRequestSigner signer, _) = Device(harness, "Object writer");
        harness.Host.BeforeDeviceWrite = () =>
        {
            harness.Host.BeforeDeviceWrite = null;
            Assert.True(harness.Socket.DeviceRevoke(new RevokeRequest(signer.DeviceId, harness.Estate.OperatorId, "Revoked before object storage.")).Ok);
        };
        byte[] bytes = Encoding.UTF8.GetBytes("An object that must not be stored.");
        string response = await SendAsync(harness, signer, false, null, "/v1/objects", bytes, "text/plain");
        Assert.StartsWith("HTTP/1.1 401", response, StringComparison.Ordinal);
        Assert.DoesNotContain(harness.Socket.Objects(), o => o.Hash == Records.Hashing.ContentHash.Sha256(bytes));
    }

    private static (DeviceRequestSigner Signer, string PublicKey) Device(LocalNetworkHarness harness, string name)
    {
        WriteOutcome device = harness.Socket.DeviceCreate(new DeviceCreateRequest(harness.Estate.BuilderId, name, "file", ["local-network"]));
        Assert.True(device.Ok, device.Problem?.Message);
        Assert.True(harness.Socket.DeviceAdmit(new AdmitRequest(device.Written!.Id, harness.Estate.OperatorId, "A request test device.")).Ok);
        return (DeviceRequestSigner.Find(device.Written.Id)!, harness.Host.Service.Standing(device.Written.Id)!.PublicKey);
    }

    private static NewRecordRequest Order(LocalNetworkHarness harness, string key)
    {
        using JsonDocument body = JsonDocument.Parse("{\"key\":\"" + key + "\",\"kind\":\"ordinary\",\"title\":\"Request race\",\"intent\":\"Test the write lock.\",\"scope\":[\"test\"],\"exitQuestion\":\"Refused?\",\"inputs\":[],\"deliverables\":[\"test\"]}");
        return new NewRecordRequest("work-order", harness.Estate.BuilderId, body.RootElement.Clone());
    }

    private static WriteOutcome WithdrawProfile(LocalNetworkHarness harness, string device)
    {
        RecordResponse record = harness.Socket.Record(device)!;
        JsonObject body = JsonNode.Parse(record.Envelope.GetProperty("body").GetRawText())!.AsObject();
        body["profiles"] = new JsonArray("embedded");
        using JsonDocument changed = JsonDocument.Parse(body.ToJsonString());
        return harness.Socket.NewRecord(new NewRecordRequest("device", harness.Estate.BuilderId, changed.RootElement, Supersedes: device));
    }

    private static Task<string> SendAsync(LocalNetworkHarness harness, DeviceRequestSigner signer, bool localNetwork, X509Certificate2? credential, string target, byte[] body, string contentType = "application/json")
    {
        string at = RequestSigning.FormatTime(DateTimeOffset.UtcNow);
        byte[] digest = localNetwork ? harness.Digest("POST", target, at, body) : RequestSigning.Digest("POST", target, at, body);
        string headers = $"POST {target} HTTP/1.1\r\nHost: x\r\nConnection: close\r\nContent-Type: {contentType}\r\nContent-Length: {body.Length}\r\n{RequestSigning.DeviceHeader}: {signer.DeviceId}\r\n{RequestSigning.SignedAtHeader}: {at}\r\n{RequestSigning.SignatureHeader}: {Base64Url.EncodeToString(signer.Sign(digest))}\r\n\r\n";
        return Raw.ExchangeAsync(localNetwork ? harness.Address : $"{harness.Host.Endpoint.Loopback!.Host}:{harness.Host.Endpoint.Loopback.Port}", localNetwork ? credential : null, harness.Host.Endpoint.Loopback!.Certificate, [.. Encoding.ASCII.GetBytes(headers), .. body], TimeSpan.FromSeconds(10));
    }
}
