// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using Vericairn.Packs.Software;
using Vericairn.Protocol;
using Vericairn.Records.Estate;
using Vericairn.Records.Model;
using Vericairn.Records.Signing;
using Vericairn.Records.Tests;
using Vericairn.Records.Types;
using Vericairn.Runtime.Providers;
using Vericairn.Runtime.Service;
using Vericairn.Runtime.Workers;

namespace Vericairn.Runtime.Tests;

/// <summary>
/// From 0.9.4 (CODE-73; the estate's design/measured-work.md § 5): the runtime fills and requires a brief's repository, reports the overlaps
/// of an assignment it records, refuses a new attempt and a run while an assignment named in after is unreleased and a narrowing while a run
/// is in flight, records the later of a held overlap after the earlier from goal step, shows overlaps in goal next and the view, reads
/// witnesses and integration from the registered clone, and its scope audit counts an excluded change as outside.
/// </summary>
[Collection("home")]
public class OverlapCheckTests
{
    private const string DocsBrief = """{"outcome":"The guide.","sources":[],"permittedChanges":["path:docs/**"],"prohibitedEffects":["no publication"],"workspace":["widget repository, a work branch"],"limits":[],"expectedOutputs":["guide"],"reviewCriteria":["it reads"],"recovery":"Record a result."}""";

    [Fact]
    public void ABriefWithAPathPatternNamesItsRepositoryFilledOrNamedAndIsRefusedWithoutOne()
    {
        using var run = new RunEstate(recordFormat: 3);
        Order(run, "W-3");
        WriteOutcome refused = run.Service.Assign(new AssignRequest("W-3", run.Estate.BuilderId, run.Estate.OperatorId, RunEstate.Json(RunEstate.Brief)));
        Assert.False(refused.Ok);
        FindingDto finding = Assert.Single(refused.Problem!.Findings, f => f.Code == "assignment.repository-required");
        Assert.Contains(run.Resource, finding.Message, StringComparison.Ordinal);

        RecordWritten estate = RunEstate.Ok(run.Service.Assign(new AssignRequest("W-3", run.Estate.BuilderId, run.Estate.OperatorId, RunEstate.Json(RunEstate.Brief)) { Repository = "estate" }));
        EstateModel model = run.Estate.Load();
        Assert.Equal(model.FirstEstate!.Id, Brief(model, estate.Id).GetProperty("repository").GetString());
        Assert.Equal(run.Resource, Brief(model, run.Assignment).GetProperty("repository").GetString());
        Assert.False(run.Service.Assign(new AssignRequest("W-3", run.Estate.BuilderId, run.Estate.OperatorId, RunEstate.Json(RunEstate.Brief)) { Repository = "01M4TRNE1GYEK4P8MXQAB3C8Q4" }).Ok);

        // A step of the same work order takes the repository its other assignments name.
        RecordWritten shared = RunEstate.Ok(run.Service.Assign(new AssignRequest("W-1", run.Estate.BuilderId, run.Estate.OperatorId, RunEstate.Json(RunEstate.Brief))));
        Assert.Equal(run.Resource, Brief(run.Estate.Load(), shared.Id).GetProperty("repository").GetString());
    }

    [Fact]
    public void AnEstateNamingNoRepositoryResourceFillsItsOwnRepository()
    {
        using var estate = new TestEstate(3);
        using EstateService service = EstateService.Open(estate.Root);
        RunEstate.Ok(service.NewRecord(new NewRecordRequest("work-order", estate.OperatorId, RunEstate.Json("""{"key":"W-1","kind":"ordinary","title":"Widget","intent":"Build it.","scope":["widget"],"exitQuestion":"Built?","inputs":[],"deliverables":["widget"]}"""))));
        RecordWritten assigned = RunEstate.Ok(service.Assign(new AssignRequest("W-1", estate.BuilderId, estate.OperatorId, RunEstate.Json(RunEstate.Brief))));
        Assert.Equal(estate.Estate.Id, Brief(estate.Load(), assigned.Id).GetProperty("repository").GetString());
    }

    [Fact]
    public void RecordingAnAssignmentReportsItsOverlapsWithTheActiveAssignments()
    {
        using var run = new RunEstate(recordFormat: 3);
        Order(run, "W-3");
        WriteOutcome outcome = run.Service.Assign(new AssignRequest("W-3", run.Estate.BuilderId, run.Estate.OperatorId, RunEstate.Json(RunEstate.Brief)) { Repository = run.Resource });
        Assert.True(outcome.Ok);
        string notice = Assert.Single(outcome.Notices!);
        Assert.StartsWith($"Overlap: W-1's assignment {run.Assignment} and W-3's assignment {outcome.Written!.Id} in widget ({run.Resource}) share paths either could create, such as widget", notice, StringComparison.Ordinal);
        // A brief that overlaps nothing reports nothing.
        Assert.Null(run.Service.Assign(new AssignRequest("W-3", run.Estate.BuilderId, run.Estate.OperatorId, RunEstate.Json(DocsBrief)) { Repository = run.Resource }).Notices);
    }

