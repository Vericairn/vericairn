// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jayson Furr

using Vericairn.Records.Estate;
using Vericairn.Records.Hashing;
using Vericairn.Records.Model;
using Vericairn.Records.Types;

namespace Vericairn.Records.Tests;

public class WorkRecordsTests
{
    private const string OrdinaryBody = """{"key":"W-1","kind":"ordinary","title":"Small task","intent":"Exercise work records.","scope":["x"],"exitQuestion":"Done?","inputs":[],"deliverables":["y"]}""";
    private const string MilestoneBody = """{"key":"M9","kind":"milestone","title":"Big step","intent":"Exercise policy.","scope":["x"],"exitQuestion":"Done?","inputs":[],"deliverables":["y"]}""";
    private const string Brief = """{"outcome":"A validated widget.","sources":[],"permittedChanges":["the widget sources"],"prohibitedEffects":["no publication"],"workspace":["example repository, main branch"],"limits":[],"expectedOutputs":["widget.txt"],"reviewCriteria":["output hash verified"],"recovery":"Resume from the last recorded result."}""";
    private const string AttemptBody = """{"startedAt":"2026-10-13T12:00:05Z","workspace":"a clone of example","note":"Build the widget."}""";

    [Fact]
    public void AssignmentAttemptResultChainIsValidRenderedAndAcceptable()
    {
        using var estate = new TestEstate();
        GrantEngineering(estate);
        RecordEnvelope order = estate.Add("work-order", estate.BuilderId, OrdinaryBody);
        RecordEnvelope assignment = estate.Add("assignment", estate.BuilderId, $$"""{"contributor":"{{estate.BuilderId}}","brief":{{Brief}}}""", order.Reference);
        RecordEnvelope attempt = estate.Add("attempt", estate.BuilderId, AttemptBody, assignment.Reference);
        estate.WriteFile("docs/widget.txt", "widget\n");
        string hash = ContentHash.Sha256File(Path.Combine(estate.Root, "docs", "widget.txt"));
        RecordEnvelope result = estate.Add("result", estate.BuilderId, $$"""{"outcome":"completed","summary":"Built the widget.","outputs":[{"name":"widget.txt","hash":"{{hash}}","bytes":7,"location":"docs/widget.txt"}],"evidence":[],"checks":[{"name":"build","outcome":"passed","summary":"Built cleanly."}],"limitations":[],"unresolvedEffects":[],"nextStep":"none"}""", attempt.Reference);
        EstateModel model = estate.Load();
        Assert.False(model.Findings.HasErrors, string.Join("\n", model.Findings));
        WorkView.AssignmentItem item = Assert.Single(WorkView.AssignmentItems(model));
        Assert.Equal("W-1", item.Key);
        Assert.Equal(1, item.Attempts);
        Assert.Equal("completed", item.Outcome);
        Assert.Equal("awaiting review", item.State);
        string brief = WorkView.RenderBrief(model, order);
        Assert.Contains("# Brief: W-1 Small task", brief, StringComparison.Ordinal);
        Assert.Contains("A validated widget.", brief, StringComparison.Ordinal);
        Assert.Contains("next step: none", brief, StringComparison.Ordinal);
        Assert.Contains("Assignments:", EstateStatus.Render(model, EstateChecks.Run(model)), StringComparison.Ordinal);
        Assert.Contains("\"assignments\"", EstateStatus.RenderJson(model, EstateChecks.Run(model)), StringComparison.Ordinal);

        estate.Add("acceptance", estate.BuilderId, """{"statement":"Accepted the exact result.","basis":[],"self":true}""", result.Reference);
        model = estate.Load();
        Assert.False(model.Findings.HasErrors, string.Join("\n", model.Findings));
        Assert.Equal("accepted", Assert.Single(WorkView.AssignmentItems(model)).State);
        Assert.Contains("accepted", WorkView.RenderObserve(model, order), StringComparison.Ordinal);
    }

