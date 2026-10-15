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
        using var lan = new Lan(harness, credential, harness.Host.Endpoint.Loopback!.Certificate);
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
        using var lan = new Lan(harness, credential, harness.Host.Endpoint.Loopback!.Certificate);
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
        using var lan = new Lan(harness, credential, harness.Host.Endpoint.Loopback!.Certificate);
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
        using var client = new MutualTls(harness, credential, pinned);
        using var otherClient = new MutualTls(harness, otherCredential, pinned);
        (string Device, string SignedAt, string Signature) captured = client.Sign(signer, "GET", "/v1/status");
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
    public async Task F6_WithoutTheCodeNothingOfAPendingPairingIsLearntOrUsedAndAnAddressIsLimited()
    {
        await using LocalNetworkHarness harness = await LocalNetworkHarness.StartAsync();
        PairingIssued issued = harness.Issue(harness.Estate.OperatorId);
        using HttpClient pairing = PairingTests.PairingClient(harness);

        // The code is named nowhere: a hello that does not prove it gets the one answer an unknown code gets, naming nothing of the
        // estate or the principal, and its connection is closed; the reviewer's probe of the handle space has nothing to probe.
        for (int i = 0; i < PairingRegistry.MaxFailures; i++)
        {
            using HttpResponseMessage miss = await pairing.PostAsJsonAsync(new Uri("/v1/pairing/hello", UriKind.Relative), new PairingHello(PairingProtocol.NewNonce(), PairingProtocol.NewNonce()), ProtocolJson.Options, TestContext.Current.CancellationToken);
            string body = await miss.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.Unauthorized, miss.StatusCode);
            Assert.Contains("No pairing here matches this code", Message(body), StringComparison.Ordinal);
            Assert.DoesNotContain("Test Estate", body, StringComparison.Ordinal);
            Assert.DoesNotContain("Test Operator", body, StringComparison.Ordinal);
            Assert.True(miss.Headers.ConnectionClose == true);
        }

        // The address that sent them is refused for a minute, even with the right code; then the code pairs, none of its attempts used.
        PairingException limited = Assert.Throws<PairingException>(() => harness.Connections.Add(harness.Address, issued.Code, new PairingOptions("Phone")));
        Assert.Contains("Too many pairing attempts", limited.Message, StringComparison.Ordinal);
        harness.Time.Advance(PairingRegistry.FailureWindow);
        RuntimeConnection connection = harness.Connections.Add(harness.Address, issued.Code, new PairingOptions("Phone"));
        Assert.Equal("Test Estate", connection.EstateName);
        Assert.Equal("Test Operator", connection.PrincipalName);
    }

    [Fact]
    public async Task F15_ADevicesClaimsAreRecordedAsItsOwnStatementAndItsNameIsPlainText()
    {
        await using LocalNetworkHarness harness = await LocalNetworkHarness.StartAsync();
        PairingIssued issued = harness.Issue(harness.Estate.OperatorId);
        Assert.True(PairingProtocol.TryParse(issued.Code, out string secret));
        (_, byte[] deviceKey) = PairingProtocol.Keys(secret);
        using HttpClient pairing = PairingTests.PairingClient(harness);

        async Task<(HttpStatusCode Status, string Body, string Record)> Present(string name, string platform, string custody, string presence)
        {
            (PairingHello hello, PairingChallenge challenge) = await PairingTests.Hello(harness, pairing, issued.Code);
            byte[] spki = harness.DeviceKeys.Create(challenge.Record);
            var device = new PairingDevice(name, platform, custody, RecordSigning.EncodePublicKey(spki), presence);
            DateTimeOffset at = DateTimeOffset.ParseExact(challenge.At, "yyyy-MM-dd'T'HH:mm:ss'Z'", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal);
            var body = Records.Authoring.DeviceRecords.Body(challenge.Principal, device.Name, device.Platform, device.Custody, device.PublicKey, challenge.Profiles, challenge.Format, device.Presence);
            string hash = Records.Authoring.DeviceRecords.Hash(Records.Authoring.DeviceRecords.Draft(challenge.Principal, challenge.Record, at, challenge.Format, body));
            var confirm = new PairingConfirm(challenge.RuntimeNonce, device, Base64Url.EncodeToString(harness.DeviceKeys.Sign(challenge.Record, RecordSigning.Digest(hash))), string.Empty);
            confirm = confirm with { Mac = PairingProtocol.ConfirmMac(deviceKey, hello, challenge, confirm) };
            using HttpResponseMessage done = await pairing.PostAsJsonAsync(new Uri("/v1/pairing/confirm", UriKind.Relative), confirm, ProtocolJson.Options, TestContext.Current.CancellationToken);
            return (done.StatusCode, await done.Content.ReadAsStringAsync(TestContext.Current.CancellationToken), challenge.Record);
        }

        // A name that hides or reorders what a person reads is refused, and nothing is recorded.
        foreach (string name in new[] { "Phone\u0007", "\u202Eenohp", "Line\nbreak" })
        {
            (HttpStatusCode refused, string body, _) = await Present(name, "linux", "file-owner-only", "none");
            Assert.Equal(HttpStatusCode.UnprocessableEntity, refused);
            Assert.Contains("without control or formatting characters", Message(body), StringComparison.Ordinal);
        }

        Assert.Single(harness.Socket.Devices());

        // Custody, presence and platform cannot be verified from here: a key in a file, stated as a Secure Enclave key needing Touch ID on
        // linux, is recorded as stated, and its admission says that what the device stated is its own statement.
        (HttpStatusCode status, string answer, string record) = await Present("Claims an enclave", "linux", SecureEnclaveCustody.Kind, "required");
        Assert.True(status == HttpStatusCode.OK, answer);
        DeviceRow row = Assert.Single(harness.Socket.Devices(), d => d.Id == record);
        Assert.Equal((SecureEnclaveCustody.Kind, "required", "linux"), (row.Custody, row.Presence, row.Platform));
        RecordEnvelope admission = Assert.Single(harness.Estate.Load().About(record, "admission"));
        Assert.Contains("its own statement", EstateModel.BodyString(admission, "note"), StringComparison.Ordinal);
    }

    [Fact]
    public async Task F16_PairingAgainReplacesTheConnectionsAReplacedIdentityLeftStale()
    {
        await using LocalNetworkHarness harness = await LocalNetworkHarness.StartAsync();
        RuntimeConnection laptop = harness.Pair(harness.Estate.BuilderId, "Laptop");
        RuntimeConnection phone = harness.Pair(harness.Estate.OperatorId, "Phone");
        Assert.Equal(2, harness.Connections.List().Count);

        // Another principal's device on the same machine is a connection of its own; pairing the same principal again replaces its own.
        RuntimeConnection laptopAgain = harness.Pair(harness.Estate.BuilderId, "Laptop, paired again");
        Assert.Equal([phone.Id, laptopAgain.Id], harness.Connections.List().Select(c => c.Id).Order(StringComparer.Ordinal));
        Assert.Null(harness.Connections.Find(laptop.Id));

        // Once the runtime replaces its identity, pairing again replaces every connection still pinned to the old one.
        Assert.NotNull(harness.Socket.RotateCertificate(new CertificateRotateRequest(Identity: true)).Rotated);
        RuntimeConnection fresh = harness.Pair(harness.Estate.BuilderId, "Laptop, paired once more");
        RuntimeConnection only = Assert.Single(harness.Connections.List());
        Assert.Equal(fresh.Id, only.Id);
        Assert.Equal(harness.Host.IdentityPublicKey, only.Identity);
    }

    [Fact]
    public async Task F4_AnIdentityProofAnswersOnlyTheConnectionThatAskedForIt()
    {
        await using LocalNetworkHarness harness = await LocalNetworkHarness.StartAsync();
        RuntimeConnection insider = harness.Pair(harness.Estate.BuilderId, "Insider");
        RuntimeConnection victim = harness.Pair(harness.Estate.OperatorId, "Victim");
        DeviceRequestSigner signer = harness.Signer(insider);
        using X509Certificate2 credential = LocalNetworkCredential.Issue(signer, Base64Url.DecodeFromChars(insider.DevicePublicKey), DateTimeOffset.UtcNow);
        string pinned = harness.Host.Endpoint.Loopback!.Certificate;
        using var client = new MutualTls(harness, credential, pinned);

        // The insider asks, on its own connection, for a proof of the victim's nonce: the proof names the insider's client certificate,
        // so it verifies for the insider's connection and for no connection of the victim's.
        string victimNonce = PairingProtocol.NewNonce();
        (HttpStatusCode status, string body) = await client.SendSignedAsync(signer, HttpMethod.Get, RuntimeIdentityScheme.Path + "?nonce=" + victimNonce);
        Assert.Equal(HttpStatusCode.OK, status);
        RuntimeIdentityProof proof = JsonSerializer.Deserialize<RuntimeIdentityProof>(body, ProtocolJson.Options)!;
        string insiderClient = RuntimeIdentityScheme.CertificateFingerprint(credential.RawData);
        Assert.Equal(insiderClient, proof.Client);
        Assert.True(RuntimeIdentityScheme.Verify(proof, insider.Identity, insider.Estate, pinned, insiderClient, victimNonce));
        using X509Certificate2 victimCredential = LocalNetworkCredential.Issue(harness.Signer(victim), Base64Url.DecodeFromChars(victim.DevicePublicKey), DateTimeOffset.UtcNow);
        Assert.False(RuntimeIdentityScheme.Verify(proof, victim.Identity, victim.Estate, pinned, RuntimeIdentityScheme.CertificateFingerprint(victimCredential.RawData), victimNonce));
        Assert.Equal(ConnectionStates.Current, harness.Connections.Probe(victim).State);
    }

    [Fact]
    public async Task F4_NoProofIsGivenOverLoopbackOrTheLocalSocket()
    {
        await using LocalNetworkHarness harness = await LocalNetworkHarness.StartAsync();
        harness.Pair(harness.Estate.OperatorId, "Victim phone");
        WriteOutcome local = harness.Socket.DeviceCreate(new DeviceCreateRequest(harness.Estate.BuilderId, "Agent worker", "file"));
        Assert.True(harness.Socket.DeviceAdmit(new AdmitRequest(local.Written!.Id, harness.Estate.OperatorId, "Agent.")).Ok);
        using VericairnClient loopback = VericairnClient.Loopback(harness.Host.Endpoint, DeviceRequestSigner.Find(local.Written.Id)!);
        Assert.Null(loopback.RuntimeIdentity(PairingProtocol.NewNonce()));
        Assert.Null(harness.Socket.RuntimeIdentity(PairingProtocol.NewNonce()));
    }

    [Fact]
    public async Task F4_AProofIsRefusedOnAConnectionThatBeganUnderAReplacedCertificate()
    {
        await using LocalNetworkHarness harness = await LocalNetworkHarness.StartAsync();
        RuntimeConnection connection = harness.Pair(harness.Estate.BuilderId);
        DeviceRequestSigner signer = harness.Signer(connection);
        using X509Certificate2 credential = LocalNetworkCredential.Issue(signer, Base64Url.DecodeFromChars(connection.DevicePublicKey), DateTimeOffset.UtcNow);
        using var open = new MutualTls(harness, credential, harness.Host.Endpoint.Loopback!.Certificate);
        Assert.Equal(HttpStatusCode.OK, (await open.SendSignedAsync(signer, HttpMethod.Get, RuntimeIdentityScheme.Path + "?nonce=" + PairingProtocol.NewNonce())).Status);

        // A rotation that is not for a compromise leaves the connection open, but it proves nothing more under the old certificate.
        Assert.NotNull(harness.Socket.RotateCertificate(new CertificateRotateRequest()).Rotated);
        (HttpStatusCode status, string body) = await open.SendSignedAsync(signer, HttpMethod.Get, RuntimeIdentityScheme.Path + "?nonce=" + PairingProtocol.NewNonce());
        Assert.Equal(HttpStatusCode.Conflict, status);
        Assert.Contains("since been replaced", Message(body), StringComparison.Ordinal);
        Assert.Equal(1, open.Connections);
        Assert.Equal(HttpStatusCode.OK, (await open.SendSignedAsync(signer, HttpMethod.Get, "/v1/health")).Status);
    }

    [Fact]
    public async Task F4_ACompromiseClosesConnectionsStillInTheirHandshake()
    {
        await using LocalNetworkHarness harness = await LocalNetworkHarness.StartAsync();
        Assert.True(RuntimeConnection.TryParseAddress(harness.Address, out string host, out int port));

        // A connection accepted but not yet through TLS, as one that waits mid-handshake under the old certificate.
        using var waiting = new Socket(SocketType.Stream, ProtocolType.Tcp);
        await waiting.ConnectAsync(host, port, TestContext.Current.CancellationToken);
        await Task.Delay(300, TestContext.Current.CancellationToken);
        (CertificateRotated? rotated, Problem? problem) = harness.Socket.RotateCertificate(new CertificateRotateRequest(Compromised: true));
        Assert.True(rotated is not null, problem?.Message);
        Assert.True(rotated.Closed >= 1, $"closed {rotated.Closed}");

        // The runtime closed it at once, well before any handshake timeout.
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(5));
        int read;
        try
        {
            read = await waiting.ReceiveAsync(new byte[16], SocketFlags.None, timeout.Token);
        }
        catch (SocketException)
        {
            read = 0;
        }

        Assert.Equal(0, read);
    }

    [Fact]
    public async Task F5_AClientCertificatesAddressesAreNeverFetched()
    {
        await using LocalNetworkHarness harness = await LocalNetworkHarness.StartAsync();
        await using AddressListener addresses = AddressListener.Start();
        (X509Certificate2 addressed, X509Certificate2 plain) = AddressListener.Presentable(addresses.Port, "CN=" + harness.OperatorDevice, client: true);
        using X509Certificate2 named = addressed;
        using X509Certificate2 nameless = plain;

        // The runtime receives a certificate that names this listener as its issuer's, OCSP and CRL address. The local network speaks
        // TLS 1.2 only, so on every platform the runtime's screen reads it in the clear and refuses the handshake before the runtime's
        // TLS reads it. On Windows the client's own TLS holds a certificate that names nothing, and its stream sends the other in its
        // place, since Windows' TLS fetches the issuer of a certificate it is given to present. The listener answers every request at
        // once, so the system's trust service never pauses its fetches after one fails.
        bool windows = OperatingSystem.IsWindows();
        for (int i = 0; i < 12; i++)
        {
            (bool refused, bool sent, string answer) = await Raw.PresentAsync(harness.Address, windows ? nameless : named, windows ? named.RawData : null, harness.Host.Endpoint.Loopback!.Certificate);
            Assert.True(sent, $"handshake {i}: the client never sent its certificate");
            Assert.True(refused, $"handshake {i} was not refused: [{answer}]");
        }

        await Task.Delay(TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken);
        Assert.True(addresses.Requests.IsEmpty, $"This machine fetched what a client certificate named: {string.Join(", ", addresses.Requests)}");
    }

    [Fact]
    public async Task F5_ARuntimeCertificatesAddressesAreNeverFetched()
    {
        // Something answering at a runtime's address with a certificate that names an address: the device pairing with it refuses
        // the handshake before its TLS reads the certificate, and fetches nothing. On Windows the impostor's own TLS holds a
        // certificate with the same key that names nothing, and its stream sends the other in its place.
        await using AddressListener addresses = AddressListener.Start();
        (X509Certificate2 addressed, X509Certificate2 plain) = AddressListener.Presentable(addresses.Port, "CN=vericairn runtime", client: false);
        using X509Certificate2 named = addressed;
        using X509Certificate2 nameless = plain;
        bool windows = OperatingSystem.IsWindows();
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        CertificateSwap? swap = null;
        try
        {
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            Task serve = Task.Run(
                async () =>
                {
                    using TcpClient client = await listener.AcceptTcpClientAsync(TestContext.Current.CancellationToken);
                    swap = windows ? new CertificateSwap(client.GetStream(), named.RawData) : null;
                    await using var tls = new SslStream(swap ?? (Stream)client.GetStream());
                    try
                    {
                        await tls.AuthenticateAsServerAsync(new SslServerAuthenticationOptions { ServerCertificateContext = SslStreamCertificateContext.Create(windows ? nameless : named, null, offline: true), AllowTlsResume = false }, TestContext.Current.CancellationToken);
                        var buffer = new byte[1024];
                        _ = await tls.ReadAsync(buffer, TestContext.Current.CancellationToken);
                    }
                    catch (Exception exception) when (exception is IOException or System.Security.Authentication.AuthenticationException)
                    {
                    }
                },
                TestContext.Current.CancellationToken);
            PairingException refused = Assert.Throws<PairingException>(() => RuntimePairing.Pair($"127.0.0.1:{port}", PairingProtocol.NewCode(), new PairingOptions("Phone") { Timeout = TimeSpan.FromSeconds(10) }));
            Say($"{refused.Code}: {refused.Message}");
            Assert.Contains("names an address to fetch", refused.Message, StringComparison.Ordinal);
            await serve.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
            Assert.True(!windows || swap?.Swapped == true, "The impostor never sent its certificate.");
        }
        finally
        {
            listener.Stop();
        }

        await Task.Delay(TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken);
        Assert.True(addresses.Requests.IsEmpty, $"This machine fetched what a runtime's certificate named: {string.Join(", ", addresses.Requests)}");
    }

    [Fact]
    public async Task F5_TheLocalNetworkSpeaksTls12WithAesGcmOnly()
    {
        await using LocalNetworkHarness harness = await LocalNetworkHarness.StartAsync();
        string pinned = harness.Host.Endpoint.Loopback!.Certificate;
        Assert.True(RuntimeConnection.TryParseAddress(harness.Address, out string host, out int port));
        async Task<SslStream> HandshakeAsync(System.Security.Authentication.SslProtocols protocols)
        {
            var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
            await socket.ConnectAsync(host, port, TestContext.Current.CancellationToken);
            var tls = new SslStream(new NetworkStream(socket, ownsSocket: true));
            try
            {
                await tls.AuthenticateAsClientAsync(
                    new SslClientAuthenticationOptions
                    {
                        TargetHost = "vericairn-runtime",
                        AllowTlsResume = false,
                        EnabledSslProtocols = protocols,
                        RemoteCertificateValidationCallback = (_, presented, _, _) => presented is not null && RuntimeIdentityScheme.CertificateFingerprint(presented.GetRawCertData()) == pinned,
                    },
                    TestContext.Current.CancellationToken);
                return tls;
            }
            catch
            {
                await tls.DisposeAsync();
                throw;
            }
        }

        // A client that offers whatever its platform does is answered in TLS 1.2, with ECDHE, ECDSA and AES-GCM.
        await using (SslStream offered = await HandshakeAsync(System.Security.Authentication.SslProtocols.None))
        {
            Assert.Equal(System.Security.Authentication.SslProtocols.Tls12, offered.SslProtocol);
            Assert.Contains(offered.NegotiatedCipherSuite, new[] { TlsCipherSuite.TLS_ECDHE_ECDSA_WITH_AES_256_GCM_SHA384, TlsCipherSuite.TLS_ECDHE_ECDSA_WITH_AES_128_GCM_SHA256 });
        }

        // One that offers TLS 1.3 alone is refused, where its platform can offer it at all.
        await Assert.ThrowsAnyAsync<Exception>(async () => await (await HandshakeAsync(System.Security.Authentication.SslProtocols.Tls13)).DisposeAsync());
    }

    [Fact]
    public async Task F9_BeforeItsCertificateIsShownAClientLearnsNothingOfADevice()
    {
        await using LocalNetworkHarness harness = await LocalNetworkHarness.StartAsync();
        RuntimeConnection phone = harness.Pair(harness.Estate.BuilderId, "Phone");
        RuntimeConnection relay = harness.Pair(harness.Estate.BuilderId, "Relay only", "relay");
        Assert.True(harness.Socket.DeviceRevoke(new RevokeRequest(phone.Device, harness.Estate.OperatorId, "Lost.")).Ok);
        string pinned = harness.Host.Endpoint.Loopback!.Certificate;
        var answers = new List<string>();
        foreach (string device in new[] { harness.OperatorDevice, phone.Device, relay.Device, Records.Identity.Ulid.NewUlid().Value })
        {
            string at = RequestSigning.FormatTime(DateTimeOffset.UtcNow);
            string payload = $"GET /v1/status HTTP/1.1\r\nHost: x\r\n{RequestSigning.DeviceHeader}: {device}\r\n{RequestSigning.SignedAtHeader}: {at}\r\n{RequestSigning.SignatureHeader}: AAAA\r\n\r\n";
            string answer = await Raw.ExchangeAsync(harness.Address, null, pinned, Encoding.ASCII.GetBytes(payload), TimeSpan.FromSeconds(3));
            Assert.StartsWith("HTTP/1.1 401", answer, StringComparison.Ordinal);
            answers.Add(Message(answer[(answer.IndexOf("\r\n\r\n", StringComparison.Ordinal) + 4)..]));
        }

        // Admitted, revoked, not permitted the profile, or unknown: one answer, which names none of them.
        Assert.Single(answers.Distinct(StringComparer.Ordinal));
        Assert.Contains("pairs first", answers[0], StringComparison.Ordinal);
        Assert.DoesNotContain("revoked", answers[0], StringComparison.Ordinal);
    }

    [Fact]
    public async Task F10_TheRuntimeCertificateAndItsDirectoryAreOwnerOnlyFromTheStart()
    {
        await using LocalNetworkHarness harness = await LocalNetworkHarness.StartAsync();
        string live = Path.GetDirectoryName(DaemonHost.EndpointFilePath(harness.Estate.Root))!;
        if (!OperatingSystem.IsWindows())
        {
            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute, File.GetUnixFileMode(live));
            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(Path.Combine(live, RuntimeCertificate.FileName)));
        }

        // A temporary file an interrupted write left, readable by anyone, is removed and never written into.
        string directory = Path.Combine(harness.DeviceHome, "certificate");
        Directory.CreateDirectory(directory);
        string stale = Path.Combine(directory, RuntimeCertificate.FileName + ".tmp");
        File.WriteAllText(stale, "left by an interrupted write");
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute | UnixFileMode.GroupRead | UnixFileMode.GroupExecute | UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
            File.SetUnixFileMode(stale, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead | UnixFileMode.OtherRead);
        }

        using X509Certificate2 replaced = RuntimeCertificate.Replace(directory);
        Assert.True(replaced.HasPrivateKey);
        Assert.False(File.Exists(stale));
        if (!OperatingSystem.IsWindows())
        {
            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute, File.GetUnixFileMode(directory));
            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(Path.Combine(directory, RuntimeCertificate.FileName)));
        }

        // The key is in memory only where the platform's TLS allows it.
        Assert.Equal(OperatingSystem.IsLinux() ? X509KeyStorageFlags.EphemeralKeySet : X509KeyStorageFlags.DefaultKeySet, RuntimeCertificate.KeyStorage);
    }

    [Fact]
    public void F10_ACredentialIsDisposedWithItsKeyOnceItsSuccessorIsReplaced()
    {
        string home = Path.Combine(Path.GetTempPath(), "vericairn-credential-" + Records.Identity.Ulid.NewUlid().Value);
        try
        {
            var store = new FileKeyStore(home);
            string id = Records.Identity.Ulid.NewUlid().Value;
            byte[] publicKey = store.Create(id);
            var time = new ManualTime(DateTimeOffset.UtcNow);
            using var credential = new LocalNetworkCredential(new DeviceRequestSigner(store, id), publicKey, time);
            X509Certificate2 first = credential.Current();
            Assert.Same(first, credential.Current());

            // Renewed within its last hour: the one replaced stays usable for a handshake that began with it.
            time.Advance(TimeSpan.FromHours(23.5));
            X509Certificate2 second = credential.Current();
            Assert.NotSame(first, second);
            Assert.True(first.HasPrivateKey);

            // Renewed again: the first is disposed, and its key with it.
            time.Advance(TimeSpan.FromHours(23.5));
            X509Certificate2 third = credential.Current();
            Assert.Equal(IntPtr.Zero, first.Handle);
            Assert.NotEqual(IntPtr.Zero, second.Handle);
            credential.Dispose();
            Assert.Equal(IntPtr.Zero, second.Handle);
            Assert.Equal(IntPtr.Zero, third.Handle);
        }
        finally
        {
            Directory.Delete(home, recursive: true);
        }
    }

    [Fact]
    public void F12_OnlyAPrivateAddressIsTakenWithoutTheOperatorsLeave()
    {
        foreach (string address in new[] { "127.0.0.1", "127.8.9.10", "::1", "10.0.0.1", "172.16.0.1", "172.31.255.254", "192.168.1.20", "169.254.10.1", "fe80::1", "fd12:3456::1", "::ffff:192.168.1.20" })
        {
            Assert.True(RuntimeSettings.Exposure(IPAddress.Parse(address)) is null, address);
        }

        foreach (string address in new[] { "0.0.0.0", "::" })
        {
            Assert.Contains("wildcard", RuntimeSettings.Exposure(IPAddress.Parse(address)), StringComparison.Ordinal);
        }

        foreach (string address in new[] { "8.8.8.8", "172.32.0.1", "100.64.0.1", "192.169.0.1", "2001:db8::1", "::ffff:8.8.8.8" })
        {
            Assert.Contains("other networks", RuntimeSettings.Exposure(IPAddress.Parse(address)), StringComparison.Ordinal);
        }

        // Settings that name such an address without the operator's leave, as when edited by hand, are refused; with it, kept.
        Assert.Contains("--allow-public", Assert.Throws<InvalidOperationException>(() => new RuntimeSettings("0.0.0.0:7443").LocalNetworkEndPoint()).Message, StringComparison.Ordinal);
        Assert.Contains("--allow-public", Assert.Throws<InvalidOperationException>(() => new RuntimeSettings("8.8.8.8:7443").LocalNetworkEndPoint()).Message, StringComparison.Ordinal);
        Assert.Equal(new IPEndPoint(IPAddress.Any, 7443), new RuntimeSettings("0.0.0.0:7443", AllowPublic: true).LocalNetworkEndPoint());
        Assert.Equal(new IPEndPoint(IPAddress.Parse("192.168.1.20"), 7443), new RuntimeSettings("192.168.1.20:7443").LocalNetworkEndPoint());
    }

    [Fact]
    public async Task F12_TheDaemonDoesNotStartOnAWildcardItWasNotAllowed()
    {
        // runtime.json edited by hand to a wildcard address: the daemon refuses to start, before anything listens.
        using var estate = new Records.Tests.TestEstate(2);
        string live = Path.GetDirectoryName(DaemonHost.EndpointFilePath(estate.Root))!;
        Directory.CreateDirectory(live);
        File.WriteAllText(Path.Combine(live, RuntimeSettings.FileName), "{\"format\":\"vericairn.runtime-settings/1\",\"localNetwork\":\"0.0.0.0:0\"}\n");
        InvalidOperationException refused = await Assert.ThrowsAsync<InvalidOperationException>(() => DaemonHost.StartAsync(estate.Root, loopback: false, cancellationToken: TestContext.Current.CancellationToken));
        Assert.Contains("--allow-public", refused.Message, StringComparison.Ordinal);
        Assert.Null(DaemonHost.Discover(estate.Root));
        Assert.False(RuntimeSettings.Load(live).AllowPublic);
    }

    [Fact]
    public async Task F11_AClientCertificateNamesNoDevice()
    {
        await using LocalNetworkHarness harness = await LocalNetworkHarness.StartAsync();
        RuntimeConnection connection = harness.Pair(harness.Estate.BuilderId);
        DeviceRequestSigner signer = harness.Signer(connection);
        using X509Certificate2 credential = LocalNetworkCredential.Issue(signer, Base64Url.DecodeFromChars(connection.DevicePublicKey), DateTimeOffset.UtcNow);
        Assert.Equal(LocalNetworkCredential.SubjectName, credential.Subject);
        Assert.Equal(LocalNetworkCredential.SubjectName, credential.Issuer);
        Assert.DoesNotContain(connection.Device, Encoding.ASCII.GetString(credential.RawData), StringComparison.Ordinal);
        using var client = new MutualTls(harness, credential, harness.Host.Endpoint.Loopback!.Certificate);
        Assert.Equal(HttpStatusCode.OK, (await client.SendSignedAsync(signer, HttpMethod.Get, "/v1/health")).Status);
    }

    [Fact]
    public async Task F13_ADevelopmentEnvironmentShowsAClientNothingMore()
    {
        string? before = Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT");
        string? aspnet = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT");
        Environment.SetEnvironmentVariable("DOTNET_ENVIRONMENT", "Development");
        Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Development");
        try
        {
            await using LocalNetworkHarness harness = await LocalNetworkHarness.StartAsync();
            byte[] body = "{\"nonce\": [}"u8.ToArray();
            string answer = await Raw.ExchangeAsync(harness.Address, null, harness.Host.Endpoint.Loopback!.Certificate, [.. Encoding.ASCII.GetBytes($"POST /v1/pairing/hello HTTP/1.1\r\nHost: x\r\nContent-Type: application/json\r\nContent-Length: {body.Length}\r\n\r\n"), .. body], TimeSpan.FromSeconds(3));
            Say(answer.Length > 600 ? answer[..600] : answer);
            Assert.StartsWith("HTTP/1.1 400", answer, StringComparison.Ordinal);
            Assert.DoesNotContain("   at ", answer, StringComparison.Ordinal);
            Assert.DoesNotContain(".cs:line", answer, StringComparison.Ordinal);
            Assert.DoesNotContain("Exception", answer, StringComparison.Ordinal);
        }
        finally
        {
            Environment.SetEnvironmentVariable("DOTNET_ENVIRONMENT", before);
            Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", aspnet);
        }
    }

    [Fact]
    public async Task F14_TheSdkFollowsNoRedirect()
    {
        // A server on a local socket of its own that answers health with a redirect, and counts whoever follows it.
        int followed = 0;
        string socket = OperatingSystem.IsWindows() ? "vericairn-redirect-" + Records.Identity.Ulid.NewUlid().Value : Path.Combine(DaemonHost.SocketDirectory(), Records.Identity.Ulid.NewUlid().Value[^12..] + ".sock");
        Microsoft.AspNetCore.Builder.WebApplicationBuilder builder = Microsoft.AspNetCore.Builder.WebApplication.CreateSlimBuilder();
        Microsoft.AspNetCore.Hosting.WebHostBuilderKestrelExtensions.ConfigureKestrel(builder.WebHost, kestrel =>
        {
            if (OperatingSystem.IsWindows())
            {
                kestrel.ListenNamedPipe(socket);
            }
            else
            {
                kestrel.ListenUnixSocket(socket);
            }
        });
        await using Microsoft.AspNetCore.Builder.WebApplication app = builder.Build();
        Microsoft.AspNetCore.Builder.EndpointRouteBuilderExtensions.MapGet(app, "/v1/health", () => Microsoft.AspNetCore.Http.Results.Redirect("/elsewhere"));
        Microsoft.AspNetCore.Builder.EndpointRouteBuilderExtensions.MapGet(app, "/elsewhere", () =>
        {
            Interlocked.Increment(ref followed);
            return Microsoft.AspNetCore.Http.Results.Json(new HealthResponse("1", "x", "x", "x", "x", 2, "x", 0), ProtocolJson.Options);
        });
        await app.StartAsync(TestContext.Current.CancellationToken);
        try
        {
            using var client = new VericairnClient(new DaemonEndpoint(OperatingSystem.IsWindows() ? "pipe" : "unix", socket, 0, string.Empty, string.Empty, ApiVersion.Current));
            HttpRequestException refused = Assert.Throws<HttpRequestException>(() => client.Health());
            Assert.Equal(HttpStatusCode.Redirect, refused.StatusCode);
            Assert.Equal(0, Volatile.Read(ref followed));
        }
        finally
        {
            await app.StopAsync(TestContext.Current.CancellationToken);
            if (!OperatingSystem.IsWindows() && File.Exists(socket))
            {
                File.Delete(socket);
            }
        }
    }

    [Fact]
    public async Task F17_ALocalNetworkRequestIsSignedForThisEstateAndThisRuntimeOnly()
    {
        await using LocalNetworkHarness harness = await LocalNetworkHarness.StartAsync();
        RuntimeConnection connection = harness.Pair(harness.Estate.BuilderId);
        DeviceRequestSigner signer = harness.Signer(connection);
        using X509Certificate2 credential = LocalNetworkCredential.Issue(signer, Base64Url.DecodeFromChars(connection.DevicePublicKey), DateTimeOffset.UtcNow);
        using var client = new MutualTls(harness, credential, harness.Host.Endpoint.Loopback!.Certificate);
        (string estate, string identity) = harness.Binding;
        Assert.Equal(connection.Identity, identity);
        using var otherRuntime = System.Security.Cryptography.ECDsa.Create(System.Security.Cryptography.ECCurve.NamedCurves.nistP256);
        string otherIdentity = Base64Url.EncodeToString(otherRuntime.ExportSubjectPublicKeyInfo());
        string at = RequestSigning.FormatTime(DateTimeOffset.UtcNow);
        (string, string, string) Signed(byte[] digest) => (signer.DeviceId, at, Base64Url.EncodeToString(signer.Sign(digest)));

        // Signed as loopback signs (version 1), for another runtime of this estate, or for another estate: each is refused.
        foreach ((string, string, string) headers in new[]
        {
            Signed(RequestSigning.Digest("GET", "/v1/status", at, [])),
            Signed(RequestSigning.LocalNetworkDigest(estate, otherIdentity, "GET", "/v1/status", at, [])),
            Signed(RequestSigning.LocalNetworkDigest(Records.Identity.Ulid.NewUlid().Value, identity, "GET", "/v1/status", at, [])),
        })
        {
            (HttpStatusCode status, string body) = await client.SendAsync(HttpMethod.Get, "/v1/status", headers);
            Assert.Equal(HttpStatusCode.Unauthorized, status);
            Assert.Contains("does not verify", body, StringComparison.Ordinal);
        }

        // Signed for this estate and this runtime's identity, it is accepted, and the SDK signs so for the runtime it paired with.
        Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(HttpMethod.Get, "/v1/status", Signed(RequestSigning.LocalNetworkDigest(estate, identity, "GET", "/v1/status", at, [])))).Status);
        using (VericairnClient sdk = VericairnClient.LocalNetwork(connection, signer))
        {
            Assert.Equal(estate, sdk.Health().Estate);
        }

        // The identity proof's request alone is signed as on loopback: it comes before the runtime has proven which identity answers.
        string nonce = PairingProtocol.NewNonce();
        string proofTarget = RuntimeIdentityScheme.Path + "?nonce=" + nonce;
        (HttpStatusCode proofStatus, string proofBody) = await client.SendAsync(HttpMethod.Get, proofTarget, Signed(RequestSigning.Digest("GET", proofTarget, at, [])));
        Assert.Equal(HttpStatusCode.OK, proofStatus);
        Assert.Equal(nonce, JsonSerializer.Deserialize<RuntimeIdentityProof>(proofBody, ProtocolJson.Options)!.Nonce);

        // Loopback keeps version 1, as API v1 clients sign it.
        using VericairnClient loopback = VericairnClient.Loopback(harness.Host.Endpoint, DeviceRequestSigner.Find(harness.OperatorDevice)!);
        Assert.Equal(estate, loopback.Health().Estate);
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
        using var lan = new Lan(harness, credential, harness.Host.Endpoint.Loopback!.Certificate);
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
            byte[] body = Encoding.UTF8.GetBytes("{\"nonce\":\"" + PairingProtocol.NewNonce() + "\",\"mac\":\"" + PairingProtocol.NewNonce() + "\"}");
            string answer = await Raw.ExchangeAsync(harness.Address, null, pinned, [.. Encoding.ASCII.GetBytes($"{head}\r\nHost: x\r\nContent-Type: application/json\r\nContent-Length: {body.Length}\r\n\r\n"), .. body], TimeSpan.FromSeconds(3));
            Say($"{head} -> {answer.Split("\r\n")[0]} {answer[(answer.IndexOf("\r\n\r\n", StringComparison.Ordinal) + 4)..]}");
            Assert.Contains("No pairing here matches this code", answer, StringComparison.Ordinal);
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
            string head = Raw.SignedHead(harness, signer, "POST", target, []);
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

        string payload = "GET /v1/status HTTP/1.1\r\nHost: x\r\n\r\n" + "GET /v1/health HTTP/1.1\r\nHost: x\r\n" + Raw.SignedHead(harness, signer, "GET", "/v1/health", []) + "\r\n";
        string answer = await Raw.ExchangeAsync(harness.Address, credential, pinned, Encoding.ASCII.GetBytes(payload), TimeSpan.FromSeconds(3));
        int responses = answer.Split("HTTP/1.1 ").Length - 1;
        Say(answer);
        Assert.Equal(1, responses);
        Assert.StartsWith("HTTP/1.1 401", answer, StringComparison.Ordinal);
        Assert.DoesNotContain("<<timeout", answer, StringComparison.Ordinal);

        // A pairing request the endpoint refuses closes the connection too (F6): what was pipelined after it is not served.
        byte[] hello = Encoding.UTF8.GetBytes("{\"nonce\":\"" + PairingProtocol.NewNonce() + "\",\"mac\":\"" + PairingProtocol.NewNonce() + "\"}");
        string pipelined = $"POST /v1/pairing/hello HTTP/1.1\r\nHost: x\r\nContent-Type: application/json\r\nContent-Length: {hello.Length}\r\n\r\n{Encoding.UTF8.GetString(hello)}GET /v1/status HTTP/1.1\r\nHost: x\r\n\r\n";
        string second = await Raw.ExchangeAsync(harness.Address, null, pinned, Encoding.ASCII.GetBytes(pipelined), TimeSpan.FromSeconds(3));
        Say(second);
        Assert.Equal(1, second.Split("HTTP/1.1 ").Length - 1);
        Assert.StartsWith("HTTP/1.1 401", second, StringComparison.Ordinal);
        Assert.Contains("No pairing here matches this code", second, StringComparison.Ordinal);
        Assert.DoesNotContain("<<timeout", second, StringComparison.Ordinal);
    }
}

