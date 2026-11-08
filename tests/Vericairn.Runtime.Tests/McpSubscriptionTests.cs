// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using System.Text.Json.Nodes;
using Vericairn.Mcp;
using Vericairn.Protocol;
using Vericairn.Records.Model;
using Vericairn.Records.Tests;
using Vericairn.Runtime.Events;
using Vericairn.Runtime.Service;

namespace Vericairn.Runtime.Tests;

/// <summary>
/// MCP-05's subscriptions without a transport: the notifications a round of events yields and how they are spaced, the operator
/// queue kept off them, and the journal's selectors that say which subscribed resource each event concerns.
/// </summary>
public sealed class McpSubscriptionTests : IDisposable
{
    private static readonly EventFilter Records = new(Kinds: ["record"]);

    private readonly string directory = Path.Combine(Path.GetTempPath(), "vericairn-mcp-events-" + Guid.NewGuid().ToString("N"));

    public McpSubscriptionTests() => Directory.CreateDirectory(directory);

    [Fact]
    public async Task UpdatesComeOnceASecondPerUriAndListChangesEveryFiveSeconds()
    {
        var clock = new StepClock();
        var watch = new McpResourceWatch(new HeldSource(), clock);
        await watch.SubscribeAsync("vericairn://e/record/a", Records, TestContext.Current.CancellationToken);
        await watch.SubscribeAsync("vericairn://e/record/b", Records, TestContext.Current.CancellationToken);

        // The first change of a resource and the first write are told at once.
        Assert.Equal(["updated vericairn://e/record/a", "list_changed"], Told(watch.Apply(Round(Event("record", "r1"), "vericairn://e/record/a"))));

        // Within the second, another resource is told at once; the first waits for its second to end, and the listing for its five.
        clock.Advance(TimeSpan.FromMilliseconds(400));
        Assert.Equal(["updated vericairn://e/record/b"], Told(watch.Apply(Round(Event("record", "r2"), "vericairn://e/record/a", "vericairn://e/record/b"))));
        Assert.Equal(TimeSpan.FromMilliseconds(600), watch.NextDue());
        clock.Advance(TimeSpan.FromMilliseconds(300));
        Assert.Empty(watch.Apply(Round(Event("record", "r3"), "vericairn://e/record/a")));
        Assert.Empty(watch.Due());

        // Changes inside an interval are told once, when it ends.
        clock.Advance(TimeSpan.FromMilliseconds(300));
        Assert.Equal(["updated vericairn://e/record/a"], Told(watch.Due()));
        Assert.Empty(watch.Due());
        Assert.Equal(TimeSpan.FromSeconds(4), watch.NextDue());
        clock.Advance(TimeSpan.FromSeconds(3.9));
        Assert.Empty(watch.Due());
        clock.Advance(TimeSpan.FromMilliseconds(100));
        Assert.Equal(["list_changed"], Told(watch.Due()));
        Assert.Null(watch.NextDue());

        // A change that is not a record's concerns its resource and leaves the listing as it is.
        clock.Advance(TimeSpan.FromSeconds(10));
        Assert.Equal(["updated vericairn://e/record/b"], Told(watch.Apply(Round(Event("step", "s1"), "vericairn://e/record/b"))));

        // A reset tells every subscribed resource and the listing; a round that delivered nothing tells nothing.
        clock.Advance(TimeSpan.FromSeconds(10));
        Assert.Empty(watch.Apply(McpEventRound.Empty));
        Assert.Equal(["updated vericairn://e/record/a", "updated vericairn://e/record/b", "list_changed"], Told(watch.Apply(new McpEventRound([], Reset: true))));
    }

    [Fact]
    public async Task OperatorRequestEventsConcernNothingAndARefusalTellsNothing()
    {
        var clock = new StepClock();
        var watch = new McpResourceWatch(new HeldSource(), clock);
        const string Uri = "vericairn://e/brief/W";
        await watch.SubscribeAsync(Uri, Records, TestContext.Current.CancellationToken);

        // An operator request's record, its closing, the attention it raises and a request event: none is told, not even as a write.
        Assert.Empty(watch.Apply(Round(Event("record", "q1", "operator-request"), Uri)));
        Assert.Empty(watch.Apply(Round(Event("record", "q2", "operator-request-close", new EventReference("operator-request", "q1", "sha256:1")), Uri)));
        Assert.Empty(watch.Apply(Round(Event("attention", "q1", "operator-request"), Uri)));
        Assert.Empty(watch.Apply(Round(Event("request", "q1", "operator-request"), Uri)));
        Assert.True(McpEvents.OperatorRequest(Event("record", "o1", "observation", new EventReference("operator-request", "q1", "sha256:1"))));
        Assert.Null(watch.NextDue());

        // Nothing is told after a refusal, even what a round carried with it.
        Assert.Empty(watch.Apply(new McpEventRound([new McpEvent(Event("record", "r1"), [Uri])], Refusal: "Device D is not admitted now.")));
        Assert.Null(watch.NextDue());
        Assert.Equal(["updated " + Uri, "list_changed"], Told(watch.Apply(Round(Event("record", "r2"), Uri))));
    }

