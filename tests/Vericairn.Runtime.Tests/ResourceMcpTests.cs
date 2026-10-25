// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Vericairn.Mcp;
using Vericairn.Protocol;
using Vericairn.Records.Canonical;
using Vericairn.Records.Hashing;
using Vericairn.Records.Model;
using Vericairn.Records.Tests;
using Vericairn.Runtime.Service;

namespace Vericairn.Runtime.Tests;

public sealed class ResourceMcpTests
{
    [Theory]
    [InlineData("2025-06-18", false)]
    [InlineData("2025-03-26", true)]
    [InlineData("2024-11-05", true)]
    public async Task ProtocolListsAllFourKindsAndDeclaresNoNotificationsOrSubscriptions(string version, bool asynchronous)
    {
        using var estate = new TestEstate(recordFormat: 2);
        RecordEnvelope order = estate.Add("work-order", estate.OperatorId, HandoffTests.Order);
        ContextTests.AddDocument(estate, "docs/original.dat", [0, 255, 13, 10]);
        using EstateService api = EstateService.Open(estate.Root);
        JsonObject[] replies = await DriveAsync(api, null, asynchronous,
            Request("initialize", new() { ["protocolVersion"] = version }),
            Request("resources/list"), Request("resources/templates/list"), Request("prompts/list"),
            new() { ["jsonrpc"] = "2.0", ["method"] = "notifications/initialized" });
        Assert.Equal(4, replies.Length);
        JsonNode initialized = Result(replies[0]);
        Assert.Equal(version, initialized["protocolVersion"]!.GetValue<string>());
        JsonNode capabilities = initialized["capabilities"]!;
        Assert.False(capabilities["resources"]!["subscribe"]!.GetValue<bool>());
        foreach (string name in new[] { "resources", "prompts", "tools" }) Assert.False(capabilities[name]!["listChanged"]!.GetValue<bool>());
        Assert.Contains("untrusted", initialized["instructions"]!.GetValue<string>(), StringComparison.Ordinal);
        JsonArray resources = Result(replies[1])["resources"]!.AsArray();
        foreach (string kind in new[] { "brief", "record", "document", "context" }) Assert.Contains(resources, r => r!["uri"]!.GetValue<string>().Contains("/" + kind + "/", StringComparison.Ordinal));
        Assert.Equal(estate.Load().Records.Count, resources.Count(r => r!["uri"]!.GetValue<string>().Contains("/record/", StringComparison.Ordinal)));
        Assert.Contains(resources, r => r!["uri"]!.GetValue<string>() == UriFor(estate, "brief", order.Id));
        Assert.DoesNotContain(resources, r => r!["uri"]!.GetValue<string>().Contains("operator", StringComparison.Ordinal));
        Assert.Null(Result(replies[1])["nextCursor"]);
        Assert.Equal(4, Result(replies[2])["resourceTemplates"]!.AsArray().Count);
        Assert.Null(Result(replies[2])["nextCursor"]);
    }

