// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jayson Furr

using Vericairn.Records.Estate;
using Vericairn.Records.Model;

namespace Vericairn.Records.Tests;

public class EstateLoaderTests
{
    private const string WorkOrderBody = """{"key":"M0","kind":"milestone","title":"Estate online","intent":"Prove the format.","scope":["records"],"exitQuestion":"Does a fresh clone validate?","inputs":[],"deliverables":["records"]}""";
    private const string OrdinaryBody = """{"key":"W-1","kind":"ordinary","title":"Small task","intent":"Exercise policy.","scope":["x"],"exitQuestion":"Done?","inputs":[],"deliverables":["y"]}""";

    [Fact]
    public void SeededEstateIsValidAndRendersStatus()
    {
        using var estate = new TestEstate();
        EstateModel model = estate.Load();
        Assert.False(model.Findings.HasErrors, string.Join("\n", model.Findings));
        Assert.NotNull(model.CurrentEstate);
        Assert.True(model.HasRole(estate.OperatorId, "operator"));
        Assert.True(model.HasRole(estate.BuilderId, "builder"));
        string status = EstateStatus.Render(model, EstateChecks.Run(model));
        Assert.Contains("Estate: Test Estate (test-estate)", status, StringComparison.Ordinal);
        Assert.Contains("source Vericairn/vericairn@0123456789ab", status, StringComparison.Ordinal);
    }

    [Fact]
    public void WorkOrderLifecycleDerivesStatus()
    {
        using var estate = new TestEstate();
        RecordEnvelope order = estate.Add("work-order", estate.BuilderId, WorkOrderBody);
        estate.Add("work-status", estate.BuilderId, """{"status":"active","note":"Started."}""", new RecordReference("work-order", order.Id, null));
        RecordEnvelope review = estate.Add("review", estate.BuilderId, """{"independence":"self","outcome":"pass","summary":"Looked at it.","findings":[]}""", order.Reference);
        EstateModel model = estate.Load();
        Assert.False(model.Findings.HasErrors, string.Join("\n", model.Findings));
        Assert.Equal("active", EstateStatus.WorkItems(model).Single().Status);

        estate.Add("acceptance", estate.OperatorId, $$"""{"statement":"Accepted.","basis":["{{review.Id}}"],"self":false}""", order.Reference);
        model = estate.Load();
        Assert.False(model.Findings.HasErrors, string.Join("\n", model.Findings));
        Assert.Equal("accepted", EstateStatus.WorkItems(model).Single().Status);
    }

    [Fact]
    public void MilestonesCannotBeAcceptedByTheBuilder()
    {
        using var estate = new TestEstate();
        RecordEnvelope order = estate.Add("work-order", estate.BuilderId, WorkOrderBody);
        estate.Add("acceptance", estate.BuilderId, """{"statement":"I accept my own milestone.","basis":[],"self":true}""", order.Reference);
        EstateModel model = estate.Load();
        Assert.Contains(model.Findings, f => f.Code == "acceptance.policy");
    }

    [Fact]
    public void SelfAcceptanceOfOrdinaryWorkNeedsAuthority()
    {
        using var estate = new TestEstate();
        RecordEnvelope order = estate.Add("work-order", estate.BuilderId, OrdinaryBody);
        estate.Add("acceptance", estate.BuilderId, """{"statement":"Done.","basis":[],"self":true}""", order.Reference);
        Assert.Contains(estate.Load().Findings, f => f.Code == "acceptance.policy");

        RecordEnvelope decision = estate.Add("decision", estate.BuilderId, $$"""{"title":"Delegate","statement":"Accept ordinary work yourself.","summary":"Delegation.","effects":["builder self-accepts ordinary work"],"affects":[],"decidedBy":"{{estate.OperatorId}}","decidedAt":"2026-10-13T12:00:00Z"}""");
        estate.Add("authority", estate.OperatorId, $$"""{"grantee":"{{estate.BuilderId}}","scope":["local-engineering","self-accept-ordinary-work"],"reserved":["publication"],"conditions":["label self-review"],"basis":"{{decision.Id}}"}""");
        EstateModel model = estate.Load();
        Assert.False(model.Findings.HasErrors, string.Join("\n", model.Findings));
        Assert.Equal("accepted", EstateStatus.WorkItems(model).Single().Status);
    }

