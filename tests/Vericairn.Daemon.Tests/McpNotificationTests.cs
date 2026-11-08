// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Channels;
using Vericairn.Mcp;
using Vericairn.Protocol;
using Vericairn.Records.Model;
using Vericairn.Sdk;
using RequestData = Vericairn.Records.Tests.OperatorRequestTests;

namespace Vericairn.Daemon.Tests;

/// <summary>
/// MCP-05's exit question: an agent session hears changes to what it subscribed to, through notifications over standard input and
/// output or on its server stream, or through events_read, without polling and without operator-request events; and it hears nothing
/// its principal may not read, nothing after its device stops standing, and nothing of another session's subscriptions. Waits are
/// bounded by the hang ceiling; the delivery tests measure time, and the quiet windows only confirm that nothing came.
/// </summary>
[Collection("daemon")]
public sealed class McpNotificationTests
{
    private static readonly TimeSpan Quiet = TimeSpan.FromSeconds(2);

    [Fact]
    public async Task OverStdioASubscribedResourceHearsItsChangeWithinSecondsAndUnsubscribingStopsIt()
    {
        (LocalNetworkHarness harness, _) = await NetworkMcp.StartAsync(mcp: false);
        await using LocalNetworkHarness owned = harness;
        string builder = harness.Estate.BuilderId;
        RecordResponse order = Order(harness);
        string resource = AddResource(harness);
        await using McpLines session = Stdio(harness, builder);
        JsonObject capabilities = (await session.InitializeAsync())["result"]!["capabilities"]!.AsObject();
        Assert.True(capabilities["resources"]!["subscribe"]!.GetValue<bool>());
        Assert.True(capabilities["resources"]!["listChanged"]!.GetValue<bool>());
        string brief = Uri(harness, "brief", order.Id);
        string record = Uri(harness, "record", resource);
        Assert.NotNull((await session.RequestAsync("resources/subscribe", new JsonObject { ["uri"] = brief }))["result"]);
        Assert.NotNull((await session.RequestAsync("resources/subscribe", new JsonObject { ["uri"] = record }))["result"]);

        // A record about the work order changes its brief: the session hears it within seconds, and the write changes the listing.
        var watch = Stopwatch.StartNew();
        Verify(harness, builder, order);
        await session.NotificationAsync(n => IsUpdated(n, brief));
        TimeSpan latency = watch.Elapsed;
        Assert.True(latency < TimeSpan.FromSeconds(15), $"The notification arrived {latency.TotalSeconds:F1} seconds after the write.");
        await session.NotificationAsync(IsListChanged);
        Assert.DoesNotContain(session.Notifications, n => IsUpdated(n, record));

        // Unsubscribed, the brief is told nothing more, while the record it still follows is told of its revision.
        Assert.NotNull((await session.RequestAsync("resources/unsubscribe", new JsonObject { ["uri"] = brief }))["result"]);
        int mark = session.Notifications.Count;
        await Task.Delay(McpResourceWatch.UpdateInterval, TestContext.Current.CancellationToken);
        Verify(harness, builder, order);
        Revise(harness, resource);
        await session.NotificationAsync(n => IsUpdated(n, record), mark);
        await Task.Delay(Quiet, TestContext.Current.CancellationToken);
        Assert.DoesNotContain(session.Notifications.Skip(mark), n => IsUpdated(n, brief));
    }

    [Fact]
    public async Task OnTheServerStreamASubscribedBriefHearsItsChangeWithinSecondsAndOnlyItsSessionHearsIt()
    {
        (LocalNetworkHarness harness, string assignment) = await NetworkMcp.StartAsync();
        await using LocalNetworkHarness owned = harness;
        string builder = harness.Estate.BuilderId;
        RecordResponse order = Order(harness);
        using McpWire laptop = NetworkMcp.Wire(harness, harness.Pair(builder, "Agent laptop"));
        using McpWire desktop = NetworkMcp.Wire(harness, harness.Pair(builder, "Agent desktop"));
        JsonObject capabilities = (await laptop.InitializeAsync())["result"]!["capabilities"]!.AsObject();
        Assert.True(capabilities["resources"]!["subscribe"]!.GetValue<bool>());
        Assert.True(capabilities["resources"]!["listChanged"]!.GetValue<bool>());
        await desktop.InitializeAsync();

        // The laptop subscribes before it opens its stream: the stream starts where the subscription did, so nothing between is lost.
        string brief = Uri(harness, "brief", order.Id);
        Assert.NotNull((await laptop.PostAsync(McpWire.Request("resources/subscribe", new JsonObject { ["uri"] = brief }))).Body!["result"]);
        await using var laptopStream = new McpStream(await laptop.OpenStreamAsync());
        await using var desktopStream = new McpStream(await desktop.OpenStreamAsync());
        Assert.Equal(HttpStatusCode.NotFound, (await desktop.OpenStreamAsync(session: laptop.Session)).StatusCode);

        var watch = Stopwatch.StartNew();
        Assert.True(harness.Socket.Attempt(new AttemptRequest(assignment, builder, "widget repository", "Build it.")).Ok);
        await laptopStream.NextAsync(n => IsUpdated(n, brief));
        TimeSpan latency = watch.Elapsed;
        Assert.True(latency < TimeSpan.FromSeconds(15), $"The notification arrived {latency.TotalSeconds:F1} seconds after the write.");
        await laptopStream.NextAsync(IsListChanged);

        // The desktop, the same principal's other device, hears the write but nothing of the laptop session's subscription.
        await desktopStream.NextAsync(IsListChanged);
        await Task.Delay(Quiet, TestContext.Current.CancellationToken);
        Assert.DoesNotContain(desktopStream.Received, n => n["method"]!.GetValue<string>() == McpEvents.UpdatedMethod);

        // A stream opened again resumes where the last one ended: what changed while none was open is heard when one opens.
        await laptopStream.DisposeAsync();
        await WaitUntilAsync(() => harness.Host.Mcp.Sessions.Named(laptop.Session!) is { Streaming: false });
        await Task.Delay(McpResourceWatch.UpdateInterval, TestContext.Current.CancellationToken);
        Verify(harness, harness.Estate.OperatorId, order);
        await Task.Delay(Quiet, TestContext.Current.CancellationToken);
        await using var reopened = new McpStream(await laptop.OpenStreamAsync());
        await reopened.NextAsync(n => IsUpdated(n, brief));

        // Unsubscribing from the network session stops its notifications too.
        Assert.NotNull((await laptop.PostAsync(McpWire.Request("resources/unsubscribe", new JsonObject { ["uri"] = brief }))).Body!["result"]);
        int mark = reopened.Received.Count;
        await Task.Delay(McpResourceWatch.UpdateInterval, TestContext.Current.CancellationToken);
        Verify(harness, harness.Estate.OperatorId, order);
        await reopened.NextAsync(IsListChanged, mark);
        await Task.Delay(Quiet, TestContext.Current.CancellationToken);
        Assert.DoesNotContain(reopened.Received.Skip(mark), n => IsUpdated(n, brief));
    }

