// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Net;
using System.Text.Json.Nodes;
using Vericairn.Mcp;
using Vericairn.Protocol;
using Vericairn.Records.Tests;
using Vericairn.Runtime.Service;
using Vericairn.Sdk;

namespace Vericairn.Runtime.Tests;

public sealed class McpStatelessTests
{
    private const string Modern = "2026-07-28";
    private const string Legacy = "2025-11-25";
    private const string ProtocolVersion = "io.modelcontextprotocol/protocolVersion";
    private const string ClientCapabilities = "io.modelcontextprotocol/clientCapabilities";
    private const string ServerInfo = "io.modelcontextprotocol/serverInfo";
    private const string SubscriptionId = "io.modelcontextprotocol/subscriptionId";

    [Fact]
    public async Task BothRevisionsShareToolsResourcesPromptsAndTheReadOnlyScope()
    {
        using var estate = new TestEstate(recordFormat: 2);
        using EstateService api = EstateService.Open(estate.Root);
        var server = new McpServer(new ToolRegistry(api, null), "test");

        (string Method, JsonObject? Params)[] operations =
        [
            ("tools/list", null),
            ("tools/call", new() { ["name"] = "estate_health" }),
            ("prompts/list", null),
            ("resources/list", null),
            ("resources/templates/list", null),
        ];
        var requests = new List<JsonObject>
        {
            Request(1, "server/discover", Metadata()),
            Request(2, "initialize", new() { ["protocolVersion"] = Legacy }),
        };
        for (int index = 0; index < operations.Length; index++)
        {
            var (method, parameters) = operations[index];
            JsonObject modern = (JsonObject?)parameters?.DeepClone() ?? new();
            modern["_meta"] = Metadata()["_meta"]!.DeepClone();
            requests.Add(Request(index * 2 + 3, method, modern));
            requests.Add(Request(index * 2 + 4, method, parameters));
        }
        JsonObject read = Metadata();
        read["uri"] = $"vericairn://{estate.Estate.Id}/record/{new string('0', 26)}";
        requests.Add(Request(20, "resources/read", read));
        requests.Add(Request(21, "resources/read", new() { ["uri"] = read["uri"]!.DeepClone() }));
        JsonObject hidden = Metadata();
        hidden["name"] = "work_attempt";
        requests.Add(Request(22, "tools/call", hidden));

        using var input = new StringReader(string.Join('\n', requests.Select(request => request.ToJsonString())));
        using var output = new StringWriter();
        await server.RunAsync(input, output, cancellationToken: TestContext.Current.CancellationToken);
        Dictionary<int, JsonObject> answers = Replies(output);
        JsonObject discover = Assert.IsType<JsonObject>(answers[1]["result"]);
        Assert.Contains(Modern, discover["supportedVersions"]!.AsArray().Select(version => version!.GetValue<string>()));
        Assert.Contains(Legacy, discover["supportedVersions"]!.AsArray().Select(version => version!.GetValue<string>()));
        Assert.Equal("vericairn", discover["_meta"]![ServerInfo]!["name"]!.GetValue<string>());
        Assert.Equal(Legacy, answers[2]["result"]!["protocolVersion"]!.GetValue<string>());
        Assert.Null(answers[2]["result"]!["resultType"]);

        for (int index = 0; index < operations.Length; index++)
        {
            JsonObject modern = answers[index * 2 + 3]["result"]!.AsObject();
            JsonNode legacy = answers[index * 2 + 4]["result"]!;
            Assert.Equal("complete", modern["resultType"]!.GetValue<string>());
            Assert.Equal("test", modern["_meta"]![ServerInfo]!["version"]!.GetValue<string>());
            if (operations[index].Method.EndsWith("/list", StringComparison.Ordinal))
            {
                Assert.Equal(0, modern["ttlMs"]!.GetValue<int>());
                Assert.Equal("private", modern["cacheScope"]!.GetValue<string>());
            }
            else
            {
                Assert.Null(modern["ttlMs"]);
            }
            modern["_meta"]!.AsObject().Remove(ServerInfo);
            if (modern["_meta"]!.AsObject().Count == 0) modern.Remove("_meta");
            modern.Remove("resultType");
            modern.Remove("ttlMs");
            modern.Remove("cacheScope");
            Assert.True(JsonNode.DeepEquals(legacy, modern), operations[index].Method);
        }

        Assert.Equal(-32602, answers[20]["error"]!["code"]!.GetValue<int>());
        Assert.Equal(-32002, answers[21]["error"]!["code"]!.GetValue<int>());
        Assert.Equal(-32602, answers[22]["error"]!["code"]!.GetValue<int>());
        Assert.Null(answers[22]["result"]);
    }