    [Fact]
    public async Task RoundTripsExactBytesHashesAndReadToolRepresentationsWithoutWriting()
    {
        using var estate = new TestEstate(recordFormat: 2);
        JsonObject body = JsonNode.Parse(HandoffTests.Order)!.AsObject();
        body["intent"] = "Read café, 漢字 and quoted \"originals\".\r\nKeep <&> and line endings.";
        RecordEnvelope order = estate.Add("work-order", estate.OperatorId, body.ToJsonString());
        byte[] original = [0, 255, 254, 13, 10, 128, 65, 195, 169];
        ContextTests.AddDocument(estate, "docs/original.dat", original);
        using EstateService api = EstateService.Open(estate.Root);
        var registry = new ToolRegistry(api, null);
        api.CommunicationClock = new ClientClock(estate.Clock);
        string[] Snapshot() => Directory.GetFiles(estate.Root, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal)
            .Select(p => Path.GetRelativePath(estate.Root, p) + ":" + ContentHash.Sha256(File.ReadAllBytes(p))).ToArray();
        string[] before = Snapshot();
        JsonObject listing = await registry.ResourcesListAsync(TestContext.Current.CancellationToken);
        string documentUri = listing["resources"]!.AsArray().Single(r => r!["name"]!.GetValue<string>() == "docs/original.dat")!["uri"]!.GetValue<string>();
        string pin = documentUri[(documentUri.LastIndexOf('/') + 1)..];
        ContextPackage package = api.Context(order.Id).Package!;
        ToolCallResult brief = await registry.CallAsync("work_brief", new() { ["workOrder"] = order.Id }, TestContext.Current.CancellationToken);
        ToolCallResult record = await registry.CallAsync("record_get", new() { ["id"] = order.Id }, TestContext.Current.CancellationToken);
        ToolCallResult context = await registry.CallAsync("context_prepare", new() { ["workOrder"] = order.Id }, TestContext.Current.CancellationToken);
        ToolCallResult document = await registry.CallAsync("context_read", new() { ["pin"] = pin }, TestContext.Current.CancellationToken);
        Assert.False(brief.IsError); Assert.False(record.IsError); Assert.False(context.IsError); Assert.False(document.IsError);
        RecordResponse envelope = JsonSerializer.Deserialize<RecordResponse>(record.Text, ProtocolJson.Options)!;
        var expected = new Dictionary<string, byte[]>
        {
            [UriFor(estate, "brief", order.Id)] = Encoding.UTF8.GetBytes(brief.Text),
            [UriFor(estate, "record", order.Id)] = CanonicalJson.SerializePrettyUtf8(envelope.Envelope),
            [documentUri] = JsonSerializer.Deserialize<ContextReadResponse>(document.Text, ProtocolJson.Options)!.Content!,
            [UriFor(estate, "context", order.Id)] = CanonicalJson.SerializePrettyUtf8(package.Payload),
            [UriFor(estate, "context", order.Id) + "?form=text"] = Encoding.UTF8.GetBytes(context.Text),
            [UriFor(estate, "context", order.Id) + "?form=json"] = JsonSerializer.SerializeToUtf8Bytes(package, ProtocolJson.Options),
        };
        JsonObject[] replies = await DriveAsync(api, null, true, expected.Keys.Select(uri => Request("resources/read", new() { ["uri"] = uri })).ToArray());
        int index = 0;
        foreach ((string uri, byte[] bytes) in expected)
        {
            JsonNode content = Assert.Single(Result(replies[index++])["contents"]!.AsArray())!;
            Assert.Equal(uri, content["uri"]!.GetValue<string>());
            AssertBytes(content, bytes);
        }
        AssertBytes(Assert.Single((await registry.ResourceReadAsync(documentUri, TestContext.Current.CancellationToken))["contents"]!.AsArray())!, original);
        JsonNode payload = Result(replies[3])["contents"]![0]!;
        Assert.Equal(package.Hash, payload["_meta"]!["vericairn/hash"]!.GetValue<string>());
        Assert.Equal(package.Bytes, payload["_meta"]!["vericairn/bytes"]!.GetValue<long>());
        Assert.Equal(order.ComputedHash, Result(replies[1])["contents"]![0]!["_meta"]!["vericairn/sourceHash"]!.GetValue<string>());
        AssertBytes(Result(replies[1])["contents"]![0]!, File.ReadAllBytes(Path.Combine(estate.Root, order.ExpectedPath)));
        Assert.Equal(before, Snapshot());
        Assert.Empty(api.Objects());

        File.WriteAllText(Path.Combine(estate.Root, "docs/original.dat"), "A different revision.");
        JsonObject refused = Assert.Single(await DriveAsync(api, null, false, Request("resources/read", new() { ["uri"] = documentUri })));
        Assert.Equal(-32002, refused["error"]!["code"]!.GetValue<int>());
        Assert.Null(refused["result"]);
        Assert.Null(api.ContextRead(pin).Content);
    }