    [Fact]
    public void AttemptsAndResultsAreAuthoredByTheContributor()
    {
        using var estate = new TestEstate();
        RecordEnvelope order = estate.Add("work-order", estate.BuilderId, OrdinaryBody);
        RecordEnvelope assignment = estate.Add("assignment", estate.OperatorId, $$"""{"contributor":"{{estate.BuilderId}}","brief":{{Brief}}}""", order.Reference);
        estate.Add("attempt", estate.OperatorId, AttemptBody, assignment.Reference);
        Assert.Contains(estate.Load().Findings, f => f.Code == "attempt.contributor");

        RecordEnvelope attempt = estate.Add("attempt", estate.BuilderId, AttemptBody, assignment.Reference);
        estate.Add("result", estate.OperatorId, """{"outcome":"completed","summary":"Someone else's claim.","outputs":[],"evidence":[],"checks":[],"limitations":[],"unresolvedEffects":[],"nextStep":"none"}""", attempt.Reference);
        Assert.Contains(estate.Load().Findings, f => f.Code == "result.contributor");
    }

    [Fact]
    public void TwoCurrentResultsForOneAttemptConflictUntilOneSupersedesTheOther()
    {
        using var estate = new TestEstate();
        RecordEnvelope attempt = StartAttempt(estate, OrdinaryBody);
        const string body = """{"outcome":"partial","summary":"Half done.","outputs":[],"evidence":[],"checks":[],"limitations":["Tests not run."],"unresolvedEffects":[],"nextStep":"Run the tests."}""";
        RecordEnvelope first = estate.Add("result", estate.BuilderId, body, attempt.Reference);
        estate.Add("result", estate.BuilderId, body, attempt.Reference);
        Assert.Contains(estate.Load().Findings, f => f.Code == "result.duplicate");

        using var corrected = new TestEstate();
        RecordEnvelope attempt2 = StartAttempt(corrected, OrdinaryBody);
        RecordEnvelope earlier = corrected.Add("result", corrected.BuilderId, body, attempt2.Reference);
        corrected.Add("result", corrected.BuilderId, body.Replace("Half done.", "Half done, corrected.", StringComparison.Ordinal), attempt2.Reference, supersedes: earlier.Id);
        EstateModel model = corrected.Load();
        Assert.False(model.Findings.HasErrors, string.Join("\n", model.Findings));
        Assert.NotEqual(first.Id, Assert.Single(WorkView.AssignmentItems(model)).LatestResultId);
    }

    [Fact]
    public void OutputDriftIsInformationNotAnError()
    {
        using var estate = new TestEstate();
        RecordEnvelope attempt = StartAttempt(estate, OrdinaryBody);
        estate.WriteFile("docs/widget.txt", "widget\n");
        string wrong = "sha256:" + new string('0', 64);
        estate.Add("result", estate.BuilderId, $$"""{"outcome":"completed","summary":"Built.","outputs":[{"name":"widget.txt","hash":"{{wrong}}","location":"docs/widget.txt"}],"evidence":[],"checks":[],"limitations":[],"unresolvedEffects":[],"nextStep":"none"}""", attempt.Reference);
        EstateModel model = estate.Load();
        Assert.False(model.Findings.HasErrors, string.Join("\n", model.Findings));
        Assert.Contains(model.Findings, f => f.Code == "result.output-changed" && f.Severity == Severity.Info);
    }

    [Fact]
    public void CompletedWithAFailedCheckIsAWarningNotAnError()
    {
        using var estate = new TestEstate();
        RecordEnvelope attempt = StartAttempt(estate, OrdinaryBody);
        estate.Add("result", estate.BuilderId, """{"outcome":"completed","summary":"Built.","outputs":[],"evidence":[],"checks":[{"name":"tests","outcome":"failed","summary":"One test fails."}],"limitations":[],"unresolvedEffects":[],"nextStep":"none"}""", attempt.Reference);
        EstateModel model = estate.Load();
        Assert.False(model.Findings.HasErrors, string.Join("\n", model.Findings));
        Assert.Contains(model.Findings, f => f.Code == "result.checks" && f.Severity == Severity.Warning);
    }

