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
        // The same revision without a narrowing is not held by the run.
        body["brief"]!.AsObject().Remove("excludedChanges");
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

    private static string Complete(RunEstate run, string assignment, string from, string head)
    {
        string attempt = RunEstate.Ok(run.Service.Attempt(new AttemptRequest(assignment, run.Estate.BuilderId, "widget repository, a work branch", "Start."))).Id;
        return RunEstate.Ok(run.Service.Result(new ResultRequest(attempt, run.Estate.BuilderId, RunEstate.Json($$$"""{"outcome":"completed","summary":"Built.","outputs":[],"evidence":[],"checks":[],"limitations":[],"unresolvedEffects":[],"nextStep":"none","change":{"repository":"{{{run.Resource}}}","base":"{{{from}}}","head":"{{{head}}}","target":"main"}}""")))).Id;
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
