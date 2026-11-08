// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Primitives;
using Microsoft.Net.Http.Headers;
using Vericairn.Mcp;
using Vericairn.Protocol;
using Vericairn.Runtime.Events;
using Vericairn.Runtime.Service;
using Vericairn.Sdk;

namespace Vericairn.Daemon;

/// <summary>
/// Network MCP over Streamable HTTP at /mcp, in signed mode (MCP-03): served on the daemon's loopback and local-network listeners
/// once the operator turned it on, never on the local socket, where the operating-system user acts for any principal. A request's
/// Host must name the listener it reached, and an Origin it names must be one the operator allowed, before anything is
/// authenticated. Each request is then authenticated as one device signing it, as the API's requests are, and that device must be
/// admitted, permitted the profile it arrived on, and an agent's or a service's. A session binds to that device; each tool call
/// runs as its API operation for that device, judged by the operation's own request scope; and the device signs every record
/// itself, in two calls. Requests are answered with one JSON body; a GET opens the session's server stream, which carries the
/// session's own messages to its device alone.
/// </summary>
internal sealed class McpEndpoint
{
    /// <summary>The MCP endpoint's path.</summary>
    internal const string Path = "/mcp";

    /// <summary>The session header of the Streamable HTTP transport.</summary>
    internal const string SessionHeader = "Mcp-Session-Id";

    /// <summary>The protocol-version header later requests carry.</summary>
    internal const string VersionHeader = "MCP-Protocol-Version";

    /// <summary>
    /// The most a message to /mcp may hold: one JSON-RPC message, whose largest arguments are a result's or a review's body. Smaller
    /// than the API's signed-body limit, it is applied before authentication.
    /// </summary>
    internal const long BodyLimit = 1024 * 1024;

    /// <summary>The revisions served over HTTP: Streamable HTTP begins at 2025-03-26, so the older HTTP transport is not served.</summary>
    internal static readonly IReadOnlyList<string> Versions = [.. McpServer.SupportedProtocolVersions.Where(v => string.CompareOrdinal(v, "2025-03-26") >= 0)];

    /// <summary>How long a server stream lasts before the client opens another with a fresh signature, as the event stream does.</summary>
    internal static readonly TimeSpan StreamLimit = EventRoutes.SignedStreamLimit;

    private readonly DaemonHost host;
    private readonly HashSet<string> origins;
    private McpOperations? operations;

    internal McpEndpoint(DaemonHost host, bool enabled, IEnumerable<string> allowedOrigins)
    {
        this.host = host;
        Enabled = enabled;
        Sessions = new McpSessions(host.Time);
        origins = new HashSet<string>(allowedOrigins.Select(o => RuntimeSettings.TryNormalizeOrigin(o, out string? origin, out _) ? origin! : o), StringComparer.Ordinal);
    }

    /// <summary>Whether /mcp is served.</summary>
    internal bool Enabled { get; }

    /// <summary>The sessions open now.</summary>
    internal McpSessions Sessions { get; }

    /// <summary>A test's hook told when a session's server stream has opened.</summary>
    internal Action<McpSession>? StreamOpened { get; set; }

    /// <summary>Whether a request is for /mcp, on a listener that serves it.</summary>
    internal bool Serves(HttpContext context) =>
        Enabled && context.Request.Path.Equals(Path, StringComparison.OrdinalIgnoreCase) && DaemonHost.OnSignedProfile(context);

    /// <summary>The routes the tools reach, read from the daemon's mapped routes when first needed.</summary>
    private McpOperations Operations => operations ??= new McpOperations(host.RouteEndpoints());

    /// <summary>
    /// Before authentication: why a request to /mcp may not proceed, or null. Its Host must name this listener's own address and port
    /// (or localhost on loopback), so a name an attacker's DNS rebinds to this machine reaches nothing; and an Origin it names must be
    /// one the operator allowed, none by default.
    /// </summary>
    internal (int Status, Problem Problem)? Screen(HttpContext context)
    {
        if (!Serves(context))
        {
            return null;
        }

        if (!HostMatches(context))
        {
            return (StatusCodes.Status403Forbidden, new Problem("denied", "The request's Host does not name this runtime's listener: address the runtime by the IP address and port it listens on.", []));
        }

        if (context.Request.Headers.TryGetValue(HeaderNames.Origin, out StringValues origin) && origin.Count > 0)
        {
            string named = origin.ToString();
            if (origin.Count > 1 || !RuntimeSettings.TryNormalizeOrigin(named, out string? normalized, out _) || !origins.Contains(normalized!))
            {
                return (StatusCodes.Status403Forbidden, new Problem("denied", "The request's Origin is not one the operator allowed for /mcp; the allowed origins are set with 'vericairn runtime configure --mcp-origin'.", []));
            }
        }

        return null;
    }

