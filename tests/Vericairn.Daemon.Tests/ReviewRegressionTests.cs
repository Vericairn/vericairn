// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Buffers.Text;
using System.Net;
using System.Net.Http.Json;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using Vericairn.Protocol;
using Vericairn.Records.Estate;
using Vericairn.Records.Model;
using Vericairn.Records.Signing;
using Vericairn.Sdk;

namespace Vericairn.Daemon.Tests;

/// <summary>
/// The security review of ID-02, held: each test named for a finding (F1 to F17) asserts the behaviour that fixed it, and each
/// Sound_ test the behaviour the review verified as sound. They listen on 127.0.0.1 only.
/// </summary>
[Collection("daemon")]
public class ReviewRegressionTests
{
    public ReviewRegressionTests()
    {
        DaemonTestHome.Ensure();
    }

    private static string Order(string key) =>
        "{\"key\":\"" + key + "\",\"kind\":\"ordinary\",\"title\":\"Review\",\"intent\":\"Probe.\",\"scope\":[\"x\"],\"exitQuestion\":\"?\",\"inputs\":[],\"deliverables\":[\"y\"]}";

    private static void Say(string text) => TestContext.Current.TestOutputHelper?.WriteLine(text);

    private static string Message(string body)
    {
        try
        {
            return JsonDocument.Parse(body).RootElement.GetProperty("message").GetString() ?? body;
        }
        catch (JsonException)
        {
            return body;
        }
    }

    [Fact]
    public async Task F1_ABomOrAForeignCharsetNeverCarriesAnotherPrincipal()
    {
        await using LocalNetworkHarness harness = await LocalNetworkHarness.StartAsync();
        RuntimeConnection connection = harness.Pair(harness.Estate.BuilderId, "Agent laptop");
        DeviceRequestSigner signer = harness.Signer(connection);
        using X509Certificate2 credential = LocalNetworkCredential.Issue(signer, Base64Url.DecodeFromChars(connection.DevicePublicKey), DateTimeOffset.UtcNow);
        using var lan = new Lan(harness.Address, credential, harness.Host.Endpoint.Loopback!.Certificate);
        string op = harness.Estate.OperatorId;
        string json = "{\"type\":\"work-order\",\"by\":\"" + op + "\",\"body\":" + Order("W-R1") + "}";

        // Plain, with a byte order mark, or declared UTF-16: the operator is never the principal a device of the builder acts for.
        (HttpStatusCode plain, string plainBody) = await lan.SendAsync(HttpMethod.Post, "/v1/records", signer, Encoding.UTF8.GetBytes(json), "application/json");
        Assert.Equal(HttpStatusCode.Unauthorized, plain);
        Assert.Contains("acts for its own principal", Message(plainBody), StringComparison.Ordinal);
        (HttpStatusCode bom, string bomBody) = await lan.SendAsync(HttpMethod.Post, "/v1/records", signer, [0xEF, 0xBB, 0xBF, .. Encoding.UTF8.GetBytes(json)], "application/json");
        Assert.Equal(HttpStatusCode.Unauthorized, bom);
        Assert.Contains("byte order mark", Message(bomBody), StringComparison.Ordinal);
        (HttpStatusCode utf16, string utf16Body) = await lan.SendAsync(HttpMethod.Post, "/v1/records", signer, Encoding.Unicode.GetBytes(json), "application/json; charset=utf-16");
        Assert.Equal(HttpStatusCode.Unauthorized, utf16);
        Assert.Contains("utf-16", Message(utf16Body), StringComparison.Ordinal);
        Assert.Null(harness.Socket.Brief("W-R1"));

        // The escalation the review demonstrated, a decision and an 'administer' grant written as the operator, is refused in every encoding.
        string decision = "{\"type\":\"decision\",\"by\":\"" + op + "\",\"body\":{\"title\":\"Escalation\",\"statement\":\"Administer devices.\",\"summary\":\"s\",\"synthesis\":\"s\",\"effects\":[\"administer\"],\"affects\":[],\"decidedBy\":\"" + op + "\",\"decidedAt\":\"2026-10-15T00:00:00Z\",\"stake\":\"medium\",\"notAuthorized\":[]}}";
        foreach (byte[] body in new[] { Encoding.UTF8.GetBytes(decision), [0xEF, 0xBB, 0xBF, .. Encoding.UTF8.GetBytes(decision)] })
        {
            Assert.Equal(HttpStatusCode.Unauthorized, (await lan.SendAsync(HttpMethod.Post, "/v1/records", signer, body, "application/json")).Status);
        }

        Assert.False(harness.Estate.Load().HasScope(harness.Estate.BuilderId, "administer", DateTimeOffset.UtcNow));
        Assert.Empty(harness.Estate.Load().OfType("decision"));

        // What the device's own principal writes, in UTF-8 with or without the charset named, goes through.
        string own = "{\"type\":\"work-order\",\"by\":\"" + harness.Estate.BuilderId + "\",\"body\":" + Order("W-R2") + "}";
        Assert.Equal(HttpStatusCode.OK, (await lan.SendAsync(HttpMethod.Post, "/v1/records", signer, Encoding.UTF8.GetBytes(own), "application/json; charset=utf-8")).Status);
        Assert.Equal(harness.Estate.BuilderId, harness.Estate.Load().OfType("work-order").Single(r => EstateModel.BodyString(r, "key") == "W-R2").By);
    }

