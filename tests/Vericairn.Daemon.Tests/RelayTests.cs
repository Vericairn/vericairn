// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Buffers.Text;
using System.IO.Pipelines;
using System.Net.Security;
using System.Net.WebSockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Connections;
using Vericairn.Protocol;
using Vericairn.Records.Identity;
using Vericairn.Records.Tests;
using Vericairn.Relay;
using Vericairn.Runtime.Service;
using Vericairn.Sdk;

namespace Vericairn.Daemon.Tests;

public class RelayTests
{
    private static CancellationToken TestStop => TestContext.Current.CancellationToken;

    [Fact]
    public void TicketIsBoundToRegistrationDeviceTimesAndCounter()
    {
        using ECDsa key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using ECDsa other = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        string publicKey = Base64Url.EncodeToString(key.ExportSubjectPublicKeyInfo());
        string address = RelayProtocol.RandomId();
        string device = Ulid.NewUlid().Value;
        const long now = 123_456;
        RelayTicket ticket = RelayProtocol.Issue(key, address, device, now, 1);
        Assert.Equal(RelayProtocol.Lifetime, ticket.Expires - ticket.Issued);
        Assert.True(RelayProtocol.Verify(ticket, address, publicKey, now));
        Assert.False(RelayProtocol.Verify(ticket, address, publicKey, ticket.Expires));
        // A relay clock up to the stated skew behind the runtime still verifies a fresh ticket; one further behind does not.
        Assert.True(RelayProtocol.Verify(ticket, address, publicKey, now - RelayProtocol.ClockSkew));
        Assert.False(RelayProtocol.Verify(ticket, address, publicKey, now - RelayProtocol.ClockSkew - 1));
        Assert.False(RelayProtocol.Verify(ticket, RelayProtocol.RandomId(), publicKey, now));
        Assert.False(RelayProtocol.Verify(ticket, address, Base64Url.EncodeToString(other.ExportSubjectPublicKeyInfo()), now));
        Assert.False(RelayProtocol.Verify(ticket with { Device = Ulid.NewUlid().Value }, address, publicKey, now));
        Assert.False(RelayProtocol.Verify(ticket with { Counter = ticket.Counter + 1 }, address, publicKey, now));
        Assert.False(RelayProtocol.Verify(ticket with { Expires = ticket.Expires + 1 }, address, publicKey, now));

        string challenge = RelayProtocol.RandomId();
        string proof = RelayProtocol.Prove(key, address, challenge);
        Assert.True(RelayProtocol.VerifyProof(address, challenge, publicKey, proof));
        Assert.False(RelayProtocol.VerifyProof(address, RelayProtocol.RandomId(), publicKey, proof));
    }

    [Fact]
    public async Task UnticketedAndExpiredJoinsNeverContactTheRuntime()
    {
        var clock = new ManualTime(DateTimeOffset.UtcNow);
        await using var relay = new InProcessRelay(clock);
        using ECDsa key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        string address = AddressOf(key);
        string device = Ulid.NewUlid().Value;
        long now = clock.GetUtcNow().ToUnixTimeSeconds();
        using WebSocket control = await relay.RegisterAsync(address, key);
        await relay.RefusesAsync(null);
        await relay.RefusesAsync(RelayProtocol.Issue(key, address, device, now - RelayProtocol.Lifetime, 1));
        RelayTicket admitted = RelayProtocol.Issue(key, address, device, now, 2);
        using WebSocket join = relay.Connect();
        await RelayProtocol.SendAsync(join, new("join", Ticket: admitted), TestStop);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(TestStop);
        deadline.CancelAfter(TimeSpan.FromSeconds(2));
        RelayMessage request = Assert.IsType<RelayMessage>(await RelayProtocol.ReceiveAsync(control, deadline.Token));
        Assert.Equal("stream", request.Kind);
        Assert.Equal(admitted, request.Ticket);
    }