    /// <summary>Serves /mcp after the request was authenticated; any other request goes on.</summary>
    internal async Task InvokeAsync(HttpContext context, RequestDelegate next)
    {
        if (!Serves(context))
        {
            await next(context).ConfigureAwait(false);
            return;
        }

        if (!context.Items.TryGetValue(RequestAuthenticator.DeviceItem, out object? item) || item is not AuthenticatedDevice device)
        {
            await Problem(context, StatusCodes.Status401Unauthorized, new Problem("denied", "Network MCP requests are signed by an admitted device.", [])).ConfigureAwait(false);
            return;
        }

        // One reading of the records judges this request's device and, when a sweep is due, every device holding a session.
        IReadOnlyCollection<string> due = Sessions.DueDevices();
        DeviceStandingsRead read = host.Service.ReadStandings(due.Append(device.Identity).Distinct(StringComparer.Ordinal), [device.Principal]);
        if (due.Count > 0)
        {
            Sessions.Sweep(read);
        }

        string profile = device.LocalNetwork ? RequestAuthenticator.LocalNetworkProfile : RequestScope.LoopbackProfile;
        if (Refusal(device, profile, read) is (int status, Problem refused))
        {
            await Problem(context, status, refused).ConfigureAwait(false);
            return;
        }

        string method = context.Request.Method;
        if (HttpMethods.IsPost(method))
        {
            await PostAsync(context, device, profile).ConfigureAwait(false);
        }
        else if (HttpMethods.IsGet(method))
        {
            await GetAsync(context, device, profile).ConfigureAwait(false);
        }
        else if (HttpMethods.IsDelete(method))
        {
            await DeleteAsync(context, device, profile).ConfigureAwait(false);
        }
        else
        {
            context.Response.Headers.Allow = "GET, POST, DELETE";
            await Problem(context, StatusCodes.Status405MethodNotAllowed, new Problem("refused", "/mcp answers POST, GET and DELETE.", [])).ConfigureAwait(false);
        }
    }

    /// <summary>Ends every session as the daemon stops.</summary>
    internal void Stop() => Sessions.EndAll();

    /// <summary>Why an authenticated device may not use network MCP on this profile, or null.</summary>
    private static (int Status, Problem Problem)? Refusal(AuthenticatedDevice device, string profile, DeviceStandingsRead read)
    {
        DeviceStanding? standing = read.Devices.GetValueOrDefault(device.Identity);
        if (standing is null || standing.Admission != "admitted" || standing.PublicKey != device.PublicKey)
        {
            return (StatusCodes.Status401Unauthorized, new Problem("denied", $"Device {device.Identity} is not admitted now.", []));
        }

        if (!standing.Profiles.Contains(profile, StringComparer.Ordinal))
        {
            return (StatusCodes.Status401Unauthorized, new Problem("denied", $"Device {device.Identity} is not permitted the {profile} profile, so it cannot use network MCP here.", []));
        }

        string? kind = read.PrincipalKinds.GetValueOrDefault(device.Principal);
        return kind is "agent" or "service"
            ? null
            : (StatusCodes.Status403Forbidden, new Problem("denied", $"Network MCP serves agents and services: device {device.Identity} acts for {device.Principal}, a {kind ?? "principal unknown here"}. A person connects an MCP client as an agent principal of their own.", []));
    }

