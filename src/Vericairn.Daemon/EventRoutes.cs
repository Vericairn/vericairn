// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Primitives;
using Microsoft.Net.Http.Headers;
using Vericairn.Protocol;
using Vericairn.Runtime.Events;

namespace Vericairn.Daemon;

/// <summary>
/// The event stream of API version 1 (WATCH-05): GET /v1/events as server-sent events when the client accepts text/event-stream, and
/// otherwise as a long poll, and GET /v1/events/{key}. Each authenticates as its profile does: the operating-system user on the local
/// socket, reading for a named principal as attention does; a device-signed opening request on loopback and the local network, which
/// reads only for its device's principal and, for a stream, ends after 60 minutes to reconnect with a fresh signature. A device holds
/// at most four streams. Every delivery passes the audience check; a device that loses admission, its key or its profile, or a
/// principal no longer known, has its streams closed before the next event.
/// </summary>
internal static class EventRoutes
{
    /// <summary>The most streams and long polls one device, or the local socket's user, holds at once.</summary>
    internal const int StreamsPerDevice = 4;

    /// <summary>
    /// The most streams and long polls paired and loopback devices hold at once. The design's ceiling is 64; the daemon admits 32
    /// connections in all, so device streams are held to half of them and never starve the API's other requests.
    /// </summary>
    internal const int StreamsPerRuntime = DaemonHost.ConcurrentConnections / 2;

    /// <summary>The streams the local socket's user always has beyond the devices' pool.</summary>
    internal const int LocalReserve = 2;

    /// <summary>The most streams all of one principal's devices hold at once, so no one principal fills the pool.</summary>
    internal const int StreamsPerPrincipal = StreamsPerRuntime / 2;

    /// <summary>How long a device-signed stream lasts before the client reconnects with a fresh signature.</summary>
    internal static readonly TimeSpan SignedStreamLimit = TimeSpan.FromMinutes(60);

    /// <summary>How often an idle stream says it is alive, on a fixed schedule from its opening, never prompted by an event.</summary>
    internal static readonly TimeSpan KeepAlive = TimeSpan.FromSeconds(15);

    /// <summary>
    /// From 0.9.2 (MCP-05): the selecting long polls the local socket's MCP servers hold at once, a budget of their own beside the
    /// command line's streams and its reserve, so MCP sessions that follow the journal never take the command line's slots.
    /// </summary>
    internal const int McpOnLocalSocket = 4;

    /// <summary>
    /// How long a selecting long poll on the local socket holds its slot before it yields to another waiting for one: it then answers
    /// at once with what it has, and its caller reads again, so beyond <see cref="McpOnLocalSocket"/> sessions take turns.
    /// </summary>
    internal static readonly TimeSpan McpTurn = TimeSpan.FromSeconds(5);

    private static readonly string[] SingleParameters = ["principal", "after", "wait"];

    public static void Map(IEndpointRouteBuilder app, DaemonHost host)
    {
        app.MapGet("/v1/events", (Func<HttpContext, Task<IResult>>)(context => ServeAsync(context, host)));
        app.MapGet("/v1/events/{key}", (string key, HttpContext context) => Key(key, context, host));
        app.MapPost("/v1/events/select", (EventSelectRequest request, HttpContext context) => SelectAsync(request, context, host));
    }