    [Fact]
    public async Task AChangeThePrincipalMayNotReadIsNeitherToldNorCountedNorTimed()
    {
        (LocalNetworkHarness harness, string assignment) = await NetworkMcp.StartAsync();
        await using LocalNetworkHarness owned = harness;
        string builder = harness.Estate.BuilderId;
        RecordResponse order = Order(harness);
        string brief = Uri(harness, "brief", order.Id);
        using McpWire agent = NetworkMcp.Wire(harness, harness.Pair(builder, "Agent laptop"));
        await agent.InitializeAsync();
        Assert.NotNull((await agent.PostAsync(McpWire.Request("resources/subscribe", new JsonObject { ["uri"] = brief }))).Body!["result"]);
        await using var stream = new McpStream(await agent.OpenStreamAsync());
        await using McpLines stdio = Stdio(harness, builder);
        await stdio.InitializeAsync();
        Assert.NotNull((await stdio.RequestAsync("resources/subscribe", new JsonObject { ["uri"] = brief }))["result"]);
        string before = EventsRead(await agent.CallAsync("events_read")).Cursor;

        // A narrower audience than today's: the agent's principal may read no verification, so one about the work order is withheld.
        harness.Host.Service.EventReadRule = (principal, reference) => !(principal == builder && reference.Type == "verification");
        string operatorCursor = (await harness.Socket.EventsAsync(filter: new EventFilter(Kinds: ["record"]), wait: 0, cancellationToken: TestContext.Current.CancellationToken)).Cursor;
        string hidden = Verify(harness, harness.Estate.OperatorId, order);
        EventsResponse seen = await harness.Socket.EventsAsync(after: operatorCursor, filter: new EventFilter(Kinds: ["record"]), wait: EventVocabulary.MaxWaitSeconds, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Contains(seen.Events, e => e.Subject.Id == hidden);

        // Neither the stream nor the stdio session says anything of it: no update, no list change, nothing at all.
        await Task.Delay(Quiet, TestContext.Current.CancellationToken);
        Assert.Empty(stream.Received);
        Assert.Empty(stdio.Notifications);

        // events_read neither returns nor counts it, and a wait for it runs its full time as if nothing had happened.
        var waited = Stopwatch.StartNew();
        EventsResponse withheld = EventsRead(await agent.CallAsync("events_read", NetworkMcp.Args(("after", before), ("wait", 2))));
        Assert.Empty(withheld.Events);
        Assert.True(waited.Elapsed >= TimeSpan.FromSeconds(1.5), "A withheld event must not end a wait early.");
        Assert.True(waited.Elapsed < TimeSpan.FromSeconds(3.5), $"A wait of two seconds ended after {waited.Elapsed.TotalSeconds:F1}.");
        Assert.NotEqual(before, withheld.Cursor);

        // A change it may read is told at once on both, its listing change too, the withheld one having moved no interval.
        Assert.True(harness.Socket.Attempt(new AttemptRequest(assignment, builder, "widget repository", "Build it.")).Ok);
        await stream.NextAsync(n => IsUpdated(n, brief));
        await stream.NextAsync(IsListChanged);
        await stdio.NotificationAsync(n => IsUpdated(n, brief));
        await stdio.NotificationAsync(IsListChanged);
        EventsResponse visible = EventsRead(await agent.CallAsync("events_read", NetworkMcp.Args(("after", withheld.Cursor), ("kinds", new JsonArray("record")), ("wait", EventVocabulary.MaxWaitSeconds))));
        Assert.Equal(["attempt"], visible.Events.Select(e => e.Subject.Type));
    }

    [Fact]
    public async Task OperatorRequestEventsReachNoNotificationAndNoEventsRead()
    {
        (LocalNetworkHarness harness, _) = await NetworkMcp.StartAsync(recordFormat: 3);
        await using LocalNetworkHarness owned = harness;
        string builder = harness.Estate.BuilderId;
        RecordResponse order = Order(harness);
        string brief = Uri(harness, "brief", order.Id);
        using McpWire agent = NetworkMcp.Wire(harness, harness.Pair(builder, "Agent laptop"));
        await agent.InitializeAsync();
        Assert.NotNull((await agent.PostAsync(McpWire.Request("resources/subscribe", new JsonObject { ["uri"] = brief }))).Body!["result"]);
        await using var stream = new McpStream(await agent.OpenStreamAsync());
        await using McpLines stdio = Stdio(harness, harness.Estate.OperatorId);
        await stdio.InitializeAsync();
        Assert.NotNull((await stdio.RequestAsync("resources/subscribe", new JsonObject { ["uri"] = brief }))["result"]);
        string agentCursor = EventsRead(await agent.CallAsync("events_read")).Cursor;
        string operatorCursor = EventsRead(await stdio.CallAsync("events_read")).Cursor;
        string journalCursor = (await harness.Socket.EventsAsync(wait: 0, cancellationToken: TestContext.Current.CancellationToken)).Cursor;

        // The operator follows requests over the API, so the journal derives request events beside the records.
        await using var requests = new EventFollower(token => harness.Socket.StreamEventsAsync(filter: new EventFilter(Kinds: ["request"]), cancellationToken: token));
        Assert.Null((await requests.NextAsync()).Event);

        // An operator request about the work order, and its closing: the operator queue's own records and derived states.
        WriteOutcome request = harness.Socket.OperatorRequestWrite(new OperatorRequestWriteRequest(builder, JsonSerializer.SerializeToElement(RequestData.Body(harness.Estate)), Subject(order)));
        Assert.True(request.Ok, request.Problem?.Message + " " + string.Join("; ", request.Problem?.Findings.Select(f => f.Code + ": " + f.Message) ?? []));
        WriteOutcome closed = harness.Socket.OperatorRequestDone(request.Written!.Id, new OperatorRequestCloseRequest(harness.Estate.OperatorId, request.Written.Hash, null, "https://example.test/effect"));
        Assert.True(closed.Ok, closed.Problem?.Message);
        await WaitForAsync(harness, journalCursor, e => e.Subject.Id == closed.Written!.Id);
        Assert.Equal(request.Written.Id, (await requests.NextEventAsync()).Subject.Id);

        // No path says anything of them: neither stream nor stdio, and events_read on either returns none of them.
        await Task.Delay(Quiet, TestContext.Current.CancellationToken);
        Assert.Empty(stream.Received);
        Assert.Empty(stdio.Notifications);
        foreach (EventsResponse read in new[] { EventsRead(await agent.CallAsync("events_read", NetworkMcp.Args(("after", agentCursor)))), EventsRead(await stdio.CallAsync("events_read", NetworkMcp.Args(("after", operatorCursor)))),
            EventsRead(await stdio.CallAsync("events_read", NetworkMcp.Args(("after", operatorCursor), ("categories", new JsonArray("decision-needed", "information"))))) })
        {
            Assert.DoesNotContain(read.Events, McpEvents.OperatorRequest);
            Assert.DoesNotContain(read.Events, e => e.Subject.Id == request.Written.Id || e.Subject.Id == closed.Written!.Id);
        }

        // The journal holds them for the API: the absence above is MCP's, not a missing event.
        EventsResponse journal = await harness.Socket.EventsAsync(after: journalCursor, wait: 0, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Contains(journal.Events, e => e.Subject.Id == request.Written.Id);
        Assert.Contains(journal.Events, e => e.Subject.Id == closed.Written!.Id);

        // Asked for by kind or type they are refused, and an operator request is not subscribable.
        Assert.True((await agent.CallAsync("events_read", NetworkMcp.Args(("kinds", new JsonArray("request")))))["isError"]!.GetValue<bool>());
        Assert.True((await stdio.CallAsync("events_read", NetworkMcp.Args(("types", new JsonArray("operator-request")))))["isError"]!.GetValue<bool>());
        JsonObject refused = (await agent.PostAsync(McpWire.Request("resources/subscribe", new JsonObject { ["uri"] = Uri(harness, "record", request.Written.Id) }))).Body!;
        Assert.Equal(-32602, refused["error"]!["code"]!.GetValue<int>());
        Assert.Contains("operator queue", refused["error"]!["message"]!.GetValue<string>(), StringComparison.Ordinal);

        // A change of the brief that is no operator request is told on both, so the silence above was no failure to deliver.
        Verify(harness, harness.Estate.OperatorId, order);
        await stream.NextAsync(n => IsUpdated(n, brief));
        await stdio.NotificationAsync(n => IsUpdated(n, brief));
    }

    [Fact]
    public async Task RevokingTheDeviceEndsItsStreamBeforeAnyNotification()
    {
        (LocalNetworkHarness harness, _) = await NetworkMcp.StartAsync();
        await using LocalNetworkHarness owned = harness;
        RecordResponse order = Order(harness);
        string brief = Uri(harness, "brief", order.Id);
        RuntimeConnection paired = harness.Pair(harness.Estate.BuilderId, "Agent laptop");
        using McpWire agent = NetworkMcp.Wire(harness, paired);
        await agent.InitializeAsync();
        Assert.NotNull((await agent.PostAsync(McpWire.Request("resources/subscribe", new JsonObject { ["uri"] = brief }))).Body!["result"]);
        await using var stream = new McpStream(await agent.OpenStreamAsync());

        // The revocation is itself a write, and the brief changes right after it: the stream closes before telling either.
        Assert.True(harness.Socket.DeviceRevoke(new RevokeRequest(paired.Device, harness.Estate.OperatorId, "The laptop was lost.")).Ok);
        Verify(harness, harness.Estate.OperatorId, order);
        Assert.True(await TestTiming.CompletesAsync(stream.Completion, TestContext.Current.CancellationToken), "The revoked device's stream stayed open.");
        Assert.Empty(stream.Received);
        Assert.Equal(0, harness.Host.Mcp.Sessions.CountFor(paired.Device));
        Assert.Equal(HttpStatusCode.Unauthorized, (await agent.PostAsync(McpWire.Request("ping"))).Status);
    }

    [Fact]
    public async Task EventsReadCursorsAreThePrincipalsAndAttentionReachesOnlyItsPrincipal()
    {
        (LocalNetworkHarness harness, _) = await NetworkMcp.StartAsync();
        await using LocalNetworkHarness owned = harness;
        string builder = harness.Estate.BuilderId;
        string op = harness.Estate.OperatorId;
        string source = AttentionApiTests.SeedFailure(harness.Estate);
        using McpWire agent = NetworkMcp.Wire(harness, harness.Pair(builder, "Agent laptop"));
        await agent.InitializeAsync();
        await using McpLines operatorSession = Stdio(harness, op);
        await operatorSession.InitializeAsync();
        var attention = new JsonArray("attention");

        // Each principal's cursor is its own: another principal's, a malformed one and one never sealed answer reset, as the API's do.
        EventsResponse agentStart = EventsRead(await agent.CallAsync("events_read", NetworkMcp.Args(("kinds", attention.DeepClone()))));
        Assert.False(agentStart.Reset);
        Assert.True(EventsRead(await operatorSession.CallAsync("events_read", NetworkMcp.Args(("after", agentStart.Cursor)))).Reset);
        Assert.True(EventsRead(await agent.CallAsync("events_read", NetworkMcp.Args(("after", "not-a-cursor")))).Reset);
        Assert.False(EventsRead(await agent.CallAsync("events_read", NetworkMcp.Args(("after", agentStart.Cursor)))).Reset);
        EventsResponse operatorStart = EventsRead(await operatorSession.CallAsync("events_read", NetworkMcp.Args(("kinds", attention.DeepClone()))));
        Assert.True(EventsRead(await agent.CallAsync("events_read", NetworkMcp.Args(("after", operatorStart.Cursor)))).Reset);

        // The operator acknowledges a notice: the operator's events_read tells it, the agent's tells nothing and waits its time out.
        AttentionNotice notice = Assert.Single(harness.Socket.AttentionGet(op).Items, i => i.SubjectId == source);
        Assert.True(harness.Socket.AttentionAcknowledge(new(op, notice.Kind, notice.SubjectType, notice.SubjectId, notice.Revision)).Ok);
        EventsResponse acknowledged = EventsRead(await operatorSession.CallAsync("events_read", NetworkMcp.Args(("after", operatorStart.Cursor), ("kinds", attention.DeepClone()), ("wait", EventVocabulary.MaxWaitSeconds))));
        EventEnvelope own = Assert.Single(acknowledged.Events);
        Assert.Equal(("acknowledged", op), (own.Change, own.To));
        var waited = Stopwatch.StartNew();
        EventsResponse nothing = EventsRead(await agent.CallAsync("events_read", NetworkMcp.Args(("after", agentStart.Cursor), ("kinds", attention.DeepClone()), ("wait", 2))));
        Assert.Empty(nothing.Events);
        Assert.True(waited.Elapsed >= TimeSpan.FromSeconds(1.5), "Another principal's attention must not end a wait early.");

        // A session acting for no principal reads no attention at all.
        await using McpLines readOnly = Stdio(harness, null);
        await readOnly.InitializeAsync();
        JsonObject refused = await readOnly.CallAsync("events_read", NetworkMcp.Args(("kinds", attention.DeepClone())));
        Assert.True(refused["isError"]!.GetValue<bool>());

        // The batch is bounded: limit names the most returned, and the cursor then continues after the last one returned.
        string cursor = EventsRead(await operatorSession.CallAsync("events_read")).Cursor;
        string journal = (await harness.Socket.EventsAsync(wait: 0, cancellationToken: TestContext.Current.CancellationToken)).Cursor;
        AddResource(harness);
        AddResource(harness);
        string third = AddResource(harness);
        await WaitForAsync(harness, journal, e => e.Subject.Id == third);
        EventsResponse first = EventsRead(await operatorSession.CallAsync("events_read", NetworkMcp.Args(("after", cursor), ("kinds", new JsonArray("record")), ("limit", 2), ("wait", EventVocabulary.MaxWaitSeconds))));
        Assert.Equal(2, first.Events.Count);
        Assert.Equal(first.Events[^1].Cursor, first.Cursor);
        EventsResponse rest = EventsRead(await operatorSession.CallAsync("events_read", NetworkMcp.Args(("after", first.Cursor), ("kinds", new JsonArray("record")), ("wait", EventVocabulary.MaxWaitSeconds))));
        Assert.Single(rest.Events);
        Assert.True((await operatorSession.CallAsync("events_read", NetworkMcp.Args(("limit", 257))))["isError"]!.GetValue<bool>());
    }

    [Fact]
    public async Task TheBridgeWritesTheSessionsNotificationsToItsClientAsLines()
    {
        (LocalNetworkHarness harness, _) = await NetworkMcp.StartAsync();
        await using LocalNetworkHarness owned = harness;
        RecordResponse order = Order(harness);
        string brief = Uri(harness, "brief", order.Id);
        RuntimeConnection paired = harness.Pair(harness.Estate.BuilderId, "Agent laptop");
        DeviceRequestSigner signer = harness.Signer(paired);
        using HttpClient http = VericairnClient.SignedHttp(paired, signer);
        using var bridge = new McpBridge(http, paired.Principal, digest => signer.Sign(digest));
        await using (var client = new McpLines((input, output, token) => bridge.RunAsync(input, output, token)))
        {
            JsonObject capabilities = (await client.InitializeAsync())["result"]!["capabilities"]!.AsObject();
            Assert.True(capabilities["resources"]!["subscribe"]!.GetValue<bool>());

            // A session that subscribes to nothing holds no server stream, and so none of its device's stream slots.
            Assert.NotNull((await client.RequestAsync("ping"))["result"]);
            await Task.Delay(Quiet, TestContext.Current.CancellationToken);
            Assert.True(harness.Host.Mcp.Sessions.Named(bridge.Session!) is { Streaming: false });

            Assert.NotNull((await client.RequestAsync("resources/subscribe", new JsonObject { ["uri"] = brief }))["result"]);
            await WaitUntilAsync(() => harness.Host.Mcp.Sessions.Named(bridge.Session!) is { Streaming: true });
            Verify(harness, harness.Estate.OperatorId, order);
            await client.NotificationAsync(n => IsUpdated(n, brief));
            await client.NotificationAsync(IsListChanged);

            // Unsubscribed from everything, the bridge lets the stream go.
            Assert.NotNull((await client.RequestAsync("resources/unsubscribe", new JsonObject { ["uri"] = brief }))["result"]);
            await WaitUntilAsync(() => harness.Host.Mcp.Sessions.Named(bridge.Session!) is { Streaming: false });
        }

        // Its input ended: the bridge let its stream go and ended its session.
        Assert.Equal(0, harness.Host.Mcp.Sessions.Count);
    }

    [Fact]
    public async Task FiveFollowingStdioSessionsAndTheCommandLineAllWork()
    {
        (LocalNetworkHarness harness, _) = await NetworkMcp.StartAsync(mcp: false);
        await using LocalNetworkHarness owned = harness;
        string builder = harness.Estate.BuilderId;
        RecordResponse order = Order(harness);
        string brief = Uri(harness, "brief", order.Id);
        var sessions = new List<McpLines>();
        try
        {
            // Four sessions follow the journal, filling the MCP servers' own slots of the local socket.
            for (int i = 0; i < EventRoutes.McpOnLocalSocket; i++)
            {
                sessions.Add(await FollowingAsync(harness, builder, brief));
            }

            await WaitUntilAsync(() => harness.Host.EventStreams.Mcp == EventRoutes.McpOnLocalSocket);

            // A fifth still declares subscriptions and subscribes, though every MCP slot is held: it waits its turn, never refused.
            sessions.Add(await FollowingAsync(harness, builder, brief));

            // The command line keeps every one of its own: four long polls and streams of the local socket open at once.
            await using var watch = new EventFollower(token => harness.Socket.StreamEventsAsync(filter: new EventFilter(Kinds: ["record"]), cancellationToken: token));
            Assert.Null((await watch.NextAsync()).Event);
            Task<EventsResponse>[] polls = [.. Enumerable.Range(0, EventRoutes.StreamsPerDevice - 1).Select(_ => harness.Socket.EventsAsync(wait: 2, cancellationToken: TestContext.Current.CancellationToken))];
            Assert.All(await Task.WhenAll(polls), poll => Assert.Null(poll.Problem));
            Assert.Equal(EventRoutes.McpOnLocalSocket, harness.Host.EventStreams.Mcp);

            // One write is heard by all five sessions, by the command line's stream, and by a session's events_read waiting its turn.
            string before = EventsRead(await sessions[0].CallAsync("events_read")).Cursor;
            string written = Verify(harness, harness.Estate.OperatorId, order);
            Assert.Equal(written, (await watch.NextEventAsync(e => e.Subject.Id == written)).Subject.Id);
            foreach (McpLines session in sessions)
            {
                await session.NotificationAsync(n => IsUpdated(n, brief));
            }

            EventsResponse read = EventsRead(await sessions[4].CallAsync("events_read", NetworkMcp.Args(("after", before), ("kinds", new JsonArray("record")), ("wait", EventVocabulary.MaxWaitSeconds))));
            Assert.Contains(read.Events, e => e.Subject.Id == written);
        }
        finally
        {
            foreach (McpLines session in sessions)
            {
                await session.DisposeAsync();
            }
        }

        static async Task<McpLines> FollowingAsync(LocalNetworkHarness harness, string principal, string uri)
        {
            McpLines session = Stdio(harness, principal);
            JsonObject capabilities = (await session.InitializeAsync())["result"]!["capabilities"]!.AsObject();
            Assert.True(capabilities["resources"]!["subscribe"]!.GetValue<bool>());
            Assert.NotNull((await session.RequestAsync("resources/subscribe", new JsonObject { ["uri"] = uri }))["result"]);
            return session;
        }
    }

    [Fact]
    public async Task TheLocalSocketsMcpReadsTakeTurnsWithinTheirOwnBudget()
    {
        var slots = new EventStreamSlots();
        var held = new List<EventStreamSlots.McpLease>();
        for (int i = 0; i < EventRoutes.McpOnLocalSocket; i++)
        {
            held.Add((await slots.AcquireMcpAsync(TestContext.Current.CancellationToken))!);
        }

        // The command line's slots are untouched by them.
        using (IDisposable? local = slots.TryAcquire(null, EventStreamSlots.LocalSocket))
        {
            Assert.NotNull(local);
        }

        // One more waits until the oldest has held its turn, which is then asked to yield; once it lets go, the newcomer holds a slot.
        var waited = Stopwatch.StartNew();
        var yielded = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using CancellationTokenRegistration registration = held[0].Yielding.Register(() => yielded.TrySetResult());
        Task<EventStreamSlots.McpLease?> next = slots.AcquireMcpAsync(TestContext.Current.CancellationToken);
        await yielded.Task.WaitAsync(TestTiming.HangTimeout, TestContext.Current.CancellationToken);
        Assert.True(waited.Elapsed >= EventRoutes.McpTurn - TimeSpan.FromMilliseconds(500), $"The oldest yielded after {waited.Elapsed.TotalSeconds:F1} seconds.");
        Assert.False(held[1].Yielding.IsCancellationRequested);
        held[0].Dispose();
        using EventStreamSlots.McpLease? turn = await next.WaitAsync(TestTiming.HangTimeout, TestContext.Current.CancellationToken);
        Assert.NotNull(turn);
        Assert.Equal(EventRoutes.McpOnLocalSocket, slots.Mcp);

        // A wait that ends before a turn comes takes nothing.
        using var shortly = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));
        Assert.Null(await slots.AcquireMcpAsync(shortly.Token));
        foreach (EventStreamSlots.McpLease lease in held.Skip(1))
        {
            lease.Dispose();
        }

        Assert.Equal(1, slots.Mcp);
    }

