// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json.Nodes;
using Vericairn.Records.Estate;
using Vericairn.Records.Model;
using Vericairn.Records.Templates;

namespace Vericairn.Records.Tests;

/// <summary>
/// The hand-off contract, review routing and failure routes (the estate's design/flow.md §§ 5 to 7): an item that feeds a reserved step
/// takes an independent review by default, a review before hand-off waits for its reviewer role and never for an author, retries go by
/// outcome within the goal's attempts per step, a failed step takes its failure route, a spent budget and the run's own limits become
/// required attention, and every attempt stays recorded.
/// </summary>
public class FlowHandOffTests
{
    [Fact]
    public void AnItemThatFeedsAReservedStepTakesAnIndependentReviewByDefaultThatConfigurationNeverLowers()
    {
        JsonObject flow = FlowFixture.Song();
        JsonObject accept = Step(flow, "accept");
        accept["inputs"] = new JsonArray(FlowFixture.Input("master", "master", "master"));
        using var run = new FlowFixture(flow, new JsonObject { ["review"] = "none", ["step.review-lyrics"] = "off", ["step.shorts"] = "off" });
        FlowDefinition resolved = run.Position().Flow!;
        Assert.Equal("it feeds step accept, a reserved act (accept-release)", FlowRuns.Consequential(resolved, resolved.Step("master")!, resolved.Step("master")!.Outputs[0]));
        Assert.Equal("it feeds step accept, a reserved act (accept-release)", FlowRuns.Consequential(resolved, resolved.Step("draft")!, resolved.Step("draft")!.Outputs[0]));

        // The goal configures the flow's review option to none; the lyrics, which reach acceptance through the master, still await an
        // independent review before the master takes them.
        RecordEnvelope lyrics = run.Result(run.Assign("draft", run.Estate.BuilderId), "completed", ("lyrics", FlowFixture.Hash('a'), "text/markdown"));
        FlowPosition.StepInstance draft = run.Step("draft");
        Assert.Equal("in-review", draft.State);
        Assert.Equal("item lyrics is in-review: it awaits an independent review at its exact hash", draft.Reasons[0]);
        FlowPosition.PendingReview pending = Assert.Single(draft.Reviews);
        Assert.Equal(["lyrics"], pending.Items);
        Assert.Equal(("independent", "reviewer", lyrics.Id), (pending.Requirement.Independence, pending.Role, pending.Subject.Id));
        Assert.Contains(run.Estate.BuilderId, pending.Authors);
        Assert.Equal("blocked", run.Step("master").State);
        run.Review(lyrics);
        Assert.Equal("done", run.Step("draft").State);

        // A flow that declares the item's review none is taken at its word.
        JsonObject plain = FlowFixture.Song();
        Step(plain, "accept")["inputs"] = new JsonArray(FlowFixture.Input("master", "master", "master"));
        Step(plain, "draft")["outputs"]![0]!["review"] = "none";
        using var declared = new FlowFixture(plain, new JsonObject { ["step.review-lyrics"] = "off" });
        declared.Result(declared.Assign("draft", declared.Estate.BuilderId), "completed", ("lyrics", FlowFixture.Hash('a'), "text/markdown"));
        Assert.Equal("done", declared.Step("draft").State);
    }

