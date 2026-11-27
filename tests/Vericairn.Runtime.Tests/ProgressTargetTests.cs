// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Channels;
using Vericairn.Protocol;
using Vericairn.Records.Estate;
using Vericairn.Records.Model;
using Vericairn.Records.Tests;
using Vericairn.Runtime.Progress;
using Vericairn.Runtime.Providers;

namespace Vericairn.Runtime.Tests;

/// <summary>
/// From 0.9.4 (WATCH-06): a progress target kept equal to the estate's progress through the adapter its configuration chooses, with a
/// fake adapter: created once, replaced only when the text differs, refreshed and commented on only for the events its triggers name,
/// in the form its destination allows, never sending what a disclosure rule holds, and every effect offered first to the stop GOAL-15
/// supplies, with every record it carries.
/// </summary>
public sealed class ProgressTargetTests : IDisposable
{
    private const string Held = "Close the widget cache's unchecked path traversal before anyone can reach it.";

    private readonly TestEstate estate = new(recordFormat: 3);
    private readonly string directory = Directory.CreateTempSubdirectory("progress-targets-").FullName;
    private readonly FakeTarget adapter = new();
    private readonly ProviderRegistry registry = new();
    private readonly RecordEnvelope product;

    public ProgressTargetTests()
    {
        registry.Register(adapter);
        product = estate.Add("product", estate.OperatorId, """{"name":"Example","slug":"example","summary":"A product.","editions":[{"name":"Community","slug":"community","summary":"Free."}],"surfaces":[{"name":"CLI","slug":"cli","summary":"Command line."}]}""");
        Order("W-1");
    }

    public void Dispose()
    {
        estate.Dispose();
        Directory.Delete(directory, recursive: true);
    }

    private ProgressTargetStore Store => new(directory);