    [Fact]
    public async Task TheSelectingReadSaysWhichSelectionsEachEventPasses()
    {
        (LocalNetworkHarness harness, _) = await NetworkMcp.StartAsync(mcp: false);
        await using LocalNetworkHarness owned = harness;
        RecordResponse order = Order(harness);
        var records = new EventFilter(Kinds: ["record"]);
        EventSelectResponse head = await harness.Socket.EventsSelectAsync(new EventSelectRequest(Filter: records, Wait: 0), TestContext.Current.CancellationToken);
        Assert.Empty(head.Events);
        string about = Verify(harness, harness.Estate.OperatorId, order);
        string unrelated = AddResource(harness);
        EventSelection[] selections = [new(new EventFilter(Kinds: ["record"], Subjects: [order.Id])), new(new EventFilter(Types: ["resource"])), new(new EventFilter(Types: ["resource"]), Since: head.Cursor)];
        var events = new List<(EventEnvelope Event, IReadOnlyList<int> Selected)>();
        string cursor = head.Cursor;
        while (events.Count < 2)
        {
            EventSelectResponse read = await harness.Socket.EventsSelectAsync(new EventSelectRequest(cursor, records, selections, Wait: EventVocabulary.MaxWaitSeconds), TestContext.Current.CancellationToken);
            Assert.Equal(read.Events.Count, read.Selected.Count);
            events.AddRange(read.Events.Select((e, i) => (e, read.Selected[i])));
            cursor = read.Cursor;
        }

        Assert.Equal([0], events.Single(e => e.Event.Subject.Id == about).Selected);
        Assert.Equal([1, 2], events.Single(e => e.Event.Subject.Id == unrelated).Selected);

        // A selection only narrows, a malformed one or too many are refused, and a device reads only for its own principal.
        Assert.Equal(HttpStatusCode.BadRequest, (await Assert.ThrowsAnyAsync<HttpRequestException>(() => harness.Socket.EventsSelectAsync(
            new EventSelectRequest(Selections: [.. Enumerable.Range(0, EventVocabulary.MaxSelections + 1).Select(_ => new EventSelection(records))], Wait: 0), TestContext.Current.CancellationToken))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Assert.ThrowsAnyAsync<HttpRequestException>(() => harness.Socket.EventsSelectAsync(
            new EventSelectRequest(Selections: [new(new EventFilter(Kinds: ["nothing"]))], Wait: 0), TestContext.Current.CancellationToken))).StatusCode);
        RuntimeConnection paired = harness.Pair(harness.Estate.BuilderId);
        using VericairnClient device = VericairnClient.LocalNetwork(paired, harness.Signer(paired));
        Assert.Equal(HttpStatusCode.Unauthorized, (await Assert.ThrowsAnyAsync<HttpRequestException>(() => device.EventsSelectAsync(
            new EventSelectRequest(Principal: harness.Estate.OperatorId, Wait: 0), TestContext.Current.CancellationToken))).StatusCode);
        Assert.Null((await device.EventsSelectAsync(new EventSelectRequest(Wait: 0), TestContext.Current.CancellationToken)).Problem);

        // The contract names the route, its scope and its schemas.
        Assert.True(RequestScope.Covers("POST /v1/events/select"));
        Assert.Contains(("POST", "/v1/events/select"), OpenApiDocument.Routes);
        using JsonDocument api = JsonDocument.Parse(OpenApiDocument.Json);
        JsonElement schemas = api.RootElement.GetProperty("components").GetProperty("schemas");
        Assert.True(schemas.TryGetProperty("EventSelectRequest", out _));
        Assert.True(schemas.GetProperty("EventSelectResponse").GetProperty("properties").TryGetProperty("selected", out _));
        Assert.False(typeof(IEstateApi).GetMethod(nameof(IEstateApi.EventsSelect))!.IsAbstract);
    }

