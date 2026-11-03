// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json.Nodes;
using Vericairn.Records.Estate;
using Vericairn.Records.Model;
using Vericairn.Records.Templates;

namespace Vericairn.Records.Tests;

/// <summary>
/// A run's plan and configuration (the estate's design/flow.md §§ 5, 12 and 13): the narrowest configuration winning, a run ending at its
/// until step, and refusals of a configuration that turns off a required step, a reserved marking or a step an estate policy requires,
/// of an agent in a person's role, of assignments that name no step or the wrong one, and of the field in an earlier format; and a
/// reader without the flow's content showing the run and running none of it.
/// </summary>
public class FlowConfigurationTests
{
    [Fact]
    public void TheNarrowestConfigurationWinsEstateThenProductThenTheGoal()
    {
        JsonObject song = FlowFixture.Song();
        song["subject"]!["fields"]!.AsArray().Add(new JsonObject { ["name"] = "product", ["type"] = "record", ["recordType"] = "product" });
        RecordEnvelope? product = null;
        using var run = new FlowFixture(song, run: false);
        product = run.Add("product", new JsonObject
        {
            ["name"] = "Song", ["slug"] = "song", ["summary"] = "A song.", ["editions"] = new JsonArray(new JsonObject { ["name"] = "Community", ["slug"] = "community", ["summary"] = "Free." }),
            ["surfaces"] = new JsonArray(new JsonObject { ["name"] = "Audio", ["slug"] = "audio", ["summary"] = "Listen." }),
        });
        Defaults(run, "estate", null, new JsonObject { ["step.shorts"] = "off", ["review"] = "independent", ["step.publish"] = "off" });
        Defaults(run, "product", product.Id, new JsonObject { ["step.shorts"] = "on", ["review"] = "person" });
        JsonObject subject = Subject(run, product);
        RecordEnvelope goal = run.Add("goal", run.GoalBody(new JsonObject { ["review"] = "none" }, subject: subject));
        EstateModel model = run.Estate.Load();
        Assert.Empty(model.Findings.Where(f => f.Severity == Severity.Error).Select(f => f.ToString()));
        FlowDefinition flow = FlowRuns.Resolve(model, goal).Flow!;
        FlowRuns.Configuration configuration = FlowRuns.Configure(model, goal, flow);
        Assert.Equal(("on", "the product defaults"), (configuration.Values["step.shorts"].Value, configuration.Values["step.shorts"].Source[..20]));
        Assert.Equal(("none", "the goal's plan"), (configuration.Values["review"].Value, configuration.Values["review"].Source));
        Assert.Equal("off", configuration.Values["step.publish"].Value);
        Assert.StartsWith("configured off by the estate defaults (template-defaults ", configuration.Off["publish"], StringComparison.Ordinal);
        Assert.False(configuration.Off.ContainsKey("shorts"));
        Assert.Equal("on", configuration.Values["step.review-lyrics"].Value);
        Assert.Equal("the flow's default", configuration.Values["step.review-lyrics"].Source);

        // Each optional step's configuration follows the narrowest: the goal turns the clips off for itself only.
        RecordEnvelope narrower = run.Add("goal", run.GoalBody(new JsonObject { ["step.shorts"] = "off" }, subject: subject));
        Assert.Equal("configured off by the goal's plan", FlowRuns.Configure(run.Estate.Load(), narrower, flow).Off["shorts"]);
        Assert.Equal("off", FlowPosition.Derive(run.Estate.Load(), narrower, run.Estate.Clock).Steps.First(s => s.Step.Key == "shorts").State);
    }

