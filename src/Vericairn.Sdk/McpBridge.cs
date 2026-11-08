// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Buffers.Text;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Vericairn.Protocol;
using Vericairn.Records.Model;
using Vericairn.Records.Signing;

namespace Vericairn.Sdk;

/// <summary>
/// The bridge of <c>vericairn mcp serve --connection</c>, from 0.9.2 (MCP-03): a server over standard input and output for a local
/// MCP client that forwards each message to a remote estate's /mcp in signed mode, over a paired connection, and performs both steps
/// of a write itself. Every HTTP request is signed by this machine's device key, as the connection's requests are; a write is first
/// prepared, the prepared record is checked to be the one its tool names for this device's principal, and only then is its hash
/// signed with the same key and the call repeated with the plan and the signature. The bridge decides nothing: the runtime judges
/// every call as its API operation for this device. From 0.9.2 (MCP-05), while the client holds a resource subscription the runtime
/// accepted, the bridge also holds the session's server stream, signed as every request is, and writes each resource notification
/// it carries to the client as a line between responses; it signs nothing a notification says.
/// </summary>
internal sealed class McpBridge : IDisposable
{
    private const string Endpoint = "mcp";
    private const string SessionHeader = "Mcp-Session-Id";
    private const string VersionHeader = "MCP-Protocol-Version";

    /// <summary>
    /// The writes the bridge prepares and signs, each with the record type it authors and what it is about: the subject's type and
    /// the argument naming it. Any other call is forwarded once, as it came, and nothing it answers is ever signed.
    /// </summary>
    private static readonly Dictionary<string, (string Type, string Subject, string? Argument)> Writes = new(StringComparer.Ordinal)
    {
        ["work_attempt"] = ("attempt", "assignment", "assignment"),
        ["work_progress"] = ("work-progress", "attempt", "attempt"),
        ["work_result"] = ("result", "attempt", "attempt"),
        ["work_amend"] = ("amendment", "assignment", "assignment"),
        ["document_review"] = ("document-review", "living-document", null),
    };

    /// <summary>The body fields each write's record may hold beyond those its arguments supply: what the runtime adds.</summary>
    private static readonly Dictionary<string, string[]> Added = new(StringComparer.Ordinal)
    {
        ["work_attempt"] = ["startedAt"],
        ["work_progress"] = ["completedAt"],
        ["work_result"] = ["progressClaim"],
        ["work_amend"] = [],
        ["document_review"] = ["content", "answers", "independence", "findings", "checks", "covered", "sections"],
    };

    private readonly HttpClient http;
    private readonly string principal;
    private readonly Func<byte[], byte[]> sign;
    private readonly TextWriter? log;
    private readonly SemaphoreSlim opening = new(1, 1);
    private readonly object subscribing = new();
    private readonly HashSet<string> subscribed = new(StringComparer.Ordinal);
    private Func<JsonObject, Task>? writer;
    private CancellationToken running;
    private CancellationTokenSource? streaming;
    private Task stream = Task.CompletedTask;
    private string? session;
    private string? version;
    private JsonNode? initialize;
    private int reopened;

    /// <summary>
    /// A bridge over a signed HTTP transport to the runtime, for the principal its device acts for; <paramref name="sign"/> signs a
    /// record-signing digest with that device's key.
    /// </summary>
    internal McpBridge(HttpClient http, string principal, Func<byte[], byte[]> sign, TextWriter? log = null)
    {
        this.http = http ?? throw new ArgumentNullException(nameof(http));
        ArgumentException.ThrowIfNullOrWhiteSpace(principal);
        this.principal = principal;
        this.sign = sign ?? throw new ArgumentNullException(nameof(sign));
        this.log = log;
    }

    /// <summary>How long a runtime keeps a prepared record for its device's signature.</summary>
    private static readonly TimeSpan PlanLifetime = TimeSpan.FromMinutes(10);