    [Fact]
    public void ANewAttemptWaitsWhileAnAssignmentItRunsAfterIsUnreleased()
    {
        using var run = new RunEstate(recordFormat: 3);
        Order(run, "W-3");
        string later = RunEstate.Ok(run.Service.Assign(new AssignRequest("W-3", run.Estate.BuilderId, run.Estate.OperatorId, After(RunEstate.Brief, run.Assignment)) { Repository = run.Resource })).Id;
        int records = run.Estate.Load().Records.Count;
        WriteOutcome refused = run.Service.Attempt(new AttemptRequest(later, run.Estate.BuilderId, "widget repository, branch work/w-3", "Start."));
        Assert.False(refused.Ok);
        FindingDto wait = Assert.Single(refused.Problem!.Findings, f => f.Code == "attempt.after");
        Assert.Contains($"runs after W-1's assignment {run.Assignment}", wait.Message, StringComparison.Ordinal);
        Assert.Equal(records, run.Estate.Load().Records.Count);

        // Once the earlier assignment's result is abandoned it is released, and the attempt is recorded.
        string attempt = RunEstate.Ok(run.Service.Attempt(new AttemptRequest(run.Assignment, run.Estate.BuilderId, "widget repository, branch work/w-1", "Start."))).Id;
        RunEstate.Ok(run.Service.Result(new ResultRequest(attempt, run.Estate.BuilderId, RunEstate.Json("""{"outcome":"abandoned","summary":"Stopped.","outputs":[],"evidence":[],"checks":[],"limitations":[],"unresolvedEffects":[],"nextStep":"none"}"""))));
        RunEstate.Ok(run.Service.Attempt(new AttemptRequest(later, run.Estate.BuilderId, "widget repository, branch work/w-3", "Start.")));
    }

    [Fact]
    public void ACompletedResultNamingNoChangeIsWrittenWithTheWriteBoundarysWarning()
    {
        using var run = new RunEstate(recordFormat: 3);
        string attempt = RunEstate.Ok(run.Service.Attempt(new AttemptRequest(run.Assignment, run.Estate.BuilderId, "widget repository, branch work/w-1", "Start."))).Id;
        WriteOutcome result = run.Service.Result(new ResultRequest(attempt, run.Estate.BuilderId, RunEstate.Json("""{"outcome":"completed","summary":"Built.","outputs":[],"evidence":[],"checks":[],"limitations":[],"unresolvedEffects":[],"nextStep":"none"}""")));
        Assert.True(result.Ok);
        Assert.StartsWith($"Warning: result {result.Written!.Id} is completed and names no change, so assignment {run.Assignment}", Assert.Single(result.Notices!), StringComparison.Ordinal);
        Assert.True(AssignmentOverlaps.PartyOf(run.Estate.Load(), run.Estate.Load().ById[run.Assignment])!.Active);
    }

    [Fact]
    public void ARunWaitsForItsAfterAndWorksInTheRepositoryItsBriefNames()
    {
        using var run = new RunEstate(recordFormat: 3);
        Order(run, "W-3", run.Goal);
        string later = RunEstate.Ok(run.Service.Assign(new AssignRequest("W-3", run.Estate.BuilderId, run.Estate.OperatorId, After(RunEstate.Brief, run.Assignment)) { Repository = run.Resource })).Id;
        RunResponse waiting = Start(run, later, "wait");
        Assert.False(waiting.Ok);
        Assert.Contains($"Assignment {later} runs after W-1's assignment {run.Assignment}", waiting.Message, StringComparison.Ordinal);
        Assert.Empty(run.Service.Records("operation"));

        Order(run, "W-4", run.Goal);
        string elsewhere = RunEstate.Ok(run.Service.Assign(new AssignRequest("W-4", run.Estate.BuilderId, run.Estate.OperatorId, RunEstate.Json(DocsBrief)) { Repository = "estate" })).Id;
        RunResponse wrong = Start(run, elsewhere, "elsewhere");
        Assert.False(wrong.Ok);
        Assert.Contains("names the estate's own repository as the repository it changes, and this run would work in widget", wrong.Message, StringComparison.Ordinal);

        // A run reports its assignment's overlaps when it starts.
        RunResponse started = run.Start("generic", "generic", run.Report("first"));
        Assert.True(started.Ok, started.Message);
        Assert.Contains(started.Notices!, n => n.Contains($"W-3's assignment {later}", StringComparison.Ordinal));
        run.Settled(started.Run);
    }

    [Fact]
    public void AnAssignmentIsNarrowedOnlyOnceItsRunIsNoLongerInFlight()
    {
        using var run = new RunEstate(recordFormat: 3);
        EstateModel model = run.Estate.Load();
        RecordEnvelope assignment = model.ById[run.Assignment];
        RecordEnvelope order = WorkView.OrderOfAssignment(model, assignment)!;
        // The run's intent, written after its goal and assignment, at this machine's time, and not settled.
        run.Estate.SetClock(DateTimeOffset.UtcNow);
        run.Estate.Add("operation", run.Estate.OperatorId, $$"""{"provider":"generic","action":"agent-run","identity":"narrowing-test","intent":"Run the widget.","target":{"assignment":"{{run.Assignment}}","principal":"{{run.Estate.BuilderId}}","workspace":"widget branch work/w-1","goal":"{{run.Goal}}"},"status":"not-started","cap":{"wallMinutes":5,"attemptsPerStep":3},"evidence":[]}""");
        JsonObject body = JsonNode.Parse(assignment.Body.GetRawText())!.AsObject();
        body["brief"]!["excludedChanges"] = new JsonArray("path:widget/generated/");
        WriteOutcome refused = run.Service.NewRecord(new NewRecordRequest("assignment", run.Estate.OperatorId, PolicyFixtureElement(body), $"work-order:{order.Id}:{order.ComputedHash}", Supersedes: run.Assignment));
        Assert.False(refused.Ok);
        Assert.Contains(refused.Problem!.Findings, f => f.Code == "assignment.narrowing-in-flight");
        body["brief"]!.AsObject().Remove("excludedChanges");
        // Nor do its repository or its permitted changes change while the run is in flight: the run keeps working where admission checked it.
        body["brief"]!["repository"] = model.FirstEstate!.Id;
        WriteOutcome moved = run.Service.NewRecord(new NewRecordRequest("assignment", run.Estate.OperatorId, PolicyFixtureElement(body), $"work-order:{order.Id}:{order.ComputedHash}", Supersedes: run.Assignment));
        Assert.Contains(moved.Problem!.Findings, f => f.Code == "assignment.narrowing-in-flight" && f.Location.EndsWith("#body.brief.repository", StringComparison.Ordinal));
        body["brief"]!["repository"] = run.Resource;
        body["brief"]!["permittedChanges"] = new JsonArray("path:widget/core/");
        WriteOutcome narrowed = run.Service.NewRecord(new NewRecordRequest("assignment", run.Estate.OperatorId, PolicyFixtureElement(body), $"work-order:{order.Id}:{order.ComputedHash}", Supersedes: run.Assignment));
        Assert.Contains(narrowed.Problem!.Findings, f => f.Code == "assignment.narrowing-in-flight" && f.Location.EndsWith("#body.brief.permittedChanges", StringComparison.Ordinal));
        body["brief"]!["permittedChanges"] = new JsonArray("path:widget/");
        // The same revision without a narrowing is not held by the run.
        body["note"] = "The same brief, noted.";
        WriteOutcome noted = run.Service.NewRecord(new NewRecordRequest("assignment", run.Estate.OperatorId, PolicyFixtureElement(body), $"work-order:{order.Id}:{order.ComputedHash}", Supersedes: run.Assignment));
        Assert.True(noted.Ok, noted.Problem?.Message + " " + string.Join("; ", noted.Problem?.Findings.Select(f => f.Code + " " + f.Message) ?? []));
    }

