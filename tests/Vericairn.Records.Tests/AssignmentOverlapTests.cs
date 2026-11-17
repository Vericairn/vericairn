// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using System.Text.Json.Nodes;
using Vericairn.Records.Estate;
using Vericairn.Records.Model;

namespace Vericairn.Records.Tests;

/// <summary>
/// From 0.9.4 (CODE-73; the estate's design/measured-work.md § 5): a brief's repository, exclusions and after and a result's change,
/// assignments active and released from the records, the overlaps of their effective permitted paths in one repository, the
/// concurrent-paths policies that hold them, and status's open overlaps.
/// </summary>
public class AssignmentOverlapTests
{
    private const string Head = "1111111111111111111111111111111111111111";
    private const string Base = "2222222222222222222222222222222222222222";

    [Fact]
    public void ABriefsRepositoryExclusionsAndAfterAreValidatedWithinFormatThree()
    {
        using var f = new PolicyFixture();
        RecordEnvelope repository = Repository(f, "widget");
        RecordEnvelope order = Order(f, "W-1");
        RecordEnvelope first = Assign(f, order, ["path:src/**"], repository: repository.Id);
        Assert.DoesNotContain(f.Estate.Load().Findings, x => x.Severity == Severity.Error);

        Assign(f, order, ["path:src/**"], repository: order.Id);
        Assign(f, order, ["path:src/**"], repository: "01M4TRNE1GYEK4P8MXQAB3C8Q4");
        Assign(f, order, ["path:src/**"], excluded: ["the operator guide"]);
        Assign(f, order, ["path:src/**"], after: [first.Id, first.Id]);
        Assign(f, order, ["path:src/**"], after: [.. Enumerable.Range(0, 21).Select(_ => first.Id)]);
        Assign(f, order, ["path:src/**"], after: ["01M4TRNE1GYEK4P8MXQAB3C8Q4"]);
        RecordEnvelope itself = Assign(f, order, ["path:src/**"]);
        Assign(f, order, ["path:src/**"], after: [itself.Id], supersedes: itself.Id);
        List<Finding> findings = [.. f.Estate.Load().Findings.Where(x => x.Severity == Severity.Error)];
        Assert.Equal(2, findings.Count(x => x.Code == "assignment.repository"));
        Assert.Single(findings, x => x.Code == "assignment.excluded" && x.Message.Contains("prose excludes nothing", StringComparison.Ordinal));
        Assert.Contains(findings, x => x.Code == "assignment.after" && x.Message == "Each assignment is named once.");
        Assert.Contains(findings, x => x.Code == "assignment.after" && x.Message.Contains("at most 20", StringComparison.Ordinal));
        Assert.Contains(findings, x => x.Code == "body.reference" && x.Location.EndsWith("#body.brief.after", StringComparison.Ordinal));
        Assert.Contains(findings, x => x.Code == "assignment.after" && x.Message == "An assignment never waits for itself.");
    }