    [Fact]
    public void VersionAndCapabilitiesComeFromEachRequest()
    {
        using var estate = new TestEstate(recordFormat: 2);
        using EstateService api = EstateService.Open(estate.Root);
        var server = new McpServer(new ToolRegistry(api, null), "test");
        JsonObject missingCapabilities = Metadata();
        missingCapabilities["_meta"]!.AsObject().Remove(ClientCapabilities);
        JsonObject[] requests =
        [
            Request(1, "server/discover", Metadata()),
            Request(2, "tools/list", missingCapabilities),
            Request(3, "server/discover", Metadata("1900-01-01")),
            Request(4, "server/discover", null),
            Request(5, "ping", Metadata()),
            Request(6, "ping", null),
        ];
        using var input = new StringReader(string.Join('\n', requests.Select(request => request.ToJsonString())));
        using var output = new StringWriter();
        server.Run(input, output);
        Dictionary<int, JsonObject> answers = Replies(output);
        Assert.NotNull(answers[1]["result"]);
        Assert.Equal(-32602, answers[2]["error"]!["code"]!.GetValue<int>());
        Assert.Equal(-32022, answers[3]["error"]!["code"]!.GetValue<int>());
        Assert.Equal("1900-01-01", answers[3]["error"]!["data"]!["requested"]!.GetValue<string>());
        Assert.True(JsonNode.DeepEquals(answers[1]["result"]!["supportedVersions"], answers[3]["error"]!["data"]!["supported"]));
        Assert.Equal(-32602, answers[4]["error"]!["code"]!.GetValue<int>());
        Assert.Equal(-32601, answers[5]["error"]!["code"]!.GetValue<int>());
        Assert.NotNull(answers[6]["result"]);
    }

    [Theory]
    [InlineData("vericairn://estate/document/café")]
    [InlineData(" vericairn://estate/document/pin ")]
    public async Task BridgeSuppliesStatelessHeadersAndPreservesTheRpcsErrorWithoutNegotiating(string resourceUri)
    {
        using var upstream = new StatelessUpstream(resourceUri);
        using var http = new HttpClient(upstream) { BaseAddress = new Uri("https://runtime.invalid") };
        using var bridge = new McpBridge(http, "test-principal", _ => throw new InvalidOperationException("A read does not sign a record."));
        JsonObject parameters = Metadata();
        parameters["uri"] = resourceUri;
        using var input = new StringReader(Request(1, "resources/read", parameters).ToJsonString());
        using var output = new StringWriter();
        await bridge.RunAsync(input, output, TestContext.Current.CancellationToken);
        JsonObject answer = Replies(output)[1];
        Assert.Equal(-32602, answer["error"]!["code"]!.GetValue<int>());
        Assert.Equal("Missing resource", answer["error"]!["message"]!.GetValue<string>());
        Assert.Equal(1, upstream.Calls);
        Assert.Null(bridge.Session);
    }