    /// <summary>
    /// The selecting long poll (MCP-05): as GET /v1/events's long poll, and for each event delivered the selections it passes. A read
    /// that waits holds a slot while it waits: a device's from its own, and on the local socket one of the MCP servers' own budget,
    /// waiting its turn when every slot is held; a read that does not wait holds none. Each delivery passes the audience check.
    /// </summary>
    private static async Task<IResult> SelectAsync(EventSelectRequest request, HttpContext context, DaemonHost host)
    {
        AuthenticatedDevice? device = context.Items.TryGetValue(RequestAuthenticator.DeviceItem, out object? item) ? item as AuthenticatedDevice : null;
        if (device is not null && request.Principal is not null && request.Principal != device.Principal)
        {
            return Refused(context, device, StatusCodes.Status401Unauthorized, new Problem("denied", "Events belong to the requesting device's principal.", []));
        }

        int wait = request.Wait ?? EventVocabulary.DefaultWaitSeconds;
        if (wait is < 0 or > EventVocabulary.MaxWaitSeconds)
        {
            return Refused(context, device, StatusCodes.Status400BadRequest, new Problem("invalid", $"wait is a whole number of seconds from 0 to {EventVocabulary.MaxWaitSeconds}.", []));
        }

        (EventSubscription? subscription, IReadOnlyList<EventSelector> selectors, Problem? problem) = host.Service.OpenEventSelection(request.After, request.Filter, request.Selections,
            request.Principal, device?.Identity, device?.PublicKey, device?.LocalNetwork == true ? RequestAuthenticator.LocalNetworkProfile : null);
        if (subscription is null)
        {
            Problem refusal = problem ?? new Problem("refused", "The event journal is unavailable.", []);
            return Refused(context, device, refusal.Code switch
            {
                "invalid" => StatusCodes.Status400BadRequest,
                "unavailable" => StatusCodes.Status503ServiceUnavailable,
                _ => StatusCodes.Status401Unauthorized,
            }, refusal);
        }

        if (subscription.Reset)
        {
            return Results.Json(new EventSelectResponse([], [], subscription.Cursor, Reset: true), ProtocolJson.Options);
        }

        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted, host.Stopping);
        deadline.CancelAfter(TimeSpan.FromSeconds(wait));
        IDisposable? slot = null;
        CancellationToken yielding = CancellationToken.None;
        try
        {
            if (wait > 0 && device is not null)
            {
                slot = host.EventStreams.TryAcquire(device.Identity, device.Principal);
                if (slot is null)
                {
                    return Refused(context, device, StatusCodes.Status429TooManyRequests,
                        new Problem("refused", $"A device holds at most {StreamsPerDevice} event streams and long polls at once, one principal's devices {StreamsPerPrincipal} and all devices {StreamsPerRuntime}; close one first.", []));
                }
            }
            else if (wait > 0)
            {
                // The local socket's MCP servers take turns within their own budget; one that waits out its time for a turn answers
                // with no event from where it stood, as a long poll that heard nothing does.
                EventStreamSlots.McpLease? lease = await host.EventStreams.AcquireMcpAsync(deadline.Token).ConfigureAwait(false);
                if (lease is null)
                {
                    return context.RequestAborted.IsCancellationRequested ? Results.Empty : Results.Json(new EventSelectResponse([], [], subscription.Cursor), ProtocolJson.Options);
                }

                slot = lease;
                yielding = lease.Yielding;
            }

            using IDisposable listening = subscription.Listen();
            using var waiting = CancellationTokenSource.CreateLinkedTokenSource(deadline.Token, yielding);
            while (true)
            {
                EventBatch batch = subscription.Read(256, selectors);
                if (batch.Closed)
                {
                    return Refused(context, device, StatusCodes.Status503ServiceUnavailable, new Problem("unavailable", "The runtime is stopping; read events again once it serves.", []));
                }

                if (batch.Refusal is not null)
                {
                    return Refused(context, device, StatusCodes.Status401Unauthorized, new Problem("denied", batch.Refusal, []));
                }

                if (batch.Reset || batch.Events.Count > 0 || wait == 0)
                {
                    return Results.Json(new EventSelectResponse(batch.Events, batch.SelectedPerEvent, subscription.Cursor, batch.Reset), ProtocolJson.Options);
                }

                // A withheld entry wakes this wait like any other, and the wait resumes: nothing returns earlier for it. A turn that
                // ends for another MCP server answers with no event, from where this read stands.
                try
                {
                    await subscription.WaitAsync(waiting.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return context.RequestAborted.IsCancellationRequested ? Results.Empty : Results.Json(new EventSelectResponse([], [], subscription.Cursor), ProtocolJson.Options);
                }
            }
        }
        finally
        {
            slot?.Dispose();
        }
    }

