// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Buffers.Text;
using System.Net;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json.Nodes;
using Vericairn.Protocol;
using Vericairn.Records.Signing;
using Vericairn.Sdk;

namespace Vericairn.Daemon.Tests;

/// <summary>
/// Section 11's threats for what MCP-03 builds, each with its test: DNS rebinding through Host and Origin, a session taken by
/// another device, a revoked device's session, a call outside the device's scope, a tool section 8 excludes, a replayed signed
/// request, an oversize body, and the stream and call limits. The local-network profile listens on 127.0.0.1.
/// </summary>
[Collection("daemon")]
public class NetworkMcpThreatTests
{
    private const string Initialize = """{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-06-18","capabilities":{},"clientInfo":{"name":"test","version":"0"}}}""";

    [Fact]
    public async Task AForeignHostOrOriginRunsNothingAndIsRefusedBeforeAuthentication()
    {
        (LocalNetworkHarness harness, _) = await NetworkMcp.StartAsync(origins: ["https://agent.example"]);
        await using LocalNetworkHarness owned = harness;
        RuntimeConnection paired = harness.Pair(harness.Estate.BuilderId, "Agent laptop");
        DeviceRequestSigner signer = harness.Signer(paired);
        using X509Certificate2 credential = LocalNetworkCredential.Issue(signer, Base64Url.DecodeFromChars(paired.DevicePublicKey), DateTimeOffset.UtcNow);
        using var raw = new MutualTls(harness, credential, harness.Host.Endpoint.Loopback!.Certificate);
        string port = harness.Address.Split(':')[^1];

        // A name an attacker's DNS points at this address reaches nothing, even signed by an admitted device.
        foreach (string host in new[] { "attacker.example:" + port, "127.0.0.1:1", "attacker.example" })
        {
            (HttpStatusCode status, string body) = await raw.SendSignedAsync(signer, HttpMethod.Post, "/mcp", Initialize, new Dictionary<string, string> { ["Host"] = host });
            Assert.Equal(HttpStatusCode.Forbidden, status);
            Assert.Contains("Host", body, StringComparison.Ordinal);
        }

        // An origin the operator did not allow is refused before anything is authenticated: unsigned, it is still a 403, not a 401.
        (HttpStatusCode unsigned, string refusal) = await raw.SendAsync(HttpMethod.Post, "/mcp", null, Initialize, new Dictionary<string, string> { ["Origin"] = "https://evil.example" });
        Assert.Equal(HttpStatusCode.Forbidden, unsigned);
        Assert.Contains("Origin", refusal, StringComparison.Ordinal);
        (HttpStatusCode signedForeign, _) = await raw.SendSignedAsync(signer, HttpMethod.Post, "/mcp", Initialize, new Dictionary<string, string> { ["Origin"] = "null" });
        Assert.Equal(HttpStatusCode.Forbidden, signedForeign);
        Assert.Equal(0, harness.Host.Mcp.Sessions.Count);

        // Without an Origin, as a native client sends, or with one the operator allowed, the device opens its session.
        Assert.Equal(HttpStatusCode.OK, (await raw.SendSignedAsync(signer, HttpMethod.Post, "/mcp", Initialize)).Status);
        Assert.Equal(HttpStatusCode.OK, (await raw.SendSignedAsync(signer, HttpMethod.Post, "/mcp", Initialize, new Dictionary<string, string> { ["Origin"] = "https://agent.example" })).Status);
        Assert.Equal(2, harness.Host.Mcp.Sessions.Count);

        // Unsigned with no Origin, a request is authenticated as every other is, and refused.
        Assert.Equal(HttpStatusCode.Unauthorized, (await raw.SendAsync(HttpMethod.Post, "/mcp", null, Initialize)).Status);
    }

