// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json.Nodes;
using Vericairn.Records.Estate;
using Vericairn.Records.Model;

namespace Vericairn.Records.Tests;

/// <summary>
/// The items a step closes on (the estate's design/flow.md § 5, revision 4, decision 01M6HB2QZ3V95J49580XEJ5MBD): each item's provenance,
/// evidence and review, the step's review its default, derived from the records; golden cases of a satisfied, missing, unproven,
/// unverified, stale and configured-off item, a step that closes only when every required item is satisfied and reopens when one
/// changes after its review, and a configuration that never turns off a required or policy-required item.
/// </summary>
public class FlowItemTests
{
    private const string Commit = "0123456789abcdef0123456789abcdef01234567";

    /// <summary>A flow whose draft closes on the lyrics (reviewed, verified, by an agent writer), optional notes and an optional sketch.</summary>
    private static JsonObject Flow()
    {
        JsonObject song = FlowFixture.Song();
        JsonObject draft = song["steps"]![0]!.AsObject();
        draft["review"] = "independent";
        draft["outputs"] = new JsonArray(
            new JsonObject
            {
                ["name"] = "lyrics", ["kind"] = "file", ["mediaType"] = "text/markdown", ["required"] = true,
                ["provenance"] = new JsonObject { ["holder"] = "agent", ["role"] = "writer" },
                ["evidence"] = new JsonArray(new JsonObject { ["name"] = "spelling", ["method"] = "spellcheck" }),
            },
            new JsonObject { ["name"] = "notes", ["kind"] = "file", ["optional"] = true, ["default"] = "on", ["review"] = "none" },
            new JsonObject { ["name"] = "sketch", ["kind"] = "file", ["optional"] = true, ["default"] = "off" });
        JsonObject accept = song["steps"]!.AsArray().Select(s => s!.AsObject()).Single(s => s["key"]!.GetValue<string>() == "accept");
        accept["outputs"] = new JsonArray(new JsonObject { ["name"] = "sign-off", ["kind"] = "attestation", ["recordType"] = "work-order", ["required"] = true });
        return song;
    }

    [Fact]
    public void AStepClosesOnlyWhenEveryRequiredItemHoldsItsProvenanceEvidenceAndReview()
    {
        Assert.Empty(Vericairn.Records.Templates.TemplateDefinition.Validate(FlowFixture.Element(Flow())).Select(f => f.ToString()));
        using var run = new FlowFixture(Flow(), new JsonObject { ["step.review-lyrics"] = "off" });
        Assert.Empty(run.Estate.Load().Findings.Where(f => f.Severity == Severity.Error).Select(f => f.ToString()));
        Assert.True(run.Position().Flow is not null, run.Position().Unavailable);
        RecordEnvelope drafting = run.Assign("draft", run.Estate.BuilderId);

        // Missing: the result names no lyrics; the optional sketch is off by the flow's default.
        run.Result(drafting, "completed", ("notes", FlowFixture.Hash('e'), null));
        FlowPosition.StepInstance draft = run.Step("draft");
        Assert.Equal("in-review", draft.State);
        Assert.Equal(["lyrics missing", "notes satisfied", "sketch off"], Items(draft));
        Assert.Equal("item lyrics is missing: its result names no output lyrics", draft.Reasons[0]);
        Assert.Equal("off by the flow's default", Item(draft, "sketch").Reasons[0]);

        // Unverified, then in review: the lyrics exist; their verification and then their independent review are what remain.
        RecordEnvelope first = run.Result(drafting, "completed", ("lyrics", FlowFixture.Hash('a'), "text/markdown"), ("notes", FlowFixture.Hash('e'), null));
        Assert.Equal("unverified", Item(run.Step("draft"), "lyrics").State);
        Assert.StartsWith("its evidence spelling has no passing verification by spellcheck at its exact hash", Item(run.Step("draft"), "lyrics").Reasons[0], StringComparison.Ordinal);
        RecordEnvelope verified = Verify(run, first, "spellcheck");
        FlowPosition.ItemState lyrics = Item(run.Step("draft"), "lyrics");
        Assert.Equal(("in-review", "it awaits an independent review at its exact hash"), (lyrics.State, lyrics.Reasons[0]));
        Assert.Equal(verified.Id, Assert.Single(lyrics.Evidence).Id);

        // Satisfied: an independent review passes the exact result; the step closes.
        RecordEnvelope review = run.Review(first);
        draft = run.Step("draft");
        Assert.Equal("done", draft.State);
        Assert.Equal(["lyrics satisfied", "notes satisfied", "sketch off"], Items(draft));
        Assert.Equal((review.Id, run.Estate.BuilderId), (Item(draft, "lyrics").Review!.Id, Item(draft, "lyrics").Producer));

        // The same lyrics in a new result keep their review and evidence: they were reviewed and verified at that exact hash.
        run.Result(drafting, "completed", ("lyrics", FlowFixture.Hash('a'), "text/markdown"), ("notes", FlowFixture.Hash('f'), null));
        Assert.Equal("done", run.Step("draft").State);

        // Stale: lyrics that changed after their review reopen the step until the new version is verified and reviewed.
        RecordEnvelope changed = run.Result(drafting, "completed", ("lyrics", FlowFixture.Hash('c'), "text/markdown"), ("notes", FlowFixture.Hash('f'), null));
        Verify(run, changed, "spellcheck");
        draft = run.Step("draft");
        Assert.Equal("in-review", draft.State);
        lyrics = Item(draft, "lyrics");
        Assert.Equal("stale", lyrics.State);
        Assert.Equal($"it changed after review {review.Id} passed an earlier version, and awaits an independent review at its exact hash", lyrics.Reasons[0]);
        Assert.Equal($"item lyrics is stale: {lyrics.Reasons[0]}", draft.Reasons[0]);
        run.Review(changed);
        Assert.Equal("done", run.Step("draft").State);
    }

