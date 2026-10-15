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
    private LocalNetworkHarness(TestEstate estate, DaemonHost host, VericairnClient socket, ManualTime time, string deviceHome, string operatorDevice)
    {
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

    public DaemonHost Host { get; }

    /// <summary>The administrator's client on the runtime's own machine, over the local socket.</summary>
    public VericairnClient Socket { get; }

    /// <summary>The clock pairing codes and the certificate schedule are judged by.</summary>
    public ManualTime Time { get; }

    /// <summary>The second machine's home: the keys and connections of the devices that pair.</summary>
    public string DeviceHome { get; }

    public FileKeyStore DeviceKeys { get; }

    public RuntimeConnections Connections { get; }

    public string OperatorDevice { get; }

    /// <summary>Where the local-network profile listens: 127.0.0.1 and the port the daemon was given.</summary>
    public string Address => $"{Host.Endpoint.LocalNetwork!.Host}:{Host.Endpoint.LocalNetwork.Port}";

    public static async Task<LocalNetworkHarness> StartAsync(long recordFormat = 2, int replayCapacity = 10_000)
    {
        DaemonTestHome.Ensure();
        var estate = new TestEstate(recordFormat);
        var time = new ManualTime(DateTimeOffset.UtcNow);
        DaemonHost host = await DaemonHost.StartAsync(
            estate.Root,
            options: new DaemonOptions { LocalNetwork = new IPEndPoint(IPAddress.Loopback, 0), IdentityStore = new FileKeyStore(VericairnHome.Path()), Time = time, ReplayCapacity = replayCapacity },
            cancellationToken: TestContext.Current.CancellationToken);
        var socket = new VericairnClient(host.Endpoint);

        // The operator's own laptop, the estate's first authority: it signs every admission a pairing ends with.
        WriteOutcome device = socket.DeviceCreate(new DeviceCreateRequest(estate.OperatorId, "Operator laptop", "file"));
        Assert.True(device.Ok, device.Problem?.Message);
        WriteOutcome admitted = socket.DeviceAdmit(new AdmitRequest(device.Written!.Id, estate.OperatorId, "The first authority."));
        Assert.True(admitted.Ok, admitted.Problem?.Message);
        string deviceHome = Path.Combine(Path.GetTempPath(), "vericairn-second-machine-" + Ulid.NewUlid().Value);
        Directory.CreateDirectory(deviceHome);
        return new LocalNetworkHarness(estate, host, socket, time, deviceHome, device.Written.Id);
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

/// <summary>A clock a test moves by hand.</summary>
internal sealed class ManualTime(DateTimeOffset start) : TimeProvider
{
    private DateTimeOffset now = start;

    public override DateTimeOffset GetUtcNow() => now;

    public void Advance(TimeSpan by) => now += by;
}

/// <summary>
/// A client of the local-network profile that sends exactly what a test says: mutual TLS with the certificate given (or none),
/// pinned to the runtime certificate the test names, and the request headers as given. It counts the TLS connections it opens,
/// so a test can tell an open connection from a new one.
/// </summary>
internal sealed class MutualTls : IDisposable
{
    private readonly HttpClient http;
    private int connections;

    public MutualTls(string address, X509Certificate2? certificate, string runtimeCertificate)
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
                Interlocked.Increment(ref connections);
                return tls;
            },
        };
        http = new HttpClient(handler, disposeHandler: true) { BaseAddress = new Uri($"http://{address}/") };
    }

    /// <summary>How many TLS connections this client opened.</summary>
    public int Connections => Volatile.Read(ref connections);

    /// <summary>The three request-signing headers for a request, signed now by a device.</summary>
    public static (string Device, string SignedAt, string Signature) Sign(IRequestSigner signer, string method, string target, string? json = null)
    {
        string signedAt = RequestSigning.FormatTime(DateTimeOffset.UtcNow);
        byte[] digest = RequestSigning.Digest(method, target, signedAt, json is null ? [] : Encoding.UTF8.GetBytes(json));
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