    [Fact]
    public void AReviewBeforeHandOffWaitsForItsReviewerRoleAndItsAuthorIsRefusedAsItsReviewer()
    {
        using var run = new FlowFixture(options: new JsonObject { ["review"] = "independent", ["step.review-lyrics"] = "off" });
        RecordEnvelope drafting = run.Assign("draft", run.Estate.BuilderId);
        RecordEnvelope lyrics = run.Result(drafting, "completed", ("lyrics", FlowFixture.Hash('a'), "text/markdown"));
        FlowPosition.PendingReview pending = Assert.Single(run.Step("draft").Reviews);
        Assert.Empty(pending.Assignments);

        // The builder authored the lyrics: an assignment of their review to the builder is refused at the record.
        RecordEnvelope own = ReviewAssignment(run, run.Estate.BuilderId, lyrics, "lyrics");
        Assert.Contains(Errors(run, own), f => f.Code == "assignment.review" && f.Message.Contains("it authored the step's work", StringComparison.Ordinal));

        // The operator, the reviewer role's principal, is assigned the review: the assignment carries it, and the operator authors nothing.
        RecordEnvelope reviewing = ReviewAssignment(run, run.Estate.OperatorId, lyrics, "lyrics");
        Assert.Empty(Errors(run, reviewing));
        Assert.Equal(reviewing.Id, Assert.Single(run.Step("draft").Reviews).Assignments[^1].Id);
        Assert.Equal(drafting.Id, run.Step("draft").Assignment!.Id);

        // An agent outside the reviewer role passes the lyrics: it is not the author, and its pass still clears nothing.
        RecordEnvelope outsider = run.Add("principal", new JsonObject { ["kind"] = "agent", ["name"] = "Outside Agent", ["roles"] = new JsonArray("contributor"), ["agent"] = new JsonObject { ["vendor"] = "Another", ["product"] = "Another Agent" } });
        AgentReview(run, lyrics, outsider.Id, "pass");
        Assert.Equal("in-review", run.Step("draft").State);
        AgentReview(run, lyrics, outsider.Id, "findings");
        Assert.Equal("in-review", run.Step("draft").State);

        run.Review(lyrics);
        Assert.Equal("done", run.Step("draft").State);
        Assert.Empty(run.Step("draft").Reviews);
        Assert.Empty(run.Estate.Load().Findings.Where(f => f.Severity == Severity.Error && f.Location != own.ExpectedPath).Select(f => f.ToString()));
    }

    [Fact]
    public void AReviewAssignmentMeetsItsIndependenceAgainstTheStepsAuthors()
    {
        JsonObject flow = FlowFixture.Song();
        JsonObject draft = Step(flow, "draft");
        draft["outputs"] = new JsonArray(
            Item("lyrics", "other-harness"), Item("notes", "person"), Item("sketch", "independent"));
        using var run = new FlowFixture(flow, new JsonObject { ["step.review-lyrics"] = "off" });
        RecordEnvelope lyrics = run.Result(run.Assign("draft", run.Estate.BuilderId), "completed", ("lyrics", FlowFixture.Hash('a'), null), ("notes", FlowFixture.Hash('b'), null), ("sketch", FlowFixture.Hash('c'), null));
        RecordEnvelope same = run.Add("principal", new JsonObject { ["kind"] = "agent", ["name"] = "Same Harness", ["roles"] = new JsonArray("reviewer"), ["agent"] = new JsonObject { ["vendor"] = "Example", ["product"] = "Example Agent" } });
        RecordEnvelope other = run.Add("principal", new JsonObject { ["kind"] = "agent", ["name"] = "Other Harness", ["roles"] = new JsonArray("reviewer"), ["agent"] = new JsonObject { ["vendor"] = "Another", ["product"] = "Another Agent" } });

        // Another harness than the builder's: the same product is refused, another is not.
        Assert.Contains(Errors(run, ReviewAssignment(run, same.Id, lyrics, "lyrics")), f => f.Code == "assignment.review" && f.Message.Contains("runs on Example Agent, as they did", StringComparison.Ordinal));
        Assert.Empty(Errors(run, ReviewAssignment(run, other.Id, lyrics, "lyrics")));

        // A person's review is never an agent's.
        Assert.Contains(Errors(run, ReviewAssignment(run, other.Id, lyrics, "notes")), f => f.Code == "assignment.review" && f.Message.Contains("the review is a person's, and it is not a person", StringComparison.Ordinal));
        Assert.Empty(Errors(run, ReviewAssignment(run, run.Estate.OperatorId, lyrics, "notes")));

        // One review carries items of one requirement, of the step it names, and never a review step's own review.
        Assert.Contains(Errors(run, ReviewAssignment(run, run.Estate.OperatorId, lyrics, "notes", "sketch")), f => f.Code == "assignment.review" && f.Message.StartsWith("The items of one review take one review requirement", StringComparison.Ordinal));
        Assert.Contains(Errors(run, ReviewAssignment(run, run.Estate.OperatorId, lyrics, "chorus")), f => f.Code == "assignment.review" && f.Message == "Step draft declares no item 'chorus'.");
        Assert.Contains(Errors(run, StepReviewAssignment(run, run.Estate.OperatorId, lyrics, "review-lyrics")), f => f.Code == "assignment.review" && f.Message.StartsWith("Step review-lyrics is a review step", StringComparison.Ordinal));

        // A review names the record that carries the items: never a result of another step's work.
        RecordEnvelope foreign = run.Result(run.Assign("master", run.Estate.BuilderId), "completed", ("master", FlowFixture.Hash('d'), null));
        Assert.Contains(Errors(run, ReviewAssignment(run, run.Estate.OperatorId, foreign, "notes")), f => f.Code == "assignment.review" && f.Message.EndsWith($"result {foreign.Id} is neither.", StringComparison.Ordinal));

        // The position groups what each review covers by subject and requirement.
        Assert.Equal(["lyrics other-harness", "notes person", "sketch independent"], run.Step("draft").Reviews.Select(r => $"{string.Join(",", r.Items)} {r.Requirement.Independence}").Order(StringComparer.Ordinal));
    }

