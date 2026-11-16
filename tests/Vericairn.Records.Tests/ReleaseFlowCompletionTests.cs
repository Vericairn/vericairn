// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json.Nodes;
using Vericairn.Records.Estate;
using Vericairn.Records.Model;

namespace Vericairn.Records.Tests;

/// <summary>
/// A release run on the flow reads complete exactly when the release and its required steps are, whatever optional posts the operator
/// skips (FIX-142, decision 01M7FX7ZJGZ9QBA6P8WQWWVQKN): an optional reserved step whose request the operator dismissed settles as
/// skipped without a revision of the goal; a revision that only turns optional steps off keeps the operator's closes already recorded;
/// the documentation steps after publication count only once the publication is observed; and nothing required becomes skippable. The
/// first-party extension's X post is made optional as its revision in the estate makes it, beside the other posts. A close of a later
/// step's request counts only when made after that step was asked in the run's current shape (FIX-144, decision
/// 01M7GSDXATT5GM3X8G4W1CKHJR): skipping an optional step, by a dismissal or by a revision that only turns it off, asks the steps after
/// it from then, so a close made while they waited on it never counts. A running run keeps its configuration (FIX-146, decision
/// 01M7H25PQC4603AKDM4QKPXXA6): template defaults written after its activation change none of its steps until the sponsor's next
/// revision; a revision that changes until asks every step again; and an optional item a revision turns off settles its step from then,
/// as a step turned off does.
/// </summary>
public class ReleaseFlowCompletionTests
{
    private static readonly string[] Required = ["mcp-registry", "announce-discussions"];

    /// <summary>The listing's command for the fixture's repository, as the first-party extension gives it.</summary>
    private const string Listing = "gh run list -R " + ReleaseFlowFixture.Repository + " --workflow mcp-registry.yml --event release --limit 1";

    [Fact]
    public void ADismissedPostSettlesAsSkippedAndTheRunCompletesWithoutARevision()
    {
        using var run = new ReleaseFlowFixture(firstParty: true, extension: OptionalX);
        RecordEnvelope guide = run.Guide();
        run.Clear(guide);
        RecordEnvelope candidate = ToPublication(run);
        Assert.Equal(("stopped", "reserved"), (run.State("announce-x"), run.Step("announce-x").Stop));

        // The operator dismisses the X post's request: the step settles as skipped at once, and the goal keeps its one revision.
        RecordEnvelope request = run.Request("announce-x");
        run.Dismiss(request, "No post on X for this release.");
        FlowPosition.StepInstance skipped = run.Step("announce-x");
        Assert.Equal("off", skipped.State);
        Assert.StartsWith($"skipped: the operator dismissed its request {request.Id} at ", skipped.Reasons[0], StringComparison.Ordinal);
        Assert.Contains("(No post on X for this release.)", skipped.Reasons[0], StringComparison.Ordinal);
        Assert.Single(run.Estate.Load().Chain(run.Goal));
        Assert.DoesNotContain(run.Position().Stops, s => s.Step?.Key == "announce-x");
        Assert.Empty(FlowStops.Classify(run.Estate.Load(), run.Position(), new FlowRuns.StepRef("announce-x", null)));

        // The required posts still wait on the operator's act; once done, the documentation pass and the carrier's close remain.
        Assert.False(run.Position().Complete);
        foreach (string key in Required)
        {
            run.Done(run.Request(key));
        }

        Assert.Equal("next", run.State("docs-after"));
        run.Clear(guide);
        Assert.Equal(("done", "done"), (run.State("docs-after"), run.State("profile-docs")));
        Assert.False(run.Position().Complete);
        run.Accept(run.Carrier);
        FlowPosition.Position position = run.Position();
        Assert.True(position.Complete, string.Join("; ", position.Steps.Where(s => s.State is not ("done" or "off")).Select(s => $"{s.Name} {s.State}: {s.Reasons[0]}")));
        Assert.Equal("off", run.State("announce-x"));
        Assert.Equal("published", EstateModel.BodyString(run.Estate.Load().Latest(candidate), "status"));
        run.AssertValid();
    }