/// <summary>A local-network client that sends exact bytes with an exact content type, signed over those bytes.</summary>
internal sealed class Lan : IDisposable
{
    private readonly HttpClient http;
    private readonly LocalNetworkHarness harness;

    public Lan(LocalNetworkHarness harness, X509Certificate2? certificate, string runtimeCertificate)
    {
        this.harness = harness;
        string address = harness.Address;
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
        request.Headers.TryAddWithoutValidation(RequestSigning.SignatureHeader, Base64Url.EncodeToString(signer.Sign(harness.Digest(method.Method, target, signedAt, body))));
        using HttpResponseMessage response = await http.SendAsync(request, TestContext.Current.CancellationToken);
        return (response.StatusCode, await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    public void Dispose() => http.Dispose();
}

/// <summary>Raw bytes over mutual TLS: what a test writes is exactly what the runtime reads, with no client-side normalization.</summary>
internal static class Raw
{
    public static string SignedHead(LocalNetworkHarness harness, IRequestSigner signer, string method, string target, byte[] body)
    {
        string at = RequestSigning.FormatTime(DateTimeOffset.UtcNow);
        string signature = Base64Url.EncodeToString(signer.Sign(harness.Digest(method, target, at, body)));
        return $"{RequestSigning.DeviceHeader}: {signer.DeviceId}\r\n{RequestSigning.SignedAtHeader}: {at}\r\n{RequestSigning.SignatureHeader}: {signature}\r\n";
    }

    /// <summary>
    /// A handshake whose client holds a certificate and, when <paramref name="sent"/> is given, sends that certificate in its place
    /// through <see cref="CertificateSwap"/>: whether the runtime refused it, whether the runtime was sent the certificate meant for
    /// it, and what was answered when the handshake was not refused.
    /// </summary>
    public static async Task<(bool Refused, bool Sent, string Answer)> PresentAsync(string address, X509Certificate2 held, byte[]? sent, string runtimeCertificate)
    {
        Assert.True(RuntimeConnection.TryParseAddress(address, out string host, out int port));
        using var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
        await socket.ConnectAsync(host, port);
        var stream = new NetworkStream(socket, ownsSocket: false);
        CertificateSwap? swap = sent is null ? null : new CertificateSwap(stream, sent);
        await using var tls = new SslStream(swap ?? (Stream)stream);
        try
        {
            await tls.AuthenticateAsClientAsync(new SslClientAuthenticationOptions
            {
                TargetHost = "vericairn-runtime",
                AllowTlsResume = false,
                ClientCertificateContext = SslStreamCertificateContext.Create(held, null, offline: true),
                CertificateChainPolicy = new X509ChainPolicy { DisableCertificateDownloads = true, RevocationMode = X509RevocationMode.NoCheck },
                RemoteCertificateValidationCallback = (_, presented, _, _) => presented is not null && RuntimeIdentityScheme.CertificateFingerprint(presented.GetRawCertData()) == runtimeCertificate,
            });
        }
        catch (Exception exception) when (exception is IOException or System.Security.Authentication.AuthenticationException)
        {
            return (true, swap?.Swapped ?? true, exception.Message);
        }

        await tls.WriteAsync(Encoding.ASCII.GetBytes("GET /v1/health HTTP/1.1\r\nHost: x\r\nConnection: close\r\n\r\n"));
        await tls.FlushAsync();
        using var reader = new StreamReader(tls, Encoding.UTF8);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        string answer;
        try
        {
            answer = await reader.ReadToEndAsync(timeout.Token);
        }
        catch (Exception exception) when (exception is IOException or OperationCanceledException)
        {
            answer = exception.Message;
        }

        return (false, swap?.Swapped ?? true, answer);
    }

    /// <summary>
    /// Writes exact bytes over TLS and reads the answer until the runtime closes the connection or the wait ends. After the payload,
    /// <paramref name="meanwhile"/> runs once the runtime has had time to read it, and then <paramref name="trickle"/> follows, a chunk
    /// at a time with a pause before each.
    /// </summary>
    public static async Task<string> ExchangeAsync(string address, X509Certificate2? certificate, string runtimeCertificate, byte[] payload, TimeSpan wait, byte[]? trickle = null, int chunkSize = 0, TimeSpan delay = default, Action? meanwhile = null)
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
        if (meanwhile is not null)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(500));
            meanwhile();
        }