    [Fact]
    public void ASelfReviewIsTheAuthorsOwnAndAnIndependentOneNeverIs()
    {
        JsonObject flow = FlowFixture.Song();
        Step(flow, "draft")["outputs"] = new JsonArray(Item("lyrics", "self"), Item("notes", "independent"));
        using var run = new FlowFixture(flow, new JsonObject { ["step.review-lyrics"] = "off" });
        RecordEnvelope drafted = run.Result(run.Assign("draft", run.Estate.BuilderId), "completed", ("lyrics", FlowFixture.Hash('a'), null), ("notes", FlowFixture.Hash('b'), null));
        run.Add("review", new JsonObject { ["independence"] = "self", ["outcome"] = "pass", ["summary"] = "My own check of the lyrics and notes.", ["findings"] = new JsonArray() }, builder: true, subject: drafted.Reference);
        FlowPosition.StepInstance draft = run.Step("draft");
        Assert.Equal(["lyrics satisfied", "notes in-review"], draft.Items.Select(i => $"{i.Declared.Name} {i.State}"));
        Assert.Empty(Errors(run, ReviewAssignment(run, run.Estate.BuilderId, drafted, "lyrics")));
        Assert.Contains(Errors(run, ReviewAssignment(run, run.Estate.BuilderId, drafted, "notes")), f => f.Code == "assignment.review");
        run.Review(drafted);
        Assert.Equal("done", run.Step("draft").State);
    }

    [Fact]
    public void AnUncertainEffectStopsTheStepUntilItIsReconciledByReading()
    {
        JsonObject flow = FlowFixture.Song();
        JsonObject upload = FlowFixture.Step("upload", "operation", "writer", done: new JsonArray(new JsonObject { ["type"] = "operation", ["fields"] = new JsonObject { ["identity"] = "example-provider:upload-1", ["status"] = "observed-success" } }));
        upload["after"] = new JsonArray();
        flow["steps"]!.AsArray().Add(upload);
        using var run = new FlowFixture(flow, new JsonObject { ["step.review-lyrics"] = "off" });
        RecordEnvelope drafting = run.Assign("draft", run.Estate.BuilderId);
        RecordEnvelope lyrics = ResultWithEffects(run, drafting, "the provider's reply to the upload was lost");
        FlowPosition.StepInstance draft = run.Step("draft");
        Assert.Equal("stopped", draft.State);
        Assert.StartsWith($"required attention: an uncertain effect: result {lyrics.Id} names effects whose outcome is not known (the provider's reply to the upload was lost)", draft.Reasons[0], StringComparison.Ordinal);
        Assert.Equal(("uncertain", lyrics.Id), (draft.Attention!.Kind, draft.Attention.Trigger.Id));
        Assert.Equal("blocked", run.Step("master").State);

        // Reconciled by reading: the result is revised without the effect, and the step closes.
        JsonObject reconciled = PolicyFixture.Body(lyrics);
        reconciled["unresolvedEffects"] = new JsonArray();
        reconciled["summary"] = "Read the provider: the upload exists once.";
        run.Add("result", reconciled, builder: true, subject: lyrics.Subject, supersedes: lyrics.Id);
        Assert.Equal("done", run.Step("draft").State);

        // A step carried by its action stops on an uncertain operation its done predicates read.
        RecordEnvelope operation = run.Add("operation", new JsonObject
        {
            ["provider"] = "example-provider", ["action"] = "upload", ["identity"] = "example-provider:upload-1", ["intent"] = "Upload the master.",
            ["target"] = new JsonObject { ["repository"] = "Example/song" }, ["status"] = "uncertain", ["dispatchedAt"] = RecordParser.FormatTimestamp(run.Estate.Clock), ["evidence"] = new JsonArray(),
        });
        FlowPosition.StepInstance uploading = run.Step("upload");
        Assert.Equal("stopped", uploading.State);
        Assert.StartsWith($"required attention: an uncertain effect: operation {operation.Id} (example-provider:upload-1) is uncertain", uploading.Reasons[0], StringComparison.Ordinal);
    }

