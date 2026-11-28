// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Buffers.Text;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using System.Threading.Channels;
using Vericairn.Protocol;
using Vericairn.Runtime.Service;

namespace Vericairn.Daemon;

/// <summary>Owns this estate's relay registration and tickets. The private registration key stays in the owner-only runtime directory.</summary>
internal sealed class RuntimeRelay : IDisposable
{
    private readonly string path;
    private readonly Uri url;
    private readonly EstateService service;
    private readonly TimeProvider clock;
    private readonly Action<string> log;
    private readonly ECDsa key;
    private readonly State state;
    private readonly object gate = new();
    private readonly Dictionary<CancellationTokenSource, RelayTicket> streams = [];
    private readonly Func<Uri, CancellationToken, Task<WebSocket>> connect;
    private Channel<RelayMessage>? control;
    private WebSocket? registered;
    private long revocations;
    private readonly string publicKey;
    private const int StreamLimit = DaemonHost.ConcurrentConnections / 2;

    internal RuntimeRelay(string directory, Uri url, EstateService service, TimeProvider clock, Action<string> log, Func<Uri, CancellationToken, Task<WebSocket>>? connect)
    {
        path = Path.Combine(directory, "relay-registration.json");
        this.url = url;
        this.service = service;
        this.clock = clock;
        this.log = log;
        this.connect = connect ?? ConnectAsync;
        key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        try
        {
            State? kept = File.Exists(path)
                ? JsonSerializer.Deserialize<State>(File.ReadAllBytes(path), ProtocolJson.Options)
                    ?? throw new InvalidOperationException("The runtime's relay registration cannot be read.")
                : null;
            if (kept?.Url == url.AbsoluteUri)
            {
                byte[] secret = Base64Url.DecodeFromChars(kept.Key);
                try
                {
                    key.ImportPkcs8PrivateKey(secret, out int read);
                    if (read != secret.Length || key.ExportParameters(false).Curve.Oid.Value != "1.2.840.10045.3.1.7")
                    {
                        throw new InvalidOperationException("The runtime's relay registration key must use P-256.");
                    }
                }
                finally
                {
                    CryptographicOperations.ZeroMemory(secret);
                }
                if (!RelayProtocol.IsId(kept.Address) || kept.Counter < 0 || kept.Devices is null)
                {
                    throw new InvalidOperationException("The runtime's relay registration is invalid.");
                }

                publicKey = Base64Url.EncodeToString(key.ExportSubjectPublicKeyInfo());
                state = kept with { Address = RelayProtocol.AddressForKey(publicKey) };
                if (state.Address != kept.Address)
                {
                    // Upgrade old random addresses or use the address of a replaced key. The next ticket carries the new route.
                    Save();
                }
            }
            else
            {
                publicKey = Base64Url.EncodeToString(key.ExportSubjectPublicKeyInfo());
                byte[] secret = key.ExportPkcs8PrivateKey();
                try
                {
                    state = new State(url.AbsoluteUri, RelayProtocol.AddressForKey(publicKey), Base64Url.EncodeToString(secret), new(StringComparer.Ordinal));
                    Save();
                }
                finally
                {
                    CryptographicOperations.ZeroMemory(secret);
                }
            }

            service.DeviceAdmissionRevoked += RevokeAfterWrite;
        }
        catch
        {
            key.Dispose();
            throw;
        }
    }

    internal RelayListener Listener { get; } = new();

    internal RelayRoute? Issue(string device)
    {
        long before = Interlocked.Read(ref revocations);
        DeviceStanding? standing = service.Standing(device);
        if (standing?.Identity != device || !Admitted(standing))
        {
            return null;
        }

        lock (gate)
        {
            long now = Now();
            if (registered is null || revocations != before)
            {
                return null;
            }

            Prune(now);
            Issuance? previous = state.Devices.GetValueOrDefault(device);

            long counter = checked(++state.Counter);
            RelayTicket ticket = RelayProtocol.Issue(key, state.Address, device, now, counter);
            state.Devices[device] = new Issuance(counter, ticket.Expires, standing.PublicKey, previous?.RevokedThrough ?? 0);
            try
            {
                Save();
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                if (previous is null)
                {
                    state.Devices.Remove(device);
                }
                else
                {
                    state.Devices[device] = previous;
                }

                log("a relay ticket could not be saved");
                return null;
            }

            return new RelayRoute(url.AbsoluteUri, ticket);
        }
    }

    internal bool PermitsCertificate(string device, X509Certificate2 certificate)
    {
        DeviceStanding? standing = service.Standing(device);
        return Admitted(standing) && RequestAuthenticator.IssuedBy(certificate, standing!.PublicKey, clock.GetUtcNow());
    }