    /// <summary>The clock a prepared record's time is judged by; a test sets it.</summary>
    internal TimeProvider Clock { get; init; } = TimeProvider.System;

    /// <summary>The session the runtime opened for this bridge, once initialize has run.</summary>
    internal string? Session => session;

    /// <inheritdoc />
    public void Dispose() => opening.Dispose();

    /// <summary>
    /// Serves until the input ends: one JSON-RPC message per line in, one per line out. Requests run concurrently, so a slow call
    /// never holds back a ping or a cancellation; initialize runs alone, first. When the input ends, the session is ended.
    /// </summary>
    internal async Task RunAsync(TextReader input, TextWriter output, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(output);
        using var writing = new SemaphoreSlim(1, 1);
        var pending = new List<Task>();

        async Task WriteAsync(JsonObject message)
        {
            await writing.WaitAsync(CancellationToken.None).ConfigureAwait(false);
            try
            {
                await output.WriteLineAsync(message.ToJsonString()).ConfigureAwait(false);
                await output.FlushAsync(CancellationToken.None).ConfigureAwait(false);
            }
            finally
            {
                writing.Release();
            }
        }

        writer = WriteAsync;
        running = cancellationToken;
        try
        {
            while (await Task.Run(() => input.ReadLineAsync(cancellationToken).AsTask(), cancellationToken).ConfigureAwait(false) is string line)
            {
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                JsonNode? parsed;
                try
                {
                    parsed = JsonNode.Parse(line);
                }
                catch (JsonException exception)
                {
                    await WriteAsync(Error(null, -32700, "Parse error: " + exception.Message)).ConfigureAwait(false);
                    continue;
                }

                if (parsed is not JsonObject message)
                {
                    await WriteAsync(Error(null, -32600, "Invalid request: not an object.")).ConfigureAwait(false);
                    continue;
                }

                JsonNode? id = message["id"];
                string? method = Text(message["method"]);
                if (method is null && id is not null && message["result"] is null && message["error"] is null)
                {
                    await WriteAsync(Error(id.DeepClone(), -32600, "Invalid request: no method.")).ConfigureAwait(false);
                    continue;
                }

                if (id is null || method is null)
                {
                    // A notification or a response: forwarded, its acceptance reported only on standard error.
                    await NotifyAsync(message, cancellationToken).ConfigureAwait(false);
                    continue;
                }

                if (method == "initialize")
                {
                    await Task.WhenAll(pending).ConfigureAwait(false);
                    pending.Clear();
                    await WriteAsync(await InitializeAsync(message, cancellationToken).ConfigureAwait(false)).ConfigureAwait(false);
                    continue;
                }

                pending.RemoveAll(task => task.IsCompleted);
                pending.Add(Task.Run(async () => await WriteAsync(await RequestAsync(message, method, cancellationToken).ConfigureAwait(false)).ConfigureAwait(false), CancellationToken.None));
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Ending the bridge stops observing; it undoes nothing the runtime did.
        }
        finally
        {
            await Task.WhenAll(pending).ConfigureAwait(false);
            await StopStreamAsync().ConfigureAwait(false);
            await EndAsync().ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Keeps what the client subscribed to after the runtime accepted it, holding the session's server stream while anything is
    /// subscribed and letting it go when nothing is, so a session that subscribes to nothing holds none of its device's stream slots.
    /// </summary>
    private void Track(string method, string uri)
    {
        lock (subscribing)
        {
            if (method == "resources/subscribe")
            {
                subscribed.Add(uri);
            }
            else
            {
                subscribed.Remove(uri);
            }

            if (subscribed.Count > 0 && streaming is null && writer is { } write)
            {
                // The previous stream, if one is still letting go, ends first: a session holds one server stream.
                var source = CancellationTokenSource.CreateLinkedTokenSource(running);
                streaming = source;
                Task previous = stream;
                stream = Task.Run(async () =>
                {
                    try
                    {
                        await previous.ConfigureAwait(false);
                        await StreamAsync(write, source.Token).ConfigureAwait(false);
                    }
                    finally
                    {
                        source.Dispose();
                    }
                }, CancellationToken.None);
            }
            else if (subscribed.Count == 0 && streaming is not null)
            {
                streaming.Cancel();
                streaming = null;
            }
        }
    }

    /// <summary>Lets the server stream go and waits until it has.</summary>
    private async Task StopStreamAsync()
    {
        Task held;
        lock (subscribing)
        {
            subscribed.Clear();
            if (streaming is not null)
            {
                streaming.Cancel();
                streaming = null;
            }

            held = stream;
        }

        await held.ConfigureAwait(false);
    }

    /// <summary>
    /// Holds the session's server stream until cancelled, opening it again after the runtime ends it (after its hour, a restart or a
    /// new session) and waiting longer after each failure; each JSON-RPC notification it carries is written to the client. A stream
    /// the runtime refuses for good, because the device no longer stands, is retried rarely: the next request says why.
    /// </summary>
    private async Task StreamAsync(Func<JsonObject, Task> write, CancellationToken cancellationToken)
    {
        int failures = 0;
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                if (session is string open)
                {
                    failures = await FollowAsync(open, write, cancellationToken).ConfigureAwait(false) ? 0 : failures + 1;
                }
            }
            catch (Exception exception) when (!cancellationToken.IsCancellationRequested
                && exception is HttpRequestException or IOException or TimeoutException or InvalidOperationException or OperationCanceledException)
            {
                failures++;
                log?.WriteLine("vericairn mcp: the server stream dropped: " + (ClientFailure.FromException(exception)?.Message ?? exception.Message));
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(failures == 0 ? 0.2 : Math.Min(30, Math.Pow(2, Math.Min(failures, 5)))), cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    /// <summary>The largest data block of one message on the server stream that the bridge reads; a larger one is discarded unread.</summary>
    internal const int MaxStreamBlock = 64 * 1024;

    /// <summary>The notifications the bridge writes to its client: the server stream carries nothing else it would pass on.</summary>
    private static readonly HashSet<string> Forwarded = new(StringComparer.Ordinal) { "notifications/resources/updated", "notifications/resources/list_changed" };

    /// <summary>
    /// Reads one server stream to its end; false when the runtime refused to open it. Each message's data is held to
    /// <see cref="MaxStreamBlock"/> characters, a line included, and only a resource notification is written to the client.
    /// </summary>
    private async Task<bool> FollowAsync(string open, Func<JsonObject, Task> write, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(Endpoint, UriKind.Relative));
        request.Headers.Accept.ParseAdd("text/event-stream");
        request.Headers.TryAddWithoutValidation(SessionHeader, open);
        if (version is not null)
        {
            request.Headers.TryAddWithoutValidation(VersionHeader, version);
        }

        using HttpResponseMessage response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode != HttpStatusCode.OK)
        {
            return false;
        }

        Stream body = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        await using (body.ConfigureAwait(false))
        {
            using var reader = new StreamReader(body, new UTF8Encoding(false));
            var lines = new BoundedLines(reader, MaxStreamBlock);
            var data = new StringBuilder();
            string kind = "message";
            bool oversized = false;
            while (await lines.NextAsync(cancellationToken).ConfigureAwait(false) is { } next)
            {
                (string line, bool overflowed) = next;
                if (overflowed || line.Length > 0)
                {
                    if (overflowed)
                    {
                        oversized = true;
                    }
                    else if (line.StartsWith("data:", StringComparison.Ordinal))
                    {
                        ReadOnlySpan<char> value = line.AsSpan(5).TrimStart(' ');
                        if (data.Length + value.Length + 1 > MaxStreamBlock)
                        {
                            oversized = true;
                        }
                        else if (!oversized)
                        {
                            data.Append(value).Append('\n');
                        }
                    }
                    else if (line.StartsWith("event:", StringComparison.Ordinal))
                    {
                        kind = line[6..].Trim();
                    }

                    continue;
                }

                if (oversized)
                {
                    log?.WriteLine($"vericairn mcp: a message on the server stream exceeded {MaxStreamBlock} characters and was discarded");
                }
                else if (kind == "message" && data.Length > 0 && TryParse(data.ToString()) is JsonObject message && message["id"] is null
                    && Text(message["method"]) is string method && Forwarded.Contains(method))
                {
                    await write(message).ConfigureAwait(false);
                }

                data.Clear();
                kind = "message";
                oversized = false;
            }
        }

        return true;
    }

    /// <summary>Lines of a text stream, each held to a bound: a longer line is consumed to its end and reported as overflowed.</summary>
    private sealed class BoundedLines(TextReader reader, int bound)
    {
        private readonly char[] buffer = new char[4096];
        private readonly StringBuilder line = new();
        private int position;
        private int length;

        /// <summary>The next line and whether it overflowed the bound; null at the stream's end.</summary>
        public async Task<(string Line, bool Overflowed)?> NextAsync(CancellationToken cancellationToken)
        {
            line.Clear();
            bool overflowed = false;
            while (true)
            {
                if (position == length)
                {
                    length = await reader.ReadAsync(buffer.AsMemory(), cancellationToken).ConfigureAwait(false);
                    position = 0;
                    if (length == 0)
                    {
                        return line.Length > 0 || overflowed ? (line.ToString(), overflowed) : null;
                    }
                }

                int end = Array.IndexOf(buffer, '\n', position, length - position);
                int take = (end < 0 ? length : end) - position;
                if (!overflowed)
                {
                    if (line.Length + take > bound)
                    {
                        overflowed = true;
                        line.Clear();
                    }
                    else
                    {
                        line.Append(buffer, position, take);
                    }
                }

                position += take;
                if (end >= 0)
                {
                    position++;
                    if (line.Length > 0 && line[^1] == '\r')
                    {
                        line.Length--;
                    }

                    return (line.ToString(), overflowed);
                }
            }
        }
    }

    private static JsonObject? TryParse(string text)
    {
        try
        {
            return JsonNode.Parse(text) as JsonObject;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private async Task<JsonObject> InitializeAsync(JsonObject message, CancellationToken cancellationToken)
    {
        JsonNode id = message["id"]!.DeepClone();
        initialize = message["params"]?.DeepClone();
        if (session is not null)
        {
            await EndAsync().ConfigureAwait(false);
        }

        try
        {
            (HttpStatusCode status, JsonObject? body, string? problem, string? opened) = await PostAsync(message, cancellationToken).ConfigureAwait(false);
            if (status != HttpStatusCode.OK || body is null)
            {
                return Error(id, -32603, Refused(status, problem));
            }

            if (body["result"] is not JsonObject result || Text(result["protocolVersion"]) is not string negotiated || opened is null)
            {
                return Error(id, -32603, "The runtime's answer to initialize names no session and protocol version; nothing was opened.");
            }

            session = opened;
            version = negotiated;
            return body;
        }
        catch (Exception exception) when (exception is HttpRequestException or IOException or TimeoutException or InvalidOperationException or JsonException
            || exception is OperationCanceledException && !cancellationToken.IsCancellationRequested)
        {
            return Error(id, -32603, "The runtime could not be reached: " + (ClientFailure.FromException(exception)?.Message ?? exception.Message));
        }
    }

    private async Task<JsonObject> RequestAsync(JsonObject message, string method, CancellationToken cancellationToken)
    {
        JsonNode id = message["id"]!.DeepClone();
        try
        {
            if (method == "tools/call")
            {
                return await CallAsync(message, id, cancellationToken).ConfigureAwait(false);
            }

            if (method is "resources/subscribe" or "resources/unsubscribe")
            {
                JsonObject answer = await ForwardAsync(message, id, cancellationToken).ConfigureAwait(false);
                if (answer["result"] is not null && Text(message["params"]?["uri"]) is string uri)
                {
                    Track(method, uri);
                }

                return answer;
            }

            return await ForwardAsync(message, id, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is HttpRequestException or IOException or TimeoutException or InvalidOperationException or JsonException)
        {
            return Error(id, -32603, "The runtime could not be reached: " + (ClientFailure.FromException(exception)?.Message ?? exception.Message));
        }
    }

    /// <summary>
    /// A write in two steps: prepared, checked to be the record its call asks for, signed by this device, repeated with the plan.
    /// Any other call is forwarded once, unchanged, and nothing it returns is signed.
    /// </summary>
    private async Task<JsonObject> CallAsync(JsonObject message, JsonNode id, CancellationToken cancellationToken)
    {
        JsonObject parameters = message["params"] as JsonObject ?? [];
        string? tool = Text(parameters["name"]);
        if (tool is null || !Writes.ContainsKey(tool))
        {
            return await ForwardAsync(message, id, cancellationToken).ConfigureAwait(false);
        }

        JsonObject arguments = parameters["arguments"] as JsonObject ?? [];
        JsonObject ownMeta = parameters["_meta"] as JsonObject ?? [];
        JsonObject preparing = (JsonObject)message.DeepClone();
        JsonObject preparingParameters = preparing["params"] as JsonObject ?? [];
        preparing["params"] = preparingParameters;
        JsonObject meta = (JsonObject)ownMeta.DeepClone();
        meta.Remove("vericairn/plan");
        meta.Remove("vericairn/signature");
        meta["vericairn/prepare"] = true;
        preparingParameters["_meta"] = meta;

        JsonObject first = await ForwardAsync(preparing, id, cancellationToken).ConfigureAwait(false);
        if (first["result"] is not JsonObject result || result["isError"] is JsonValue failed && failed.TryGetValue(out bool isError) && isError
            || result["_meta"]?["vericairn/plan"]?["prepared"] is not JsonObject prepared)
        {
            // An active progress note, which writes no record, or a refusal: the runtime prepared nothing, and this is the answer.
            return first;
        }

        if (Unsignable(prepared, tool, arguments) is string refusal)
        {
            log?.WriteLine("vericairn mcp: " + refusal);
            return Result(id, refusal, isError: true);
        }

        string hash = Text(prepared["hash"])!;
        string signature;
        try
        {
            signature = Base64Url.EncodeToString(sign(RecordSigning.Digest(hash)));
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            return Error(id, -32603, $"This device did not sign the prepared record: {exception.Message} Nothing was written.");
        }

        JsonObject signing = (JsonObject)message.DeepClone();
        JsonObject signingParameters = signing["params"] as JsonObject ?? [];
        signing["params"] = signingParameters;
        JsonObject signed = (JsonObject)ownMeta.DeepClone();
        signed.Remove("vericairn/prepare");
        signed["vericairn/plan"] = Text(prepared["plan"]);
        signed["vericairn/signature"] = signature;
        signingParameters["_meta"] = signed;
        return await ForwardAsync(signing, id, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Why this device does not sign a prepared record, or null. The record must be one a write tool authors, of that tool's type, hash
    /// to the hash the runtime asks to be signed, carry no signature, name this device's principal as author, supersede nothing, be
    /// dated now, be about the very record the call's arguments name, and hold every field those arguments supply unchanged, with
    /// nothing beyond what the runtime adds to such a record. Anything else is refused.
    /// </summary>
    internal string? Unsignable(JsonObject prepared, string tool, JsonObject arguments)
    {
        const string Nothing = " Nothing was signed or written.";
        if (!Writes.TryGetValue(tool, out (string Type, string Subject, string? Argument) shape))
        {
            return $"{tool} is not a write this bridge signs; the runtime prepared a record for it." + Nothing;
        }

        if (Text(prepared["hash"]) is not string hash || Text(prepared["plan"]) is null || prepared["record"] is not JsonObject record)
        {
            return "The runtime's prepared record is incomplete." + Nothing;
        }

        var findings = new FindingList();
        RecordEnvelope? envelope;
        try
        {
            envelope = RecordParser.Parse(Encoding.UTF8.GetBytes(record.ToJsonString()), "prepared", findings, checkFormatting: false);
        }
        catch (Exception exception) when (exception is ArgumentException or FormatException or InvalidOperationException or JsonException)
        {
            envelope = null;
        }

        if (envelope is null || envelope.ComputedHash != hash || envelope.Signatures.Count > 0 || Text(prepared["id"]) is string named && named != envelope.Id)
        {
            return "The runtime asked this device to sign a hash its prepared record does not have." + Nothing;
        }

        if (!string.Equals(envelope.By, principal, StringComparison.Ordinal))
        {
            return $"The prepared record names {envelope.By} as its author, not this device's principal {principal}." + Nothing;
        }

        if (envelope.Type != shape.Type)
        {
            return $"{tool} writes a record of type {shape.Type}, and the runtime prepared one of type {envelope.Type}." + Nothing;
        }

        // An honest runtime prepares a new record, never a revision of another, and dates it now: within a plan's ten minutes of this
        // machine's clock, allowing the skew a signed request itself may have.
        if (envelope.Supersedes is not null)
        {
            return $"The prepared record supersedes {envelope.Supersedes}; {tool} writes a new record." + Nothing;
        }

        DateTimeOffset now = Clock.GetUtcNow();
        if (!RecordParser.IsTimestamp(envelope.At) || envelope.AtTime < now - PlanLifetime - RequestSigning.MaxSkew || envelope.AtTime > now + RequestSigning.MaxSkew)
        {
            return $"The prepared record is dated {envelope.At}, not now." + Nothing;
        }

        if (envelope.Subject is not { } subject || subject.Type != shape.Subject
            || shape.Argument is string argument && Text(arguments[argument]) is string asked && subject.Id != asked
            || shape.Argument is string required && Text(arguments[required]) is null)
        {
            return $"The prepared record is not about the {shape.Subject} this call names." + Nothing;
        }

        return BodyRefusal(tool, record["body"] as JsonObject, arguments, Text(record["at"])) is string body ? body + Nothing : null;
    }

    /// <summary>Why a prepared record's body is not the one its call's arguments supply, or null.</summary>
    private static string? BodyRefusal(string tool, JsonObject? body, JsonObject arguments, string? at)
    {
        if (body is null)
        {
            return "The prepared record has no body.";
        }

        var supplied = new JsonObject();
        switch (tool)
        {
            case "work_attempt":
                supplied["workspace"] = arguments["workspace"]?.DeepClone();
                supplied["note"] = arguments["note"]?.DeepClone();
                if (Text(body["startedAt"]) != at) return "The prepared attempt states a start other than its own time.";
                break;
            case "work_progress":
                if (Text(arguments["state"]) != "completed") return "Only a completed phase is a record; the runtime prepared one for another claim.";
                supplied["phase"] = arguments["phase"]?.DeepClone();
                supplied["note"] = arguments["note"]?.DeepClone();
                if (arguments.ContainsKey("progress")) supplied["progress"] = arguments["progress"]?.DeepClone();
                if (arguments.ContainsKey("statusText")) supplied["statusText"] = arguments["statusText"]?.DeepClone();
                if (Text(body["completedAt"]) != at) return "The prepared progress record states a completion other than its own time.";
                break;
            case "work_result":
            case "work_amend":
                if (arguments["body"] is not JsonObject given) return "The call supplies no body, yet the runtime prepared one.";
                foreach ((string name, JsonNode? value) in given)
                {
                    supplied[name] = value?.DeepClone();
                }

                break;
            case "document_review":
                supplied["outcome"] = arguments["outcome"]?.DeepClone();
                supplied["summary"] = arguments["summary"]?.DeepClone();
                if (body["findings"] is not JsonArray { Count: 0 }) return "The prepared review carries findings the call did not supply.";
                if (arguments.ContainsKey("sections") != body.ContainsKey("sections")) return "The prepared review's sections are not the ones the call names.";
                break;
            default:
                return "The call is not a write this bridge signs.";
        }

        foreach ((string name, JsonNode? value) in supplied)
        {
            if (!body.TryGetPropertyValue(name, out JsonNode? held) || !JsonNode.DeepEquals(held, value))
            {
                return $"The prepared record's {name} is not the one the call supplies.";
            }
        }

        string[] added = Added[tool];
        return body.Select(p => p.Key).FirstOrDefault(name => !supplied.ContainsKey(name) && !added.Contains(name, StringComparer.Ordinal)) is string extra
            ? $"The prepared record holds {extra}, which the call does not supply."
            : null;
    }

    /// <summary>Forwards a request and returns its answer; a session the runtime ended is opened again once and the request repeated.</summary>
    private async Task<JsonObject> ForwardAsync(JsonObject message, JsonNode id, CancellationToken cancellationToken)
    {
        (HttpStatusCode status, JsonObject? body, string? problem, _) = await PostAsync(message, cancellationToken).ConfigureAwait(false);
        if (status == HttpStatusCode.NotFound && await ReopenAsync(cancellationToken).ConfigureAwait(false))
        {
            (status, body, problem, _) = await PostAsync(message, cancellationToken).ConfigureAwait(false);
        }

        return status == HttpStatusCode.OK && body is not null ? body : Error(id, -32603, Refused(status, problem));
    }

    private async Task NotifyAsync(JsonObject message, CancellationToken cancellationToken)
    {
        try
        {
            (HttpStatusCode status, _, string? problem, _) = await PostAsync(message, cancellationToken).ConfigureAwait(false);
            if (status != HttpStatusCode.Accepted)
            {
                log?.WriteLine($"vericairn mcp: the runtime did not accept a notification: {Refused(status, problem)}");
            }
        }
        catch (Exception exception) when (exception is HttpRequestException or IOException or TimeoutException or InvalidOperationException)
        {
            log?.WriteLine("vericairn mcp: a notification did not reach the runtime: " + exception.Message);
        }
    }

    /// <summary>Opens a new session after the runtime ended one (an idle hour, or its restart), with the client's own initialize.</summary>
    private async Task<bool> ReopenAsync(CancellationToken cancellationToken)
    {
        if (initialize is null || session is null)
        {
            return false;
        }

        string? ended = session;
        await opening.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (session != ended)
            {
                return true;
            }

            session = null;
            var open = new JsonObject
            {
                ["jsonrpc"] = "2.0", ["id"] = "vericairn-bridge-" + Interlocked.Increment(ref reopened).ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["method"] = "initialize", ["params"] = initialize.DeepClone(),
            };
            (HttpStatusCode status, JsonObject? body, _, string? opened) = await PostAsync(open, cancellationToken).ConfigureAwait(false);
            if (status != HttpStatusCode.OK || body is null || opened is null)
            {
                return false;
            }

            session = opened;
            version = Text(body["result"]?["protocolVersion"]);
            log?.WriteLine("vericairn mcp: the runtime ended the session; a new one is open");
            await PostAsync(new JsonObject { ["jsonrpc"] = "2.0", ["method"] = "notifications/initialized" }, cancellationToken).ConfigureAwait(false);
            await ResubscribeAsync(cancellationToken).ConfigureAwait(false);
            return session is not null;
        }
        finally
        {
            opening.Release();
        }
    }

    /// <summary>
    /// Subscribes the new session to what the client had subscribed to, since the runtime's subscriptions ended with the old one; a
    /// resource the runtime no longer accepts is dropped, and the client hears nothing more of it.
    /// </summary>
    private async Task ResubscribeAsync(CancellationToken cancellationToken)
    {
        string[] uris;
        lock (subscribing)
        {
            uris = [.. subscribed];
        }

        foreach (string uri in uris)
        {
            var subscribe = new JsonObject
            {
                ["jsonrpc"] = "2.0", ["id"] = "vericairn-bridge-" + Interlocked.Increment(ref reopened).ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["method"] = "resources/subscribe", ["params"] = new JsonObject { ["uri"] = uri },
            };
            (HttpStatusCode status, JsonObject? body, string? problem, _) = await PostAsync(subscribe, cancellationToken).ConfigureAwait(false);
            if (status != HttpStatusCode.OK || body?["result"] is null)
            {
                log?.WriteLine($"vericairn mcp: the new session did not take the subscription to {uri}: {Refused(status, problem ?? Text(body?["error"]?["message"]))}");
                Track("resources/unsubscribe", uri);
            }
        }
    }

    private async Task EndAsync()
    {
        if (session is not string open)
        {
            return;
        }

        session = null;
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Delete, new Uri(Endpoint, UriKind.Relative));
            request.Headers.TryAddWithoutValidation(SessionHeader, open);
            if (version is not null)
            {
                request.Headers.TryAddWithoutValidation(VersionHeader, version);
            }

            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            using HttpResponseMessage response = await http.SendAsync(request, timeout.Token).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is HttpRequestException or IOException or OperationCanceledException or InvalidOperationException)
        {
            // The runtime ends an unused session within the hour by itself.
            log?.WriteLine("vericairn mcp: the session could not be ended now: " + exception.Message);
        }
    }

    private async Task<(HttpStatusCode Status, JsonObject? Body, string? Problem, string? Session)> PostAsync(JsonObject message, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(Endpoint, UriKind.Relative))
        {
            Content = new StringContent(message.ToJsonString(), new UTF8Encoding(false), "application/json"),
        };
        request.Headers.Accept.ParseAdd("application/json");
        request.Headers.Accept.ParseAdd("text/event-stream");
        if (session is not null && Text(message["method"]) != "initialize")
        {
            request.Headers.TryAddWithoutValidation(SessionHeader, session);
            if (version is not null)
            {
                request.Headers.TryAddWithoutValidation(VersionHeader, version);
            }
        }

        using HttpResponseMessage response = await http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        string text = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        JsonObject? body = null;
        string? problem = null;
        if (text.Length > 0)
        {
            try
            {
                JsonNode? node = JsonNode.Parse(text);
                body = node as JsonObject;
                problem = Text(body?["message"]) ?? Text(body?["error"]?["message"]);
            }
            catch (JsonException)
            {
                problem = text.Length > 300 ? text[..300] : text;
            }
        }

        string? opened = response.Headers.TryGetValues(SessionHeader, out IEnumerable<string>? values) ? values.FirstOrDefault() : null;
        return (response.StatusCode, response.StatusCode == HttpStatusCode.OK ? body : null, problem, opened);
    }

    /// <summary>A JSON string's value, or null for anything else.</summary>
    private static string? Text(JsonNode? node) => node is JsonValue value && value.TryGetValue(out string? text) ? text : null;

    private static string Refused(HttpStatusCode status, string? problem) =>
        $"The runtime answered {(int)status} {status}{(problem is null ? "." : ": " + problem)}";

    private static JsonObject Result(JsonNode id, string text, bool isError) => new()
    {
        ["jsonrpc"] = "2.0", ["id"] = id,
        ["result"] = new JsonObject { ["content"] = new JsonArray(new JsonObject { ["type"] = "text", ["text"] = text }), ["isError"] = isError },
    };

    private static JsonObject Error(JsonNode? id, int code, string message) => new() { ["jsonrpc"] = "2.0", ["id"] = id, ["error"] = new JsonObject { ["code"] = code, ["message"] = message } };
}