    [Theory]
    [InlineData(HttpStatusCode.TooManyRequests, true)]
    [InlineData(HttpStatusCode.ServiceUnavailable, true)]
    [InlineData(HttpStatusCode.Unauthorized, false)]
    public async Task ABusyRuntimeStillDeclaresSubscriptionsAndWhatWasDeclaredHolds(HttpStatusCode status, bool declared)
    {
        var runtime = new SelectingRuntime { Status = status };
        using var client = new VericairnClient(new HttpClient(runtime) { BaseAddress = new Uri("http://fixture.invalid") });
        var watch = new McpResourceWatch(new McpApiEventSource(client, null));
        Assert.Equal(declared, await watch.AvailableAsync(TestContext.Current.CancellationToken));

        // The runtime answers differently later: the session keeps what initialization declared, and serves subscriptions only then.
        runtime.Status = declared ? HttpStatusCode.Unauthorized : HttpStatusCode.OK;
        Assert.Equal(declared, await watch.AvailableAsync(TestContext.Current.CancellationToken));
        var server = new McpServer(new ToolRegistry(client, null), "test") { Subscriptions = watch };
        JsonObject answer = await server.RespondAsync(JsonValue.Create(1), "resources/subscribe", new JsonObject { ["uri"] = "vericairn://e/record/a" }, TestContext.Current.CancellationToken);
        Assert.Equal(declared, answer["error"]?["code"]?.GetValue<int>() != -32601);
    }