    [Fact]
    public void WaitingInACircleIsRefusedFollowingEachAssignmentToItsCurrentRevision()
    {
        using var f = new PolicyFixture();
        RecordEnvelope order = Order(f, "W-1");
        RecordEnvelope a = Assign(f, order, ["path:src/**"]);
        RecordEnvelope b = Assign(f, order, ["path:docs/**"], after: [a.Id]);
        Assert.DoesNotContain(f.Estate.Load().Findings, x => x.Code == "assignment.after");
        RecordEnvelope c = Assign(f, order, ["path:tests/**"], after: [b.Id]);
        // a's revision naming c closes the circle a -> c -> b -> a, through b's first revision followed to its current one.
        RecordEnvelope revised = Assign(f, order, ["path:src/**"], after: [c.Id], supersedes: a.Id);
        Finding cycle = Assert.Single(f.Estate.Load().Findings, x => x.Code == "assignment.after" && x.Location.StartsWith(revised.ExpectedPath, StringComparison.Ordinal));
        Assert.Contains("makes a cycle", cycle.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ARecordFormatTwoBriefAndResultRefuseTheNewFields()
    {
        using var f = new PolicyFixture(format: 2);
        RecordEnvelope order = Order(f, "W-1");
        RecordEnvelope assignment = Assign(f, order, ["path:src/**"], repository: f.Estate.Estate.Id);
        Assert.Contains(f.Estate.Load().Findings, x => x.Code == "body.format" && x.Location == assignment.ExpectedPath + "#body.brief.repository");
        RecordEnvelope plain = Assign(f, order, ["path:src/**"]);
        RecordEnvelope result = Complete(f, plain, change: (f.Estate.Estate.Id, "main"));
        Assert.Contains(f.Estate.Load().Findings, x => x.Code == "body.format" && x.Location == result.ExpectedPath + "#body.change");
    }

    [Theory]
    [InlineData("main", null)]
    [InlineData("release/0.9", null)]
    [InlineData("a..b", "result.change")]
    [InlineData("-main", "result.change")]
    [InlineData("has space", "result.change")]
    public void AResultsChangeNamesARepositoryTwoCommitsAndABranch(string target, string? code)
    {
        using var f = new PolicyFixture();
        RecordEnvelope assignment = Assign(f, Order(f, "W-1"), ["path:src/**"], repository: f.Estate.Estate.Id);
        RecordEnvelope result = Complete(f, assignment, change: (f.Estate.Estate.Id, target));
        List<Finding> errors = [.. f.Estate.Load().Findings.Where(x => x.Severity == Severity.Error && x.Location.StartsWith(result.ExpectedPath, StringComparison.Ordinal))];
        if (code is null) Assert.Empty(errors);
        else Assert.Contains(errors, x => x.Code == code);
    }

    [Fact]
    public void AChangeWithAShortCommitOrAnotherRecordForItsRepositoryIsRefused()
    {
        using var f = new PolicyFixture();
        RecordEnvelope order = Order(f, "W-1");
        RecordEnvelope assignment = Assign(f, order, ["path:src/**"], repository: f.Estate.Estate.Id);
        RecordEnvelope attempt = Attempt(f, assignment);
        JsonObject body = PolicyFixture.ResultBody();
        body["change"] = new JsonObject { ["repository"] = order.Id, ["base"] = "abc", ["head"] = Head, ["target"] = "main" };
        RecordEnvelope result = f.Estate.Add("result", f.Estate.BuilderId, body.ToJsonString(), attempt.Reference);
        List<Finding> errors = [.. f.Estate.Load().Findings.Where(x => x.Severity == Severity.Error && x.Location.StartsWith(result.ExpectedPath, StringComparison.Ordinal))];
        Assert.Contains(errors, x => x.Code == "result.change" && x.Location.EndsWith(".repository", StringComparison.Ordinal));
        Assert.Contains(errors, x => x.Location.EndsWith(".base", StringComparison.Ordinal));
    }

    [Fact]
    public void AssignmentsAreActiveUntilTheirWorkIsReleasedAsTheRecordsShowIt()
    {
        using var f = new PolicyFixture();
        // Recorded before the overlap check: no record names a repository, exclusions, after or a change yet.
        RecordEnvelope early = Assign(f, Order(f, "W-0"), ["path:src/**"]);
        Complete(f, early);
        RecordEnvelope waiting = Assign(f, Order(f, "W-1"), ["path:src/**"], repository: f.Estate.Estate.Id);
        RecordEnvelope abandoned = Assign(f, Order(f, "W-2"), ["path:src/**"], repository: f.Estate.Estate.Id);
        Complete(f, abandoned, outcome: "abandoned");
        RecordEnvelope prose = Assign(f, Order(f, "W-3"), ["the widget's sources"]);
        Complete(f, prose);
        RecordEnvelope unnamed = Assign(f, Order(f, "W-4"), ["path:src/**"], repository: f.Estate.Estate.Id);
        Complete(f, unnamed);
        RecordEnvelope changed = Assign(f, Order(f, "W-5"), ["path:src/**"], repository: f.Estate.Estate.Id);
        Complete(f, changed, change: (f.Estate.Estate.Id, "main"));
        RecordEnvelope partial = Assign(f, Order(f, "W-6"), ["path:src/**"], repository: f.Estate.Estate.Id);
        Complete(f, partial, outcome: "partial");
        RecordEnvelope acceptedOrder = Order(f, "W-7");
        RecordEnvelope accepted = Assign(f, acceptedOrder, ["path:src/**"], repository: f.Estate.Estate.Id);
        f.Add("acceptance", new JsonObject { ["statement"] = "Accepted.", ["basis"] = new JsonArray(), ["self"] = true }, subject: acceptedOrder.Reference);

        EstateModel model = f.Estate.Load();
        Dictionary<string, AssignmentOverlaps.Party> parties = AssignmentOverlaps.Parties(model).ToDictionary(p => p.Assignment.Id, StringComparer.Ordinal);
        Assert.False(parties[early.Id].Active);
        Assert.Contains("recorded before the overlap check", Assert.Single(parties[early.Id].Labels), StringComparison.Ordinal);
        Assert.Equal((true, "active: no attempt yet"), (parties[waiting.Id].Active, parties[waiting.Id].State));
        Assert.StartsWith("released: its current result", parties[abandoned.Id].State, StringComparison.Ordinal);
        Assert.False(parties[abandoned.Id].Active);
        Assert.Contains("brief holds no path pattern", parties[prose.Id].State, StringComparison.Ordinal);
        Assert.False(parties[prose.Id].Active);
        Assert.True(parties[unnamed.Id].Active);
        Assert.Contains("active until acceptance", parties[unnamed.Id].State, StringComparison.Ordinal);
        Assert.True(parties[partial.Id].Active);
        Assert.Equal("released: its work order is accepted", parties[accepted.Id].State);

        // A change's integration as this machine's clone shows it, labeled; unknown keeps the assignment active.
        Assert.True(parties[changed.Id].Active);
        Assert.Equal("unknown", parties[changed.Id].Change!.Integration);
        AssignmentOverlaps.Party reachable = AssignmentOverlaps.PartyOf(model, changed, new View { Reachable = true })!;
        Assert.False(reachable.Active);
        Assert.Contains("observed here", Assert.Single(reachable.Labels), StringComparison.Ordinal);
        AssignmentOverlaps.Party behind = AssignmentOverlaps.PartyOf(model, changed, new View { Reachable = false })!;
        Assert.True(behind.Active);
        Assert.Equal("not-integrated", behind.Change!.Integration);
    }

    [Fact]
    public void AChangeReleasesItsAssignmentOnlyAsItsOwnWorkIntegratedIntoTheMainLine()
    {
        using var f = new PolicyFixture();
        RecordEnvelope widget = Repository(f, "widget");
        RecordEnvelope gadget = Repository(f, "gadget");
        RecordEnvelope honest = Assign(f, Order(f, "W-1"), ["path:src/**"], repository: widget.Id);
        Complete(f, honest, change: (widget.Id, "main"));
        RecordEnvelope ownBranch = Assign(f, Order(f, "W-2"), ["path:src/**"], repository: widget.Id);
        Complete(f, ownBranch, change: (widget.Id, "work/w-2"));
        RecordEnvelope same = Assign(f, Order(f, "W-3"), ["path:src/**"], repository: widget.Id);
        Complete(f, same, change: (widget.Id, "main"), from: Head);
        RecordEnvelope elsewhere = Assign(f, Order(f, "W-4"), ["path:src/**"], repository: widget.Id);
        Complete(f, elsewhere, change: (gadget.Id, "main"));
        RecordEnvelope unnamed = Assign(f, Order(f, "W-5"), ["path:src/**"]);
        Complete(f, unnamed, change: (widget.Id, "main"));
        EstateModel model = f.Estate.Load();
        Assert.DoesNotContain(model.Findings, x => x.Severity == Severity.Error && x.Location.StartsWith("records/result", StringComparison.Ordinal));

        // The honest change, its base before its head and its head reachable from the main line, releases its assignment.
        var reached = new View { Reachable = true };
        AssignmentOverlaps.Party released = AssignmentOverlaps.PartyOf(model, honest, reached)!;
        Assert.False(released.Active);
        Assert.Equal(("reachable", "main"), (released.Change!.Integration, released.Change.Branch));

        // Reachability is read from the main line only, never from the branch the result names, so its own work branch releases nothing.
        var view = new View { Reachable = false };
        AssignmentOverlaps.Party behind = AssignmentOverlaps.PartyOf(model, ownBranch, view)!;
        Assert.True(behind.Active);
        Assert.Equal("not-integrated", behind.Change!.Integration);
        Assert.Contains($"{Head} reachable from main in {widget.Id}", view.Reads);
        Assert.DoesNotContain(view.Reads, r => r.Contains("work/w-2", StringComparison.Ordinal));
        Assert.Contains(behind.Labels, l => l.Contains("names work/w-2 as its target; only the repository's main line, main", StringComparison.Ordinal));

        // A change whose base is its head, or that is in another repository than its assignment's, or whose assignment names none,
        // cannot be the assignment's work: it stays active whatever the clone shows, and the clone is not asked whether it is reachable.
        foreach ((RecordEnvelope assignment, string problem) in new[]
        {
            (same, "its base and head are the same commit"), (elsewhere, $"it is in gadget ({gadget.Id}), not in widget ({widget.Id})"), (unnamed, "its assignment names no repository"),
        })
        {
            var clone = new View { Reachable = true };
            AssignmentOverlaps.Party party = AssignmentOverlaps.PartyOf(model, assignment, clone)!;
            Assert.True(party.Active, problem);
            Assert.Equal("mismatched", party.Change!.Integration);
            Assert.Contains(problem, party.Change.Problem, StringComparison.Ordinal);
            Assert.StartsWith("active: its change cannot release it, since ", party.State, StringComparison.Ordinal);
            Assert.Empty(clone.Reads);
            Assert.True(AssignmentOverlaps.PartyOf(model, assignment)!.Active);
        }

        // A base that is no ancestor of the head in the clone is not this change's range either.
        AssignmentOverlaps.Party unrelated = AssignmentOverlaps.PartyOf(model, honest, new View { Reachable = true, Precedes = false })!;
        Assert.True(unrelated.Active);
        Assert.Contains("is not an ancestor of its head", unrelated.Change!.Problem, StringComparison.Ordinal);
        // With no main line in the clone, nothing is observed integrated.
        Assert.True(AssignmentOverlaps.PartyOf(model, honest, new View { Reachable = true, Line = null })!.Active);
    }

    [Fact]
    public void AReadingAsksTheCloneOnlyForCompletedResultsOfOpenWorkAndOnceForEachChange()
    {
        using var f = new PolicyFixture();
        RecordEnvelope widget = Repository(f, "widget");
        RecordEnvelope acceptedOrder = Order(f, "W-1");
        RecordEnvelope accepted = Assign(f, acceptedOrder, ["path:src/**"], repository: widget.Id);
        Complete(f, accepted, change: (widget.Id, "main"));
        f.Add("acceptance", new JsonObject { ["statement"] = "Accepted.", ["basis"] = new JsonArray(), ["self"] = true }, subject: acceptedOrder.Reference);
        RecordEnvelope abandoned = Assign(f, Order(f, "W-2"), ["path:src/**"], repository: widget.Id);
        Complete(f, abandoned, outcome: "abandoned", change: (widget.Id, "main"));
        RecordEnvelope partial = Assign(f, Order(f, "W-3"), ["path:src/**"], repository: widget.Id);
        Complete(f, partial, outcome: "partial", change: (widget.Id, "main"));
        EstateModel model = f.Estate.Load();

        var view = new View { Reachable = true };
        Dictionary<string, AssignmentOverlaps.Party> parties = AssignmentOverlaps.Parties(model, view).ToDictionary(p => p.Assignment.Id, StringComparer.Ordinal);
        Assert.Equal("released: its work order is accepted", parties[accepted.Id].State);
        Assert.False(parties[abandoned.Id].Active);
        Assert.True(parties[partial.Id].Active);
        Assert.Empty(view.Reads);

        // Two open assignments whose completed results name the same change read the clone once for it within one reading.
        RecordEnvelope first = Assign(f, Order(f, "W-4"), ["path:src/**"], repository: widget.Id);
        Complete(f, first, change: (widget.Id, "main"));
        RecordEnvelope second = Assign(f, Order(f, "W-5"), ["path:src/**"], repository: widget.Id);
        Complete(f, second, change: (widget.Id, "main"));
        var counted = new View { Reachable = false };
        Assert.All(AssignmentOverlaps.Parties(f.Estate.Load(), counted).Where(p => p.Assignment.Id == first.Id || p.Assignment.Id == second.Id), p => Assert.True(p.Active));
        Assert.Equal([$"main line of {widget.Id}", $"{Base} before {Head} in {widget.Id}", $"{Head} reachable from main in {widget.Id}", $"paths {Base}..{Head} in {widget.Id}"], counted.Reads);
    }

    [Fact]
    public void AChangeMergeObservedAtTheChangesHeadReleasesItsAssignment()
    {
        using var f = new PolicyFixture();
        RecordEnvelope repository = f.Add("resource", new JsonObject { ["kind"] = "repository", ["name"] = "Widget", ["role"] = "Source", ["location"] = new JsonObject { ["github"] = "Example/widget" }, ["visibility"] = "private" });
        RecordEnvelope product = f.Add("product", JsonNode.Parse($$$"""
            {"name":"Widget","slug":"widget","summary":"A widget.","editions":[{"name":"Community","slug":"community","summary":"Free."}],"surfaces":[{"name":"CLI","slug":"cli","summary":"Commands."}],
             "changes":{"provider":"example","repository":"{{{repository.Id}}}","target":"main","requiredChecks":["tests"]}}
            """)!.AsObject());
        RecordEnvelope assignment = Assign(f, Order(f, "W-1"), ["path:src/**"], repository: repository.Id);
        Complete(f, assignment, change: (repository.Id, "main"));
        Assert.True(AssignmentOverlaps.PartyOf(f.Estate.Load(), assignment)!.Active);

        var body = new JsonObject
        {
            ["provider"] = "example", ["action"] = "change-merge", ["identity"] = "example-change", ["intent"] = "Merge the widget's change.", ["status"] = "not-started",
            ["target"] = new JsonObject { ["head"] = Head, ["product"] = product.Id, ["productHash"] = product.ComputedHash, ["repository"] = repository.Id, ["repositoryHash"] = repository.ComputedHash, ["destination"] = "Example/widget", ["target"] = "main" },
            ["requiredChecks"] = new JsonArray("tests"), ["evidence"] = new JsonArray(),
            ["checks"] = new JsonArray(new JsonObject { ["name"] = "tests", ["head"] = Head, ["status"] = "completed", ["conclusion"] = "success" }),
        };
        RecordEnvelope intent = f.Add("operation", body);
        body["status"] = "dispatched";
        body["dispatchedAt"] = "2026-10-13T13:00:00Z";
        RecordEnvelope dispatched = f.Add("operation", body, supersedes: intent.Id);
        body["status"] = "observed-success";
        body["observed"] = new JsonObject { ["summary"] = "Merged.", ["at"] = "2026-10-13T13:01:00Z" };
        RecordEnvelope merged = f.Add("operation", body, supersedes: dispatched.Id);
        EstateModel model = f.Estate.Load();
        Assert.DoesNotContain(model.Findings, x => x.Severity == Severity.Error && x.Location.StartsWith("records/operation", StringComparison.Ordinal));
        AssignmentOverlaps.Party released = AssignmentOverlaps.PartyOf(model, assignment)!;
        Assert.False(released.Active);
        Assert.Equal(("merged", merged.Id), (released.Change!.Integration, released.Change.Merge!.Id));
    }

    [Fact]
    public void OverlapsAreComparedInOneRepositoryLessExclusionsAndTheEstatesRecords()
    {
        using var f = new PolicyFixture();
        RecordEnvelope widget = Repository(f, "widget");
        RecordEnvelope gadget = Repository(f, "gadget");
        RecordEnvelope order = Order(f, "W-1");
        RecordEnvelope a = Assign(f, order, ["path:src/**", "the docs"], repository: widget.Id);
        RecordEnvelope b = Assign(f, order, ["path:src/core/"], repository: widget.Id);
        Assign(f, order, ["path:src/**"], repository: gadget.Id);
        RecordEnvelope c = Assign(f, order, ["path:src/core/**"], repository: widget.Id, excluded: ["path:src/core/**"]);
        RecordEnvelope estateA = Assign(f, order, ["path:records/**", "path:objects/**"], repository: f.Estate.Estate.Id);
        RecordEnvelope estateB = Assign(f, order, ["path:records/**"], repository: f.Estate.Estate.Id);
        EstateModel model = f.Estate.Load();
        Assert.DoesNotContain(model.Findings, x => x.Severity == Severity.Error);

        AssignmentOverlaps.Overlap overlap = Assert.Single(AssignmentOverlaps.Overlaps(model));
        Assert.Equal((a.Id, b.Id, widget.Id), (overlap.First.Assignment.Id, overlap.Second.Assignment.Id, overlap.Repository));
        Assert.True(overlap.Determined && overlap.Open && !overlap.Tracked);
        Assert.Equal("src/core", Assert.Single(overlap.Witnesses));
        Assert.DoesNotContain(new[] { c.Id, estateA.Id, estateB.Id }, id => id == overlap.First.Assignment.Id || id == overlap.Second.Assignment.Id);

        // Tracked paths at the clone's main line are the witnesses when the intersection holds some.
        var view = new View { Paths = ("main", ["README.md", "src/core/a.cs", "src/core/b.cs", "src/other.cs"]) };
        AssignmentOverlaps.Overlap tracked = Assert.Single(AssignmentOverlaps.Overlaps(model, view));
        Assert.True(tracked.Tracked);
        Assert.Equal(["src/core/a.cs", "src/core/b.cs"], tracked.Witnesses);
        Assert.Equal("main", tracked.Branch);
        Assert.Contains(AssignmentOverlaps.Overlaps(model, new View { Folds = true })[0].Labels, l => l.Contains("case-insensitive", StringComparison.Ordinal));
    }

    [Fact]
    public void AnAssignmentNamingNoRepositoryIsComparedAsPossiblyInAnyAndLabeled()
    {
        using var f = new PolicyFixture();
        RecordEnvelope widget = Repository(f, "widget");
        RecordEnvelope order = Order(f, "W-1");
        Assign(f, order, ["path:docs/**"], repository: widget.Id);
        RecordEnvelope unnamed = Assign(f, order, ["path:docs/guide.md"]);
        AssignmentOverlaps.Overlap overlap = Assert.Single(AssignmentOverlaps.Overlaps(f.Estate.Load()));
        Assert.Equal(widget.Id, overlap.Repository);
        Assert.Contains(overlap.Labels, l => l.Contains(unnamed.Id, StringComparison.Ordinal) && l.Contains("possibly in any repository", StringComparison.Ordinal));
    }

    [Fact]
    public void AfterSequencesAnOverlapAndAWaitLastsUntilTheCurrentRevisionIsReleased()
    {
        using var f = new PolicyFixture();
        RecordEnvelope order = Order(f, "W-1");
        RecordEnvelope first = Assign(f, order, ["path:docs/**"], repository: f.Estate.Estate.Id);
        RecordEnvelope later = Assign(f, order, ["path:docs/**"], repository: f.Estate.Estate.Id, after: [first.Id]);
        AssignmentOverlaps.Overlap overlap = Assert.Single(AssignmentOverlaps.Overlaps(f.Estate.Load()));
        Assert.True(overlap.Sequenced);
        Assert.False(overlap.Open);
        Assert.Equal("sequence", overlap.Resolution);
        Assert.Equal(string.Empty, AssignmentOverlaps.StatusText(f.Estate.Load(), AssignmentOverlaps.Overlaps(f.Estate.Load())));

        // The earlier assignment revised: the wait follows it to its current revision, and ends once that is released.
        RecordEnvelope revised = Assign(f, order, ["path:docs/**"], repository: f.Estate.Estate.Id, supersedes: first.Id);
        AssignmentOverlaps.Wait wait = Assert.Single(AssignmentOverlaps.Waits(f.Estate.Load(), later));
        Assert.Equal((first.Id, revised.Id), (wait.Named, wait.Current.Id));
        Assert.Contains("is not released yet", AssignmentOverlaps.WaitRefusal(f.Estate.Load(), later, "a new attempt"), StringComparison.Ordinal);
        Complete(f, revised, outcome: "abandoned");
        Assert.Empty(AssignmentOverlaps.Waits(f.Estate.Load(), later));
        Assert.Null(AssignmentOverlaps.WaitRefusal(f.Estate.Load(), later, "a new attempt"));
    }

    [Fact]
    public void AConcurrentPathsPolicyHoldsAnOverlapWithinItsSelectionAtItsLevel()
    {
        using var f = new PolicyFixture();
        RecordEnvelope widget = Repository(f, "widget");
        RecordEnvelope gadget = Repository(f, "gadget");
        RecordEnvelope order = Order(f, "W-1");
        RecordEnvelope owner = Assign(f, order, ["path:docs/**"], repository: widget.Id);
        RecordEnvelope other = Assign(f, order, ["path:docs/**"], repository: widget.Id);
        RecordEnvelope held = Policy(f, "held-guides", widget, ["docs/operator-guide.md"], "refuse");
        Policy(f, "gadget-guides", gadget, ["docs/**"], "refuse");
        EstateModel model = f.Estate.Load();
        Assert.DoesNotContain(model.Findings, x => x.Severity == Severity.Error);
        AssignmentOverlaps.Overlap overlap = Assert.Single(AssignmentOverlaps.Overlaps(model));
        AssignmentOverlaps.Hold hold = Assert.Single(overlap.Holds);
        Assert.Equal((held.Id, "refuse", "docs/operator-guide.md"), (hold.Policy.Record.Id, hold.Policy.Level, hold.Witness));
        Assert.True(overlap.Held);
        Assert.Contains("held at refuse by policy held-guides", AssignmentOverlaps.StatusText(model, AssignmentOverlaps.Overlaps(model)), StringComparison.Ordinal);

        // One owner: the other assignment excludes the held guide, so the overlap stays and the policy no longer holds it.
        Assign(f, order, ["path:docs/**"], repository: widget.Id, excluded: ["path:docs/operator-guide.md"], supersedes: other.Id);
        AssignmentOverlaps.Overlap resolved = Assert.Single(AssignmentOverlaps.Overlaps(f.Estate.Load()));
        Assert.Empty(resolved.Holds);
        Assert.False(resolved.Held);
        Assert.True(resolved.Open);
        Assert.Equal(owner.Id, resolved.First.Assignment.Id);
    }

    [Fact]
    public void AWarnLevelPolicyReportsTheOverlapWithoutHoldingIt()
    {
        using var f = new PolicyFixture();
        RecordEnvelope widget = Repository(f, "widget");
        RecordEnvelope order = Order(f, "W-1");
        Assign(f, order, ["path:docs/**"], repository: widget.Id);
        Assign(f, order, ["path:docs/**"], repository: widget.Id);
        Policy(f, "reported-guides", widget, ["docs/**"], "warn");
        AssignmentOverlaps.Overlap overlap = Assert.Single(AssignmentOverlaps.Overlaps(f.Estate.Load()));
        Assert.Equal("warn", Assert.Single(overlap.Holds).Policy.Level);
        Assert.False(overlap.Held);
    }

    [Fact]
    public void StatusShowsOpenOverlapsFromTheRecordsAloneIdenticallyOnEveryRead()
    {
        using var f = new PolicyFixture();
        RecordEnvelope order = Order(f, "W-1");
        Assert.DoesNotContain("\"overlaps\"", EstateStatus.RenderJson(f.Estate.Load(), new FindingList()), StringComparison.Ordinal);
        RecordEnvelope a = Assign(f, order, ["path:docs/**"], repository: f.Estate.Estate.Id);
        RecordEnvelope b = Assign(f, order, ["path:docs/guide.md"], repository: f.Estate.Estate.Id);
        EstateModel model = f.Estate.Load();
        string json = EstateStatus.RenderJson(model, new FindingList());
        Assert.Equal(json, EstateStatus.RenderJson(f.Estate.Load(), new FindingList()));
        using JsonDocument status = JsonDocument.Parse(json);
        JsonElement overlap = Assert.Single(status.RootElement.GetProperty("overlaps").EnumerateArray());
        Assert.Equal((a.Id, b.Id, "docs/guide.md", "possible", "none"), (overlap.GetProperty("first").GetProperty("assignment").GetString(), overlap.GetProperty("second").GetProperty("assignment").GetString(),
            overlap.GetProperty("witnesses")[0].GetString(), overlap.GetProperty("witnessKind").GetString(), overlap.GetProperty("resolution").GetString()));
        Assert.Contains($"Overlapping assignments: 1 open (vericairn work overlap)\n  W-1 {a.Id} and W-1 {b.Id} in the estate's own repository", EstateStatus.Render(model, new FindingList()), StringComparison.Ordinal);
        Assert.Contains("\"overlaps\"", ProgressReport.RenderJson(model, null), StringComparison.Ordinal);
        Assert.Contains("Overlapping assignments: 1 open", ProgressReport.Render(model, null), StringComparison.Ordinal);
    }

    [Fact]
    public void ADraftBriefIsPreviewedAgainstTheActiveAssignments()
    {
        using var f = new PolicyFixture();
        RecordEnvelope widget = Repository(f, "widget");
        RecordEnvelope gadget = Repository(f, "gadget");
        RecordEnvelope active = Assign(f, Order(f, "W-1"), ["path:docs/**"], repository: widget.Id);
        EstateModel model = f.Estate.Load();
        JsonElement brief = JsonSerializer.SerializeToElement(new JsonObject { ["permittedChanges"] = new JsonArray("path:docs/cli.md", "prose entries grant nothing") });
        AssignmentOverlaps.Party draft = AssignmentOverlaps.Draft(model, brief, widget.Id);
        AssignmentOverlaps.Overlap overlap = Assert.Single(AssignmentOverlaps.With(model, draft));
        Assert.Equal((active.Id, "docs/cli.md"), (overlap.First.Assignment.Id, overlap.Witnesses[0]));
        Assert.StartsWith("Overlap: W-1's assignment " + active.Id + " and the draft brief in widget", AssignmentOverlaps.Sentence(model, overlap), StringComparison.Ordinal);
        Assert.Empty(AssignmentOverlaps.With(model, AssignmentOverlaps.Draft(model, brief, gadget.Id)));
    }

    private static RecordEnvelope Repository(PolicyFixture f, string name) =>
        f.Add("resource", new JsonObject { ["kind"] = "repository", ["name"] = name, ["role"] = "Source.", ["location"] = new JsonObject { ["github"] = "Example/" + name }, ["visibility"] = "private" });

    private static RecordEnvelope Order(PolicyFixture f, string key) =>
        f.Add("work-order", new JsonObject { ["key"] = key, ["kind"] = "ordinary", ["title"] = "Work " + key, ["intent"] = "Exercise overlaps.", ["scope"] = new JsonArray("x"), ["exitQuestion"] = "Done?", ["inputs"] = new JsonArray(), ["deliverables"] = new JsonArray("y") });

    private static RecordEnvelope Assign(PolicyFixture f, RecordEnvelope order, string[] permitted, string? repository = null, string[]? excluded = null, string[]? after = null, string? supersedes = null)
    {
        var brief = new JsonObject
        {
            ["outcome"] = "The work.", ["sources"] = new JsonArray(), ["permittedChanges"] = new JsonArray([.. permitted.Select(p => (JsonNode)p)]), ["prohibitedEffects"] = new JsonArray(),
            ["workspace"] = new JsonArray("a work branch"), ["limits"] = new JsonArray(), ["expectedOutputs"] = new JsonArray("y"), ["reviewCriteria"] = new JsonArray("done"), ["recovery"] = "Resume from the brief.",
        };
        if (repository is not null) brief["repository"] = repository;
        if (excluded is not null) brief["excludedChanges"] = new JsonArray([.. excluded.Select(p => (JsonNode)p)]);
        if (after is not null) brief["after"] = new JsonArray([.. after.Select(p => (JsonNode)p)]);
        return f.Add("assignment", new JsonObject { ["contributor"] = f.Estate.BuilderId, ["brief"] = brief }, subject: order.Reference, supersedes: supersedes);
    }

    private static RecordEnvelope Attempt(PolicyFixture f, RecordEnvelope assignment) =>
        f.Add("attempt", new JsonObject { ["startedAt"] = "2026-10-13T12:00:00Z", ["workspace"] = "a work branch", ["note"] = "Work." }, builder: true, subject: assignment.Reference);

    private static RecordEnvelope Complete(PolicyFixture f, RecordEnvelope assignment, string outcome = "completed", (string Repository, string Target)? change = null, string from = Base, string head = Head)
    {
        RecordEnvelope attempt = Attempt(f, assignment);
        JsonObject body = PolicyFixture.ResultBody();
        body["outcome"] = outcome;
        if (change is { } named) body["change"] = new JsonObject { ["repository"] = named.Repository, ["base"] = from, ["head"] = head, ["target"] = named.Target };
        return f.Add("result", body, builder: true, subject: attempt.Reference);
    }

    private static RecordEnvelope Policy(PolicyFixture f, string key, RecordEnvelope repository, string[] selectors, string level)
    {
        JsonObject body = f.PolicyBody(key, duty: false);
        body["appliesTo"] = new JsonObject { ["kind"] = "repository", ["repository"] = repository.Id, ["selectors"] = new JsonArray([.. selectors.Select(s => (JsonNode)s)]) };
        body["rules"] = new JsonArray(new JsonObject { ["key"] = "concurrent", ["kind"] = AssignmentOverlaps.PolicyKind, ["version"] = 1, ["parameters"] = new JsonObject() });
        body["level"] = level;
        return f.Policy(body);
    }

    // A clone as the check reads it, counting each read of a change so that a test sees which reads were made.
    private sealed class View : IRepositoryView
    {
        public (string Branch, IReadOnlyList<string> Paths)? Paths { get; init; }

        public bool? Reachable { get; init; }

        public bool? Precedes { get; init; } = true;

        public string? Line { get; init; } = "main";

        public bool Folds { get; init; }

        public List<string> Reads { get; } = [];

        (string Branch, IReadOnlyList<string> Paths)? IRepositoryView.Tracked(string repository) => Paths;

        string? IRepositoryView.MainLine(string repository)
        {
            Reads.Add($"main line of {repository}");
            return Line;
        }

        bool? IRepositoryView.Reachable(string repository, string commit, string branch)
        {
            Reads.Add($"{commit} reachable from {branch} in {repository}");
            return Reachable;
        }

        bool? IRepositoryView.Precedes(string repository, string ancestor, string descendant)
        {
            Reads.Add($"{ancestor} before {descendant} in {repository}");
            return Precedes;
        }

        bool IRepositoryView.CaseInsensitive(string repository) => Folds;

        IReadOnlyList<(string Path, string Change)>? IRepositoryView.Changed(string repository, string from, string to)
        {
            Reads.Add($"paths {from}..{to} in {repository}");
            return null;
        }
    }
}