    [Fact]
    public async Task AnotherDevicesSessionIdIsNotFoundAndTheSessionKeepsWorking()
    {
        (LocalNetworkHarness harness, _) = await NetworkMcp.StartAsync();
        await using LocalNetworkHarness owned = harness;
        string other = harness.Estate.Add("principal", harness.Estate.OperatorId, """{"kind":"agent","name":"Other Agent","roles":["contributor"],"agent":{"vendor":"Example","product":"Example Agent"}}""").Id;
        RuntimeConnection first = harness.Pair(harness.Estate.BuilderId, "Agent laptop");
        RuntimeConnection sameAgent = harness.Pair(harness.Estate.BuilderId, "Agent desktop");
        RuntimeConnection otherAgent = harness.Pair(other, "Other laptop");
        using McpWire owner = NetworkMcp.Wire(harness, first);
        await owner.InitializeAsync();

        // The id belongs to the device that opened it: neither another device of its principal nor another principal finds it.
        foreach (RuntimeConnection thief in new[] { sameAgent, otherAgent })
        {
            using McpWire stolen = NetworkMcp.Wire(harness, thief);
            stolen.Session = owner.Session;
            stolen.Version = owner.Version;
            (HttpStatusCode status, _, string text, _) = await stolen.PostAsync(McpWire.Request("tools/list"));
            Assert.Equal(HttpStatusCode.NotFound, status);
            Assert.DoesNotContain(first.Device, text, StringComparison.Ordinal);
            using HttpResponseMessage stream = await stolen.OpenStreamAsync();
            Assert.Equal(HttpStatusCode.NotFound, stream.StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, await stolen.DeleteAsync());
        }

        Assert.False((await owner.CallAsync("estate_status"))["isError"]!.GetValue<bool>());
        Assert.Equal(1, harness.Host.Mcp.Sessions.CountFor(first.Device));
    }