    [Fact]
    public void NoConfigurationTurnsOffARequiredStepOrAReservedMarking()
    {
        using var run = new FlowFixture(run: false);
        RecordEnvelope defaults = Defaults(run, "estate", null, new JsonObject { ["step.master"] = "off" });
        RecordEnvelope reserved = run.Add("goal", run.GoalBody(new JsonObject { ["step.accept"] = "off" }));
        RecordEnvelope unknown = run.Add("goal", run.GoalBody(new JsonObject { ["step.mixing"] = "on", ["review"] = "sometimes" }));
        EstateModel model = run.Estate.Load();
        Assert.Contains(Errors(model, defaults), f => f.Code == "flow.required" && f.Message.StartsWith("Step master is required", StringComparison.Ordinal));
        Assert.Contains(Errors(model, reserved), f => f.Code == "flow.required" && f.Message.Contains("reserved as accept-release", StringComparison.Ordinal));
        Assert.Equal(2, Errors(model, unknown).Count(f => f.Code == "flow.option"));
    }

    [Fact]
    public void TurningOffAStepNeverLeavesARequiredStepWithoutItsInput()
    {
        JsonObject song = FlowFixture.Song();
        JsonObject master = song["steps"]!.AsArray().Select(s => s!.AsObject()).Single(s => s["key"]!.GetValue<string>() == "master");
        master["inputs"]!.AsArray().Add(FlowFixture.Input("verdict", "review-lyrics", "verdict"));
        using var run = new FlowFixture(song, run: false);
        RecordEnvelope goal = run.Add("goal", run.GoalBody(new JsonObject { ["step.review-lyrics"] = "off" }));
        Assert.Contains(Errors(run.Estate.Load(), goal), f => f.Code == "flow.required" && f.Message == "Turning off review-lyrics leaves required step master without its input verdict.");
    }

    [Fact]
    public void AStepAnEstatePolicyRequiresIsNeverConfiguredOffOrLeftOutByUntil()
    {
        using var run = new FlowFixture(run: false);
        JsonObject body = run.Policies.PolicyBody("song-reviews", duty: false);
        body["rules"] = new JsonArray(new JsonObject
        {
            ["key"] = "reviews", ["kind"] = FlowRuns.PolicyKind, ["version"] = 1,
            ["parameters"] = new JsonObject { ["flow"] = "example/song", ["steps"] = new JsonArray("review-lyrics", "shorts") },
        });
        RecordEnvelope early = Defaults(run, "estate", null, new JsonObject { ["step.shorts"] = "off" });
        RecordEnvelope before = run.Add("goal", run.GoalBody());
        RecordEnvelope policy = run.Policies.Policy(body);
        RecordEnvelope configured = run.Add("goal", run.GoalBody(new JsonObject { ["step.review-lyrics"] = "off", ["step.shorts"] = "on" }));
        RecordEnvelope ended = run.Add("goal", run.GoalBody(new JsonObject { ["step.shorts"] = "on" }, until: "draft"));
        RecordEnvelope widened = run.Add("goal", run.GoalBody());
        RecordEnvelope turnedOn = run.Add("goal", run.GoalBody(new JsonObject { ["step.shorts"] = "on" }));
        RecordEnvelope revised = run.Add("template-defaults", new JsonObject
        {
            ["template"] = FlowFixture.Pin(run.Flow), ["scope"] = new JsonObject { ["kind"] = "estate" }, ["options"] = new JsonObject { ["step.review-lyrics"] = "off" }, ["reason"] = "Skip the reviews.",
        }, supersedes: early.Id);
        EstateModel model = run.Estate.Load();
        Assert.Empty(model.Findings.Where(f => f.Location == policy.ExpectedPath && f.Severity == Severity.Error).Select(f => f.ToString()));
        Assert.Empty(Errors(model, early));
        Assert.Empty(Errors(model, before));
        Assert.Contains(Errors(model, configured), f => f.Code == "flow.policy" && f.Message.StartsWith("Step review-lyrics is required by estate policy song-reviews", StringComparison.Ordinal));
        Assert.Contains(Errors(model, ended), f => f.Code == "flow.policy" && f.Message.StartsWith("Ending the run at draft leaves out review-lyrics", StringComparison.Ordinal));
        Assert.Contains(Errors(model, widened), f => f.Code == "flow.policy" && f.Message.Contains("and this run leaves it off: configured off by the estate defaults", StringComparison.Ordinal));
        Assert.Empty(Errors(model, turnedOn));
        Assert.Contains(Errors(model, revised), f => f.Code == "flow.policy");
        Assert.Equal(["review-lyrics", "shorts"], FlowRuns.PolicyRequired(model, "example/song", null).Keys.Order(StringComparer.Ordinal));
        Assert.Empty(FlowRuns.PolicyRequired(model, "example/song", null, before));
    }

