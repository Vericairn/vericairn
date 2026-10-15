// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Buffers.Text;
using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Vericairn.Protocol;
using Vericairn.Records.Estate;
using Vericairn.Records.Signing;
using Vericairn.Runtime.Providers;
using Vericairn.Runtime.Service;
using Vericairn.Runtime.Tests;
using Vericairn.Runtime.Workers;
using Vericairn.Sdk;

namespace Vericairn.Daemon.Tests;

/// <summary>
/// The second round of review of ID-02, held: each test named for a finding of the second review (C1 to C6) or of the original
/// reviewer's re-review (N1 to N5) asserts the behaviour that fixed it. They listen on 127.0.0.1 only.
/// </summary>
[Collection("daemon")]
public class SecondReviewTests
{
    public SecondReviewTests()
    {
        DaemonTestHome.Ensure();
    }

    private static void Say(string text) => TestContext.Current.TestOutputHelper?.WriteLine(text);

    private static string Order(string key) =>
        "{\"key\":\"" + key + "\",\"kind\":\"ordinary\",\"title\":\"Review\",\"intent\":\"Probe.\",\"scope\":[\"x\"],\"exitQuestion\":\"?\",\"inputs\":[],\"deliverables\":[\"y\"]}";

    /// <summary>A request's head, signed at a time with a digest, for a JSON body, asking the runtime to close the connection after its answer.</summary>
    private static byte[] Head(string method, string target, byte[] body, string signedAt, DeviceRequestSigner signer, byte[] digest, string contentType = "application/json") =>
        Encoding.ASCII.GetBytes(
            $"{method} {target} HTTP/1.1\r\nHost: x\r\nContent-Type: {contentType}\r\nContent-Length: {body.Length.ToString(CultureInfo.InvariantCulture)}\r\nConnection: close\r\n"
            + $"{RequestSigning.DeviceHeader}: {signer.DeviceId}\r\n{RequestSigning.SignedAtHeader}: {signedAt}\r\n{RequestSigning.SignatureHeader}: {Base64Url.EncodeToString(signer.Sign(digest))}\r\n\r\n");

    private static string LoopbackAddress(LocalNetworkHarness harness) => $"{harness.Host.Endpoint.Loopback!.Host}:{harness.Host.Endpoint.Loopback.Port}";

    [Fact]
    public async Task C1_ABodyThatArrivesAfterItsSignatureIsStaleIsRefusedAndNeverReplaysTheWrite()
    {
        await using LocalNetworkHarness harness = await LocalNetworkHarness.StartAsync();
        RuntimeConnection connection = harness.Pair(harness.Estate.BuilderId, "Agent laptop");
        DeviceRequestSigner signer = harness.Signer(connection);
        using X509Certificate2 credential = LocalNetworkCredential.Issue(signer, Base64Url.DecodeFromChars(connection.DevicePublicKey), DateTimeOffset.UtcNow);
        string pinned = harness.Host.Endpoint.Loopback!.Certificate;

        // One signed write: its first copy is accepted at once.
        byte[] body = Encoding.UTF8.GetBytes("{\"type\":\"work-order\",\"by\":\"" + harness.Estate.BuilderId + "\",\"body\":" + Order("W-C1") + "}");
        string at = RequestSigning.FormatTime(harness.Time.GetUtcNow());
        byte[] head = Head("POST", "/v1/records", body, at, signer, harness.Digest("POST", "/v1/records", at, body));
        Assert.StartsWith("HTTP/1.1 200", await Raw.ExchangeAsync(harness.Address, credential, pinned, [.. head, .. body], TimeSpan.FromSeconds(5)), StringComparison.Ordinal);

        // A copy whose head arrives within its five minutes and whose body after them, on the runtime's clock: refused as stale, and
        // never accepted as a new write once what remembered the first copy could have forgotten it.
        string late = await Raw.ExchangeAsync(harness.Address, credential, pinned, [.. head, body[0]], TimeSpan.FromSeconds(5), trickle: body[1..], meanwhile: () => harness.Time.Advance(TimeSpan.FromMinutes(6)));
        Say(late);
        Assert.StartsWith("HTTP/1.1 401", late, StringComparison.Ordinal);
        Assert.Contains("five minutes", late, StringComparison.Ordinal);
        Assert.Single(harness.Socket.Records("work-order"), r => r.Envelope.GetProperty("body").GetProperty("key").GetString() == "W-C1");

        // A device revoked while its body arrives is refused once the body is in: the device is read from the records again.
        byte[] second = Encoding.UTF8.GetBytes("{\"type\":\"work-order\",\"by\":\"" + harness.Estate.BuilderId + "\",\"body\":" + Order("W-C1B") + "}");
        at = RequestSigning.FormatTime(harness.Time.GetUtcNow());
        head = Head("POST", "/v1/records", second, at, signer, harness.Digest("POST", "/v1/records", at, second));
        string revoked = await Raw.ExchangeAsync(harness.Address, credential, pinned, [.. head, second[0]], TimeSpan.FromSeconds(5), trickle: second[1..], meanwhile: () => Assert.True(harness.Socket.DeviceRevoke(new RevokeRequest(connection.Device, harness.Estate.OperatorId, "Lost while it wrote.")).Ok));
        Say(revoked);
        Assert.StartsWith("HTTP/1.1 401", revoked, StringComparison.Ordinal);
        Assert.Contains("revoked", revoked, StringComparison.Ordinal);
        Assert.DoesNotContain(harness.Socket.Records("work-order"), r => r.Envelope.GetProperty("body").GetProperty("key").GetString() == "W-C1B");

        // On loopback too, a body that arrives after the signature is stale is refused.
        DeviceRequestSigner operatorDevice = DeviceRequestSigner.Find(harness.OperatorDevice)!;
        byte[] third = Encoding.UTF8.GetBytes("{\"type\":\"work-order\",\"by\":\"" + harness.Estate.OperatorId + "\",\"body\":" + Order("W-C1C") + "}");
        at = RequestSigning.FormatTime(harness.Time.GetUtcNow());
        head = Head("POST", "/v1/records", third, at, operatorDevice, RequestSigning.Digest("POST", "/v1/records", at, third));
        string loopback = await Raw.ExchangeAsync(LoopbackAddress(harness), null, pinned, [.. head, third[0]], TimeSpan.FromSeconds(5), trickle: third[1..], meanwhile: () => harness.Time.Advance(TimeSpan.FromMinutes(6)));
        Say(loopback);
        Assert.StartsWith("HTTP/1.1 401", loopback, StringComparison.Ordinal);
        Assert.Contains("five minutes", loopback, StringComparison.Ordinal);
        Assert.DoesNotContain(harness.Socket.Records("work-order"), r => r.Envelope.GetProperty("body").GetProperty("key").GetString() == "W-C1C");
    }