    [Fact]
    public async Task ListedAssignmentBriefUsesTheSameCommunicationScopeAndAllocationAsTheTool()
    {
        using var writer = new PromptWriter();
        AssignRequest request = writer.Assignment();
        writer.Api.CommunicationClock = new ClientClock(writer.Fixture.Estate.Clock);
        RecordResponse assignment = writer.Api.Records("assignment").Single();
        Assert.True(writer.Api.CommunicationSet(new(writer.Operator, "person", new(Handoff: "short"), writer.Operator)).Ok);
        var registry = new ToolRegistry(writer.Api, writer.Builder);
        JsonArray entries = (await registry.ResourcesListAsync(TestContext.Current.CancellationToken))["resources"]!.AsArray();
        string uri = entries.Single(r => r!["uri"]!.GetValue<string>().EndsWith("?assignment=" + assignment.Id, StringComparison.Ordinal))!["uri"]!.GetValue<string>();
        uri += "&recipient=" + writer.Operator + "&maxBytes=500000";
        JsonNode content = Assert.Single((await registry.ResourceReadAsync(uri, TestContext.Current.CancellationToken))["contents"]!.AsArray())!;
        ToolCallResult tool = await registry.CallAsync("work_brief", new() { ["workOrder"] = request.WorkOrder, ["assignment"] = assignment.Id, ["recipient"] = writer.Operator, ["maxBytes"] = "500000" }, TestContext.Current.CancellationToken);
        Assert.False(tool.IsError);
        AssertBytes(content, Encoding.UTF8.GetBytes(tool.Text));
        Assert.Contains("handoff (short)", tool.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RevocationRechecksPreviouslyListedResourcesAndPromptReadAccess()
    {
        using var writer = new PromptWriter();
        writer.Add();
        AssignRequest assignment = writer.Assignment();
        ContextTests.AddDocument(writer.Fixture.Estate, "docs/pin.md", Encoding.UTF8.GetBytes("Original."));
        var registry = new ToolRegistry(writer.Api, writer.Builder);
        string[] uris = (await registry.ResourcesListAsync(TestContext.Current.CancellationToken))["resources"]!.AsArray().Select(r => r!["uri"]!.GetValue<string>()).ToArray();
        DeviceRow device = writer.Api.Devices().Single(d => d.Principal == writer.Builder && d.Name == "Prompt fixture writer");
        DeviceStanding standing = writer.Api.Standing(device.Id)!;
        string documentUri = uris.Single(u => u.Contains("/document/", StringComparison.Ordinal));
        string pin = documentUri[(documentUri.LastIndexOf('/') + 1)..];
        Assert.True(writer.Api.DeviceRevoke(new(device.Id, writer.Operator, "Withdraw the fixture reader.")).Ok);
        using (writer.Api.ForDeviceRequest(standing.Identity, standing.PublicKey))
        {
            foreach (string uri in uris)
            {
                JsonObject reply = Assert.Single(await DriveAsync(writer.Api, writer.Builder, false, Request("resources/read", new() { ["uri"] = uri })));
                Assert.Equal(-32002, reply["error"]!["code"]!.GetValue<int>());
                Assert.Null(reply["result"]);
                Assert.Equal("Completeness cannot be confirmed.", reply["error"]!["message"]!.GetValue<string>());
            }
            foreach (string method in new[] { "resources/list", "prompts/list" })
            {
                JsonObject reply = Assert.Single(await DriveAsync(writer.Api, writer.Builder, false, Request(method)));
                Assert.NotNull(reply["error"]);
                Assert.Null(reply["result"]);
            }
            foreach ((string tool, JsonObject arguments) in new[]
            {
                ("work_brief", new JsonObject { ["workOrder"] = assignment.WorkOrder }),
                ("context_prepare", new JsonObject { ["workOrder"] = assignment.WorkOrder }),
                ("context_read", new JsonObject { ["pin"] = pin }),
                ("prompts_list", new JsonObject()),
            }) Assert.True((await registry.CallAsync(tool, arguments, TestContext.Current.CancellationToken)).IsError);
            ResourcesResponse listing = writer.Api.Resources();
            Assert.Null(listing.Estate);
            Assert.Empty(listing.Resources);
        }
    }

    [Theory]
    [InlineData("resources/read", "{}", -32602)]
    [InlineData("resources/list", "[]", -32602)]
    [InlineData("resources/templates/list", "42", -32602)]
    [InlineData("resources/read", "[]", -32602)]
    [InlineData("prompts/get", "{\"name\":\"\"}", -32602)]
    [InlineData("resources/read", "{\"uri\":42}", -32602)]
    [InlineData("resources/read", "{\"uri\":\"file:///original\"}", -32602)]
    [InlineData("resources/read", "{\"uri\":\"vericairn://estate/record/%zz\"}", -32602)]
    [InlineData("resources/read", "{\"uri\":\"vericairn://estate/context/work?maxBytes=0\"}", -32602)]
    [InlineData("resources/read", "{\"uri\":\"vericairn://estate/context/work?maxBytes=5&maxBytes=6\"}", -32602)]
    [InlineData("resources/read", "{\"uri\":\"vericairn://estate/record/id?assignment=other\"}", -32602)]
    [InlineData("resources/read", "{\"uri\":\"vericairn://estate/operator/queue\"}", -32602)]
    [InlineData("resources/list", "{\"cursor\":\"unknown\"}", -32602)]
    [InlineData("resources/templates/list", "{\"cursor\":\"unknown\"}", -32602)]
    [InlineData("prompts/list", "{\"cursor\":\"unknown\"}", -32602)]
    [InlineData("prompts/get", "{\"name\":\"unknown\",\"arguments\":[]}", -32602)]
    [InlineData("resources/subscribe", "{}", -32601)]
    [InlineData("resources/unsubscribe", "{}", -32601)]
    public async Task InvalidRequestsReturnProtocolErrorsWithoutContent(string method, string parameters, int code)
    {
        using var estate = new TestEstate(recordFormat: 2);
        using EstateService api = EstateService.Open(estate.Root);
        JsonObject request = Request(method);
        request["params"] = JsonNode.Parse(parameters);
        JsonObject reply = Assert.Single(await DriveAsync(api, null, false, request));
        Assert.Equal(code, reply["error"]!["code"]!.GetValue<int>());
        Assert.Null(reply["result"]);
    }

    [Fact]
    public async Task UnknownAndForeignEstateResourcesAreUnavailableAndOldRecordsRemainReadable()
    {
        using var estate = new TestEstate(recordFormat: 2);
        RecordEnvelope order = estate.Add("work-order", estate.OperatorId, HandoffTests.Order);
        RecordEnvelope latest = estate.Add("work-order", estate.OperatorId, HandoffTests.Order, supersedes: order.Id);
        using EstateService api = EstateService.Open(estate.Root);
        var registry = new ToolRegistry(api, null);
        JsonArray listed = (await registry.ResourcesListAsync(TestContext.Current.CancellationToken))["resources"]!.AsArray();
        Assert.DoesNotContain(listed, r => r!["uri"]!.GetValue<string>() == UriFor(estate, "record", order.Id));
        Assert.Contains(listed, r => r!["uri"]!.GetValue<string>() == UriFor(estate, "record", latest.Id));
        JsonNode content = Assert.Single((await registry.ResourceReadAsync(UriFor(estate, "record", order.Id), TestContext.Current.CancellationToken))["contents"]!.AsArray())!;
        AssertBytes(content, CanonicalJson.SerializePrettyUtf8(api.Record(order.Id)!.Envelope));
        foreach (string uri in new[] { UriFor(estate, "record", "unknown"), "vericairn://foreign/record/" + order.Id })
        {
            JsonObject reply = Assert.Single(await DriveAsync(api, null, false, Request("resources/read", new() { ["uri"] = uri })));
            Assert.Equal(-32002, reply["error"]!["code"]!.GetValue<int>());
            Assert.Null(reply["result"]);
        }
    }

    internal static string UriFor(TestEstate estate, string kind, string key) => "vericairn://" + estate.Estate.Id + "/" + kind + "/" + Uri.EscapeDataString(key);
    internal static JsonObject Request(string method, JsonObject? parameters = null) => new() { ["jsonrpc"] = "2.0", ["method"] = method, ["params"] = parameters };
    internal static JsonNode Result(JsonObject reply) { Assert.Null(reply["error"]); return reply["result"]!; }
    internal static void AssertBytes(JsonNode content, byte[] expected)
    {
        byte[] actual = Convert.FromBase64String(content["blob"]!.GetValue<string>());
        Assert.Equal(expected, actual);
        Assert.Equal(ContentHash.Sha256(actual), content["_meta"]!["vericairn/hash"]!.GetValue<string>());
        Assert.Equal(actual.LongLength, content["_meta"]!["vericairn/bytes"]!.GetValue<long>());
        Assert.Contains("Untrusted", content["_meta"]!["vericairn/trust"]!.GetValue<string>(), StringComparison.Ordinal);
    }

    internal static async Task<JsonObject[]> DriveAsync(IEstateApi api, string? principal, bool asynchronous, params JsonObject[] requests)
    {
        int id = 0;
        foreach (JsonObject request in requests) if (!request["method"]!.GetValue<string>().StartsWith("notifications/", StringComparison.Ordinal)) request["id"] = ++id;
        using var input = new StringReader(string.Join('\n', requests.Select(r => r.ToJsonString())) + "\n");
        using var output = new StringWriter(CultureInfo.InvariantCulture);
        var server = new McpServer(new ToolRegistry(api, principal), "test");
        if (asynchronous) await server.RunAsync(input, output, cancellationToken: TestContext.Current.CancellationToken);
        else server.Run(input, output);
        return output.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(s => JsonNode.Parse(s)!.AsObject()).OrderBy(r => r["id"]!.GetValue<int>()).ToArray();
    }
}