    [Fact]
    public void ResultsTowardMilestonesAreAcceptedByTheOperatorOnly()
    {
        using var estate = new TestEstate();
        GrantEngineering(estate);
        RecordEnvelope attempt = StartAttempt(estate, MilestoneBody);
        RecordEnvelope result = estate.Add("result", estate.BuilderId, """{"outcome":"completed","summary":"Done.","outputs":[],"evidence":[],"checks":[],"limitations":[],"unresolvedEffects":[],"nextStep":"none"}""", attempt.Reference);
        estate.Add("acceptance", estate.BuilderId, """{"statement":"I accept my own milestone result.","basis":[],"self":true}""", result.Reference);
        Assert.Contains(estate.Load().Findings, f => f.Code == "acceptance.policy");

        using var accepted = new TestEstate();
        RecordEnvelope attempt2 = StartAttempt(accepted, MilestoneBody);
        RecordEnvelope result2 = accepted.Add("result", accepted.BuilderId, """{"outcome":"completed","summary":"Done.","outputs":[],"evidence":[],"checks":[],"limitations":[],"unresolvedEffects":[],"nextStep":"none"}""", attempt2.Reference);
        accepted.Add("acceptance", accepted.OperatorId, """{"statement":"Accepted.","basis":[],"self":false}""", result2.Reference);
        EstateModel model = accepted.Load();
        Assert.False(model.Findings.HasErrors, string.Join("\n", model.Findings));
        Assert.Equal("accepted", Assert.Single(WorkView.AssignmentItems(model)).State);
    }

    [Fact]
    public void AssigningNeedsAuthorityAndAnAdmittedContributor()
    {
        using var estate = new TestEstate();
        RecordEnvelope order = estate.Add("work-order", estate.BuilderId, OrdinaryBody);
        estate.Add("assignment", estate.BuilderId, $$"""{"contributor":"{{estate.BuilderId}}","brief":{{Brief}}}""", order.Reference);
        Assert.Contains(estate.Load().Findings, f => f.Code == "assignment.author");

        estate.Add("assignment", estate.OperatorId, $$"""{"contributor":"{{Identity.Ulid.NewUlid().Value}}","brief":{{Brief}}}""", order.Reference);
        Assert.Contains(estate.Load().Findings, f => f.Code == "assignment.contributor");
    }

    [Fact]
    public void AnAmendmentIsRecordedAndChangesNoStatus()
    {
        using var estate = new TestEstate();
        RecordEnvelope attempt = StartAttempt(estate, OrdinaryBody);
        RecordEnvelope assignment = WorkView.AssignmentOfAttempt(estate.Load(), attempt)!;
        estate.Add("amendment", estate.BuilderId, """{"kind":"scope","rationale":"The widget needs a second part.","proposedChanges":["Add the second part to the permitted changes."]}""", assignment.Reference);
        EstateModel model = estate.Load();
        Assert.False(model.Findings.HasErrors, string.Join("\n", model.Findings));
        Assert.Equal("planned", Assert.Single(EstateStatus.WorkItems(model)).Status);
        Assert.Equal("in progress", Assert.Single(WorkView.AssignmentItems(model)).State);
        Assert.Contains("amendment", WorkView.RenderObserve(model, WorkView.FindOrder(model, "W-1")!), StringComparison.Ordinal);
        Assert.DoesNotContain("assignment", RecordTypeRegistry.Reserved);
    }

    private static RecordEnvelope StartAttempt(TestEstate estate, string orderBody)
    {
        RecordEnvelope order = estate.Add("work-order", estate.BuilderId, orderBody);
        RecordEnvelope assignment = estate.Add("assignment", estate.OperatorId, $$"""{"contributor":"{{estate.BuilderId}}","brief":{{Brief}}}""", order.Reference);
        return estate.Add("attempt", estate.BuilderId, AttemptBody, assignment.Reference);
    }

    private static void GrantEngineering(TestEstate estate)
    {
        RecordEnvelope decision = estate.Add("decision", estate.BuilderId, $$"""{"title":"Delegate","statement":"Build and accept ordinary work yourself.","summary":"Delegation.","synthesis":"Standing delegation for ordinary work.","effects":["builder engineers and self-accepts ordinary work"],"affects":[],"decidedBy":"{{estate.OperatorId}}","decidedAt":"2026-10-13T12:00:00Z"}""");
        estate.Add("authority", estate.OperatorId, $$"""{"grantee":"{{estate.BuilderId}}","scope":["local-engineering","self-accept-ordinary-work"],"reserved":["publication"],"conditions":["label self-review"],"basis":"{{decision.Id}}"}""");
    }
}