    [Fact]
    public void ARefreshCreatesThePlaceOnceAndReplacesItsTextOnlyWhenItDiffers()
    {
        Configure("private");
        ProgressOutcome created = Runner().Refresh("status");
        Assert.Equal(("created", "https://example.invalid/discussions/1", "private"), (created.Action, created.Url, created.Form));
        Assert.Equal(["locate", "create"], adapter.Calls);
        Assert.StartsWith("# Progress of Test Estate\n", adapter.Body, StringComparison.Ordinal);
        Assert.EndsWith("<!-- " + adapter.Specs[^1].Marker + " -->\n", adapter.Body, StringComparison.Ordinal);
        Assert.Matches("^vericairn progress-target status [0-9a-f]{16}$", adapter.Specs[^1].Marker);
        // The adapter is given its settings with each declared default, the title and the marker.
        Assert.Equal(new Dictionary<string, string> { ["place"] = "the shelf", ["shelf"] = "top" }, adapter.Specs[^1].Settings);
        Assert.Equal(("D_1", "https://example.invalid/discussions/1"), (Store.State("status").PlaceId, Store.State("status").PlaceUrl));

        // Nothing changed: the text is the same, so nothing is sent; the known place is located by its identity.
        Assert.Equal("unchanged", Runner().Refresh("status").Action);
        Assert.Equal("D_1", adapter.Specs[^1].Known);
        Assert.Equal(["locate", "create", "locate"], adapter.Calls);

        // A new work order changes the text, which replaces the place's text in place; it is never created twice.
        Order("W-2");
        ProgressOutcome replaced = Runner().Refresh("status");
        Assert.Equal("replaced", replaced.Action);
        Assert.Contains("W-2", adapter.Body, StringComparison.Ordinal);
        Assert.Equal(1, adapter.Calls.Count(c => c == "create"));
        Assert.Contains(replaced.Carried, r => r.Type == "estate");

        // A dry run locates and composes, and sends nothing.
        Order("W-3");
        ProgressOutcome dry = Runner().Refresh("status", dryRun: true);
        Assert.Equal("would-replace", dry.Action);
        Assert.Contains("W-3", dry.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("W-3", adapter.Body, StringComparison.Ordinal);
    }

    [Fact]
    public void APrivateTargetWhoseDestinationAnyoneMayReadTakesThePublicForm()
    {
        estate.Add("work-order", estate.OperatorId, $$"""{"key":"FIX-90","kind":"milestone","title":"Cache fix","intent":"{{Held}}","scope":["work"],"exitQuestion":"Done?","inputs":[],"deliverables":["work"],"disclosure":"held"}""");
        estate.Add("work-order", estate.OperatorId, $$"""{"key":"M-1","kind":"milestone","title":"First milestone","intent":"Work.","scope":["work"],"exitQuestion":"Done?","inputs":[],"deliverables":["work"]}""");
        Configure("private");
        adapter.Private = false;
        ProgressOutcome outcome = Runner().Refresh("status");
        Assert.Equal(("created", "public"), (outcome.Action, outcome.Form));
        Assert.Contains("readable beyond its own members", outcome.Detail, StringComparison.Ordinal);
        Assert.DoesNotContain("FIX-90", adapter.Body, StringComparison.Ordinal);
        Assert.Contains("First milestone", adapter.Body, StringComparison.Ordinal);
        Assert.Equal(["FIX-90"], outcome.LeftOut);

        // Only a destination its adapter reports private takes the private form.
        adapter.Private = true;
        Assert.Equal(("replaced", "private"), (Runner().Refresh("status").Action, Runner().Refresh("status").Form));
        Assert.Contains("FIX-90", adapter.Body, StringComparison.Ordinal);
    }

    [Fact]
    public void ATextADisclosureRuleHoldsIsRefusedAndNothingIsSent()
    {
        estate.Add("work-order", estate.OperatorId, $$"""{"key":"FIX-90","kind":"milestone","title":"Cache fix","intent":"{{Held}}","scope":["work"],"exitQuestion":"Done?","inputs":[],"deliverables":["work"],"disclosure":"held"}""");
        Configure("public", title: "Status of FIX-90");
        ProgressOutcome refused = Runner().Refresh("status");
        Assert.Equal("refused", refused.Action);
        Assert.Equal("The public form would carry held work (FIX-90) before the release that carries it is published; nothing was sent.", refused.Detail);
        Assert.Equal(["locate"], adapter.Calls);
        Assert.Null(adapter.Body);
    }

    [Fact]
    public void AnUncertainCreationIsLocatedBeforeAnythingIsCreatedAgain()
    {
        Configure("private");
        adapter.Uncertain = true;
        adapter.TakesEffect = true;
        ProgressOutcome confirmed = Runner().Refresh("status");
        Assert.Equal(("created", "The creation was confirmed by locating it."), (confirmed.Action, confirmed.Detail));
        Assert.Equal(["locate", "create", "locate"], adapter.Calls);
        Assert.Equal("D_1", Store.State("status").PlaceId);

        var other = new FakeTarget { Uncertain = true };
        registry.Register(other);
        Store.Remove("status");
        Configure("private");
        ProgressOutcome uncertain = Runner().Refresh("status");
        Assert.Equal("uncertain", uncertain.Action);
        Assert.Contains("The next refresh locates it before creating another.", uncertain.Detail, StringComparison.Ordinal);
        Assert.Null(Store.State("status").PlaceId);
    }

    [Fact]
    public void EveryEffectIsOfferedToTheStopFirstWithEveryRecordItCarries()
    {
        Configure("private");
        var offered = new List<ProgressEffect>();
        ProgressOutcome stopped = Runner(effect =>
        {
            offered.Add(effect);
            return "Its exposure holds and reads; a person decides.";
        }).Refresh("status");
        Assert.Equal(("stopped", "Its exposure holds and reads; a person decides."), (stopped.Action, stopped.Detail));
        Assert.Equal(["locate"], adapter.Calls);
        ProgressEffect effect = Assert.Single(offered);
        Assert.Equal(("status", "create", "private"), (effect.Target, effect.Action, effect.Form));
        Assert.Equal(stopped.Carried, effect.Carried);
        EstateModel model = estate.Load();
        Assert.Contains(effect.Carried, r => r.Id == model.Current("work-order").Single().Id);
        Assert.Contains(effect.Carried, r => r.Id == model.CurrentEstate!.Id);

        // A comment is offered too, and a stopped comment is not added.
        Assert.Equal("created", Runner().Refresh("status").Action);
        RecordEnvelope candidate = Release("0.2.0", "candidate");
        offered.Clear();
        IReadOnlyList<ProgressOutcome> outcomes = Take(Runner(e => e.Action == "comment" ? "held" : null), Event("record", candidate));
        Assert.Equal(["replaced", "stopped"], outcomes.Select(o => o.Action));
        Assert.Empty(adapter.Comments);
    }

    [Fact]
    public void OnlyTheEventsATargetsTriggersNameRefreshItOrAddAComment()
    {
        Configure("private");
        Assert.Equal("created", Runner().Refresh("status").Action);
        adapter.Calls.Clear();

        // A decision is named by no default trigger: nothing is read or sent.
        RecordEnvelope decision = estate.Add("observation", estate.OperatorId, """{"summary":"Seen.","observedAt":"2026-10-13T12:00:00Z"}""");
        Assert.Empty(Take(Runner(), Event("record", decision)));
        Assert.Empty(adapter.Calls);

        // A recorded result refreshes and comments on nothing.
        RecordEnvelope result = Worked("W-5");
        Assert.Equal(["replaced"], Take(Runner(), Event("record", result)).Select(o => o.Action));
        Assert.Empty(adapter.Comments);

        // A release's new state refreshes and comments; a revision that keeps its state does neither.
        RecordEnvelope candidate = Release("0.2.0", "candidate");
        Assert.Equal(["replaced", "commented"], Take(Runner(), Event("record", candidate)).Select(o => o.Action));
        Assert.Equal(["Example 0.2.0 is now candidate."], adapter.Comments);
        RecordEnvelope same = estate.Add("release", estate.OperatorId, ReleaseBody("0.2.0", "candidate").Replace("\"limitations\":[]", "\"limitations\":[\"Unsigned.\"]", StringComparison.Ordinal), new RecordReference("product", product.Id, null), supersedes: candidate.Id);
        Assert.Empty(Take(Runner(), Event("record", same)));

        // A merged change refreshes; another operation does not.
        RecordEnvelope merged = estate.Add("operation", estate.OperatorId, """{"provider":"example","action":"change-merge","identity":"example:merge","intent":"Merge it.","target":{"repository":"Example/example"},"status":"observed-success","evidence":[]}""");
        Assert.Equal(["unchanged"], Take(Runner(), Event("record", merged)).Select(o => o.Action));
        RecordEnvelope build = estate.Add("operation", estate.OperatorId, """{"provider":"example","action":"dispatch-build","identity":"example:build","intent":"Build it.","target":{"repository":"Example/example"},"status":"observed-success","evidence":[]}""");
        Assert.Empty(Take(Runner(), Event("record", build)));

        // In the public form a release's comment says the same, charged with the release and product whose text it carries.
        adapter.Private = false;
        RecordEnvelope next = Release("0.2.1", "candidate");
        ProgressOutcome remark = Take(Runner(), Event("record", next)).Single(o => o.Action == "commented");
        Assert.Equal(("public", "Example 0.2.1 is now candidate."), (remark.Form, remark.Text));
        Assert.Equal([next.Id, product.Id], remark.Carried.Select(r => r.Id));
        adapter.Private = true;
        Assert.Equal("replaced", Runner().Refresh("status").Action);

        // A target's own triggers replace the defaults: here a decision's record refreshes and comments, filtered by its change.
        Configure("private", refresh: ["record type=observation change=written"], comment: ["record type=observation"]);
        Assert.Empty(Take(Runner(), Event("record", candidate)));
        Assert.Equal(["commented"], Take(Runner(), Event("record", decision, change: "found")).Select(o => o.Action));
        IReadOnlyList<ProgressOutcome> named = Take(Runner(), Event("record", decision));
        Assert.Equal(["unchanged", "commented"], named.Select(o => o.Action));
        Assert.Equal($"record written: observation {decision.Id}.", named[1].Text);
    }

    [Fact]
    public void AWaitIsCommentedOnceWhenItBeginsAndNeverForWhatAlreadyWaited()
    {
        RecordEnvelope first = Ask("Ship the release?");
        Configure("private", refresh: ["none"], comment: ["ask to=operator when=waiting"]);
        // The first look refreshes, and comments on nothing that already waited.
        Assert.Equal(["created"], Runner().Take(Target(), [], look: true).Select(o => o.Action));
        Assert.Equal(["ask:" + first.Id], Store.State("status").Waiting);

        RecordEnvelope second = Ask("Publish the notes?");
        // No refresh trigger is named, so only the comment is added.
        IReadOnlyList<ProgressOutcome> outcomes = Take(Runner(), Event("ask", second, change: "raised", to: estate.OperatorId));
        Assert.Equal(["commented"], outcomes.Select(o => o.Action));
        Assert.Equal(["An ask waits on Test Operator: Publish the notes? Recommendation: Answer yes."], adapter.Comments);
        Assert.Contains(outcomes[0].Carried, r => r.Id == second.Id);
        Assert.DoesNotContain("Publish the notes?", adapter.Body, StringComparison.Ordinal);

        // Heard again, it is not new: nothing is commented twice.
        Assert.Empty(Take(Runner(), Event("ask", second, change: "changed", to: estate.OperatorId)));
        Assert.Single(adapter.Comments);

        // A wait that began while no event was heard, as after a reset or a restart, is commented on the next fresh look.
        RecordEnvelope missed = Ask("Rotate the key?");
        outcomes = Runner().Take(Target(), [], look: true);
        Assert.Equal(["replaced", "commented"], outcomes.Select(o => o.Action));
        Assert.Equal("An ask waits on Test Operator: Rotate the key? Recommendation: Answer yes.", adapter.Comments[^1]);
        Assert.Contains("ask:" + missed.Id, Store.State("status").Waiting!.Value);
        Assert.Equal(["unchanged"], Runner().Take(Target(), [], look: true).Select(o => o.Action));

        // In the public form, only that an ask waits and on whom by role, carrying no record's text.
        adapter.Private = false;
        RecordEnvelope third = Ask("Answer the reviewer?");
        outcomes = Take(Runner(), Event("ask", third, change: "raised", to: estate.OperatorId));
        Assert.Equal("An ask now waits on the operator.", adapter.Comments[^1]);
        Assert.Empty(outcomes.Single(o => o.Action == "commented").Carried);
    }

    [Fact]
    public async Task FollowingRefreshesOnceTheStreamIsOpenThenOnEachTriggerAndKeepsItsCursor()
    {
        Configure("private");
        var stream = Channel.CreateUnbounded<EventStreamItem>();
        var told = Channel.CreateUnbounded<ProgressOutcome>();
        string? after = "unset";
        EventFilter? filter = null;
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        stop.CancelAfter(TestTiming.HangTimeout);
        Task following = Runner().FollowAsync("status", (cursor, f) => { after = cursor; filter = f; return stream.Reader.ReadAllAsync(stop.Token); }, o => told.Writer.TryWrite(o), stop.Token);

        // The stream opens: the target is brought equal.
        await stream.Writer.WriteAsync(new EventStreamItem("c1"), stop.Token);
        Assert.Equal("created", (await told.Reader.ReadAsync(stop.Token)).Action);
        Assert.Null(after);
        Assert.Equal(["record", "step"], filter!.Kinds);

        // A release's new state: refreshed, then commented.
        RecordEnvelope candidate = Release("0.2.0", "candidate");
        await stream.Writer.WriteAsync(new EventStreamItem("c2", Event("record", candidate)), stop.Token);
        Assert.Equal("replaced", (await told.Reader.ReadAsync(stop.Token)).Action);
        Assert.Contains("  - release candidate 0.2.0 from commit aaaaaaaaaaaa\n", adapter.Body, StringComparison.Ordinal);
        Assert.Equal("commented", (await told.Reader.ReadAsync(stop.Token)).Action);

        // A reset: the state is read again and the target refreshed.
        Order("W-9");
        await stream.Writer.WriteAsync(new EventStreamItem("c3", Reset: true), stop.Token);
        Assert.Equal("replaced", (await told.Reader.ReadAsync(stop.Token)).Action);
        stream.Writer.Complete();
        await following;
        Assert.Equal("c3", Store.State("status").Cursor);
        Assert.Equal(["Example 0.2.0 is now candidate."], adapter.Comments);

        // Started again, it resumes after the cursor it kept.
        var again = Channel.CreateUnbounded<EventStreamItem>();
        again.Writer.Complete();
        await Runner().FollowAsync("status", (cursor, _) => { after = cursor; return again.Reader.ReadAllAsync(stop.Token); }, _ => { }, stop.Token);
        Assert.Equal("c3", after);
    }

    [Theory]
    [InlineData("before acceptance")]
    [InlineData("after acceptance")]
    [InlineData("before saving the outcome")]
    public async Task RestartDoesNotResendAnInterruptedComment(string interruption)
    {
        Configure("private");
        await Follow(new EventStreamItem("c1"));
        RecordEnvelope candidate = Release("0.2.0", "candidate");
        var item = new EventStreamItem("c2", Event("record", candidate));
        string obstruction = Path.Combine(directory, ProgressTargetStore.StateFileName + ".tmp");
        adapter.OnComment = accepted =>
        {
            if (interruption == "before saving the outcome" && accepted) Directory.CreateDirectory(obstruction);
            if (interruption == (accepted ? "after acceptance" : "before acceptance"))
                throw new InvalidOperationException("The follower stopped.");
        };

        Exception? failure = await Record.ExceptionAsync(() => Follow(item));
        Assert.True(failure is InvalidOperationException or IOException or UnauthorizedAccessException, failure?.ToString());
        int accepted = interruption == "before acceptance" ? 0 : 1;
        Assert.Equal(accepted, adapter.Comments.Count);
        ProgressTargetState state = Store.State("status");
        Assert.Equal("c1", state.Cursor);
        ProgressTargetComment pending = state.Comments[item.Event!.Key];
        Assert.Equal(("fake-target", "private", "D_1", "https://example.invalid/discussions/1", null),
            (pending.Adapter, pending.Form, pending.PlaceId, pending.PlaceUrl, pending.Outcome));
        string body = "Example 0.2.0 is now candidate.";
        Assert.Equal("sha256:" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(body))), pending.PayloadHash);
        if (Directory.Exists(obstruction)) Directory.Delete(obstruction);
        adapter.OnComment = null;

