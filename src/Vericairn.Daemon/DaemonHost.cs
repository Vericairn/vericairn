// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Collections.Concurrent;
using System.IO.Pipelines;
using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Connections;
using Microsoft.AspNetCore.Connections.Features;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.AspNetCore.Server.Kestrel.Https;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Vericairn.Protocol;
using Vericairn.Records.Model;
using Vericairn.Records.Signing;
using Vericairn.Runtime.Service;
using Vericairn.Sdk;

namespace Vericairn.Daemon;

/// <summary>How a daemon is started beyond its estate: the local-network profile, the custody of the runtime identity, the clock and the log.</summary>
public sealed record DaemonOptions
{
    /// <summary>
    /// The address the local-network profile listens on. When null, the runtime settings beside the live store say, and the profile
    /// is off unless the operator configured an address there with 'vericairn runtime configure'.
    /// </summary>
    public IPEndPoint? LocalNetwork { get; init; }

    /// <summary>Where the runtime identity's key is made and held; the strongest custody this machine offers when null.</summary>
    public IDeviceKeyStore? IdentityStore { get; init; }

    /// <summary>The clock that pairing codes and the certificate's schedule are judged by; the system's when null.</summary>
    public TimeProvider? Time { get; init; }

    /// <summary>Where the daemon says what it did (pairings, rotations); nowhere when null.</summary>
    public Action<string>? Log { get; init; }

    /// <summary>
    /// How many signed requests of one device the runtime remembers at once, each until it is stale: within that window a device's
    /// further requests are refused rather than any remembered signature forgotten.
    /// </summary>
    public int ReplayCapacity { get; init; } = 10_000;
}

/// <summary>
/// The daemon: one process owns one open estate and serves the versioned API over a local socket (a Unix domain socket, or a named
/// pipe on Windows), over loopback TLS pinned to the runtime certificate, and, when the operator configured an address, over the
/// local network with mutual TLS to paired devices. Clients never open the live store; they call this. The endpoints are published
/// in a small file beside the live store so that the CLI and the SDK on the same machine can find them.
/// </summary>
public sealed class DaemonHost : IAsyncDisposable
{
    /// <summary>The most connections Kestrel admits at once, across all listeners and including TLS handshakes.</summary>
    internal const int ConcurrentConnections = 32;

    private static readonly object LoopbackMarker = new();
    private static readonly object LocalNetworkMarker = new();
    private static readonly object CertificateMarker = new();
    private static readonly TimeSpan ScheduleCheck = TimeSpan.FromHours(1);

    private readonly string endpointFile;
    private readonly string directory;
    private readonly DaemonOptions options;
    private readonly ConcurrentDictionary<string, ConnectionContext> open = new(StringComparer.Ordinal);
    private readonly object gate = new();
    private readonly object rotation = new();
    private readonly CancellationTokenSource schedule = new();
    private WebApplication? app;
    private Task? scheduled;
    private Task? stopping;
    private ListenOptions? loopbackListener;
    private ListenOptions? localNetworkListener;

    private DaemonHost(EstateService service, string endpointFile, string directory, DaemonOptions options, RuntimeCertificates? certificates, RuntimeIdentity? identity)
    {
        Service = service;
        this.endpointFile = endpointFile;
        this.directory = directory;
        this.options = options;
        Certificates = certificates;
        Identity = identity;
        Time = options.Time ?? TimeProvider.System;
        Pairings = new PairingRegistry(Time, service.PairingAuthorityRefusal);
        Authenticator = new RequestAuthenticator(service, options.ReplayCapacity, () => Identity?.PublicKey, Time);
        Endpoint = new DaemonEndpoint(string.Empty, string.Empty, Environment.ProcessId, string.Empty, service.Root, ApiVersion.Current);
    }

    /// <summary>The service this daemon owns.</summary>
    public EstateService Service { get; }

    /// <summary>Where clients connect, as last published: a rotation publishes the new certificate's fingerprint.</summary>
    public DaemonEndpoint Endpoint { get; private set; }

    /// <summary>The runtime identity's public key (base64url SubjectPublicKeyInfo) that paired devices pin; null without the local-network profile.</summary>
    public string? IdentityPublicKey => Identity?.PublicKey;

    internal RuntimeCertificates? Certificates { get; }

    internal RuntimeIdentity? Identity { get; private set; }

    internal PairingRegistry Pairings { get; }

