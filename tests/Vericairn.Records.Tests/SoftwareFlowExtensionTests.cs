// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using System.Text.Json.Nodes;
using Vericairn.Records.Estate;
using Vericairn.Records.Model;
using Vericairn.Records.Templates;

namespace Vericairn.Records.Tests;

/// <summary>
/// Extending the software pack's release flow (the estate's design/release-procedure.md § 7; design/flow.md § 5): an estate adds steps
/// after a named step in a flow that names the release flow in extends, never turning off a required step, a reserved marking or a step
/// an estate policy requires; and Vericairn's first-party extension, the estate's own file, adds the NuGet observation, the MCP Registry
/// listing and the announcement as reserved publication steps after publication, done only by the operator's close of each step's
/// request, and the profile repository's documents after the documentation pass.
/// </summary>
public class SoftwareFlowExtensionTests
{
    private static readonly string[] Publications = ["mcp-registry", "announce-discussions", "announce-x"];

    private static readonly string[] PublishSteps = ["publish", .. Publications];

    [Fact]
    public void TheFirstPartyExtensionPinsTheShippedReleaseFlowAndAddsItsStepsAfterTheNamedOnes()
    {
        using var estate = new TestEstate(3);
        TemplateDefinition extension = ReleaseFlowFixture.InstallFirstParty(estate);
        JsonElement extended = extension.Content.GetProperty("extends");
        Assert.Equal((SoftwareFlows.Release, SoftwareFlows.Version, SoftwareFlows.ReleaseFlow.Hash),
            (extended.GetProperty("id").GetString(), extended.GetProperty("version").GetString(), extended.GetProperty("hash").GetString()));

        FlowDefinition flow = FlowDefinition.Resolve(estate.Load(), new TemplatePin(extension.Id, extension.Version, extension.Hash));
        FlowDefinition shipped = FlowDefinition.Resolve(estate.Load(), new TemplatePin(SoftwareFlows.Release, SoftwareFlows.Version, SoftwareFlows.ReleaseFlow.Hash));
        Assert.Equal([shipped.Pin, flow.Pin], flow.Chain);
        string[] keys = flow.Steps.Select(s => s.Key).ToArray();
        Assert.Equal("nuget", keys[Array.IndexOf(keys, "observe") + 1]);
        Assert.Equal("profile-docs", keys[Array.IndexOf(keys, "docs-after") + 1]);
        Assert.Equal(["draft-announcement", "mcp-registry", "announce-discussions", "announce-x"], keys[^4..]);

        // Every base step keeps its marking, its reserved act and its role; the extension's publications are reserved publish steps.
        foreach (FlowStep step in shipped.Steps)
        {
            FlowStep kept = flow.Step(step.Key)!;
            Assert.Equal((step.Required, step.Reserved, step.Role, step.DeclaredIn), (kept.Required, kept.Reserved, kept.Role, kept.DeclaredIn));
        }

        Assert.Equal(["accept", "publish", "delete-draft", "mcp-registry", "announce-discussions", "announce-x"], flow.Steps.Where(s => s.Reserved is not null).Select(s => s.Key).ToArray());
        Assert.All(Publications, key => Assert.Equal("publish", flow.Step(key)!.Reserved));
        Assert.Contains("publish", flow.Step("mcp-registry")!.After);
        Assert.Equal(["publish", "draft-announcement"], flow.Step("announce-x")!.After);

        // The announcement's drafts feed reserved steps, so each takes an independent review before it is handed on.
        FlowStep draft = flow.Step("draft-announcement")!;
        Assert.All(draft.Outputs, o => Assert.NotNull(FlowRuns.Consequential(flow, draft, o)));
    }

    [Fact]
    public void TheFirstPartyExtensionIsNeverPartOfTheSoftwarePack()
    {
        using var estate = new TestEstate(3);
        Assert.DoesNotContain(TemplateCatalog.All(estate.Load()), d => d.Id == "vericairn.first-party/release");
        Assert.Equal([SoftwareFlows.Release, SoftwareFlows.Upgrade], TemplateCatalog.All(estate.Load()).Where(d => d.IsFlow).Select(d => d.Id).Order(StringComparer.Ordinal).ToArray());
    }