    [Fact]
    public void C1_TheReplayCacheNeverAcceptsAStaleSignature()
    {
        var cache = new ReplayCache(10);
        DateTimeOffset now = DateTimeOffset.UtcNow;
        Assert.Equal(ReplayCache.Outcome.Stale, cache.Accept("device", "first", now.AddSeconds(-1), now));
        Assert.Equal(ReplayCache.Outcome.Accepted, cache.Accept("device", "second", now.AddMinutes(5), now));
        Assert.Equal(ReplayCache.Outcome.Repeated, cache.Accept("device", "second", now.AddMinutes(5), now.AddMinutes(4)));
        Assert.Equal(ReplayCache.Outcome.Stale, cache.Accept("device", "second", now.AddMinutes(5), now.AddMinutes(6)));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task N1_AReplayWhoseBodyArrivesAfterItsSignatureIsStaleIsRefused(bool loopback)
    {
        // The re-review's probe, on the system clock: a signature five seconds from stale, accepted once, then replayed with its body
        // trickled until after the signature is stale.
        await using LocalNetworkHarness harness = await LocalNetworkHarness.StartAsync(systemClock: true);
        RuntimeConnection connection = harness.Pair(harness.Estate.BuilderId);
        DeviceRequestSigner signer = loopback ? DeviceRequestSigner.Find(harness.OperatorDevice)! : harness.Signer(connection);
        string address = loopback ? LoopbackAddress(harness) : harness.Address;
        using X509Certificate2? credential = loopback ? null : LocalNetworkCredential.Issue(signer, Base64Url.DecodeFromChars(connection.DevicePublicKey), DateTimeOffset.UtcNow);
        string pinned = harness.Host.Endpoint.Loopback!.Certificate;
        byte[] body = Encoding.ASCII.GetBytes(new string('a', 3600));
        string at = RequestSigning.FormatTime(DateTimeOffset.UtcNow.AddSeconds(-295));
        byte[] digest = loopback ? RequestSigning.Digest("GET", "/v1/status", at, body) : harness.Digest("GET", "/v1/status", at, body);
        byte[] head = Head("GET", "/v1/status", body, at, signer, digest, "text/plain");
        Assert.StartsWith("HTTP/1.1 200", await Raw.ExchangeAsync(address, credential, pinned, [.. head, .. body], TimeSpan.FromSeconds(5)), StringComparison.Ordinal);

        string replay = await Raw.ExchangeAsync(address, credential, pinned, head, TimeSpan.FromSeconds(15), trickle: body, chunkSize: 360, delay: TimeSpan.FromSeconds(1));
        Say(replay);
        Assert.StartsWith("HTTP/1.1 401", replay, StringComparison.Ordinal);
        Assert.Contains("five minutes", replay, StringComparison.Ordinal);
        Assert.Contains("Connection: close", replay, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task InfoB_ABodyThatIsNotJsonPassesAsBytesInWhateverCharsetItDeclares()
    {
        // An object's bytes over loopback, declared text in Latin-1: stored as the bytes they are. A JSON body in another charset is
        // still refused (F1).
        await using LocalNetworkHarness harness = await LocalNetworkHarness.StartAsync();
        DeviceRequestSigner operatorDevice = DeviceRequestSigner.Find(harness.OperatorDevice)!;
        string pinned = harness.Host.Endpoint.Loopback!.Certificate;
        byte[] bytes = Encoding.Latin1.GetBytes("café au lait");
        string at = RequestSigning.FormatTime(DateTimeOffset.UtcNow);
        byte[] head = Head("POST", "/v1/objects", bytes, at, operatorDevice, RequestSigning.Digest("POST", "/v1/objects", at, bytes), "text/plain; charset=iso-8859-1");
        string stored = await Raw.ExchangeAsync(LoopbackAddress(harness), null, pinned, [.. head, .. bytes], TimeSpan.FromSeconds(5));
        Say(stored);
        Assert.StartsWith("HTTP/1.1 200", stored, StringComparison.Ordinal);
        string hash = Records.Hashing.ContentHash.Sha256(bytes);
        Assert.Contains(harness.Socket.Objects(), o => o.Hash == hash);
    }

    /// <summary>An HTTP client of the local-network profile without a client certificate, pinned to the runtime's certificate, as a stranger is.</summary>
    private static HttpClient Stranger(LocalNetworkHarness harness)
    {
        string pinned = harness.Host.Endpoint.Loopback!.Certificate;
        var handler = new SocketsHttpHandler
        {
            SslOptions = new SslClientAuthenticationOptions
            {
                TargetHost = "vericairn-runtime",
                RemoteCertificateValidationCallback = (_, presented, _, _) => presented is not null && RuntimeIdentityScheme.CertificateFingerprint(presented.GetRawCertData()) == pinned,
            },
        };
        return new HttpClient(handler, disposeHandler: true) { BaseAddress = new Uri($"https://{harness.Address}/") };
    }

    private static PairingHello Guess() => new(PairingProtocol.NewNonce(), Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32)));

    private static (PairingRegistry.Pairing? Pairing, string? Problem) Try(PairingRegistry registry, PairingHello hello, string address) =>
        registry.Hello(hello, Base64Url.DecodeFromChars(hello.Mac), "sha256:runtime", address);

    [Fact]
    public void C2_AMacOfAnySizeIsWeighedByItsLengthBeforeAnythingIsDecoded()
    {
        string expected = Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32));
        string huge = new('A', 24 * 1024 * 1024);
        long before = GC.GetAllocatedBytesForCurrentThread();
        bool matched = PairingProtocol.Matches(expected, huge);
        bool mac = PairingProtocol.IsMac(huge);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.False(matched);
        Assert.False(mac);
        Assert.True(allocated < 64 * 1024, $"{allocated} bytes allocated to weigh a MAC");
        Assert.True(PairingProtocol.IsMac(expected));
        Assert.False(PairingProtocol.IsMac(expected + "A"));
    }

    [Fact]
    public async Task C2_APairingMessageIsReadOnlyAsFarAsItsLimitAndAMacOfTheWrongLengthIsRefusedUnread()
    {
        await using LocalNetworkHarness harness = await LocalNetworkHarness.StartAsync();
        using HttpClient stranger = Stranger(harness);

        // An oversized hello is refused without parsing beyond the limit. Its entire payload also fits the refusal drain bound;
        // a peer sending more than that bound cannot require delivery after a bounded close. A wrong-length MAC is never decoded.
        var oversized = new PairingHello(PairingProtocol.NewNonce(), new string('A', 32 * 1024));
        Assert.True(JsonSerializer.SerializeToUtf8Bytes(oversized, ProtocolJson.Options).Length <= DaemonHost.RefusedBodyLimit);
        using HttpResponseMessage large = await stranger.PostAsJsonAsync(new Uri("/v1/pairing/hello", UriKind.Relative), oversized, ProtocolJson.Options, TestContext.Current.CancellationToken);
        Say($"{(int)large.StatusCode} {await large.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)}");
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, large.StatusCode);

        using HttpResponseMessage wrong = await stranger.PostAsJsonAsync(new Uri("/v1/pairing/hello", UriKind.Relative), new PairingHello(PairingProtocol.NewNonce(), Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(33))), ProtocolJson.Options, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, wrong.StatusCode);

        // Neither counts against the address: a genuine device there pairs.
        PairingIssued issued = harness.Issue(harness.Estate.BuilderId);
        Assert.Equal(harness.Estate.BuilderId, harness.Connections.Add(harness.Address, issued.Code, new PairingOptions("Phone")).Principal);
    }

    [Fact]
    public void C5_AnAddressStillCountedIsNeverForgottenANewOneWaitsAndAllFailuresHaveABudget()
    {
        var time = new ManualTime(DateTimeOffset.UtcNow);
        var registry = new PairingRegistry(time);

        // An address that sent too many failed hellos is refused for its minute.
        for (int i = 0; i < PairingRegistry.MaxFailures; i++)
        {
            Assert.Contains("No pairing here matches", Try(registry, Guess(), "203.0.113.1").Problem, StringComparison.Ordinal);
        }

        Assert.Contains("Too many pairing attempts", Try(registry, Guess(), "203.0.113.1").Problem, StringComparison.Ordinal);

        // As many addresses as are counted fill the count; a new one then waits, and the first is still refused, never forgotten.
        for (int i = 1; i < PairingRegistry.MaxAddresses; i++)
        {
            Assert.Contains("No pairing here matches", Try(registry, Guess(), $"198.51.{i / 256}.{i % 256}").Problem, StringComparison.Ordinal);
        }

        Assert.Contains("too many addresses", Try(registry, Guess(), "192.0.2.77").Problem, StringComparison.Ordinal);
        Assert.Contains("Too many pairing attempts", Try(registry, Guess(), "203.0.113.1").Problem, StringComparison.Ordinal);

        // Once the minute ends, both are heard again.
        time.Advance(PairingRegistry.FailureWindow);
        Assert.Contains("No pairing here matches", Try(registry, Guess(), "192.0.2.77").Problem, StringComparison.Ordinal);
        Assert.Contains("No pairing here matches", Try(registry, Guess(), "203.0.113.1").Problem, StringComparison.Ordinal);

        // In a later minute, failures in all have a budget: past it, every hello waits, from an address that sent few as well.
        time.Advance(PairingRegistry.FailureWindow);
        int perAddress = (PairingRegistry.MaxFailuresInAll / PairingRegistry.MaxAddresses) + 1;
        int sent = 0;
        for (int i = 0; sent < PairingRegistry.MaxFailuresInAll; i++)
        {
            for (int j = 0; j < perAddress && sent < PairingRegistry.MaxFailuresInAll; j++, sent++)
            {
                Assert.Contains("No pairing here matches", Try(registry, Guess(), $"10.0.{i / 256}.{i % 256}").Problem, StringComparison.Ordinal);
            }
        }

        Assert.Contains("in all this minute", Try(registry, Guess(), "10.0.0.0").Problem, StringComparison.Ordinal);
    }

    [Fact]
    public async Task N5_HellosThatMatchNoCodeFromOneAddressRefuseNoGenuineDeviceThere()
    {
        // The re-review's probe: ten hellos that match no code from one address (a NAT, a proxy, a container's gateway), then the
        // genuine device there with the genuine code. A failed hello closes its connection.
        await using LocalNetworkHarness harness = await LocalNetworkHarness.StartAsync();
        PairingIssued issued = harness.Issue(harness.Estate.BuilderId);
        using HttpClient stranger = Stranger(harness);
        for (int i = 0; i < 10; i++)
        {
            using HttpResponseMessage miss = await stranger.PostAsJsonAsync(new Uri("/v1/pairing/hello", UriKind.Relative), Guess(), ProtocolJson.Options, TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.Unauthorized, miss.StatusCode);
            Assert.True(miss.Headers.ConnectionClose == true);
        }

        RuntimeConnection connection = harness.Connections.Add(harness.Address, issued.Code, new PairingOptions("Phone"));
        Assert.Equal(harness.Estate.BuilderId, connection.Principal);
    }

    [Fact]
    public void C6_TheLastFailedConfirmationClosesEveryExchangeOfTheCodeAndTheCapHoldsAsExchangesOpen()
    {
        var time = new ManualTime(DateTimeOffset.UtcNow);
        var registry = new PairingRegistry(time);
        (string? code, PairingRegistry.Pairing? issued, _) = registry.Issue(Records.Identity.Ulid.NewUlid().Value, Records.Identity.Ulid.NewUlid().Value, ["local-network"]);
        Assert.True(PairingProtocol.TryParse(code, out string secret));
        (byte[] runtimeKey, byte[] deviceKey) = PairingProtocol.Keys(secret);
        var plan = new PairingPlan(Records.Identity.Ulid.NewUlid().Value, "2026-10-15T12:00:00Z", 2, Records.Identity.Ulid.NewUlid().Value, "Estate", issued!.Principal, "Agent", "agent");
        var device = new PairingDevice("Phone", "linux", "file", Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(91)), "none");

        (PairingHello Hello, PairingChallenge Challenge) Open()
        {
            string nonce = PairingProtocol.NewNonce();
            var hello = new PairingHello(nonce, PairingProtocol.HelloMac(deviceKey, nonce, "sha256:runtime"));
            (PairingRegistry.Pairing? found, string? problem) = Try(registry, hello, "192.0.2.1");
            Assert.True(found is not null, problem);
            var challenge = new PairingChallenge(nonce, PairingProtocol.NewNonce(), plan.Estate, plan.EstateName, "identity", "sha256:runtime", plan.Principal, plan.PrincipalName, plan.PrincipalKind, found.Profiles, plan.Record, plan.At, plan.Format, "2026-10-15T12:10:00Z", string.Empty);
            challenge = challenge with { Mac = PairingProtocol.ChallengeMac(runtimeKey, hello, challenge) };
            Assert.Null(registry.Open(found, hello, challenge, plan));
            return (hello, challenge);
        }

        PairingConfirm Confirm((PairingHello Hello, PairingChallenge Challenge) exchange, bool genuine)
        {
            var confirm = new PairingConfirm(exchange.Challenge.RuntimeNonce, device, "signature", string.Empty);
            return confirm with { Mac = genuine ? PairingProtocol.ConfirmMac(deviceKey, exchange.Hello, exchange.Challenge, confirm) : Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32)) };
        }

        // At most four exchanges of a code are open: opening six closes the two oldest.
        List<(PairingHello, PairingChallenge)> opened = [.. Enumerable.Range(0, 6).Select(_ => Open())];
        Assert.Contains("No exchange", registry.Confirm(Confirm(opened[0], genuine: true)).Problem, StringComparison.Ordinal);
        Assert.Contains("No exchange", registry.Confirm(Confirm(opened[1], genuine: true)).Problem, StringComparison.Ordinal);

        // Four confirmations that do not match use four attempts; the fifth, on another exchange, uses the last and closes the rest.
        foreach ((PairingHello, PairingChallenge) exchange in opened[2..])
        {
            Assert.Contains("does not match", registry.Confirm(Confirm(exchange, genuine: false)).Problem, StringComparison.Ordinal);
        }

        List<(PairingHello, PairingChallenge)> more = [.. Enumerable.Range(0, 4).Select(_ => Open())];
        Assert.Contains("does not match", registry.Confirm(Confirm(more[0], genuine: false)).Problem, StringComparison.Ordinal);
        Assert.Equal(0, issued.AttemptsLeft);

        // Nothing is open any more, not even for the genuine device, and nothing opens again.
        foreach ((PairingHello, PairingChallenge) exchange in more[1..])
        {
            (PairingRegistry.Pairing? pairing, _, string? problem) = registry.Confirm(Confirm(exchange, genuine: true));
            Assert.Null(pairing);
            Assert.Contains("No exchange", problem, StringComparison.Ordinal);
        }

        string nonce = PairingProtocol.NewNonce();
        Assert.Contains("confirmations that did not match", Try(registry, new PairingHello(nonce, PairingProtocol.HelloMac(deviceKey, nonce, "sha256:runtime")), "192.0.2.1").Problem, StringComparison.Ordinal);
        Assert.Equal(0, issued.AttemptsLeft);
    }

    private static JsonElement Json(string text)
    {
        using JsonDocument document = JsonDocument.Parse(text);
        return document.RootElement.Clone();
    }

    /// <summary>
    /// A run in flight on the harness's runtime: the operator's goal, a work order and its assignment to the builder, carried by the
    /// stand-in harness that ignores a stop until its grace runs out. Devices of other principals are made first, while the
    /// operator's device still signs admissions here; then the operator's key leaves this machine, as a run requires.
    /// </summary>
    private static async Task<string> RunInFlightAsync(LocalNetworkHarness harness, string scratch, Action devices)
    {
        string op = harness.Estate.OperatorId;
        string builder = harness.Estate.BuilderId;
        VericairnClient api = harness.Socket;
        var registry = new ProviderRegistry();
        registry.Register(new RunApiTests.StandInLauncher());
        registry.Register(new RunApiTests.DirectoryWorkspaces());
        harness.Host.Service.Providers = registry;
        harness.Host.Service.WorkerOptions = new WorkerOptions { Heartbeat = TimeSpan.FromMilliseconds(100), Grace = TimeSpan.FromSeconds(1), StopWait = TimeSpan.FromSeconds(2) };
        Assert.True(api.DeviceAdmit(new AdmitRequest(api.DeviceCreate(new DeviceCreateRequest(builder, "Builder's device", "file")).Written!.Id, op, "The builder's.")).Ok);
        devices();
        new FileKeyStore(VericairnHome.Path()).Remove(harness.OperatorDevice);
        string goal = api.GoalPropose(new GoalProposeRequest(op, Json($$$"""{"title":"Build the widget","objective":"Ship the widget.","completion":[{"evidence":"It ships.","check":"Read it."}],"constraints":[],"kind":"finite","sponsor":"{{{op}}}","budget":{"wallMinutes":60,"attemptsPerStep":3},"plan":{"policy":"proposed-by-planner","approval":"sponsor-approves"},"inputs":[]}"""))).Written!.Id;
        Assert.True(api.GoalActivate(goal, new GoalActionRequest(op)).Ok);
        Assert.True(api.NewRecord(new NewRecordRequest("work-order", op, Json($$"""{"key":"W-1","kind":"ordinary","title":"Build the widget","intent":"Build it.","scope":["widget"],"exitQuestion":"Does it build?","inputs":[{"type":"goal","id":"{{goal}}"}],"deliverables":["widget"]}"""))).Ok);
        string assignment = api.Assign(new AssignRequest("W-1", builder, op, Json("""{"outcome":"A widget.","sources":[],"permittedChanges":["the widget"],"prohibitedEffects":[],"workspace":["widget"],"limits":[],"expectedOutputs":["widget"],"reviewCriteria":["it builds"],"recovery":"Record a result."}"""))).Written!.Id;
        string resource = api.NewRecord(new NewRecordRequest("resource", op, Json("""{"kind":"repository","name":"widget","role":"Its source.","location":{"github":"Example/widget"},"visibility":"private"}"""))).Written!.Id;
        RepositoryClones.Register(harness.Host.Service.Session.Manifest.Estate, resource, scratch);
        RunResponse started = api.RunStart(new RunStartRequest(assignment, "stand-in", builder, op, resource, Settings: new Dictionary<string, string>(StringComparer.Ordinal) { ["command"] = FakeHarness.Setting("ignore-stop", Path.Combine(scratch, "report.jsonl")) }));
        Assert.True(started.Ok, started.Message);
        DateTimeOffset until = DateTimeOffset.UtcNow.AddSeconds(30);
        while (!api.Runs().Runs.Single(r => r.Run == started.Run).Observed)
        {
            Assert.True(DateTimeOffset.UtcNow < until, "The run was never observed.");
            await Task.Delay(100, TestContext.Current.CancellationToken);
        }

        return started.Run;
    }

    [Fact]
    public async Task C3_N2_OnlyAnOperatorWhoeverDecidesForTheGoalOrWhoStartedTheRunStopsItAndNoStopIsHeldForItsGrace()
    {
        await using LocalNetworkHarness harness = await LocalNetworkHarness.StartAsync();
        string op = harness.Estate.OperatorId;
        string builder = harness.Estate.BuilderId;

        // A third principal, an agent with no part in the run, with a paired laptop and a device on the runtime's machine.
        using JsonDocument person = JsonDocument.Parse("""{"kind":"agent","name":"Bystander","roles":["contributor"],"agent":{"vendor":"Example","product":"Other"}}""");
        string bystander = harness.Socket.NewRecord(new NewRecordRequest("principal", op, person.RootElement)).Written!.Id;
        RuntimeConnection laptop = null!;
        string local = null!;
        string scratch = Path.Combine(Path.GetTempPath(), "vericairn-second-review-run-" + Records.Identity.Ulid.NewUlid().Value);
        Directory.CreateDirectory(scratch);
        try
        {
            string run = await RunInFlightAsync(harness, scratch, () =>
            {
                laptop = harness.Pair(bystander, "Bystander laptop");
                local = harness.Socket.DeviceCreate(new DeviceCreateRequest(bystander, "Bystander worker", "file")).Written!.Id;
                Assert.True(harness.Socket.DeviceAdmit(new AdmitRequest(local, op, "The bystander's.")).Ok);
            });

            // Over the local network and over loopback, the bystander's devices are refused at once, and the run goes on.
            var clock = System.Diagnostics.Stopwatch.StartNew();
            using (VericairnClient lan = VericairnClient.LocalNetwork(laptop, harness.Signer(laptop)))
            {
                RunStopResponse refused = lan.RunStop(run, new RunStopRequest(bystander, "Because I can.", 8));
                Assert.False(refused.Ok);
                Assert.Equal("refused", refused.Problem!.Code);
                Assert.Contains("may not stop run", refused.Problem.Message, StringComparison.Ordinal);
            }

            using (VericairnClient loopback = VericairnClient.Loopback(harness.Host.Endpoint, DeviceRequestSigner.Find(local)!))
            {
                RunStopResponse refused = loopback.RunStop(run, new RunStopRequest(bystander, "Because I can.", 8));
                Assert.Equal("refused", refused.Problem!.Code);
            }

            // The run's own agent did not start it and decides nothing for its goal: refused as well, on the local socket too.
            Assert.Equal("refused", harness.Socket.RunStop(run, new RunStopRequest(builder, "Mine.", 8)).Problem!.Code);
            Assert.True(clock.Elapsed < TimeSpan.FromSeconds(8), $"The refusals took {clock.Elapsed}.");
            Assert.Equal("dispatched", harness.Socket.Runs().Runs.Single(r => r.Run == run).Status);

            // The operator, who started it, stops it: the answer comes within the stop wait, not after the eight seconds of grace.
            clock.Restart();
            RunStopResponse stopped = harness.Socket.RunStop(run, new RunStopRequest(op, "Replaced.", 8));
            Say($"{stopped.State} after {clock.Elapsed.TotalSeconds:F1} s: {stopped.Message}");
            Assert.True(stopped.Ok, stopped.Message);
            Assert.True(clock.Elapsed < TimeSpan.FromSeconds(8), $"The stop was held for {clock.Elapsed}.");
            Assert.NotEqual("contained", stopped.State);

            // The worker still escalates after the grace and the settlement records it.
            DateTimeOffset until = DateTimeOffset.UtcNow.AddSeconds(40);
            while (harness.Socket.Runs().Runs.Single(r => r.Run == run).Status == "dispatched")
            {
                Assert.True(DateTimeOffset.UtcNow < until, "The run never settled.");
                await Task.Delay(200, TestContext.Current.CancellationToken);
            }
        }
        finally
        {
            Directory.Delete(scratch, recursive: true);
        }
    }

    [Fact]
    public async Task N4_AnExportIsAnAdministratorsOnLoopbackAndNamesTheirPrincipal()
    {
        await using LocalNetworkHarness harness = await LocalNetworkHarness.StartAsync();
        WriteOutcome local = harness.Socket.DeviceCreate(new DeviceCreateRequest(harness.Estate.BuilderId, "Agent worker", "file"));
        Assert.True(harness.Socket.DeviceAdmit(new AdmitRequest(local.Written!.Id, harness.Estate.OperatorId, "Agent.")).Ok);
        string target = Path.Combine(harness.DeviceHome, "chosen", "by", "the-agent", "export.zip");

        // The agent's loopback device naming no principal, or its own, is refused, and nothing is written.
        using (VericairnClient agent = VericairnClient.Loopback(harness.Host.Endpoint, DeviceRequestSigner.Find(local.Written.Id)!))
        {
            (ExportResponse? unnamed, Problem? anonymous) = agent.Export(new ExportRequest(target));
            Assert.Null(unnamed);
            Assert.Equal("denied", anonymous!.Code);
            (ExportResponse? own, Problem? notAdministrator) = agent.Export(new ExportRequest(target, harness.Estate.BuilderId));
            Assert.Null(own);
            Assert.Contains("administrator's", notAdministrator!.Message, StringComparison.Ordinal);
        }

        Assert.False(File.Exists(target));
        Assert.False(Directory.Exists(Path.Combine(harness.DeviceHome, "chosen")));

        // The operator's own device exports, naming the operator.
        using VericairnClient operatorDevice = VericairnClient.Loopback(harness.Host.Endpoint, DeviceRequestSigner.Find(harness.OperatorDevice)!);
        (ExportResponse? export, Problem? problem) = operatorDevice.Export(new ExportRequest(target, harness.Estate.OperatorId));
        Assert.True(export is not null, problem?.Message);
        Assert.True(File.Exists(target));
    }

    [Fact]
    public async Task InfoA_ARequestOfATypeTheRulesDoNotNameIsRefusedWhereverItIsDeclared()
    {
        await using LocalNetworkHarness harness = await LocalNetworkHarness.StartAsync();
        var scope = new RequestScope(harness.Host.Service, new LocalNetworkLedger(harness.DeviceHome));
        var context = new DefaultHttpContext();
        context.Request.Method = "POST";
        context.Request.Path = "/v1/records";
        context.Items[RequestAuthenticator.DeviceItem] = new AuthenticatedDevice(harness.OperatorDevice, harness.Estate.OperatorId, LocalNetwork: false);
        bool reached = false;
        object? result = await scope.InvokeAsync(new DefaultEndpointFilterInvocationContext(context, new Uri("https://example.invalid/")), _ =>
        {
            reached = true;
            return ValueTask.FromResult<object?>(null);
        });
        Assert.False(reached);
        Assert.Equal(StatusCodes.Status401Unauthorized, Assert.IsAssignableFrom<IStatusCodeHttpResult>(result).StatusCode);
    }

    [Theory]
    [InlineData("{\"at\":")]
    [InlineData("malformed tail")]
    [InlineData("{\"at\":\"unfinished")]
    public void C4_ATornTailCannotSwallowTheNextIntentOrItsReconciliation(string tail)
    {
        string directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            string path = Path.Combine(directory, LocalNetworkLedger.FileName);
            var ledger = new LocalNetworkLedger(directory);
            string first = ledger.Intend("2026-10-15T00:00:00Z", "device", "principal", "POST /v1/records");
            File.AppendAllText(path, tail);
            string second = ledger.Intend("2026-10-15T00:00:01Z", "device", "principal", "POST /v1/records");
            Assert.Equal(new[] { first, second }, LocalNetworkLedger.Read(directory).Select(w => w.Id));
            Assert.All(LocalNetworkLedger.Read(directory), w => Assert.Equal("pending", w.State));

            // A failed outcome append must not swallow the interruption appended on the next start either.
            File.AppendAllText(path, tail);
            Assert.Equal(2, new LocalNetworkLedger(directory).Reconcile("2026-10-15T00:00:02Z", (_, _) => ["candidate"]));
            Assert.All(LocalNetworkLedger.Read(directory), w =>
            {
                Assert.Equal("interrupted", w.State);
                Assert.Equal("candidate", Assert.Single(w.Candidates!));
            });
            Assert.Equal(0, ledger.Reconcile("2026-10-15T00:00:03Z", (_, _) => []));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task C4_AWriteWhosePairedDeviceCannotBeKeptIsRefusedAndNothingIsWritten()
    {
        await using LocalNetworkHarness harness = await LocalNetworkHarness.StartAsync();
        RuntimeConnection laptop = harness.Pair(harness.Estate.BuilderId, "Agent laptop");
        string live = Path.GetDirectoryName(DaemonHost.EndpointFilePath(harness.Estate.Root))!;
        string ledger = Path.Combine(live, LocalNetworkLedger.FileName);

        // A ledger that cannot be written, here because a directory stands where it would be.
        Directory.CreateDirectory(ledger);
        using JsonDocument order = JsonDocument.Parse(Order("W-C4"));
        using (VericairnClient lan = VericairnClient.LocalNetwork(laptop, harness.Signer(laptop)))
        {
            HttpRequestException refused = Assert.Throws<HttpRequestException>(() => lan.NewRecord(new NewRecordRequest("work-order", harness.Estate.BuilderId, order.RootElement)));
            Assert.Equal(HttpStatusCode.ServiceUnavailable, refused.StatusCode);
        }

        Assert.DoesNotContain(harness.Socket.Records("work-order"), r => r.Envelope.GetProperty("body").GetProperty("key").GetString() == "W-C4");

        // Once it can be written, the write goes through, and its paired device is kept with the record it produced.
        Directory.Delete(ledger);
        using (VericairnClient lan = VericairnClient.LocalNetwork(laptop, harness.Signer(laptop)))
        {
            WriteOutcome written = lan.NewRecord(new NewRecordRequest("work-order", harness.Estate.BuilderId, order.RootElement));
            Assert.True(written.Ok, written.Problem?.Message);
            LocalNetworkWrite kept = Assert.Single(LocalNetworkLedger.Read(live));
            Assert.Equal(("written", laptop.Device, written.Written!.Id, written.Written.Hash), (kept.State, kept.Device, kept.Record, kept.Hash));
        }

        // The file holds the intent, flushed before the write, and the outcome after it, under one id.
        string[] lines = File.ReadAllLines(ledger);
        Assert.Equal(2, lines.Length);
        Assert.Contains("\"state\":\"pending\"", lines[0], StringComparison.Ordinal);
        Assert.Contains("\"state\":\"written\"", lines[1], StringComparison.Ordinal);
    }

    [Fact]
    public async Task C4_AWriteTheRuntimeStoppedInTheMiddleOfIsClosedAsInterruptedWhenItStartsAgain()
    {
        await using LocalNetworkHarness harness = await LocalNetworkHarness.StartAsync();
        RuntimeConnection laptop = harness.Pair(harness.Estate.BuilderId, "Agent laptop");
        string live = Path.GetDirectoryName(DaemonHost.EndpointFilePath(harness.Estate.Root))!;

        // The crash window: the intent kept, the record written, the runtime gone before the outcome.
        string intended = new LocalNetworkLedger(live).Intend(Records.Model.RecordParser.FormatTimestamp(DateTimeOffset.UtcNow), laptop.Device, harness.Estate.BuilderId, "POST /v1/records");
        using JsonDocument order = JsonDocument.Parse(Order("W-C4B"));
        WriteOutcome written = harness.Socket.NewRecord(new NewRecordRequest("work-order", harness.Estate.BuilderId, order.RootElement));
        Assert.True(written.Ok, written.Problem?.Message + " " + string.Join("; ", written.Problem?.Findings.Select(f => $"{f.Code} {f.Message}") ?? []));
        Assert.Equal("pending", Assert.Single(LocalNetworkLedger.Read(live)).State);
        await harness.Host.StopAsync();

        // The next start closes it, naming the record it may have produced; a start after that closes nothing again.
        var options = new DaemonOptions { LocalNetwork = new IPEndPoint(IPAddress.Loopback, 0), IdentityStore = new FileKeyStore(VericairnHome.Path()) };
        await using (DaemonHost restarted = await DaemonHost.StartAsync(harness.Estate.Root, options: options, cancellationToken: TestContext.Current.CancellationToken))
        {
            LocalNetworkWrite closed = Assert.Single(LocalNetworkLedger.Read(live));
            Assert.Equal((intended, "interrupted", laptop.Device), (closed.Id, closed.State, closed.Device));
            Assert.Contains(written.Written!.Id, closed.Candidates!);
            await restarted.StopAsync();
        }

        await using (DaemonHost again = await DaemonHost.StartAsync(harness.Estate.Root, options: options, cancellationToken: TestContext.Current.CancellationToken))
        {
            Assert.Equal(2, File.ReadAllLines(Path.Combine(live, LocalNetworkLedger.FileName)).Length);
            await again.StopAsync();
        }
    }

    [Fact]
    public async Task Renegotiation_NoClientRenegotiatesToPresentACertificateInsideTheEncryptedChannel()
    {
        await using LocalNetworkHarness harness = await LocalNetworkHarness.StartAsync();
        await using AddressListener addresses = AddressListener.Start();
        (X509Certificate2 addressed, X509Certificate2 plain) = AddressListener.Presentable(addresses.Port, "CN=" + harness.OperatorDevice, client: true);
        using X509Certificate2 named = addressed;
        using X509Certificate2 nameless = plain;
        Assert.True(RuntimeConnection.TryParseAddress(harness.Address, out string host, out int port));

        // A first handshake with no certificate, which the screen lets through, held to the server's verified Finished.
        using Tls12Probe probe = await Tls12Probe.ConnectAsync(host, port);
        Say($"handshake held; the runtime offered secure renegotiation: {probe.SecureRenegotiation}");

        // Inside the encrypted channel a renegotiating ClientHello: the runtime starts no new handshake, and closes the connection.
        IReadOnlyList<byte> answered = await probe.RenegotiateAsync(TimeSpan.FromSeconds(5));
        Say($"records answered to the renegotiating hello: [{string.Join(", ", answered)}]");
        Assert.DoesNotContain((byte)22, answered);

        // A certificate sent sealed as if the renegotiation went on is never read as one: nothing it names is fetched.
        bool sent = await probe.TrySendSealedAsync(22, Tls12Probe.CertificateMessage(named.RawData));
        Say($"the sealed certificate could be sent: {sent}");
        await Task.Delay(TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken);
        Assert.True(addresses.Requests.IsEmpty, $"This machine fetched what a certificate sent in a renegotiation named: {string.Join(", ", addresses.Requests)}");
    }
}
