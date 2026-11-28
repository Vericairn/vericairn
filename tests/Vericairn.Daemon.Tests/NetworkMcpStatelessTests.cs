// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Buffers.Text;
using System.Net;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;
using Vericairn.Mcp;
using Vericairn.Protocol;
using Vericairn.Sdk;

namespace Vericairn.Daemon.Tests;

[Collection("daemon")]
public class NetworkMcpStatelessTests
{
    private const string Modern = "2026-07-28";
    private const string Legacy = "2025-11-25";
    private const string VersionKey = "io.modelcontextprotocol/protocolVersion";
    private const string CapabilitiesKey = "io.modelcontextprotocol/clientCapabilities";
    private const string SubscriptionKey = "io.modelcontextprotocol/subscriptionId";
    private const string Acknowledged = "notifications/subscriptions/acknowledged";
    private static readonly string[] RepeatedMethods = ["tools/list", "tools/list"];

    [Theory]
    [InlineData("MCP-Protocol-Version", null)]
    [InlineData("MCP-Protocol-Version", "2025-11-25")]
    [InlineData("Mcp-Method", null)]
    [InlineData("Mcp-Method", "TOOLS/CALL")]
    [InlineData("Mcp-Name", null)]
    [InlineData("Mcp-Name", "different")]
    [InlineData("Mcp-Name", "=?base64?invalid!?=")]
    public void RequiredHeadersMatchTheirSignedMessage(string header, string? other)
    {
        JsonObject message = Request("tools/call", new JsonObject { ["name"] = "example-é" });
        HttpRequest request = new DefaultHttpContext().Request;
        request.Headers["MCP-Protocol-Version"] = Modern;
        request.Headers["mcp-method"] = "tools/call";
        request.Headers["mcp-name"] = "=?base64?" + Convert.ToBase64String(Encoding.UTF8.GetBytes("example-é")) + "?=";
        Assert.True(McpStatelessHttp.Selected(request, "tools/call", message["params"]));
        Assert.Null(McpStatelessHttp.Validate(request, message, message["id"], "tools/call", message["params"]));

        request.Headers[header] = new StringValues(other);
        JsonObject? refused = McpStatelessHttp.Validate(request, message, message["id"], "tools/call", message["params"]);
        Assert.Equal(-32020, refused!["error"]!["code"]!.GetValue<int>());
        Assert.Contains(header, refused["error"]!["message"]!.GetValue<string>(), StringComparison.Ordinal);
    }

    [Fact]
    public void ExplicitVersionDoesNotInheritCapabilitiesOrAProtocolSession()
    {
        HttpRequest request = new DefaultHttpContext().Request;
        JsonObject message = Request("tools/list", version: "1900-01-01");
        request.Headers["MCP-Protocol-Version"] = "1900-01-01";
        request.Headers["Mcp-Method"] = "tools/list";
        request.Headers["Mcp-Session-Id"] = "session-from-an-older-request";
        JsonObject unsupported = McpStatelessHttp.Validate(request, message, message["id"], "tools/list", message["params"])!;
        Assert.Equal(-32022, unsupported["error"]!["code"]!.GetValue<int>());
        Assert.Equal("1900-01-01", unsupported["error"]!["data"]!["requested"]!.GetValue<string>());
        Assert.Contains(unsupported["error"]!["data"]!["supported"]!.AsArray(), version => version!.GetValue<string>() == Legacy);
        Assert.Contains(unsupported["error"]!["data"]!["supported"]!.AsArray(), version => version!.GetValue<string>() == Modern);

        message = Request("tools/list");
        request.Headers["MCP-Protocol-Version"] = Modern;
        request.Headers["Mcp-Method"] = new StringValues(RepeatedMethods);
        Assert.Equal(-32020, McpStatelessHttp.Validate(request, message, message["id"], "tools/list", message["params"])!["error"]!["code"]!.GetValue<int>());
        request.Headers["Mcp-Method"] = "tools/list";
        JsonObject meta = message["params"]!["_meta"]!.AsObject();
        Assert.True(meta.Remove(CapabilitiesKey));
        Assert.Equal(-32602, McpStatelessHttp.Validate(request, message, message["id"], "tools/list", message["params"])!["error"]!["code"]!.GetValue<int>());
        Assert.True(meta.Remove(VersionKey));
        Assert.Equal(-32602, McpStatelessHttp.Validate(request, message, message["id"], "tools/list", message["params"])!["error"]!["code"]!.GetValue<int>());
    }