    [Fact]
    public async Task StdioListenAcknowledgesBeforeListChangesAndCompletesOnCancellation()
    {
        using var estate = new TestEstate(recordFormat: 2);
        using EstateService api = EstateService.Open(estate.Root);
        var server = new McpServer(new ToolRegistry(api, null), "test")
        {
            ListenSubscriptions = () => new McpResourceWatch(new ListChangeSource()),
        };
        JsonObject parameters = Metadata();
        parameters["notifications"] = new JsonObject { ["resourcesListChanged"] = true, ["toolsListChanged"] = true };
        using var output = new ListenOutput();
        using var input = new ListenInput(Request(1, "subscriptions/listen", parameters).ToJsonString(), output.Observed);
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        stop.CancelAfter(TestTiming.HangTimeout);
        await server.RunAsync(input, output, cancellationToken: stop.Token);

        JsonObject[] lines = output.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(line => JsonNode.Parse(line)!.AsObject()).ToArray();
        Assert.Equal(3, lines.Length);
        Assert.Equal("notifications/subscriptions/acknowledged", lines[0]["method"]!.GetValue<string>());
        Assert.True(lines[0]["params"]!["notifications"]!["resourcesListChanged"]!.GetValue<bool>());
        Assert.Null(lines[0]["params"]!["notifications"]!["toolsListChanged"]);
        Assert.Equal(McpEvents.ListChangedMethod, lines[1]["method"]!.GetValue<string>());
        Assert.All(lines[..2], notification => Assert.Equal(1, notification["params"]!["_meta"]![SubscriptionId]!.GetValue<int>()));
        Assert.Equal("complete", lines[2]["result"]!["resultType"]!.GetValue<string>());
        Assert.Equal(1, lines[2]["result"]!["_meta"]![SubscriptionId]!.GetValue<int>());
    }

    private sealed class ListChangeSource : IMcpEventSource
    {
        private bool told;

        public Task<bool> AvailableAsync(CancellationToken stopping) => Task.FromResult(true);

        public Task AddAsync(string key, EventFilter filter, CancellationToken stopping) => Task.CompletedTask;

        public void Remove(string key) { }

        public async Task<McpEventRound> ReadAsync(TimeSpan wait, CancellationToken stopping)
        {
            if (told) await Task.Delay(Timeout.InfiniteTimeSpan, stopping);
            told = true;
            return new McpEventRound([], Reset: true);
        }
    }

    private sealed class ListenInput(string request, Task observed) : TextReader
    {
        private int lines;

        public override async ValueTask<string?> ReadLineAsync(CancellationToken cancellationToken)
        {
            switch (lines++)
            {
                case 0:
                    return request;
                case 1:
                    await observed.WaitAsync(cancellationToken);
                    return """{"jsonrpc":"2.0","method":"notifications/cancelled","params":{"requestId":1}}""";
                default:
                    return null;
            }
        }
    }

    private sealed class ListenOutput : StringWriter
    {
        private readonly TaskCompletionSource observed = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task Observed => observed.Task;

        public override async Task WriteLineAsync(string? value)
        {
            await base.WriteLineAsync(value);
            if (value?.Contains(McpEvents.ListChangedMethod, StringComparison.Ordinal) == true ||
                value?.Contains("\"error\"", StringComparison.Ordinal) == true)
                observed.TrySetResult();
        }
    }

    private sealed class StatelessUpstream(string resourceUri) : HttpMessageHandler
    {
        public int Calls { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.False(request.Headers.Contains("Mcp-Session-Id"));
            Assert.Equal(Modern, Assert.Single(request.Headers.GetValues("MCP-Protocol-Version")));
            Assert.Equal("resources/read", Assert.Single(request.Headers.GetValues("Mcp-Method")));
            string uri = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(resourceUri));
            Assert.Equal("=?base64?" + uri + "?=", Assert.Single(request.Headers.GetValues("Mcp-Name")));
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadRequest)
            {
                Content = new StringContent("""{"jsonrpc":"2.0","id":1,"error":{"code":-32602,"message":"Missing resource"}}"""),
            });
        }
    }

    private static Dictionary<int, JsonObject> Replies(StringWriter writer) => writer.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries)
        .Select(line => JsonNode.Parse(line)!.AsObject()).ToDictionary(reply => reply["id"]!.GetValue<int>());

    private static JsonObject Metadata(string version = Modern) => new()
    {
        ["_meta"] = new JsonObject { [ProtocolVersion] = version, [ClientCapabilities] = new JsonObject() },
    };

    private static JsonObject Request(int id, string method, JsonObject? parameters)
    {
        var request = new JsonObject { ["jsonrpc"] = "2.0", ["id"] = id, ["method"] = method };
        if (parameters is not null) request["params"] = parameters;
        return request;
    }
}