    [Fact]
    public async Task RelayForwardsBytesAndClosesTheRevokedDevicesStream()
    {
        var clock = new ManualTime(DateTimeOffset.UtcNow);
        await using var relay = new InProcessRelay(clock);
        using ECDsa key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        string address = AddressOf(key);
        string device = Ulid.NewUlid().Value;
        RelayTicket ticket = RelayProtocol.Issue(key, address, device, clock.GetUtcNow().ToUnixTimeSeconds(), 1);
        using WebSocket control = await relay.RegisterAsync(address, key);
        (RelayStream phone, RelayStream runtime) = await relay.JoinAsync(address, control, ticket);
        using (phone)
        using (runtime)
        {
            byte[] encrypted = RandomNumberGenerator.GetBytes(64);
            await phone.WriteAsync(encrypted, TestStop);
            byte[] forwarded = new byte[encrypted.Length];
            await runtime.ReadExactlyAsync(forwarded, TestStop);
            Assert.Equal(encrypted, forwarded);

            await RelayProtocol.SendAsync(control, new("revoke", Device: device, Counter: ticket.Counter), TestStop);
            await Assert.ThrowsAnyAsync<WebSocketException>(async () => await phone.ReadExactlyAsync(forwarded, TestStop));
            await relay.RefusesAsync(ticket);
        }
    }

    [Fact]
    public async Task FloodingOneAddressAndTicketLeavesAnotherAddressReachable()
    {
        var clock = new ManualTime(DateTimeOffset.UtcNow);
        await using var relay = new InProcessRelay(clock);
        using ECDsa key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using ECDsa otherKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        string first = AddressOf(key);
        string second = AddressOf(otherKey);
        string device = Ulid.NewUlid().Value;
        long now = clock.GetUtcNow().ToUnixTimeSeconds();
        using WebSocket firstControl = await relay.RegisterAsync(first, key);
        RelayTicket ticket = RelayProtocol.Issue(key, first, device, now, 1);
        for (int i = 0; i < 4; i++)
        {
            (RelayStream phone, RelayStream runtime) = await relay.JoinAsync(first, firstControl, ticket);
            phone.Dispose();
            runtime.Dispose();
        }
        await relay.RefusesAsync(ticket);

        // Valid tickets share the address's budget of 32 a window.
        for (int i = 0; i < 27; i++)
        {
            (RelayStream phone, RelayStream runtime) = await relay.JoinAsync(first, firstControl, RelayProtocol.Issue(key, first, device, now, i + 2));
            phone.Dispose();
            runtime.Dispose();
        }

        await relay.RefusesAsync(RelayProtocol.Issue(key, first, device, now, 35));
        clock.Advance(TimeSpan.FromSeconds(10));
        (RelayStream firstPhone, RelayStream firstRuntime) =
            await relay.JoinAsync(first, firstControl, RelayProtocol.Issue(key, first, device, now, 36));
        firstPhone.Dispose();
        firstRuntime.Dispose();

        using WebSocket secondControl = await relay.RegisterAsync(second, otherKey);
        (RelayStream otherPhone, RelayStream otherRuntime) =
            await relay.JoinAsync(second, secondControl, RelayProtocol.Issue(otherKey, second, device, now, 1));
        otherPhone.Dispose();
        otherRuntime.Dispose();
    }

    [Fact]
    public async Task JoinsWithoutAValidTicketNeverLockOutOneWithIt()
    {
        var clock = new ManualTime(DateTimeOffset.UtcNow);
        await using var relay = new InProcessRelay(clock);
        using ECDsa key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        string address = AddressOf(key);
        string device = Ulid.NewUlid().Value;
        long now = clock.GetUtcNow().ToUnixTimeSeconds();
        using WebSocket control = await relay.RegisterAsync(address, key);
        RelayTicket valid = RelayProtocol.Issue(key, address, device, now, 1);
        // A revoked device or a stranger knows the address and floods it with tickets that do not verify.
        for (int i = 0; i < 64; i++)
        {
            await relay.RefusesAsync(valid with { Counter = valid.Counter + i + 1 }, "203.0.113.9");
        }

        (RelayStream phone, RelayStream runtime) = await relay.JoinAsync(address, control, valid);
        phone.Dispose();
        runtime.Dispose();
    }

