// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Connections.Features;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Vericairn.Protocol;
using Vericairn.Sdk;
using Vericairn.Records.Estate;
using Vericairn.Records.Model;
using Vericairn.Runtime.Service;
using Vericairn.Runtime.Store;

namespace Vericairn.Daemon;

/// <summary>
/// The daemon: one process owns one open estate and serves the versioned API over a local socket (a Unix domain socket, or a named
/// pipe on Windows) and over loopback TLS pinned to the runtime certificate. Clients never open the live store; they call this. The
/// endpoints are published in a small file beside the live store so that the CLI and the SDK on the same machine can find them.
/// </summary>
public sealed class DaemonHost : IAsyncDisposable
{
    private static readonly object LoopbackMarker = new();
    private readonly WebApplication app;
    private readonly string endpointFile;
    private readonly X509Certificate2? certificate;
    private readonly object gate = new();
    private Task? stopping;

    private DaemonHost(WebApplication app, EstateService service, DaemonEndpoint endpoint, string endpointFile, X509Certificate2? certificate)
    {
        this.app = app;
        Service = service;
        Endpoint = endpoint;
        this.endpointFile = endpointFile;
        this.certificate = certificate;
    }

    /// <summary>The service this daemon owns.</summary>
    public EstateService Service { get; }

    /// <summary>Where clients connect.</summary>
    public DaemonEndpoint Endpoint { get; }

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

    /// <summary>Starts a daemon for the estate at a root, publishes the endpoint file, and serves loopback TLS unless told not to.</summary>
    public static async Task<DaemonHost> StartAsync(string root, string? endpoint = null, bool loopback = true, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        root = Path.GetFullPath(root);
        DaemonEndpoint? existing = Discover(root);
        if (existing is not null && Sdk.VericairnClient.IsAlive(existing))
        {
            throw new InvalidOperationException($"Daemon {existing.ProcessId} already serves {root}; one process owns an estate's live state. Stop it with 'vericairn runtime stop' first.");
        }

        EstateService service = EstateService.Open(root);
        string address = endpoint ?? DefaultEndpoint(root);
        string file = EndpointFilePath(root);
        string directory = Path.GetDirectoryName(file)!;
        Directory.CreateDirectory(directory);
        bool pipe = OperatingSystem.IsWindows();
        if (!pipe && File.Exists(address))
        {
            File.Delete(address);
        }

        X509Certificate2? certificate = loopback ? RuntimeCertificate.Load(directory) : null;
        var authenticator = new RequestAuthenticator(service);
        WebApplicationBuilder builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions { ApplicationName = "vericairnd" });
        builder.Logging.ClearProviders();
        builder.Services.ConfigureHttpJsonOptions(options =>
        {
            options.SerializerOptions.PropertyNamingPolicy = ProtocolJson.Options.PropertyNamingPolicy;
            options.SerializerOptions.PropertyNameCaseInsensitive = true;
        });
        builder.WebHost.UseKestrelHttpsConfiguration();
        builder.WebHost.ConfigureKestrel(kestrel =>
        {
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

            if (certificate is not null)
            {
                kestrel.Listen(IPAddress.Loopback, 0, options =>
                {
                    options.UseHttps(certificate);
                    options.Use(next => connection =>
                    {
                        connection.Items[LoopbackMarker] = true;
                        return next(connection);
                    });
                });
            }
        });
        WebApplication app = builder.Build();
        app.Use(async (context, next) =>
        {
            bool viaLoopback = context.Features.Get<IConnectionItemsFeature>()?.Items.ContainsKey(LoopbackMarker) == true || context.Connection.LocalIpAddress is not null;
            if (viaLoopback)
            {
                Problem? denied = await authenticator.VerifyAsync(context).ConfigureAwait(false);
                if (denied is not null)
                {
                    context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    await context.Response.WriteAsJsonAsync(denied, ProtocolJson.Options).ConfigureAwait(false);
                    return;
                }
            }

            await next(context).ConfigureAwait(false);
        });
        DaemonHost? host = null;
        ApiRoutes.Map(app, service, () =>
        {
            if (host is not null)
            {
                _ = Task.Run(host.StopAsync);
            }
        });
        try
        {
            await app.StartAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            await app.DisposeAsync().ConfigureAwait(false);
            certificate?.Dispose();
            service.Dispose();
            throw;
        }

        if (!pipe && File.Exists(address))
        {
            File.SetUnixFileMode(address, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }

        LoopbackEndpoint? loopbackEndpoint = null;
        if (certificate is not null)
        {
            string? url = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()?.Addresses.FirstOrDefault(a => a.StartsWith("https://", StringComparison.Ordinal));
            if (url is not null)
            {
                loopbackEndpoint = new LoopbackEndpoint(IPAddress.Loopback.ToString(), new Uri(url).Port, RuntimeCertificate.Fingerprint(certificate));
            }
        }

        var published = new DaemonEndpoint(pipe ? "pipe" : "unix", address, Environment.ProcessId, RecordParser.FormatTimestamp(DateTimeOffset.UtcNow), root, ApiVersion.Current, loopbackEndpoint);
        await File.WriteAllBytesAsync(file, JsonSerializer.SerializeToUtf8Bytes(published, ProtocolJson.Options), cancellationToken).ConfigureAwait(false);
        host = new DaemonHost(app, service, published, file, certificate);
        return host;
    }

    /// <summary>The routes the running daemon maps, as method and pattern; the contract test compares them with the OpenAPI document.</summary>
    public IReadOnlyList<(string Method, string Path)> MappedRoutes()
    {
        var routes = new List<(string Method, string Path)>();
        foreach (RouteEndpoint endpoint in app.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>())
        {
            string path = "/" + (endpoint.RoutePattern.RawText ?? string.Empty).TrimStart('/');
            foreach (string method in endpoint.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods ?? [])
            {
                routes.Add((method, path));
            }
        }

        return routes;
    }

    /// <summary>Waits until the daemon is asked to stop.</summary>
    public Task WaitForShutdownAsync(CancellationToken cancellationToken = default) => app.WaitForShutdownAsync(cancellationToken);

    /// <summary>Stops serving, removes the endpoint file and the socket, and closes the estate; safe to call more than once.</summary>
    public Task StopAsync()
    {
        lock (gate)
        {
            stopping ??= StopCoreAsync();
            return stopping;
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
        await app.DisposeAsync().ConfigureAwait(false);
        certificate?.Dispose();
    }

    private async Task StopCoreAsync()
    {
        try
        {
            await app.StopAsync().ConfigureAwait(false);
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
