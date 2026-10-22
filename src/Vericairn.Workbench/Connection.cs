// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Net.Http;
using Vericairn.Protocol;
using Vericairn.Sdk;

namespace Vericairn.Workbench;

/// <summary>
/// How Workbench reaches an estate: through the daemon that owns it, never by hosting the runtime. When no daemon answers the
/// estate is unavailable to this client until the runtime manager starts one.
/// </summary>
public interface IEstateConnection : IDisposable
{
    /// <summary>The estate root this connection is for.</summary>
    string Root { get; }

    /// <summary>The API, or null while unavailable.</summary>
    IEstateApi? Api { get; }

    /// <summary>daemon or unavailable (the state language's runtime vocabulary).</summary>
    string Mode { get; }

    /// <summary>Where the daemon serves, for the target context.</summary>
    string Where { get; }

    /// <summary>Re-discovers the daemon.</summary>
    void Refresh();

    /// <summary>Re-discovers the daemon without blocking the UI thread.</summary>
    Task RefreshAsync(CancellationToken cancellationToken = default) =>
        ClientOperation.RunSynchronousAsync("Connect", () => { Refresh(); return true; }, false, cancellationToken);
}

/// <summary>The production connection: discovers the daemon's endpoint file and connects to its local socket.</summary>
public sealed class DaemonConnection : IEstateConnection
{
    private VericairnClient? client;
    private readonly Func<string, DaemonEndpoint?> discover;
    private readonly Func<DaemonEndpoint, VericairnClient> connect;

    /// <summary>A connection for an estate root.</summary>
    public DaemonConnection(string root) : this(root, DaemonDiscovery.Discover, endpoint => new VericairnClient(endpoint))
    {
    }

    internal DaemonConnection(string root, Func<string, DaemonEndpoint?> discover, Func<DaemonEndpoint, VericairnClient> connect)
    {
        Root = root;
        this.discover = discover;
        this.connect = connect;
    }

    /// <inheritdoc />
    public string Root { get; }

    /// <inheritdoc />
    public IEstateApi? Api => client;

    /// <inheritdoc />
    public string Mode => client is null ? "unavailable" : "daemon";

    /// <inheritdoc />
    public string Where { get; private set; } = "no daemon answers for this estate";

    /// <inheritdoc />
    public void Refresh() => RefreshAsync().GetAwaiter().GetResult();

    /// <inheritdoc />
    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        client?.Dispose();
        client = null;
        try
        {
            DaemonEndpoint? endpoint = discover(Root);
            if (endpoint is not null)
            {
                VericairnClient? probe = connect(endpoint);
                try
                {
                    HealthResponse health = await probe.HealthAsync(cancellationToken).ConfigureAwait(false);
                    Where = $"{endpoint.Kind} endpoint, process {endpoint.ProcessId}, since {endpoint.StartedAt}";
                    if (probe.DaemonVersionWarning is { } warning) Where += ". " + warning;
                    if (health.Api != ApiVersion.Current)
                    {
                        Where += $" The daemon serves API {health.Api}; this client requires API {ApiVersion.Current}.";
                        return;
                    }
                    client = probe;
                    probe = null;
                    return;
                }
                finally { probe?.Dispose(); }
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException or HttpRequestException)
        {
            Where = exception.Message;
            return;
        }

        Where = "no daemon answers for this estate";
    }

    /// <inheritdoc />
    public void Dispose() => client?.Dispose();
}