    [Fact]
    public async Task AnAddressCanOnlyBeRegisteredByTheKeyItIsDerivedFrom()
    {
        var clock = new ManualTime(DateTimeOffset.UtcNow);
        await using var relay = new InProcessRelay(clock);
        using ECDsa owner = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using ECDsa stranger = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        string address = AddressOf(owner);
        // A stranger proving its own key for the owner's address is never registered, so the owner's registration is never squatted.
        Assert.False(await relay.TryRegisterAsync(address, stranger));
        Assert.False(await relay.TryRegisterAsync(RelayProtocol.RandomId(), owner));
        using WebSocket control = await relay.RegisterAsync(address, owner);
        (RelayStream phone, RelayStream runtime) = await relay.JoinAsync(address, control, RelayProtocol.Issue(owner, address, Ulid.NewUlid().Value, clock.GetUtcNow().ToUnixTimeSeconds(), 1));
        phone.Dispose();
        runtime.Dispose();
    }

    [Fact]
    public async Task OneClientsJoinsWithoutAValidTicketCloseTheAddressOnlyToThatClient()
    {
        var clock = new ManualTime(DateTimeOffset.UtcNow);
        await using var relay = new InProcessRelay(clock);
        using ECDsa key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        string address = AddressOf(key);
        using WebSocket control = await relay.RegisterAsync(address, key);
        await SendBadJoinsAsync(relay, address, "203.0.113.9", 300);
        await WaitForUnverifiedAsync(relay, address, 32);

        // Past its share, that client is refused before verifying, a valid ticket too; another client still joins.
        await relay.RefusesAsync(RelayProtocol.Issue(key, address, Ulid.NewUlid().Value, clock.GetUtcNow().ToUnixTimeSeconds(), 1), "203.0.113.9");
        (RelayStream phone, RelayStream runtime) = await relay.JoinAsync(address, control,
            RelayProtocol.Issue(key, address, Ulid.NewUlid().Value, clock.GetUtcNow().ToUnixTimeSeconds(), 2), "198.51.100.4");
        phone.Dispose();
        runtime.Dispose();
        Assert.Equal(32, relay.Unverified(address));
    }

    [Fact]
    public async Task OnceAnAddressHasRefusedManyClientsAClientThatHasNotFailedIsStillVerified()
    {
        var clock = new ManualTime(DateTimeOffset.UtcNow);
        await using var relay = new InProcessRelay(clock);
        using ECDsa key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        string address = AddressOf(key);
        using WebSocket control = await relay.RegisterAsync(address, key);
        for (int client = 0; client < 8; client++)
        {
            await SendBadJoinsAsync(relay, address, $"2001:db8:0:{client}::/64", 32);
        }

        // The ninth client's join follows the first 256 being counted, so none of theirs is refused ahead of it.
        await WaitForUnverifiedAsync(relay, address, 256);
        await SendBadJoinsAsync(relay, address, "2001:db8:0:ff::/64", 1);
        await WaitForUnverifiedAsync(relay, address, 257);
        long now = clock.GetUtcNow().ToUnixTimeSeconds();
        // Past the address's total, a client that has failed this window is refused before verifying, a valid ticket too;
        // one that has not is verified as before, so flooding from many clients never closes the address to it.
        await relay.RefusesAsync(RelayProtocol.Issue(key, address, Ulid.NewUlid().Value, now, 1), "2001:db8:0:ff::/64");
        (RelayStream phone, RelayStream runtime) = await relay.JoinAsync(address, control,
            RelayProtocol.Issue(key, address, Ulid.NewUlid().Value, now, 2), "198.51.100.4");
        phone.Dispose();
        runtime.Dispose();
        clock.Advance(TimeSpan.FromSeconds(10));
        (phone, runtime) = await relay.JoinAsync(address, control,
            RelayProtocol.Issue(key, address, Ulid.NewUlid().Value, clock.GetUtcNow().ToUnixTimeSeconds(), 3), "2001:db8:0:ff::/64");
        phone.Dispose();
        runtime.Dispose();
    }