    [Fact]
    public void GoalStepRecordsTheLaterOfAHeldOverlapAfterTheEarlierAndStartsItOnceReleased()
    {
        using var run = new RunEstate(recordFormat: 3, keepOperatorKey: true);
        string op = run.Estate.OperatorId;
        Routed(run);
        Accept(run, "W-1");
        Order(run, "W-3");
        string earlier = RunEstate.Ok(run.Service.Assign(new AssignRequest("W-3", run.Estate.BuilderId, op, RunEstate.Json(DocsBrief)) { Repository = run.Resource })).Id;
        RecordEnvelope policy = Declare(run, "held-docs", ["docs/**"], "refuse");
        Assert.DoesNotContain(run.Estate.Load().Findings, f => f.Severity == Severity.Error);
        new FileKeyStore(VericairnHome.Path()).Remove(run.OperatorDevice);

        // goal next shows the overlap the next step's new assignment would have, and that goal step records it after the earlier one.
        NextResponse next = run.Service.GoalNext(run.Goal)!;
        Assert.Contains($"A new assignment of W-2 would overlap: 1\n  Overlap: W-3's assignment {earlier} and W-2's new assignment", next.Text, StringComparison.Ordinal);
        Assert.Contains($"Goal step records it to run after W-3's assignment {earlier}.", next.Text, StringComparison.Ordinal);
        Assert.True(Assert.Single(next.Json.GetProperty("overlaps").EnumerateArray()).GetProperty("held").GetBoolean());

        var settings = new Dictionary<string, string>(StringComparer.Ordinal) { ["command"] = FakeHarness.Setting("generic", run.Report("step")) };
        GoalStepResponse first = run.Service.GoalStep(run.Goal, new GoalStepRequest(op, run.Resource, settings));
        Assert.True(first.Ok, first.Message);
        Assert.Equal(("assigned", "W-2"), (first.Outcome, first.Step));
        Assert.Null(first.Run);
        Assert.Contains($"recorded to run after {earlier}", first.Message, StringComparison.Ordinal);
        Assert.Contains(first.Notices!, n => n.StartsWith($"W-2 is recorded to run after W-3's assignment {earlier}", StringComparison.Ordinal) && n.Contains(policy.Id, StringComparison.Ordinal));
        EstateModel model = run.Estate.Load();
        JsonElement brief = Brief(model, first.Assignment!.Id);
        Assert.Equal(run.Resource, brief.GetProperty("repository").GetString());
        Assert.Equal(earlier, Assert.Single(brief.GetProperty("after").EnumerateArray()).GetString());
        Assert.False(model.ById[first.Assignment.Id].Body.TryGetProperty("routedWith", out _));
        Assert.Empty(run.Service.Records("operation"));

        // The step is held by that assignment, and its run waits while the earlier assignment is unreleased.
        GoalStepResponse waiting = run.Service.GoalStep(run.Goal, new GoalStepRequest(op, run.Resource, settings));
        Assert.False(waiting.Ok);
        Assert.Contains($"runs after W-3's assignment {earlier}", waiting.Message, StringComparison.Ordinal);
        Assert.Empty(run.Service.Records("operation"));

        string attempt = RunEstate.Ok(run.Service.Attempt(new AttemptRequest(earlier, run.Estate.BuilderId, "widget repository, branch work/w-3", "Start."))).Id;
        RunEstate.Ok(run.Service.Result(new ResultRequest(attempt, run.Estate.BuilderId, RunEstate.Json("""{"outcome":"abandoned","summary":"Stopped.","outputs":[],"evidence":[],"checks":[],"limitations":[],"unresolvedEffects":[],"nextStep":"none"}"""))));
        GoalStepResponse started = run.Service.GoalStep(run.Goal, new GoalStepRequest(op, run.Resource, settings));
        Assert.True(started.Ok, started.Message);
        Assert.Equal(("run", first.Assignment.Id), (started.Outcome, AgentRuns.Find(run.Estate.Load(), started.Run!.Run)!.Assignment));
        run.Settled(started.Run.Run);
    }