    [Fact]
    public void AFailedRunBacksOffFindingsRetryWithinTheAttemptsAndASpentBudgetStopsOnlyWhatDependsOnIt()
    {
        JsonObject flow = FlowFixture.Song();
        JsonObject notes = FlowFixture.Step("notes", "work", "writer", optional: "on");
        notes["after"] = new JsonArray();
        flow["steps"]!.AsArray().Add(notes);
        using var run = new FlowFixture(flow, new JsonObject { ["step.review-lyrics"] = "off" });
        RecordEnvelope drafting = run.Assign("draft", run.Estate.BuilderId);

        // The first run fails without a result: the step backs off a minute, then goes on with another run.
        RecordEnvelope failed = AgentRun(run, drafting, "first", "known-failure");
        FlowPosition.StepInstance draft = run.Step("draft", failed.AtTime);
        Assert.Equal("blocked", draft.State);
        Assert.StartsWith($"it backs off after run ", draft.Reasons[0], StringComparison.Ordinal);
        Assert.Contains($"another run after {RecordParser.FormatTimestamp(failed.AtTime.AddMinutes(1))}, within its attempts (1 of 3 used)", draft.Reasons[0], StringComparison.Ordinal);
        draft = run.Step("draft", failed.AtTime.AddMinutes(2));
        Assert.Equal("next", draft.State);
        Assert.Contains("goes on with it after run ", draft.Reasons[0], StringComparison.Ordinal);

        // A second failed run in a row doubles the wait.
        RecordEnvelope again = AgentRun(run, drafting, "second", "known-failure");
        Assert.Contains($"another run after {RecordParser.FormatTimestamp(again.AtTime.AddMinutes(2))}", run.Step("draft", again.AtTime.AddMinutes(1)).Reasons[0], StringComparison.Ordinal);

        // The third run records a result, and a review finds what to change: the three attempts per step are spent, and the step stops
        // for the sponsor; what waits on it waits, and the notes, which do not, go on.
        AgentRun(run, drafting, "third", "observed-success");
        RecordEnvelope lyrics = run.Result(drafting, "completed", ("lyrics", FlowFixture.Hash('a'), "text/markdown"));
        RecordEnvelope findings = run.Review(lyrics, "findings");
        draft = run.Step("draft", run.Estate.Clock.AddHours(2));
        Assert.Equal("stopped", draft.State);
        Assert.StartsWith("required attention: its retry budget is spent: its work has had 3 of the 3 runs the goal's attempts per step grant", draft.Reasons[0], StringComparison.Ordinal);
        Assert.Equal(("exhausted", findings.Id), (draft.Attention!.Kind, draft.Attention.Trigger.Id));
        Assert.Equal((3, 3), (draft.AttemptsUsed, draft.AttemptsAllowed));
        Assert.Equal("blocked", run.Step("master").State);
        Assert.Equal("next", run.Step("notes").State);

        // Nothing is lost: the attempt and its result stay recorded and shown with the step.
        FlowPosition.AttemptState attempt = Assert.Single(draft.Attempts);
        Assert.Equal((drafting.Id, lyrics.Id, "completed"), (attempt.Assignment.Id, attempt.Result!.Id, attempt.Outcome));
        Assert.Contains("attempts: 1 recorded, 3 of 3 agent runs used", FlowPosition.RenderText(run.Estate.Load(), run.Position()), StringComparison.Ordinal);
    }