    [Fact]
    public void AnItemsProvenanceNamesItsProducerItsPinnedInputsAndItsTool()
    {
        JsonObject flow = Flow();
        JsonObject master = flow["steps"]!.AsArray().Select(s => s!.AsObject()).Single(s => s["key"]!.GetValue<string>() == "master");
        master["outputs"]![0]!["provenance"] = new JsonObject { ["inputs"] = new JsonArray("lyrics"), ["tool"] = "claude-code" };
        using (var byPerson = new FlowFixture(flow, new JsonObject { ["step.review-lyrics"] = "off" }))
        {
            // Produced by the operator, a person, where the writer's agent was required.
            byPerson.Result(byPerson.Assign("draft", byPerson.Estate.OperatorId), "completed", ("lyrics", FlowFixture.Hash('a'), "text/markdown"));
            FlowPosition.ItemState lyrics = Item(byPerson.Step("draft"), "lyrics");
            Assert.Equal("unproven", lyrics.State);
            Assert.Contains("it is to be produced by an agent, and Test Operator produced it", lyrics.Reasons);
            Assert.Contains("its evidence spelling has no passing verification by spellcheck at its exact hash", lyrics.Reasons);
        }

        // The agent's lyrics, verified and reviewed: the master's assignment pins none of them, and no run of a launcher produced it.
        using var run = new FlowFixture(flow, new JsonObject { ["step.review-lyrics"] = "off" });
        RecordEnvelope byAgent = run.Result(run.Assign("draft", run.Estate.BuilderId), "completed", ("lyrics", FlowFixture.Hash('b'), "text/markdown"), ("notes", FlowFixture.Hash('e'), null));
        Verify(run, byAgent, "spellcheck");
        run.Review(byAgent);
        Assert.Equal("done", run.Step("draft").State);
        run.Result(run.Assign("master", run.Estate.BuilderId), "completed", ("master", FlowFixture.Hash('d'), null));
        FlowPosition.ItemState mastered = Item(run.Step("master"), "master");
        Assert.Equal("unproven", mastered.State);
        Assert.Equal(["it is to come from input lyrics, and the record that produced it pins no hash of it", "it is to be produced with tool claude-code, and the record that produced it shows none"], mastered.Reasons);
    }