        if (trickle is not null)
        {
            // The runtime may answer and close before the trickle ends; what it answered is read below.
            int size = chunkSize > 0 ? chunkSize : Math.Max(1, trickle.Length);
            try
            {
                for (int sent = 0; sent < trickle.Length; sent += size)
                {
                    if (delay > TimeSpan.Zero)
                    {
                        await Task.Delay(delay);
                    }

                    await tls.WriteAsync(trickle.AsMemory(sent, Math.Min(size, trickle.Length - sent)));
                    await tls.FlushAsync();
                }
            }
            catch (IOException)
            {
            }
        }

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

/// <summary>
/// An address a certificate names, on 127.0.0.1: it answers every request at once with 404 and keeps its request line, so a test
/// sees any fetch, and the system's trust service, which pauses after a fetch fails, keeps fetching.
/// </summary>
internal sealed class AddressListener : IAsyncDisposable
{
    private readonly TcpListener listener;
    private readonly Task accepting;

    private AddressListener(TcpListener listener)
    {
        this.listener = listener;
        accepting = Task.Run(AcceptAsync);
    }

    public int Port => ((IPEndPoint)listener.LocalEndpoint).Port;

    public System.Collections.Concurrent.ConcurrentQueue<string> Requests { get; } = new();

    public static AddressListener Start()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return new AddressListener(listener);
    }

