// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Buffers.Text;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Vericairn.Protocol;
using Vericairn.Records.Identity;
using Vericairn.Records.Signing;
using Vericairn.Records.Tests;
using Vericairn.Sdk;

namespace Vericairn.Daemon.Tests;

/// <summary>
/// A daemon serving the local-network profile on 127.0.0.1 only, with the operator's own device admitted as the estate's first
/// authority, and a second "machine" for the devices that pair: its own home for their keys and connections. Tests never listen
/// beyond loopback.
/// </summary>
internal sealed class LocalNetworkHarness : IAsyncDisposable
{
    private readonly DaemonOptions options;

    private LocalNetworkHarness(TestEstate estate, DaemonHost host, VericairnClient socket, ManualTime time, string deviceHome, string operatorDevice, DaemonOptions options)
    {
        this.options = options;
        Estate = estate;
        Host = host;
        Socket = socket;
        Time = time;
        DeviceHome = deviceHome;
        DeviceKeys = new FileKeyStore(deviceHome);
        Connections = new RuntimeConnections(deviceHome, DeviceKeys);
        OperatorDevice = operatorDevice;
    }

    public TestEstate Estate { get; }

    public DaemonHost Host { get; private set; }

    /// <summary>The administrator's client on the runtime's own machine, over the local socket.</summary>
    public VericairnClient Socket { get; private set; }

    /// <summary>The clock pairing codes, request signatures and the certificate schedule are judged by, unless the harness runs on the system clock.</summary>
    public ManualTime Time { get; }

    /// <summary>The second machine's home: the keys and connections of the devices that pair.</summary>
    public string DeviceHome { get; }

    public FileKeyStore DeviceKeys { get; }

    public RuntimeConnections Connections { get; }

    public string OperatorDevice { get; }

    /// <summary>Where the local-network profile listens: 127.0.0.1 and the port the daemon was given.</summary>
    public string Address => $"{Host.Endpoint.LocalNetwork!.Host}:{Host.Endpoint.LocalNetwork.Port}";

    /// <summary>What a local-network request is signed for: this estate and the runtime identity now.</summary>
    public (string Estate, string Identity) Binding => (Host.Service.Session.Manifest.Estate, Host.Identity!.PublicKey);

    /// <summary>The digest a paired device signs for a request to this runtime: version 2, or version 1 for the identity proof's request, as the SDK signs.</summary>
    public byte[] Digest(string method, string target, string signedAt, ReadOnlySpan<byte> body) =>
        target.Split('?')[0] == RuntimeIdentityScheme.Path
            ? RequestSigning.Digest(method, target, signedAt, body)
            : RequestSigning.LocalNetworkDigest(Binding.Estate, Binding.Identity, method, target, signedAt, body);

    /// <summary>Starts the runtime on loopback's local-network profile, on a clock the test moves, or on the system's when <paramref name="systemClock"/>.</summary>
    public static async Task<LocalNetworkHarness> StartAsync(long recordFormat = 2, int replayCapacity = 10_000, bool systemClock = false, Action<string>? log = null)
    {
        DaemonTestHome.Ensure();
        var estate = new TestEstate(recordFormat);
        var time = new ManualTime(DateTimeOffset.UtcNow, running: true);
        var options = new DaemonOptions { LocalNetwork = new IPEndPoint(IPAddress.Loopback, 0), IdentityStore = new FileKeyStore(VericairnHome.Path()), Time = systemClock ? TimeProvider.System : time, ReplayCapacity = replayCapacity, Log = log };
        DaemonHost host = await DaemonTestHome.StartAsync(
            estate.Root,
            options: options,
            cancellationToken: TestContext.Current.CancellationToken);
        var socket = new VericairnClient(host.Endpoint);

        // The operator's own laptop, the estate's first authority: it signs every admission a pairing ends with.
        WriteOutcome device = socket.DeviceCreate(new DeviceCreateRequest(estate.OperatorId, "Operator laptop", "file"));
        Assert.True(device.Ok, Failure(device));
        WriteOutcome admitted = socket.DeviceAdmit(new AdmitRequest(device.Written!.Id, estate.OperatorId, "The first authority."));
        Assert.True(admitted.Ok, Failure(admitted));
        string deviceHome = Path.Combine(Path.GetTempPath(), "vericairn-second-machine-" + Ulid.NewUlid().Value);
        Directory.CreateDirectory(deviceHome);
        return new LocalNetworkHarness(estate, host, socket, time, deviceHome, device.Written.Id, options);

        // Keep the validation findings: a generic rejection hid the cause of F7's failure during this setup, before its clock move.
        static string Failure(WriteOutcome outcome) => outcome.Problem?.Message + "\n"
            + string.Join('\n', outcome.Problem?.Findings.Select(f => $"{f.Code}: {f.Message}") ?? []);
    }

    /// <summary>A new runtime instance over the same estate, identity and clock, with an empty replay cache.</summary>
    public async Task RestartAsync()
    {
        Socket.Dispose();
        await Host.DisposeAsync();
        Host = await DaemonTestHome.StartAsync(Estate.Root, options: options, cancellationToken: TestContext.Current.CancellationToken);
        Socket = new VericairnClient(Host.Endpoint);
    }