    private async Task PostAsync(HttpContext context, AuthenticatedDevice device, string profile)
    {
        if (!MediaTypeHeaderValue.TryParse(context.Request.ContentType, out MediaTypeHeaderValue? type)
            || !string.Equals(type.MediaType.Value, "application/json", StringComparison.OrdinalIgnoreCase))
        {
            await Problem(context, StatusCodes.Status415UnsupportedMediaType, new Problem("refused", "A message to /mcp is one JSON-RPC message as application/json.", [])).ConfigureAwait(false);
            return;
        }

        if (!Accepts(context.Request, "application/json"))
        {
            await Problem(context, StatusCodes.Status406NotAcceptable, new Problem("refused", "/mcp answers a request with application/json; the Accept header must admit it.", [])).ConfigureAwait(false);
            return;
        }

        JsonNode? parsed;
        try
        {
            if (context.Request.Body.CanSeek)
            {
                context.Request.Body.Position = 0;
            }

            parsed = await JsonNode.ParseAsync(context.Request.Body, documentOptions: new JsonDocumentOptions { MaxDepth = 64 }, cancellationToken: context.RequestAborted).ConfigureAwait(false);
        }
        catch (JsonException exception)
        {
            await Rpc(context, StatusCodes.Status400BadRequest, Error(null, -32700, "Parse error: " + exception.Message)).ConfigureAwait(false);
            return;
        }

        if (parsed is JsonArray)
        {
            await Rpc(context, StatusCodes.Status400BadRequest, Error(null, -32600, "Invalid request: batches are not served; send one message per request.")).ConfigureAwait(false);
            return;
        }

        if (parsed is not JsonObject message)
        {
            await Rpc(context, StatusCodes.Status400BadRequest, Error(null, -32600, "Invalid request: not an object.")).ConfigureAwait(false);
            return;
        }

        JsonNode? id = message["id"];
        string? method = message["method"] is JsonValue m && m.TryGetValue(out string? name) ? name : null;
        bool request = method is not null && id is not null;
        if (method == "initialize" && request)
        {
            await InitializeAsync(context, device, profile, id!, message["params"]).ConfigureAwait(false);
            return;
        }

        if (await SessionAsync(context, device, profile).ConfigureAwait(false) is not McpSession session)
        {
            return;
        }

        if (!request)
        {
            if (method is null && id is null || method is null && message["result"] is null && message["error"] is null)
            {
                await Rpc(context, StatusCodes.Status400BadRequest, Error(null, -32600, "Invalid request: no method.")).ConfigureAwait(false);
                return;
            }

            // A notification or a response is accepted; a cancellation stops observing the named request, and undoes no effect.
            if (method == "notifications/cancelled" && message["params"] is JsonObject cancellation && cancellation["requestId"] is JsonNode cancelled
                && session.Active.TryGetValue(cancelled.ToJsonString(), out CancellationTokenSource? stop))
            {
                try
                {
                    await stop.CancelAsync().ConfigureAwait(false);
                }
                catch (ObjectDisposedException)
                {
                    // It finished first.
                }
            }

            context.Response.StatusCode = StatusCodes.Status202Accepted;
            context.Response.ContentLength = 0;
            return;
        }

        // A request takes one of its device's call slots while it runs. Parsing is bounded by the message's 1 MiB, and notifications
        // (a cancellation above all) and initialize take none, so a client can always cancel and open a session when its slots are full.
        using IDisposable? slot = Sessions.TryCall(device.Identity);
        if (slot is null)
        {
            await Problem(context, StatusCodes.Status429TooManyRequests, new Problem("refused", $"A device runs at most {McpSessions.CallsPerDevice} MCP requests at once and all devices {McpSessions.CallsPerRuntime}; retry when one finishes.", [])).ConfigureAwait(false);
            return;
        }

        string key = id!.ToJsonString();
        // A dropped connection cancels nothing (a write whose answer is lost is reconciled by reading): a call stops being observed
        // only when its client cancels it, its session ends or the daemon stops.
        using var observing = CancellationTokenSource.CreateLinkedTokenSource(session.Ended, host.Stopping);
        if (!session.Active.TryAdd(key, observing))
        {
            await Rpc(context, StatusCodes.Status200OK, Error(id.DeepClone(), -32600, "A request with this id is already active.")).ConfigureAwait(false);
            return;
        }

        try
        {
            JsonObject answer = await RespondAsync(context, device, id.DeepClone(), method!, message["params"], observing.Token).ConfigureAwait(false);
            await Rpc(context, StatusCodes.Status200OK, answer).ConfigureAwait(false);
        }
        finally
        {
            session.Active.TryRemove(key, out _);
        }
    }