    [Fact]
    public async Task F2_OnLoopbackADeviceActsForItsOwnPrincipalAndIssuesNoCodeAsAnother()
    {
        await using LocalNetworkHarness harness = await LocalNetworkHarness.StartAsync();
        string op = harness.Estate.OperatorId;
        string builder = harness.Estate.BuilderId;

        // An agent's own device on the runtime's machine, admitted for the agent's work, reached over loopback (TCP on 127.0.0.1).
        WriteOutcome local = harness.Socket.DeviceCreate(new DeviceCreateRequest(builder, "Agent worker", "file"));
        Assert.True(local.Ok, local.Problem?.Message);
        Assert.True(harness.Socket.DeviceAdmit(new AdmitRequest(local.Written!.Id, op, "The agent's worker device.")).Ok);
        using VericairnClient agent = VericairnClient.Loopback(harness.Host.Endpoint, DeviceRequestSigner.Find(local.Written.Id)!);

        // It issues no pairing code under the operator's authority, for the operator or anyone.
        (PairingIssued? issued, Problem? problem) = agent.IssuePairing(new PairingIssueRequest(op, op));
        Assert.Null(issued);
        Assert.Equal("denied", problem!.Code);
        Assert.Contains("acts for its own principal", problem.Message, StringComparison.Ordinal);
        Assert.Equal("refused", agent.IssuePairing(new PairingIssueRequest(builder, builder)).Problem!.Code);

        // It writes no record, creates no device and renames no device of another principal.
        using JsonDocument order = JsonDocument.Parse(Order("W-R3"));
        Assert.Equal(HttpStatusCode.Unauthorized, Assert.Throws<HttpRequestException>(() => agent.NewRecord(new NewRecordRequest("work-order", op, order.RootElement))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, Assert.Throws<HttpRequestException>(() => agent.DeviceCreate(new DeviceCreateRequest(op, "Planted", "file"))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, Assert.Throws<HttpRequestException>(() => agent.DeviceRename(new DeviceRenameRequest(harness.OperatorDevice, "Renamed"))).StatusCode);
        (CertificateRotated? rotated, Problem? rotation) = agent.RotateCertificate(new CertificateRotateRequest(Identity: true));
        Assert.Null(rotated);
        Assert.Equal("denied", rotation!.Code);
        Assert.Contains("is an administrator's", rotation.Message, StringComparison.Ordinal);
        Assert.Equal(2, harness.Socket.Devices().Count);

        // What it does for its own principal goes through, and the operator's own device acts for the operator as before.
        Assert.True(agent.NewRecord(new NewRecordRequest("work-order", builder, order.RootElement)).Ok);
        using VericairnClient operatorDevice = VericairnClient.Loopback(harness.Host.Endpoint, DeviceRequestSigner.Find(harness.OperatorDevice)!);
        (PairingIssued? genuine, Problem? none) = operatorDevice.IssuePairing(new PairingIssueRequest(builder, op));
        Assert.True(genuine is not null, none?.Message);
        Assert.NotNull(operatorDevice.RotateCertificate(new CertificateRotateRequest()).Rotated);
    }

    [Fact]
    public async Task F3_ADeviceRecordIsRefusedOverTheLocalNetworkOnTheRecordsRouteAsOnRename()
    {
        await using LocalNetworkHarness harness = await LocalNetworkHarness.StartAsync();
        RuntimeConnection phone = harness.Pair(harness.Estate.OperatorId, "Operator phone");
        DeviceRequestSigner signer = harness.Signer(phone);
        using X509Certificate2 credential = LocalNetworkCredential.Issue(signer, Base64Url.DecodeFromChars(phone.DevicePublicKey), DateTimeOffset.UtcNow);
        using var lan = new Lan(harness.Address, credential, harness.Host.Endpoint.Loopback!.Certificate);
        string rename = "{\"device\":\"" + harness.OperatorDevice + "\",\"name\":\"Renamed\"}";
        Assert.Equal(HttpStatusCode.Unauthorized, (await lan.SendAsync(HttpMethod.Post, "/v1/devices/rename", signer, Encoding.UTF8.GetBytes(rename), "application/json")).Status);

        RecordEnvelope device = harness.Estate.Load().ById[harness.OperatorDevice];
        var body = System.Text.Json.Nodes.JsonNode.Parse(device.Body.GetRawText())!.AsObject();
        body["name"] = "Renamed over the local network";
        string json = "{\"type\":\"device\",\"by\":\"" + harness.Estate.OperatorId + "\",\"supersedes\":\"" + harness.OperatorDevice + "\",\"body\":" + body.ToJsonString() + "}";
        (HttpStatusCode status, string answer) = await lan.SendAsync(HttpMethod.Post, "/v1/records", signer, Encoding.UTF8.GetBytes(json), "application/json");
        Assert.Equal(HttpStatusCode.Unauthorized, status);
        Assert.Contains("machine that holds its key", Message(answer), StringComparison.Ordinal);
        Assert.Equal("Operator laptop", Assert.Single(harness.Socket.Devices(), d => d.Id == harness.OperatorDevice).Name);
    }

    [Fact]
    public async Task F3_ReleasesUpgradesObjectsAndRunsAreTheRuntimeMachinesAndRefusedOverTheLocalNetwork()
    {
        await using LocalNetworkHarness harness = await LocalNetworkHarness.StartAsync();
        RuntimeConnection connection = harness.Pair(harness.Estate.BuilderId);
        DeviceRequestSigner signer = harness.Signer(connection);
        using X509Certificate2 credential = LocalNetworkCredential.Issue(signer, Base64Url.DecodeFromChars(connection.DevicePublicKey), DateTimeOffset.UtcNow);
        using var lan = new Lan(harness.Address, credential, harness.Host.Endpoint.Loopback!.Certificate);
        string by = harness.Estate.BuilderId;
        string ulid = Records.Identity.Ulid.NewUlid().Value;
        (string Path, string Json)[] refused =
        [
            ("/v1/releases/submit", "{\"product\":\"" + by + "\",\"version\":\"9.9.9\",\"repository\":\"someone/else\",\"workflow\":\"any.yml\",\"ref\":\"main\",\"by\":\"" + by + "\",\"provider\":\"github\"}"),
            ("/v1/releases/observe", "{\"version\":\"9.9.9\",\"repository\":\"someone/else\",\"by\":\"" + by + "\"}"),
            ("/v1/estate/upgrade", "{\"version\":\"9.9.9\",\"by\":\"" + by + "\",\"check\":false}"),
            ("/v1/objects", "not json at all, stored on the runtime's disk"),
            ("/v1/runs", "{\"assignment\":\"" + ulid + "\",\"launcher\":\"generic\",\"agent\":\"" + by + "\",\"by\":\"" + by + "\",\"repository\":\"" + ulid + "\"}"),
        ];
        foreach ((string path, string json) in refused)
        {
            (HttpStatusCode status, string answer) = await lan.SendAsync(HttpMethod.Post, path, signer, Encoding.UTF8.GetBytes(json), path == "/v1/objects" ? "application/octet-stream" : "application/json");
            Say($"{path}: {(int)status} {answer}");
            Assert.True(status == HttpStatusCode.Unauthorized, $"{path}: {status} {answer}");
        }

        Assert.Empty(harness.Socket.Objects());

        // An upgrade's plan and a run's stop stay the device's principal's to ask for.
        (HttpStatusCode plan, string planBody) = await lan.SendAsync(HttpMethod.Post, "/v1/estate/upgrade", signer, Encoding.UTF8.GetBytes("{\"version\":\"9.9.9\",\"check\":true}"), "application/json");
        Assert.NotEqual(HttpStatusCode.Unauthorized, plan);
        Say($"plan: {(int)plan} {planBody}");
        (HttpStatusCode stop, string stopBody) = await lan.SendAsync(HttpMethod.Post, "/v1/runs/" + ulid + "/stop", signer, Encoding.UTF8.GetBytes("{\"by\":\"" + by + "\"}"), "application/json");
        Assert.NotEqual(HttpStatusCode.Unauthorized, stop);
        Say($"stop: {(int)stop} {stopBody}");
    }

    [Fact]
    public async Task F7_ALocalNetworkWriteIsKeptWithThePairedDeviceBehindItBeyondItsRevocation()
    {
        await using LocalNetworkHarness harness = await LocalNetworkHarness.StartAsync();
        RuntimeConnection phone = harness.Pair(harness.Estate.OperatorId, "Operator phone");
        using VericairnClient client = harness.Connections.Connect(phone);
        using JsonDocument order = JsonDocument.Parse(Order("W-R7"));
        WriteOutcome written = client.NewRecord(new NewRecordRequest("work-order", harness.Estate.OperatorId, order.RootElement));
        Assert.True(written.Ok, written.Problem?.Message);

        // The record is the operator's, signed by the operator's device that this machine holds, as every API write is.
        RecordEnvelope record = harness.Estate.Load().ById[written.Written!.Id];
        Assert.Equal(harness.OperatorDevice, Assert.Single(record.Signatures).Device);

        // The paired device behind it is kept beside the live store, with the record; a refused write leaves no line.
        using JsonDocument other = JsonDocument.Parse(Order("W-R8"));
        Assert.Equal(HttpStatusCode.Unauthorized, Assert.Throws<HttpRequestException>(() => client.NewRecord(new NewRecordRequest("work-order", harness.Estate.BuilderId, other.RootElement))).StatusCode);
        string directory = Path.GetDirectoryName(DaemonHost.EndpointFilePath(harness.Estate.Root))!;
        LocalNetworkWrite kept = Assert.Single(LocalNetworkLedger.Read(directory));
        Assert.Equal((phone.Device, harness.Estate.OperatorId, "POST /v1/records", written.Written.Id, written.Written.Hash), (kept.Device, kept.Principal, kept.Operation, kept.Record, kept.Hash));
        if (!OperatingSystem.IsWindows())
        {
            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(Path.Combine(directory, LocalNetworkLedger.FileName)));
        }

        // It outlives the device's revocation.
        Assert.True(harness.Socket.DeviceRevoke(new RevokeRequest(phone.Device, harness.Estate.OperatorId, "Lost.")).Ok);
        Assert.Equal(written.Written.Id, Assert.Single(LocalNetworkLedger.Read(directory)).Record);
    }

    [Fact]
    public async Task F7_ALocalNetworkWriteIsRefusedWhenItsSignerHereNeedsItsOwnersPresence()
    {
        await using LocalNetworkHarness harness = await LocalNetworkHarness.StartAsync();
        string op = harness.Estate.OperatorId;

        // A person whose only device on the runtime's machine asks for its owner's presence at every signature.
        using JsonDocument person = JsonDocument.Parse("""{"kind":"human","name":"Present Person","roles":["contributor"]}""");
        string present = harness.Socket.NewRecord(new NewRecordRequest("principal", op, person.RootElement)).Written!.Id;
        // Its key is a file here, which a test can make; the record states the enclave custody and presence a Mac with Touch ID would.
        var keys = new FileKeyStore(VericairnHome.Path());
        harness.Estate.SetClock(DateTimeOffset.UtcNow.AddSeconds(-30));
        string id = Records.Identity.Ulid.NewUlid(harness.Estate.Clock.AddSeconds(1)).Value;
        byte[] spki = keys.Create(id);
        System.Text.Json.Nodes.JsonObject body = Records.Authoring.DeviceRecords.Body(present, "Laptop with Touch ID", "macos", SecureEnclaveCustody.Kind, RecordSigning.EncodePublicKey(spki), ["embedded"], 2, "required");
        RecordEnvelope device = harness.Estate.Add("device", present, body.ToJsonString(), id: id, signer: new StoreSigner(keys, id));
        Assert.True(harness.Socket.DeviceAdmit(new AdmitRequest(device.Id, op, "Present Person's laptop.")).Ok);

        // Their phone, paired, is refused a write the runtime would sign with that laptop's key; the same write on the machine goes through.
        RuntimeConnection phone = harness.Pair(present, "Phone");
        using VericairnClient client = harness.Connections.Connect(phone);
        using JsonDocument order = JsonDocument.Parse(Order("W-R9"));
        HttpRequestException refused = Assert.Throws<HttpRequestException>(() => client.NewRecord(new NewRecordRequest("work-order", present, order.RootElement)));
        Assert.Equal(HttpStatusCode.Unauthorized, refused.StatusCode);
        Assert.Null(harness.Socket.Brief("W-R9"));
        Assert.Empty(LocalNetworkLedger.Read(Path.GetDirectoryName(DaemonHost.EndpointFilePath(harness.Estate.Root))!));
        Assert.True(harness.Socket.NewRecord(new NewRecordRequest("work-order", present, order.RootElement)).Ok);
        keys.Remove(id);
    }

    [Fact]
    public async Task F8_NoFloodMakesRoomForAReplayAndOneDevicesFloodLeavesAnothersRequestsAlone()
    {
        // The reviewer's flood of 4,096 signatures, against a runtime that remembers 64 per device: nothing is evicted to make room.
        const int Capacity = 64;
        await using LocalNetworkHarness harness = await LocalNetworkHarness.StartAsync(replayCapacity: Capacity);
        RuntimeConnection laptop = harness.Pair(harness.Estate.BuilderId, "Laptop");
        RuntimeConnection phone = harness.Pair(harness.Estate.OperatorId, "Phone");
        DeviceRequestSigner signer = harness.Signer(laptop);
        DeviceRequestSigner other = harness.Signer(phone);
        using X509Certificate2 credential = LocalNetworkCredential.Issue(signer, Base64Url.DecodeFromChars(laptop.DevicePublicKey), DateTimeOffset.UtcNow);
        using X509Certificate2 otherCredential = LocalNetworkCredential.Issue(other, Base64Url.DecodeFromChars(phone.DevicePublicKey), DateTimeOffset.UtcNow);
        string pinned = harness.Host.Endpoint.Loopback!.Certificate;
        using var client = new MutualTls(harness.Address, credential, pinned);
        using var otherClient = new MutualTls(harness.Address, otherCredential, pinned);
        (string Device, string SignedAt, string Signature) captured = MutualTls.Sign(signer, "GET", "/v1/status");
        Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(HttpMethod.Get, "/v1/status", captured)).Status);