    [Fact]
    public async Task ARevokedDevicesSessionEndsBeforeItsNextCall()
    {
        (LocalNetworkHarness harness, _) = await NetworkMcp.StartAsync();
        await using LocalNetworkHarness owned = harness;
        RuntimeConnection paired = harness.Pair(harness.Estate.BuilderId, "Agent laptop");
        using McpWire client = NetworkMcp.Wire(harness, paired);
        await client.InitializeAsync();
        using HttpResponseMessage stream = await client.OpenStreamAsync();
        Assert.Equal(HttpStatusCode.OK, stream.StatusCode);
        using var reader = new StreamReader(await stream.Content.ReadAsStreamAsync(TestContext.Current.CancellationToken));
        Assert.NotNull(await McpWire.ReadUntilAsync(reader, line => line.StartsWith(':')));

        Assert.True(harness.Socket.DeviceRevoke(new RevokeRequest(paired.Device, harness.Estate.OperatorId, "The laptop was lost.")).Ok);

        // Its next request is refused, on an open connection or when a new one proves its identity; its session is gone and its server
        // stream has ended.
        string refusal;
        try
        {
            (HttpStatusCode status, _, string text, _) = await client.PostAsync(McpWire.Request("tools/list"));
            Assert.Equal(HttpStatusCode.Unauthorized, status);
            refusal = text;
        }
        catch (HttpRequestException exception)
        {
            refusal = exception.Message;
        }

        Assert.Contains("revoked", refusal, StringComparison.Ordinal);
        Assert.Equal(0, harness.Host.Mcp.Sessions.CountFor(paired.Device));
        Assert.Null(await McpWire.ReadUntilAsync(reader, line => line.StartsWith("event:", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task ACallOutsideTheDevicesScopeIsRefusedExactlyAsTheApiRefusesIt()
    {
        (LocalNetworkHarness harness, string assignment) = await NetworkMcp.StartAsync();
        await using LocalNetworkHarness owned = harness;
        string other = harness.Estate.Add("principal", harness.Estate.OperatorId, """{"kind":"agent","name":"Other Agent","roles":["contributor"],"agent":{"vendor":"Example","product":"Example Agent"}}""").Id;
        string attempt = harness.Socket.Attempt(new AttemptRequest(assignment, harness.Estate.BuilderId, "widget repository", "Build it.")).Written!.Id;
        RuntimeConnection stranger = harness.Pair(other, "Other laptop");

        // Over the API, as the device: its principal cannot claim another principal's progress.
        using VericairnClient api = harness.Connections.Connect(stranger);
        WorkProgressResponse direct = await api.WorkProgressAsync(new WorkProgressRequest(attempt, other, "build", "Not mine."), TestContext.Current.CancellationToken);
        Assert.False(direct.Ok);

        // Over MCP, the same operation for the same device is refused with the same problem.
        using McpWire client = NetworkMcp.Wire(harness, stranger);
        await client.InitializeAsync();
        JsonObject refused = await client.CallAsync("work_progress", NetworkMcp.Args(("attempt", attempt), ("phase", "build"), ("note", "Not mine."), ("state", "active")));
        Assert.True(refused["isError"]!.GetValue<bool>());
        Assert.Contains(direct.Problem!.Message, McpWire.Text(refused), StringComparison.Ordinal);

        // A person's device reaches no network MCP: a person connects a client as an agent principal of their own.
        RuntimeConnection person = harness.Pair(harness.Estate.OperatorId, "Operator phone");
        using McpWire human = NetworkMcp.Wire(harness, person);
        (HttpStatusCode status, _, string text, _) = await human.PostAsync(JsonNode.Parse(Initialize)!.AsObject());
        Assert.Equal(HttpStatusCode.Forbidden, status);
        Assert.Contains("agent principal of their own", text, StringComparison.Ordinal);

        // What the tools reach in this process: the routes' reads, section 8's writes, never the machine's, the operator's or a run.
        Assert.True(McpOperations.Offered("POST /v1/work/attempt"));
        Assert.True(McpOperations.Offered("GET /v1/status"));
        foreach (string refusedOperation in new[] { "POST /v1/runs", "POST /v1/work/review", "POST /v1/operator/record", "POST /v1/operator/close", "POST /v1/records/signed",
            "POST /v1/records", "POST /v1/devices/admit", "POST /v1/pairings", "POST /v1/shutdown", "POST /v1/goals/{id}/step", "POST /v1/releases/submit", "GET /v1/events",
            "POST /v1/prompts/use", "POST /v1/estate-policies", "POST /v1/attention/acknowledge" })
        {
            Assert.False(McpOperations.Offered(refusedOperation), refusedOperation);
        }

        Assert.All(McpOperations.Posts, operation => Assert.True(RequestScope.Covers(operation), operation));
    }

    [Fact]
    public async Task AToolSectionEightExcludesIsAbsentAndAnsweredAsUnknown()
    {
        (LocalNetworkHarness harness, string assignment) = await NetworkMcp.StartAsync();
        await using LocalNetworkHarness owned = harness;
        RuntimeConnection paired = harness.Pair(harness.Estate.BuilderId, "Agent laptop");
        using McpWire client = NetworkMcp.Wire(harness, paired);
        await client.InitializeAsync();
        int records = harness.Estate.Load().Records.Count;

        foreach (string tool in NetworkMcpJourneyTests.Hidden)
        {
            (HttpStatusCode status, JsonObject? body, _, _) = await client.PostAsync(McpWire.Request("tools/call", new JsonObject { ["name"] = tool, ["arguments"] = new JsonObject() }));
            Assert.Equal(HttpStatusCode.OK, status);
            Assert.Equal(-32602, body!["error"]!["code"]!.GetValue<int>());
            Assert.Equal($"Unknown tool '{tool}'.", body["error"]!["message"]!.GetValue<string>());
        }

        (_, JsonObject? prompt, _, _) = await client.PostAsync(McpWire.Request("prompts/get", new JsonObject { ["name"] = "any" }));
        Assert.Equal(-32601, prompt!["error"]!["code"]!.GetValue<int>());

        // Duty evidence cannot be prepared, so a result carrying duties or an expected hash is refused before anything runs.
        string attempt = harness.Socket.Attempt(new AttemptRequest(assignment, harness.Estate.BuilderId, "widget repository", "Build it.")).Written!.Id;
        JsonObject withDuties = NetworkMcp.ResultBody();
        withDuties["duties"] = new JsonArray();
        JsonObject duties = await client.CallAsync("work_result", NetworkMcp.Args(("attempt", attempt), ("body", withDuties)), new JsonObject { ["vericairn/prepare"] = true });
        Assert.True(duties["isError"]!.GetValue<bool>());
        Assert.Contains("Duty evidence", McpWire.Text(duties), StringComparison.Ordinal);
        JsonObject expected = await client.CallAsync("work_result", NetworkMcp.Args(("attempt", attempt), ("expected", "sha256:" + new string('0', 64)), ("body", NetworkMcp.ResultBody())), new JsonObject { ["vericairn/prepare"] = true });
        Assert.True(expected["isError"]!.GetValue<bool>());
        JsonObject listed = (await client.PostAsync(McpWire.Request("tools/list"))).Body!;
        Assert.False(listed["result"]!["tools"]!.AsArray().Single(t => t!["name"]!.GetValue<string>() == "work_result")!["inputSchema"]!["properties"]!.AsObject().ContainsKey("expected"));
        Assert.Equal(records + 1, harness.Estate.Load().Records.Count);
    }

    [Fact]
    public async Task ReplayedSignedRequestsAreRefused()
    {
        (LocalNetworkHarness harness, _) = await NetworkMcp.StartAsync();
        await using LocalNetworkHarness owned = harness;
        RuntimeConnection paired = harness.Pair(harness.Estate.BuilderId, "Agent laptop");
        DeviceRequestSigner signer = harness.Signer(paired);
        using X509Certificate2 credential = LocalNetworkCredential.Issue(signer, Base64Url.DecodeFromChars(paired.DevicePublicKey), DateTimeOffset.UtcNow);
        using var raw = new MutualTls(harness, credential, harness.Host.Endpoint.Loopback!.Certificate);

        (string Device, string SignedAt, string Signature) once = raw.Sign(signer, "POST", "/mcp", Initialize);
        Assert.Equal(HttpStatusCode.OK, (await raw.SendAsync(HttpMethod.Post, "/mcp", once, Initialize)).Status);
        (HttpStatusCode status, string body) = await raw.SendAsync(HttpMethod.Post, "/mcp", once, Initialize);
        Assert.Equal(HttpStatusCode.Unauthorized, status);
        Assert.Contains("already used", body, StringComparison.Ordinal);
        Assert.Equal(1, harness.Host.Mcp.Sessions.Count);

        // A signature over one message does not carry another.
        (HttpStatusCode other, string otherBody) = await raw.SendAsync(HttpMethod.Post, "/mcp", raw.Sign(signer, "POST", "/mcp", Initialize), Initialize.Replace("\"id\":1", "\"id\":2", StringComparison.Ordinal));
        Assert.Equal(HttpStatusCode.Unauthorized, other);
        Assert.Contains("does not verify", otherBody, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnOversizeBodyIsRefusedBeforeItIsReadOrAuthenticated()
    {
        (LocalNetworkHarness harness, _) = await NetworkMcp.StartAsync();
        await using LocalNetworkHarness owned = harness;
        RuntimeConnection paired = harness.Pair(harness.Estate.BuilderId, "Agent laptop");
        DeviceRequestSigner signer = harness.Signer(paired);
        using X509Certificate2 credential = LocalNetworkCredential.Issue(signer, Base64Url.DecodeFromChars(paired.DevicePublicKey), DateTimeOffset.UtcNow);

        // The declared length alone refuses it: nothing of the body is read beyond the refusal's bounded drain, and no signature is tried.
        byte[] head = System.Text.Encoding.ASCII.GetBytes($"POST /mcp HTTP/1.1\r\nHost: {harness.Address}\r\nContent-Type: application/json\r\nConnection: close\r\nContent-Length: {RequestAuthenticator.SignedBodyLimit + 1}\r\n\r\n{{\"jsonrpc\":\"2.0\"");
        string answer = await Raw.ExchangeAsync(harness.Address, credential, harness.Host.Endpoint.Loopback!.Certificate, head, TestTiming.HangTimeout);
        Assert.StartsWith("HTTP/1.1 413", answer, StringComparison.Ordinal);
        Assert.Contains("limit", answer, StringComparison.Ordinal);
        Assert.Equal(0, harness.Host.Mcp.Sessions.Count);
    }

    [Fact]
    public async Task ServerStreamsAndCallsAreLimitedSoMcpNeverStarvesTheApi()
    {
        (LocalNetworkHarness harness, _) = await NetworkMcp.StartAsync();
        await using LocalNetworkHarness owned = harness;
        RuntimeConnection paired = harness.Pair(harness.Estate.BuilderId, "Agent laptop");
        using McpWire client = NetworkMcp.Wire(harness, paired);
        await client.InitializeAsync();

        // A session has one server stream; a GET that does not accept one is not opened.
        using (HttpResponseMessage notAccepted = await client.OpenStreamAsync("application/json"))
        {
            Assert.Equal(HttpStatusCode.NotAcceptable, notAccepted.StatusCode);
        }

        using HttpResponseMessage first = await client.OpenStreamAsync();
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        using (HttpResponseMessage second = await client.OpenStreamAsync())
        {
            Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        }

        // Server streams take the event streams' slots: with its device's other three held by event streams, a new one is refused.
        using VericairnClient api = harness.Connections.Connect(paired);
        using var holding = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var opened = new List<Task>();
        using var ready = new SemaphoreSlim(0);
        harness.Host.EventStreamOpened = () => ready.Release();
        for (int i = 0; i < EventRoutes.StreamsPerDevice - 1; i++)
        {
            opened.Add(Task.Run(async () =>
            {
                try
                {
                    await foreach (EventStreamItem _ in api.StreamEventsAsync(cancellationToken: holding.Token))
                    {
                    }
                }
                catch (Exception exception) when (exception is OperationCanceledException or HttpRequestException or IOException)
                {
                    // Closed by the test.
                }
            }, TestContext.Current.CancellationToken));
            Assert.True(await ready.WaitAsync(TestTiming.HangTimeout, TestContext.Current.CancellationToken));
        }

        using McpWire another = NetworkMcp.Wire(harness, paired);
        await another.InitializeAsync();
        using (HttpResponseMessage refused = await another.OpenStreamAsync())
        {
            Assert.Equal(HttpStatusCode.TooManyRequests, refused.StatusCode);
        }

        await holding.CancelAsync();
        await Task.WhenAll(opened).WaitAsync(TestTiming.HangTimeout, TestContext.Current.CancellationToken);
        harness.Host.EventStreamOpened = null;
    }

    [Fact]
    public void SessionsAndCallsAreBoundedPerDeviceAndPerRuntime()
    {
        var time = new ManualTime(DateTimeOffset.UtcNow);
        var sessions = new McpSessions(time);
        AuthenticatedDevice Device(int i) => new("01DEVICE" + i.ToString("D18", System.Globalization.CultureInfo.InvariantCulture), "01PRINCIPAL", "key", LocalNetwork: true);

        // A device's fifth session ends its least recently used.
        List<McpSession> own = [.. Enumerable.Range(0, McpSessions.PerDevice).Select(_ => sessions.Open(Device(0), "local-network", "2025-06-18")!)];
        time.Advance(TimeSpan.FromSeconds(1));
        Assert.NotNull(sessions.Find(own[0].Id, Device(0), "local-network"));
        McpSession fifth = sessions.Open(Device(0), "local-network", "2025-06-18")!;
        Assert.True(own[1].IsEnded);
        Assert.False(own[0].IsEnded);
        Assert.Equal(McpSessions.PerDevice, sessions.CountFor(Device(0).Identity));

        // The runtime keeps a bounded number in all.
        for (int i = 1; sessions.Count < McpSessions.PerRuntime; i++)
        {
            Assert.NotNull(sessions.Open(Device(i), "local-network", "2025-06-18"));
        }

        Assert.Null(sessions.Open(Device(1000), "local-network", "2025-06-18"));

        // Another profile, an idle hour: no session.
        Assert.Null(sessions.Find(fifth.Id, Device(0), "loopback"));
        time.Advance(McpSessions.Idle + TimeSpan.FromSeconds(1));
        Assert.Null(sessions.Find(fifth.Id, Device(0), "local-network"));
        Assert.True(fifth.IsEnded);

        // A device runs a bounded number of calls at once, and all devices a quarter of the daemon's connections.
        List<IDisposable> calls = [.. Enumerable.Range(0, McpSessions.CallsPerDevice).Select(_ => sessions.TryCall("A")!)];
        Assert.Null(sessions.TryCall("A"));
        calls.AddRange(Enumerable.Range(0, McpSessions.CallsPerRuntime - McpSessions.CallsPerDevice).Select(i => sessions.TryCall("B" + (i % 2).ToString(System.Globalization.CultureInfo.InvariantCulture))!));
        Assert.Null(sessions.TryCall("C"));
        calls[0].Dispose();
        Assert.NotNull(sessions.TryCall("C"));
        Assert.True(McpSessions.CallsPerRuntime + EventRoutes.StreamsPerRuntime + EventRoutes.LocalReserve < DaemonHost.ConcurrentConnections);
    }
}