    /// <summary>
    /// Two certificates with one key, each with it: one issued by a CA nobody holds, whose authority information access and CRL
    /// distribution points name this listener, and one that names nothing, self-signed, which a test's own TLS holds on Windows
    /// while a <see cref="CertificateSwap"/> sends the first in its place.
    /// </summary>
    public static (X509Certificate2 Addressed, X509Certificate2 Plain) Presentable(int port, string subject, bool client)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        var usage = new System.Security.Cryptography.Oid(client ? "1.3.6.1.5.5.7.3.2" : "1.3.6.1.5.5.7.3.1");
        using var caKey = System.Security.Cryptography.ECDsa.Create(System.Security.Cryptography.ECCurve.NamedCurves.nistP256);
        var caRequest = new CertificateRequest("CN=Review CA " + port.ToString(System.Globalization.CultureInfo.InvariantCulture), caKey, System.Security.Cryptography.HashAlgorithmName.SHA256);
        caRequest.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
        using X509Certificate2 ca = caRequest.CreateSelfSigned(now.AddHours(-1), now.AddDays(1));
        using var key = System.Security.Cryptography.ECDsa.Create(System.Security.Cryptography.ECCurve.NamedCurves.nistP256);
        var addressedRequest = new CertificateRequest(subject, key, System.Security.Cryptography.HashAlgorithmName.SHA256);
        addressedRequest.CertificateExtensions.Add(new X509AuthorityInformationAccessExtension([$"http://127.0.0.1:{port}/ocsp"], [$"http://127.0.0.1:{port}/ca.cer"]));
        addressedRequest.CertificateExtensions.Add(CertificateRevocationListBuilder.BuildCrlDistributionPointExtension([$"http://127.0.0.1:{port}/crl"]));
        addressedRequest.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension([usage], false));
        using X509Certificate2 addressed = addressedRequest.Create(ca, now.AddHours(-1), now.AddDays(1), [1, 2, 3, 4]);
        using X509Certificate2 addressedWithKey = addressed.CopyWithPrivateKey(key);
        var plainRequest = new CertificateRequest(subject, key, System.Security.Cryptography.HashAlgorithmName.SHA256);
        plainRequest.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension([usage], false));
        using X509Certificate2 plain = plainRequest.CreateSelfSigned(now.AddHours(-1), now.AddDays(1));
        return (X509CertificateLoader.LoadPkcs12(addressedWithKey.Export(X509ContentType.Pkcs12), null), X509CertificateLoader.LoadPkcs12(plain.Export(X509ContentType.Pkcs12), null));
    }

    public async ValueTask DisposeAsync()
    {
        listener.Stop();
        try
        {
            await accepting;
        }
        catch (Exception exception) when (exception is SocketException or ObjectDisposedException or OperationCanceledException)
        {
        }
    }

    private async Task AcceptAsync()
    {
        while (true)
        {
            TcpClient client;
            try
            {
                client = await listener.AcceptTcpClientAsync();
            }
            catch (Exception exception) when (exception is SocketException or ObjectDisposedException or InvalidOperationException)
            {
                return;
            }

            _ = Task.Run(async () =>
            {
                using (client)
                {
                    var buffer = new byte[4096];
                    int read = 0;
                    try
                    {
                        using var wait = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                        read = await client.GetStream().ReadAsync(buffer, wait.Token);
                        Requests.Enqueue(Encoding.ASCII.GetString(buffer, 0, read).Split("\r\n")[0]);
                        await client.GetStream().WriteAsync(Encoding.ASCII.GetBytes("HTTP/1.1 404 Not Found\r\nContent-Length: 0\r\nConnection: close\r\n\r\n"), wait.Token);
                    }
                    catch (Exception exception) when (exception is IOException or SocketException or OperationCanceledException)
                    {
                        Requests.Enqueue("(connected)");
                    }
                }
            });
        }
    }
}