        // Another device fills its own window, and is refused beyond it; the laptop's captured signature is still remembered.
        for (int i = 0; i < Capacity; i++)
        {
            Assert.Equal(HttpStatusCode.OK, (await otherClient.SendSignedAsync(other, HttpMethod.Get, "/v1/health")).Status);
        }

        (HttpStatusCode full, string fullBody) = await otherClient.SendSignedAsync(other, HttpMethod.Get, "/v1/health");
        Assert.Equal(HttpStatusCode.Unauthorized, full);
        Assert.Contains("more requests in the last five minutes", Message(fullBody), StringComparison.Ordinal);
        Assert.Contains("already used", Message((await client.SendAsync(HttpMethod.Get, "/v1/status", captured)).Body), StringComparison.Ordinal);

        // The laptop's own flood fills its window and makes no room either: the replay stays refused, as does the next new request.
        for (int i = 1; i < Capacity; i++)
        {
            Assert.Equal(HttpStatusCode.OK, (await client.SendSignedAsync(signer, HttpMethod.Get, "/v1/health")).Status);
        }

        Assert.Contains("already used", Message((await client.SendAsync(HttpMethod.Get, "/v1/status", captured)).Body), StringComparison.Ordinal);
        Assert.Contains("more requests in the last five minutes", Message((await client.SendSignedAsync(signer, HttpMethod.Get, "/v1/health")).Body), StringComparison.Ordinal);
    }

    [Fact]
    public void F2_EveryRequestTypeOfTheProtocolIsHeldToAPrincipal()
    {
        // A request type an operation could bind that the scope does not know would be refused for every device; this keeps the table whole.
        Type[] requests = [.. typeof(Problem).Assembly.GetExportedTypes().Where(t => t.Name.EndsWith("Request", StringComparison.Ordinal) || t == typeof(PairingHello) || t == typeof(PairingConfirm))];
        Assert.NotEmpty(requests);
        Assert.All(requests, t => Assert.True(RequestScope.Covers(t), t.Name));
    }

    [Fact]
    public async Task Sound_ANonJsonContentTypeDoesNotCarryAnotherPrincipal()
    {
        await using LocalNetworkHarness harness = await LocalNetworkHarness.StartAsync();
        RuntimeConnection connection = harness.Pair(harness.Estate.BuilderId, "Agent laptop");
        DeviceRequestSigner signer = harness.Signer(connection);
        using X509Certificate2 credential = LocalNetworkCredential.Issue(signer, Base64Url.DecodeFromChars(connection.DevicePublicKey), DateTimeOffset.UtcNow);
        using var lan = new Lan(harness.Address, credential, harness.Host.Endpoint.Loopback!.Certificate);
        string json = "{\"type\":\"work-order\",\"by\":\"" + harness.Estate.OperatorId + "\",\"body\":" + Order("W-R4") + "}";
        foreach (string type in new[] { "text/plain", "application/x-www-form-urlencoded", "application/octet-stream", "application/vnd.api+json", "text/json", "application/json; charset=us-ascii", "application/json; charset=iso-8859-1" })
        {
            (HttpStatusCode status, string answer) = await lan.SendAsync(HttpMethod.Post, "/v1/records", signer, Encoding.UTF8.GetBytes(json), type);
            Say($"{type}: {(int)status} {(answer.Length > 160 ? answer[..160] : answer)}");
            Assert.NotEqual(HttpStatusCode.OK, status);
        }

        Assert.Null(harness.Socket.Brief("W-R4"));
    }

    [Fact]
    public async Task Sound_MethodAndPathVariantsNeverReachAnEndpointUnauthenticated()
    {
        await using LocalNetworkHarness harness = await LocalNetworkHarness.StartAsync();
        string pinned = harness.Host.Endpoint.Loopback!.Certificate;
        string[] heads =
        [
            "HEAD /v1/status HTTP/1.1",
            "OPTIONS /v1/status HTTP/1.1",
            "OPTIONS /v1/pairing/hello HTTP/1.1",
            "GET /v1/pairing/hello HTTP/1.1",
            "OPTIONS * HTTP/1.1",
            "GET /v1/nowhere HTTP/1.1",
            "POST /v1/pairing/hello/../../shutdown HTTP/1.1",
            "POST /v1/pairing/hello/..%2F..%2Fshutdown HTTP/1.1",
            "POST /v1/pairing/hello%2F..%2F..%2Fshutdown HTTP/1.1",
            "POST http://vericairn-runtime/v1/shutdown HTTP/1.1",
            "POST http://x/v1/shutdown HTTP/1.1",
            "POST http://x/v1/pairing/hello/../../shutdown HTTP/1.1",
            "POST /v1/pairing/confirm/../../runtime/certificate HTTP/1.1",
            "GET /v1/runtime/identity?nonce=AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA HTTP/1.1",
        ];
        foreach (string head in heads)
        {
            string answer = await Raw.ExchangeAsync(harness.Address, null, pinned, Encoding.ASCII.GetBytes(head + "\r\nHost: x\r\nContent-Length: 0\r\n\r\n"), TimeSpan.FromSeconds(3));
            string first = answer.Split("\r\n")[0];
            Say($"{head} -> {first}");
            Assert.True(first.StartsWith("HTTP/1.1 401", StringComparison.Ordinal) || first.StartsWith("HTTP/1.1 400", StringComparison.Ordinal), first);
        }

        // Case, a trailing slash and a percent-encoded letter still route to the pairing endpoint itself, which is the exemption's purpose.
        foreach (string head in new[] { "POST /V1/PAIRING/HELLO/ HTTP/1.1", "POST /v1/%70airing/hello HTTP/1.1" })
        {
            byte[] body = Encoding.UTF8.GetBytes("{\"handle\":\"ZZZZ\",\"nonce\":\"" + PairingProtocol.NewNonce() + "\"}");
            string answer = await Raw.ExchangeAsync(harness.Address, null, pinned, [.. Encoding.ASCII.GetBytes($"{head}\r\nHost: x\r\nContent-Type: application/json\r\nContent-Length: {body.Length}\r\n\r\n"), .. body], TimeSpan.FromSeconds(3));
            Say($"{head} -> {answer.Split("\r\n")[0]} {answer[(answer.IndexOf("\r\n\r\n", StringComparison.Ordinal) + 4)..]}");
            Assert.Contains("No pairing here has this code", answer, StringComparison.Ordinal);
        }

        Assert.Equal(harness.Estate.Estate.Id, harness.Socket.Health().Estate);
    }

    [Fact]
    public async Task Sound_MachineLocalHoldsAcrossCaseSlashDotAndQueryVariants()
    {
        await using LocalNetworkHarness harness = await LocalNetworkHarness.StartAsync();
        RuntimeConnection phone = harness.Pair(harness.Estate.OperatorId, "Operator phone");
        DeviceRequestSigner signer = harness.Signer(phone);
        using X509Certificate2 credential = LocalNetworkCredential.Issue(signer, Base64Url.DecodeFromChars(phone.DevicePublicKey), DateTimeOffset.UtcNow);
        string pinned = harness.Host.Endpoint.Loopback!.Certificate;
        foreach (string target in new[] { "/V1/SHUTDOWN/", "/v1/./shutdown", "/v1/shutdown?x=1", "/v1/records/../shutdown", "/v1/Runtime/Certificate", "/v1/PAIRINGS/" })
        {
            string head = Raw.SignedHead(signer, "POST", target, []);
            byte[] payload = Encoding.ASCII.GetBytes($"POST {target} HTTP/1.1\r\nHost: x\r\n{head}Content-Length: 0\r\n\r\n");
            string answer = await Raw.ExchangeAsync(harness.Address, credential, pinned, payload, TimeSpan.FromSeconds(3));
            Say($"POST {target} -> {answer.Split("\r\n")[0]}");
            Assert.StartsWith("HTTP/1.1 401", answer, StringComparison.Ordinal);
        }

        Assert.Equal(harness.Estate.Estate.Id, harness.Socket.Health().Estate);
    }

    [Fact]
    public async Task Sound_ARefusalClosesTheConnectionAndAPipelinedRequestIsNotServed()
    {
        await using LocalNetworkHarness harness = await LocalNetworkHarness.StartAsync();
        RuntimeConnection connection = harness.Pair(harness.Estate.BuilderId);
        DeviceRequestSigner signer = harness.Signer(connection);
        using X509Certificate2 credential = LocalNetworkCredential.Issue(signer, Base64Url.DecodeFromChars(connection.DevicePublicKey), DateTimeOffset.UtcNow);
        string pinned = harness.Host.Endpoint.Loopback!.Certificate;

        string payload = "GET /v1/status HTTP/1.1\r\nHost: x\r\n\r\n" + "GET /v1/health HTTP/1.1\r\nHost: x\r\n" + Raw.SignedHead(signer, "GET", "/v1/health", []) + "\r\n";
        string answer = await Raw.ExchangeAsync(harness.Address, credential, pinned, Encoding.ASCII.GetBytes(payload), TimeSpan.FromSeconds(3));
        int responses = answer.Split("HTTP/1.1 ").Length - 1;
        Say(answer);
        Assert.Equal(1, responses);
        Assert.StartsWith("HTTP/1.1 401", answer, StringComparison.Ordinal);
        Assert.DoesNotContain("<<timeout", answer, StringComparison.Ordinal);

        // An exempt pairing request answered 401 by the endpoint keeps the connection; what follows is still authenticated afresh.
        byte[] hello = Encoding.UTF8.GetBytes("{\"handle\":\"ZZZZ\",\"nonce\":\"" + PairingProtocol.NewNonce() + "\"}");
        string pipelined = $"POST /v1/pairing/hello HTTP/1.1\r\nHost: x\r\nContent-Type: application/json\r\nContent-Length: {hello.Length}\r\n\r\n{Encoding.UTF8.GetString(hello)}GET /v1/status HTTP/1.1\r\nHost: x\r\n\r\n";
        string second = await Raw.ExchangeAsync(harness.Address, null, pinned, Encoding.ASCII.GetBytes(pipelined), TimeSpan.FromSeconds(3));
        Say(second);
        Assert.Equal(2, second.Split("HTTP/1.1 401").Length - 1);
        Assert.Contains("paired device", second, StringComparison.Ordinal);
    }
}

