// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Buffers.Text;
using System.Globalization;
using System.Net;
using System.Numerics;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using Vericairn.Protocol;
using Vericairn.Records.Canonical;
using Vericairn.Records.Identity;
using Vericairn.Records.Signing;
using Vericairn.Records.Tests;
using Vericairn.Sdk;

namespace Vericairn.Daemon.Tests;

/// <summary>
/// Conformance of the local-network profile (design/identity-and-connectivity.md §§ 4, 6 and 7), over loopback on every platform:
/// off unless configured; mutual TLS where a paired device presents a certificate its key issued and the runtime identity proves
/// each connection; every request device-signed, fresh and once; refused for an unpaired client, a revoked device at once, a device
/// without the profile, another principal, and the operations on the runtime's own machine.
/// </summary>
[Collection("daemon")]
public class LocalNetworkConformanceTests
{
    private const string Order = """{"key":"W-9","kind":"ordinary","title":"Over the local network","intent":"Act within the admitted scope.","scope":["x"],"exitQuestion":"?","inputs":[],"deliverables":["y"]}""";

    public LocalNetworkConformanceTests()
    {
        DaemonTestHome.Ensure();
    }

    [Fact]
    public async Task ByDefaultNothingListensBeyondTheLocalSocketAndLoopback()
    {
        using var estate = new TestEstate(recordFormat: 2);
        await using DaemonHost host = await DaemonHost.StartAsync(estate.Root, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Null(host.Endpoint.LocalNetwork);
        Assert.Null(DaemonHost.Discover(estate.Root)!.LocalNetwork);
        Assert.Null(host.IdentityPublicKey);

        // The server's own account of what it bound: the local socket and one TCP listener, on the loopback interface.
        IReadOnlyList<string> addresses = host.Addresses();
        Assert.Equal(2, addresses.Count);
        Assert.Single(addresses, a => a.StartsWith("http://unix:", StringComparison.Ordinal) || a.StartsWith("http://pipe:", StringComparison.Ordinal));
        Uri tcp = new(Assert.Single(addresses, a => a.StartsWith("https://", StringComparison.Ordinal)));
        Assert.True(IPAddress.IsLoopback(IPAddress.Parse(tcp.Host)), tcp.ToString());
        Assert.Equal(host.Endpoint.Loopback!.Port, tcp.Port);

        // No runtime identity exists until the operator configures an address, so there is nothing to pair with.
        string directory = Path.GetDirectoryName(DaemonHost.EndpointFilePath(estate.Root))!;
        Assert.False(File.Exists(Path.Combine(directory, RuntimeIdentity.FileName)));
        Assert.Equal(RuntimeSettings.Load(directory), new RuntimeSettings());
        using var socket = new VericairnClient(host.Endpoint);
        Assert.Null(socket.RuntimeIdentity(PairingProtocol.NewNonce()));
        (PairingIssued? issued, Problem? problem) = socket.IssuePairing(new PairingIssueRequest(estate.BuilderId, estate.OperatorId));
        Assert.Null(issued);
        Assert.Contains("serves no local-network profile", problem!.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheOperatorConfiguresTheAddressTheProfileListensOnAndTheIdentityOutlivesARestart()
    {
        using var estate = new TestEstate(recordFormat: 2);
        string directory = Path.GetDirectoryName(DaemonHost.EndpointFilePath(estate.Root))!;
        new RuntimeSettings("127.0.0.1:0").Save(directory);
        if (!OperatingSystem.IsWindows())
        {
            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(Path.Combine(directory, RuntimeSettings.FileName)));
        }

        var options = new DaemonOptions { IdentityStore = new FileKeyStore(VericairnHome.Path()) };
        string identity;
        await using (DaemonHost host = await DaemonHost.StartAsync(estate.Root, options: options, cancellationToken: TestContext.Current.CancellationToken))
        {
            LocalNetworkEndpoint served = host.Endpoint.LocalNetwork!;
            Assert.Equal("127.0.0.1", served.Host);
            Assert.NotEqual(0, served.Port);
            Assert.NotEqual(host.Endpoint.Loopback!.Port, served.Port);
            identity = host.IdentityPublicKey!;
            Assert.Equal(RuntimeIdentityScheme.Fingerprint(identity), served.Identity);
            Assert.Equal(served, DaemonHost.Discover(estate.Root)!.LocalNetwork);
            Assert.All(host.Addresses().Where(a => a.StartsWith("https://", StringComparison.Ordinal)), a => Assert.True(IPAddress.IsLoopback(IPAddress.Parse(new Uri(a).Host)), a));
        }

        // The identity is kept beside the live store, naming its key and custody, never the key; a restart proves the same identity.
        string kept = File.ReadAllText(Path.Combine(directory, RuntimeIdentity.FileName));
        Assert.Contains(identity, kept, StringComparison.Ordinal);
        Assert.DoesNotContain("PRIVATE", kept, StringComparison.OrdinalIgnoreCase);
        await using (DaemonHost again = await DaemonHost.StartAsync(estate.Root, options: options, cancellationToken: TestContext.Current.CancellationToken))
        {
            Assert.Equal(identity, again.IdentityPublicKey);
        }

        // Only an IP address of this machine with a port is an address to listen on.
        Assert.True(RuntimeSettings.TryParseAddress("[::1]:7443", out IPEndPoint? v6, out _));
        Assert.Equal(IPAddress.IPv6Loopback, v6!.Address);
        Assert.False(RuntimeSettings.TryParseAddress("192.168.1.20", out _, out string? noPort));
        Assert.Contains("port", noPort, StringComparison.Ordinal);
        Assert.False(RuntimeSettings.TryParseAddress("runtime.example:7443", out _, out _));
    }

    [Fact]
    public async Task APairedDeviceActsWithinItsAdmittedScopeOverMutualTls()
    {
        await using LocalNetworkHarness harness = await LocalNetworkHarness.StartAsync();
        RuntimeConnection connection = harness.Pair(harness.Estate.BuilderId, "Build laptop");
        using VericairnClient client = harness.Connections.Connect(connection);
        Assert.Equal("local-network", client.Transport);
        Assert.Equal(harness.Estate.Estate.Id, client.Health().Estate);
        Assert.Equal(CanonicalJson.SerializePretty(harness.Socket.Status().Json), CanonicalJson.SerializePretty(client.Status().Json));

        // It writes as its own principal, whose authority the runtime evaluates as for any request...
        using JsonDocument order = JsonDocument.Parse(Order);
        WriteOutcome written = client.NewRecord(new NewRecordRequest("work-order", harness.Estate.BuilderId, order.RootElement));
        Assert.True(written.Ok, written.Problem?.Message);
        Assert.Equal(harness.Estate.BuilderId, harness.Socket.Record(written.Written!.Id)!.Envelope.GetProperty("by").GetString());

        // ...and never as another, even the operator whose admitted device the runtime holds.
        HttpRequestException other = Assert.Throws<HttpRequestException>(() => client.NewRecord(new NewRecordRequest("work-order", harness.Estate.OperatorId, order.RootElement)));
        Assert.Equal(HttpStatusCode.Unauthorized, other.StatusCode);

        // In whatever spelling or repetition the body names the acting principal.
        DeviceRequestSigner signer = harness.Signer(connection);
        using X509Certificate2 credential = LocalNetworkCredential.Issue(signer, Base64Url.DecodeFromChars(connection.DevicePublicKey), DateTimeOffset.UtcNow);
        using var raw = new MutualTls(harness, credential, harness.Host.Endpoint.Loopback!.Certificate);
        string body = Order.Replace("W-9", "W-10", StringComparison.Ordinal);
        foreach (string acting in new[] { $$"""{"type":"work-order","BY":"{{harness.Estate.OperatorId}}","body":{{body}}}""", $$"""{"type":"work-order","by":"{{harness.Estate.BuilderId}}","By":"{{harness.Estate.OperatorId}}","body":{{body}}}""" })
        {
            (HttpStatusCode status, string answer) = await raw.SendSignedAsync(signer, HttpMethod.Post, "/v1/records", acting);
            Assert.Equal(HttpStatusCode.Unauthorized, status);
            Assert.Contains("acts for its own principal", answer, StringComparison.Ordinal);
        }

        Assert.Null(harness.Socket.Brief("W-10"));
    }

    [Fact]
    public async Task AnUnpairedClientIsRefused()
    {
        await using LocalNetworkHarness harness = await LocalNetworkHarness.StartAsync();
        string runtimeCertificate = harness.Host.Endpoint.Loopback!.Certificate;

        // Without a certificate or a signature, nothing but the pairing exchange answers.
        using (var anonymous = new MutualTls(harness, null, runtimeCertificate))
        {
            (HttpStatusCode status, string body) = await anonymous.SendAsync(HttpMethod.Get, "/v1/health", null);
            Assert.Equal(HttpStatusCode.Unauthorized, status);
            Assert.Contains("pairs first", body, StringComparison.Ordinal);
            Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.SendAsync(HttpMethod.Get, "/v1/status", null)).Status);
        }

        // A key that never paired, with a certificate it issued itself and a device id of its own.
        var stranger = new FileKeyStore(harness.DeviceHome);
        string id = Ulid.NewUlid().Value;
        byte[] spki = stranger.Create(id);
        var strangerSigner = new DeviceRequestSigner(stranger, id);
        using (X509Certificate2 own = LocalNetworkCredential.Issue(strangerSigner, spki, DateTimeOffset.UtcNow))
        using (var unpaired = new MutualTls(harness, own, runtimeCertificate))
        {
            (HttpStatusCode status, string body) = await unpaired.SendSignedAsync(strangerSigner, HttpMethod.Get, "/v1/health");
            Assert.Equal(HttpStatusCode.Unauthorized, status);
            Assert.Contains("pairs first", body, StringComparison.Ordinal);
        }

        // Through the SDK it is refused while the connection is set up, before any request of its own.
        var pretend = new RuntimeConnection(Ulid.NewUlid().Value, harness.Address, harness.Estate.Estate.Id, null, harness.Host.IdentityPublicKey!, id, Base64Url.EncodeToString(spki), harness.Estate.BuilderId, null, ["local-network"], "2026-10-15T00:00:00Z");
        using (VericairnClient client = VericairnClient.LocalNetwork(pretend, strangerSigner))
        {
            HttpRequestException refused = Assert.ThrowsAny<HttpRequestException>(() => client.Health());
            Assert.Equal(ConnectionStates.Denied, Refusal(refused).State);
        }

        // A paired device's certificate makes no other key's requests acceptable, nor does its key make another certificate acceptable.
        RuntimeConnection paired = harness.Pair(harness.Estate.BuilderId);
        DeviceRequestSigner signer = harness.Signer(paired);
        using (X509Certificate2 genuine = LocalNetworkCredential.Issue(signer, Base64Url.DecodeFromChars(paired.DevicePublicKey), DateTimeOffset.UtcNow))
        using (var borrowed = new MutualTls(harness, genuine, runtimeCertificate))
        {
            (HttpStatusCode status, string body) = await borrowed.SendAsync(HttpMethod.Get, "/v1/health", borrowed.Sign(new KeySigner(stranger, id, paired.Device), "GET", "/v1/health"));
            Assert.Equal(HttpStatusCode.Unauthorized, status);
            Assert.Contains("does not verify", body, StringComparison.Ordinal);
        }

        using (X509Certificate2 forged = LocalNetworkCredential.Issue(new KeySigner(stranger, id, paired.Device), spki, DateTimeOffset.UtcNow))
        using (var mismatched = new MutualTls(harness, forged, runtimeCertificate))
        {
            (HttpStatusCode status, string body) = await mismatched.SendSignedAsync(signer, HttpMethod.Get, "/v1/health");
            Assert.Equal(HttpStatusCode.Unauthorized, status);
            Assert.Contains("pairs first", body, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task ARevokedDeviceIsRefusedAtOnceOnItsOpenConnection()
    {
        await using LocalNetworkHarness harness = await LocalNetworkHarness.StartAsync();
        RuntimeConnection connection = harness.Pair(harness.Estate.BuilderId);
        DeviceRequestSigner signer = harness.Signer(connection);
        using X509Certificate2 credential = LocalNetworkCredential.Issue(signer, Base64Url.DecodeFromChars(connection.DevicePublicKey), DateTimeOffset.UtcNow);
        using var open = new MutualTls(harness, credential, harness.Host.Endpoint.Loopback!.Certificate);
        Assert.Equal(HttpStatusCode.OK, (await open.SendSignedAsync(signer, HttpMethod.Get, "/v1/health")).Status);
        Assert.Equal(HttpStatusCode.OK, (await open.SendSignedAsync(signer, HttpMethod.Get, "/v1/status")).Status);
        Assert.Equal(1, open.Connections);

        // Revoked on the runtime's machine: refused at the next request on the connection it already had, with no restart.
        WriteOutcome revoked = harness.Socket.DeviceRevoke(new RevokeRequest(connection.Device, harness.Estate.OperatorId, "The phone was lost."));
        Assert.True(revoked.Ok, revoked.Problem?.Message);
        (HttpStatusCode status, string body) = await open.SendSignedAsync(signer, HttpMethod.Get, "/v1/health");
        Assert.Equal(HttpStatusCode.Unauthorized, status);
        Assert.Contains("revoked", body, StringComparison.Ordinal);
        Assert.Equal(1, open.Connections);

        // A new connection fares no better: the SDK is refused while it sets up, and a check says denied.
        using VericairnClient client = harness.Connections.Connect(connection);
        Assert.Equal(ConnectionStates.Denied, Refusal(Assert.ThrowsAny<HttpRequestException>(() => client.Health())).State);
        ConnectionStatus check = harness.Connections.Probe(connection);
        Assert.Equal(ConnectionStates.Denied, check.State);
        Assert.Contains("revoked", check.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ADeviceWithoutTheLocalNetworkProfileIsRefused()
    {
        await using LocalNetworkHarness harness = await LocalNetworkHarness.StartAsync();

        // Paired for another profile only: the pairing completes, and the local network refuses it.
        RuntimeConnection relayOnly = harness.Pair(harness.Estate.BuilderId, "Relay phone", "relay");
        Assert.Equal(["relay"], Assert.Single(harness.Socket.Devices(), d => d.Id == relayOnly.Device).Profiles);
        ConnectionStatus check = harness.Connections.Probe(relayOnly);
        Assert.Equal(ConnectionStates.Denied, check.State);
        Assert.Contains("not permitted the local-network profile", check.Message, StringComparison.Ordinal);

        // A device recorded on the runtime's machine with its default profile, admitted and presenting a certificate its key issued.
        WriteOutcome local = harness.Socket.DeviceCreate(new DeviceCreateRequest(harness.Estate.BuilderId, "Build machine", "file"));
        Assert.True(local.Ok, local.Problem?.Message);
        Assert.True(harness.Socket.DeviceAdmit(new AdmitRequest(local.Written!.Id, harness.Estate.OperatorId, "The build machine.")).Ok);
        DeviceRequestSigner signer = DeviceRequestSigner.Find(local.Written.Id)!;
        byte[] spki = new FileKeyStore(VericairnHome.Path()).PublicKey(local.Written.Id)!;
        using X509Certificate2 credential = LocalNetworkCredential.Issue(signer, spki, DateTimeOffset.UtcNow);
        using var client = new MutualTls(harness, credential, harness.Host.Endpoint.Loopback!.Certificate);
        (HttpStatusCode status, string body) = await client.SendSignedAsync(signer, HttpMethod.Get, "/v1/health");
        Assert.Equal(HttpStatusCode.Unauthorized, status);
        Assert.Contains("its permitted profiles are embedded", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AReplayedSignedRequestIsRefused()
    {
        await using LocalNetworkHarness harness = await LocalNetworkHarness.StartAsync();
        RuntimeConnection connection = harness.Pair(harness.Estate.BuilderId);
        DeviceRequestSigner signer = harness.Signer(connection);
        using X509Certificate2 credential = LocalNetworkCredential.Issue(signer, Base64Url.DecodeFromChars(connection.DevicePublicKey), DateTimeOffset.UtcNow);
        using var client = new MutualTls(harness, credential, harness.Host.Endpoint.Loopback!.Certificate);

        (string Device, string SignedAt, string Signature) once = client.Sign(signer, "GET", "/v1/status");
        Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(HttpMethod.Get, "/v1/status", once)).Status);

        // The same signature again, its base64 padded spelling, and its malleated twin (r, n - s) are all the signature already used,
        // on the same connection or on a new one.
        string twin = Base64Url.EncodeToString(Malleate(Base64Url.DecodeFromChars(once.Signature)));
        foreach ((string Device, string SignedAt, string Signature) copy in new[] { once, once with { Signature = once.Signature + "==" }, once with { Signature = twin } })
        {
            (HttpStatusCode status, string body) = await client.SendAsync(HttpMethod.Get, "/v1/status", copy);
            Assert.Equal(HttpStatusCode.Unauthorized, status);
            Assert.Contains("already used", body, StringComparison.Ordinal);
        }

        using (var another = new MutualTls(harness, credential, harness.Host.Endpoint.Loopback!.Certificate))
        {
            Assert.Contains("already used", (await another.SendAsync(HttpMethod.Get, "/v1/status", once)).Body, StringComparison.Ordinal);
        }

        // A signature older than five minutes is refused as stale, before anything else; a fresh one is accepted.
        string stale = RequestSigning.FormatTime(DateTimeOffset.UtcNow.AddMinutes(-6));
        string old = Base64Url.EncodeToString(signer.Sign(harness.Digest("GET", "/v1/status", stale, [])));
        (HttpStatusCode staleStatus, string staleBody) = await client.SendAsync(HttpMethod.Get, "/v1/status", (signer.DeviceId, stale, old));
        Assert.Equal(HttpStatusCode.Unauthorized, staleStatus);
        Assert.Contains("five minutes", staleBody, StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.OK, (await client.SendSignedAsync(signer, HttpMethod.Get, "/v1/status")).Status);
    }

    [Fact]
    public async Task WhatActsOnTheRuntimesOwnMachineIsRefusedOverTheLocalNetwork()
    {
        await using LocalNetworkHarness harness = await LocalNetworkHarness.StartAsync();

        // The operator's own phone: the refusal is the profile's, not the principal's.
        RuntimeConnection phone = harness.Pair(harness.Estate.OperatorId, "Operator phone");
        DeviceRequestSigner signer = harness.Signer(phone);
        using X509Certificate2 credential = LocalNetworkCredential.Issue(signer, Base64Url.DecodeFromChars(phone.DevicePublicKey), DateTimeOffset.UtcNow);
        using var client = new MutualTls(harness, credential, harness.Host.Endpoint.Loopback!.Certificate);
        string op = harness.Estate.OperatorId;
        string export = Path.Combine(harness.DeviceHome, "export.zip");
        (string Path, string? Json)[] local =
        [
            ("/v1/shutdown", null),
            ("/v1/devices", $$"""{"principal":"{{op}}","name":"Planted key","custody":"file"}"""),
            ("/v1/devices/rotate", $$"""{"device":"{{harness.OperatorDevice}}"}"""),
            ("/v1/devices/rename", $$"""{"device":"{{harness.OperatorDevice}}","name":"Renamed"}"""),
            ("/v1/devices/recovery", $$"""{"principal":"{{op}}","name":"Recovery","output":{{JsonSerializer.Serialize(export)}},"passphrase":"a long passphrase"}"""),
            ("/v1/estate/export", $$"""{"output":{{JsonSerializer.Serialize(export)}},"by":"{{op}}"}"""),
            ("/v1/pairings", $$"""{"principal":"{{op}}","by":"{{op}}"}"""),
            ("/v1/runtime/certificate", """{"compromised":true}"""),
        ];
        foreach ((string path, string? json) in local)
        {
            (HttpStatusCode status, string body) = await client.SendSignedAsync(signer, HttpMethod.Post, path, json);
            Assert.True(status == HttpStatusCode.Unauthorized, $"{path}: {status} {body}");
            Assert.Contains("own machine and is refused over the local network", body, StringComparison.Ordinal);
        }

        // Nor does it reach a file there through an operation that is otherwise its principal's: a recovery key is used on its machine only.
        string recovery = $$"""{"device":"{{harness.OperatorDevice}}","by":"{{op}}","reason":"Lost.","recovery":{{JsonSerializer.Serialize(export)}},"passphrase":"a long passphrase"}""";
        (HttpStatusCode recoveryStatus, string recoveryBody) = await client.SendSignedAsync(signer, HttpMethod.Post, "/v1/devices/revoke", recovery);
        Assert.Equal(HttpStatusCode.Unauthorized, recoveryStatus);
        Assert.Contains("used there only", recoveryBody, StringComparison.Ordinal);

        Assert.False(File.Exists(export));
        Assert.Equal(2, harness.Socket.Devices().Count);
        Assert.Equal("admitted", Assert.Single(harness.Socket.Devices(), d => d.Id == harness.OperatorDevice).Admission);
        Assert.Equal(harness.Estate.Estate.Id, harness.Socket.Health().Estate);

        // Reading stays the device's to do.
        Assert.Equal(HttpStatusCode.OK, (await client.SendSignedAsync(signer, HttpMethod.Get, "/v1/devices")).Status);
    }

    [Fact]
    public async Task AClientSendsNothingToWhatDoesNotProveThePairedIdentity()
    {
        await using LocalNetworkHarness harness = await LocalNetworkHarness.StartAsync();
        RuntimeConnection connection = harness.Pair(harness.Estate.BuilderId);
        using VericairnClient genuine = harness.Connections.Connect(connection);

        // Something else answers at an address with its own certificate and relays the runtime's genuine proof for each nonce: the proof
        // names the certificate the runtime presented, not the one this connection showed, so the client stops there.
        string directory = Path.Combine(harness.DeviceHome, "impostor");
        using X509Certificate2 stranger = RuntimeCertificate.Load(directory);
        await using var impostor = new Impostor(stranger, nonce => JsonSerializer.Serialize(genuine.RuntimeIdentity(nonce), ProtocolJson.Options));
        RuntimeConnection redirected = connection with { Id = Ulid.NewUlid().Value, Address = impostor.Address };
        using (VericairnClient misled = harness.Connections.Connect(redirected))
        {
            RuntimeConnectionException refused = Refusal(Assert.ThrowsAny<HttpRequestException>(() => misled.Health()));
            Assert.Equal(ConnectionStates.Unavailable, refused.State);
            Assert.Contains("did not prove the runtime identity", refused.Message, StringComparison.Ordinal);
        }

        ConnectionStatus check = harness.Connections.Probe(redirected, TimeSpan.FromSeconds(5));
        Assert.Equal(ConnectionStates.Unavailable, check.State);
        await impostor.DisposeAsync();
        Assert.All(impostor.Requests, r => Assert.StartsWith("GET /v1/runtime/identity?nonce=", r, StringComparison.Ordinal));
        Assert.NotEmpty(impostor.Requests);
    }

    internal static RuntimeConnectionException Refusal(Exception exception)
    {
        for (Exception? inner = exception; inner is not null; inner = inner.InnerException)
        {
            if (inner is RuntimeConnectionException refusal)
            {
                return refusal;
            }
        }

        throw new Xunit.Sdk.XunitException($"No refusal in {exception}");
    }

    /// <summary>The twin of an IEEE P1363 ECDSA P-256 signature (r, s): (r, n - s), which verifies wherever the signature does.</summary>
    private static byte[] Malleate(byte[] signature)
    {
        BigInteger order = BigInteger.Parse("0FFFFFFFF00000000FFFFFFFFFFFFFFFFBCE6FAADA7179E84F3B9CAC2FC632551", NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture);
        BigInteger twin = order - new BigInteger(signature.AsSpan(32), isUnsigned: true, isBigEndian: true);
        byte[] copy = (byte[])signature.Clone();
        Array.Clear(copy, 32, 32);
        twin.TryWriteBytes(copy.AsSpan(64 - twin.GetByteCount(isUnsigned: true)), out _, isUnsigned: true, isBigEndian: true);
        return copy;
    }

    /// <summary>Signs in a device's name with a key held under another id.</summary>
    private sealed class KeySigner(IDeviceKeyStore store, string keyId, string deviceId) : IRequestSigner
    {
        public string DeviceId => deviceId;

        public byte[] Sign(ReadOnlySpan<byte> digest) => store.Sign(keyId, digest);
    }
}
