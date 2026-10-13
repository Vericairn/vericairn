// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using Vericairn.Records.Estate;
using Vericairn.Records.Model;

namespace Vericairn.Records.Tests;

public class ReconciliationTests
{
    private const string OrderBody = """{"key":"W-1","kind":"ordinary","title":"Task","intent":"Diverge.","scope":["x"],"exitQuestion":"?","inputs":[],"deliverables":["y"]}""";

    [Fact]
    public void DivergentRevisionsAreAReconcileTaskUntilOneIsKept()
    {
        using var estate = new TestEstate();
        RecordEnvelope order = estate.Add("work-order", estate.BuilderId, OrderBody);
        RecordEnvelope fromA = estate.Add("work-order", estate.BuilderId, OrderBody.Replace("Task", "Task, revised on machine A", StringComparison.Ordinal), supersedes: order.Id);
        RecordEnvelope fromB = estate.Add("work-order", estate.OperatorId, OrderBody.Replace("Task", "Task, revised on machine B", StringComparison.Ordinal), supersedes: order.Id);
        EstateModel model = estate.Load();
        Assert.Contains(model.Findings, f => f.Code == "reconcile.successors");
        Assert.NotEmpty(EstateStatus.ReconcileTasks(model.Findings));
        Assert.Contains("Reconcile:", EstateStatus.Render(model, EstateChecks.Run(model)), StringComparison.Ordinal);
        Assert.Contains("\"reconcile\"", EstateStatus.RenderJson(model, EstateChecks.Run(model)), StringComparison.Ordinal);

        estate.Add("reconciliation", estate.OperatorId, $$"""{"kept":"{{fromB.Id}}","withdrawn":["{{fromA.Id}}"],"note":"B's wording is kept; A's change is folded into the next revision."}""", new RecordReference("work-order", order.Id, null));
        model = estate.Load();
        Assert.False(model.Findings.HasErrors, string.Join("\n", model.Findings));
        Assert.Empty(EstateStatus.ReconcileTasks(model.Findings));
        Assert.Equal(fromB.Id, model.Latest(order).Id);
        Assert.Equal(fromB.Id, model.Latest(fromA).Id);
        Assert.False(model.IsCurrent(fromA));
        Assert.Equal("W-1", EstateModel.BodyString(Assert.Single(model.Current("work-order")), "key"));
    }

    [Fact]
    public void AReconciliationNamesOnlyDirectSuccessorsAndNeedsAuthority()
    {
        using var estate = new TestEstate();
        RecordEnvelope order = estate.Add("work-order", estate.BuilderId, OrderBody);
        RecordEnvelope successor = estate.Add("work-order", estate.BuilderId, OrderBody.Replace("Task", "Revised", StringComparison.Ordinal), supersedes: order.Id);
        RecordEnvelope unrelated = estate.Add("work-order", estate.BuilderId, OrderBody.Replace("W-1", "W-2", StringComparison.Ordinal));
        estate.Add("reconciliation", estate.OperatorId, $$"""{"kept":"{{successor.Id}}","withdrawn":["{{unrelated.Id}}"],"note":"Wrong."}""", new RecordReference("work-order", order.Id, null));
        Assert.Contains(estate.Load().Findings, f => f.Code == "reconciliation.successor");

        estate.Add("reconciliation", estate.BuilderId, $$"""{"kept":"{{successor.Id}}","withdrawn":["{{successor.Id}}"],"note":"No authority, and kept is withdrawn."}""", new RecordReference("work-order", order.Id, null));
        EstateModel model = estate.Load();
        Assert.Contains(model.Findings, f => f.Code == "reconciliation.author");
        Assert.Contains(model.Findings, f => f.Code == "reconciliation.kept");
    }
}