    [Fact]
    public async Task RegistrationLimitsApplyInTotalAndPerClientAsConfigured()
    {
        await using var relay = new InProcessRelay(TimeProvider.System, new RelayLimits(Registrations: 3, RegistrationsPerClient: 2));
        ECDsa[] keys = [.. Enumerable.Range(0, 4).Select(_ => ECDsa.Create(ECCurve.NamedCurves.nistP256))];
        try
        {
            using WebSocket first = await relay.RegisterAsync(AddressOf(keys[0]), keys[0]);
            using WebSocket second = await relay.RegisterAsync(AddressOf(keys[1]), keys[1]);
            // The client's own limit refuses its third; another client's is held until the total is reached.
            Assert.False(await relay.TryRegisterAsync(AddressOf(keys[2]), keys[2]));
            using WebSocket third = await relay.RegisterAsync(AddressOf(keys[2]), keys[2], "198.51.100.4");
            Assert.False(await relay.TryRegisterAsync(AddressOf(keys[3]), keys[3], "192.0.2.80"));
        }
        finally
        {
            foreach (ECDsa key in keys) key.Dispose();
        }
    }

    private static async Task SendBadJoinsAsync(InProcessRelay relay, string address, string peer, int count)
    {
        for (int i = 0; i < count; i++)
        {
            using WebSocket stranger = relay.Connect(peer);
            await RelayProtocol.SendAsync(stranger, new("join", Ticket: new RelayTicket(address, "0", 0, 0, 0, "")), TestStop);
        }
    }

    // Joins are counted as the relay reads them; wait for its count rather than for time.
    private static async Task WaitForUnverifiedAsync(InProcessRelay relay, string address, int count)
    {
        for (int i = 0; relay.Unverified(address) < count; i++)
        {
            Assert.True(i < 500, $"The relay counted {relay.Unverified(address)} of {count} joins without a valid ticket.");
            await Task.Delay(10, TestStop);
        }
    }

