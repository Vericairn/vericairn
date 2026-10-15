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
using Vericairn.Protocol;
using Vericairn.Runtime.Service;
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
        byte[] second = Encoding.UTF8.GetBytes("{\"type\":\"work-order\",\"by\":\"" + harness.Estate.BuilderId + "\",\"body\":" + Order("W-C1b") + "}");
        at = RequestSigning.FormatTime(harness.Time.GetUtcNow());
        head = Head("POST", "/v1/records", second, at, signer, harness.Digest("POST", "/v1/records", at, second));
        string revoked = await Raw.ExchangeAsync(harness.Address, credential, pinned, [.. head, second[0]], TimeSpan.FromSeconds(5), trickle: second[1..], meanwhile: () => Assert.True(harness.Socket.DeviceRevoke(new RevokeRequest(connection.Device, harness.Estate.OperatorId, "Lost while it wrote.")).Ok));
        Say(revoked);
        Assert.StartsWith("HTTP/1.1 401", revoked, StringComparison.Ordinal);
        Assert.Contains("revoked", revoked, StringComparison.Ordinal);
        Assert.DoesNotContain(harness.Socket.Records("work-order"), r => r.Envelope.GetProperty("body").GetProperty("key").GetString() == "W-C1b");

        // On loopback too, a body that arrives after the signature is stale is refused.
        DeviceRequestSigner operatorDevice = DeviceRequestSigner.Find(harness.OperatorDevice)!;
        byte[] third = Encoding.UTF8.GetBytes("{\"type\":\"work-order\",\"by\":\"" + harness.Estate.OperatorId + "\",\"body\":" + Order("W-C1c") + "}");
        at = RequestSigning.FormatTime(harness.Time.GetUtcNow());
        head = Head("POST", "/v1/records", third, at, operatorDevice, RequestSigning.Digest("POST", "/v1/records", at, third));
        string loopback = await Raw.ExchangeAsync(LoopbackAddress(harness), null, pinned, [.. head, third[0]], TimeSpan.FromSeconds(5), trickle: third[1..], meanwhile: () => harness.Time.Advance(TimeSpan.FromMinutes(6)));
        Say(loopback);
        Assert.StartsWith("HTTP/1.1 401", loopback, StringComparison.Ordinal);
        Assert.Contains("five minutes", loopback, StringComparison.Ordinal);
        Assert.DoesNotContain(harness.Socket.Records("work-order"), r => r.Envelope.GetProperty("body").GetProperty("key").GetString() == "W-C1c");
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

    [Fact]
    public async Task N1_AReplayWhoseBodyArrivesAfterItsSignatureIsStaleIsRefused()
    {
        // The re-review's probe, on the system clock: a signature five seconds from stale, accepted once, then replayed with its body
        // trickled until after the signature is stale.
        await using LocalNetworkHarness harness = await LocalNetworkHarness.StartAsync(systemClock: true);
        RuntimeConnection connection = harness.Pair(harness.Estate.BuilderId);
        DeviceRequestSigner signer = harness.Signer(connection);
        using X509Certificate2 credential = LocalNetworkCredential.Issue(signer, Base64Url.DecodeFromChars(connection.DevicePublicKey), DateTimeOffset.UtcNow);
        string pinned = harness.Host.Endpoint.Loopback!.Certificate;
        byte[] body = Encoding.ASCII.GetBytes(new string('a', 3600));
        string at = RequestSigning.FormatTime(DateTimeOffset.UtcNow.AddSeconds(-295));
        byte[] head = Head("GET", "/v1/status", body, at, signer, harness.Digest("GET", "/v1/status", at, body), "text/plain");
        Assert.StartsWith("HTTP/1.1 200", await Raw.ExchangeAsync(harness.Address, credential, pinned, [.. head, .. body], TimeSpan.FromSeconds(5)), StringComparison.Ordinal);

        string replay = await Raw.ExchangeAsync(harness.Address, credential, pinned, head, TimeSpan.FromSeconds(15), trickle: body, chunkSize: 360, delay: TimeSpan.FromSeconds(1));
        Say(replay);
        Assert.StartsWith("HTTP/1.1 401", replay, StringComparison.Ordinal);
        Assert.Contains("five minutes", replay, StringComparison.Ordinal);
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

        // A hello far beyond what any pairing message holds is refused before it is read; one whose MAC is not 32 bytes, unread.
        var oversized = new PairingHello(PairingProtocol.NewNonce(), new string('A', 256 * 1024));
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
}