    /// <summary>A pairing code the operator issues for a principal.</summary>
    public PairingIssued Issue(string principal, params string[] profiles)
    {
        (PairingIssued? issued, Problem? problem) = Socket.IssuePairing(new PairingIssueRequest(principal, Estate.OperatorId, profiles.Length == 0 ? null : profiles));
        Assert.True(issued is not null, problem?.Message);
        return issued;
    }

    /// <summary>Pairs a new device of the second machine for a principal and keeps its connection.</summary>
    public RuntimeConnection Pair(string principal, string name = "Phone", params string[] profiles) =>
        Connections.Add(Address, Issue(principal, profiles).Code, new PairingOptions(name));

    /// <summary>The signer of a paired device, its key held on the second machine.</summary>
    public DeviceRequestSigner Signer(RuntimeConnection connection) => new(DeviceKeys, connection.Device);

    internal Func<Task> WhenReadingBody(Action action)
    {
        var entered = TestTiming.Signal();
        Host.Authenticator.ReadingBody = () => entered.TrySetResult();
        return async () =>
        {
            await entered.Task.WaitAsync(TestTiming.HangTimeout, TestContext.Current.CancellationToken);
            Host.Authenticator.ReadingBody = null;
            action();
        };
    }

    public async ValueTask DisposeAsync()
    {
        Socket.Dispose();
        await Host.DisposeAsync();
        Estate.Dispose();
        try
        {
            Directory.Delete(DeviceHome, recursive: true);
        }
        catch (IOException)
        {
            // Best effort.
        }
    }
}

/// <summary>A clock a test moves by hand; optionally follows the system clock between advances, as SDK clients do.</summary>
internal sealed class ManualTime(DateTimeOffset start, bool running = false) : TimeProvider
{
    private readonly ClientClock timers = new(allowPeriodic: true);

    public override DateTimeOffset GetUtcNow() => (running ? DateTimeOffset.UtcNow : start).AddTicks(timers.GetTimestamp());
    public override long TimestampFrequency => timers.TimestampFrequency;
    public override long GetTimestamp() => timers.GetTimestamp();
    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period) =>
        timers.CreateTimer(callback, state, dueTime, period);

    public void Advance(TimeSpan by)
    {
        timers.Elapse(by);
    }
}

/// <summary>
/// A client of the local-network profile that sends exactly what a test says: mutual TLS with the certificate given (or none),
/// pinned to the runtime certificate the test names, and the request headers as given. It counts the TLS connections it opens,
/// so a test can tell an open connection from a new one.
/// </summary>
internal sealed class MutualTls : IDisposable
{
    private readonly HttpClient http;
    private readonly LocalNetworkHarness harness;
    private int connections;

    public MutualTls(LocalNetworkHarness harness, X509Certificate2? certificate, string runtimeCertificate)
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
                Interlocked.Increment(ref connections);
                return tls;
            },
        };
        http = new HttpClient(handler, disposeHandler: true) { BaseAddress = new Uri($"http://{address}/") };
    }

    /// <summary>How many TLS connections this client opened.</summary>
    public int Connections => Volatile.Read(ref connections);

    /// <summary>The three request-signing headers for a request, signed now by a device for this runtime.</summary>
    public (string Device, string SignedAt, string Signature) Sign(IRequestSigner signer, string method, string target, string? json = null)
    {
        string signedAt = RequestSigning.FormatTime(DateTimeOffset.UtcNow);
        byte[] digest = harness.Digest(method, target, signedAt, json is null ? [] : Encoding.UTF8.GetBytes(json));
        return (signer.DeviceId, signedAt, Base64Url.EncodeToString(signer.Sign(digest)));
    }

    /// <summary>Sends a request with the given headers; a JSON body when given.</summary>
    public async Task<(HttpStatusCode Status, string Body)> SendAsync(HttpMethod method, string target, (string Device, string SignedAt, string Signature)? headers, string? json = null)
    {
        using var request = new HttpRequestMessage(method, new Uri(target, UriKind.Relative));
        if (json is not null)
        {
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        }

        if (headers is { } signed)
        {
            request.Headers.TryAddWithoutValidation(RequestSigning.DeviceHeader, signed.Device);
            request.Headers.TryAddWithoutValidation(RequestSigning.SignedAtHeader, signed.SignedAt);
            request.Headers.TryAddWithoutValidation(RequestSigning.SignatureHeader, signed.Signature);
        }

        using HttpResponseMessage response = await http.SendAsync(request, TestContext.Current.CancellationToken);
        return (response.StatusCode, await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    /// <summary>Signs a request with a device and sends it.</summary>
    public Task<(HttpStatusCode Status, string Body)> SendSignedAsync(IRequestSigner signer, HttpMethod method, string target, string? json = null) =>
        SendAsync(method, target, Sign(signer, method.Method, target, json), json);

    public void Dispose() => http.Dispose();
}
