// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Buffers.Text;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Channels;
using Vericairn.Protocol;
using Vericairn.Records.Signing;
using Vericairn.Records.Tests;
using Vericairn.Sdk;

namespace Vericairn.Daemon.Tests;

/// <summary>
/// A network MCP client for tests (MCP-03): JSON-RPC over Streamable HTTP to /mcp, every request signed by a device through the
/// SDK's signed transport, the session and protocol-version headers kept as a client keeps them.
/// </summary>
internal sealed class McpWire(HttpClient http) : IDisposable
{
    private int next;

    public string? Session { get; set; }

    public string? Version { get; set; }

    public HttpClient Http => http;

    public static JsonObject Request(string method, JsonObject? parameters = null, JsonNode? id = null) => new()
    {
        ["jsonrpc"] = "2.0", ["id"] = id ?? JsonValue.Create("t-" + Guid.NewGuid().ToString("N")[..8]), ["method"] = method, ["params"] = parameters ?? new JsonObject(),
    };

    public async Task<(HttpStatusCode Status, JsonObject? Body, string Text, string? Session)> PostAsync(JsonObject message, IReadOnlyDictionary<string, string?>? headers = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri("mcp", UriKind.Relative))
        {
            Content = new StringContent(message.ToJsonString(), new UTF8Encoding(false), "application/json"),
        };
        request.Headers.TryAddWithoutValidation("Accept", "application/json, text/event-stream");
        var all = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase) { ["Mcp-Session-Id"] = Session, ["MCP-Protocol-Version"] = Version };
        foreach ((string name, string? value) in headers ?? new Dictionary<string, string?>())
        {
            all[name] = value;
        }

        foreach ((string name, string? value) in all)
        {
            if (value is not null)
            {
                request.Headers.TryAddWithoutValidation(name, value);
            }
        }

        using HttpResponseMessage response = await http.SendAsync(request, TestContext.Current.CancellationToken);
        string text = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        JsonObject? body = text.Length > 0 && text.TrimStart().StartsWith('{') ? JsonNode.Parse(text) as JsonObject : null;
        string? session = response.Headers.TryGetValues("Mcp-Session-Id", out IEnumerable<string>? values) ? values.Single() : null;
        return (response.StatusCode, body, text, session);
    }

    public async Task<JsonObject> InitializeAsync(string version = "2025-06-18")
    {
        Session = null;
        Version = null;
        (HttpStatusCode status, JsonObject? body, string text, string? session) = await PostAsync(Request("initialize", new JsonObject
        {
            ["protocolVersion"] = version, ["capabilities"] = new JsonObject(), ["clientInfo"] = new JsonObject { ["name"] = "test", ["version"] = "0" },
        }));
        Assert.True(status == HttpStatusCode.OK, $"{(int)status}: {text}");
        Session = session ?? throw new InvalidOperationException("initialize opened no session");
        Version = body!["result"]!["protocolVersion"]!.GetValue<string>();
        (HttpStatusCode accepted, _, _, _) = await PostAsync(new JsonObject { ["jsonrpc"] = "2.0", ["method"] = "notifications/initialized" });
        Assert.Equal(HttpStatusCode.Accepted, accepted);
        return body;
    }

    /// <summary>A tools/call's result object; fails the test on a transport or JSON-RPC error.</summary>
    public async Task<JsonObject> CallAsync(string tool, JsonObject? arguments = null, JsonObject? meta = null)
    {
        var parameters = new JsonObject { ["name"] = tool, ["arguments"] = arguments ?? new JsonObject() };
        if (meta is not null)
        {
            parameters["_meta"] = meta;
        }

        (HttpStatusCode status, JsonObject? body, string text, _) = await PostAsync(Request("tools/call", parameters, JsonValue.Create(Interlocked.Increment(ref next))));
        Assert.True(status == HttpStatusCode.OK, $"{(int)status}: {text}");
        return body!["result"] as JsonObject ?? throw new InvalidOperationException("No result: " + text);
    }

    /// <summary>A write in its two steps: prepared, then signed by the device and repeated with the plan.</summary>
    public async Task<JsonObject> WriteAsync(string tool, JsonObject arguments, IRequestSigner signer)
    {
        JsonObject prepared = await CallAsync(tool, (JsonObject)arguments.DeepClone(), new JsonObject { ["vericairn/prepare"] = true });
        Assert.False(prepared["isError"]!.GetValue<bool>(), Text(prepared));
        JsonObject plan = prepared["_meta"]!["vericairn/plan"]!["prepared"]!.AsObject();
        return await CallAsync(tool, (JsonObject)arguments.DeepClone(), new JsonObject
        {
            ["vericairn/plan"] = plan["plan"]!.GetValue<string>(), ["vericairn/signature"] = Sign(signer, plan["hash"]!.GetValue<string>()),
        });
    }

    /// <summary>Opens the session's server stream; the response stays open until the caller disposes it.</summary>
    public async Task<HttpResponseMessage> OpenStreamAsync(string accept = "text/event-stream", string? session = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri("mcp", UriKind.Relative));
        request.Headers.TryAddWithoutValidation("Accept", accept);
        if ((session ?? Session) is string named) request.Headers.TryAddWithoutValidation("Mcp-Session-Id", named);
        if (Version is not null) request.Headers.TryAddWithoutValidation("MCP-Protocol-Version", Version);
        return await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, TestContext.Current.CancellationToken);
    }

    /// <summary>Ends the session with DELETE.</summary>
    public async Task<HttpStatusCode> DeleteAsync(string? session = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Delete, new Uri("mcp", UriKind.Relative));
        if ((session ?? Session) is string named) request.Headers.TryAddWithoutValidation("Mcp-Session-Id", named);
        using HttpResponseMessage response = await http.SendAsync(request, TestContext.Current.CancellationToken);
        return response.StatusCode;
    }

    /// <summary>Reads a server stream until a line passes the test or the stream ends; null when it ended first.</summary>
    public static async Task<string?> ReadUntilAsync(StreamReader stream, Func<string, bool> wanted)
    {
        using var guard = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        guard.CancelAfter(TestTiming.HangTimeout);
        while (await stream.ReadLineAsync(guard.Token) is string line)
        {
            if (wanted(line))
            {
                return line;
            }
        }

        return null;
    }

    public static string Sign(IRequestSigner signer, string hash) => Base64Url.EncodeToString(signer.Sign(RecordSigning.Digest(hash)));

    public static string Text(JsonObject result) => result["content"]![0]!["text"]!.GetValue<string>();

    public static WriteOutcome Outcome(JsonObject result) => JsonSerializer.Deserialize<WriteOutcome>(Text(result), ProtocolJson.Options)!;

    public void Dispose() => http.Dispose();
}