    [Fact]
    public void ARunEndsAtItsUntilStep()
    {
        using var run = new FlowFixture(until: "master");
        Assert.Empty(run.Estate.Load().Findings.Where(f => f.Severity == Severity.Error).Select(f => f.ToString()));
        Assert.Equal("after master, the step the run ends at", run.Step("listen").Reasons[0]);
        Assert.Equal("off", run.Step("shorts[intro]").State);
        RecordEnvelope lyrics = run.Result(run.Assign("draft", run.Estate.BuilderId), "completed", ("lyrics", FlowFixture.Hash('a'), "text/markdown"));
        run.Review(lyrics);
        run.Result(run.Assign("master", run.Estate.BuilderId, inputs: new JsonArray(FlowFixture.PinOf(lyrics, "lyrics"))), "completed", ("master", FlowFixture.Hash('b'), null));
        Assert.True(run.Position().Complete);
        Assert.Contains("the run is complete", FlowPosition.RenderText(run.Estate.Load(), run.Position()), StringComparison.Ordinal);

        RecordEnvelope unknown = run.Add("goal", run.GoalBody(until: "mixdown"));
        RecordEnvelope off = run.Add("goal", run.GoalBody(new JsonObject { ["step.shorts"] = "off" }, until: "shorts"));
        EstateModel model = run.Estate.Load();
        Assert.Contains(Errors(model, unknown), f => f.Code == "flow.until");
        Assert.Contains(Errors(model, off), f => f.Code == "flow.until" && f.Message.Contains("configured off by the goal's plan", StringComparison.Ordinal));
    }

    [Fact]
    public void AnAgentIsNeverNamedInAPersonsRoleNorRoutedToOne()
    {
        using var run = new FlowFixture(run: false);
        RecordEnvelope named = run.Add("goal", run.GoalBody(roles: new JsonArray(FlowFixture.Role("listener", run.Estate.BuilderId))));
        Assert.Contains(Errors(run.Estate.Load(), named), f => f.Code == "flow.holder" && f.Message.StartsWith("Role listener of flow example/song is a person's", StringComparison.Ordinal));

        // The roster names the agent first for the person's role: its step is the operator's, never the agent's.
        using var rostered = new FlowFixture(roster: false);
        rostered.Add("roster", new JsonObject { ["roles"] = new JsonArray(FlowFixture.Role("writer", rostered.Estate.BuilderId), FlowFixture.Role("listener", rostered.Estate.BuilderId, rostered.Estate.OperatorId)) });
        RecordEnvelope lyrics = rostered.Result(rostered.Assign("draft", rostered.Estate.BuilderId), "completed", ("lyrics", FlowFixture.Hash('a'), "text/markdown"));
        rostered.Review(lyrics);
        RecordEnvelope master = rostered.Result(rostered.Assign("master", rostered.Estate.BuilderId, inputs: new JsonArray(FlowFixture.PinOf(lyrics, "lyrics"))), "completed", ("master", FlowFixture.Hash('b'), null));
        foreach (string track in new[] { "intro", "outro" })
        {
            rostered.Result(rostered.Assign("shorts", rostered.Estate.BuilderId, track, new JsonArray(FlowFixture.PinOf(master, "master"))), "completed", ("clip", FlowFixture.Hash('c'), null));
        }

        FlowPosition.StepInstance listen = rostered.Step("listen");
        Assert.Equal(("stopped", rostered.Estate.OperatorId), (listen.State, listen.Principal));
    }