    [Fact]
    public async Task UnsubscribingStopsAResourcesUpdatesAndASessionHoldsAtMostThirtyTwo()
    {
        var clock = new StepClock();
        var source = new HeldSource();
        var watch = new McpResourceWatch(source, clock);
        await watch.SubscribeAsync("vericairn://e/record/a", Records, TestContext.Current.CancellationToken);
        await watch.SubscribeAsync("vericairn://e/record/a", Records, TestContext.Current.CancellationToken);
        Assert.Equal(["vericairn://e/record/a"], watch.Subscribed);

        // A change waiting for its interval is not told once its subscription ends.
        Assert.NotEmpty(watch.Apply(Round(Event("record", "r1"), "vericairn://e/record/a")));
        Assert.Empty(watch.Apply(Round(Event("ask", "k1"), "vericairn://e/record/a")));
        Assert.True(watch.Unsubscribe("vericairn://e/record/a"));
        Assert.False(watch.Unsubscribe("vericairn://e/record/a"));
        Assert.DoesNotContain("vericairn://e/record/a", source.Keys);
        clock.Advance(TimeSpan.FromSeconds(2));
        Assert.Empty(watch.Due());

        for (int i = 0; i < McpResourceWatch.MaxSubscriptions; i++)
        {
            await watch.SubscribeAsync("vericairn://e/record/" + i, Records, TestContext.Current.CancellationToken);
        }

        await Assert.ThrowsAnyAsync<Exception>(() => watch.SubscribeAsync("vericairn://e/record/one-more", Records, TestContext.Current.CancellationToken));
        Assert.Equal(McpResourceWatch.MaxSubscriptions, watch.Subscribed.Count);
    }

    [Fact]
    public void ASelectorPassesOnlyWhatConcernsItsSubjectsAfterItOpenedAndOnlyThroughTheAudienceCheck()
    {
        using var estate = new TestEstate(3);
        using EstateService service = EstateService.Open(estate.Root);
        service.EnableEvents(directory);
        string op = estate.OperatorId;
        string builder = estate.BuilderId;
        RecordEnvelope order = estate.Add("work-order", op, """{"key":"W-1","kind":"ordinary","title":"Build the widget","intent":"Selectors.","scope":["widget"],"exitQuestion":"Does it build?","inputs":[],"deliverables":["widget"]}""");
        (EventSubscription? reader, Problem? problem) = service.OpenEvents(null, Records, builder);
        Assert.Null(problem);

        // A record about the work order before the selector opens is the reader's, and concerns no selector.
        string before = Verify(service, builder, order);
        (EventSelector? selector, problem) = service.OpenEventSelector(new EventFilter(Kinds: ["record"], Subjects: [order.Id]));
        Assert.Null(problem);
        string related = Verify(service, builder, order);
        string unrelated = Written(service.NewRecord(new NewRecordRequest("resource", op, Json("""{"kind":"repository","name":"Gadget","role":"source","location":{"github":"Example/gadget"},"visibility":"private"}"""))));

        // A record about it that the reader may not read is neither delivered nor selected.
        string hidden = Verify(service, builder, order);
        service.EventReadRule = (principal, reference) => !(principal == builder && reference.Id == hidden);
        service.DeriveEvents();
        EventBatch batch = reader!.Read(256, [selector!]);
        Assert.Equal([before, related, unrelated], batch.Events.Select(e => e.Subject.Id));
        Assert.Equal(3, batch.Selected.Count);
        Assert.Equal([false, true, false], batch.Selected.Select(s => s.Contains(0)));

        // Without selectors a read selects nothing; an invalid filter opens no selector.
        Assert.Empty(service.OpenEvents(null, Records, builder).Subscription!.Read().Selected);
        Assert.Equal("invalid", service.OpenEventSelector(new EventFilter(Kinds: ["nothing"])).Problem?.Code);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(directory, recursive: true);
        }
        catch (IOException)
        {
            // Best effort.
        }
    }

    private static string Verify(EstateService service, string by, RecordEnvelope subject) =>
        Written(service.NewRecord(new NewRecordRequest("verification", by, Json("""{"method":"test","commit":"0123456789abcdef0123456789abcdef01234567","environment":{"runner":"fixture"},"role":"automation","outcome":"passed","summary":"Checked.","evidence":[],"limitations":[]}"""),
            Subject: "work-order:" + subject.Id + ":" + subject.ComputedHash)));

    private static string Written(WriteOutcome outcome)
    {
        Assert.True(outcome.Ok, outcome.Problem?.Message);
        return outcome.Written!.Id;
    }

    private static JsonElement Json(string text)
    {
        using JsonDocument document = JsonDocument.Parse(text);
        return document.RootElement.Clone();
    }

    private static EventEnvelope Event(string kind, string id, string type = "verification", params EventReference[] sources) =>
        new(EventVocabulary.Format, "sha256:" + id, "cursor-" + id, kind, kind == "record" ? "written" : "changed", "2026-11-07T00:00:00Z", new EventReference(type, id, "sha256:" + id), sources);

    private static McpEventRound Round(EventEnvelope envelope, params string[] uris) => new([new McpEvent(envelope, uris)]);

    private static string[] Told(IReadOnlyList<JsonObject> notifications) =>
        [.. notifications.Select(n => n["method"]!.GetValue<string>() == McpEvents.UpdatedMethod ? "updated " + n["params"]!["uri"]!.GetValue<string>() : "list_changed")];

    /// <summary>A source that keeps filters and delivers nothing by itself: the tests hand the watch its rounds.</summary>
    private sealed class HeldSource : IMcpEventSource
    {
        public List<string> Keys { get; } = [];

        public Task<bool> AvailableAsync(CancellationToken cancellationToken) => Task.FromResult(true);

        public Task AddAsync(string key, EventFilter filter, CancellationToken cancellationToken)
        {
            Keys.Add(key);
            return Task.CompletedTask;
        }

        public void Remove(string key) => Keys.Remove(key);

        public Task<McpEventRound> ReadAsync(TimeSpan wait, CancellationToken cancellationToken) => Task.FromResult(McpEventRound.Empty);
    }

    private sealed class StepClock : TimeProvider
    {
        private DateTimeOffset now = new(2026, 11, 7, 0, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => now;

        public void Advance(TimeSpan by) => now += by;
    }
}