    [Theory]
    [InlineData("off-required")]
    [InlineData("redeclare-reserved")]
    [InlineData("redeclare-role")]
    [InlineData("branch-takes-base-step")]
    public void AnExtensionNeverTurnsOffARequiredStepOrChangesAReservedMarking(string change)
    {
        using var estate = new TestEstate(3);
        JsonObject extension = Extension();
        switch (change)
        {
            case "off-required":
                extension["off"] = new JsonArray("publish");
                break;
            case "redeclare-reserved":
                ((JsonArray)extension["steps"]!).Add(JsonNode.Parse("""
                    {"key":"accept","title":"Accepted by the release manager","kind":"operation","role":"release-manager","required":true,"action":"accept it","done":[{"type":"acceptance","subject":"{subject.carrier}"}],"recovery":"Again."}
                    """));
                break;
            case "redeclare-role":
                extension["roles"] = new JsonArray(JsonNode.Parse("""{"id":"publisher","holder":"agent","brief":"Publish for the operator."}"""));
                break;
            case "branch-takes-base-step":
                extension["branches"] = new JsonArray(JsonNode.Parse("""{"name":"skip","when":[{"type":"work-order","fields":{"key":"NEVER"}}],"steps":["publish"]}"""));
                break;
        }

        TemplateDefinition installed = TemplateFixture.Install(estate, extension);
        var refusal = Assert.Throws<InvalidOperationException>(() => FlowDefinition.Resolve(estate.Load(), new TemplatePin(installed.Id, installed.Version, installed.Hash)));
        Assert.StartsWith("The flow does not resolve:", refusal.Message, StringComparison.Ordinal);
        Assert.Contains(change switch
        {
            "off-required" => "turns off 'publish', a required step",
            "redeclare-reserved" => "declares step 'accept' again",
            "redeclare-role" => "declares role 'publisher' again",
            _ => "branch 'skip' names 'publish', which is not one of the steps it declares",
        }, refusal.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AnExtensionsReservedStepNeverWaitsOnAStepDoneAtNoTimeTheRecordsGive(bool throughAnOptionalStep)
    {
        using var estate = new TestEstate(3);
        JsonObject extension = Extension();
        var steps = (JsonArray)extension["steps"]!;

        // A check done when no living document is due leaves no record of its own, so it is done at no time the records give.
        steps.Add(JsonNode.Parse("""
            {"key":"checked","title":"Nothing due","kind":"operation","role":"release-manager","required":true,"following":"site","action":"Clear what the release made due.","done":[{"type":"living-document","state":"due","max":0}],"recovery":"Clear it again."}
            """));
        string waited = "checked";
        if (throughAnOptionalStep)
        {
            // An optional step that is off hands its waiting on, so the post still waits on the check.
            steps.Add(JsonNode.Parse("""
                {"key":"maybe","title":"Optional extra","kind":"operation","role":"release-manager","optional":true,"default":"off","following":"checked","action":"Do the extra.","done":[{"type":"work-order","fields":{"key":"EXTRA-1"}}],"recovery":"Again."}
                """));
            waited = "maybe";
        }

        steps.Add(JsonNode.Parse($$$"""
            {"key":"post","title":"Posted","kind":"operation","role":"publisher","required":true,"reserved":"publish","following":"{{{waited}}}","after":["{{{waited}}}"],"action":"Post the release notes of {subject.version}.","done":[{"type":"operator-request-close","subject":"{request}","fields":{"outcome":"done"}}],"recovery":"Post again."}
            """));

        TemplateDefinition installed = TemplateFixture.Install(estate, extension);
        var refusal = Assert.Throws<InvalidOperationException>(() => FlowDefinition.Resolve(estate.Load(), new TemplatePin(installed.Id, installed.Version, installed.Hash)));
        Assert.Contains(FlowContract.UnclosableCode + ": Reserved step 'post' is done by the operator's close of its request and waits on 'checked'", refusal.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AnExtensionMayTurnOffAnOptionalStepButNeverOneAnEstatePolicyRequires()
    {
        // Without a policy, an extension turns off the optional upgrade-estate step.
        using (var estate = new TestEstate(3))
        {
            JsonObject extension = Extension();
            extension["off"] = new JsonArray("upgrade-estate");
            TemplateDefinition installed = TemplateFixture.Install(estate, extension);
            FlowDefinition flow = FlowDefinition.Resolve(estate.Load(), new TemplatePin(installed.Id, installed.Version, installed.Hash));
            Assert.True(flow.TurnedOff.ContainsKey("upgrade-estate"));
        }

        // With a policy requiring it of the release flow, a run of the extension that turns it off is refused, as a run of the base
        // that leaves it off is; a run of the base that turns it on is not.
        using var run = new ReleaseFlowFixture(options: new JsonObject { ["step.upgrade-estate"] = "on" }, policy: RequiresUpgrade);
        run.AssertValid();
        JsonObject turningOff = Extension();
        turningOff["off"] = new JsonArray("upgrade-estate");
        TemplateDefinition off = TemplateFixture.Install(run.Estate, turningOff);
        JsonObject body = run.GoalBody(ReleaseFlowFixture.Version);
        body["plan"]!["flow"]!["pin"] = FlowFixture.Pin(off);
        run.Add("goal", body);
        EstateModel model = run.Estate.Load();
        Finding refused = Assert.Single(model.Findings, f => f.Code == "flow.policy");
        Assert.Contains("Step upgrade-estate is required by estate policy", refused.Message, StringComparison.Ordinal);
        Assert.Contains("turned off by", refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TheFirstPartyPublicationsWaitOnTheOperatorAndOnlyTheOperatorsCloseOfEachRequestCompletesThem()
    {
        using var run = new ReleaseFlowFixture(firstParty: true);
        run.Accept(run.First);
        run.Accept(run.Second);
        run.GuidesChecked();
        run.WorkStep("docs-set", ("manifest", ReleaseFlowFixture.Hash('1'), "application/json"), ("notes", ReleaseFlowFixture.Hash('2'), "text/markdown"));
        run.WorkStep("release-commit", ("frozen-index", ReleaseFlowFixture.Hash('3'), "application/json"));
        run.Settle(run.Intent(), "dispatched");
        RecordEnvelope candidate = run.Candidate();
        run.ReleaseReview(candidate);

        // Before acceptance, no publication step has a command, and the draft waits on acceptance.
        Assert.All(PublishSteps, key => Assert.Null(run.Step(key).Command));
        Assert.Equal("blocked", run.State("draft-announcement"));
        run.Accept(candidate);

        // The drafts are the release manager's work; each is reviewed independently before the operator posts it.
        Assert.Equal("next", run.State("draft-announcement"));
        RecordEnvelope drafts = run.WorkStep("draft-announcement", ("discussion", ReleaseFlowFixture.Hash('d'), "text/markdown"), ("x-post", ReleaseFlowFixture.Hash('e'), "text/plain"));
        Assert.Equal("in-review", run.State("draft-announcement"));
        Assert.Contains(run.Step("draft-announcement").Reviews, r => r.Requirement.Independence == "independent");
        run.ByReviewer("review", new JsonObject { ["independence"] = "independent-agent", ["outcome"] = "pass", ["summary"] = "Read both drafts.", ["findings"] = new JsonArray() }, drafts.Reference);
        Assert.Equal("done", run.State("draft-announcement"));

        // Publication first: until the publication is observed, the listing and the announcement have no command.
        Assert.NotNull(run.Step("publish").Command);
        Assert.All(Publications, key => Assert.Null(run.Step(key).Command));
        run.Published(candidate);

        Assert.Equal($"gh run list -R {ReleaseFlowFixture.Repository} --workflow mcp-registry.yml --event release --limit 1", run.Step("mcp-registry").Command);
        Assert.Equal($"Post the reviewed announcement {ReleaseFlowFixture.Hash('d')} of Vericairn {ReleaseFlowFixture.Version} in the Announcements category of github.com/orgs/Vericairn/discussions", run.Step("announce-discussions").Command);
        Assert.Equal($"Post the reviewed summary {ReleaseFlowFixture.Hash('e')} of Vericairn {ReleaseFlowFixture.Version} on the operator's X account", run.Step("announce-x").Command);
        Assert.All(Publications, key => Assert.Equal(("stopped", "reserved"), (run.State(key), run.Step(key).Stop)));

        // An agent's close citing the operator's words completes nothing; the operator's own close with the observed link does.
        RecordEnvelope x = run.Request("announce-x");
        run.DoneByAgent(x);
        Assert.Equal("stopped", run.State("announce-x"));
        RecordEnvelope again = run.Request("announce-x");
        run.Done(again);
        Assert.Equal("done", run.State("announce-x"));

        // A later request for the step, by anyone, never undoes the operator's recorded act.
        run.Request("announce-x", builder: true);
        Assert.Equal("done", run.State("announce-x"));
        Assert.Equal(("stopped", "stopped"), (run.State("mcp-registry"), run.State("announce-discussions")));

        // A close of another step's request completes no other step.
        run.Done(run.Request("mcp-registry"));
        Assert.Equal(("done", "stopped"), (run.State("mcp-registry"), run.State("announce-discussions")));
        run.AssertValid();
    }

    [Fact]
    public void TheListingAndTheAnnouncementAreAskedAgainForACorrectiveVersion()
    {
        using var run = new ReleaseFlowFixture(firstParty: true);
        ToPublication(run);
        run.Done(run.Request("mcp-registry"));
        run.Done(run.Request("announce-x"));
        Assert.Equal(("done", "done"), (run.State("mcp-registry"), run.State("announce-x")));

        // A revision of the run, such as a corrective version, is a new ask: the closes made before it answer nothing now.
        run.Revise("1.2.1");
        Assert.NotEqual("done", run.State("mcp-registry"));
        Assert.NotEqual("done", run.State("announce-x"));
        Assert.Equal("blocked", run.State("publish"));
    }

    [Fact]
    public void ARevisionOfTheBudgetAloneAsksNoPublicationAgainWhileAVersionChangeDoes()
    {
        using var run = new ReleaseFlowFixture(firstParty: true);
        ToPublication(run);
        run.Done(run.Request("mcp-registry"));
        run.Done(run.Request("announce-x"));

        // More attempts change neither the flow's pin, its subject nor its options: the operator's closes still answer the same asks,
        // so nothing already posted is asked for again.
        run.ReviseBudget(attempts: 5);
        Assert.Equal(("done", "done"), (run.State("mcp-registry"), run.State("announce-x")));
        Assert.Equal("stopped", run.State("announce-discussions"));

        // A new version is a new ask for each.
        run.Revise("1.2.1");
        Assert.NotEqual("done", run.State("mcp-registry"));
        Assert.NotEqual("done", run.State("announce-x"));
    }

    [Fact]
    public void ACloseDatedAfterTheReadCountsOnlyOnceItsTimeIsReached()
    {
        using var run = new ReleaseFlowFixture(firstParty: true);
        ToPublication(run);
        RecordEnvelope close = run.Done(run.Request("announce-x"));
        Assert.Equal("done", run.State("announce-x"));

        // Read before the close's own time, as a clock behind the closer's would read it, the step is still asked.
        FlowPosition.StepInstance early = Assert.Single(FlowPosition.Derive(run.Estate.Load(), run.Goal, close.AtTime.AddSeconds(-1)).Steps, s => s.Name == "announce-x");
        Assert.NotEqual("done", early.State);
    }

    [Fact]
    public void AClosedRequestCountsOnlyForTheStepsCommandFromAStepTakerAfterTheStepWasAsked()
    {
        using var run = new ReleaseFlowFixture(firstParty: true);

        // A request with the listing's exact command, closed by the operator before publication, answers no later ask.
        RecordEnvelope early = run.Request("mcp-registry", command: $"gh run list -R {ReleaseFlowFixture.Repository} --workflow mcp-registry.yml --event release --limit 1");
        run.Done(early);
        ToPublication(run);
        Assert.Equal("stopped", run.State("mcp-registry"));
        Assert.Equal("due", Stop(run, "mcp-registry").Delivery);

        // A request from a principal who does not take the run's steps, closed by the operator, completes nothing.
        RecordEnvelope stranger = run.Request("announce-discussions", command: "echo the announcement is posted", by: run.Reviewer);
        run.Done(stranger);
        Assert.Equal("stopped", run.State("announce-discussions"));

        // Nor does a request of another command, even the operator's own.
        run.Done(run.Request("announce-x", command: "Post something else on the operator's X account"));
        Assert.Equal("stopped", run.State("announce-x"));
        Assert.Equal("due", Stop(run, "announce-x").Delivery);

        // An observation that closes the request is no operator-request-close: the step stays asked, as its done predicate reads.
        RecordEnvelope observed = run.Request("announce-x");
        run.Add("observation", new JsonObject
        {
            ["kind"] = "operator-request", ["asOf"] = Vericairn.Records.Model.RecordParser.FormatTimestamp(run.Estate.Clock), ["summary"] = "The summary is posted.",
            ["facts"] = new JsonObject { ["outcome"] = "done", ["destination"] = EstateModel.BodyString(observed, "destination"), ["location"] = "https://example.invalid/post", ["evidenceHash"] = ReleaseFlowFixture.Hash('7') },
            ["limitations"] = new JsonArray(),
        }, subject: observed.Reference);
        Assert.Equal("stopped", run.State("announce-x"));
        Assert.Equal("due", Stop(run, "announce-x").Delivery);

        // The operator's own close of a request a step-taker put, with the step's command, after the step was asked, completes it.
        run.Done(run.Request("announce-x"));
        Assert.Equal("done", run.State("announce-x"));
        run.Done(run.Request("mcp-registry"));
        Assert.Equal("done", run.State("mcp-registry"));
        run.AssertValid();
    }

    /// <summary>A first-party run taken to its publication, the announcement's drafts reviewed.</summary>
    private static RecordEnvelope ToPublication(ReleaseFlowFixture run)
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
        RecordEnvelope drafts = run.WorkStep("draft-announcement", ("discussion", ReleaseFlowFixture.Hash('d'), "text/markdown"), ("x-post", ReleaseFlowFixture.Hash('e'), "text/plain"));
        run.ByReviewer("review", new JsonObject { ["independence"] = "independent-agent", ["outcome"] = "pass", ["summary"] = "Read both drafts.", ["findings"] = new JsonArray() }, drafts.Reference);
        run.Published(candidate);
        return candidate;
    }

    private static FlowStops.RunStop Stop(ReleaseFlowFixture run, string key) => Assert.Single(run.Position().Stops, s => s.Step?.Key == key);

    private static JsonObject RequiresUpgrade(ReleaseFlowFixture run)
    {
        JsonObject body = run.Records.PolicyBody("release-steps", duty: false);
        body["rules"] = new JsonArray(new JsonObject
        {
            ["key"] = "steps", ["kind"] = FlowRuns.PolicyKind, ["version"] = 1,
            ["parameters"] = new JsonObject { ["flow"] = SoftwareFlows.Release, ["steps"] = new JsonArray("upgrade-estate") },
        });
        return body;
    }

    private static JsonObject Extension() => new()
    {
        ["format"] = "vericairn.template/1", ["kind"] = "flow", ["id"] = "example.estate/release", ["version"] = "1.0.0", ["purpose"] = "The software release with an estate's own step.",
        ["extends"] = new JsonObject { ["id"] = SoftwareFlows.Release, ["version"] = SoftwareFlows.Version, ["hash"] = SoftwareFlows.ReleaseFlow.Hash },
        ["steps"] = new JsonArray(JsonNode.Parse("""
            {"key":"site","title":"Website updated","kind":"operation","role":"release-manager","required":true,"following":"observe","action":"Update the website's release page.","done":[{"type":"work-order","fields":{"key":"SITE-1"}}],"recovery":"Update it again."}
            """)),
    };
}