    [Fact]
    public void GoalStepReportsAnOverlapNoPolicyHoldsAndStartsTheWork()
    {
        using var run = new RunEstate(recordFormat: 3);
        string op = run.Estate.OperatorId;
        Routed(run);
        Accept(run, "W-1");
        Order(run, "W-3");
        string earlier = RunEstate.Ok(run.Service.Assign(new AssignRequest("W-3", run.Estate.BuilderId, op, RunEstate.Json(DocsBrief)) { Repository = run.Resource })).Id;
        GoalStepResponse step = run.Service.GoalStep(run.Goal, new GoalStepRequest(op, run.Resource, new Dictionary<string, string>(StringComparer.Ordinal) { ["command"] = FakeHarness.Setting("generic", run.Report("step")) }));
        Assert.True(step.Ok, step.Message);
        Assert.Equal("run", step.Outcome);
        Assert.False(Brief(run.Estate.Load(), step.Assignment!.Id).TryGetProperty("after", out _));
        Assert.Contains(step.Run!.Notices!, n => n.Contains($"W-3's assignment {earlier}", StringComparison.Ordinal));
        run.Settled(step.Run.Run);
    }

    [Fact]
    public void TheViewListsActiveAssignmentsAndOverlapsAndPreviewsADraftBrief()
    {
        using var run = new RunEstate(recordFormat: 3);
        OverlapResponse alone = run.Service.WorkOverlap();
        Assert.Contains("Active assignments with permitted paths: 1", alone.Text, StringComparison.Ordinal);
        Assert.Contains("Overlaps: 0", alone.Text, StringComparison.Ordinal);
        Assert.Equal("vericairn.overlaps/1", alone.Json.GetProperty("format").GetString());
        Order(run, "W-3");
        string other = RunEstate.Ok(run.Service.Assign(new AssignRequest("W-3", run.Estate.BuilderId, run.Estate.OperatorId, RunEstate.Json(RunEstate.Brief)) { Repository = run.Resource })).Id;
        OverlapResponse view = run.Service.WorkOverlap(run.Resource);
        JsonElement overlap = Assert.Single(view.Json.GetProperty("overlaps").EnumerateArray());
        Assert.Equal((run.Assignment, other, "none"), (overlap.GetProperty("first").GetProperty("assignment").GetString(), overlap.GetProperty("second").GetProperty("assignment").GetString(), overlap.GetProperty("resolution").GetString()));
        Assert.Empty(run.Service.WorkOverlap("estate").Json.GetProperty("overlaps").EnumerateArray());
        Assert.Contains("No repository has id", run.Service.WorkOverlap("01M4TRNE1GYEK4P8MXQAB3C8Q4").Json.GetProperty("problem").GetString(), StringComparison.Ordinal);

        OverlapResponse preview = run.Service.WorkOverlapPreview(new OverlapPreviewRequest(RunEstate.Json("""{"permittedChanges":["path:widget/part.txt"]}"""), run.Resource));
        Assert.Equal(2, preview.Json.GetProperty("overlaps").GetArrayLength());
        Assert.Contains("and the draft brief in widget", preview.Text, StringComparison.Ordinal);
        Assert.Equal(run.Resource, preview.Json.GetProperty("draft").GetProperty("repository").GetString());
        Assert.Contains("It overlaps no active assignment.", run.Service.WorkOverlapPreview(new OverlapPreviewRequest(RunEstate.Json(DocsBrief), run.Resource)).Text, StringComparison.Ordinal);

        // A draft is held to what the write boundary holds a recorded brief to before anything is compared.
        var invalid = new JsonObject
        {
            ["permittedChanges"] = new JsonArray("path:" + new string('a', 2001)), ["excludedChanges"] = new JsonArray("the operator guide"),
            ["after"] = new JsonArray([.. Enumerable.Range(0, 21).Select(_ => (JsonNode)run.Assignment)]),
        };
        string problem = run.Service.WorkOverlapPreview(new OverlapPreviewRequest(PolicyFixtureElement(invalid), run.Resource)).Json.GetProperty("problem").GetString()!;
        Assert.StartsWith("The draft brief would not be recorded, so nothing was compared: ", problem, StringComparison.Ordinal);
        Assert.Contains("1 to 2000 characters", problem, StringComparison.Ordinal);
        Assert.Contains("prose excludes nothing", problem, StringComparison.Ordinal);
        Assert.Contains("at most 20 assignments", problem, StringComparison.Ordinal);

        // A draft that inflates the characters the patterns name with exclusions that never match is compared within the search's bounds.
        var inflated = new JsonObject
        {
            ["permittedChanges"] = new JsonArray("path:widget/" + new string('w', 1980)),
            ["excludedChanges"] = new JsonArray([.. Enumerable.Range(0, 10).Select(part => (JsonNode)("path:q/" + new string([.. Enumerable.Range(0x4E00 + part * 1990, 1990).Select(i => (char)i)])))]),
        };
        OverlapResponse bounded = run.Service.WorkOverlapPreview(new OverlapPreviewRequest(PolicyFixtureElement(inflated), run.Resource));
        Assert.False(bounded.Json.TryGetProperty("problem", out JsonElement refusal), refusal.ToString());
        JsonElement found = Assert.Single(bounded.Json.GetProperty("overlaps").EnumerateArray(), o => o.GetProperty("first").GetProperty("assignment").GetString() == run.Assignment);
        Assert.True(found.GetProperty("determined").GetBoolean());
    }