    private static async Task<IResult> ServeAsync(HttpContext context, DaemonHost host)
    {
        AuthenticatedDevice? device = context.Items.TryGetValue(RequestAuthenticator.DeviceItem, out object? item) ? item as AuthenticatedDevice : null;
        IQueryCollection query = context.Request.Query;
        foreach (string name in SingleParameters)
        {
            if (query.TryGetValue(name, out StringValues values) && values.Count > 1)
            {
                return Refused(context, device, StatusCodes.Status400BadRequest, new Problem("invalid", $"Name {name} at most once.", []));
            }
        }

        int wait = EventVocabulary.DefaultWaitSeconds;
        if (query.TryGetValue("wait", out StringValues waitValue)
            && (!int.TryParse(waitValue.ToString(), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out wait) || wait > EventVocabulary.MaxWaitSeconds))
        {
            return Refused(context, device, StatusCodes.Status400BadRequest, new Problem("invalid", $"wait is a whole number of seconds from 0 to {EventVocabulary.MaxWaitSeconds}.", []));
        }

        string? principal = Single(query, "principal");
        if (device is not null && principal is not null && principal != device.Principal)
        {
            return Refused(context, device, StatusCodes.Status401Unauthorized, new Problem("denied", "Events belong to the requesting device's principal.", []));
        }

        // A reconnecting event source sends the newest id it saw, which supersedes the cursor its first request named.
        string? after = context.Request.Headers.TryGetValue("Last-Event-ID", out StringValues last) && !StringValues.IsNullOrEmpty(last) ? last.ToString() : Single(query, "after");
        var filter = new EventFilter(Many(query, "kind"), Many(query, "type"), Many(query, "subject"), Many(query, "to"), Many(query, "category"));
        using IDisposable? slot = host.EventStreams.TryAcquire(device?.Identity, device?.Principal ?? principal ?? EventStreamSlots.LocalSocket);
        if (slot is null)
        {
            return Refused(context, device, StatusCodes.Status429TooManyRequests,
                new Problem("refused", $"A device holds at most {StreamsPerDevice} event streams and long polls at once, one principal's devices {StreamsPerPrincipal}, all devices {StreamsPerRuntime}, and the local socket {StreamsPerDevice}; close one first.", []));
        }

        (EventSubscription? subscription, Problem? problem) = host.Service.OpenEvents(after, filter, principal, device?.Identity, device?.PublicKey,
            device?.LocalNetwork == true ? RequestAuthenticator.LocalNetworkProfile : null);
        if (subscription is null)
        {
            Problem refusal = problem ?? new Problem("refused", "The event stream is unavailable.", []);
            return Refused(context, device, refusal.Code switch
            {
                "invalid" => StatusCodes.Status400BadRequest,
                "unavailable" => StatusCodes.Status503ServiceUnavailable,
                _ => StatusCodes.Status401Unauthorized,
            }, refusal);
        }

        using IDisposable listening = subscription.Listen();
        host.EventStreamOpened?.Invoke();
        if (AcceptsStream(context.Request))
        {
            await StreamAsync(context, host, subscription, signed: device is not null).ConfigureAwait(false);
            return Results.Empty;
        }

        return await LongPollAsync(context, host, device, subscription, wait).ConfigureAwait(false);
    }

    private static async Task<IResult> LongPollAsync(HttpContext context, DaemonHost host, AuthenticatedDevice? device, EventSubscription subscription, int wait)
    {
        if (subscription.Reset)
        {
            return Results.Json(new EventsResponse([], subscription.Cursor, Reset: true), ProtocolJson.Options);
        }

        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted, host.Stopping);
        deadline.CancelAfter(TimeSpan.FromSeconds(wait));
        while (true)
        {
            EventBatch batch = subscription.Read();
            if (batch.Closed)
            {
                return Refused(context, device, StatusCodes.Status503ServiceUnavailable, new Problem("unavailable", "The runtime is stopping; read events again once it serves.", []));
            }

            if (batch.Refusal is not null)
            {
                return Refused(context, device, StatusCodes.Status401Unauthorized, new Problem("denied", batch.Refusal, []));
            }

            if (batch.Reset || batch.Events.Count > 0)
            {
                return Results.Json(new EventsResponse(batch.Events, subscription.Cursor, batch.Reset), ProtocolJson.Options);
            }

            // A withheld entry wakes this wait like any other, and the wait resumes: nothing returns earlier for it.
            try
            {
                await subscription.WaitAsync(deadline.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // The wait ended, or the daemon is stopping: nothing more to deliver. A client that left gets nothing.
                return context.RequestAborted.IsCancellationRequested ? Results.Empty : Results.Json(new EventsResponse([], subscription.Cursor), ProtocolJson.Options);
            }
        }
    }