    [Fact]
    public void APlanFlowIsAFieldOfFormatThree()
    {
        using var estate = new TestEstate(recordFormat: 2);
        TemplateDefinition flow = FlowFixture.Install(estate, FlowFixture.Song());
        RecordEnvelope order = estate.Add("work-order", estate.OperatorId, """{"key":"SONG-1","kind":"ordinary","title":"Song","intent":"Song.","scope":["path:song/"],"exitQuestion":"Released?","inputs":[],"deliverables":["song"]}""");
        var plan = new JsonObject { ["pin"] = FlowFixture.Pin(flow), ["subject"] = new JsonObject() };
        RecordEnvelope goal = estate.Add("goal", estate.OperatorId, new JsonObject
        {
            ["title"] = "Song", ["objective"] = "A song.", ["completion"] = new JsonArray(new JsonObject { ["record"] = FlowFixture.Marker("PUBLISHED") }), ["constraints"] = new JsonArray(),
            ["kind"] = "finite", ["sponsor"] = estate.OperatorId, ["budget"] = new JsonObject { ["wallMinutes"] = 60, ["attemptsPerStep"] = 3 },
            ["plan"] = new JsonObject { ["policy"] = "given", ["approval"] = "sponsor-approves", ["workOrders"] = new JsonArray(order.Id), ["flow"] = plan }, ["inputs"] = new JsonArray(),
        }.ToJsonString());
        Assert.Contains(Errors(estate.Load(), goal), f => f.Code == "body.format" && f.Location.EndsWith("plan.flow", StringComparison.Ordinal));
    }

    [Fact]
    public void AReaderWithoutTheFlowsContentShowsTheRunAndRunsNoneOfIt()
    {
        using var run = new FlowFixture();
        RecordEnvelope assignment = run.Assign("draft", run.Estate.BuilderId);
        File.Delete(Directory.GetFiles(Path.Combine(run.Estate.Root, "docs", "templates")).Single());
        EstateModel model = run.Estate.Load();
        Assert.Empty(model.Findings.Where(f => f.Severity == Severity.Error).Select(f => f.ToString()));
        Assert.Contains(model.Findings, f => f.Code == "flow.unavailable" && f.Location == run.Goal.ExpectedPath && f.Message.Contains("none of its steps is run on this machine", StringComparison.Ordinal));
        Assert.Contains(model.Findings, f => f.Code == "flow.unavailable" && f.Location == assignment.ExpectedPath);
        FlowPosition.Position position = run.Position();
        Assert.Null(position.Flow);
        Assert.Empty(position.Steps);
        string shown = GoalView.RenderText(model, model.Latest(run.Goal));
        Assert.Contains("Goal: Release the song", shown, StringComparison.Ordinal);
        Assert.Contains($"Flow: example/song 1.0.0 ({run.Flow.Hash}), not run here: flow example/song 1.0.0 at {run.Flow.Hash} does not resolve here", shown, StringComparison.Ordinal);
        Assert.Null(Routing.RoleOf(model, assignment));
    }

    [Fact]
    public void EveryAssignmentOfARunsWorkNamesOneOfItsWorkOrReviewSteps()
    {
        using var run = new FlowFixture();
        RecordEnvelope unnamed = run.Add("assignment", Brief(run.Estate.BuilderId), subject: run.Work.Reference);
        RecordEnvelope decision = run.Assign("accept", run.Estate.OperatorId);
        RecordEnvelope unknown = run.Assign("mixdown", run.Estate.BuilderId);
        RecordEnvelope itemless = run.Assign("shorts", run.Estate.BuilderId);
        RecordEnvelope stray = run.Assign("shorts", run.Estate.BuilderId, "bridge");
        RecordEnvelope once = run.Assign("draft", run.Estate.BuilderId, "intro");
        RecordEnvelope pinned = run.Assign("draft", run.Estate.BuilderId, inputs: new JsonArray(FlowFixture.PinOf(run.Work, "brief")));
        RecordEnvelope other = run.Marked("OTHER");
        JsonObject elsewhere = Brief(run.Estate.BuilderId);
        elsewhere["step"] = new JsonObject { ["key"] = "draft" };
        RecordEnvelope outside = run.Add("assignment", elsewhere, subject: other.Reference);
        EstateModel model = run.Estate.Load();
        Assert.Contains(Errors(model, unnamed), f => f.Code == "assignment.step" && f.Message.Contains("each assignment of it names the flow step it carries", StringComparison.Ordinal));
        Assert.Contains(Errors(model, decision), f => f.Message == "Step accept is a decision step; an assignment carries a work or review step.");
        Assert.Contains(Errors(model, unknown), f => f.Message == "Flow example/song has no step 'mixdown'.");
        Assert.Contains(Errors(model, itemless), f => f.Message.StartsWith("Step shorts runs once for each of the run's tracks", StringComparison.Ordinal));
        Assert.Contains(Errors(model, stray), f => f.Message.StartsWith("Step shorts runs once for each of the run's tracks", StringComparison.Ordinal));
        Assert.Contains(Errors(model, once), f => f.Message == "Step draft runs once; its assignment names no instance.");
        Assert.Contains(Errors(model, pinned), f => f.Message == "Step draft declares no input 'brief'.");
        Assert.Contains(Errors(model, outside), f => f.Message.StartsWith("A step names a step of a run's flow, and work order OTHER is the work of no run.", StringComparison.Ordinal));
    }