    [Fact]
    public async Task BothRevisionsExposeTheSameToolsAndRequireTheSameDeviceAuthority()
    {
        (LocalNetworkHarness harness, string assignment) = await NetworkMcp.StartAsync();
        await using LocalNetworkHarness owned = harness;
        RuntimeConnection builder = harness.Pair(harness.Estate.BuilderId, "Agent laptop");
        using McpWire legacy = NetworkMcp.Wire(harness, builder);
        using McpWire modern = NetworkMcp.Wire(harness, builder);
        Assert.Equal(Legacy, (await legacy.InitializeAsync(Legacy))["result"]!["protocolVersion"]!.GetValue<string>());
        modern.Session = legacy.Session;

        JsonObject discovery = await ResultAsync(modern, "server/discover");
        Assert.Equal("complete", discovery["resultType"]!.GetValue<string>());
        Assert.Equal("private", discovery["cacheScope"]!.GetValue<string>());
        Assert.Equal(0, discovery["ttlMs"]!.GetValue<int>());
        Assert.Equal("vericairn", discovery["_meta"]!["io.modelcontextprotocol/serverInfo"]!["name"]!.GetValue<string>());
        Assert.True(discovery["capabilities"]!["resources"]!["subscribe"]!.GetValue<bool>());
        Assert.Equal([Modern, Legacy], discovery["supportedVersions"]!.AsArray().Take(2).Select(version => version!.GetValue<string>()));

        foreach ((string method, string collection) in new[] { ("tools/list", "tools"), ("resources/list", "resources"), ("resources/templates/list", "resourceTemplates"), ("prompts/list", "prompts") })
        {
            JsonObject before = (await legacy.PostAsync(McpWire.Request(method))).Body!["result"]!.AsObject();
            JsonObject after = await ResultAsync(modern, method);
            Assert.Null(before["resultType"]);
            Assert.Equal("complete", after["resultType"]!.GetValue<string>());
            Assert.Equal("private", after["cacheScope"]!.GetValue<string>());
            Assert.Equal(0, after["ttlMs"]!.GetValue<int>());
            string key = collection == "tools" || collection == "prompts" ? "name" : collection == "resources" ? "uri" : "uriTemplate";
            Assert.Equal(Sorted(before, collection, key), Sorted(after, collection, key));
        }

        string uri = $"vericairn://{harness.Host.Service.Session.Manifest.Estate}/record/{assignment}";
        JsonObject legacyRead = (await legacy.PostAsync(McpWire.Request("resources/read", new JsonObject { ["uri"] = uri }))).Body!["result"]!.AsObject();
        JsonObject modernRead = await ResultAsync(modern, "resources/read", new JsonObject { ["uri"] = uri });
        Assert.True(JsonNode.DeepEquals(legacyRead["contents"], modernRead["contents"]));
        Assert.Equal("private", modernRead["cacheScope"]!.GetValue<string>());
        Assert.Equal(0, modernRead["ttlMs"]!.GetValue<int>());

        Assert.False((await ResultAsync(modern, "tools/call", Tools("estate_health")))["isError"]!.GetValue<bool>());
        JsonObject hidden = (await modern.PostAsync(Request("tools/call", Tools("work_review")), Headers("tools/call", "work_review"))).Body!;
        Assert.Equal(-32602, hidden["error"]!["code"]!.GetValue<int>());
        JsonObject arguments = NetworkMcp.Args(("assignment", assignment), ("workspace", "widget repository"), ("note", "Build it."));
        int records = harness.Estate.Load().Records.Count;
        JsonObject unsigned = await ResultAsync(modern, "tools/call", Tools("work_attempt", arguments));
        Assert.True(unsigned["isError"]!.GetValue<bool>());
        Assert.Contains("two calls", McpWire.Text(unsigned), StringComparison.Ordinal);
        Assert.Equal(records, harness.Estate.Load().Records.Count);

        RuntimeConnection other = PairOther(harness);
        using McpWire stranger = NetworkMcp.Wire(harness, other);
        await stranger.InitializeAsync(Legacy);
        var prepare = new JsonObject { ["vericairn/prepare"] = true };
        int beforeForeignCalls = harness.Estate.Load().Records.Count;
        JsonObject legacyForeign = await stranger.CallAsync("work_attempt", (JsonObject)arguments.DeepClone(), (JsonObject)prepare.DeepClone());
        Assert.False(legacyForeign["isError"]!.GetValue<bool>(), McpWire.Text(legacyForeign));
        JsonObject legacyRefusal = await stranger.CallAsync("work_attempt", (JsonObject)arguments.DeepClone(), SignPlan(legacyForeign, harness.Signer(other)));
        Assert.True(legacyRefusal["isError"]!.GetValue<bool>());
        Assert.Contains(McpWire.Outcome(legacyRefusal).Problem!.Findings, finding => finding.Code == "attempt.contributor");
        Assert.Equal(HttpStatusCode.NoContent, await stranger.DeleteAsync());

        stranger.Session = legacy.Session;
        JsonObject modernForeign = await ResultAsync(stranger, "tools/call", Tools("work_attempt", arguments, prepare));
        Assert.False(modernForeign["isError"]!.GetValue<bool>(), McpWire.Text(modernForeign));
        JsonObject modernRefusal = await ResultAsync(stranger, "tools/call", Tools("work_attempt", arguments, SignPlan(modernForeign, harness.Signer(other))));
        Assert.True(modernRefusal["isError"]!.GetValue<bool>());
        Assert.Contains(McpWire.Outcome(modernRefusal).Problem!.Findings, finding => finding.Code == "attempt.contributor");
        Assert.Equal(beforeForeignCalls, harness.Estate.Load().Records.Count);

        JsonObject prepared = await ResultAsync(modern, "tools/call", Tools("work_attempt", arguments, prepare));
        JsonObject plan = prepared["_meta"]!["vericairn/plan"]!["prepared"]!.AsObject();
        Assert.Equal(harness.Estate.BuilderId, plan["record"]!["by"]!.GetValue<string>());
        JsonObject written = await ResultAsync(modern, "tools/call", Tools("work_attempt", arguments, SignPlan(prepared, harness.Signer(builder))));
        Assert.True(McpWire.Outcome(written).Ok, McpWire.Text(written));
        Assert.Equal(1, harness.Host.Mcp.Sessions.Count);
    }