    [Fact]
    public void ASkippedXPostByARevisionKeepsTheClosesAlreadyRecordedAndTheRunCompletes()
    {
        using var run = new ReleaseFlowFixture(firstParty: true, extension: OptionalX);
        RecordEnvelope guide = run.Guide();
        ToPublication(run);
        run.Done(run.Request("mcp-registry"));
        Assert.Equal("done", run.State("mcp-registry"));

        // A revision that only turns the optional X post off asks nothing again: the listing's close still answers the same ask.
        run.Revise(ReleaseFlowFixture.Version, new JsonObject { ["step.announce-x"] = "off" });
        run.Add("goal-status", new JsonObject { ["status"] = "active", ["note"] = "Activated again for the test." }, subject: run.Goal.Reference);
        Assert.Equal("off", run.State("announce-x"));
        Assert.Equal("configured off by the goal's plan", run.Step("announce-x").Reasons[0]);
        Assert.Equal("done", run.State("mcp-registry"));
        run.AssertValid();

        run.Done(run.Request("announce-discussions"));
        run.Clear(guide);
        run.Accept(run.Carrier);
        FlowPosition.Position position = run.Position();
        Assert.True(position.Complete, string.Join("; ", position.Steps.Where(s => s.State is not ("done" or "off")).Select(s => $"{s.Name} {s.State}: {s.Reasons[0]}")));

        // A revision that changes the version is a new ask for each publication, as before.
        run.Revise("1.2.1", new JsonObject { ["step.announce-x"] = "off" });
        Assert.NotEqual("done", run.State("mcp-registry"));
        Assert.NotEqual("done", run.State("announce-discussions"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TheDocumentationStepsCountOnlyOnceThePublicationIsObserved(bool firstParty)
    {
        using var run = new ReleaseFlowFixture(firstParty, extension: firstParty ? OptionalX : null);
        RecordEnvelope guide = run.Guide();
        run.Clear(guide);

        // Nothing is due before the publication, yet the documentation pass after it does not read done: it waits on the steps before it.
        FlowPosition.StepInstance before = run.Step("docs-after");
        Assert.Equal("blocked", before.State);
        Assert.Contains("its done predicates hold only because nothing is there yet to count, so they count once every step it waits on is done", before.Reasons);
        Assert.Contains(before.Done, d => d.Verdict.Value == "met");
        if (firstParty)
        {
            Assert.Equal("blocked", run.State("profile-docs"));
            Assert.Contains("it waits on docs-after, which is blocked", run.Step("profile-docs").Reasons);
        }

        // Up to the publication's observation it still waits; the publication makes the guide due, and clearing it completes the pass.
        RecordEnvelope candidate = firstParty ? ToAcceptance(run) : ToAcceptanceOfBase(run);
        Assert.Equal("blocked", run.State("docs-after"));
        run.Published(candidate);
        Assert.Equal(("done", "off"), (run.State("observe"), run.State("upgrade-estate")));
        Assert.Equal("next", run.State("docs-after"));
        run.Clear(guide);
        Assert.Equal("done", run.State("docs-after"));
        if (firstParty)
        {
            Assert.Equal("done", run.State("profile-docs"));
        }
    }

    [Fact]
    public void NothingRequiredBecomesSkippable()
    {
        using var run = new ReleaseFlowFixture(firstParty: true, extension: OptionalX);

        // A dismissal before the step was asked answers no later ask, even with the step's exact command.
        RecordEnvelope early = run.Request("announce-x", command: $"Post the reviewed summary {ReleaseFlowFixture.Hash('e')} of Vericairn {ReleaseFlowFixture.Version} on the operator's X account");
        run.Dismiss(early);
        ToPublication(run);
        Assert.Equal("stopped", run.State("announce-x"));

        // A required post the operator dismisses stays asked: its request is closed and not made again while its command holds.
        run.Dismiss(run.Request("mcp-registry"));
        Assert.Equal(("stopped", "reserved"), (run.State("mcp-registry"), run.Step("mcp-registry").Stop));
        Assert.Equal("closed", Assert.Single(run.Position().Stops, s => s.Step?.Key == "mcp-registry").Delivery);

        // An agent's dismissal citing the operator's words, and the operator's dismissal of another command, skip nothing.
        run.DismissByAgent(run.Request("announce-x"));
        Assert.Equal("stopped", run.State("announce-x"));
        run.Dismiss(run.Request("announce-x", command: "Post something else on the operator's X account"));
        Assert.Equal("stopped", run.State("announce-x"));

        // Every required step done but the dismissed one: the run is not complete.
        run.Done(run.Request("announce-discussions"));
        run.Accept(run.Carrier);
        Assert.False(run.Position().Complete);
        Assert.Equal("stopped", run.State("mcp-registry"));
        run.AssertValid();
    }

    [Fact]
    public void AnOptionalPostAnEstatePolicyRequiresIsNeverSkippedByADismissal()
    {
        using var run = new ReleaseFlowFixture(firstParty: true, extension: OptionalX, policy: RequiresX);
        run.AssertValid();
        ToPublication(run);
        run.Dismiss(run.Request("announce-x"), "No post on X for this release.");
        Assert.Equal(("stopped", "reserved"), (run.State("announce-x"), run.Step("announce-x").Stop));
        Assert.Equal("closed", Assert.Single(run.Position().Stops, s => s.Step?.Key == "announce-x").Delivery);
    }

    [Fact]
    public void AnEarlyCloseOfAStepAfterADismissedPostDoesNotCount()
    {
        using var run = new ReleaseFlowFixture(firstParty: true, extension: ListingAfterX);
        ToPublication(run);
        FlowPosition.StepInstance waiting = run.Step("mcp-registry");
        Assert.Equal("blocked", waiting.State);
        Assert.Contains("it waits on announce-x, which is stopped", waiting.Reasons);
        Assert.Null(waiting.Command);

        // The operator closes the listing's exact command as done while it waits on the X post: the close does not count.
        run.Done(run.Request("mcp-registry", command: Listing));
        Assert.Equal("blocked", run.State("mcp-registry"));

        // The X post is dismissed and skipped: the listing is asked from the dismissal, so the earlier close still does not count.
        RecordEnvelope dismissal = run.Dismiss(run.Request("announce-x"), "No post on X for this release.");
        Assert.Equal("off", run.State("announce-x"));
        FlowPosition.StepInstance listing = run.Step("mcp-registry");
        Assert.Equal(("stopped", "reserved", Listing), (listing.State, listing.Stop, listing.Command));
        Assert.Equal(dismissal.AtTime, listing.AskedSince);
        Assert.Equal("due", Assert.Single(run.Position().Stops, s => s.Step?.Key == "mcp-registry").Delivery);

        // A close made after the listing is asked counts.
        run.Done(run.Request("mcp-registry"));
        Assert.Equal("done", run.State("mcp-registry"));
        run.AssertValid();
    }

    [Fact]
    public void AnEarlyCloseOfAStepAfterAPostTurnedOffByARevisionDoesNotCount()
    {
        using var run = new ReleaseFlowFixture(firstParty: true, extension: ListingAfterX);
        ToPublication(run);
        run.Done(run.Request("mcp-registry", command: Listing));
        Assert.Equal("blocked", run.State("mcp-registry"));

        // A revision that only turns the X post off keeps the run's shape, yet the listing is asked from that revision, not before it.
        RecordEnvelope revision = run.Revise(ReleaseFlowFixture.Version, new JsonObject { ["step.announce-x"] = "off" });
        run.Add("goal-status", new JsonObject { ["status"] = "active", ["note"] = "Activated again for the test." }, subject: run.Goal.Reference);
        Assert.Equal(("off", "configured off by the goal's plan"), (run.State("announce-x"), run.Step("announce-x").Reasons[0]));
        FlowPosition.StepInstance listing = run.Step("mcp-registry");
        Assert.Equal(("stopped", "reserved", Listing), (listing.State, listing.Stop, listing.Command));
        Assert.Equal(revision.AtTime, listing.AskedSince);

        // A close made after the listing is asked counts, and the run completes without the X post.
        run.Done(run.Request("mcp-registry"));
        Assert.Equal("done", run.State("mcp-registry"));
        run.Done(run.Request("announce-discussions"));
        run.Clear(run.Guide());
        run.Accept(run.Carrier);
        FlowPosition.Position position = run.Position();
        Assert.True(position.Complete, string.Join("; ", position.Steps.Where(s => s.State is not ("done" or "off")).Select(s => $"{s.Name} {s.State}: {s.Reasons[0]}")));
        run.AssertValid();
    }

    [Fact]
    public void AnOptionalStepSettledBeforeARevisionTurnsItOffKeepsTheCloseOfTheStepAfterIt()
    {
        // The X post is done and the listing closed after it: a revision turning the post off afterwards asks the listing nothing again.
        using (var run = new ReleaseFlowFixture(firstParty: true, extension: ListingAfterX))
        {
            ToPublication(run);
            RecordEnvelope posted = run.Done(run.Request("announce-x"));
            Assert.Equal("done", run.State("announce-x"));
            run.Done(run.Request("mcp-registry"));
            Assert.Equal("done", run.State("mcp-registry"));
            run.Revise(ReleaseFlowFixture.Version, new JsonObject { ["step.announce-x"] = "off" });
            run.Add("goal-status", new JsonObject { ["status"] = "active", ["note"] = "Activated again for the test." }, subject: run.Goal.Reference);
            Assert.Equal(("off", "done"), (run.State("announce-x"), run.State("mcp-registry")));
            Assert.Equal(posted.AtTime, run.Step("mcp-registry").AskedSince);
        }

        // The X post is dismissed and the listing closed after it: a revision turning the post off afterwards keeps the listing asked
        // from the dismissal.
        using (var run = new ReleaseFlowFixture(firstParty: true, extension: ListingAfterX))
        {
            ToPublication(run);
            RecordEnvelope dismissal = run.Dismiss(run.Request("announce-x"));
            run.Done(run.Request("mcp-registry"));
            Assert.Equal("done", run.State("mcp-registry"));
            run.Revise(ReleaseFlowFixture.Version, new JsonObject { ["step.announce-x"] = "off" });
            run.Add("goal-status", new JsonObject { ["status"] = "active", ["note"] = "Activated again for the test." }, subject: run.Goal.Reference);
            Assert.Equal(("off", "done"), (run.State("announce-x"), run.State("mcp-registry")));
            Assert.Equal(dismissal.AtTime, run.Step("mcp-registry").AskedSince);
        }
    }

    [Fact]
    public void AStepLeftWithoutAnInputByADismissedPostIsSkippedWithItAndTheStepAfterItIsAskedFromThen()
    {
        using var run = new ReleaseFlowFixture(firstParty: true, extension: ListingAfterRepost);
        run.AssertValid();
        ToPublication(run);
        Assert.Equal(("stopped", "blocked", "blocked"), (run.State("announce-x"), run.State("repost"), run.State("mcp-registry")));
        run.Done(run.Request("mcp-registry", command: Listing));

        // The X post is dismissed: the repost, which takes its input from it, is skipped with it, and the listing after the repost is
        // asked from the dismissal, though nothing it waits on through the repost leads to the X post.
        RecordEnvelope dismissal = run.Dismiss(run.Request("announce-x"));
        Assert.Equal(("off", "off"), (run.State("announce-x"), run.State("repost")));
        Assert.Equal("its input post comes from announce-x, which is off", run.Step("repost").Reasons[0]);
        FlowPosition.StepInstance listing = run.Step("mcp-registry");
        Assert.Equal(("stopped", Listing, dismissal.AtTime), (listing.State, listing.Command, listing.AskedSince));
        run.Done(run.Request("mcp-registry"));
        Assert.Equal("done", run.State("mcp-registry"));
    }

    [Fact]
    public void TemplateDefaultsWrittenAfterActivationChangeNoStepUntilTheSponsorsNextRevision()
    {
        using (var run = new ReleaseFlowFixture(firstParty: true, extension: ListingAfterX))
        {
            ToPublication(run);
            Assert.Equal(("stopped", "blocked"), (run.State("announce-x"), run.State("mcp-registry")));
            run.Done(run.Request("mcp-registry", command: Listing));

            // The estate's defaults turn the X post off after the run was activated: the running run keeps its configuration, so the X
            // post still waits on the operator, the listing still waits on it, and the close made while the listing waited counts for
            // nothing.
            RecordEnvelope defaults = run.Defaults(new JsonObject { ["step.announce-x"] = "off" });
            run.AssertValid();
            Assert.Equal(("stopped", "blocked"), (run.State("announce-x"), run.State("mcp-registry")));
            Assert.Equal(("on", "the flow's default"), (run.Position().Configuration!.Value("step.announce-x"), run.Position().Configuration!.Values["step.announce-x"].Source));

            // The next revision the sponsor signs takes the defaults in, proposed and once activated: the X post is off from the revision,
            // and the listing is asked from then, so the early close still counts for nothing and a close made after it counts.
            RecordEnvelope revision = run.Revise(ReleaseFlowFixture.Version);
            Assert.Equal($"configured off by the estate defaults (template-defaults {defaults.Id})", run.Step("announce-x").Reasons[0]);
            run.Activate();
            FlowPosition.StepInstance listing = run.Step("mcp-registry");
            Assert.Equal(("off", "stopped", revision.AtTime), (run.State("announce-x"), listing.State, listing.AskedSince));
            run.Done(run.Request("mcp-registry"));
            Assert.Equal("done", run.State("mcp-registry"));
            run.AssertValid();
        }

        // Defaults that turn a step on take effect only at the next revision too, even one that changes only the budget, and since they
        // change more than steps turned off, that revision asks every reserved step again.
        using (var run = new ReleaseFlowFixture(firstParty: true, extension: OptionalX))
        {
            ToPublication(run);
            run.Done(run.Request("mcp-registry"));
            run.Defaults(new JsonObject { ["step.upgrade-estate"] = "on" });
            Assert.Equal(("off", "done"), (run.State("upgrade-estate"), run.State("mcp-registry")));
            RecordEnvelope revision = run.ReviseBudget(5);
            run.Activate();
            Assert.Equal("on", run.Position().Configuration!.Value("step.upgrade-estate"));
            FlowPosition.StepInstance listing = run.Step("mcp-registry");
            Assert.Equal(("stopped", revision.AtTime), (listing.State, listing.AskedSince));
        }
    }

    [Fact]
    public void ARevisionThatChangesUntilAsksEveryStepAgain()
    {
        using var run = new ReleaseFlowFixture(firstParty: true, extension: ListingAfterX, until: "announce-x");
        run.AssertValid();
        ToPublication(run);
        Assert.Equal(("off", "after announce-x, the step the run ends at"), (run.State("mcp-registry"), run.Step("mcp-registry").Reasons[0]));
        run.Done(run.Request("announce-x"));
        Assert.Equal("done", run.State("announce-x"));
        run.Done(run.Request("mcp-registry", command: Listing));

        // The sponsor's revision ends the run no longer at the X post: every step is asked again from it, so the X post's close made
        // before counts no more, and the listing's close made while it was off never counts.
        RecordEnvelope revision = run.Revise(ReleaseFlowFixture.Version);
        run.Activate();
        run.AssertValid();
        FlowPosition.StepInstance post = run.Step("announce-x");
        Assert.Equal(("stopped", revision.AtTime), (post.State, post.AskedSince));
        Assert.Equal("blocked", run.State("mcp-registry"));
        RecordEnvelope again = run.Done(run.Request("announce-x"));
        FlowPosition.StepInstance listing = run.Step("mcp-registry");
        Assert.Equal(("stopped", again.AtTime), (listing.State, listing.AskedSince));
        run.Done(run.Request("mcp-registry"));
        Assert.Equal("done", run.State("mcp-registry"));
    }

    [Fact]
    public void AnItemARevisionTurnsOffSettlesItsStepFromThen()
    {
        // The announcement is drafted without the X summary: the draft is not done, and a close of the discussion's request made while
        // the discussion waited on the draft counts for nothing once a revision turns the summary off; the draft counts as done from the
        // revision, and a close made after it counts.
        using (var run = new ReleaseFlowFixture(firstParty: true, extension: OptionalXItem))
        {
            run.AssertValid();
            RecordEnvelope candidate = ToAcceptanceOfBase(run);
            RecordEnvelope draft = run.WorkStep("draft-announcement", ("discussion", ReleaseFlowFixture.Hash('d'), "text/markdown"));
            run.ByReviewer("review", new JsonObject { ["independence"] = "independent-agent", ["outcome"] = "pass", ["summary"] = "Read the draft.", ["findings"] = new JsonArray() }, draft.Reference);
            run.Published(candidate);
            Assert.NotEqual("done", run.State("draft-announcement"));
            Assert.Equal("blocked", run.State("announce-discussions"));
            run.Done(run.Request("announce-discussions", command: Discussion));

            RecordEnvelope revision = run.Revise(ReleaseFlowFixture.Version, new JsonObject { ["item.draft-announcement.x-post"] = "off" });
            run.Activate();
            run.AssertValid();
            FlowPosition.StepInstance drafted = run.Step("draft-announcement");
            Assert.Equal(("done", revision.AtTime), (drafted.State, drafted.DoneAt));
            Assert.Contains($"it counts as done from {RecordParser.FormatTimestamp(revision.AtTime)}, when goal revision {revision.Id} turned its optional item x-post off, so the steps after it are asked from then", drafted.Reasons);
            Assert.Equal(("off", "its input x-post is item x-post of draft-announcement, which is off"), (run.State("announce-x"), run.Step("announce-x").Reasons[0]));
            FlowPosition.StepInstance discussion = run.Step("announce-discussions");
            Assert.Equal(("stopped", Discussion, revision.AtTime), (discussion.State, discussion.Command, discussion.AskedSince));
            run.Done(run.Request("announce-discussions"));
            Assert.Equal("done", run.State("announce-discussions"));
        }

        // The draft carries the X summary, reviewed, before the revision turns it off: it was done then, so the discussion's close made
        // since keeps counting.
        using (var run = new ReleaseFlowFixture(firstParty: true, extension: OptionalXItem))
        {
            ToPublication(run);
            DateTimeOffset? drafted = run.Step("draft-announcement").DoneAt;
            run.Done(run.Request("announce-discussions"));
            Assert.Equal("done", run.State("announce-discussions"));
            run.Revise(ReleaseFlowFixture.Version, new JsonObject { ["item.draft-announcement.x-post"] = "off" });
            run.Activate();
            Assert.Equal(("done", drafted), (run.State("draft-announcement"), run.Step("draft-announcement").DoneAt));
            Assert.Equal("done", run.State("announce-discussions"));
        }
    }

    /// <summary>The discussion post's command for the fixture's version and the draft's reviewed hash, as the first-party extension gives it.</summary>
    private static string Discussion => $"Post the reviewed announcement {ReleaseFlowFixture.Hash('d')} of Vericairn {ReleaseFlowFixture.Version} in the Announcements category of github.com/orgs/Vericairn/discussions";

    /// <summary>
    /// The first-party extension with the X post optional, as <see cref="OptionalX"/> makes it, and the X summary the draft carries an
    /// optional item, on by default, so that a revision turning it off turns the X post off with it.
    /// </summary>
    private static void OptionalXItem(JsonObject extension)
    {
        OptionalX(extension);
        extension["version"] = "1.0.4";
        var steps = (JsonArray)extension["steps"]!;
        JsonObject draft = steps.First(s => s!["key"]!.GetValue<string>() == "draft-announcement")!.AsObject();
        JsonObject post = draft["outputs"]!.AsArray().First(o => o!["name"]!.GetValue<string>() == "x-post")!.AsObject();
        post.Remove("required");
        post["optional"] = true;
        post["default"] = "on";
    }

    /// <summary>
    /// The first-party extension as its revision makes the X post optional, on by default like the other posts. The NuGet observation,
    /// which needs eight registry observations of its own, is left out so the run is the release's posts and documents.
    /// </summary>
    private static void OptionalX(JsonObject extension)
    {
        extension["version"] = "1.0.1";
        var steps = (JsonArray)extension["steps"]!;
        steps.Remove(steps.First(s => s!["key"]!.GetValue<string>() == "nuget"));
        JsonObject x = steps.First(s => s!["key"]!.GetValue<string>() == "announce-x")!.AsObject();
        x.Remove("required");
        x["optional"] = true;
        x["default"] = "on";
    }

    /// <summary>
    /// The first-party extension with the X post optional, as <see cref="OptionalX"/> makes it, and the MCP Registry listing placed
    /// after the X post: a reserved step after an optional one.
    /// </summary>
    private static void ListingAfterX(JsonObject extension)
    {
        OptionalX(extension);
        extension["version"] = "1.0.2";
        var steps = (JsonArray)extension["steps"]!;
        JsonObject listing = steps.First(s => s!["key"]!.GetValue<string>() == "mcp-registry")!.AsObject();
        steps.Remove(listing);
        listing["after"] = new JsonArray("announce-x");
        steps.Add(listing);
    }

    /// <summary>
    /// The first-party extension with the X post optional, its close of the request handed on as an item, an optional repost after
    /// publication that takes that item as its input, and the MCP Registry listing placed after the repost.
    /// </summary>
    private static void ListingAfterRepost(JsonObject extension)
    {
        OptionalX(extension);
        extension["version"] = "1.0.3";
        var steps = (JsonArray)extension["steps"]!;
        JsonObject x = steps.First(s => s!["key"]!.GetValue<string>() == "announce-x")!.AsObject();
        x["outputs"] = new JsonArray(new JsonObject { ["name"] = "post", ["kind"] = "record", ["recordType"] = "operator-request-close", ["required"] = true });
        JsonObject listing = steps.First(s => s!["key"]!.GetValue<string>() == "mcp-registry")!.AsObject();
        steps.Remove(listing);
        steps.Add(new JsonObject
        {
            ["key"] = "repost", ["title"] = "Post shared", ["kind"] = "operation", ["role"] = "release-manager", ["optional"] = true, ["default"] = "on",
            ["after"] = new JsonArray("publish"), ["action"] = "Share the post.",
            ["inputs"] = new JsonArray(new JsonObject { ["name"] = "post", ["from"] = "step", ["step"] = "announce-x", ["output"] = "post" }),
            ["done"] = new JsonArray(new JsonObject { ["type"] = "observation", ["fields"] = new JsonObject { ["kind"] = "repost" }, ["min"] = 1 }),
            ["recovery"] = "Share it again.",
        });
        listing["after"] = new JsonArray("repost");
        steps.Add(listing);
    }

    private static JsonObject RequiresX(ReleaseFlowFixture run)
    {
        JsonObject body = run.Records.PolicyBody("release-posts", duty: false);
        body["rules"] = new JsonArray(new JsonObject
        {
            ["key"] = "posts", ["kind"] = FlowRuns.PolicyKind, ["version"] = 1,
            ["parameters"] = new JsonObject { ["flow"] = "vericairn.first-party/release", ["steps"] = new JsonArray("announce-x") },
        });
        return body;
    }

    /// <summary>A first-party run taken to the operator's acceptance of its candidate, the announcement's drafts reviewed.</summary>
    private static RecordEnvelope ToAcceptance(ReleaseFlowFixture run)
    {
        RecordEnvelope candidate = ToAcceptanceOfBase(run);
        RecordEnvelope drafts = run.WorkStep("draft-announcement", ("discussion", ReleaseFlowFixture.Hash('d'), "text/markdown"), ("x-post", ReleaseFlowFixture.Hash('e'), "text/plain"));
        run.ByReviewer("review", new JsonObject { ["independence"] = "independent-agent", ["outcome"] = "pass", ["summary"] = "Read both drafts.", ["findings"] = new JsonArray() }, drafts.Reference);
        return candidate;
    }

    /// <summary>A run taken to the operator's acceptance of its candidate.</summary>
    private static RecordEnvelope ToAcceptanceOfBase(ReleaseFlowFixture run)
    {
        run.Accept(run.First);
        run.Accept(run.Second);
        run.GuidesChecked();
        run.WorkStep("docs-set", ("manifest", ReleaseFlowFixture.Hash('1'), "application/json"), ("notes", ReleaseFlowFixture.Hash('2'), "text/markdown"));
        run.WorkStep("release-commit", ("frozen-index", ReleaseFlowFixture.Hash('3'), "application/json"));
        run.Settle(run.Intent(), "dispatched");
        RecordEnvelope candidate = run.Candidate();
        run.ReleaseReview(candidate);
        run.Accept(candidate);
        return candidate;
    }

    /// <summary>A first-party run taken to its observed publication.</summary>
    private static RecordEnvelope ToPublication(ReleaseFlowFixture run)
    {
        RecordEnvelope candidate = ToAcceptance(run);
        run.Published(candidate);
        return candidate;
    }
}