    private async Task InitializeAsync(HttpContext context, AuthenticatedDevice device, string profile, JsonNode id, JsonNode? parameters)
    {
        if (context.Request.Headers.ContainsKey(SessionHeader))
        {
            await Rpc(context, StatusCodes.Status400BadRequest, Error(id.DeepClone(), -32600, "initialize opens a session: send it without Mcp-Session-Id.")).ConfigureAwait(false);
            return;
        }

        string requested = parameters?["protocolVersion"] is JsonValue v && v.TryGetValue(out string? text) ? text : Versions[0];
        string negotiated = Versions.Contains(requested, StringComparer.Ordinal) ? requested : Versions[0];
        McpSession? session = Sessions.Open(device, profile, negotiated);
        if (session is null)
        {
            await Problem(context, StatusCodes.Status429TooManyRequests, new Problem("refused", $"The runtime holds {McpSessions.PerRuntime} MCP sessions, as many as it keeps; retry when one ends.", [])).ConfigureAwait(false);
            return;
        }

        JsonObject answer = await RespondAsync(context, device, id.DeepClone(), "initialize", parameters, host.Stopping).ConfigureAwait(false);
        context.Response.Headers[SessionHeader] = session.Id;
        await Rpc(context, StatusCodes.Status200OK, answer).ConfigureAwait(false);
    }

    /// <summary>Answers one request with a server for this request's device: a registry over the API's routes run as that device.</summary>
    private async Task<JsonObject> RespondAsync(HttpContext context, AuthenticatedDevice device, JsonNode id, string method, JsonNode? parameters, CancellationToken observing)
    {
        McpOperations routes = Operations;
        var session = new NetworkSession(device.Principal);
        using var client = new VericairnClient(new HttpClient(routes.Handler(host, context, device, session.Current, observing), disposeHandler: true)
            { BaseAddress = new Uri("http://vericairn-mcp/") });
        var server = new McpServer(ToolRegistry.ForNetwork(client, session), EstateService.Version)
        {
            ProtocolVersions = Versions,
            Instructions = McpServer.NetworkDescription,
        };
        return await server.RespondAsync(id, method, parameters, observing).ConfigureAwait(false);
    }

    private async Task GetAsync(HttpContext context, AuthenticatedDevice device, string profile)
    {
        if (!Accepts(context.Request, EventVocabulary.StreamMediaType, wildcard: false))
        {
            await Problem(context, StatusCodes.Status406NotAcceptable, new Problem("refused", "A GET of /mcp opens the session's server stream: the Accept header names text/event-stream.", [])).ConfigureAwait(false);
            return;
        }

        if (await SessionAsync(context, device, profile).ConfigureAwait(false) is not McpSession session)
        {
            return;
        }

        if (!session.TryOpenStream())
        {
            await Problem(context, StatusCodes.Status409Conflict, new Problem("refused", "This session's server stream is open already; a session has one.", [])).ConfigureAwait(false);
            return;
        }

        try
        {
            using IDisposable? slot = host.EventStreams.TryAcquire(device.Identity, device.Principal);
            if (slot is null)
            {
                await Problem(context, StatusCodes.Status429TooManyRequests, new Problem("refused", $"A device holds at most {EventRoutes.StreamsPerDevice} streams at once, event streams and MCP server streams together, one principal's devices {EventRoutes.StreamsPerPrincipal} and all devices {EventRoutes.StreamsPerRuntime}; close one first.", [])).ConfigureAwait(false);
                return;
            }

            // The stream is bound to the session's device as the event stream is: it ends before the next message once the device is
            // no longer admitted with its key and profile, and its wake-ups come from the journal, which a revocation enters.
            (EventSubscription? subscription, Problem? problem) = host.Service.OpenEvents(null, new EventFilter(Kinds: ["record"]), device.Principal,
                device.Identity, device.PublicKey, profile);
            if (subscription is null)
            {
                Problem refusal = problem ?? new Problem("unavailable", "The server stream is unavailable.", []);
                await Problem(context, refusal.Code == "unavailable" ? StatusCodes.Status503ServiceUnavailable : StatusCodes.Status401Unauthorized, refusal).ConfigureAwait(false);
                return;
            }

            using IDisposable listening = subscription.Listen();
            await StreamAsync(context, session, subscription).ConfigureAwait(false);
        }
        finally
        {
            session.CloseStream();
        }
    }