/// <summary>
/// A test endpoint's stream that swaps the certificate its TLS 1.2 endpoint presents for another on the wire. The endpoint's own
/// TLS holds a certificate that names nothing, and the other end receives the one given here: Windows' TLS builds the chain of a
/// certificate it is given to present, fetching the issuer the certificate names, so an endpoint holding a certificate that names
/// an address would fetch it itself before the other end saw anything. What it reads passes through unchanged.
/// </summary>
internal sealed class CertificateSwap(Stream inner, byte[] sent) : Stream
{
    private const int MaxFragment = 1 << 14;

    private readonly List<byte> records = [];
    private readonly List<byte> messages = [];
    private bool clear = true;

    /// <summary>Whether a certificate was swapped.</summary>
    public bool Swapped { get; private set; }

    public override bool CanRead => true;

    public override bool CanWrite => true;

    public override bool CanSeek => false;

    public override long Length => throw new NotSupportedException();

    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, count);

    public override int Read(Span<byte> buffer) => inner.Read(buffer);

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) => inner.ReadAsync(buffer, offset, count, cancellationToken);

    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) => inner.ReadAsync(buffer, cancellationToken);

    public override void Write(byte[] buffer, int offset, int count) => inner.Write(Swap(buffer.AsSpan(offset, count)));

    public override void Write(ReadOnlySpan<byte> buffer) => inner.Write(Swap(buffer));

    public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) => inner.WriteAsync(Swap(buffer.AsSpan(offset, count)), cancellationToken).AsTask();

    public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default) => inner.WriteAsync(Swap(buffer.Span), cancellationToken);

    public override void Flush() => inner.Flush();

    public override Task FlushAsync(CancellationToken cancellationToken) => inner.FlushAsync(cancellationToken);

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            inner.Dispose();
        }

        base.Dispose(disposing);
    }

    private static byte[] Length24(int length) => [(byte)(length >> 16), (byte)(length >> 8), (byte)length];

    /// <summary>Rewrites what the endpoint writes until its change of cipher spec: whole handshake messages, each Certificate replaced.</summary>
    private byte[] Swap(ReadOnlySpan<byte> written)
    {
        if (!clear && records.Count == 0)
        {
            return written.ToArray();
        }

        records.AddRange(written.ToArray());
        var output = new List<byte>();
        while (records.Count >= 5)
        {
            if (!clear)
            {
                output.AddRange(records);
                records.Clear();
                break;
            }

            int length = (records[3] << 8) | records[4];
            if (records.Count < 5 + length)
            {
                break;
            }

            byte[] header = [.. records.GetRange(0, 5)];
            byte[] fragment = [.. records.GetRange(5, length)];
            records.RemoveRange(0, 5 + length);
            if (header[0] != 22)
            {
                clear = header[0] != 20;
                output.AddRange(header);
                output.AddRange(fragment);
                continue;
            }

            messages.AddRange(fragment);
            var whole = new List<byte>();
            while (messages.Count >= 4)
            {
                int size = (messages[1] << 16) | (messages[2] << 8) | messages[3];
                if (messages.Count < 4 + size)
                {
                    break;
                }

                byte[] message = [.. messages.GetRange(0, 4 + size)];
                messages.RemoveRange(0, 4 + size);
                if (message[0] == 11)
                {
                    // TLS 1.2: certificate_list<0..2^24-1> of ASN.1Cert<1..2^24-1>, here the one certificate given.
                    message = [11, .. Length24(6 + sent.Length), .. Length24(3 + sent.Length), .. Length24(sent.Length), .. sent];
                    Swapped = true;
                }

                whole.AddRange(message);
            }

            for (int at = 0; at < whole.Count; at += MaxFragment)
            {
                int count = Math.Min(MaxFragment, whole.Count - at);
                output.AddRange([22, header[1], header[2], (byte)(count >> 8), (byte)count]);
                output.AddRange(whole.GetRange(at, count));
            }
        }

        return [.. output];
    }
}
