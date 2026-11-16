// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using Vericairn.Records.Estate;
using Vericairn.Records.Model;
using static Vericairn.Records.Tests.GoalEvaluationTests;

namespace Vericairn.Records.Tests;

/// <summary>
/// What status reads once per judged estate instead of once for each record or view (FIX-136): the work orders that name a goal, the
/// current operations' identities and the living documents' states. Each answers as the whole-estate pass it replaces did.
/// </summary>
public class StatusPassTests
{
    [Fact]
    public void AGoalsWorkIsEveryWorkOrderOneOfWhoseRevisionsNamesARevisionOfTheGoalEachOnceAtItsLatestRevision()
    {
        using var estate = new TestEstate(recordFormat: 2);
        RecordEnvelope goal = estate.Add("goal", estate.OperatorId, Goal(estate, """{"policy":"proposed-by-planner","approval":"self-approves-ordinary"}"""));
        RecordEnvelope revisedGoal = estate.Add("goal", estate.OperatorId, Goal(estate, """{"policy":"proposed-by-planner","approval":"self-approves-ordinary"}""")
            .Replace("Ship the widget.", "Ship the widget soon.", StringComparison.Ordinal), supersedes: goal.Id);
        RecordEnvelope first = estate.Add("work-order", estate.BuilderId, Order("W-1", Names(goal)));
        RecordEnvelope second = estate.Add("work-order", estate.BuilderId, Order("W-2", Names(revisedGoal)));
        RecordEnvelope both = estate.Add("work-order", estate.BuilderId, Order("W-3", Names(goal) + "," + Names(revisedGoal)));
        RecordEnvelope unnamed = estate.Add("work-order", estate.BuilderId, Order("W-4", string.Empty));
        RecordEnvelope cutoff = estate.Add("work-status", estate.OperatorId, """{"status":"active","note":"Started."}""", first.Reference);
        RecordEnvelope named = estate.Add("work-order", estate.BuilderId, Order("W-4", Names(goal)), supersedes: unnamed.Id);
        RecordEnvelope dropped = estate.Add("work-order", estate.BuilderId, Order("W-1", string.Empty), supersedes: first.Id);
        estate.Add("work-order", estate.BuilderId, Order("W-5", string.Empty));

        EstateModel model = estate.Load();
        Assert.DoesNotContain(model.Findings, f => f.Severity == Severity.Error);
        // A revision that named the goal keeps its work order in the goal's work, which stands at its latest revision; a work order
        // naming two revisions of the goal is there once; one named only by a later revision joins from that revision on.
        Assert.Equal([dropped.Id, second.Id, both.Id, named.Id], GoalRules.Work(model, revisedGoal).Select(o => o.Id));
        // The first revision of the goal is named only by what names it, not by what names its successor.
        Assert.Equal([dropped.Id, both.Id, named.Id], GoalRules.Work(model, goal).Select(o => o.Id));
        Assert.Equal([dropped.Id, second.Id, both.Id], GoalRules.Work(model, revisedGoal, cutoff).Select(o => o.Id));
    }

    [Fact]
    public void AnOperationsIdentityIsDuplicatedOnlyByAnotherCurrentOperation()
    {
        using var estate = new TestEstate();
        RecordEnvelope first = estate.Add("operation", estate.OperatorId, Operation("example:one", "not-started"));
        RecordEnvelope revised = estate.Add("operation", estate.OperatorId, Operation("example:one", "canceled"), supersedes: first.Id);
        RecordEnvelope other = estate.Add("operation", estate.OperatorId, Operation("example:two", "not-started"));
        EstateModel model = estate.Load();
        Assert.DoesNotContain(model.Findings, f => f.Code == "operation.duplicate-identity");

        RecordEnvelope duplicate = estate.Add("operation", estate.OperatorId, Operation("example:two", "not-started"));
        model = estate.Load();
        Assert.Equal([other.ExpectedPath, duplicate.ExpectedPath],
            model.Findings.Where(f => f.Code == "operation.duplicate-identity").Select(f => f.Location).Order(StringComparer.Ordinal));
        Assert.DoesNotContain(model.Findings, f => f.Code == "operation.duplicate-identity" && f.Location == revised.ExpectedPath);
    }

    [Fact]
    public void LivingDocumentsAreDerivedOnceForAJudgedEstateAndAfreshForAnother()
    {
        using var estate = new TestEstate();
        estate.Add("living-document", estate.OperatorId, """{"path":"docs/guide.md","title":"Guide","kind":"authored","source":"estate-file","triggers":[{"on":"decision","level":"warn"}],"summary":"Follows the decisions."}""");
        EstateModel model = estate.Load();
        IReadOnlyList<LivingDocuments.State> states = LivingDocuments.Derive(model);
        Assert.Same(states, LivingDocuments.Derive(model));
        LivingDocuments.State state = Assert.Single(states);
        Assert.False(state.Due);
        // Every reader is given the same states, so none can change what a later one reads.
        Assert.True(((ICollection<LivingDocuments.Reason>)state.Reasons).IsReadOnly);
        Assert.True(((ICollection<string>)state.Unavailable).IsReadOnly);
        Assert.True(((ICollection<string>)state.Notes).IsReadOnly);

        // A changed manifest is read afresh, and another judgment of the estate derives its own states.
        model.Manifest = model.Manifest;
        Assert.NotSame(states, LivingDocuments.Derive(model));
        estate.Add("living-document", estate.OperatorId, """{"path":"docs/notes.md","title":"Notes","kind":"authored","source":"estate-file","triggers":[{"on":"decision","level":"warn"}],"summary":"Follows the decisions."}""");
        Assert.Equal(["docs/guide.md", "docs/notes.md"], LivingDocuments.Derive(estate.Load()).Select(d => d.Path));
        Assert.Single(LivingDocuments.Derive(model));
    }

    private static string Names(RecordEnvelope goal) => $$"""{"type":"goal","id":"{{goal.Id}}"}""";

    private static string Operation(string identity, string status) =>
        $$"""{"provider":"example","action":"dispatch-build","identity":"{{identity}}","intent":"Build it.","target":{"repository":"Example/example"},"status":"{{status}}","evidence":[]}""";
}