/// <summary>Drives a bridge's standard streams line by line, as a local MCP client would.</summary>
internal sealed class BridgeDriver : IAsyncDisposable
{
    private readonly Channel<string?> input = Channel.CreateUnbounded<string?>();
    private readonly Channel<string> output = Channel.CreateUnbounded<string>();
    private readonly List<JsonObject> seen = [];
    private readonly Task running;

    public BridgeDriver(McpBridge bridge)
    {
        running = Task.Run(() => bridge.RunAsync(new ChannelReader(input), new ChannelWriter(output), TestContext.Current.CancellationToken));
    }

    public async Task<JsonObject> RequestAsync(JsonObject message)
    {
        string id = message["id"]!.ToJsonString();
        await input.Writer.WriteAsync(message.ToJsonString(), TestContext.Current.CancellationToken);
        while (true)
        {
            if (seen.FirstOrDefault(s => s["id"]?.ToJsonString() == id) is JsonObject answered)
            {
                seen.Remove(answered);
                return answered;
            }

            string line = await output.Reader.ReadAsync(TestContext.Current.CancellationToken).AsTask().WaitAsync(TestTiming.HangTimeout, TestContext.Current.CancellationToken);
            seen.Add(JsonNode.Parse(line)!.AsObject());
        }
    }

    public ValueTask SendAsync(JsonObject message) => input.Writer.WriteAsync(message.ToJsonString(), TestContext.Current.CancellationToken);