    [Fact]
    public void AFailedStepReturnsTheRunToAnEarlierStepAndKeepsEveryAttempt()
    {
        JsonObject flow = FlowFixture.Song();
        Step(flow, "master")["failure"] = new JsonObject { ["route"] = "step", ["step"] = "draft" };
        using var run = new FlowFixture(flow, new JsonObject { ["step.review-lyrics"] = "off", ["step.shorts"] = "off" });
        RecordEnvelope first = run.Result(run.Assign("draft", run.Estate.BuilderId), "completed", ("lyrics", FlowFixture.Hash('a'), "text/markdown"));
        RecordEnvelope mastering = run.Assign("master", run.Estate.BuilderId, inputs: new JsonArray(FlowFixture.PinOf(first, "lyrics")));
        RecordEnvelope master = run.Result(mastering, "completed", ("master", FlowFixture.Hash('b'), null));
        RecordEnvelope failed = run.Review(master, "fail");

        // The master failed: the run returns to draft, which reruns from a fresh assignment, and the master waits for it.
        FlowPosition.StepInstance failing = run.Step("master");
        Assert.Equal("blocked", failing.State);
        Assert.Equal([$"it failed: review {failed.Id} failed result {master.Id}", "it waits on draft, which is next"], failing.Reasons);
        Assert.Equal(failed.Id, failing.Failed!.Record.Id);
        FlowPosition.StepInstance reopened = run.Step("draft");
        Assert.Equal("next", reopened.State);
        Assert.True(reopened.Rerun);
        Assert.StartsWith($"reopened: step master failed (review {failed.Id} failed result {master.Id})", reopened.Reasons[0], StringComparison.Ordinal);

        // draft is carried again with the same lyrics: the master, whose input did not change, reruns by its failure route.
        RecordEnvelope same = run.Result(run.Assign("draft", run.Estate.BuilderId), "completed", ("lyrics", FlowFixture.Hash('a'), "text/markdown"));
        Assert.Equal("done", run.Step("draft").State);
        FlowPosition.StepInstance rerun = run.Step("master");
        Assert.Equal("next", rerun.State);
        Assert.True(rerun.Rerun);
        Assert.Contains("its failure route returned the run to draft, done again at", rerun.Reasons[0], StringComparison.Ordinal);

        // The rerun's fresh assignment holds the master; its result is reviewed and passes, and the run goes on.
        RecordEnvelope remastering = run.Assign("master", run.Estate.BuilderId, inputs: new JsonArray(FlowFixture.PinOf(same, "lyrics")));
        RecordEnvelope remaster = run.Result(remastering, "completed", ("master", FlowFixture.Hash('c'), null));
        Assert.Equal("done", run.Step("master").State);
        Assert.Null(run.Step("master").Failed);

        // Every attempt and result stays recorded: the failed master's and both drafts'.
        Assert.Equal([master.Id, remaster.Id], run.Step("master").Attempts.Select(a => a.Result!.Id));
        Assert.Equal([first.Id, same.Id], run.Step("draft").Attempts.Select(a => a.Result!.Id));
        Assert.True(run.Estate.Load().ById.ContainsKey(failed.Id));
    }