    private static async Task StreamAsync(HttpContext context, DaemonHost host, EventSubscription subscription, bool signed)
    {
        HttpResponse response = context.Response;
        response.StatusCode = StatusCodes.Status200OK;
        response.ContentType = EventVocabulary.StreamMediaType + "; charset=utf-8";
        response.Headers.CacheControl = "no-cache, no-store";
        context.Features.Get<IHttpResponseBodyFeature>()?.DisableBuffering();
        using var limit = signed ? new CancellationTokenSource(SignedStreamLimit, host.Time) : new CancellationTokenSource();
        using var end = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted, host.Stopping, limit.Token);
        try
        {
            // The opening message carries where the stream starts, so a client that sees no event before a drop still resumes there.
            await WriteAsync(response, subscription.Reset ? Reset(subscription.Cursor) : "retry: 5000\nid: " + subscription.Cursor + "\n\n", end.Token).ConfigureAwait(false);
            DateTimeOffset nextKeepAlive = DateTimeOffset.UtcNow + KeepAlive;
            while (!end.IsCancellationRequested)
            {
                EventBatch batch = subscription.Read();
                if (batch.Closed)
                {
                    // The journal closed without the daemon's stop being signalled: end rather than wait on a journal that never wakes.
                    return;
                }

                if (batch.Refusal is not null)
                {
                    host.Log($"closed an event stream of {subscription.Principal}: {batch.Refusal}");
                    return;
                }

                if (batch.Reset)
                {
                    await WriteAsync(response, Reset(subscription.Cursor), end.Token).ConfigureAwait(false);
                    continue;
                }

                if (batch.Events.Count > 0)
                {
                    var text = new StringBuilder();
                    foreach (EventEnvelope envelope in batch.Events)
                    {
                        text.Append("id: ").Append(envelope.Cursor).Append("\nevent: ").Append(envelope.Kind).Append("\ndata: ")
                            .Append(JsonSerializer.Serialize(envelope, ProtocolJson.Options)).Append("\n\n");
                    }

                    await WriteAsync(response, text.ToString(), end.Token).ConfigureAwait(false);
                }

                TimeSpan untilKeepAlive = nextKeepAlive - DateTimeOffset.UtcNow;
                if (untilKeepAlive > TimeSpan.Zero)
                {
                    using var pause = CancellationTokenSource.CreateLinkedTokenSource(end.Token);
                    Task arrived = subscription.WaitAsync(pause.Token);
                    Task alive = Task.Delay(untilKeepAlive, pause.Token);
                    Task first = await Task.WhenAny(arrived, alive).ConfigureAwait(false);
                    await pause.CancelAsync().ConfigureAwait(false);
                    if (first == arrived && arrived.IsCompletedSuccessfully)
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
                    host.Log($"closed an event stream of {subscription.Principal}: {refusal}");
                    return;
                }

                // An id-only message dispatches no event; its cursor is sealed afresh, so it says nothing about what was withheld.
                await WriteAsync(response, ": keep-alive\nid: " + subscription.Cursor + "\n\n", end.Token).ConfigureAwait(false);
                nextKeepAlive = DateTimeOffset.UtcNow + KeepAlive;
            }
        }
        catch (Exception exception) when (exception is OperationCanceledException or IOException or ObjectDisposedException)
        {
            // The client left, the 60 minutes ended or the daemon is stopping: the stream ends, and a client resumes from its last id.
        }
    }

    private static IResult Key(string key, HttpContext context, DaemonHost host)
    {
        AuthenticatedDevice? device = context.Items.TryGetValue(RequestAuthenticator.DeviceItem, out object? item) ? item as AuthenticatedDevice : null;
        string? principal = Single(context.Request.Query, "principal");
        if (device is not null && principal is not null && principal != device.Principal)
        {
            return Refused(context, device, StatusCodes.Status401Unauthorized, new Problem("denied", "Events belong to the requesting device's principal.", []));
        }

        (EventEnvelope? envelope, Problem? problem) = host.Service.EventByKeyFor(key, principal, device?.Identity, device?.PublicKey,
            device?.LocalNetwork == true ? RequestAuthenticator.LocalNetworkProfile : null);
        if (problem is not null)
        {
            return Refused(context, device, problem.Code == "unavailable" ? StatusCodes.Status503ServiceUnavailable : StatusCodes.Status401Unauthorized, problem);
        }

        // An unknown key and a key the reader may not read are answered alike.
        return envelope is null ? Results.NotFound() : Results.Json(envelope, ProtocolJson.Options);
    }

    private static bool AcceptsStream(HttpRequest request) =>
        MediaTypeHeaderValue.TryParseList(request.Headers.Accept, out IList<MediaTypeHeaderValue>? accepted)
        && accepted.Any(media => string.Equals(media.MediaType.Value, EventVocabulary.StreamMediaType, StringComparison.OrdinalIgnoreCase));

    private static string Reset(string cursor) =>
        "retry: 5000\nid: " + cursor + "\nevent: reset\ndata: " + JsonSerializer.Serialize(new { reset = true, cursor }, ProtocolJson.Options) + "\n\n";

    private static async Task WriteAsync(HttpResponse response, string text, CancellationToken cancellationToken)
    {
        await response.Body.WriteAsync(Encoding.UTF8.GetBytes(text), cancellationToken).ConfigureAwait(false);
        await response.Body.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    private static string? Single(IQueryCollection query, string name) =>
        query.TryGetValue(name, out StringValues values) && !StringValues.IsNullOrEmpty(values) ? values.ToString() : null;

    private static string[]? Many(IQueryCollection query, string name) =>
        query.TryGetValue(name, out StringValues values) && values.Count > 0 ? [.. values.Where(v => v is not null).Select(v => v!)] : null;

    private static IResult Refused(HttpContext context, AuthenticatedDevice? device, int status, Problem problem)
    {
        if (device?.LocalNetwork == true)
        {
            context.Response.Headers.Connection = "close";
        }

        return Results.Json(problem, ProtocolJson.Options, statusCode: status);
    }
}