    [Fact]
    public void AProvenancePrincipalThatIsNoPrincipalOfTheEstateLeavesTheItemUnproven()
    {
        JsonObject flow = Flow();
        JsonObject lyrics = flow["steps"]![0]!["outputs"]![0]!.AsObject();
        lyrics.Remove("evidence");
        lyrics["review"] = "none";
        string unknown = "01" + new string('Z', 24);
        lyrics["provenance"] = new JsonObject { ["principal"] = unknown };
        using var run = new FlowFixture(flow, new JsonObject { ["step.review-lyrics"] = "off" });
        run.Result(run.Assign("draft", run.Estate.BuilderId), "completed", ("lyrics", FlowFixture.Hash('a'), "text/markdown"));
        FlowPosition.ItemState item = Item(run.Step("draft"), "lyrics");
        Assert.Equal("unproven", item.State);
        Assert.Equal($"it is to be produced by {unknown}, which is not a principal of this estate", item.Reasons[0]);
        Assert.NotEqual("done", run.Step("draft").State);
    }

    [Fact]
    public void ARecordItemsIndependentReviewExcludesTheStepsOwnHolders()
    {
        // The master step also closes on a record item, the work order its done predicate counts, which the operator records; the builder
        // holds the step.
        JsonObject flow = Flow();
        flow["steps"]![0]!["outputs"]![0]!.AsObject().Remove("evidence");
        flow["steps"]![0]!["outputs"]![0]!["review"] = "none";
        JsonObject master = flow["steps"]!.AsArray().Select(s => s!.AsObject()).Single(s => s["key"]!.GetValue<string>() == "master");
        master["done"] = new JsonArray(FlowFixture.Marker("MIXED"));
        master["outputs"]!.AsArray().Add(new JsonObject { ["name"] = "mix", ["kind"] = "record", ["recordType"] = "work-order", ["required"] = true, ["review"] = "independent" });
        using var run = new FlowFixture(flow, new JsonObject { ["step.review-lyrics"] = "off" });
        RecordEnvelope lyrics = run.Result(run.Assign("draft", run.Estate.BuilderId), "completed", ("lyrics", FlowFixture.Hash('a'), "text/markdown"), ("notes", FlowFixture.Hash('e'), null));
        Assert.Equal("done", run.Step("draft").State);
        run.Result(run.Assign("master", run.Estate.BuilderId, inputs: new JsonArray(FlowFixture.PinOf(lyrics, "lyrics"))), "completed", ("master", FlowFixture.Hash('b'), null));
        RecordEnvelope mixed = run.Marked("MIXED");
        Assert.Equal(("in-review", run.Estate.OperatorId), (Item(run.Step("master"), "mix").State, Item(run.Step("master"), "mix").Producer));

        // The builder did not record the work order, but holds the step: its pass is not independent of the step's own work.
        RecordEnvelope outsider = run.Add("principal", new JsonObject { ["kind"] = "agent", ["name"] = "Outside Reviewer", ["roles"] = new JsonArray("reviewer"), ["agent"] = new JsonObject { ["vendor"] = "Another", ["product"] = "Another Agent" } });
        AgentReview(run, mixed, run.Estate.BuilderId);
        Assert.Equal("in-review", Item(run.Step("master"), "mix").State);
        Assert.NotEqual("done", run.Step("master").State);
        AgentReview(run, mixed, outsider.Id);
        Assert.Equal("satisfied", Item(run.Step("master"), "mix").State);
        Assert.Equal("done", run.Step("master").State);
    }

    [Fact]
    public void AnItemsOwnReviewOverridesTheStepsAndAnotherHarnessIsAnotherAgentProduct()
    {
        JsonObject flow = Flow();
        flow["steps"]![0]!["outputs"]![0]!.AsObject().Remove("evidence");
        flow["steps"]![0]!["outputs"]![0]!["review"] = "other-harness";
        using var run = new FlowFixture(flow, new JsonObject { ["step.review-lyrics"] = "off" });
        RecordEnvelope lyrics = run.Result(run.Assign("draft", run.Estate.BuilderId), "completed", ("lyrics", FlowFixture.Hash('a'), "text/markdown"));
        RecordEnvelope same = run.Add("principal", new JsonObject { ["kind"] = "agent", ["name"] = "Same Harness", ["roles"] = new JsonArray("reviewer"), ["agent"] = new JsonObject { ["vendor"] = "Example", ["product"] = "Example Agent" } });
        RecordEnvelope other = run.Add("principal", new JsonObject { ["kind"] = "agent", ["name"] = "Other Harness", ["roles"] = new JsonArray("reviewer"), ["agent"] = new JsonObject { ["vendor"] = "Another", ["product"] = "Another Agent" } });

        // The operator's review satisfies the step's independent review of the notes item, never the lyrics' review on another harness.
        run.Review(lyrics);
        Assert.Equal("in-review", Item(run.Step("draft"), "lyrics").State);
        Assert.Equal("it awaits an independent review on another harness at its exact hash", Item(run.Step("draft"), "lyrics").Reasons[0]);
        AgentReview(run, lyrics, same.Id);
        Assert.Equal("in-review", Item(run.Step("draft"), "lyrics").State);
        AgentReview(run, lyrics, other.Id);
        Assert.Equal("satisfied", Item(run.Step("draft"), "lyrics").State);
    }