    [Fact]
    public void AFailedStepWithoutARouteStopsForTheSponsorAndABranchRouteEntersItsBranchAndReturns()
    {
        using var stopping = new FlowFixture(options: new JsonObject { ["step.review-lyrics"] = "off" });
        RecordEnvelope abandoned = stopping.Result(stopping.Assign("draft", stopping.Estate.BuilderId), "abandoned");
        FlowPosition.StepInstance draft = stopping.Step("draft");
        Assert.Equal("stopped", draft.State);
        Assert.Equal($"it failed: its result {abandoned.Id} is abandoned", draft.Reasons[0]);
        Assert.Equal("required attention: its failure route stops here: the sponsor is asked, and only what depends on it waits", draft.Reasons[1]);
        Assert.Equal(("failed", abandoned.Id), (draft.Attention!.Kind, draft.Attention.Trigger.Id));

        JsonObject flow = FlowFixture.Song();
        JsonObject rework = FlowFixture.Step("rework", "work", "writer", optional: "on");
        rework["after"] = new JsonArray();
        flow["steps"]!.AsArray().Add(rework);
        flow["branches"] = new JsonArray(new JsonObject { ["name"] = "rework", ["when"] = new JsonArray(FlowFixture.Marker("REWORK")), ["steps"] = new JsonArray("rework"), ["returnTo"] = "draft" });
        Step(flow, "master")["failure"] = new JsonObject { ["route"] = "branch", ["branch"] = "rework" };
        using var run = new FlowFixture(flow, new JsonObject { ["step.review-lyrics"] = "off", ["step.shorts"] = "off" });
        Assert.Equal("off", run.Step("rework").State);
        RecordEnvelope lyrics = run.Result(run.Assign("draft", run.Estate.BuilderId), "completed", ("lyrics", FlowFixture.Hash('a'), "text/markdown"));
        RecordEnvelope master = run.Result(run.Assign("master", run.Estate.BuilderId, inputs: new JsonArray(FlowFixture.PinOf(lyrics, "lyrics"))), "completed", ("master", FlowFixture.Hash('b'), null));
        run.Review(master, "fail");

        // The branch is entered by the failure: its step runs, and the master waits on it.
        Assert.Equal("next", run.Step("rework").State);
        Assert.StartsWith("its failure route entered branch rework: it waits on the branch's steps (rework), then the run returns to draft", run.Step("master").Reasons[1], StringComparison.Ordinal);
        Assert.Equal("done", run.Step("draft").State);

        // Once the branch's step is done, the run returns to draft, its returnTo.
        run.Result(run.Assign("rework", run.Estate.BuilderId), "completed");
        Assert.Equal("done", run.Step("rework").State);
        Assert.StartsWith("reopened: step master failed", run.Step("draft").Reasons[0], StringComparison.Ordinal);
        Assert.Equal(("blocked", "it waits on draft, which is next"), (run.Step("master").State, run.Step("master").Reasons[1]));
        Assert.Equal("branch", run.Step("master").Failed!.Route.Route);
    }

    [Fact]
    public void ConsecutiveFailedStepsHoldTheWholeRunForTheSponsor()
    {
        JsonObject flow = FlowFixture.Song();
        JsonObject notes = FlowFixture.Step("notes", "work", "writer", optional: "on");
        notes["after"] = new JsonArray();
        flow["steps"]!.AsArray().Add(notes);
        using var run = new FlowFixture(flow, run: false);
        JsonObject body = run.GoalBody(new JsonObject { ["step.review-lyrics"] = "off" });
        body["limits"] = new JsonObject { ["consecutiveFailures"] = 2 };
        RecordEnvelope goal = run.Add("goal", body);
        run.Add("goal-status", new JsonObject { ["status"] = "active", ["note"] = "Activated for the test." }, subject: goal.Reference);
        FlowPosition.Position Position() => FlowPosition.Derive(run.Estate.Load(), goal, run.Estate.Clock);

        run.Result(run.Assign("draft", run.Estate.BuilderId), "abandoned");
        Assert.Empty(Position().RunAttention);
        RecordEnvelope second = run.Result(run.Assign("notes", run.Estate.BuilderId), "blocked");
        FlowPosition.Attention held = Assert.Single(Position().RunAttention);
        Assert.Equal(("consecutive-failures", second.Id), (held.Kind, held.Trigger.Id));
        Assert.StartsWith("the run has had 2 failed steps in a row, the limit of 2 consecutive failures its goal sets", held.Summary, StringComparison.Ordinal);
        Assert.Contains("Required attention (consecutive-failures)", FlowPosition.RenderText(run.Estate.Load(), Position()), StringComparison.Ordinal);
    }

    [Fact]
    public void ARoleBriefDueAtRefuseHoldsTheStepsItBriefs()
    {
        JsonObject flow = FlowFixture.Song();
        flow["roles"]![0]!["document"] = "docs/roles/writer.md";
        using var run = new FlowFixture(flow);
        Directory.CreateDirectory(Path.Combine(run.Estate.Root, "docs", "roles"));
        File.WriteAllText(Path.Combine(run.Estate.Root, "docs", "roles", "writer.md"), "# Writer\n\nWrite the song's material.\n");
        Assert.Equal("next", run.Step("draft").State);
        RecordEnvelope declaration = run.Add("living-document", new JsonObject
        {
            ["path"] = "docs/roles/writer.md", ["title"] = "The writer's brief", ["kind"] = "authored", ["source"] = "estate-file",
            ["triggers"] = new JsonArray(new JsonObject { ["on"] = "covers", ["level"] = "refuse", ["paths"] = new JsonArray("docs/roles/writer.md") }), ["summary"] = "How the writer works here.",
        });
        Assert.True(Assert.Single(LivingDocuments.Derive(run.Estate.Load()), d => d.Path == "docs/roles/writer.md").Closes);

        // Due at refuse, the brief holds every step of its role, and the steps of other roles go on as before.
        FlowPosition.StepInstance draft = run.Step("draft");
        Assert.Equal("blocked", draft.State);
        Assert.StartsWith("its role brief, docs/roles/writer.md: due at refuse", draft.Reasons[0], StringComparison.Ordinal);
        Assert.EndsWith("a revision of it, or a review that clears it, comes before the writer role is briefed from it", draft.Reasons[0], StringComparison.Ordinal);
        Assert.Equal("docs/roles/writer.md", run.Position().Flow!.Role("writer")!.Document);

        // A holder already briefed goes on with it.
        run.Assign("draft", run.Estate.BuilderId);
        Assert.Equal("next", run.Step("draft").State);
        Assert.Equal("living-document", declaration.Type);
    }