/// <summary>
/// The streams and long polls held now. Devices share a pool of <see cref="EventRoutes.StreamsPerRuntime"/>, at most
/// <see cref="EventRoutes.StreamsPerDevice"/> each and <see cref="EventRoutes.StreamsPerPrincipal"/> for all of one principal's
/// devices; the local socket's user may take free slots of that pool too and always has <see cref="EventRoutes.LocalReserve"/> more,
/// so paired devices can never lock out the runtime machine's own user.
/// </summary>
internal sealed class EventStreamSlots
{
    /// <summary>The holder name of the local socket's operating-system user.</summary>
    internal const string LocalSocket = "local-socket";

    private readonly object gate = new();
    private readonly Dictionary<string, int> held = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> principals = new(StringComparer.Ordinal);
    private readonly LinkedList<McpLease> mcp = new();
    private TaskCompletionSource mcpFreed = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int devices;
    private int local;

    /// <summary>How many of the local socket's MCP slots are held.</summary>
    public int Mcp
    {
        get { lock (gate) return mcp.Count; }
    }

    /// <summary>
    /// One of the local socket's MCP slots (MCP-05), a budget of its own that never takes the command line's. When every slot is held,
    /// the oldest holder that has held its slot for <see cref="EventRoutes.McpTurn"/> is asked to yield, and this waits until a slot
    /// frees: sessions beyond the budget take turns rather than being refused. Null when <paramref name="cancellationToken"/> ends
    /// the wait first.
    /// </summary>
    public async Task<McpLease?> AcquireMcpAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            Task freed;
            TimeSpan untilTurn;
            lock (gate)
            {
                if (mcp.Count < EventRoutes.McpOnLocalSocket)
                {
                    var lease = new McpLease(this);
                    lease.Node = mcp.AddLast(lease);
                    return lease;
                }

                McpLease oldest = mcp.First!.Value;
                TimeSpan held = Stopwatch.GetElapsedTime(oldest.Since);
                if (held >= EventRoutes.McpTurn)
                {
                    oldest.Yield();
                    untilTurn = EventRoutes.McpTurn;
                }
                else
                {
                    untilTurn = EventRoutes.McpTurn - held;
                }

                freed = mcpFreed.Task;
            }