    [Fact]
    public async Task AnotherDevicesCertificateWithATicketGetsNoResponse()
    {
        await using var relay = new InProcessRelay(TimeProvider.System);
        await using LocalNetworkHarness harness = await LocalNetworkHarness.StartAsync(systemClock: true,
            relay: new Uri("https://relay.invalid"), connectRelay: (_, _) => Task.FromResult(relay.Connect()));
        RuntimeConnection a = harness.Pair(harness.Estate.BuilderId, "A");
        RuntimeConnection b = harness.Pair(harness.Estate.BuilderId, "B");
        RelayRoute route = await RouteAsync(harness, a);

        Assert.Equal("HTTP 200", await RelayedAttemptAsync(harness, relay, route, a));
        // Another paired device's certificate is aborted once the handshake ends, before any request is read: no response at all,
        // where the request-level check alone would answer 401.
        Assert.Equal("no response", await RelayedAttemptAsync(harness, relay, route, b));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ARelayThatRewritesOneHandshakeByteBreaksTheDevicesConnection(bool fromDevice)
    {
        await using var relay = new InProcessRelay(TimeProvider.System);
        await using LocalNetworkHarness harness = await LocalNetworkHarness.StartAsync(systemClock: true,
            relay: new Uri("https://relay.invalid"), connectRelay: (_, _) => Task.FromResult(relay.Connect()));
        RuntimeConnection a = harness.Pair(harness.Estate.BuilderId, "A");
        RelayRoute route = await RouteAsync(harness, a);

        Assert.Equal("HTTP 200", await RelayedAttemptAsync(harness, relay, route, a));
        // One byte of the hello's random, rewritten in flight in either direction, leaves the two ends with different
        // transcripts: the handshake fails and no request is answered.
        Assert.Equal("no response", await RelayedAttemptAsync(harness, relay, route, a, inner => new FlipOneByte(inner, 20, fromDevice)));
    }

    private static async Task<RelayRoute> RouteAsync(LocalNetworkHarness harness, RuntimeConnection device)
    {
        using var direct = VericairnClient.LocalNetwork(device, harness.Signer(device));
        RelayRoute? route = device.Relay;
        for (int i = 0; route is null && i < 20; i++)
        {
            await Task.Delay(100, TestStop);
            route = await direct.RefreshRelayAsync(TestStop);
        }

        return Assert.IsType<RelayRoute>(route);
    }

    // TLS straight over the relayed stream, presenting whichever device's certificate the test chooses with the route's ticket.
    private static async Task<string> RelayedAttemptAsync(LocalNetworkHarness harness, InProcessRelay relay, RelayRoute route, RuntimeConnection who,
        Func<Stream, Stream>? tamper = null)
    {
        DeviceRequestSigner signer = harness.Signer(who);
        using var credential = new LocalNetworkCredential(signer, Base64Url.DecodeFromChars(who.DevicePublicKey));
        WebSocket ws = relay.Connect();
        await RelayProtocol.SendAsync(ws, new("join", Ticket: route.Ticket), TestStop);
        Assert.Equal("joined", (await RelayProtocol.ReceiveAsync(ws, TestStop))?.Kind);
        Stream relayed = new RelayStream(ws);
        using var tls = new SslStream(tamper is null ? relayed : tamper(relayed), false);
        var options = LocalNetworkTls.ClientOptions(LocalNetworkTls.Current, (_, c, _, _) => c is not null, credential.Current());
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(TestStop);
        deadline.CancelAfter(TimeSpan.FromSeconds(8));
        try
        {
            await tls.AuthenticateAsClientAsync(options, deadline.Token);
            const string path = "/v1/health";
            string signed = RequestSigning.FormatTime(DateTimeOffset.UtcNow);
            string signature = Base64Url.EncodeToString(signer.Sign(harness.Digest("GET", path, signed, [])));
            byte[] request = Encoding.ASCII.GetBytes($"GET {path} HTTP/1.1\r\nHost: {who.Address}\r\n{RequestSigning.DeviceHeader}: {who.Device}\r\n{RequestSigning.SignedAtHeader}: {signed}\r\n{RequestSigning.SignatureHeader}: {signature}\r\n\r\n");
            await tls.WriteAsync(request, deadline.Token);
            return "HTTP " + (await RawHttp.ReadResponseAsync(tls, deadline.Token)).Status;
        }
        catch (Exception error) when (error is IOException or AuthenticationException or WebSocketException or OperationCanceledException or InvalidOperationException)
        {
            return "no response";
        }
    }

    // A relay that rewrites the byte at one offset of the stream, in the bytes the device sends or in those it receives.
    private sealed class FlipOneByte(Stream inner, long offset, bool sent) : Stream
    {
        private long written;
        private long read;

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override void Flush() => inner.Flush();
        public override Task FlushAsync(CancellationToken cancellationToken) => inner.FlushAsync(cancellationToken);
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override int Read(byte[] buffer, int offset, int count) => ReadAsync(buffer.AsMemory(offset, count)).AsTask().GetAwaiter().GetResult();
        public override void Write(byte[] buffer, int offset, int count) => WriteAsync(buffer.AsMemory(offset, count)).AsTask().GetAwaiter().GetResult();

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            int count = await inner.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (!sent && read <= offset && offset < read + count) buffer.Span[(int)(offset - read)] ^= 0x01;
            read += count;
            return count;
        }

        public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (sent && written <= offset && offset < written + buffer.Length)
            {
                byte[] copy = buffer.ToArray();
                copy[offset - written] ^= 0x01;
                buffer = copy;
            }

            written += buffer.Length;
            await inner.WriteAsync(buffer, cancellationToken).ConfigureAwait(false);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) inner.Dispose();
            base.Dispose(disposing);
        }
    }

    [Theory]
    [InlineData("192.0.2.7", "192.0.2.7")]
    [InlineData("::ffff:192.0.2.7", "192.0.2.7")]
    [InlineData("2001:db8:1:2:aaaa:bbbb:cccc:dddd", "2001:db8:1:2::/64")]
    [InlineData("2001:db8:1:2:1::1", "2001:db8:1:2::/64")]
    public void LimitsCountAnIPv6ClientByItsSlash64(string address, string peer) =>
        Assert.Equal(peer, RelayServer.PeerOf(System.Net.IPAddress.Parse(address)));

    private static string AddressOf(ECDsa key) => RelayProtocol.AddressForKey(Base64Url.EncodeToString(key.ExportSubjectPublicKeyInfo()));

    [Fact]
    public async Task RuntimeRegistrationAndItsRevocationsSurviveRestart()
    {
        DaemonTestHome.Ensure();
        using var estate = new TestEstate(2);
        using EstateService service = EstateService.Open(estate.Root);
        WriteOutcome created = service.DeviceCreate(new DeviceCreateRequest(estate.OperatorId, "Test device", "file", ["relay"]));
        Assert.True(created.Ok, created.Problem?.Message);
        string device = created.Written!.Id;
        WriteOutcome admitted = service.DeviceAdmit(new AdmitRequest(device, estate.OperatorId, "Fixture."));
        Assert.True(admitted.Ok, admitted.Problem?.Message);

        var address = new Uri("https://relay.invalid");
        string state = Path.Combine(estate.Root, "local", "relay");
        Directory.CreateDirectory(state);
        using var firstStop = CancellationTokenSource.CreateLinkedTokenSource(TestStop);
        RelayRoute first;
        await using (var relay = new InProcessRelay(TimeProvider.System))
        using (var runtime = new RuntimeRelay(state, address, service, TimeProvider.System, _ => { }, (_, _) => Task.FromResult(relay.Connect())))
        {
            Task running = runtime.RunAsync(firstStop.Token);
            try
            {
                RelayRoute? available = null;
                for (int i = 0; available is null && i < 50; i++)
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(20), TestStop);
                    available = runtime.Issue(device);
                }

                first = Assert.IsType<RelayRoute>(available);
                using WebSocket phone = relay.Connect();
                using var deadline = CancellationTokenSource.CreateLinkedTokenSource(TestStop);
                deadline.CancelAfter(TimeSpan.FromSeconds(2));
                await RelayProtocol.SendAsync(phone, new("join", Ticket: first.Ticket), deadline.Token);
                Assert.Equal("joined", (await RelayProtocol.ReceiveAsync(phone, deadline.Token))?.Kind);
                await using ConnectionContext connection = Assert.IsAssignableFrom<ConnectionContext>(await runtime.Listener.AcceptAsync(deadline.Token));
                Assert.Equal(device, connection.Items[RelayListener.TicketDevice]);
                WriteOutcome revoked = service.DeviceRevoke(new RevokeRequest(device, estate.OperatorId, "Fixture revoked."));
                Assert.True(revoked.Ok, revoked.Problem?.Message);
                Assert.Null(runtime.Issue(device));
                Assert.True(connection.ConnectionClosed.IsCancellationRequested);
            }
            finally
            {
                await firstStop.CancelAsync();
                await running;
            }
        }

        using var secondStop = CancellationTokenSource.CreateLinkedTokenSource(TestStop);
        await using var restartedRelay = new InProcessRelay(TimeProvider.System);
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var restarted = new RuntimeRelay(state, address, service, TimeProvider.System,
            message => { if (message == "relay registration current") ready.TrySetResult(); }, (_, _) => Task.FromResult(restartedRelay.Connect()));
        Task registration = restarted.RunAsync(secondStop.Token);
        try
        {
            await ready.Task.WaitAsync(TimeSpan.FromSeconds(5), TestStop);
            Assert.Null(restarted.Issue(device));
            await restartedRelay.RefusesAsync(first.Ticket);
        }
        finally
        {
            await secondStop.CancelAsync();
            await registration;
        }
    }

    [Fact]
    public async Task PairedDeviceRefreshesAndUsesItsTicketWithThePinnedIdentity()
    {
        await using var relay = new InProcessRelay(TimeProvider.System);
        await using LocalNetworkHarness harness = await LocalNetworkHarness.StartAsync(systemClock: true,
            relay: new Uri("https://relay.invalid"), connectRelay: (_, _) => Task.FromResult(relay.Connect()));

        RuntimeConnection phone = harness.Pair(harness.Estate.BuilderId);
        using var direct = VericairnClient.LocalNetwork(phone, harness.Signer(phone));
        RelayRoute? route = phone.Relay;
        for (int i = 0; route is null && i < 20; i++)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(100), TestStop);
            route = await direct.RefreshRelayAsync(TestStop);
        }

        RelayRoute first = Assert.IsType<RelayRoute>(route);
        Assert.Equal(phone.Device, first.Ticket.Device);
        Assert.Contains("relay", phone.Profiles);
        phone = phone with { Relay = first };
        using var remote = VericairnClient.ViaRelay(phone, harness.Signer(phone), TimeSpan.FromSeconds(5), (_, _) => Task.FromResult(relay.Connect()));
        Assert.Equal((await direct.HealthAsync(TestStop)).Api, (await remote.HealthAsync(TestStop)).Api);
        RelayRoute next = Assert.IsType<RelayRoute>(await remote.RefreshRelayAsync(TestStop));
        Assert.Equal(first.Ticket.Address, next.Ticket.Address);
        Assert.True(next.Ticket.Counter > first.Ticket.Counter);
        Assert.ThrowsAny<HttpRequestException>(remote.Shutdown);

        DeviceRequestSigner signer = harness.Signer(phone);
        using var credential = new LocalNetworkCredential(signer, Base64Url.DecodeFromChars(phone.DevicePublicKey));
        await using Stream stream = await LocalNetworkTransport.ConnectAsync(phone, signer, credential, TestStop,
            route: next, relayConnect: (_, _) => Task.FromResult(relay.Connect()));
        const string path = "/v1/health";
        string signed = RequestSigning.FormatTime(DateTimeOffset.UtcNow);
        string signature = Base64Url.EncodeToString(signer.Sign(harness.Digest("GET", path, signed, [])));
        byte[] request = Encoding.ASCII.GetBytes($"GET {path} HTTP/1.1\r\nHost: {phone.Address}\r\n{RequestSigning.DeviceHeader}: {phone.Device}\r\n{RequestSigning.SignedAtHeader}: {signed}\r\n{RequestSigning.SignatureHeader}: {signature}\r\n\r\n");
        await stream.WriteAsync(request, TestStop);
        Assert.Equal(200, (await RawHttp.ReadResponseAsync(stream, TestStop)).Status);
        await stream.WriteAsync(request, TestStop);
        Assert.Equal(401, (await RawHttp.ReadResponseAsync(stream, TestStop)).Status);

        using ECDsa wrong = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        RuntimeConnection altered = phone with { Identity = Base64Url.EncodeToString(wrong.ExportSubjectPublicKeyInfo()) };
        using var refused = VericairnClient.ViaRelay(altered, harness.Signer(phone), TimeSpan.FromSeconds(5), (_, _) => Task.FromResult(relay.Connect()));
        await Assert.ThrowsAnyAsync<HttpRequestException>(() => refused.HealthAsync(TestStop));

        Assert.True(harness.Socket.DeviceRevoke(new RevokeRequest(phone.Device, harness.Estate.OperatorId, "Test revocation.")).Ok);
        await Assert.ThrowsAnyAsync<HttpRequestException>(() => remote.HealthAsync(TestStop));
    }

    private sealed class InProcessRelay(TimeProvider clock, RelayLimits? limits = null) : IAsyncDisposable
    {
        private readonly RelayServer server = new(clock, limits);
        private readonly CancellationTokenSource stopped = CancellationTokenSource.CreateLinkedTokenSource(TestStop);
        private readonly List<Task> handlers = [];

        internal int Unverified(string address) => server.Unverified(address);

        internal WebSocket Connect(string peer = "in-process")
        {
            var incoming = new Pipe();
            var outgoing = new Pipe();
            var client = new Wire(incoming.Reader.AsStream(), outgoing.Writer.AsStream());
            var relay = new Wire(outgoing.Reader.AsStream(), incoming.Writer.AsStream());
            lock (handlers)
            {
                handlers.Add(RunAsync(relay, peer));
            }

            return WebSocket.CreateFromStream(client, false, null, Timeout.InfiniteTimeSpan);
        }

        private async Task RunAsync(Stream transport, string peer)
        {
            using WebSocket socket = WebSocket.CreateFromStream(transport, true, null, Timeout.InfiniteTimeSpan);
            await server.HandleAsync(socket, peer, stopped.Token);
        }

        // Whether a registration of the address with the key is accepted: the relay answers "registered" or closes without a word.
        internal async Task<bool> TryRegisterAsync(string address, ECDsa key, string peer = "in-process")
        {
            using WebSocket control = Connect(peer);
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(TestStop);
            deadline.CancelAfter(TimeSpan.FromSeconds(2));
            try
            {
                await RelayProtocol.SendAsync(control, new("register", address, Base64Url.EncodeToString(key.ExportSubjectPublicKeyInfo())), deadline.Token);
                RelayMessage? challenge = await RelayProtocol.ReceiveAsync(control, deadline.Token);
                if (challenge?.Kind != "challenge") return false;
                await RelayProtocol.SendAsync(control, new("prove", Signature: RelayProtocol.Prove(key, address, challenge.Nonce!)), deadline.Token);
                await RelayProtocol.SendAsync(control, new("ready"), deadline.Token);
                return (await RelayProtocol.ReceiveAsync(control, deadline.Token))?.Kind == "registered";
            }
            catch (WebSocketException)
            {
                // A refused registration is closed without a word.
                return false;
            }
        }

        internal async Task<WebSocket> RegisterAsync(string address, ECDsa key, string peer = "in-process")
        {
            WebSocket control = Connect(peer);
            await RelayProtocol.SendAsync(control, new("register", address, Base64Url.EncodeToString(key.ExportSubjectPublicKeyInfo())), TestStop);
            RelayMessage challenge = Assert.IsType<RelayMessage>(await RelayProtocol.ReceiveAsync(control, TestStop));
            Assert.Equal("challenge", challenge.Kind);
            await RelayProtocol.SendAsync(control, new("prove", Signature: RelayProtocol.Prove(key, address, challenge.Nonce!)), TestStop);
            await RelayProtocol.SendAsync(control, new("ready"), TestStop);
            Assert.Equal("registered", (await RelayProtocol.ReceiveAsync(control, TestStop))?.Kind);
            return control;
        }

        internal async Task<(RelayStream, RelayStream)> JoinAsync(string address, WebSocket control, RelayTicket ticket, string peer = "in-process")
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(TestStop);
            deadline.CancelAfter(TimeSpan.FromSeconds(2));
            WebSocket phone = Connect(peer);
            await RelayProtocol.SendAsync(phone, new("join", Ticket: ticket), deadline.Token);
            RelayMessage request = Assert.IsType<RelayMessage>(await RelayProtocol.ReceiveAsync(control, deadline.Token));
            Assert.Equal("stream", request.Kind);
            WebSocket runtime = Connect();
            await RelayProtocol.SendAsync(runtime, new("accept", address, Nonce: request.Nonce), deadline.Token);
            Assert.Equal("joined", (await RelayProtocol.ReceiveAsync(phone, deadline.Token))?.Kind);
            return (new RelayStream(phone), new RelayStream(runtime));
        }

        internal async Task RefusesAsync(RelayTicket? ticket, string peer = "in-process")
        {
            using WebSocket phone = Connect(peer);
            await RelayProtocol.SendAsync(phone, new("join", Ticket: ticket), TestStop);
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(TestStop);
            deadline.CancelAfter(TimeSpan.FromSeconds(2));
            await Assert.ThrowsAnyAsync<WebSocketException>(() => RelayProtocol.ReceiveAsync(phone, deadline.Token));
        }

        public async ValueTask DisposeAsync()
        {
            await stopped.CancelAsync();
            await Task.WhenAll(handlers);
            stopped.Dispose();
        }
    }

    private sealed class Wire(Stream input, Stream output) : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override void Flush() => output.Flush();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override int Read(byte[] buffer, int offset, int count) => input.Read(buffer, offset, count);
        public override void Write(byte[] buffer, int offset, int count) => output.Write(buffer, offset, count);
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) => input.ReadAsync(buffer, cancellationToken);
        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default) => output.WriteAsync(buffer, cancellationToken);

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                input.Dispose();
                output.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}