    [Fact]
    public void AnAttestationIsAPersonsAndOnlyAnOptionalItemIsConfiguredOff()
    {
        using var run = new FlowFixture(Flow(), run: false);
        RecordEnvelope required = run.Add("goal", run.GoalBody(new JsonObject { ["item.draft.lyrics"] = "off" }));
        RecordEnvelope unknown = run.Add("goal", run.GoalBody(new JsonObject { ["item.draft.chorus"] = "off" }));
        RecordEnvelope optional = run.Add("goal", run.GoalBody(new JsonObject { ["item.draft.notes"] = "off", ["item.draft.sketch"] = "on" }));
        JsonObject body = run.Policies.PolicyBody("song-notes", duty: false);
        body["rules"] = new JsonArray(new JsonObject { ["key"] = "notes", ["kind"] = FlowRuns.PolicyKind, ["version"] = 1, ["parameters"] = new JsonObject { ["flow"] = "example/song", ["items"] = new JsonArray("draft.notes") } });
        run.Policies.Policy(body);
        RecordEnvelope policed = run.Add("goal", run.GoalBody(new JsonObject { ["item.draft.notes"] = "off" }));
        EstateModel model = run.Estate.Load();
        Assert.Contains(Errors(model, required), f => f.Code == "flow.required" && f.Message == "Item lyrics of step draft is required: configuration turns off only an optional item.");
        Assert.Contains(Errors(model, unknown), f => f.Code == "flow.option");
        Assert.Empty(Errors(model, optional));
        Assert.Contains(Errors(model, policed), f => f.Code == "flow.policy" && f.Message.StartsWith("Item notes of step draft is required by estate policy song-notes", StringComparison.Ordinal));
        FlowRuns.Configuration configured = FlowRuns.Configure(model, optional, FlowRuns.Resolve(model, optional).Flow!);
        Assert.Equal("configured off by the goal's plan", configured.ItemsOff["draft.notes"]);
        Assert.False(configured.ItemsOff.ContainsKey("draft.sketch"));

        // The acceptance's sign-off is an attestation of a reserved act: the operator's record counts, an agent's does not count at all.
        Assert.Equal(["sign-off missing"], Items(FlowPosition.Derive(model, optional, run.Estate.Clock).Steps.Single(s => s.Step.Key == "accept")));
        run.Add("work-order", new JsonObject
        {
            ["key"] = "ACCEPTED", ["kind"] = "ordinary", ["title"] = "Signed by the builder", ["intent"] = "A stand-in.", ["scope"] = new JsonArray("marker"),
            ["exitQuestion"] = "Recorded?", ["inputs"] = new JsonArray(), ["deliverables"] = new JsonArray("marker"),
        }, builder: true);
        FlowPosition.ItemState unsigned = Item(FlowPosition.Derive(run.Estate.Load(), optional, run.Estate.Clock).Steps.Single(s => s.Step.Key == "accept"), "sign-off");
        Assert.Equal("missing", unsigned.State);
        Assert.EndsWith("recorded by a principal who does not hold the act, does not count", unsigned.Reasons[0], StringComparison.Ordinal);
    }

