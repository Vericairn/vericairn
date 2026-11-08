// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json.Nodes;
using Vericairn.Protocol;
using Vericairn.Records.Signing;
using Vericairn.Sdk;

namespace Vericairn.Daemon.Tests;

/// <summary>
/// The Streamable HTTP transport MCP-03 serves at /mcp (revision 2025-06-18, and 2025-03-26), its signed mode's two-step writes, and
/// the server stream a GET opens for its session's device. The local-network profile listens on 127.0.0.1.
/// </summary>
[Collection("daemon")]
public class NetworkMcpTransportTests
{
    [Fact]
    public async Task TheTransportAnswersAsItsRevisionStates()
    {
        (LocalNetworkHarness harness, _) = await NetworkMcp.StartAsync();
        await using LocalNetworkHarness owned = harness;
        RuntimeConnection paired = harness.Pair(harness.Estate.BuilderId, "Agent laptop");
        using McpWire client = NetworkMcp.Wire(harness, paired);
        Assert.Equal("/mcp", harness.Host.Endpoint.Mcp);

        // The older HTTP transport's revision is not served: initialize negotiates the newest.
        JsonObject older = await client.InitializeAsync("2024-11-05");
        Assert.Equal("2025-06-18", older["result"]!["protocolVersion"]!.GetValue<string>());
        JsonObject compatible = await client.InitializeAsync("2025-03-26");
        Assert.Equal("2025-03-26", compatible["result"]!["protocolVersion"]!.GetValue<string>());
        await client.InitializeAsync();
        string session = client.Session!;
        Assert.True(session.Length >= 40 && session.All(c => c is >= '!' and <= '~'));

        // Every later request names its session; one it does not hold is not found; a version other than the negotiated one is refused.
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync(McpWire.Request("ping"), new Dictionary<string, string?> { ["Mcp-Session-Id"] = null })).Status);
        Assert.Equal(HttpStatusCode.NotFound, (await client.PostAsync(McpWire.Request("ping"), new Dictionary<string, string?> { ["Mcp-Session-Id"] = "not-a-session" })).Status);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync(McpWire.Request("ping"), new Dictionary<string, string?> { ["MCP-Protocol-Version"] = "2025-03-26" })).Status);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsync(McpWire.Request("ping"), new Dictionary<string, string?> { ["MCP-Protocol-Version"] = null })).Status);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync(McpWire.Request("initialize", new JsonObject { ["protocolVersion"] = "2025-06-18" }))).Status);

        // A request gets one JSON body; a notification or a response gets 202 and none; batches are not served.
        (HttpStatusCode pinged, JsonObject? pong, _, _) = await client.PostAsync(McpWire.Request("ping", id: 7));
        Assert.Equal(HttpStatusCode.OK, pinged);
        Assert.Equal(7, pong!["id"]!.GetValue<int>());
        (HttpStatusCode accepted, _, string empty, _) = await client.PostAsync(new JsonObject { ["jsonrpc"] = "2.0", ["method"] = "notifications/cancelled", ["params"] = new JsonObject { ["requestId"] = 99 } });
        Assert.Equal((HttpStatusCode.Accepted, string.Empty), (accepted, empty));
        Assert.Equal(HttpStatusCode.Accepted, (await client.PostAsync(new JsonObject { ["jsonrpc"] = "2.0", ["id"] = 5, ["result"] = new JsonObject() })).Status);
        using (var batch = new HttpRequestMessage(HttpMethod.Post, new Uri("mcp", UriKind.Relative)) { Content = new StringContent("[" + McpWire.Request("ping").ToJsonString() + "]", Encoding.UTF8, "application/json") })
        {
            batch.Headers.TryAddWithoutValidation("Mcp-Session-Id", session);
            using HttpResponseMessage refused = await client.Http.SendAsync(batch, TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
            Assert.Contains("batches", await refused.Content.ReadAsStringAsync(TestContext.Current.CancellationToken), StringComparison.Ordinal);
        }

        using (var text = new HttpRequestMessage(HttpMethod.Post, new Uri("mcp", UriKind.Relative)) { Content = new StringContent("ping", Encoding.UTF8, "text/plain") })
        {
            using HttpResponseMessage refused = await client.Http.SendAsync(text, TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.UnsupportedMediaType, refused.StatusCode);
        }

        using (var put = new HttpRequestMessage(HttpMethod.Put, new Uri("mcp", UriKind.Relative)))
        {
            using HttpResponseMessage refused = await client.Http.SendAsync(put, TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.MethodNotAllowed, refused.StatusCode);
            Assert.Contains("POST", refused.Content.Headers.Allow);
        }

        // DELETE ends the session, after which it is not found; the device's two earlier sessions stay until they end.
        Assert.Equal(3, harness.Host.Mcp.Sessions.CountFor(paired.Device));
        Assert.Equal(HttpStatusCode.NoContent, await client.DeleteAsync());
        Assert.Equal(HttpStatusCode.NotFound, (await client.PostAsync(McpWire.Request("ping"))).Status);
        Assert.Equal(2, harness.Host.Mcp.Sessions.CountFor(paired.Device));
    }

    [Fact]
    public async Task McpIsOffUntilTurnedOnAndNeverServedOnTheLocalSocket()
    {
        (LocalNetworkHarness off, _) = await NetworkMcp.StartAsync(mcp: false);
        await using (off)
        {
            Assert.Null(off.Host.Endpoint.Mcp);
            RuntimeConnection paired = off.Pair(off.Estate.BuilderId, "Agent laptop");
            using McpWire client = NetworkMcp.Wire(off, paired);
            (HttpStatusCode status, _, _, _) = await client.PostAsync(McpWire.Request("initialize", new JsonObject { ["protocolVersion"] = "2025-06-18" }));
            Assert.Equal(HttpStatusCode.NotFound, status);
        }

        (LocalNetworkHarness on, _) = await NetworkMcp.StartAsync();
        await using LocalNetworkHarness owned = on;
        DaemonEndpoint endpoint = on.Host.Endpoint;
        using var socket = new HttpClient(new SocketsHttpHandler
        {
            ConnectCallback = async (_, token) =>
            {
                if (endpoint.Kind == "pipe")
                {
                    var pipe = new System.IO.Pipes.NamedPipeClientStream(".", endpoint.Address, System.IO.Pipes.PipeDirection.InOut, System.IO.Pipes.PipeOptions.Asynchronous);
                    await pipe.ConnectAsync(token);
                    return pipe;
                }

                var unix = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
                await unix.ConnectAsync(new UnixDomainSocketEndPoint(endpoint.Address), token);
                return new NetworkStream(unix, ownsSocket: true);
            },
        }) { BaseAddress = new Uri("http://vericairn/") };
        using HttpResponseMessage local = await socket.PostAsync(new Uri("mcp", UriKind.Relative), new StringContent(McpWire.Request("initialize", new JsonObject { ["protocolVersion"] = "2025-06-18" }).ToJsonString(), Encoding.UTF8, "application/json"), TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NotFound, local.StatusCode);
        Assert.Equal(0, on.Host.Mcp.Sessions.Count);
    }

    [Fact]
    public async Task AWriteIsTheDevicesOwnInTwoStepsAndNothingElseIsWritten()
    {
        (LocalNetworkHarness harness, string assignment) = await NetworkMcp.StartAsync();
        await using LocalNetworkHarness owned = harness;
        RuntimeConnection agent = harness.Pair(harness.Estate.BuilderId, "Agent laptop");
        RuntimeConnection desktop = harness.Pair(harness.Estate.BuilderId, "Agent desktop");
        DeviceRequestSigner signer = harness.Signer(agent);
        using McpWire client = NetworkMcp.Wire(harness, agent);
        using McpWire other = NetworkMcp.Wire(harness, desktop);
        await client.InitializeAsync();
        await other.InitializeAsync();
        int records = harness.Estate.Load().Records.Count;
        JsonObject arguments = NetworkMcp.Args(("assignment", assignment), ("workspace", "widget repository"), ("note", "Build it."));

        // A write in one call is a tool error, and writes nothing.
        JsonObject unsigned = await client.CallAsync("work_attempt", (JsonObject)arguments.DeepClone());
        Assert.True(unsigned["isError"]!.GetValue<bool>());
        Assert.Contains("two calls", McpWire.Text(unsigned), StringComparison.Ordinal);

        // Preparing writes nothing and returns the exact record, for this device's principal, to sign.
        JsonObject prepared = await client.CallAsync("work_attempt", (JsonObject)arguments.DeepClone(), new JsonObject { ["vericairn/prepare"] = true });
        Assert.False(prepared["isError"]!.GetValue<bool>(), McpWire.Text(prepared));
        JsonObject plan = prepared["_meta"]!["vericairn/plan"]!["prepared"]!.AsObject();
        Assert.Equal(harness.Estate.BuilderId, plan["record"]!["by"]!.GetValue<string>());
        Assert.Equal("attempt", plan["record"]!["type"]!.GetValue<string>());
        Assert.Equal(records, harness.Estate.Load().Records.Count);
        Assert.Empty(LocalNetworkLedger.Read(Path.GetDirectoryName(DaemonHost.EndpointFilePath(harness.Estate.Root))!));
        string id = plan["plan"]!.GetValue<string>();
        string hash = plan["hash"]!.GetValue<string>();

        // Both steps at once are refused; another device cannot sign or spend this device's plan.
        JsonObject both = await client.CallAsync("work_attempt", (JsonObject)arguments.DeepClone(), new JsonObject { ["vericairn/prepare"] = true, ["vericairn/plan"] = id, ["vericairn/signature"] = "x" });
        Assert.True(both["isError"]!.GetValue<bool>());
        JsonObject foreign = await other.CallAsync("work_attempt", (JsonObject)arguments.DeepClone(), new JsonObject { ["vericairn/plan"] = id, ["vericairn/signature"] = McpWire.Sign(harness.Signer(desktop), hash) });
        Assert.Contains("another device", McpWire.Text(foreign), StringComparison.Ordinal);

        // Other arguments do not yield the record signed: refused, and the plan is spent whatever happens.
        JsonObject changed = (JsonObject)arguments.DeepClone();
        changed["note"] = "Something else.";
        JsonObject differs = await client.CallAsync("work_attempt", changed, new JsonObject { ["vericairn/plan"] = id, ["vericairn/signature"] = McpWire.Sign(signer, hash) });
        Assert.True(differs["isError"]!.GetValue<bool>());
        Assert.Contains("differs from the one its device signed", McpWire.Text(differs), StringComparison.Ordinal);
        JsonObject spent = await client.CallAsync("work_attempt", (JsonObject)arguments.DeepClone(), new JsonObject { ["vericairn/plan"] = id, ["vericairn/signature"] = McpWire.Sign(signer, hash) });
        Assert.Contains("No prepared record waits", McpWire.Text(spent), StringComparison.Ordinal);

        // A signature over another hash writes nothing.
        JsonObject again = await client.CallAsync("work_attempt", (JsonObject)arguments.DeepClone(), new JsonObject { ["vericairn/prepare"] = true });
        JsonObject next = again["_meta"]!["vericairn/plan"]!["prepared"]!.AsObject();
        JsonObject wrong = await client.CallAsync("work_attempt", (JsonObject)arguments.DeepClone(), new JsonObject { ["vericairn/plan"] = next["plan"]!.GetValue<string>(), ["vericairn/signature"] = McpWire.Sign(signer, "sha256:" + new string('0', 64)) });
        Assert.Contains("does not verify", McpWire.Text(wrong), StringComparison.Ordinal);
        Assert.Equal(records, harness.Estate.Load().Records.Count);

        // Prepared again and signed, it is written with this device's signature alone.
        WriteOutcome written = McpWire.Outcome(await client.WriteAsync("work_attempt", arguments, signer));
        Assert.True(written.Ok, written.Problem?.Message);
        Assert.Equal(agent.Device, Assert.Single(harness.Estate.Load().ById[written.Written!.Id].Signatures).Device);
        Assert.Equal(records + 1, harness.Estate.Load().Records.Count);
        Assert.Equal(0, harness.Host.Service.Check().Errors);
    }

    [Fact]
    public async Task APreparedRecordLapsesUnsignedAfterTenMinutes()
    {
        (LocalNetworkHarness harness, string assignment) = await NetworkMcp.StartAsync();
        await using LocalNetworkHarness owned = harness;
        RuntimeConnection agent = harness.Pair(harness.Estate.BuilderId, "Agent laptop");
        using McpWire client = NetworkMcp.Wire(harness, agent);
        await client.InitializeAsync();
        var clock = new MovableClock(DateTimeOffset.UtcNow);
        harness.Host.Service.PreparedClock = clock;
        JsonObject arguments = NetworkMcp.Args(("assignment", assignment), ("workspace", "widget repository"), ("note", "Build it."));
        JsonObject prepared = await client.CallAsync("work_attempt", (JsonObject)arguments.DeepClone(), new JsonObject { ["vericairn/prepare"] = true });
        JsonObject plan = prepared["_meta"]!["vericairn/plan"]!["prepared"]!.AsObject();
        int records = harness.Estate.Load().Records.Count;

        clock.Now += TimeSpan.FromMinutes(11);
        JsonObject lapsed = await client.CallAsync("work_attempt", (JsonObject)arguments.DeepClone(), new JsonObject
        {
            ["vericairn/plan"] = plan["plan"]!.GetValue<string>(), ["vericairn/signature"] = McpWire.Sign(harness.Signer(agent), plan["hash"]!.GetValue<string>()),
        });
        Assert.True(lapsed["isError"]!.GetValue<bool>());
        Assert.Contains("lapsed unsigned", McpWire.Text(lapsed), StringComparison.Ordinal);
        Assert.Equal(records, harness.Estate.Load().Records.Count);
    }

    [Fact]
    public async Task OnLoopbackAWriteIsKeptInTheLedgerAndLosingTheProfileEndsTheStream()
    {
        (LocalNetworkHarness harness, string assignment) = await NetworkMcp.StartAsync();
        await using LocalNetworkHarness owned = harness;
        WriteOutcome created = harness.Socket.DeviceCreate(new DeviceCreateRequest(harness.Estate.BuilderId, "Agent here", "file", ["embedded", "loopback"]));
        Assert.True(created.Ok, created.Problem?.Message);
        string device = created.Written!.Id;
        Assert.True(harness.Socket.DeviceAdmit(new AdmitRequest(device, harness.Estate.OperatorId, "The agent's device here.")).Ok);
        DeviceRequestSigner signer = DeviceRequestSigner.Find(device)!;
        using var client = new McpWire(VericairnClient.SignedHttp(harness.Host.Endpoint, signer));
        await client.InitializeAsync();

        // A network write on loopback names the device that asked in local-network-writes.jsonl, as one on the local network does.
        WriteOutcome written = McpWire.Outcome(await client.WriteAsync("work_attempt", NetworkMcp.Args(("assignment", assignment), ("workspace", "widget repository"), ("note", "Build it.")), signer));
        Assert.True(written.Ok, written.Problem?.Message);
        LocalNetworkWrite kept = Assert.Single(LocalNetworkLedger.Read(Path.GetDirectoryName(DaemonHost.EndpointFilePath(harness.Estate.Root))!));
        Assert.Equal((device, harness.Estate.BuilderId, "POST /v1/work/attempt", written.Written!.Id, "written"), (kept.Device, kept.Principal, kept.Operation, kept.Record, kept.State));

        // The server stream is bound to the loopback profile: withdrawing it ends the stream at once.
        using HttpResponseMessage stream = await client.OpenStreamAsync();
        Assert.Equal(HttpStatusCode.OK, stream.StatusCode);
        using var reader = new StreamReader(await stream.Content.ReadAsStreamAsync(TestContext.Current.CancellationToken));
        Assert.NotNull(await McpWire.ReadUntilAsync(reader, line => line.StartsWith(':')));
        RecordResponse record = harness.Socket.Record(device)!;
        JsonObject body = JsonNode.Parse(record.Envelope.GetProperty("body").GetRawText())!.AsObject();
        body["profiles"] = new JsonArray("embedded");
        using System.Text.Json.JsonDocument changed = System.Text.Json.JsonDocument.Parse(body.ToJsonString());
        Assert.True(harness.Socket.NewRecord(new NewRecordRequest("device", harness.Estate.BuilderId, changed.RootElement, Supersedes: device)).Ok);
        Assert.Null(await McpWire.ReadUntilAsync(reader, line => line.StartsWith("data:", StringComparison.Ordinal)));
        Assert.Equal(0, harness.Host.Mcp.Sessions.CountFor(device));
    }

    [Fact]
    public async Task AFailingRouteAnswersAGenericProblemAndNoMachinePath()
    {
        await using LocalNetworkHarness harness = await LocalNetworkHarness.StartAsync(mcp: true);
        var failing = new Microsoft.AspNetCore.Routing.RouteEndpoint(_ => throw new IOException("Cannot read /private/estate/.vericairn/live.db"),
            Microsoft.AspNetCore.Routing.Patterns.RoutePatternFactory.Parse("/v1/status"), 0,
            new Microsoft.AspNetCore.Http.EndpointMetadataCollection(new Microsoft.AspNetCore.Routing.HttpMethodMetadata(["GET"])), "failing");
        var operations = new McpOperations([failing]);
        var device = new AuthenticatedDevice("01DEVICE", harness.Estate.BuilderId, "key", LocalNetwork: true);
        using var http = new HttpClient(operations.Handler(harness.Host, new Microsoft.AspNetCore.Http.DefaultHttpContext(), device, Vericairn.Runtime.Service.DeviceSigning.Read, CancellationToken.None))
            { BaseAddress = new Uri("http://vericairn-mcp/") };
        using HttpResponseMessage answer = await http.GetAsync(new Uri("v1/status", UriKind.Relative), TestContext.Current.CancellationToken);
        string text = await answer.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.InternalServerError, answer.StatusCode);
        Assert.Contains("failed on the runtime", text, StringComparison.Ordinal);
        Assert.DoesNotContain("/private/estate", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheServerStreamCarriesItsSessionsMessagesToItsDeviceAlone()
    {
        (LocalNetworkHarness harness, _) = await NetworkMcp.StartAsync();
        await using LocalNetworkHarness owned = harness;
        RuntimeConnection agent = harness.Pair(harness.Estate.BuilderId, "Agent laptop");
        RuntimeConnection desktop = harness.Pair(harness.Estate.BuilderId, "Agent desktop");
        using McpWire client = NetworkMcp.Wire(harness, agent);
        using McpWire other = NetworkMcp.Wire(harness, desktop);
        await client.InitializeAsync();
        await other.InitializeAsync();
        var opened = new TaskCompletionSource<McpSession>(TaskCreationOptions.RunContinuationsAsynchronously);
        harness.Host.Mcp.StreamOpened = session => opened.TrySetResult(session);

        using HttpResponseMessage stream = await client.OpenStreamAsync();
        Assert.Equal(HttpStatusCode.OK, stream.StatusCode);
        Assert.Equal("text/event-stream", stream.Content.Headers.ContentType!.MediaType);
        McpSession bound = await opened.Task.WaitAsync(TestTiming.HangTimeout, TestContext.Current.CancellationToken);
        Assert.Equal((agent.Device, harness.Estate.BuilderId, "local-network"), (bound.Device, bound.Principal, bound.Profile));
        using var reader = new StreamReader(await stream.Content.ReadAsStreamAsync(TestContext.Current.CancellationToken));

        // A message for the session reaches its stream; another device's session, of the same principal, has none of it.
        Assert.True(bound.Outbox.Writer.TryWrite(new JsonObject { ["jsonrpc"] = "2.0", ["method"] = "notifications/message", ["params"] = new JsonObject { ["data"] = "for the laptop" } }));
        string? delivered = await McpWire.ReadUntilAsync(reader, line => line.StartsWith("data:", StringComparison.Ordinal));
        Assert.Contains("for the laptop", delivered, StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.NotFound, (await other.OpenStreamAsync(session: client.Session)).StatusCode);

        // DELETE ends the session, and its stream with it.
        Assert.Equal(HttpStatusCode.NoContent, await client.DeleteAsync());
        Assert.Null(await McpWire.ReadUntilAsync(reader, line => line.StartsWith("data:", StringComparison.Ordinal)));
        harness.Host.Mcp.StreamOpened = null;
    }

    [Fact]
    public async Task OnLoopbackADeviceNeedsTheLoopbackProfile()
    {
        await using LocalNetworkHarness harness = await LocalNetworkHarness.StartAsync(mcp: true);
        string agent = harness.Estate.BuilderId;
        string permitted = Create(harness, agent, "Agent here", ["embedded", "loopback"]);
        string embedded = Create(harness, agent, "Agent embedded", ["embedded"]);

        using (var client = new McpWire(VericairnClient.SignedHttp(harness.Host.Endpoint, DeviceRequestSigner.Find(permitted)!)))
        {
            await client.InitializeAsync();
            Assert.False((await client.CallAsync("estate_status"))["isError"]!.GetValue<bool>());
            Assert.Equal(1, harness.Host.Mcp.Sessions.CountFor(permitted));
        }

        using var refused = new McpWire(VericairnClient.SignedHttp(harness.Host.Endpoint, DeviceRequestSigner.Find(embedded)!));
        (HttpStatusCode status, _, string text, _) = await refused.PostAsync(McpWire.Request("initialize", new JsonObject { ["protocolVersion"] = "2025-06-18" }));
        Assert.Equal(HttpStatusCode.Unauthorized, status);
        Assert.Contains("loopback profile", text, StringComparison.Ordinal);

        static string Create(LocalNetworkHarness harness, string principal, string name, string[] profiles)
        {
            WriteOutcome device = harness.Socket.DeviceCreate(new DeviceCreateRequest(principal, name, "file", profiles));
            Assert.True(device.Ok, device.Problem?.Message);
            Assert.True(harness.Socket.DeviceAdmit(new AdmitRequest(device.Written!.Id, harness.Estate.OperatorId, "The agent's device here.")).Ok);
            return device.Written.Id;
        }
    }
}