    [Fact]
    public void AReservedStepOrAPersonsRoleIsNeverAssignedToAnAgent()
    {
        JsonObject song = FlowFixture.Song();
        song["steps"]!.AsArray().Select(s => s!.AsObject()).Single(s => s["key"]!.GetValue<string>() == "review-lyrics")["reserved"] = "security-review";
        using var run = new FlowFixture(song);
        RecordEnvelope reserved = run.Assign("review-lyrics", run.Estate.BuilderId);
        RecordEnvelope person = run.Assign("listen", run.Estate.BuilderId);
        RecordEnvelope operatorCarries = run.Assign("review-lyrics", run.Estate.OperatorId);
        EstateModel model = run.Estate.Load();
        Assert.Contains(Errors(model, reserved), f => f.Message == "Step review-lyrics is a reserved act, security-review: it is assigned only to a person who holds it, never to an agent.");
        Assert.Contains(Errors(model, person), f => f.Message == "Step listen is carried in the listener role, which the flow marks a person's: it is never assigned to an agent.");
        Assert.Empty(Errors(model, operatorCarries));
    }

    [Fact]
    public void ALivingDocumentNeverPinsAFlow()
    {
        using var run = new FlowFixture(run: false);
        JsonObject declaration = TemplateFixture.Declaration(run.Estate, run.Flow);
        RecordEnvelope declared = run.Add("living-document", declaration);
        Assert.Contains(Errors(run.Estate.Load(), declared), f => f.Code == "template.kind");
    }

    private static RecordEnvelope Defaults(FlowFixture run, string kind, string? target, JsonObject options)
    {
        var scope = new JsonObject { ["kind"] = kind };
        if (target is not null) scope["target"] = target;
        return run.Add("template-defaults", new JsonObject { ["template"] = FlowFixture.Pin(run.Flow), ["scope"] = scope, ["options"] = options, ["reason"] = "The venture's configuration." });
    }

    private static JsonObject Subject(FlowFixture run, RecordEnvelope product) => new()
    {
        ["recording"] = new JsonObject { ["type"] = "work-order", ["id"] = run.Work.Id }, ["tracks"] = new JsonArray("intro"),
        ["product"] = new JsonObject { ["type"] = "product", ["id"] = product.Id },
    };

    private static JsonObject Brief(string contributor) => new()
    {
        ["contributor"] = contributor,
        ["brief"] = new JsonObject
        {
            ["outcome"] = "Something.", ["sources"] = new JsonArray(), ["permittedChanges"] = new JsonArray("path:song/"), ["prohibitedEffects"] = new JsonArray(),
            ["workspace"] = new JsonArray("song repository"), ["limits"] = new JsonArray(), ["expectedOutputs"] = new JsonArray("something"), ["reviewCriteria"] = new JsonArray("it exists"), ["recovery"] = "Again.",
        },
    };

    private static List<Finding> Errors(EstateModel model, RecordEnvelope record) =>
        model.Findings.Where(f => f.Severity == Severity.Error && f.Location.StartsWith(record.ExpectedPath, StringComparison.Ordinal)).ToList();
}