    internal RequestAuthenticator Authenticator { get; }

    internal TimeProvider Time { get; }

    /// <summary>A test's hook between device authentication and the operation's write, outside the estate's gate.</summary>
    internal Action? BeforeDeviceWrite { get; set; }

    /// <summary>The endpoint file of an estate on this machine, beside its live store.</summary>
    public static string EndpointFilePath(string root) => DaemonDiscovery.EndpointFilePath(root);

    /// <summary>
    /// The default local-socket endpoint for an estate on this machine, named by a hash of its live-store directory: a named pipe on
    /// Windows, otherwise a socket file in the user's runtime directory (XDG_RUNTIME_DIR, else TMPDIR, else /tmp/vericairn-user), which is
    /// short enough for a socket path on every platform and owner-only.
    /// </summary>
    public static string DefaultEndpoint(string root)
    {
        string directory = Path.GetDirectoryName(EndpointFilePath(root))!;
        string name = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(directory)))[..24];
        if (OperatingSystem.IsWindows())
        {
            return "vericairn-" + name;
        }

        return Path.Combine(SocketDirectory(), name[..16] + ".sock");
    }

    /// <summary>The user's runtime directory for socket files, created owner-only; refused when it exists and cannot be made owner-only.</summary>
    public static string SocketDirectory()
    {
        string? xdg = Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR");
        string? tmp = Environment.GetEnvironmentVariable("TMPDIR");
        string[] candidates =
        [
            string.IsNullOrWhiteSpace(xdg) ? string.Empty : Path.Combine(xdg, "vericairn"),
            string.IsNullOrWhiteSpace(tmp) ? string.Empty : Path.Combine(tmp, "vericairn"),
            Path.Combine("/tmp", "vericairn-" + Environment.UserName),
        ];
        foreach (string candidate in candidates.Where(c => c.Length > 0 && c.Length <= 80))
        {
            Directory.CreateDirectory(candidate);
            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(candidate, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            }

            return candidate;
        }

        throw new InvalidOperationException("No runtime directory short enough for a socket path is available; set XDG_RUNTIME_DIR or TMPDIR.");
    }

    /// <summary>Reads the endpoint file of an estate, or null when no daemon has published one.</summary>
    public static DaemonEndpoint? Discover(string root) => DaemonDiscovery.Discover(root);

    /// <summary>
    /// Starts a daemon for the estate at a root, publishes the endpoint file, and serves loopback TLS unless told not to. The
    /// local-network profile is served only at an address the options or the operator's runtime settings name; by default nothing
    /// listens beyond the local socket and loopback.
    /// </summary>
    public static async Task<DaemonHost> StartAsync(string root, string? endpoint = null, bool loopback = true, DaemonOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        root = Path.GetFullPath(root);
        options ??= new DaemonOptions();
        DaemonEndpoint? existing = Discover(root);
        if (existing is not null && VericairnClient.IsAlive(existing))
        {
            throw new InvalidOperationException($"Daemon {existing.ProcessId} already serves {root}; one process owns an estate's live state. Stop it with 'vericairn runtime stop' first.");
        }

        EstateService service = EstateService.Open(root);
        string address = endpoint ?? DefaultEndpoint(root);
        string file = EndpointFilePath(root);
        string directory = Path.GetDirectoryName(file)!;

        // The live store's directory holds the runtime's certificate and key, its identity and settings, and the local-network ledger.
        RuntimeCertificate.OwnerOnly(directory);

        // A write a paired device asked for whose outcome the last runtime never kept is closed now, with what it may have produced.
        int interrupted = new LocalNetworkLedger(directory).Reconcile(Records.Model.RecordParser.FormatTimestamp(DateTimeOffset.UtcNow), service.WrittenSince);
        if (interrupted > 0)
        {
            options.Log?.Invoke($"{interrupted} local-network write(s) had no outcome kept when the last runtime stopped; kept as interrupted");
        }
        bool pipe = OperatingSystem.IsWindows();
        if (!pipe && File.Exists(address))
        {
            File.Delete(address);
        }

        RuntimeCertificates? certificates = null;
        RuntimeIdentity? identity = null;
        IPEndPoint? localNetwork;
        try
        {
            localNetwork = options.LocalNetwork ?? RuntimeSettings.Load(directory).LocalNetworkEndPoint();
            certificates = loopback || localNetwork is not null ? new RuntimeCertificates(directory) : null;
            identity = localNetwork is null ? null : RuntimeIdentity.LoadOrCreate(directory, options.IdentityStore, options.Log);
        }
        catch
        {
            certificates?.Dispose();
            service.Dispose();
            throw;
        }

        var host = new DaemonHost(service, file, directory, options, certificates, identity);
        // Always the production environment: what a development environment adds, such as stack traces in answers, never reaches a client.
        WebApplicationBuilder builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions { ApplicationName = "vericairnd", EnvironmentName = Environments.Production });
        builder.Logging.ClearProviders();
        builder.Services.ConfigureHttpJsonOptions(json =>
        {
            json.SerializerOptions.PropertyNamingPolicy = ProtocolJson.Options.PropertyNamingPolicy;
            json.SerializerOptions.PropertyNameCaseInsensitive = true;
        });
        builder.WebHost.UseKestrelHttpsConfiguration();
        builder.WebHost.ConfigureKestrel(kestrel =>
        {
            kestrel.Limits.MaxConcurrentConnections = ConcurrentConnections;
            if (pipe)
            {
                // Kestrel's named-pipe transport creates the pipe with CurrentUserOnly, its default, and no PipeSecurity: the pipe's
                // access list allows only the owner of this process's token, the user that started the daemon (for an elevated
                // process, the owner Windows gives it, by default the Administrators group). A Windows test reads that list.
                kestrel.ListenNamedPipe(address);
            }
            else
            {
                kestrel.ListenUnixSocket(address);
            }

            if (certificates is not null && loopback)
            {
                kestrel.Listen(IPAddress.Loopback, 0, listen =>
                {
                    host.loopbackListener = listen;

                    // Tracked from the moment the connection is accepted, before TLS, so a compromise also closes handshakes under way.
                    listen.Use(host.Track(LoopbackMarker));
                    listen.UseHttps(new HttpsConnectionAdapterOptions { ServerCertificateSelector = host.SelectCertificate });
                });
            }

            if (localNetwork is not null)
            {
                // Mutual TLS: the runtime presents its certificate, and a client may present one; the request pipeline then requires
                // a certificate its device's key issued for everything but the pairing exchange. Sessions are never resumed, so each
                // connection shows its certificates afresh.
                kestrel.Listen(localNetwork, listen =>
                {
                    host.localNetworkListener = listen;
                    listen.Protocols = HttpProtocols.Http1;
                    listen.Use(host.Track(LocalNetworkMarker));

                    // A client certificate is read for what its device's key signed, never by building its chain: nothing it names, no
                    // issuer, OCSP or CRL address, is fetched, so no client, paired or not, makes the runtime reach anywhere. A platform's
                    // TLS may evaluate a client certificate before any policy applies (macOS's asks its trust service, which fetches), so
                    // the listener speaks TLS 1.2 only, where the certificate travels in the clear, and the screen refuses one that names
                    // an address before the platform's TLS reads it. The chain policy stays as well.
                    listen.Use(Screen);
                    listen.UseHttps(new HttpsConnectionAdapterOptions
                    {
                        ServerCertificateSelector = host.SelectCertificate,
                        SslProtocols = TlsScreenStream.Protocols,
                        ClientCertificateMode = ClientCertificateMode.AllowCertificate,
                        ClientCertificateValidation = (_, _, _) => true,
                        CheckCertificateRevocation = false,
                        OnAuthenticate = (_, tls) =>
                        {
                            // A renegotiation would carry a new certificate inside the encrypted channel, out of the screen's sight.
                            tls.AllowRenegotiation = false;
                            tls.AllowTlsResume = false;
                            tls.CipherSuitesPolicy = TlsScreenStream.CipherSuites;
                            tls.CertificateChainPolicy = new X509ChainPolicy { DisableCertificateDownloads = true, RevocationMode = X509RevocationMode.NoCheck };
                        },
                    });
                });
            }
        });
        WebApplication app = builder.Build();
        host.app = app;
        app.Use(host.HandleRequestAsync);
        // Every route is reached through one group, whose filter holds a device-signed request to its device's principal once bound.
        RouteGroupBuilder routes = app.MapGroup(string.Empty);
        routes.AddEndpointFilter(new RequestScope(service, new LocalNetworkLedger(directory), host.Log, () => host.BeforeDeviceWrite?.Invoke()));
        ApiRoutes.Map(routes, service, () => _ = Task.Run(host.StopAsync));
        DaemonRoutes.Map(routes, host);
        try
        {
            host.Authenticator.StartAccepting();
            await app.StartAsync(cancellationToken).ConfigureAwait(false);
            // Request times have whole-second precision. Keep the exact cutoff, and publish readiness only when a freshly
            // signed request can meet it, including after a restart in the same second as the previous instance's last request.
            TimeSpan signingDelay = host.Authenticator.FirstSigningTime - host.Time.GetUtcNow();
            if (signingDelay > TimeSpan.Zero)
            {
                await Task.Delay(signingDelay, cancellationToken).ConfigureAwait(false);
            }
        }
        catch
        {
            await app.DisposeAsync().ConfigureAwait(false);
            certificates?.Dispose();
            service.Dispose();
            host.schedule.Dispose();
            throw;
        }

        if (!pipe && File.Exists(address))
        {
            File.SetUnixFileMode(address, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }

        LoopbackEndpoint? loopbackEndpoint = host.loopbackListener?.IPEndPoint is IPEndPoint bound
            ? new LoopbackEndpoint(IPAddress.Loopback.ToString(), bound.Port, certificates!.Fingerprint)
            : null;
        LocalNetworkEndpoint? localNetworkEndpoint = host.localNetworkListener?.IPEndPoint is IPEndPoint served
            ? new LocalNetworkEndpoint(served.Address.ToString(), served.Port, identity!.Fingerprint)
            : null;
        host.Publish(new DaemonEndpoint(pipe ? "pipe" : "unix", address, Environment.ProcessId, RecordParser.FormatTimestamp(DateTimeOffset.UtcNow), root, ApiVersion.Current, loopbackEndpoint, localNetworkEndpoint));
        if (certificates is not null)
        {
            host.scheduled = Task.Run(() => host.RunScheduleAsync(host.schedule.Token), CancellationToken.None);
        }

        return host;
    }

    /// <summary>The routes the running daemon maps, as method and pattern; the contract test compares them with the OpenAPI document.</summary>
    public IReadOnlyList<(string Method, string Path)> MappedRoutes()
    {
        var routes = new List<(string Method, string Path)>();
        foreach (RouteEndpoint endpoint in App.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>())
        {
            string path = "/" + (endpoint.RoutePattern.RawText ?? string.Empty).TrimStart('/');
            foreach (string method in endpoint.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods ?? [])
            {
                routes.Add((method, path));
            }
        }

        return routes;
    }

    /// <summary>Every address the daemon listens on, as the server reports them: the local socket, loopback, and the local network when configured.</summary>
    public IReadOnlyList<string> Addresses() =>
        App.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()?.Addresses.ToList() ?? [];

    /// <summary>
    /// Replaces the runtime certificate now. Paired devices keep working, since they pin the runtime identity, which signs the new
    /// certificate's fingerprint; loopback clients read it from the endpoint file. For a compromise every open connection is closed
    /// too, and with <paramref name="identity"/> the runtime identity is replaced as well, after which every paired device pairs again.
    /// </summary>
    public CertificateRotated RotateCertificate(bool compromised = false, string? reason = null, bool identity = false) => RotateCertificate(compromised, reason, identity, keep: null);

    /// <summary>Replaces the certificate when it expires within <see cref="RuntimeCertificate.RenewBefore"/> of a moment: the schedule a running daemon keeps.</summary>
    public bool RotateIfDue(DateTimeOffset now)
    {
        if (Certificates is null || !RuntimeCertificate.IsDue(Certificates.Current, now))
        {
            return false;
        }

        RotateCertificate(compromised: false, reason: "on schedule, before it expires", identity: false, keep: null);
        return true;
    }

    /// <summary>Waits until the daemon is asked to stop.</summary>
    public Task WaitForShutdownAsync(CancellationToken cancellationToken = default) => App.WaitForShutdownAsync(cancellationToken);

    /// <summary>Stops serving, removes the endpoint file and the socket, and closes the estate; safe to call more than once.</summary>
    public Task StopAsync()
    {
        lock (gate)
        {
            stopping ??= Task.Run(StopCoreAsync);
            return stopping;
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
        await App.DisposeAsync().ConfigureAwait(false);
        Certificates?.Dispose();
        schedule.Dispose();
    }

    internal CertificateRotated RotateCertificate(bool compromised, string? reason, bool identity, string? keep)
    {
        lock (rotation)
        {
            if (Certificates is null)
            {
                throw new InvalidOperationException("This daemon serves no TLS profile, so it presents no certificate to rotate.");
            }

            if (identity && Identity is null)
            {
                throw new InvalidOperationException("This daemon serves no local-network profile, so it has no identity to replace.");
            }

            (string previous, X509Certificate2 next) = Certificates.Rotate();
            if (identity)
            {
                Identity = Identity!.Replace(directory, options.IdentityStore);
            }

            string fingerprint = RuntimeCertificate.Fingerprint(next);
            DaemonEndpoint published = Endpoint;
            Publish(published with
            {
                Loopback = published.Loopback is null ? null : published.Loopback with { Certificate = fingerprint },
                LocalNetwork = published.LocalNetwork is null || Identity is null ? published.LocalNetwork : published.LocalNetwork with { Identity = Identity.Fingerprint },
            });

            // After a compromise nothing continues on a connection that began under the old certificate: every client connects
            // again, sees the new certificate and, on the local network, has the runtime identity prove it.
            bool close = compromised || identity;
            int closed = 0;
            if (close)
            {
                foreach ((string id, ConnectionContext connection) in open)
                {
                    if (id != keep)
                    {
                        connection.Abort(new ConnectionAbortedException("The runtime certificate was replaced after a compromise."));
                        closed++;
                    }
                }
            }

            options.Log?.Invoke($"runtime certificate {previous} replaced by {fingerprint}{(close ? $" after a compromise; {closed} open connections closed" : string.Empty)}{(identity ? $"; runtime identity replaced by {Identity!.Fingerprint}, and every paired device pairs again" : string.Empty)}{(string.IsNullOrWhiteSpace(reason) ? string.Empty : ": " + reason)}");
            return new CertificateRotated(fingerprint, previous, RecordParser.FormatTimestamp(new DateTimeOffset(next.NotAfter.ToUniversalTime())), close, closed, identity ? Identity!.Fingerprint : null);
        }
    }

    /// <summary>Whether a request came over the local-network profile.</summary>
    internal static bool OnLocalNetwork(HttpContext context) => context.Features.Get<IConnectionItemsFeature>()?.Items.ContainsKey(LocalNetworkMarker) == true;

    /// <summary>The fingerprint of the certificate the runtime presented on a request's connection; the current one for a connection without TLS.</summary>
    internal string CertificateOf(HttpContext context) =>
        context.Features.Get<IConnectionItemsFeature>()?.Items.TryGetValue(CertificateMarker, out object? presented) == true && presented is X509Certificate2 certificate
            ? RuntimeCertificate.Fingerprint(certificate)
            : Certificates?.Fingerprint ?? string.Empty;

    internal void Log(string message) => options.Log?.Invoke(message);

    /// <summary>Answers with a problem whose length is stated, so a client reading the raw exchange knows where it ends.</summary>
    internal static async Task WriteProblemAsync(HttpContext context, int status, Problem problem, bool close = false)
    {
        byte[] payload = JsonSerializer.SerializeToUtf8Bytes(problem, ProtocolJson.Options);
        context.Response.StatusCode = status;
        context.Response.ContentType = "application/json; charset=utf-8";
        context.Response.ContentLength = payload.Length;
        if (close)
        {
            context.Response.Headers.Connection = "close";
        }

        await context.Response.Body.WriteAsync(payload).ConfigureAwait(false);
    }

    private WebApplication App => app ?? throw new InvalidOperationException("The daemon has not started.");

    /// <summary>
    /// Holds every HTTP refusal to one completion and bounded discard, including binding errors and size limits. Kestrel's own
    /// body limit poisons its reader on overflow, so enforce the same limit through a non-owning wrapper and keep its reader for
    /// cleanup. Authentication and JSON binding never receive bytes beyond the limit.
    /// </summary>
    internal async Task HandleRequestAsync(HttpContext context, RequestDelegate next)
    {
        PipeReader body = context.Request.BodyReader;
        IHttpMaxRequestBodySizeFeature? size = context.Features.Get<IHttpMaxRequestBodySizeFeature>();
        long? maximum = size?.MaxRequestBodySize;
        if (RequestAuthenticator.PairingRoutes.Contains(RequestAuthenticator.Operation(context)))
        {
            maximum = Math.Min(maximum ?? long.MaxValue, RequestAuthenticator.PairingBodyLimit);
        }
        else if (context.Connection.LocalIpAddress is not null
            || context.Features.Get<IConnectionItemsFeature>()?.Items is { } items && (items.ContainsKey(LoopbackMarker) || items.ContainsKey(LocalNetworkMarker)))
        {
            maximum = Math.Min(maximum ?? long.MaxValue, RequestAuthenticator.SignedBodyLimit);
        }

        if (size is { IsReadOnly: false })
        {
            size.MaxRequestBodySize = null;
        }

        var limited = new LimitedBodyReader(body, maximum ?? long.MaxValue);
        bool hasBody = context.Features.Get<IHttpRequestBodyDetectionFeature>()?.CanHaveBody ?? true;
        context.Request.Body = limited.AsStream(leaveOpen: true);
        context.Features.Set<IRequestBodyPipeFeature>(limited);

        context.Response.OnStarting(() =>
        {
            if (context.Response.StatusCode >= 400 && hasBody && !limited.IsFullyRead)
            {
                context.Response.Headers.Connection = "close";
            }

            return Task.CompletedTask;
        });
        try
        {
            if (context.Request.ContentLength is long length && maximum is long bound && length > bound)
            {
                await WriteProblemAsync(context, StatusCodes.Status413PayloadTooLarge, new Problem("refused", $"The request body exceeds its limit of {bound} bytes.", []), close: true).ConfigureAwait(false);
            }
            else
            {
                await AuthenticateAsync(context, next).ConfigureAwait(false);
            }
        }
        catch (Microsoft.AspNetCore.Http.BadHttpRequestException exception) when (!context.Response.HasStarted)
        {
            await WriteProblemAsync(context, exception.StatusCode, new Problem("refused", exception.Message, []), close: true).ConfigureAwait(false);
        }

        if (context.Response.StatusCode >= 400)
        {
            await CompleteRefusalAsync(context, body).ConfigureAwait(false);
        }
    }

    /// <summary>Authenticates every request by the profile it came over: device-signed on loopback; on the local network a paired device by its certificate and its signature; the local socket by operating-system identity.</summary>
    private async Task AuthenticateAsync(HttpContext context, RequestDelegate next)
    {
        IDictionary<object, object?>? items = context.Features.Get<IConnectionItemsFeature>()?.Items;
        bool localNetwork = items?.ContainsKey(LocalNetworkMarker) == true;
        Problem? denied = localNetwork
            ? await Authenticator.VerifyLocalNetworkAsync(context).ConfigureAwait(false)
            : items?.ContainsKey(LoopbackMarker) == true || context.Connection.LocalIpAddress is not null
                ? await Authenticator.VerifyAsync(context).ConfigureAwait(false)
                : null;
        if (denied is not null)
        {
            await WriteProblemAsync(context, StatusCodes.Status401Unauthorized, denied, close: true).ConfigureAwait(false);
            return;
        }

        await next(context).ConfigureAwait(false);
    }

    /// <summary>Completes any HTTP refusal before discarding its unread transport bytes, without parsing or binding them.</summary>
    internal static async Task CompleteRefusalAsync(HttpContext context, PipeReader body)
    {
        await context.Response.CompleteAsync().ConfigureAwait(false);
        await DrainRefusedBodyAsync(body, RefusedBodyLimit, TimeSpan.FromSeconds(10), context.RequestAborted).ConfigureAwait(false);
    }

    /// <summary>The most unread bytes discarded after a refusal, independently of its parsing or request-size limit.</summary>
    internal const long RefusedBodyLimit = 64 * 1024;

    /// <summary>Discards a refused body's remainder within byte and time limits, leaving the response and transport live.</summary>
    internal static async Task<long> DrainRefusedBodyAsync(PipeReader reader, long maximum, TimeSpan limit, CancellationToken aborted)
    {
        long consumed = 0;
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(aborted);
        deadline.CancelAfter(limit);
        // As in authentication, cancelling the pipe's pending read preserves TLS; cancelling a stream read need not.
        using CancellationTokenRegistration pending = deadline.Token.Register(reader.CancelPendingRead);
        try
        {
            while (consumed < maximum && !deadline.IsCancellationRequested)
            {
                ReadResult read = await reader.ReadAsync(CancellationToken.None).ConfigureAwait(false);
                long take = Math.Min(read.Buffer.Length, maximum - consumed);
                reader.AdvanceTo(read.Buffer.GetPosition(take));
                consumed += take;
                if (read.IsCompleted || read.IsCanceled)
                {
                    break;
                }
            }
        }
        catch (Exception exception) when (exception is IOException or OperationCanceledException)
        {
            // The refusal is already complete. A disconnected peer or malformed remainder changes no authentication outcome.
        }

        return consumed;
    }

    /// <summary>Presents the current certificate to a new TLS connection and remembers which one it was.</summary>
    private X509Certificate2? SelectCertificate(ConnectionContext? connection, string? name)
    {
        X509Certificate2? current = Certificates?.Current;
        if (connection is not null && current is not null)
        {
            connection.Items[CertificateMarker] = current;
        }

        return current;
    }

    /// <summary>
    /// Reads a local-network connection through the TLS certificate screen, so a client certificate that names an address to fetch
    /// is refused before the platform's TLS sees it.
    /// </summary>
    private static ConnectionDelegate Screen(ConnectionDelegate next) => async connection =>
    {
        IDuplexPipe transport = connection.Transport;
        var screen = new TlsScreenStream(transport.Input.AsStream(leaveOpen: true), transport.Output.AsStream(leaveOpen: true), leaveOpen: true);
        PipeReader reader = PipeReader.Create(screen, new StreamPipeReaderOptions(leaveOpen: true));
        PipeWriter writer = PipeWriter.Create(screen, new StreamPipeWriterOptions(leaveOpen: true));
        connection.Transport = new ScreenedTransport(reader, writer);
        try
        {
            await next(connection).ConfigureAwait(false);
        }
        finally
        {
            connection.Transport = transport;
            await reader.CompleteAsync().ConfigureAwait(false);
            await writer.CompleteAsync().ConfigureAwait(false);
            await screen.DisposeAsync().ConfigureAwait(false);
            if (screen.RefusedPeer)
            {
                // No HTTP or established TLS exists at this point. Complete a fatal TLS handshake_failure alert first, then
                // discard raw transport bytes: never run rejected certificates or their remainder through the screen again.
                await transport.Output.WriteAsync(new byte[] { 21, 3, 3, 0, 2, 2, 40 }, connection.ConnectionClosed).ConfigureAwait(false);
                await DrainRefusedBodyAsync(transport.Input, RefusedBodyLimit, TimeSpan.FromSeconds(10), connection.ConnectionClosed).ConfigureAwait(false);
            }
        }
    };

    /// <summary>Marks a TLS connection with its profile and keeps it in view while it is open, so a compromise can close it.</summary>
    private Func<ConnectionDelegate, ConnectionDelegate> Track(object marker) => next => async connection =>
    {
        connection.Items[marker] = true;
        open[connection.ConnectionId] = connection;
        try
        {
            await next(connection).ConfigureAwait(false);
        }
        finally
        {
            open.TryRemove(connection.ConnectionId, out _);
        }
    };

    private void Publish(DaemonEndpoint endpoint)
    {
        lock (gate)
        {
            Endpoint = endpoint;
            if (stopping is null)
            {
                File.WriteAllBytes(endpointFile, JsonSerializer.SerializeToUtf8Bytes(endpoint, ProtocolJson.Options));
            }
        }
    }

    private async Task RunScheduleAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(ScheduleCheck, Time);
        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
            {
                try
                {
                    RotateIfDue(Time.GetUtcNow());
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or CryptographicException or InvalidOperationException)
                {
                    Log($"the runtime certificate could not be replaced on schedule: {exception.Message}");
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Stopping.
        }
    }

    private async Task StopCoreAsync()
    {
        await schedule.CancelAsync().ConfigureAwait(false);
        try
        {
            await App.StopAsync().ConfigureAwait(false);
            if (scheduled is not null)
            {
                await scheduled.ConfigureAwait(false);
            }
        }
        finally
        {
            if (File.Exists(endpointFile))
            {
                File.Delete(endpointFile);
            }

            if (Endpoint.Kind == "unix" && File.Exists(Endpoint.Address))
            {
                File.Delete(Endpoint.Address);
            }

            Service.Dispose();
        }
    }
}

/// <summary>A connection's transport read through the TLS certificate screen.</summary>
internal sealed record ScreenedTransport(PipeReader Input, PipeWriter Output) : IDuplexPipe;