    [Fact]
    public void AFailureRouteAndARoleDocumentAreCheckedWithTheFlow()
    {
        JsonObject onReview = FlowFixture.Song();
        Step(onReview, "review-lyrics")["failure"] = new JsonObject { ["route"] = "stop" };
        Assert.Contains("A work step declares its failure route", Refusal(onReview), StringComparison.Ordinal);

        JsonObject later = FlowFixture.Song();
        Step(later, "draft")["failure"] = new JsonObject { ["route"] = "step", ["step"] = "master" };
        Assert.Contains("'master' is not a step declared before this one", Refusal(later), StringComparison.Ordinal);

        JsonObject unknown = FlowFixture.Song();
        Step(unknown, "master")["failure"] = new JsonObject { ["route"] = "branch", ["branch"] = "rework" };
        Assert.Contains("enters branch 'rework', which the flow does not declare", Refusal(unknown), StringComparison.Ordinal);

        JsonObject both = FlowFixture.Song();
        Step(both, "master")["failure"] = new JsonObject { ["route"] = "stop", ["step"] = "draft" };
        Assert.Contains("Only a failure route of route step names the step the run returns to", Refusal(both), StringComparison.Ordinal);

        JsonObject hidden = FlowFixture.Song();
        hidden["roles"]![0]!["document"] = ".vericairn/writer.md";
        Assert.Contains("A role brief's document is a portable path", Refusal(hidden), StringComparison.Ordinal);
        JsonObject records = FlowFixture.Song();
        records["roles"]![0]!["document"] = "records/writer.md";
        Assert.Contains("A role brief's document is a portable path", Refusal(records), StringComparison.Ordinal);

        JsonObject valid = FlowFixture.Song();
        Step(valid, "master")["failure"] = new JsonObject { ["route"] = "step", ["step"] = "draft" };
        valid["roles"]![0]!["document"] = "docs/roles/writer.md";
        FlowDefinition resolved = FlowDefinition.Resolve(TemplateDefinition.Read(FlowFixture.Element(valid)), _ => throw new InvalidOperationException("No base."));
        Assert.Equal(new FlowFailure("step", null, "draft"), resolved.Step("master")!.Failure);
        Assert.Equal("stop", resolved.Step("draft")!.FailureRoute.Route);
    }

    private static string Refusal(JsonObject flow) =>
        string.Join(" | ", TemplateDefinition.Validate(FlowFixture.Element(flow)).Where(f => f.Severity == Severity.Error).Select(f => f.Code + ": " + f.Message));

    private static JsonObject Step(JsonObject flow, string key) => flow["steps"]!.AsArray().Select(s => s!.AsObject()).Single(s => s["key"]!.GetValue<string>() == key);

    private static JsonObject Item(string name, string review) => new() { ["name"] = name, ["kind"] = "file", ["required"] = true, ["review"] = review };

    /// <summary>The operator's assignment of a step's review before hand-off to a reviewer, naming the items and the exact subject.</summary>
    private static RecordEnvelope ReviewAssignment(FlowFixture run, string reviewer, RecordEnvelope subject, params string[] items) => StepReviewAssignment(run, reviewer, subject, "draft", items);