    [Fact]
    public async Task ARevocationJustBeforeAHeldBackListChangeEndsTheStreamFirst()
    {
        (LocalNetworkHarness harness, _) = await NetworkMcp.StartAsync();
        await using LocalNetworkHarness owned = harness;
        RuntimeConnection paired = harness.Pair(harness.Estate.BuilderId, "Agent laptop");
        using McpWire agent = NetworkMcp.Wire(harness, paired);
        await agent.InitializeAsync();
        await using var stream = new McpStream(await agent.OpenStreamAsync());
        try
        {
            // A write is told at once; the next, within five seconds, waits for its interval.
            AddResource(harness);
            await stream.NextAsync(IsListChanged);
            string journal = (await harness.Socket.EventsAsync(wait: 0, cancellationToken: TestContext.Current.CancellationToken)).Cursor;
            string second = AddResource(harness);
            await WaitForAsync(harness, journal, e => e.Subject.Id == second);

            // The device is revoked while no derivation pass runs, so the audience a pass publishes still names it as standing; the
            // held-back list change falls due, and the stream reads the device's standing afresh and ends before sending it.
            harness.Host.Service.EventPassStarting = () => throw new InvalidOperationException("No pass runs while the test holds it.");
            Assert.True(harness.Socket.DeviceRevoke(new RevokeRequest(paired.Device, harness.Estate.OperatorId, "The laptop was lost.")).Ok);
            Assert.True(await TestTiming.CompletesAsync(stream.Completion, TestContext.Current.CancellationToken), "The revoked device's stream stayed open.");
            Assert.Single(stream.Received, IsListChanged);
            Assert.Equal(0, harness.Host.Mcp.Sessions.CountFor(paired.Device));
        }
        finally
        {
            harness.Host.Service.EventPassStarting = null;
        }
    }