    [Fact]
    public async Task ModernMethodAndHeadersAreCheckedBeforeDispatch()
    {
        (LocalNetworkHarness harness, string assignment) = await NetworkMcp.StartAsync();
        await using LocalNetworkHarness owned = harness;
        using McpWire client = NetworkMcp.Wire(harness, harness.Pair(harness.Estate.BuilderId, "Agent laptop"));
        int records = harness.Estate.Load().Records.Count;
        JsonObject request = Request("tools/call", Tools("work_attempt",
            NetworkMcp.Args(("assignment", assignment), ("workspace", "widget repository"), ("note", "Build it.")),
            new JsonObject { ["vericairn/prepare"] = true }));
        (HttpStatusCode status, JsonObject? body, _, _) = await client.PostAsync(request, Headers("tools/call", "estate_health"));
        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.Equal(-32020, body!["error"]!["code"]!.GetValue<int>());
        Assert.Equal(records, harness.Estate.Load().Records.Count);

        (status, body, _, _) = await client.PostAsync(Request("ping"), Headers("ping"));
        Assert.Equal(HttpStatusCode.NotFound, status);
        Assert.Equal(-32601, body!["error"]!["code"]!.GetValue<int>());

        var badOrigin = Headers("server/discover");
        badOrigin["Origin"] = "https://foreign.example";
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsync(Request("server/discover"), badOrigin)).Status);
        Assert.Equal(0, harness.Host.Mcp.Sessions.Count);

        foreach (HttpMethod method in new[] { HttpMethod.Get, HttpMethod.Delete })
        foreach (string? version in new[] { Modern, null })
        {
            using var attempt = new HttpRequestMessage(method, new Uri("mcp", UriKind.Relative));
            if (version is not null) attempt.Headers.TryAddWithoutValidation("MCP-Protocol-Version", version);
            using HttpResponseMessage refused = await client.Http.SendAsync(attempt, TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.MethodNotAllowed, refused.StatusCode);
            Assert.Equal(["POST"], refused.Content.Headers.Allow);
        }
    }

    [Fact]
    public async Task LegacySessionAndVersionCannotDispatchModernMetadata()
    {
        (LocalNetworkHarness harness, _) = await NetworkMcp.StartAsync();
        await using LocalNetworkHarness owned = harness;
        using McpWire client = NetworkMcp.Wire(harness, harness.Pair(harness.Estate.BuilderId, "Agent laptop"));
        await client.InitializeAsync(Legacy);

        (HttpStatusCode status, JsonObject? body, _, _) = await client.PostAsync(Request("tools/list"));
        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.Equal(-32020, body!["error"]!["code"]!.GetValue<int>());
        Assert.Contains("MCP-Protocol-Version", body["error"]!["message"]!.GetValue<string>(), StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsync(McpWire.Request("tools/list"))).Status);
        Assert.Equal(1, harness.Host.Mcp.Sessions.Count);
    }

    [Fact]
    public async Task AReplayedStatelessRequestIsRefused()
    {
        (LocalNetworkHarness harness, _) = await NetworkMcp.StartAsync();
        await using LocalNetworkHarness owned = harness;
        RuntimeConnection paired = harness.Pair(harness.Estate.BuilderId, "Agent laptop");
        DeviceRequestSigner signer = harness.Signer(paired);
        using X509Certificate2 credential = LocalNetworkCredential.Issue(signer, Base64Url.DecodeFromChars(paired.DevicePublicKey), DateTimeOffset.UtcNow);
        using var raw = new MutualTls(harness, credential, harness.Host.Endpoint.Loopback!.Certificate);
        string request = Request("tools/list").ToJsonString();
        Dictionary<string, string> headers = RawHeaders("tools/list");
        var signed = raw.Sign(signer, "POST", "/mcp", request);

        Assert.Equal(HttpStatusCode.OK, (await raw.SendAsync(HttpMethod.Post, "/mcp", signed, request, headers)).Status);
        (HttpStatusCode status, string body) = await raw.SendAsync(HttpMethod.Post, "/mcp", signed, request, headers);
        Assert.Equal(HttpStatusCode.Unauthorized, status);
        Assert.Contains("already used", body, StringComparison.Ordinal);
        Assert.Equal(0, harness.Host.Mcp.Sessions.Count);
    }

    [Fact]
    public async Task StatelessCallsAndStreamsHaveSeparateBoundedSlots()
    {
        (LocalNetworkHarness harness, _) = await NetworkMcp.StartAsync();
        await using LocalNetworkHarness owned = harness;
        RuntimeConnection paired = harness.Pair(harness.Estate.BuilderId, "Agent laptop");
        using McpWire client = NetworkMcp.Wire(harness, paired);
        using VericairnClient api = harness.Connections.Connect(paired);
        List<IDisposable> calls = [];
        List<HttpResponseMessage> streams = [];
        try
        {
            for (int i = 0; i < EventRoutes.StreamsPerDevice; i++)
            {
                HttpResponseMessage stream = await ListenAsync(client, $"stream-{i}", new JsonObject());
                streams.Add(stream);
                Assert.Equal(HttpStatusCode.OK, stream.StatusCode);
            }

            using HttpResponseMessage extra = await ListenAsync(client, "extra", new JsonObject());
            Assert.Equal(HttpStatusCode.TooManyRequests, extra.StatusCode);
            using HttpResponseMessage apiStream = await client.Http.GetAsync(new Uri("v1/events?wait=0", UriKind.Relative), TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.TooManyRequests, apiStream.StatusCode);
            await ResultAsync(client, "tools/list");

            for (int i = 0; i < McpSessions.CallsPerDevice; i++)
            {
                calls.Add(Assert.IsAssignableFrom<IDisposable>(harness.Host.Mcp.Sessions.TryCall(paired.Device)));
            }

            Assert.Equal(HttpStatusCode.TooManyRequests, (await client.PostAsync(Request("tools/list"), Headers("tools/list"))).Status);
            Assert.Equal(harness.Host.Service.Session.Manifest.Estate, api.Health().Estate);
            Assert.Equal(0, harness.Host.Mcp.Sessions.Count);
        }
        finally
        {
            foreach (IDisposable call in calls) call.Dispose();
            foreach (HttpResponseMessage stream in streams) stream.Dispose();
        }

        await WaitUntilAsync(() => harness.Host.EventStreams.Total == 0);
        await ResultAsync(client, "tools/list");
        using HttpResponseMessage reopened = await ListenAsync(client, "reopened", new JsonObject());
        Assert.Equal(HttpStatusCode.OK, reopened.StatusCode);
    }

    [Fact]
    public async Task SeparateSubscriptionsAcknowledgeTheirOwnFiltersAndKeepPresenceOnlyWhileOpen()
    {
        (LocalNetworkHarness harness, string assignment) = await NetworkMcp.StartAsync();
        await using LocalNetworkHarness owned = harness;
        string principal = harness.Estate.BuilderId;
        using McpWire client = NetworkMcp.Wire(harness, harness.Pair(principal, "Agent laptop"));
        string order = Assert.Single(harness.Socket.Records("work-order")).Id;
        string uri = $"vericairn://{harness.Host.Service.Session.Manifest.Estate}/brief/{order}";

        await using var first = new McpStream(await ListenAsync(client, "updates", new JsonObject
        {
            ["toolsListChanged"] = true,
            ["resourceSubscriptions"] = new JsonArray(JsonValue.Create(uri)),
        }));
        string privateAsks = $"vericairn://{harness.Host.Service.Session.Manifest.Estate}/asks/{harness.Estate.OperatorId}";
        await using var second = new McpStream(await ListenAsync(client, "lists", new JsonObject
        {
            ["resourcesListChanged"] = true,
            ["resourceSubscriptions"] = new JsonArray(JsonValue.Create(privateAsks)),
        }));
        JsonObject firstAck = await first.NextAsync(_ => true);
        JsonObject secondAck = await second.NextAsync(_ => true);
        AssertMessage(firstAck, Acknowledged, "updates");
        AssertMessage(secondAck, Acknowledged, "lists");
        Assert.Equal(uri, firstAck["params"]!["notifications"]!["resourceSubscriptions"]![0]!.GetValue<string>());
        Assert.Null(firstAck["params"]!["notifications"]!["toolsListChanged"]);
        Assert.True(secondAck["params"]!["notifications"]!["resourcesListChanged"]!.GetValue<bool>());
        Assert.Null(secondAck["params"]!["notifications"]!["resourceSubscriptions"]);
        Assert.Equal(2, harness.Socket.Presence(principal).Sessions.Count(s => s.State == "live"));
        Assert.Equal(0, harness.Host.Mcp.Sessions.Count);

        Assert.True(harness.Socket.Attempt(new AttemptRequest(assignment, principal, "widget repository", "Build it.")).Ok);
        AssertMessage(await first.NextAsync(_ => true, 1), McpEvents.UpdatedMethod, "updates");
        AssertMessage(await second.NextAsync(_ => true, 1), McpEvents.ListChangedMethod, "lists");

        await first.DisposeAsync();
        await second.DisposeAsync();
        await WaitUntilAsync(() => harness.Socket.Presence(principal).Sessions.Count == 0);
    }

    [Fact]
    public async Task EachDevicesStatelessListenerHearsOnlyItsPrincipalsAsks()
    {
        (LocalNetworkHarness harness, string assignment) = await NetworkMcp.StartAsync();
        await using LocalNetworkHarness owned = harness;
        RuntimeConnection builder = harness.Pair(harness.Estate.BuilderId, "Agent laptop");
        RuntimeConnection other = PairOther(harness);
        using McpWire first = NetworkMcp.Wire(harness, builder);
        using McpWire second = NetworkMcp.Wire(harness, other);
        string estate = harness.Host.Service.Session.Manifest.Estate;
        string firstAsks = $"vericairn://{estate}/asks/{builder.Principal}";
        string secondAsks = $"vericairn://{estate}/asks/{other.Principal}";
        string brief = $"vericairn://{estate}/brief/W-1";
        var requested = new JsonArray(firstAsks, secondAsks, brief);
        await using var firstStream = new McpStream(await ListenAsync(first, "first", new JsonObject { ["resourceSubscriptions"] = requested.DeepClone() }));
        await using var secondStream = new McpStream(await ListenAsync(second, "second", new JsonObject { ["resourceSubscriptions"] = requested.DeepClone() }));
        await CheckAcknowledged(firstStream, "first", firstAsks);
        await CheckAcknowledged(secondStream, "second", secondAsks);

        WriteAsk(builder.Principal);
        AssertMessage(await firstStream.NextAsync(message => IsUpdate(message, firstAsks)), McpEvents.UpdatedMethod, "first");
        WriteAsk(other.Principal);
        AssertMessage(await secondStream.NextAsync(message => IsUpdate(message, secondAsks)), McpEvents.UpdatedMethod, "second");

        // Both observers reach an event after the two private asks, so silence about the other principal is meaningful.
        Assert.True(harness.Socket.Attempt(new AttemptRequest(assignment, builder.Principal, "widget repository", "Build it.")).Ok);
        AssertMessage(await firstStream.NextAsync(message => IsUpdate(message, brief)), McpEvents.UpdatedMethod, "first");
        AssertMessage(await secondStream.NextAsync(message => IsUpdate(message, brief)), McpEvents.UpdatedMethod, "second");
        Assert.DoesNotContain(firstStream.Received, message => IsUpdate(message, secondAsks));
        Assert.DoesNotContain(secondStream.Received, message => IsUpdate(message, firstAsks));

        async Task CheckAcknowledged(McpStream stream, string id, string asks)
        {
            JsonObject ack = await stream.NextAsync(_ => true);
            AssertMessage(ack, Acknowledged, id);
            Assert.Equal(new[] { asks, brief }.Order(StringComparer.Ordinal),
                ack["params"]!["notifications"]!["resourceSubscriptions"]!.AsArray().Select(uri => uri!.GetValue<string>()).Order(StringComparer.Ordinal));
        }

        void WriteAsk(string to)
        {
            var body = JsonSerializer.SerializeToElement(new
            {
                to, question = "Is the widget ready?", recommendation = "Review it.", consequence = "Work waits.", state = "open",
            });
            WriteOutcome written = harness.Socket.NewRecord(new NewRecordRequest("ask", harness.Estate.OperatorId, body));
            Assert.True(written.Ok, written.Problem?.Message);
        }
    }

    [Fact]
    public async Task AnIdleStatelessSubscriptionSendsKeepAliveCommentsAfterItsAcknowledgment()
    {
        (LocalNetworkHarness harness, _) = await NetworkMcp.StartAsync();
        await using LocalNetworkHarness owned = harness;
        using McpWire client = NetworkMcp.Wire(harness, harness.Pair(harness.Estate.BuilderId, "Agent laptop"));
        using HttpResponseMessage response = await ListenAsync(client, "idle", new JsonObject());
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var reader = new StreamReader(await response.Content.ReadAsStreamAsync(TestContext.Current.CancellationToken));
        Assert.Equal("event: message", await McpWire.ReadUntilAsync(reader, _ => true));
        string data = Assert.IsType<string>(await McpWire.ReadUntilAsync(reader, _ => true));
        Assert.StartsWith("data: ", data, StringComparison.Ordinal);
        AssertMessage(JsonNode.Parse(data[6..])!.AsObject(), Acknowledged, "idle");

        for (int i = 0; i < 2; i++)
        {
            harness.Time.Advance(EventRoutes.KeepAlive);
            Assert.Equal(": keep-alive", await McpWire.ReadUntilAsync(reader, line => line.Length > 0));
        }
    }

    [Fact]
    public async Task RevocationClosesAStatelessSubscriptionAndRefusesTheDevicesNextRequest()
    {
        (LocalNetworkHarness harness, _) = await NetworkMcp.StartAsync();
        await using LocalNetworkHarness owned = harness;
        RuntimeConnection paired = harness.Pair(harness.Estate.BuilderId, "Agent laptop");
        using McpWire client = NetworkMcp.Wire(harness, paired);
        DeviceRequestSigner signer = harness.Signer(paired);
        using X509Certificate2 credential = LocalNetworkCredential.Issue(signer, Base64Url.DecodeFromChars(paired.DevicePublicKey), DateTimeOffset.UtcNow);
        using var raw = new MutualTls(harness, credential, harness.Host.Endpoint.Loopback!.Certificate);
        Dictionary<string, string> headers = RawHeaders("tools/list");
        Assert.Equal(HttpStatusCode.OK, (await raw.SendSignedAsync(signer, HttpMethod.Post, "/mcp", Request("tools/list").ToJsonString(), headers)).Status);
        await using var stream = new McpStream(await ListenAsync(client, "idle", new JsonObject()));
        AssertMessage(await stream.NextAsync(_ => true), Acknowledged, "idle");

        Assert.True(harness.Socket.DeviceRevoke(new RevokeRequest(paired.Device, harness.Estate.OperatorId, "The laptop was lost.")).Ok);
        (HttpStatusCode status, string body) = await raw.SendSignedAsync(signer, HttpMethod.Post, "/mcp", Request("tools/list").ToJsonString(), headers);
        Assert.Equal(HttpStatusCode.Unauthorized, status);
        Assert.Contains("revoked", body, StringComparison.Ordinal);
        Assert.True(await TestTiming.CompletesAsync(stream.Completion, TestContext.Current.CancellationToken), "The revoked device's stream stayed open.");
        Assert.Single(stream.Received);
        Assert.Equal(0, harness.Host.Mcp.Sessions.CountFor(paired.Device));
    }

    [Fact]
    public async Task TheBridgeDiscoversWritesAndListensAgainstTheDaemonWithoutASession()
    {
        (LocalNetworkHarness harness, string assignment) = await NetworkMcp.StartAsync();
        await using LocalNetworkHarness owned = harness;
        RuntimeConnection paired = harness.Pair(harness.Estate.BuilderId, "Agent laptop");
        DeviceRequestSigner signer = harness.Signer(paired);
        using HttpClient http = VericairnClient.SignedHttp(paired, signer);
        using var bridge = new McpBridge(http, paired.Principal, digest => signer.Sign(digest));
        await using (var client = new McpLines((input, output, token) => bridge.RunAsync(input, output, token)))
        {
            JsonObject discover = await client.RequestAsync("server/discover", Parameters());
            Assert.Equal(Modern, discover["result"]!["supportedVersions"]![0]!.GetValue<string>());
            JsonObject listed = await client.RequestAsync("tools/list", Parameters());
            Assert.Contains(listed["result"]!["tools"]!.AsArray(), tool => tool!["name"]!.GetValue<string>() == "work_attempt");

            string brief = $"vericairn://{harness.Host.Service.Session.Manifest.Estate}/brief/W-1";
            await client.SendAsync(Request("subscriptions/listen", new JsonObject
            {
                ["notifications"] = new JsonObject { ["resourceSubscriptions"] = new JsonArray(brief), ["resourcesListChanged"] = true },
            }, id: "watch"));
            AssertMessage(await client.NotificationAsync(_ => true), Acknowledged, "watch");

            JsonObject attempt = await client.RequestAsync("tools/call", Parameters(Tools("work_attempt",
                NetworkMcp.Args(("assignment", assignment), ("workspace", "widget repository"), ("note", "Build it.")))));
            WriteOutcome written = McpWire.Outcome(attempt["result"]!.AsObject());
            Assert.True(written.Ok, written.Problem?.Message);
            var record = Assert.Single(harness.Estate.Load().Records, envelope => envelope.Id == written.Written!.Id);
            Assert.Equal(paired.Device, Assert.Single(record.Signatures).Device);

            AssertMessage(await client.NotificationAsync(message => IsUpdate(message, brief)), McpEvents.UpdatedMethod, "watch");
            AssertMessage(await client.NotificationAsync(message => message["method"]?.GetValue<string>() == McpEvents.ListChangedMethod), McpEvents.ListChangedMethod, "watch");
            Assert.Null(bridge.Session);
            Assert.Equal(0, harness.Host.Mcp.Sessions.Count);
        }

        await WaitUntilAsync(() => harness.Host.EventStreams.Total == 0);
        Assert.Equal(0, harness.Host.Mcp.Sessions.Count);
    }

    [Fact]
    public async Task StdioListOnlySubscriptionsFollowTheDaemonsJournal()
    {
        (LocalNetworkHarness harness, string assignment) = await NetworkMcp.StartAsync(mcp: false);
        await using LocalNetworkHarness owned = harness;
        using var api = new VericairnClient(harness.Host.Endpoint);
        string principal = harness.Estate.BuilderId;
        var server = new McpServer(new ToolRegistry(api, principal), "test")
        {
            ListenSubscriptions = () => new McpResourceWatch(new McpApiEventSource(api, principal, keepListCursor: true)),
        };
        await using var client = new McpLines((input, output, token) => server.RunAsync(input, output, null, token));
        await client.SendAsync(Request("subscriptions/listen", new JsonObject
        {
            ["notifications"] = new JsonObject { ["resourcesListChanged"] = true },
        }, id: "lists"));
        JsonObject ack = await client.NotificationAsync(_ => true);
        AssertMessage(ack, Acknowledged, "lists");
        Assert.True(ack["params"]!["notifications"]!["resourcesListChanged"]!.GetValue<bool>());

        await WaitUntilAsync(() => harness.Host.EventStreams.Mcp > 0);
        Assert.True(harness.Socket.Attempt(new AttemptRequest(assignment, principal, "widget repository", "Build it.")).Ok);
        AssertMessage(await client.NotificationAsync(_ => true, from: 1), McpEvents.ListChangedMethod, "lists");
    }

    private static string[] Sorted(JsonObject result, string collection, string key) =>
        [.. result[collection]!.AsArray().OrderBy(item => item![key]!.GetValue<string>(), StringComparer.Ordinal).Select(item => item!.ToJsonString())];

    private static void AssertMessage(JsonObject message, string method, string id)
    {
        Assert.Equal(method, message["method"]!.GetValue<string>());
        Assert.Equal(id, message["params"]!["_meta"]![SubscriptionKey]!.GetValue<string>());
    }

    private static bool IsUpdate(JsonObject message, string uri) =>
        message["method"]?.GetValue<string>() == McpEvents.UpdatedMethod && message["params"]?["uri"]?.GetValue<string>() == uri;

    private static RuntimeConnection PairOther(LocalNetworkHarness harness)
    {
        string other = harness.Estate.Add("principal", harness.Estate.OperatorId,
            """{"kind":"agent","name":"Other Agent","roles":["contributor"],"agent":{"vendor":"Example","product":"Example Agent"}}""").Id;
        return harness.Pair(other, "Other laptop");
    }

    private static JsonObject SignPlan(JsonObject prepared, IRequestSigner signer)
    {
        JsonObject plan = prepared["_meta"]!["vericairn/plan"]!["prepared"]!.AsObject();
        return new JsonObject
        {
            ["vericairn/plan"] = plan["plan"]!.GetValue<string>(),
            ["vericairn/signature"] = McpWire.Sign(signer, plan["hash"]!.GetValue<string>()),
        };
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        using var guard = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        guard.CancelAfter(TestTiming.HangTimeout);
        while (!condition())
        {
            await Task.Delay(TimeSpan.FromMilliseconds(20), guard.Token);
        }
    }

    private static async Task<JsonObject> ResultAsync(McpWire client, string method, JsonObject? parameters = null)
    {
        string? name = (parameters?["name"] ?? parameters?["uri"])?.GetValue<string>();
        (HttpStatusCode status, JsonObject? body, string text, string? session) = await client.PostAsync(Request(method, parameters), Headers(method, name));
        Assert.True(status == HttpStatusCode.OK, text);
        Assert.Null(session);
        return body!["result"]!.AsObject();
    }

    private static JsonObject Tools(string tool, JsonObject? arguments = null, JsonObject? meta = null) => new()
    {
        ["name"] = tool,
        ["arguments"] = arguments?.DeepClone() ?? new JsonObject(),
        ["_meta"] = meta?.DeepClone() ?? new JsonObject(),
    };

    private static JsonObject Request(string method, JsonObject? parameters = null, string? id = null, string version = Modern) =>
        McpWire.Request(method, Parameters(parameters, version), id is null ? null : JsonValue.Create(id));

    private static JsonObject Parameters(JsonObject? parameters = null, string version = Modern)
    {
        parameters ??= new JsonObject();
        JsonObject meta = parameters["_meta"] is JsonObject current ? current : new JsonObject();
        meta[VersionKey] = version;
        meta[CapabilitiesKey] = new JsonObject();
        if (parameters["_meta"] is null)
        {
            parameters["_meta"] = meta;
        }
        return parameters;
    }

    private static Dictionary<string, string?> Headers(string method, string? name = null) => new(StringComparer.OrdinalIgnoreCase)
    {
        ["MCP-Protocol-Version"] = Modern,
        ["Mcp-Method"] = method,
        ["Mcp-Name"] = name,
    };

    private static Dictionary<string, string> RawHeaders(string method) => new()
    {
        ["Accept"] = "application/json, text/event-stream",
        ["MCP-Protocol-Version"] = Modern,
        ["Mcp-Method"] = method,
    };

    private static async Task<HttpResponseMessage> ListenAsync(McpWire client, string id, JsonObject notifications)
    {
        const string method = "subscriptions/listen";
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri("mcp", UriKind.Relative))
        {
            Content = new StringContent(Request(method, new JsonObject { ["notifications"] = notifications }, id).ToJsonString(), Encoding.UTF8, "application/json"),
        };
        request.Headers.TryAddWithoutValidation("Accept", "application/json, text/event-stream");
        request.Headers.TryAddWithoutValidation("MCP-Protocol-Version", Modern);
        request.Headers.TryAddWithoutValidation("Mcp-Method", method);
        using var guard = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        guard.CancelAfter(TestTiming.HangTimeout);
        return await client.Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, guard.Token);
    }
}