        string? resumedAfter = null;
        var outcomes = new List<ProgressOutcome>();
        await Runner().FollowAsync("status", (cursor, _) =>
        {
            resumedAfter = cursor;
            return Stream(item);
        }, outcomes.Add, TestContext.Current.CancellationToken);

        Assert.Equal("c1", resumedAfter);
        Assert.Equal(accepted, adapter.Comments.Count);
        Assert.Single(adapter.Calls, call => call == "comment");
        Assert.Equal(item.Event.Key, Assert.Single(outcomes, outcome => outcome.Action == "uncertain").CommentKey);
        ProgressTargetState resumed = Store.State("status");
        Assert.Equal("c2", resumed.Cursor);
        Assert.Equal(pending with { Outcome = "uncertain" }, resumed.Comments[item.Event.Key]);
    }

    [Fact]
    public async Task AnInterruptedWaitKeepsItsOriginalTextFormAndPlaceAfterReconfiguration()
    {
        Configure("private", refresh: ["none"], comment: ["ask to=operator when=waiting"]);
        await Follow(new EventStreamItem("c1"));
        RecordEnvelope ask = Ask("Publish the notes?");
        var item = new EventStreamItem("c2", Event("ask", ask, change: "raised", to: estate.OperatorId));
        adapter.OnComment = _ => throw new InvalidOperationException("The follower stopped.");
        await Assert.ThrowsAsync<InvalidOperationException>(() => Follow(item));
        string key = "ask:" + ask.Id;
        ProgressTargetState previous = Store.State("status");
        Assert.Empty(previous.Waiting!.Value);
        ProgressTargetComment pending = previous.Comments[key];

        Configure("public", refresh: ["none"], comment: ["ask to=operator when=waiting"]);
        adapter.Move("2");
        adapter.OnComment = null;
        await Follow(item);

        Assert.Empty(adapter.Comments);
        ProgressTargetState current = Store.State("status");
        Assert.Equal("D_2", current.PlaceId);
        Assert.Equal("c2", current.Cursor);
        Assert.Equal([key], current.Waiting);
        Assert.Equal(pending with { Outcome = "uncertain" }, Assert.Single(current.Comments).Value);
    }

    [Fact]
    public void SavedOutcomesSurviveAnInterruptedBatchAndUnattemptedCommentsCanStillBeSent()
    {
        Configure("private");
        ProgressTargetRunner runner = Runner();
        ProgressTargetRunner.Target target = Target();
        string[] versions = ["0.2.0", "0.2.1", "0.2.2", "0.2.3", "0.2.4"];
        EventEnvelope[] events = [.. versions.Select(version => Event("record", Release(version, "candidate")))];
        adapter.OnComment = accepted =>
        {
            int call = adapter.Calls.Count(action => action == "comment");
            if (call == 2) throw new ProviderRefusedException("The request was refused.");
            if (call == 3 && accepted) throw new ProviderUncertainException("The request may have taken effect.");
            if (call == 4) throw new InvalidOperationException("The follower stopped.");
        };
        Assert.Throws<InvalidOperationException>(() => runner.Take(target, events, look: true, cursor: "c2"));
        ProgressTargetState interrupted = Store.State("status");
        Assert.Null(interrupted.Cursor);
        Assert.Equal(["commented", "refused", "uncertain", null], events.Take(4).Select(e => interrupted.Comments[e.Key].Outcome));
        Assert.Equal("https://example.invalid/discussions/1#comment-1", interrupted.Comments[events[0].Key].Url);
        Assert.False(interrupted.Comments.ContainsKey(events[4].Key));

        adapter.OnComment = null;
        Assert.Equal(["unchanged", "commented"], runner.Take(target, events, look: true, cursor: "c2").Select(o => o.Action));
        Assert.Equal(["Example 0.2.0 is now candidate.", "Example 0.2.2 is now candidate.", "Example 0.2.4 is now candidate."], adapter.Comments);
        Assert.Equal("c2", Store.State("status").Cursor);
    }

    [Fact]
    public void AWaitThatEndsAndWaitsAgainIsCommentedOnAgain()
    {
        Configure("private", refresh: ["none"], comment: ["ask to=operator when=waiting"]);
        Assert.Equal(["created"], Runner().Take(Target(), [], look: true).Select(o => o.Action));
        RecordEnvelope ask = Ask("Publish the notes?");
        string key = "ask:" + ask.Id;
        Assert.Equal(["commented"], Take(Runner(), Event("ask", ask, change: "raised", to: estate.OperatorId)).Select(o => o.Action));
        Assert.Equal("commented", Store.State("status").Comments[key].Outcome);

        // Withdrawn, it no longer waits: its entry has done its work and is dropped.
        RecordEnvelope withdrawn = estate.Add("ask", estate.OperatorId, AskBody("Publish the notes?", "withdrawn"), supersedes: ask.Id);
        Assert.DoesNotContain(Runner().Take(Target(), [Event("ask", withdrawn, change: "withdrawn", to: estate.OperatorId)], look: true), o => o.Action == "commented");
        Assert.Empty(Store.State("status").Waiting!.Value);
        Assert.False(Store.State("status").Comments.ContainsKey(key));

        // Opened again, the same ask waits again under the same key, and is commented on again, once.
        RecordEnvelope reopened = estate.Add("ask", estate.OperatorId, AskBody("Publish the notes?", "open"), supersedes: withdrawn.Id);
        Assert.Equal(["commented"], Take(Runner(), Event("ask", reopened, change: "raised", to: estate.OperatorId)).Select(o => o.Action));
        Assert.Empty(Take(Runner(), Event("ask", reopened, change: "changed", to: estate.OperatorId)));
        Assert.Equal(2, adapter.Comments.Count);
        Assert.Equal(adapter.Comments[0], adapter.Comments[1]);
    }

    [Fact]
    public void OnlyACommentedOrRefusedEntryOfAWaitNoLongerListedIsDropped()
    {
        var entry = new ProgressTargetComment("fake-target", "sha256:" + new string('0', 64), "private", "D_1", "https://example.invalid/discussions/1");
        ImmutableSortedDictionary<string, ProgressTargetComment> comments = ImmutableSortedDictionary.CreateRange(StringComparer.Ordinal,
        [
            KeyValuePair.Create("ask:A", entry with { Outcome = "commented", Url = "https://example.invalid/discussions/1#comment-1" }),
            KeyValuePair.Create("ask:B", entry with { Outcome = "uncertain" }),
            KeyValuePair.Create("request:C", entry with { Outcome = "refused" }),
            KeyValuePair.Create("ask:D", entry),
            KeyValuePair.Create("step:G/review/review", entry with { Outcome = "commented", Url = "https://example.invalid/discussions/1#comment-2" }),
            KeyValuePair.Create("sha256:E", entry with { Outcome = "commented", Url = "https://example.invalid/discussions/1#comment-3" }),
        ]);

        // Only step:G is listed. An uncertain entry is never dropped, nor one with no outcome yet, a listed wait's or an event's.
        ImmutableSortedDictionary<string, ProgressTargetComment> kept = ProgressTargetRunner.Retire(comments, new HashSet<string>(["step:G/review/review"], StringComparer.Ordinal));
        Assert.Equal(["ask:B", "ask:D", "sha256:E", "step:G/review/review"], kept.Keys);
        Assert.Same(kept, ProgressTargetRunner.Retire(kept, new HashSet<string>(["step:G/review/review"], StringComparer.Ordinal)));
    }

    [Fact]
    public void AnUncertainCommentIsNeverSentAgainWhenItsWaitIsListedAgainAndAPersonIsTold()
    {
        Configure("private", refresh: ["none"], comment: ["ask to=operator when=waiting"]);
        Assert.Equal(["created"], Runner().Take(Target(), [], look: true).Select(o => o.Action));
        RecordEnvelope ask = Ask("Publish the notes?");
        string key = "ask:" + ask.Id;
        adapter.OnComment = accepted =>
        {
            if (accepted) throw new ProviderUncertainException("The request may have taken effect.");
        };
        Assert.Equal(["uncertain"], Take(Runner(), Event("ask", ask, change: "raised", to: estate.OperatorId)).Select(o => o.Action));
        adapter.OnComment = null;

        // Withdrawn and opened again, as a wait missing from one look and listed at the next is: the uncertain entry stays, nothing is sent.
        RecordEnvelope withdrawn = estate.Add("ask", estate.OperatorId, AskBody("Publish the notes?", "withdrawn"), supersedes: ask.Id);
        Runner().Take(Target(), [Event("ask", withdrawn, change: "withdrawn", to: estate.OperatorId)], look: true);
        Assert.Equal("uncertain", Store.State("status").Comments[key].Outcome);
        RecordEnvelope reopened = estate.Add("ask", estate.OperatorId, AskBody("Publish the notes?", "open"), supersedes: withdrawn.Id);
        ProgressOutcome told = Assert.Single(Take(Runner(), Event("ask", reopened, change: "raised", to: estate.OperatorId)));
        Assert.Equal(("stopped", key), (told.Action, told.CommentKey));
        Assert.Contains($"vericairn progress target resolve status {key}", told.Detail, StringComparison.Ordinal);
        Assert.Single(adapter.Comments);
        Assert.Single(adapter.Calls, call => call == "comment");

        // Once a person resolves it, the next time the ask waits it is commented on.
        Store.Update("status", state => state with { Comments = state.Comments.Remove(key) });
        RecordEnvelope again = estate.Add("ask", estate.OperatorId, AskBody("Publish the notes?", "withdrawn"), supersedes: reopened.Id);
        Runner().Take(Target(), [Event("ask", again, change: "withdrawn", to: estate.OperatorId)], look: true);
        RecordEnvelope last = estate.Add("ask", estate.OperatorId, AskBody("Publish the notes?", "open"), supersedes: again.Id);
        Assert.Equal(["commented"], Take(Runner(), Event("ask", last, change: "raised", to: estate.OperatorId)).Select(o => o.Action));
        Assert.Equal(2, adapter.Comments.Count);
    }

    [Fact]
    public async Task ASecondFollowerOfTheSameTargetIsRefusedWhileOneFollows()
    {
        Configure("private");
        var stream = Channel.CreateUnbounded<EventStreamItem>();
        var opened = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        stop.CancelAfter(TestTiming.HangTimeout);
        Task following = Runner().FollowAsync("status", (_, _) =>
        {
            opened.TrySetResult();
            return stream.Reader.ReadAllAsync(stop.Token);
        }, _ => { }, stop.Token);
        await opened.Task.WaitAsync(stop.Token);

        Assert.True(Store.Followed("status"));
        InvalidOperationException refused = await Assert.ThrowsAsync<InvalidOperationException>(() => Follow(new EventStreamItem("c1")));
        Assert.Contains("Another follower is following progress target status", refused.Message, StringComparison.Ordinal);

        stream.Writer.Complete();
        await following;
        Assert.False(Store.Followed("status"));
        await Follow(new EventStreamItem("c1"));
    }

    private Task Follow(EventStreamItem item) =>
        Runner().FollowAsync("status", (_, _) => Stream(item), _ => { }, TestContext.Current.CancellationToken);

    private static IAsyncEnumerable<EventStreamItem> Stream(EventStreamItem item)
    {
        var stream = Channel.CreateUnbounded<EventStreamItem>();
        Assert.True(stream.Writer.TryWrite(item));
        stream.Writer.Complete();
        return stream.Reader.ReadAllAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public void TriggersAreReadInTheirTextFormAndTheDefaultsAreTheDecisions()
    {
        Assert.Equal(["record type=release when=state-changed", "record type=operation when=merged", "record type=acceptance,result", "step to=person when=waiting"],
            ProgressTrigger.DefaultRefresh.Select(t => t.ToString()));
        Assert.Equal(["step to=operator when=waiting", "record type=release when=state-changed"], ProgressTrigger.DefaultComment.Select(t => t.ToString()));
        ProgressTrigger trigger = ProgressTrigger.Parse(" attention  category=decision-needed,blocked to=operator change=raised ");
        Assert.Equal("attention change=raised category=blocked,decision-needed to=operator", trigger.ToString());
        Assert.Equal(trigger, ProgressTrigger.Parse(trigger.ToString()));
        Assert.True(trigger.Admits(new EventEnvelope(EventVocabulary.Format, "k", "c", "attention", "raised", "2026-10-13T12:00:00Z", new EventReference("ask", "a"), [], null, "blocked")));
        Assert.False(trigger.Admits(new EventEnvelope(EventVocabulary.Format, "k", "c", "attention", "raised", "2026-10-13T12:00:00Z", new EventReference("ask", "a"), [], null, null)));
        foreach ((string text, string problem) in new[]
        {
            ("", "names an event kind"), ("records", "neither an event kind"), ("record when=merged when=merged", "names when once"), ("record colour=red", "not 'colour'"),
            ("step when=merged", "its kind is record"), ("record when=waiting", "step, request or ask"), ("record to=everyone", "to names one of"), ("record change=moved", "A change is one of"),
            ("record type=", "names no value"), ("attention category=urgent", "A category is one of"), ("record type=Release", "lower-case kebab-case"),
        })
            Assert.Contains(problem, Assert.Throws<FormatException>(() => ProgressTrigger.Parse(text)).Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AConfigurationIsCheckedAgainstItsAdapterAndKeptApartFromWhatTheFollowerKeeps()
    {
        ProgressTargetConfiguration Make(string name = "status", string adapterId = "fake-target", string form = "private", string? title = null, params (string, string)[] settings) =>
            new(name, adapterId, settings.ToImmutableSortedDictionary(s => s.Item1, s => s.Item2, StringComparer.Ordinal), form, title, ProgressTrigger.DefaultRefresh, ProgressTrigger.DefaultComment);
        Assert.Null(Make(settings: ("place", "here")).Problem(adapter));
        Assert.Contains("requires the setting 'place'", Make().Problem(adapter), StringComparison.Ordinal);
        Assert.Contains("takes no setting 'colour'", Make(settings: [("place", "here"), ("colour", "red")]).Problem(adapter), StringComparison.Ordinal);
        Assert.Contains("lower-case kebab-case", Make(name: "Status", settings: ("place", "here")).Problem(adapter), StringComparison.Ordinal);
        Assert.Contains("private or public", Make(form: "internal", settings: ("place", "here")).Problem(adapter), StringComparison.Ordinal);
        Assert.Contains("one line", Make(title: "Two\nlines", settings: ("place", "here")).Problem(adapter), StringComparison.Ordinal);
        Assert.Contains("nothing selects another", Make(adapterId: "elsewhere", settings: ("place", "here")).Problem(registry.ProgressTarget("elsewhere")), StringComparison.Ordinal);

        ProgressTargetConfiguration saved = Make(title: "Our progress", settings: ("place", "here"));
        Store.Save(saved);
        Store.Update("status", _ => new ProgressTargetState("D_9", "https://example.invalid/d/9", "c9", ["ask:x"]));
        ProgressTargetConfiguration read = Assert.Single(Store.Load());
        Assert.Equal((saved.Name, saved.Adapter, saved.Form, saved.Title), (read.Name, read.Adapter, read.Form, read.Title));
        Assert.Equal(saved.Settings, read.Settings);
        Assert.Equal(saved.Refresh, read.Refresh);
        Assert.Equal(saved.Comment, read.Comment);
        Assert.Equal(new ProgressTargetState("D_9", "https://example.invalid/d/9", "c9", ["ask:x"]).ToJson().ToJsonString(), Store.State("status").ToJson().ToJsonString());
        // Saving the configuration again keeps what the follower kept; removing it removes both.
        Store.Save(saved with { Form = "public" });
        Assert.Equal("c9", Store.State("status").Cursor);
        if (!OperatingSystem.IsWindows())
            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(Path.Combine(directory, ProgressTargetStore.FileName)));
        Assert.True(Store.Remove("status"));
        Assert.Empty(Store.Load());
        Assert.Null(Store.State("status").Cursor);
        Assert.False(Store.Remove("status"));
    }

    private ProgressTargetRunner Runner(Func<ProgressEffect, string?>? stop = null) => new(() => estate.Load(), registry, Store) { Stop = stop };

    private ProgressTargetRunner.Target Target() => Runner().Resolve("status").Target!;

    private static IReadOnlyList<ProgressOutcome> Take(ProgressTargetRunner runner, EventEnvelope envelope) => runner.Take(runner.Resolve("status").Target!, [envelope], look: false);

    private void Configure(string form, string? title = null, string[]? refresh = null, string[]? comment = null)
    {
        ImmutableArray<ProgressTrigger> Triggers(string[]? given, ImmutableArray<ProgressTrigger> defaults) =>
            given is null ? defaults : given is ["none"] ? [] : [.. given.Select(ProgressTrigger.Parse)];
        Store.Save(new ProgressTargetConfiguration("status", "fake-target", ImmutableSortedDictionary.CreateRange(StringComparer.Ordinal, [KeyValuePair.Create("place", "the shelf")]),
            form, title, Triggers(refresh, ProgressTrigger.DefaultRefresh), Triggers(comment, ProgressTrigger.DefaultComment)));
    }

    private static EventEnvelope Event(string kind, RecordEnvelope subject, string change = "written", string? to = null) =>
        new(EventVocabulary.Format, "sha256:" + subject.Id + change, "cursor", kind, change, "2026-10-13T12:00:00Z", new EventReference(subject.Type, subject.Id, subject.ComputedHash), [], to);

    // A milestone, which a target's text always shows.
    private RecordEnvelope Order(string key) => estate.Add("work-order", estate.OperatorId,
        $$"""{"key":"{{key}}","kind":"milestone","title":"Order {{key}}","intent":"Work.","scope":["work"],"exitQuestion":"Done?","inputs":[],"deliverables":["work"]}""");

    private RecordEnvelope Worked(string key)
    {
        RecordEnvelope order = Order(key);
        RecordEnvelope assignment = estate.Add("assignment", estate.OperatorId, $$$"""{"contributor":"{{{estate.BuilderId}}}","brief":{"outcome":"A widget.","sources":[],"permittedChanges":["the widget"],"prohibitedEffects":["no publication"],"workspace":["example repository"],"limits":[],"expectedOutputs":["widget.txt"],"reviewCriteria":["tests pass"],"recovery":"Record a result before stopping."}}""", order.Reference);
        RecordEnvelope attempt = estate.Add("attempt", estate.BuilderId, """{"startedAt":"2026-10-13T12:00:00Z","workspace":"example repository, work branch","note":"Do it."}""", assignment.Reference);
        return estate.Add("result", estate.BuilderId, """{"outcome":"completed","summary":"Done.","outputs":[],"evidence":[],"checks":[],"limitations":[],"unresolvedEffects":[],"nextStep":"none"}""", attempt.Reference);
    }

    private RecordEnvelope Ask(string question) => estate.Add("ask", estate.OperatorId, AskBody(question, "open"));

    private string AskBody(string question, string state) =>
        $$"""{"to":"{{estate.OperatorId}}","question":"{{question}}","recommendation":"Answer yes.","consequence":"Nothing moves.","state":"{{state}}"}""";

    private RecordEnvelope Release(string version, string status) =>
        estate.Add("release", estate.OperatorId, ReleaseBody(version, status), new RecordReference("product", product.Id, null));

    private static string ReleaseBody(string version, string status) =>
        $$"""{"version":"{{version}}","repository":"Example/example","commit":"{{new string('a', 40)}}","status":"{{status}}","artifacts":[{"name":"example-{{version}}-linux-x64.zip","hash":"sha256:{{new string('1', 64)}}","bytes":10,"platform":"linux-x64"}],"recordFormat":{"reads":[1,2],"writes":2},"evidence":[],"qualified":[],"signing":"unsigned developer build","limitations":[]}""";

    /// <summary>An in-memory progress target: one place, its text and comments, and every call; it reaches no service.</summary>
    internal sealed class FakeTarget : IProgressTargetAdapter
    {
        public string Adapter => "fake-target";

        public ProviderDescriptor Descriptor { get; } = new("fake-target", IProgressTargetAdapter.Contract, "implemented", ["locate", "create", "replace", "comment"],
            ["In memory; reaches no service."], [new ProviderSetting("place", "Where the text goes.", true), new ProviderSetting("shelf", "Which shelf.", false, "top")]);

        public bool Private { get; set; } = true;

        public bool Uncertain { get; set; }

        public bool TakesEffect { get; set; }

        public string? Body { get; private set; }

        public List<string> Calls { get; } = [];

        public List<string> Comments { get; } = [];

        public Action<bool>? OnComment { get; set; }

        public List<ProgressTargetSpec> Specs { get; } = [];

        private string? id;

        public ProgressTargetDestination Locate(ProgressTargetSpec target)
        {
            Calls.Add("locate");
            Specs.Add(target);
            return new(Private, id is not null && Body!.Contains("<!-- " + target.Marker + " -->", StringComparison.Ordinal) ? Place(Body) : null);
        }

        public ProgressTargetPlace Create(ProgressTargetSpec target, string body)
        {
            Calls.Add("create");
            if (Uncertain && !TakesEffect) throw new ProviderUncertainException("The request may have taken effect.");
            id = "D_1";
            Body = body;
            if (Uncertain) throw new ProviderUncertainException("The request may have taken effect.");
            return Place(null);
        }

        public ProgressTargetPlace Replace(ProgressTargetSpec target, ProgressTargetPlace place, string body)
        {
            Calls.Add("replace");
            Assert.Equal(id, place.Id);
            Body = body;
            return Place(null);
        }

        public string Comment(ProgressTargetSpec target, ProgressTargetPlace place, string body)
        {
            Calls.Add("comment");
            Assert.Equal(id, place.Id);
            OnComment?.Invoke(false);
            Comments.Add(body);
            OnComment?.Invoke(true);
            return $"{place.Url}#comment-{Comments.Count}";
        }

        public void Move(string number) => id = "D_" + number;

        private ProgressTargetPlace Place(string? body) => new(id!, $"https://example.invalid/discussions/{id![2..]}", body);
    }
}
