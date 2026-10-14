// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Net.Http;
using Vericairn.Daemon;
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
}

/// <summary>The production connection: discovers the daemon's endpoint file and connects to its local socket.</summary>
public sealed class DaemonConnection : IEstateConnection
{
    private VericairnClient? client;

    /// <summary>A connection for an estate root.</summary>
    public DaemonConnection(string root)
    {
        Root = root;
        Refresh();
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
    public void Refresh()
    {
        client?.Dispose();
        client = null;
        try
        {
            DaemonEndpoint? endpoint = DaemonHost.Discover(Root);
            if (endpoint is not null && VericairnClient.IsAlive(endpoint))
            {
                client = new VericairnClient(endpoint);
                Where = $"{endpoint.Kind} endpoint, process {endpoint.ProcessId}, since {endpoint.StartedAt}";
                return;
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