    private static McpLines Stdio(LocalNetworkHarness harness, string? principal)
    {
        var client = new VericairnClient(harness.Host.Endpoint);
        var server = new McpServer(new ToolRegistry(client, principal), "test") { Subscriptions = new McpResourceWatch(new McpApiEventSource(client, principal)) };
        return new McpLines(async (input, output, token) =>
        {
            try
            {
                await server.RunAsync(input, output, null, token);
            }
            finally
            {
                client.Dispose();
            }
        });
    }

    private static RecordResponse Order(LocalNetworkHarness harness) => Assert.Single(harness.Socket.Records("work-order"));

    private static string Uri(LocalNetworkHarness harness, string kind, string key) =>
        "vericairn://" + harness.Host.Service.Session.Manifest.Estate + "/" + kind + "/" + key;

    private static string Subject(RecordResponse record) => record.Type + ":" + record.Id + ":" + record.Hash;

    private static string Verify(LocalNetworkHarness harness, string by, RecordResponse subject)
    {
        WriteOutcome written = harness.Socket.NewRecord(new NewRecordRequest("verification", by, Json("""{"method":"test","commit":"0123456789abcdef0123456789abcdef01234567","environment":{"runner":"fixture"},"role":"automation","outcome":"passed","summary":"Checked.","evidence":[],"limitations":[]}"""),
            Subject: Subject(subject)));
        Assert.True(written.Ok, written.Problem?.Message);
        return written.Written!.Id;
    }