    [Fact]
    public void TheRegisteredCloneGivesTrackedWitnessesAndShowsAChangeIntegrated()
    {
        using var run = new RunEstate(recordFormat: 3);
        run.Service.Providers.Register(new GitCommandAdapter());
        string root = Git(run.Clone, "rev-parse", "HEAD");
        Directory.CreateDirectory(Path.Combine(run.Clone, "widget"));
        File.WriteAllText(Path.Combine(run.Clone, "widget", "part.txt"), "part\n");
        Git(run.Clone, "add", "widget/part.txt");
        Git(run.Clone, "commit", "-q", "-m", "A part");
        string merged = Git(run.Clone, "rev-parse", "HEAD");
        Git(run.Clone, "checkout", "-q", "-b", "side");
        File.WriteAllText(Path.Combine(run.Clone, "widget", "side.txt"), "side\n");
        Git(run.Clone, "add", "widget/side.txt");
        Git(run.Clone, "commit", "-q", "-m", "A side change");
        string side = Git(run.Clone, "rev-parse", "HEAD");
        Git(run.Clone, "checkout", "-q", "main");

        Order(run, "W-3");
        string other = RunEstate.Ok(run.Service.Assign(new AssignRequest("W-3", run.Estate.BuilderId, run.Estate.OperatorId, RunEstate.Json(RunEstate.Brief)) { Repository = run.Resource })).Id;
        JsonElement overlap = Assert.Single(run.Service.WorkOverlap().Json.GetProperty("overlaps").EnumerateArray());
        Assert.Equal(("tracked", "main", "widget/part.txt"), (overlap.GetProperty("witnessKind").GetString(), overlap.GetProperty("branch").GetString(), Assert.Single(overlap.GetProperty("witnesses").EnumerateArray()).GetString()));

        // A change whose head is not reachable from its target keeps the assignment active; one whose head is reachable releases it, labeled.
        Complete(run, other, root, side);
        JsonElement behind = Assert.Single(run.Service.WorkOverlap().Json.GetProperty("assignments").EnumerateArray(), a => a.GetProperty("assignment").GetString() == other);
        Assert.Equal("not-integrated", behind.GetProperty("change").GetProperty("integration").GetString());
        Assert.Equal(["widget/part.txt", "widget/side.txt"], behind.GetProperty("change").GetProperty("paths").EnumerateArray().Select(p => p.GetProperty("path").GetString()!));
        Assert.Contains("2 paths changed, by name without rename detection", run.Service.WorkOverlap().Text, StringComparison.Ordinal);
        string corrected = Complete(run, other, root, merged);
        JsonElement view = run.Service.WorkOverlap().Json;
        Assert.DoesNotContain(view.GetProperty("assignments").EnumerateArray(), a => a.GetProperty("assignment").GetString() == other);
        Assert.Empty(view.GetProperty("overlaps").EnumerateArray());
        AssignmentOverlaps.Party released = AssignmentOverlaps.PartyOf(run.Estate.Load(), run.Estate.Load().ById[other], RepositoryView.For(run.Estate.Load(), run.Service.Providers, run.Service.Session.Manifest.Estate))!;
        Assert.Equal(corrected, released.Result!.Id);
        Assert.Contains("observed here", Assert.Single(released.Labels), StringComparison.Ordinal);

        // The change's paths, by name without rename detection; a missing commit makes them unavailable, never empty.
        var git = new GitCommandAdapter();
        Assert.Equal([new SourceChange("widget/part.txt", "added")], git.ChangedPaths(run.Clone, root, merged));
        Assert.Null(git.ChangedPaths(run.Clone, root, new string('3', 40)));
        Assert.True(git.Reachable(run.Clone, merged, "main"));
        Assert.False(git.Reachable(run.Clone, side, "main"));
        Assert.Null(git.Reachable(run.Clone, new string('3', 40), "main"));
        Assert.Contains("widget/part.txt", git.TrackedPaths(run.Clone, "main")!);
    }

    [Fact]
    public void AResultReleasesItsAssignmentOnlyWhenItsOwnChangeIsIntegratedIntoTheMainLine()
    {
        using var run = new RunEstate(recordFormat: 3);
        run.Service.Providers.Register(new GitCommandAdapter());
        string root = Git(run.Clone, "rev-parse", "HEAD");
        Order(run, "W-3");
        Order(run, "W-4");
        string earlier = RunEstate.Ok(run.Service.Assign(new AssignRequest("W-3", run.Estate.BuilderId, run.Estate.OperatorId, RunEstate.Json(DocsBrief)) { Repository = run.Resource })).Id;
        string later = RunEstate.Ok(run.Service.Assign(new AssignRequest("W-4", run.Estate.BuilderId, run.Estate.OperatorId, After(DocsBrief, earlier)) { Repository = run.Resource })).Id;
        // The work, pushed to its own branch, as a worker's settlement fetches a run's audited branch into the registered clone.
        Git(run.Clone, "checkout", "-q", "-b", "work/w-3");
        Commit(run.Clone, "docs/guide.md", "guide\n", "The guide");
        string work = Git(run.Clone, "rev-parse", "HEAD");
        Git(run.Clone, "checkout", "-q", "main");

        // The first probe: the result names the assignment's own work branch as its target. Only the main line shows integration.
        Complete(run, earlier, root, work, target: "work/w-3");
        JsonElement own = Party(run, earlier);
        Assert.True(own.GetProperty("active").GetBoolean());
        Assert.Equal(("not-integrated", "main"), (own.GetProperty("change").GetProperty("integration").GetString(), own.GetProperty("change").GetProperty("mainLine").GetString()));
        Assert.Equal("attempt.after", Assert.Single(run.Service.Attempt(new AttemptRequest(later, run.Estate.BuilderId, "widget repository, branch work/w-4", "Start.")).Problem!.Findings).Code);

        // The second probe: base and head are both the main line's existing commit.
        WriteOutcome same = Submit(run, earlier, root, root);
        Assert.True(same.Ok);
        Assert.Contains(same.Notices!, n => n.StartsWith($"Warning: result {same.Written!.Id} names a change that cannot release assignment {earlier}, since its base and head are the same commit", StringComparison.Ordinal));
        JsonElement empty = Party(run, earlier);
        Assert.True(empty.GetProperty("active").GetBoolean());
        Assert.Equal("mismatched", empty.GetProperty("change").GetProperty("integration").GetString());
        Assert.False(run.Service.Attempt(new AttemptRequest(later, run.Estate.BuilderId, "widget repository, branch work/w-4", "Start.")).Ok);

        // The third probe: a change in another registered repository, reachable from that clone's main line.
        string gadgetClone = Path.Combine(run.Scratch, "gadget");
        Directory.CreateDirectory(gadgetClone);
        Git(gadgetClone, "init", "-q", "-b", "main");
        Commit(gadgetClone, "README.md", "gadget\n", "The first commit");
        string gadgetRoot = Git(gadgetClone, "rev-parse", "HEAD");
        Commit(gadgetClone, "docs/guide.md", "guide\n", "The gadget's guide");
        string gadgetHead = Git(gadgetClone, "rev-parse", "HEAD");
        string gadget = RunEstate.Ok(run.Service.NewRecord(new NewRecordRequest("resource", run.Estate.OperatorId, RunEstate.Json($$"""{"kind":"repository","name":"gadget","role":"The gadget's source.","location":{"rootCommit":"{{gadgetRoot}}"},"visibility":"private"}""")))).Id;
        RepositoryClones.Register(run.Service.Session.Manifest.Estate, gadget, gadgetClone);
        Assert.True(new GitCommandAdapter().Reachable(gadgetClone, gadgetHead, "main"));
        WriteOutcome elsewhere = Submit(run, earlier, gadgetRoot, gadgetHead, repository: gadget);
        Assert.Contains(elsewhere.Notices!, n => n.Contains($"since it is in gadget ({gadget}), not in widget ({run.Resource})", StringComparison.Ordinal));
        JsonElement other = Party(run, earlier);
        Assert.True(other.GetProperty("active").GetBoolean());
        Assert.Equal("mismatched", other.GetProperty("change").GetProperty("integration").GetString());
        Assert.False(run.Service.Attempt(new AttemptRequest(later, run.Estate.BuilderId, "widget repository, branch work/w-4", "Start.")).Ok);

        // The honest case: the work merged into the main line, named by its own range, releases the assignment, and the wait ends.
        Git(run.Clone, "merge", "-q", "--no-ff", "-m", "Merge the guide", "work/w-3");
        WriteOutcome integrated = Submit(run, earlier, root, work);
        Assert.Null(integrated.Notices);
        Assert.DoesNotContain(run.Service.WorkOverlap().Json.GetProperty("assignments").EnumerateArray(), a => a.GetProperty("assignment").GetString() == earlier);
        RunEstate.Ok(run.Service.Attempt(new AttemptRequest(later, run.Estate.BuilderId, "widget repository, branch work/w-4", "Start.")));
    }