/// <summary>A local-network client that sends exact bytes with an exact content type, signed over those bytes.</summary>
internal sealed class Lan : IDisposable
{
    private readonly HttpClient http;

    public Lan(string address, X509Certificate2? certificate, string runtimeCertificate)
    {
        Assert.True(RuntimeConnection.TryParseAddress(address, out string host, out int port));
        var handler = new SocketsHttpHandler
        {
            ConnectCallback = async (_, cancellationToken) =>
            {
                var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
                await socket.ConnectAsync(host, port, cancellationToken);
                var tls = new SslStream(new NetworkStream(socket, ownsSocket: true));
                await tls.AuthenticateAsClientAsync(
                    new SslClientAuthenticationOptions
                    {
                        TargetHost = "vericairn-runtime",
                        AllowTlsResume = false,
                        LocalCertificateSelectionCallback = (_, _, _, _, _) => certificate!,
                        RemoteCertificateValidationCallback = (_, presented, _, _) => presented is not null && RuntimeIdentityScheme.CertificateFingerprint(presented.GetRawCertData()) == runtimeCertificate,
                    },
                    cancellationToken);
                return tls;
            },
        };
        http = new HttpClient(handler, disposeHandler: true) { BaseAddress = new Uri($"http://{address}/") };
    }

    public async Task<(HttpStatusCode Status, string Body)> SendAsync(HttpMethod method, string target, IRequestSigner signer, byte[] body, string contentType)
    {
        using var request = new HttpRequestMessage(method, new Uri(target, UriKind.Relative)) { Content = new ByteArrayContent(body) };
        request.Content.Headers.TryAddWithoutValidation("Content-Type", contentType);
        string signedAt = RequestSigning.FormatTime(DateTimeOffset.UtcNow);
        request.Headers.TryAddWithoutValidation(RequestSigning.DeviceHeader, signer.DeviceId);
        request.Headers.TryAddWithoutValidation(RequestSigning.SignedAtHeader, signedAt);
        request.Headers.TryAddWithoutValidation(RequestSigning.SignatureHeader, Base64Url.EncodeToString(signer.Sign(RequestSigning.Digest(method.Method, target, signedAt, body))));
        using HttpResponseMessage response = await http.SendAsync(request, TestContext.Current.CancellationToken);
        return (response.StatusCode, await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    public void Dispose() => http.Dispose();
}

/// <summary>Raw bytes over mutual TLS: what a test writes is exactly what the runtime reads, with no client-side normalization.</summary>
internal static class Raw
{
    public static string SignedHead(IRequestSigner signer, string method, string target, byte[] body)
    {
        string at = RequestSigning.FormatTime(DateTimeOffset.UtcNow);
        string signature = Base64Url.EncodeToString(signer.Sign(RequestSigning.Digest(method, target, at, body)));
        return $"{RequestSigning.DeviceHeader}: {signer.DeviceId}\r\n{RequestSigning.SignedAtHeader}: {at}\r\n{RequestSigning.SignatureHeader}: {signature}\r\n";
    }

    public static async Task<string> ExchangeOfflineAsync(string address, X509Certificate2 certificate, string runtimeCertificate, byte[] payload, TimeSpan wait)
    {
        Assert.True(RuntimeConnection.TryParseAddress(address, out string host, out int port));
        using var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
        await socket.ConnectAsync(host, port);
        await using var tls = new SslStream(new NetworkStream(socket, ownsSocket: false));
        await tls.AuthenticateAsClientAsync(new SslClientAuthenticationOptions
        {
            TargetHost = "vericairn-runtime",
            AllowTlsResume = false,
            // The client never builds or fetches a chain for its own certificate: offline, with nothing to add.
            ClientCertificateContext = SslStreamCertificateContext.Create(certificate, null, offline: true),
            CertificateChainPolicy = new X509ChainPolicy { DisableCertificateDownloads = true, RevocationMode = X509RevocationMode.NoCheck },
            RemoteCertificateValidationCallback = (_, presented, _, _) => presented is not null && RuntimeIdentityScheme.CertificateFingerprint(presented.GetRawCertData()) == runtimeCertificate,
        });
        await tls.WriteAsync(payload);
        await tls.FlushAsync();
        using var buffer = new MemoryStream();
        byte[] chunk = new byte[8192];
        using var timeout = new CancellationTokenSource(wait);
        try
        {
            while (true)
            {
                int read = await tls.ReadAsync(chunk, timeout.Token);
                if (read == 0)
                {
                    break;
                }

                buffer.Write(chunk, 0, read);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (IOException)
        {
        }

        return Encoding.UTF8.GetString(buffer.ToArray());
    }

    public static async Task<string> ExchangeAsync(string address, X509Certificate2? certificate, string runtimeCertificate, byte[] payload, TimeSpan wait)
    {
        Assert.True(RuntimeConnection.TryParseAddress(address, out string host, out int port));
        using var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
        await socket.ConnectAsync(host, port);
        await using var tls = new SslStream(new NetworkStream(socket, ownsSocket: false));
        await tls.AuthenticateAsClientAsync(new SslClientAuthenticationOptions
        {
            TargetHost = "vericairn-runtime",
            AllowTlsResume = false,
            LocalCertificateSelectionCallback = (_, _, _, _, _) => certificate!,
            RemoteCertificateValidationCallback = (_, presented, _, _) => presented is not null && RuntimeIdentityScheme.CertificateFingerprint(presented.GetRawCertData()) == runtimeCertificate,
        });
        await tls.WriteAsync(payload);
        await tls.FlushAsync();
        using var buffer = new MemoryStream();
        byte[] chunk = new byte[8192];
        using var timeout = new CancellationTokenSource(wait);
        try
        {
            while (true)
            {
                int read = await tls.ReadAsync(chunk, timeout.Token);
                if (read == 0)
                {
                    break;
                }

                buffer.Write(chunk, 0, read);
            }
        }
        catch (OperationCanceledException)
        {
            buffer.Write("\n<<timeout: the connection stayed open>>"u8);
        }
        catch (IOException)
        {
            // Closed by the peer.
        }

        return Encoding.UTF8.GetString(buffer.ToArray());
    }
}