    private async Task StreamAsync(HttpContext context, McpSession session, EventSubscription subscription)
    {
        HttpResponse response = context.Response;
        response.StatusCode = StatusCodes.Status200OK;
        response.ContentType = EventVocabulary.StreamMediaType + "; charset=utf-8";
        response.Headers.CacheControl = "no-cache, no-store";
        context.Features.Get<IHttpResponseBodyFeature>()?.DisableBuffering();
        using var limit = new CancellationTokenSource(StreamLimit, host.Time);
        using var end = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted, host.Stopping, session.Ended, limit.Token);
        try
        {
            await WriteAsync(response, ": vericairn mcp server stream\n\n", end.Token).ConfigureAwait(false);
            StreamOpened?.Invoke(session);
            DateTimeOffset nextKeepAlive = DateTimeOffset.UtcNow + EventRoutes.KeepAlive;
            while (!end.IsCancellationRequested)
            {
                EventBatch batch = subscription.Read();
                if (batch.Closed)
                {
                    return;
                }

                if (batch.Refusal is not null)
                {
                    host.Log($"closed an MCP server stream of device {session.Device}: {batch.Refusal}");
                    Sessions.End(session);
                    return;
                }

                // The session's own messages, for its device alone; the journal's events wake the stream and are MCP-05's to send.
                var text = new StringBuilder();
                while (session.Outbox.Reader.TryRead(out JsonObject? message))
                {
                    text.Append("event: message\ndata: ").Append(message.ToJsonString()).Append("\n\n");
                }

                if (text.Length > 0)
                {
                    await WriteAsync(response, text.ToString(), end.Token).ConfigureAwait(false);
                }

                TimeSpan untilKeepAlive = nextKeepAlive - DateTimeOffset.UtcNow;
                if (untilKeepAlive > TimeSpan.Zero)
                {
                    using var pause = CancellationTokenSource.CreateLinkedTokenSource(end.Token);
                    Task arrived = subscription.WaitAsync(pause.Token);
                    Task queued = session.Outbox.Reader.WaitToReadAsync(pause.Token).AsTask();
                    Task alive = Task.Delay(untilKeepAlive, pause.Token);
                    Task first = await Task.WhenAny(arrived, queued, alive).ConfigureAwait(false);
                    await pause.CancelAsync().ConfigureAwait(false);
                    if (first != alive && first.IsCompletedSuccessfully)
                    {
                        continue;
                    }
                }

                if (end.IsCancellationRequested)
                {
                    break;
                }

                if (subscription.Refusal() is string refusal)
                {
                    host.Log($"closed an MCP server stream of device {session.Device}: {refusal}");
                    Sessions.End(session);
                    return;
                }

                await WriteAsync(response, ": keep-alive\n\n", end.Token).ConfigureAwait(false);
                nextKeepAlive = DateTimeOffset.UtcNow + EventRoutes.KeepAlive;
            }
        }
        catch (Exception exception) when (exception is OperationCanceledException or IOException or ObjectDisposedException)
        {
            // The client left, the session ended, the hour passed or the daemon is stopping: the stream ends.
        }
    }

    private async Task DeleteAsync(HttpContext context, AuthenticatedDevice device, string profile)
    {
        if (await SessionAsync(context, device, profile).ConfigureAwait(false) is not McpSession session)
        {
            return;
        }

        Sessions.End(session);
        context.Response.StatusCode = StatusCodes.Status204NoContent;
    }

    /// <summary>
    /// The session a request names, checked against its protocol-version header; null after answering 400 when it names none or an
    /// unsupported version, or 404 when it names one this device does not hold.
    /// </summary>
    private async Task<McpSession?> SessionAsync(HttpContext context, AuthenticatedDevice device, string profile)
    {
        if (!context.Request.Headers.TryGetValue(SessionHeader, out StringValues named) || named.Count != 1 || string.IsNullOrWhiteSpace(named.ToString()))
        {
            await Problem(context, StatusCodes.Status400BadRequest, new Problem("invalid", "Name the session initialize opened in Mcp-Session-Id.", [])).ConfigureAwait(false);
            return null;
        }

        McpSession? session = Sessions.Find(named.ToString(), device, profile);
        if (session is null)
        {
            await Problem(context, StatusCodes.Status404NotFound, new Problem("not-found", "No such session: it ended or was never this device's. Open another with initialize.", [])).ConfigureAwait(false);
            return null;
        }

        if (context.Request.Headers.TryGetValue(VersionHeader, out StringValues version) && (version.Count != 1 || version.ToString() != session.ProtocolVersion))
        {
            await Problem(context, StatusCodes.Status400BadRequest, new Problem("invalid", $"This session speaks MCP {session.ProtocolVersion}; MCP-Protocol-Version names another.", [])).ConfigureAwait(false);
            return null;
        }

        return session;
    }

    /// <summary>
    /// Whether a request's Host names the listener the connection reached: its address and port, or on loopback localhost and its
    /// port. Any other name, including one an attacker's DNS points at this address, is refused.
    /// </summary>
    private static bool HostMatches(HttpContext context)
    {
        HostString named = context.Request.Host;
        IPAddress? local = context.Connection.LocalIpAddress;
        if (!named.HasValue || named.Port is not int port || local is null || port != context.Connection.LocalPort)
        {
            return false;
        }

        string host = named.Host.Trim('[', ']');
        if (string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase))
        {
            return IPAddress.IsLoopback(local);
        }

        return IPAddress.TryParse(host, out IPAddress? address) && Unscoped(address).Equals(Unscoped(local));
    }

    /// <summary>An address as compared for Host: IPv4 for an IPv4-mapped one, and an IPv6 address without its scope id.</summary>
    internal static IPAddress Unscoped(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6)
        {
            return address.MapToIPv4();
        }

        return address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6 ? new IPAddress(address.GetAddressBytes()) : address;
    }

    /// <summary>Whether a request's Accept header admits a media type; an absent header admits anything, as HTTP has it.</summary>
    private static bool Accepts(HttpRequest request, string media, bool wildcard = true)
    {
        if (!request.Headers.TryGetValue(HeaderNames.Accept, out StringValues accept) || StringValues.IsNullOrEmpty(accept))
        {
            return wildcard;
        }

        if (!MediaTypeHeaderValue.TryParseList(accept, out IList<MediaTypeHeaderValue>? accepted))
        {
            return false;
        }

        string family = media[..media.IndexOf('/', StringComparison.Ordinal)] + "/*";
        return accepted.Any(a => string.Equals(a.MediaType.Value, media, StringComparison.OrdinalIgnoreCase)
            || wildcard && (a.MediaType.Value == "*/*" || string.Equals(a.MediaType.Value, family, StringComparison.OrdinalIgnoreCase)));
    }

    private static JsonObject Error(JsonNode? id, int code, string message) => new() { ["jsonrpc"] = "2.0", ["id"] = id, ["error"] = new JsonObject { ["code"] = code, ["message"] = message } };

    private static async Task Rpc(HttpContext context, int status, JsonObject message)
    {
        byte[] payload = Encoding.UTF8.GetBytes(message.ToJsonString());
        context.Response.StatusCode = status;
        context.Response.ContentType = "application/json; charset=utf-8";
        context.Response.ContentLength = payload.Length;
        await context.Response.Body.WriteAsync(payload).ConfigureAwait(false);
    }

    private static Task Problem(HttpContext context, int status, Problem problem) => DaemonHost.WriteProblemAsync(context, status, problem);

    private static async Task WriteAsync(HttpResponse response, string text, CancellationToken cancellationToken)
    {
        await response.Body.WriteAsync(Encoding.UTF8.GetBytes(text), cancellationToken).ConfigureAwait(false);
        await response.Body.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// The network session one request's tools run in: the device's principal, and the signing rule of the call in flight, which the
    /// in-process routes give each operation they run.
    /// </summary>
    private sealed class NetworkSession(string principal) : IMcpNetworkSession
    {
        private readonly AsyncLocal<DeviceSigning?> current = new();

        public string Principal => principal;

        /// <summary>The rule of the call in flight; a read when none was set, so no operation ever signs on this machine.</summary>
        internal DeviceSigning Current() => current.Value ?? DeviceSigning.Read();

        public async Task<(T Result, RecordPrepared? Prepared)> RunAsync<T>(McpSigning signing, string? plan, string? signature, Func<Task<T>> operation)
        {
            ArgumentNullException.ThrowIfNull(operation);
            DeviceSigning rule = signing switch
            {
                McpSigning.Prepare => DeviceSigning.Prepare(),
                McpSigning.Sign => DeviceSigning.Sign(plan!, signature!),
                _ => DeviceSigning.Read(),
            };
            current.Value = rule;
            T result = await operation().ConfigureAwait(false);
            return (result, rule.Prepared);
        }
    }
}