    [Fact]
    public void AResultTheCheckCountsReleasedAtCompletionIsNotWarnedAndOneAfterItsFirstRecordIs()
    {
        using var estate = new TestEstate(3);
        using EstateService service = EstateService.Open(estate.Root);
        // A work order and an assignment an older runtime recorded: its brief holds a path pattern and names no repository, and no record of
        // the check exists.
        RecordEnvelope order = estate.Add("work-order", estate.OperatorId, """{"key":"W-1","kind":"ordinary","title":"Widget","intent":"Build it.","scope":["widget"],"exitQuestion":"Built?","inputs":[],"deliverables":["widget"]}""");
        JsonObject body = new() { ["contributor"] = estate.BuilderId, ["brief"] = JsonNode.Parse(RunEstate.Brief) };
        RecordEnvelope older = estate.Add("assignment", estate.OperatorId, body.ToJsonString(), subject: order.Reference);
        const string Completed = """{"outcome":"completed","summary":"Built.","outputs":[],"evidence":[],"checks":[],"limitations":[],"unresolvedEffects":[],"nextStep":"none"}""";
        string attempt = RunEstate.Ok(service.Attempt(new AttemptRequest(older.Id, estate.BuilderId, "widget repository, a work branch", "Start."))).Id;
        WriteOutcome released = service.Result(new ResultRequest(attempt, estate.BuilderId, RunEstate.Json(Completed)));
        Assert.True(released.Ok);
        Assert.Null(released.Notices);
        AssignmentOverlaps.Party party = AssignmentOverlaps.PartyOf(estate.Load(), older)!;
        Assert.False(party.Active);
        Assert.Contains("recorded before the overlap check", Assert.Single(party.Labels), StringComparison.Ordinal);

        // A new assignment names its repository, the check's first record; a later completed result naming no change keeps its assignment
        // active until acceptance, and the write says so.
        RunEstate.Ok(service.Assign(new AssignRequest("W-1", estate.BuilderId, estate.OperatorId, RunEstate.Json(RunEstate.Brief))));
        attempt = RunEstate.Ok(service.Attempt(new AttemptRequest(older.Id, estate.BuilderId, "widget repository, a work branch", "Again."))).Id;
        WriteOutcome warned = service.Result(new ResultRequest(attempt, estate.BuilderId, RunEstate.Json(Completed)));
        Assert.StartsWith($"Warning: result {warned.Written!.Id} is completed and names no change", Assert.Single(warned.Notices!), StringComparison.Ordinal);
        Assert.True(AssignmentOverlaps.PartyOf(estate.Load(), older)!.Active);
    }