    private static string AddResource(LocalNetworkHarness harness)
    {
        WriteOutcome written = harness.Socket.NewRecord(new NewRecordRequest("resource", harness.Estate.OperatorId, Json("""{"kind":"repository","name":"Widget","role":"source","location":{"github":"Example/widget"},"visibility":"private"}""")));
        Assert.True(written.Ok, written.Problem?.Message);
        return written.Written!.Id;
    }

    private static void Revise(LocalNetworkHarness harness, string resource)
    {
        WriteOutcome written = harness.Socket.NewRecord(new NewRecordRequest("resource", harness.Estate.OperatorId, Json("""{"kind":"repository","name":"Widget","role":"source","location":{"github":"Example/widget-renamed"},"visibility":"private"}"""), Supersedes: resource));
        Assert.True(written.Ok, written.Problem?.Message);
    }

    private static JsonElement Json(string text)
    {
        using JsonDocument document = JsonDocument.Parse(text);
        return document.RootElement.Clone();
    }

    private static EventsResponse EventsRead(JsonObject result)
    {
        Assert.False(result["isError"]!.GetValue<bool>(), McpWire.Text(result));
        return JsonSerializer.Deserialize<EventsResponse>(McpWire.Text(result), ProtocolJson.Options)!;
    }

    private static bool IsUpdated(JsonObject notification, string uri) =>
        notification["method"]?.GetValue<string>() == McpEvents.UpdatedMethod && notification["params"]?["uri"]?.GetValue<string>() == uri;

    private static bool IsListChanged(JsonObject notification) => notification["method"]?.GetValue<string>() == McpEvents.ListChangedMethod;

    /// <summary>Waits until the journal holds an event the operator reads that matches.</summary>
    private static async Task WaitForAsync(LocalNetworkHarness harness, string after, Func<EventEnvelope, bool> wanted)
    {
        string cursor = after;
        using var guard = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        guard.CancelAfter(TestTiming.HangTimeout);
        while (true)
        {
            EventsResponse read = await harness.Socket.EventsAsync(after: cursor, wait: EventVocabulary.MaxWaitSeconds, cancellationToken: guard.Token);
            if (read.Events.Any(wanted))
            {
                return;
            }

            cursor = read.Cursor;
        }
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        using var guard = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        guard.CancelAfter(TestTiming.HangTimeout);
        while (!condition())
        {
            await Task.Delay(50, guard.Token);
        }
    }
}

/// <summary>
/// Drives an MCP server's standard streams line by line, as a local client would: answers are matched to their requests, and every
/// notification between them is kept in order.
/// </summary>
internal sealed class McpLines : IAsyncDisposable
{
    private readonly Channel<string?> input = Channel.CreateUnbounded<string?>();
    private readonly Channel<string> output = Channel.CreateUnbounded<string>();
    private readonly List<JsonObject> answers = [];
    private readonly CancellationTokenSource stop = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
    private readonly Task running;
    private int next;

    public McpLines(Func<TextReader, TextWriter, CancellationToken, Task> run)
    {
        running = Task.Run(() => run(new LineReader(input), new LineWriter(output), stop.Token));
    }

    /// <summary>Every notification received so far, in order.</summary>
    public List<JsonObject> Notifications { get; } = [];

    public Task<JsonObject> InitializeAsync() => InitializeThenAsync();