    [Fact]
    public void AResultsRecordedProvenanceShowsItsInputsToolAndSettings()
    {
        JsonObject flow = Flow();
        JsonObject master = flow["steps"]!.AsArray().Select(s => s!.AsObject()).Single(s => s["key"]!.GetValue<string>() == "master");
        master["outputs"]![0]!["provenance"] = new JsonObject { ["inputs"] = new JsonArray("lyrics"), ["tool"] = "example-tool", ["settings"] = new JsonObject { ["effort"] = "high" } };
        using var run = new FlowFixture(flow, new JsonObject { ["step.review-lyrics"] = "off" });
        RecordEnvelope lyrics = run.Result(run.Assign("draft", run.Estate.BuilderId), "completed", ("lyrics", FlowFixture.Hash('b'), "text/markdown"), ("notes", FlowFixture.Hash('e'), null));
        Verify(run, lyrics, "spellcheck");
        run.Review(lyrics);
        RecordEnvelope mastering = run.Assign("master", run.Estate.BuilderId);
        JsonObject Recorded(string effort) => new() { ["inputs"] = new JsonArray(FlowFixture.PinOf(lyrics, "lyrics")), ["tool"] = "example-tool", ["settings"] = new JsonObject { ["effort"] = effort } };

        run.ResultWith(mastering, Recorded("low"), "completed", ("master", FlowFixture.Hash('d'), null));
        FlowPosition.ItemState item = Item(run.Step("master"), "master");
        Assert.Equal(("unproven", "it is to be produced with setting effort high, and the record that produced it shows low"), (item.State, Assert.Single(item.Reasons)));
        run.ResultWith(mastering, Recorded("high"), "completed", ("master", FlowFixture.Hash('d'), null));
        Assert.Equal("satisfied", Item(run.Step("master"), "master").State);
        Assert.Equal("done", run.Step("master").State);
        Assert.Empty(run.Estate.Load().Findings.Where(f => f.Severity == Severity.Error).Select(f => f.ToString()));
    }

    [Fact]
    public void AnExtensionTurnsOffOnlyAnOptionalItem()
    {
        Templates.TemplateDefinition song = Templates.TemplateDefinition.Read(FlowFixture.Element(Flow()));
        Templates.FlowDefinition Extend(string off) => Templates.FlowDefinition.Resolve(Templates.TemplateDefinition.Read(FlowFixture.Element(new JsonObject
        {
            ["format"] = "vericairn.template/1", ["kind"] = "flow", ["id"] = "example/song-plain", ["version"] = "1.0.0", ["purpose"] = "The song without notes.",
            ["extends"] = FlowFixture.Pin(song), ["off"] = new JsonArray(off),
        })), pin => pin.Hash == song.Hash ? song : throw new InvalidOperationException("No such base."));
        Templates.FlowDefinition plain = Extend("draft.notes");
        Assert.True(plain.TurnedOffItems.ContainsKey("draft.notes"));
        Assert.False(plain.Options.ContainsKey("item.draft.notes"));
        Assert.True(plain.Options.ContainsKey("item.draft.sketch"));
        Assert.Contains("turns off item 'draft.lyrics', a required item", Assert.Throws<InvalidOperationException>(() => Extend("draft.lyrics")).Message, StringComparison.Ordinal);
    }

    private static RecordEnvelope Verify(FlowFixture run, RecordEnvelope result, string method) =>
        run.Add("verification", new JsonObject
        {
            ["method"] = method, ["commit"] = Commit, ["environment"] = new JsonObject { ["platform"] = "test" }, ["role"] = "operator", ["outcome"] = "passed",
            ["summary"] = "Checked the exact result.", ["evidence"] = new JsonArray(), ["limitations"] = new JsonArray(),
        }, subject: result.Reference);

    private static void AgentReview(FlowFixture run, RecordEnvelope result, string reviewer) =>
        run.Estate.Add("review", reviewer, new JsonObject { ["independence"] = "independent-agent", ["outcome"] = "pass", ["summary"] = "Reviewed on its harness.", ["findings"] = new JsonArray() }.ToJsonString(), result.Reference);

    private static FlowPosition.ItemState Item(FlowPosition.StepInstance step, string name) => Assert.Single(step.Items, i => i.Declared.Name == name);

    private static List<string> Items(FlowPosition.StepInstance step) => step.Items.Select(i => $"{i.Declared.Name} {i.State}").ToList();

    private static List<Finding> Errors(EstateModel model, RecordEnvelope record) =>
        model.Findings.Where(f => f.Severity == Severity.Error && f.Location.StartsWith(record.ExpectedPath, StringComparison.Ordinal)).ToList();
}