            try
            {
                await Task.WhenAny(freed, Task.Delay(untilTurn, cancellationToken)).ConfigureAwait(false);
            }
            catch (ObjectDisposedException)
            {
                return null;
            }

            if (cancellationToken.IsCancellationRequested)
            {
                return null;
            }
        }
    }

    /// <summary>A slot for a device of a principal, or for the local socket when <paramref name="device"/> is null; null when a limit is reached.</summary>
    public IDisposable? TryAcquire(string? device, string principal)
    {
        lock (gate)
        {
            if (device is null)
            {
                if (local >= EventRoutes.StreamsPerDevice || devices + local >= EventRoutes.StreamsPerRuntime + EventRoutes.LocalReserve)
                {
                    return null;
                }

                local++;
                return new Slot(this, null, principal);
            }

            if (held.GetValueOrDefault(device) >= EventRoutes.StreamsPerDevice || principals.GetValueOrDefault(principal) >= EventRoutes.StreamsPerPrincipal
                || devices >= EventRoutes.StreamsPerRuntime || devices + local >= EventRoutes.StreamsPerRuntime + EventRoutes.LocalReserve)
            {
                return null;
            }

            held[device] = held.GetValueOrDefault(device) + 1;
            principals[principal] = principals.GetValueOrDefault(principal) + 1;
            devices++;
            return new Slot(this, device, principal);
        }
    }

    /// <summary>How many slots are held in all.</summary>
    public int Total
    {
        get { lock (gate) return devices + local; }
    }

    private void Release(string? device, string principal)
    {
        lock (gate)
        {
            if (device is null)
            {
                local--;
                return;
            }

            devices--;
            if (--held[device] == 0)
            {
                held.Remove(device);
            }

            if (--principals[principal] == 0)
            {
                principals.Remove(principal);
            }
        }
    }

    private void ReleaseMcp(McpLease lease)
    {
        TaskCompletionSource freed;
        lock (gate)
        {
            if (lease.Node is not null)
            {
                mcp.Remove(lease.Node);
                lease.Node = null;
            }

            freed = mcpFreed;
            mcpFreed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        freed.TrySetResult();
    }

    /// <summary>A held MCP slot of the local socket: asked to yield once it has held its turn and another waits.</summary>
    internal sealed class McpLease : IDisposable
    {
        private readonly EventStreamSlots slots;
        private readonly CancellationTokenSource yielding = new();
        private int released;

        internal McpLease(EventStreamSlots slots)
        {
            this.slots = slots;
            Since = Stopwatch.GetTimestamp();
        }

        /// <summary>When it was taken.</summary>
        internal long Since { get; }

        internal LinkedListNode<McpLease>? Node { get; set; }

        /// <summary>Cancelled when another waits and this one has held its turn: its read answers at once.</summary>
        internal CancellationToken Yielding => yielding.Token;

        internal void Yield()
        {
            try
            {
                yielding.Cancel();
            }
            catch (ObjectDisposedException)
            {
                // Already released.
            }
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref released, 1) == 0)
            {
                slots.ReleaseMcp(this);
                yielding.Dispose();
            }
        }
    }

    private sealed class Slot(EventStreamSlots slots, string? device, string principal) : IDisposable
    {
        private int released;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref released, 1) == 0)
            {
                slots.Release(device, principal);
            }
        }
    }
}