    public async ValueTask DisposeAsync()
    {
        input.Writer.TryComplete();
        await running.WaitAsync(TestTiming.HangTimeout, TestContext.Current.CancellationToken);
    }

    private sealed class ChannelReader(Channel<string?> lines) : TextReader
    {
        public override async ValueTask<string?> ReadLineAsync(CancellationToken cancellationToken)
        {
            while (await lines.Reader.WaitToReadAsync(cancellationToken))
            {
                if (lines.Reader.TryRead(out string? line))
                {
                    return line;
                }
            }

            return null;
        }
    }

    private sealed class ChannelWriter(Channel<string> lines) : TextWriter
    {
        public override Encoding Encoding => Encoding.UTF8;

        public override void Write(char value) => throw new NotSupportedException();

        public override Task WriteLineAsync(string? value)
        {
            lines.Writer.TryWrite(value ?? string.Empty);
            return Task.CompletedTask;
        }

        public override Task FlushAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}

/// <summary>An estate whose runtime serves network MCP over the local-network profile, with a work order assigned to its agent.</summary>
internal static class NetworkMcp
{
    public const string WorkOrder = """{"key":"W-1","kind":"ordinary","title":"Build the widget","intent":"Network MCP.","scope":["widget"],"exitQuestion":"Does it build?","inputs":[],"deliverables":["widget"]}""";

    public const string Brief = """{"outcome":"Widget","sources":[],"permittedChanges":["src/"],"prohibitedEffects":[],"workspace":["widget repository"],"limits":[],"expectedOutputs":["widget"],"reviewCriteria":["tests"],"recovery":"Record a result."}""";

    public static async Task<(LocalNetworkHarness Harness, string Assignment)> StartAsync(bool mcp = true, IReadOnlyList<string>? origins = null)
    {
        LocalNetworkHarness harness = await LocalNetworkHarness.StartAsync(mcp: mcp, mcpOrigins: origins);
        TestEstate estate = harness.Estate;
        estate.Add("work-order", estate.OperatorId, WorkOrder);
        using JsonDocument brief = JsonDocument.Parse(Brief);
        WriteOutcome assigned = harness.Socket.Assign(new AssignRequest("W-1", estate.BuilderId, estate.OperatorId, brief.RootElement));
        Assert.True(assigned.Ok, assigned.Problem?.Message);
        return (harness, assigned.Written!.Id);
    }

    /// <summary>A client of /mcp for a device paired from the second machine.</summary>
    public static McpWire Wire(LocalNetworkHarness harness, RuntimeConnection connection) =>
        new(VericairnClient.SignedHttp(connection, harness.Signer(connection)));

    public static JsonObject Args(params (string Name, JsonNode? Value)[] values)
    {
        var arguments = new JsonObject();
        foreach ((string name, JsonNode? value) in values)
        {
            arguments[name] = value;
        }

        return arguments;
    }

    public static JsonObject ResultBody(string summary = "The widget builds.") => JsonNode.Parse($$"""{"outcome":"completed","summary":"{{summary}}","outputs":[],"evidence":[],"checks":[],"limitations":[],"unresolvedEffects":[],"nextStep":"Review it."}""")!.AsObject();
}

/// <summary>A clock a test moves by assignment, for prepared records' lapse.</summary>
internal sealed class MovableClock(DateTimeOffset now) : TimeProvider
{
    public DateTimeOffset Now { get; set; } = now;

    public override DateTimeOffset GetUtcNow() => Now;
}
