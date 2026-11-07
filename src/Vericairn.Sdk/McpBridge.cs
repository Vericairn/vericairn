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
/// every call as its API operation for this device.
/// </summary>
internal sealed class McpBridge : IDisposable
{
    private const string Endpoint = "mcp";
    private const string SessionHeader = "Mcp-Session-Id";
    private const string VersionHeader = "MCP-Protocol-Version";

    /// <summary>The record type each prepared write authors, which the bridge checks before its device signs.</summary>
    private static readonly Dictionary<string, string> Writes = new(StringComparer.Ordinal)
    {
        ["work_attempt"] = "attempt",
        ["work_progress"] = "work-progress",
        ["work_result"] = "result",
        ["work_amend"] = "amendment",
        ["document_review"] = "document-review",
    };

    private readonly HttpClient http;
    private readonly string principal;
    private readonly Func<byte[], byte[]> sign;
    private readonly TextWriter? log;
    private readonly SemaphoreSlim opening = new(1, 1);
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
                string? method = message["method"] is JsonValue m && m.TryGetValue(out string? name) ? name : null;
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
            await EndAsync().ConfigureAwait(false);
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

        (HttpStatusCode status, JsonObject? body, string? problem, string? opened) = await PostAsync(message, cancellationToken).ConfigureAwait(false);
        if (status != HttpStatusCode.OK || body is null)
        {
            return Error(id, -32603, Refused(status, problem));
        }

        session = opened;
        version = body["result"]?["protocolVersion"]?.GetValue<string>();
        return body;
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

            return await ForwardAsync(message, id, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is HttpRequestException or IOException or TimeoutException or InvalidOperationException or JsonException)
        {
            return Error(id, -32603, "The runtime could not be reached: " + (ClientFailure.FromException(exception)?.Message ?? exception.Message));
        }
    }

    /// <summary>A write in two steps: prepared, checked, signed by this device, repeated with the plan; any other call once.</summary>
    private async Task<JsonObject> CallAsync(JsonObject message, JsonNode id, CancellationToken cancellationToken)
    {
        JsonObject parameters = message["params"] as JsonObject ?? [];
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
        if (first["result"] is not JsonObject result || result["isError"]?.GetValue<bool>() == true
            || result["_meta"]?["vericairn/plan"]?["prepared"] is not JsonObject prepared)
        {
            // A read, an active progress note or a refusal: the runtime prepared nothing, and this is the call's answer.
            return first;
        }

        string tool = parameters["name"]?.GetValue<string>() ?? string.Empty;
        if (Unsignable(prepared, tool) is string refusal)
        {
            log?.WriteLine("vericairn mcp: " + refusal);
            return Result(id, refusal, isError: true);
        }

        string hash = prepared["hash"]!.GetValue<string>();
        string signature = Base64Url.EncodeToString(sign(RecordSigning.Digest(hash)));
        JsonObject signing = (JsonObject)message.DeepClone();
        JsonObject signingParameters = signing["params"] as JsonObject ?? [];
        signing["params"] = signingParameters;
        JsonObject signed = (JsonObject)ownMeta.DeepClone();
        signed.Remove("vericairn/prepare");
        signed["vericairn/plan"] = prepared["plan"]!.GetValue<string>();
        signed["vericairn/signature"] = signature;
        signingParameters["_meta"] = signed;
        return await ForwardAsync(signing, id, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Why this device does not sign a prepared record, or null: its content must hash to the hash the runtime asks to be signed, name
    /// this device's principal as author and carry no signature, and be the record type its tool writes.
    /// </summary>
    private string? Unsignable(JsonObject prepared, string tool)
    {
        if (prepared["hash"] is not JsonValue hashValue || !hashValue.TryGetValue(out string? hash) || prepared["plan"] is not JsonValue
            || prepared["record"] is not JsonObject record)
        {
            return "The runtime's prepared record is incomplete; nothing was signed or written.";
        }

        var findings = new FindingList();
        RecordEnvelope? envelope = RecordParser.Parse(Encoding.UTF8.GetBytes(record.ToJsonString()), "prepared", findings, checkFormatting: false);
        if (envelope is null || envelope.ComputedHash != hash || envelope.Signatures.Count > 0)
        {
            return "The runtime asked this device to sign a hash its prepared record does not have; nothing was signed or written.";
        }

        if (!string.Equals(envelope.By, principal, StringComparison.Ordinal))
        {
            return $"The prepared record names {envelope.By} as its author, not this device's principal {principal}; nothing was signed or written.";
        }

        return Writes.TryGetValue(tool, out string? type) && envelope.Type != type
            ? $"{tool} writes a {type} record, and the runtime prepared a {envelope.Type}; nothing was signed or written."
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
            version = body["result"]?["protocolVersion"]?.GetValue<string>();
            log?.WriteLine("vericairn mcp: the runtime ended the session; a new one is open");
            await PostAsync(new JsonObject { ["jsonrpc"] = "2.0", ["method"] = "notifications/initialized" }, cancellationToken).ConfigureAwait(false);
            return session is not null;
        }
        finally
        {
            opening.Release();
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
        if (session is not null && message["method"]?.GetValue<string>() != "initialize")
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
                problem = body?["message"]?.GetValue<string>() ?? body?["error"]?["message"]?.GetValue<string>();
            }
            catch (JsonException)
            {
                problem = text.Length > 300 ? text[..300] : text;
            }
        }

        string? opened = response.Headers.TryGetValues(SessionHeader, out IEnumerable<string>? values) ? values.FirstOrDefault() : null;
        return (response.StatusCode, response.StatusCode == HttpStatusCode.OK ? body : null, problem, opened);
    }

    private static string Refused(HttpStatusCode status, string? problem) =>
        $"The runtime answered {(int)status} {status}{(problem is null ? "." : ": " + problem)}";

    private static JsonObject Result(JsonNode id, string text, bool isError) => new()
    {
        ["jsonrpc"] = "2.0", ["id"] = id,
        ["result"] = new JsonObject { ["content"] = new JsonArray(new JsonObject { ["type"] = "text", ["text"] = text }), ["isError"] = isError },
    };

    private static JsonObject Error(JsonNode? id, int code, string message) => new() { ["jsonrpc"] = "2.0", ["id"] = id, ["error"] = new JsonObject { ["code"] = code, ["message"] = message } };
}