    public async Task<JsonObject> RequestAsync(string method, JsonObject? parameters = null)
    {
        int id = Interlocked.Increment(ref next);
        await input.Writer.WriteAsync(new JsonObject { ["jsonrpc"] = "2.0", ["id"] = id, ["method"] = method, ["params"] = parameters ?? new JsonObject() }.ToJsonString(), TestContext.Current.CancellationToken);
        while (true)
        {
            if (answers.FirstOrDefault(a => a["id"]?.GetValue<int>() == id) is JsonObject answer)
            {
                answers.Remove(answer);
                return answer;
            }

            await ReceiveAsync();
        }
    }

    /// <summary>A tools/call's result object.</summary>
    public async Task<JsonObject> CallAsync(string tool, JsonObject? arguments = null)
    {
        JsonObject answer = await RequestAsync("tools/call", new JsonObject { ["name"] = tool, ["arguments"] = arguments ?? new JsonObject() });
        return answer["result"] as JsonObject ?? throw new InvalidOperationException("No result: " + answer.ToJsonString());
    }

    /// <summary>The first notification from <paramref name="from"/> on that passes the test, waiting for it within the hang ceiling.</summary>
    public async Task<JsonObject> NotificationAsync(Func<JsonObject, bool> wanted, int from = 0)
    {
        while (true)
        {
            if (Notifications.Skip(from).FirstOrDefault(wanted) is JsonObject found)
            {
                return found;
            }

            await ReceiveAsync();
        }
    }

    public async ValueTask DisposeAsync()
    {
        input.Writer.TryComplete();
        try
        {
            await running.WaitAsync(TestTiming.HangTimeout, TestContext.Current.CancellationToken);
        }
        finally
        {
            await stop.CancelAsync();
            stop.Dispose();
        }
    }

    private async Task<JsonObject> InitializeThenAsync()
    {
        JsonObject answer = await RequestAsync("initialize", new JsonObject
        {
            ["protocolVersion"] = "2025-06-18", ["capabilities"] = new JsonObject(), ["clientInfo"] = new JsonObject { ["name"] = "test", ["version"] = "0" },
        });
        await input.Writer.WriteAsync(new JsonObject { ["jsonrpc"] = "2.0", ["method"] = "notifications/initialized" }.ToJsonString(), TestContext.Current.CancellationToken);
        return answer;
    }

    private async Task ReceiveAsync()
    {
        string line = await output.Reader.ReadAsync(TestContext.Current.CancellationToken).AsTask().WaitAsync(TestTiming.HangTimeout, TestContext.Current.CancellationToken);
        JsonObject message = JsonNode.Parse(line)!.AsObject();
        if (message["id"] is null)
        {
            Notifications.Add(message);
        }
        else
        {
            answers.Add(message);
        }
    }

    private sealed class LineReader(Channel<string?> lines) : TextReader
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

    private sealed class LineWriter(Channel<string> lines) : TextWriter
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

/// <summary>Reads a session's server stream in the background: each message it carries, in order, and whether it ended.</summary>
internal sealed class McpStream : IAsyncDisposable
{
    private readonly HttpResponseMessage response;
    private readonly CancellationTokenSource stop = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
    private readonly Channel<JsonObject> arrived = Channel.CreateUnbounded<JsonObject>();
    private readonly List<JsonObject> received = [];
    private int disposed;

    public McpStream(HttpResponseMessage response)
    {
        this.response = response;
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Completion = Task.Run(ReadAsync);
    }

    /// <summary>Completes when the stream ends.</summary>
    public Task Completion { get; }

    /// <summary>Every message the stream carried so far.</summary>
    public IReadOnlyList<JsonObject> Received
    {
        get
        {
            while (arrived.Reader.TryRead(out JsonObject? message))
            {
                received.Add(message);
            }

            return [.. received];
        }
    }

    /// <summary>The first message from <paramref name="from"/> on that passes the test, waiting within the hang ceiling.</summary>
    public async Task<JsonObject> NextAsync(Func<JsonObject, bool> wanted, int from = 0)
    {
        using var guard = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        guard.CancelAfter(TestTiming.HangTimeout);
        while (true)
        {
            if (Received.Skip(from).FirstOrDefault(wanted) is JsonObject found)
            {
                return found;
            }

            received.Add(await arrived.Reader.ReadAsync(guard.Token));
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0)
        {
            return;
        }

        await stop.CancelAsync();
        response.Dispose();
        try
        {
            await Completion.WaitAsync(TestTiming.HangTimeout, TestContext.Current.CancellationToken);
        }
        catch (Exception exception) when (exception is OperationCanceledException or IOException or HttpRequestException or ObjectDisposedException)
        {
            // The stream was let go.
        }

        stop.Dispose();
    }

    private async Task ReadAsync()
    {
        try
        {
            using var reader = new StreamReader(await response.Content.ReadAsStreamAsync(stop.Token));
            while (await reader.ReadLineAsync(stop.Token) is string line)
            {
                if (line.StartsWith("data:", StringComparison.Ordinal))
                {
                    arrived.Writer.TryWrite(JsonNode.Parse(line[5..].Trim())!.AsObject());
                }
            }
        }
        catch (Exception exception) when (exception is OperationCanceledException or IOException or HttpRequestException or ObjectDisposedException)
        {
            // The stream ended or was let go.
        }
    }
}

/// <summary>A runtime that answers every selecting read with one status, or an empty batch when it is 200.</summary>
internal sealed class SelectingRuntime : HttpMessageHandler
{
    public HttpStatusCode Status { get; set; } = HttpStatusCode.OK;

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        string body = Status == HttpStatusCode.OK
            ? JsonSerializer.Serialize(new EventSelectResponse([], [], "cursor"), ProtocolJson.Options)
            : JsonSerializer.Serialize(new Problem(Status == HttpStatusCode.TooManyRequests ? "refused" : Status == HttpStatusCode.Unauthorized ? "denied" : "unavailable", "As the test answers.", []), ProtocolJson.Options);
        return Task.FromResult(new HttpResponseMessage(Status) { Content = new StringContent(body, Encoding.UTF8, "application/json") });
    }
}