    [Fact]
    public void TheClonesGraftsAndSubmoduleSettingSteerNoOverlapRead()
    {
        TestPrerequisites.RequireGit();
        string clone = Path.Combine(Path.GetTempPath(), "vericairn-overlap-git-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(clone);
        try
        {
            Git(clone, "init", "-q", "-b", "main");
            Commit(clone, "README.md", "one\n", "One");
            string first = Git(clone, "rev-parse", "HEAD");
            Git(clone, "checkout", "-q", "-b", "side");
            Commit(clone, "side.txt", "side\n", "A side commit");
            string side = Git(clone, "rev-parse", "HEAD");
            Git(clone, "checkout", "-q", "main");
            Commit(clone, "README.md", "two\n", "Two");
            string main = Git(clone, "rev-parse", "HEAD");
            // A submodule's commit recorded and then moved, as a gitlink.
            Git(clone, "update-index", "--add", "--cacheinfo", $"160000,{first},sub");
            Git(clone, "commit", "-q", "-m", "A submodule");
            string linked = Git(clone, "rev-parse", "HEAD");
            Git(clone, "update-index", "--cacheinfo", $"160000,{side},sub");
            Git(clone, "commit", "-q", "-m", "The submodule moved");
            string moved = Git(clone, "rev-parse", "HEAD");

            var git = new GitCommandAdapter();
            Assert.False(git.Reachable(clone, side, "main"));
            // A graft giving main's commit the side commit as its parent, and a setting that hides submodules from diffs.
            File.WriteAllText(Path.Combine(clone, ".git", "info", "grafts"), $"{main} {side}\n");
            Git(clone, "config", "diff.ignoreSubmodules", "all");
            Assert.Equal("true", PlainGit(clone, "merge-base", "--is-ancestor", side, main));
            Assert.Empty(PlainGit(clone, "diff", "--name-only", linked, moved));

            Assert.False(git.Reachable(clone, side, "main"));
            Assert.False(git.Precedes(clone, side, main));
            Assert.True(git.Precedes(clone, first, main));
            Assert.False(git.Precedes(clone, main, main));
            Assert.Equal([new SourceChange("sub", "modified")], git.ChangedPaths(clone, linked, moved));
        }
        finally
        {
            try { Directory.Delete(clone, recursive: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    // Git as a clone's own configuration and info files steer it, for the control of what the overlap check's reads ignore.
    private static string PlainGit(string clone, params string[] arguments)
    {
        var info = new ProcessStartInfo("git") { WorkingDirectory = clone, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (string argument in arguments) info.ArgumentList.Add(argument);
        using Process process = Process.Start(info)!;
        string output = process.StandardOutput.ReadToEnd();
        process.StandardError.ReadToEnd();
        process.WaitForExit();
        return arguments[0] == "merge-base" ? (process.ExitCode == 0 ? "true" : "false") : output.Trim();
    }

    private static JsonElement Party(RunEstate run, string assignment) =>
        Assert.Single(run.Service.WorkOverlap().Json.GetProperty("assignments").EnumerateArray(), a => a.GetProperty("assignment").GetString() == assignment);

    [Fact]
    public void TheScopeAuditCountsAChangedPathMatchingAnExclusionAsOutside()
    {
        var before = new WorkspaceSnapshot(new Dictionary<string, string>(StringComparer.Ordinal));
        var after = new WorkspaceSnapshot(new Dictionary<string, string>(StringComparer.Ordinal) { ["docs/a.md"] = "changed", ["docs/operator-guide.md"] = "changed" });
        RunScope scope = WorkspaceScopeAudit.Compare(before, after, ["path:docs/**"], ["path:docs/operator-guide.md"]);
        Assert.Equal((2, 1), (scope.Changed, scope.OutsideCount));
        Assert.Equal(["docs/operator-guide.md"], scope.Outside);
        Assert.Equal(0, WorkspaceScopeAudit.Compare(before, after, ["path:docs/**"]).OutsideCount);
        // A provider that cannot apply exclusions says its audit is incomplete whenever the brief names one.
        IWorkspaceProvider plain = new PlainProvider();
        Assert.Null(plain.CheckScope(new PreparedWorkspace("w", "b", "c", false), before, ["path:docs/**"], []).Problem);
        Assert.Contains("cannot apply the brief's exclusions", plain.CheckScope(new PreparedWorkspace("w", "b", "c", false), before, ["path:docs/**"], ["path:docs/a.md"]).Problem, StringComparison.Ordinal);
    }

    private static JsonElement PolicyFixtureElement(JsonObject body) => JsonSerializer.SerializeToElement(body);

    /// <summary>The builder as an agent the generic launcher carries, a roster naming it for the builder role, and a second step of the goal, W-2, granting docs/.</summary>
    private static void Routed(RunEstate run)
    {
        string op = run.Estate.OperatorId;
        RunEstate.Ok(run.Service.NewRecord(new NewRecordRequest("principal", op, RunEstate.Json("""{"kind":"agent","name":"Test Builder","roles":["builder"],"agent":{"vendor":"Example","product":"generic"}}"""), Supersedes: run.Estate.BuilderId)));
        RunEstate.Ok(run.Service.NewRecord(new NewRecordRequest("roster", op, RunEstate.Json($$"""{"roles":[{"role":"builder","principals":["{{run.Estate.BuilderId}}"]},{"role":"decider","principals":["{{op}}"]}]}"""))));
        RunEstate.Ok(run.Service.NewRecord(new NewRecordRequest("work-order", op, RunEstate.Json($$"""{"key":"W-2","kind":"ordinary","title":"Document the widget","intent":"Document it.","scope":["widget guide","path:docs/"],"exitQuestion":"Does the guide describe the widget?","inputs":[{"type":"goal","id":"{{run.Goal}}"}],"deliverables":["guide"]}"""))));
    }

    private static JsonElement Brief(EstateModel model, string assignment) => model.ById[assignment].Body.GetProperty("brief");

    private static JsonElement After(string brief, string assignment)
    {
        JsonObject body = JsonNode.Parse(brief)!.AsObject();
        body["after"] = new JsonArray(assignment);
        return JsonSerializer.SerializeToElement(body);
    }

    private static void Order(RunEstate run, string key, string? goal = null) =>
        RunEstate.Ok(run.Service.NewRecord(new NewRecordRequest("work-order", run.Estate.OperatorId, RunEstate.Json($$"""{"key":"{{key}}","kind":"ordinary","title":"Work {{key}}","intent":"Exercise overlaps.","scope":["the widget"],"exitQuestion":"Done?","inputs":[{{(goal is null ? string.Empty : $$"""{"type":"goal","id":"{{goal}}"}""")}}],"deliverables":["widget"]}"""))));

    private static void Accept(RunEstate run, string key)
    {
        RecordEnvelope order = run.Estate.Load().Current("work-order").Single(o => EstateModel.BodyString(o, "key") == key);
        RunEstate.Ok(run.Service.NewRecord(new NewRecordRequest("acceptance", run.Estate.OperatorId, RunEstate.Json("""{"statement":"Accepted.","basis":[],"self":true}"""), $"work-order:{order.Id}:{order.ComputedHash}")));
    }

    private static RunResponse Start(RunEstate run, string assignment, string report) =>
        run.Service.RunStart(new RunStartRequest(assignment, "generic", run.Estate.BuilderId, run.Estate.OperatorId, run.Resource,
            Settings: new Dictionary<string, string>(StringComparer.Ordinal) { ["command"] = FakeHarness.Setting("generic", run.Report(report)) }));

    private static string Complete(RunEstate run, string assignment, string from, string head, string target = "main", string? repository = null) =>
        RunEstate.Ok(Submit(run, assignment, from, head, target, repository)).Id;

    private static WriteOutcome Submit(RunEstate run, string assignment, string from, string head, string target = "main", string? repository = null)
    {
        string attempt = RunEstate.Ok(run.Service.Attempt(new AttemptRequest(assignment, run.Estate.BuilderId, "widget repository, a work branch", "Start."))).Id;
        return run.Service.Result(new ResultRequest(attempt, run.Estate.BuilderId, RunEstate.Json($$$"""{"outcome":"completed","summary":"Built.","outputs":[],"evidence":[],"checks":[],"limitations":[],"unresolvedEffects":[],"nextStep":"none","change":{"repository":"{{{repository ?? run.Resource}}}","base":"{{{from}}}","head":"{{{head}}}","target":"{{{target}}}"}}""")));
    }

    private static void Commit(string clone, string path, string content, string message)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(clone, path))!);
        File.WriteAllText(Path.Combine(clone, path), content);
        Git(clone, "add", path);
        Git(clone, "commit", "-q", "-m", message);
    }

    // A concurrent-paths policy on the widget's repository, approved and signed with the operator's key held here.
    private static RecordEnvelope Declare(RunEstate run, string key, string[] selectors, string level)
    {
        TestEstate estate = run.Estate;
        // Signed after the operator's device was admitted, as the runtime's records are, at this machine's time.
        estate.SetClock(DateTimeOffset.UtcNow);
        var signer = new StoreSigner(new FileKeyStore(VericairnHome.Path()), run.OperatorDevice);
        var definition = new JsonObject
        {
            ["key"] = key, ["title"] = "Held paths", ["owner"] = estate.OperatorId, ["reason"] = "Keep concurrent work on shared paths in sequence.", ["state"] = "active",
            ["appliesTo"] = new JsonObject { ["kind"] = "repository", ["repository"] = run.Resource, ["selectors"] = new JsonArray([.. selectors.Select(s => (JsonNode)s)]) },
            ["rules"] = new JsonArray(new JsonObject { ["key"] = "concurrent", ["kind"] = AssignmentOverlaps.PolicyKind, ["version"] = 1, ["parameters"] = new JsonObject() }),
            ["level"] = level, ["startsAt"] = RecordParser.FormatTimestamp(estate.Clock.AddSeconds(10)), ["maxExceptionDays"] = 30,
        };
        var decision = new JsonObject
        {
            ["title"] = "Hold shared paths", ["statement"] = "Approve the stated definition only.", ["summary"] = "Hold concurrent work on shared paths.", ["synthesis"] = "Test evidence.",
            ["effects"] = new JsonArray("Apply the exact approved definition."), ["affects"] = new JsonArray(), ["decidedBy"] = estate.OperatorId, ["decidedAt"] = RecordParser.FormatTimestamp(estate.Clock),
            ["stake"] = "medium", ["notAuthorized"] = new JsonArray("publication"),
            ["policies"] = new JsonArray(new JsonObject { ["key"] = "definition", ["action"] = "declare", ["policy"] = key, ["change"] = "Approve this exact body.", ["definitionHash"] = PolicyRequirements.DefinitionHash(JsonSerializer.SerializeToElement(definition)) }),
        };
        RecordEnvelope approval = estate.Add("decision", estate.OperatorId, decision.ToJsonString(), signer: signer);
        definition["basis"] = new JsonArray(new JsonObject { ["decision"] = new JsonObject { ["type"] = "decision", ["id"] = approval.Id, ["hash"] = approval.ComputedHash }, ["requirement"] = "definition" });
        return estate.Add("estate-policy", estate.OperatorId, definition.ToJsonString(), signer: signer);
    }

    private static readonly string[] GitIdentity = ["-c", "user.name=Test", "-c", "user.email=test@example.invalid", "-c", "commit.gpgsign=false"];

    private static string Git(string directory, params string[] arguments)
    {
        var info = new ProcessStartInfo("git") { WorkingDirectory = directory, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (string argument in GitIdentity.Concat(arguments)) info.ArgumentList.Add(argument);
        using Process process = Process.Start(info)!;
        string output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        Assert.True(process.ExitCode == 0, process.StandardError.ReadToEnd());
        return output.Trim();
    }

    private sealed class PlainProvider : IWorkspaceProvider
    {
        public ProviderDescriptor Descriptor { get; } = new("plain", IWorkspaceProvider.Contract, "test", [], []);

        public PreparedWorkspace Prepare(string clone, string branch, string? baseReference, string directory) => throw new NotSupportedException();

        public RunScope CheckScope(PreparedWorkspace workspace, WorkspaceSnapshot baseline, IReadOnlyList<string> permitted) => new(0, 0, []);
    }
}