    internal async Task RunAsync(CancellationToken cancellationToken)
    {
        int delay = 1;
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await RegisterAsync(cancellationToken).ConfigureAwait(false);
                delay = 1;
            }
            catch (Exception error) when (error is IOException or InvalidOperationException or WebSocketException or OperationCanceledException or JsonException or CryptographicException)
            {
                if (!cancellationToken.IsCancellationRequested)
                {
                    log($"relay registration ended ({error.GetType().Name}); reconnecting");
                }
            }

            if (cancellationToken.IsCancellationRequested)
            {
                return;
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(delay), clock, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            delay = Math.Min(delay * 2, 60);
        }
    }

    private async Task RegisterAsync(CancellationToken cancellationToken)
    {
        Refresh();
        using var setup = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        setup.CancelAfter(TimeSpan.FromSeconds(10));
        using WebSocket socket = await connect(url, setup.Token).ConfigureAwait(false);
        await RelayProtocol.SendAsync(socket, new("register", state.Address, publicKey), setup.Token).ConfigureAwait(false);
        if (await RelayProtocol.ReceiveAsync(socket, setup.Token).ConfigureAwait(false) is not { Kind: "challenge", Nonce: string nonce } || !RelayProtocol.IsId(nonce))
        {
            throw new IOException("The relay did not issue a registration challenge.");
        }

        string proof;
        RelayMessage[] revoked;
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        Channel<RelayMessage> messages = Channel.CreateBounded<RelayMessage>(128);
        var active = new HashSet<Task>();
        Task sending = Task.CompletedTask;
        Task working = Task.CompletedTask;
        try
        {
            lock (gate)
            {
                proof = RelayProtocol.Prove(key, state.Address, nonce);
                revoked = [.. state.Devices.Where(item => item.Value.RevokedThrough > 0)
                    .Select(item => new RelayMessage("revoke", Device: item.Key, Counter: item.Value.RevokedThrough))];
                control = messages;
            }

            await RelayProtocol.SendAsync(socket, new("prove", Signature: proof), setup.Token).ConfigureAwait(false);
            foreach (RelayMessage message in revoked)
            {
                await RelayProtocol.SendAsync(socket, message, setup.Token).ConfigureAwait(false);
            }
            await RelayProtocol.SendAsync(socket, new("ready"), setup.Token).ConfigureAwait(false);
            if (await RelayProtocol.ReceiveAsync(socket, setup.Token).ConfigureAwait(false) is not { Kind: "registered" })
            {
                throw new IOException("The relay refused registration.");
            }
            lock (gate)
            {
                registered = socket;
            }

            log("relay registration current");
            sending = SendAsync(socket, messages.Reader, stop.Token);
            working = RefreshAsync(stop.Token);
            while (!stop.IsCancellationRequested)
            {
                Task<RelayMessage?> read = RelayProtocol.ReceiveAsync(socket, stop.Token);
                if (await Task.WhenAny(read, sending, working).ConfigureAwait(false) != read)
                {
                    await stop.CancelAsync().ConfigureAwait(false);
                    await read.ConfigureAwait(false);
                    break;
                }

                if (await read.ConfigureAwait(false) is not { Kind: "stream", Nonce: string join, Ticket: RelayTicket ticket } || !RelayProtocol.IsId(join))
                {
                    break;
                }

                active.RemoveWhere(task => task.IsCompleted);
                if (active.Count < StreamLimit)
                {
                    active.Add(OpenAsync(join, ticket, stop.Token));
                }
            }
        }
        finally
        {
            lock (gate)
            {
                registered = null;
                control = null;
            }

            await stop.CancelAsync().ConfigureAwait(false);
            try
            {
                await Task.WhenAll(active.Append(sending).Append(working)).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stop.IsCancellationRequested)
            {
                // A control channel end closes every subordinate connection.
            }
        }
    }

    private static async Task SendAsync(WebSocket socket, ChannelReader<RelayMessage> messages, CancellationToken stop)
    {
        await foreach (RelayMessage message in messages.ReadAllAsync(stop).ConfigureAwait(false))
        {
            await RelayProtocol.SendAsync(socket, message, stop).ConfigureAwait(false);
        }
    }

    private async Task RefreshAsync(CancellationToken stop)
    {
        using var tick = new PeriodicTimer(TimeSpan.FromSeconds(1), clock);
        while (await tick.WaitForNextTickAsync(stop).ConfigureAwait(false))
        {
            Refresh();
        }
    }

    private void Refresh()
    {
        KeyValuePair<string, Issuance>[] issued;
        lock (gate)
        {
            Prune(Now());
            issued = [.. state.Devices.Where(item => !item.Value.Revoked)];
        }

        foreach ((string device, Issuance issuance) in issued)
        {
            DeviceStanding? standing = service.Standing(device);
            if (!Admitted(standing) || standing!.PublicKey != issuance.PublicKey)
            {
                Revoke(device, issuance.Counter);
            }
        }
    }

    private void RevokeAfterWrite(string device) => Revoke(service.Standing(device)?.Identity ?? device);

    private void Revoke(string device, long? expected = null)
    {
        lock (gate)
        {
            Interlocked.Increment(ref revocations);
            if (!state.Devices.TryGetValue(device, out Issuance? current) || current.Revoked || expected.HasValue && current.Counter != expected)
            {
                return;
            }

            state.Devices[device] = current with { RevokedThrough = current.Counter };
            foreach ((CancellationTokenSource stop, RelayTicket ticket) in streams)
            {
                if (ticket.Device == device)
                {
                    stop.Cancel();
                }
            }

            if (control?.Writer.TryWrite(new("revoke", Device: device, Counter: current.Counter)) == false)
            {
                registered?.Abort();
            }

            // The runtime already checks standing on this path, even if the relay is unreachable or the state file cannot be saved.
            try
            {
                Save();
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                registered?.Abort();
                log("relay registration closed because its revocation could not be saved");
            }
        }
    }

    private async Task OpenAsync(string nonce, RelayTicket ticket, CancellationToken cancellationToken)
    {
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        try
        {
            long now = Now();
            if (!RelayProtocol.Verify(ticket, state.Address, publicKey, now) || service.Standing(ticket.Device) is not { } standing || !Admitted(standing))
            {
                return;
            }

            lock (gate)
            {
                if (!state.Devices.TryGetValue(ticket.Device, out Issuance? issued)
                    || issued.RevokedThrough >= ticket.Counter || issued.PublicKey != standing.PublicKey)
                {
                    return;
                }

                streams.Add(stop, ticket);
            }

            stop.CancelAfter(TimeSpan.FromSeconds(ticket.Expires - now));
            using var setup = CancellationTokenSource.CreateLinkedTokenSource(stop.Token);
            setup.CancelAfter(TimeSpan.FromSeconds(10));
            using WebSocket socket = await connect(url, setup.Token).ConfigureAwait(false);
            await RelayProtocol.SendAsync(socket, new("accept", state.Address, Nonce: nonce), setup.Token).ConfigureAwait(false);
            using var stream = new RelayStream(socket);
            await Listener.ServeAsync(stream, ticket.Device, stop.Token).ConfigureAwait(false);
        }
        catch (Exception error) when (error is IOException or OperationCanceledException or WebSocketException or ObjectDisposedException or JsonException)
        {
            // One failed join never ends the registration.
        }
        finally
        {
            lock (gate)
            {
                streams.Remove(stop);
            }
        }
    }

    private void Prune(long now)
    {
        // A relay whose clock is behind can still see an apparently unexpired ticket.
        foreach (string device in state.Devices.Where(item => item.Value.Expires <= now - RelayProtocol.ClockSkew).Select(item => item.Key).ToArray())
        {
            state.Devices.Remove(device);
        }
    }

    private void Save()
    {
        string temporary = path + ".tmp";
        var options = new FileStreamOptions { Mode = FileMode.Create, Access = FileAccess.Write, Share = FileShare.None };
        if (!OperatingSystem.IsWindows())
        {
            options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        }
        using (var file = new FileStream(temporary, options))
        {
            JsonSerializer.Serialize(file, state, ProtocolJson.Options);
        }
        File.Move(temporary, path, overwrite: true);
    }

    private static bool Admitted(DeviceStanding? standing) =>
        standing?.Admission == "admitted" && standing.Profiles.Contains(RequestAuthenticator.RelayProfile, StringComparer.Ordinal);

    private long Now() => clock.GetUtcNow().ToUnixTimeSeconds();

    private static async Task<WebSocket> ConnectAsync(Uri url, CancellationToken stop) =>
        await RelayProtocol.ConnectAsync(url, stop).ConfigureAwait(false);

    public void Dispose()
    {
        service.DeviceAdmissionRevoked -= RevokeAfterWrite;
        key.Dispose();
    }

    private sealed record Issuance(long Counter, long Expires, string PublicKey, long RevokedThrough)
    {
        internal bool Revoked => Counter <= RevokedThrough;
    }

    private sealed record State(string Url, string Address, string Key, Dictionary<string, Issuance> Devices)
    {
        public long Counter { get; set; }
    }
}