    private static RecordEnvelope StepReviewAssignment(FlowFixture run, string reviewer, RecordEnvelope subject, string step, params string[] items)
    {
        var review = new JsonObject { ["subject"] = new JsonObject { ["type"] = subject.Type, ["id"] = subject.Id, ["hash"] = subject.ComputedHash } };
        if (items.Length > 0)
        {
            review["items"] = new JsonArray(items.Select(i => (JsonNode)i).ToArray());
        }

        return run.Add("assignment", new JsonObject
        {
            ["contributor"] = reviewer, ["step"] = new JsonObject { ["key"] = step, ["review"] = review },
            ["brief"] = new JsonObject
            {
                ["outcome"] = "Review the step's items.", ["sources"] = new JsonArray(), ["permittedChanges"] = new JsonArray("path:song/"), ["prohibitedEffects"] = new JsonArray("publication"),
                ["workspace"] = new JsonArray("song repository, a work branch"), ["limits"] = new JsonArray(), ["expectedOutputs"] = new JsonArray("a review"),
                ["reviewCriteria"] = new JsonArray("the items hold"), ["recovery"] = "Review it again.",
            },
        }, subject: run.Work.Reference);
    }

    private static void AgentReview(FlowFixture run, RecordEnvelope result, string reviewer, string outcome) =>
        run.Estate.Add("review", reviewer, new JsonObject { ["independence"] = "independent-agent", ["outcome"] = outcome, ["summary"] = "Reviewed it.", ["findings"] = new JsonArray() }.ToJsonString(), result.Reference);

    private static RecordEnvelope ResultWithEffects(FlowFixture run, RecordEnvelope assignment, string effect)
    {
        RecordEnvelope attempt = run.Add("attempt", new JsonObject { ["startedAt"] = RecordParser.FormatTimestamp(run.Estate.Clock), ["workspace"] = "song repository, a work branch", ["note"] = "Carry the step." }, builder: true, subject: assignment.Reference);
        return run.Add("result", new JsonObject
        {
            ["outcome"] = "completed", ["summary"] = "Carried the step.", ["outputs"] = new JsonArray(new JsonObject { ["name"] = "lyrics", ["hash"] = FlowFixture.Hash('a'), ["mediaType"] = "text/markdown" }),
            ["evidence"] = new JsonArray(), ["checks"] = new JsonArray(), ["limitations"] = new JsonArray(), ["unresolvedEffects"] = new JsonArray(effect), ["nextStep"] = "Reconcile it.",
        }, builder: true, subject: attempt.Reference);
    }

    /// <summary>An agent run on an assignment, from its intent to its settlement: dispatched, then settled as given, charged a minute.</summary>
    private static RecordEnvelope AgentRun(FlowFixture run, RecordEnvelope assignment, string identity, string status)
    {
        JsonObject Body(string state) => new()
        {
            ["provider"] = "claude-code", ["action"] = "agent-run", ["identity"] = "run-" + identity + "-" + assignment.Id, ["intent"] = "Run the writer.",
            ["target"] = new JsonObject { ["assignment"] = assignment.Id, ["principal"] = run.Estate.BuilderId, ["workspace"] = "song repository, branch work/song-1-draft", ["goal"] = run.Goal.Id },
            ["status"] = state, ["evidence"] = new JsonArray(), ["cap"] = new JsonObject { ["wallMinutes"] = 10, ["attemptsPerStep"] = 3 },
        };
        RecordEnvelope intent = run.Add("operation", Body("not-started"));
        JsonObject dispatched = Body("dispatched");
        dispatched["dispatchedAt"] = intent.At;
        RecordEnvelope started = run.Add("operation", dispatched, supersedes: intent.Id);
        JsonObject settled = Body(status);
        settled["dispatchedAt"] = intent.At;
        settled["observed"] = new JsonObject { ["summary"] = status == "known-failure" ? "The harness exited with code 1." : "The harness exited with code 0.", ["at"] = started.At };
        settled["usage"] = new JsonObject { ["wallMinutes"] = 1, ["unreported"] = new JsonArray() };
        return run.Add("operation", settled, supersedes: started.Id);
    }

    private static List<Finding> Errors(FlowFixture run, RecordEnvelope record) =>
        run.Estate.Load().Findings.Where(f => f.Severity == Severity.Error && f.Location.StartsWith(record.ExpectedPath, StringComparison.Ordinal)).ToList();
}
