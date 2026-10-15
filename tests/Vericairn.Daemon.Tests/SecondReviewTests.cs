// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Buffers.Text;
using System.Globalization;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Vericairn.Protocol;
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
}