    [Fact]
    public void LyingAboutIndependenceIsRejected()
    {
        using var estate = new TestEstate();
        RecordEnvelope order = estate.Add("work-order", estate.BuilderId, WorkOrderBody);
        estate.Add("review", estate.BuilderId, """{"independence":"independent-agent","outcome":"pass","summary":"Totally independent.","findings":[]}""", order.Reference);
        Assert.Contains(estate.Load().Findings, f => f.Code == "review.independence");
    }

    [Fact]
    public void StaleSubjectsAreUnresolvedNotInvalid()
    {
        using var estate = new TestEstate();
        RecordEnvelope order = estate.Add("work-order", estate.BuilderId, WorkOrderBody);
        estate.Add("acceptance", estate.OperatorId, """{"statement":"Accepted.","basis":[],"self":false}""", order.Reference);
        estate.Add("work-order", estate.BuilderId, WorkOrderBody.Replace("Estate online", "Estate online, revised", StringComparison.Ordinal), supersedes: order.Id);
        EstateModel model = estate.Load();
        Assert.False(model.Findings.HasErrors, string.Join("\n", model.Findings));
        Assert.Contains(model.Findings, f => f.Code == "record.subject-stale");
        Assert.Contains(model.Findings, f => f.Code == "acceptance.unresolved");
        Assert.Equal("accepted (earlier revision)", EstateStatus.WorkItems(model).Single().Status);
    }

    [Fact]
    public void DuplicateKeysAndTwoSuccessorsAreErrors()
    {
        using var estate = new TestEstate();
        estate.Add("work-order", estate.BuilderId, WorkOrderBody);
        estate.Add("work-order", estate.BuilderId, WorkOrderBody);
        RecordEnvelope ordinary = estate.Add("work-order", estate.BuilderId, OrdinaryBody);
        estate.Add("work-order", estate.BuilderId, OrdinaryBody.Replace("Small task", "Revised once", StringComparison.Ordinal), supersedes: ordinary.Id);
        estate.Add("work-order", estate.BuilderId, OrdinaryBody.Replace("Small task", "Revised twice", StringComparison.Ordinal), supersedes: ordinary.Id);
        EstateModel model = estate.Load();
        Assert.Contains(model.Findings, f => f.Code == "work.duplicate-key");
        Assert.Contains(model.Findings, f => f.Code == "reconcile.successors");
    }

    [Fact]
    public void DecisionsWantASynthesisAndRenderCleanly()
    {
        using var estate = new TestEstate();
        RecordEnvelope bare = estate.Add("decision", estate.BuilderId, $$"""{"title":"Bare","statement":"Do it.","summary":"Do it.","effects":["x"],"affects":[],"decidedBy":"{{estate.OperatorId}}","decidedAt":"2026-10-13T12:00:00Z"}""");
        Assert.Contains(estate.Load().Findings, f => f.Code == "decision.synthesis" && f.Severity == Severity.Warning);
        estate.Add("decision", estate.BuilderId, $$"""{"title":"Bare","statement":"Do it.","summary":"Do it.","synthesis":"Context, decision, rationale, consequences, limits.","effects":["x"],"affects":[],"decidedBy":"{{estate.OperatorId}}","decidedAt":"2026-10-13T12:00:00Z"}""", supersedes: bare.Id);
        EstateModel model = estate.Load();
        Assert.DoesNotContain(model.Findings, f => f.Code == "decision.synthesis");
        RecordEnvelope? current = DecisionView.Find(model, bare.Id);
        Assert.NotNull(current);
        string markdown = DecisionView.Render(model, current);
        Assert.Contains("## Synthesis", markdown, StringComparison.Ordinal);
        Assert.Contains("> Do it.", markdown, StringComparison.Ordinal);
        Assert.Contains("(revision 2)", markdown, StringComparison.Ordinal);
        Assert.Single(DecisionView.Current(model));
    }

    [Fact]
    public void OnlyTheRootOperatorMayAuthorItself()
    {
        using var estate = new TestEstate();
        string impostor = Records.Identity.Ulid.NewUlid().Value;
        estate.Add("principal", impostor, """{"kind":"human","name":"Impostor","roles":["operator"]}""", id: impostor);
        Assert.Contains(estate.Load().Findings, f => f.Code == "principal.self-authored");
    }
}
