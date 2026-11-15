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
/// first-party extension's X post is made optional as its revision in the estate makes it, beside the other posts.
/// </summary>
public class ReleaseFlowCompletionTests
{
    private static readonly string[] Required = ["mcp-registry", "announce-discussions"];

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
