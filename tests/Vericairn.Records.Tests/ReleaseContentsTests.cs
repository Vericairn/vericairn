// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using Vericairn.Records.Estate;
using Vericairn.Records.Model;
using static Vericairn.Records.Tests.ProgressReportTests;

namespace Vericairn.Records.Tests;

/// <summary>The current results of a release's window, classified by their own acceptance or their current work order's.</summary>
public class ReleaseContentsTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SupersededResultsAreOmittedEvenWhenTheyWereAccepted(bool accepted)
    {
        using var estate = new TestEstate(recordFormat: 2);
        (_, RecordEnvelope first) = Worked(estate, "W-1");
        if (accepted)
        {
            estate.Add("acceptance", estate.OperatorId, """{"statement":"Accepted.","basis":[],"self":false}""", first.Reference);
        }

        RecordEnvelope latest = estate.Add("result", estate.BuilderId, first.Body.GetRawText().Replace("Done.", "Corrected.", StringComparison.Ordinal), first.Subject, supersedes: first.Id);
        EstateModel model = estate.Load();
        Assert.False(model.Findings.HasErrors, string.Join("\n", model.Findings));
        var results = ReleaseContents.Results(model, ReleaseContents.Window(model, null, latest.At));
        Assert.Empty(results.Accepted);
        Assert.Equal([latest.Id], results.NotAccepted.Select(r => r.Id));
    }

    [Fact]
    public void AResultSupersededOutsideTheWindowIsNotListedInTheEarlierWindow()
    {
        using var estate = new TestEstate(recordFormat: 2);
        (_, RecordEnvelope first) = Worked(estate, "W-1");
        estate.Add("result", estate.BuilderId, first.Body.GetRawText(), first.Subject, supersedes: first.Id);
        EstateModel model = estate.Load();
        Assert.False(model.Findings.HasErrors, string.Join("\n", model.Findings));
        var results = ReleaseContents.Results(model, ReleaseContents.Window(model, null, first.At));
        Assert.Empty(results.Accepted);
        Assert.Empty(results.NotAccepted);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AcceptanceOfTheCurrentOrderIncludesAnotherPrincipalsResult(bool revised)
    {
        using var estate = new TestEstate(recordFormat: 2);
        (RecordEnvelope order, RecordEnvelope result) = Worked(estate, "W-1");
        RecordEnvelope current = revised
            ? estate.Add("work-order", estate.OperatorId, order.Body.GetRawText().Replace("Work.", "Work completed.", StringComparison.Ordinal), supersedes: order.Id)
            : order;
        RecordEnvelope acceptance = Accept(estate, current);
        EstateModel model = estate.Load();
        Assert.False(model.Findings.HasErrors, string.Join("\n", model.Findings));
        Assert.NotEqual(current.By, result.By);
        Assert.DoesNotContain(model.Current("acceptance"), a => a.Subject?.Id == result.Id);
        var results = ReleaseContents.Results(model, ReleaseContents.Window(model, null, acceptance.At));
        ReleaseContents.Accepted work = Assert.Single(results.Accepted);
        Assert.Equal(result.Id, work.Result.Id);
        Assert.Equal(current.Id, work.Order?.Id);
        Assert.Equal(acceptance.Id, work.Acceptance.Id);
        Assert.Empty(results.NotAccepted);
    }

    [Fact]
    public void AcceptanceOfAnEarlierOrderDoesNotAcceptItsRevisionButAnExactResultAcceptanceStillCounts()
    {
        using var estate = new TestEstate(recordFormat: 2);
        (RecordEnvelope order, RecordEnvelope result) = Worked(estate, "W-1");
        Accept(estate, order);
        RecordEnvelope current = estate.Add("work-order", estate.OperatorId, order.Body.GetRawText().Replace("Work.", "More work.", StringComparison.Ordinal), supersedes: order.Id);
        EstateModel model = estate.Load();
        Assert.False(model.Findings.HasErrors, string.Join("\n", model.Findings));
        var results = ReleaseContents.Results(model, ReleaseContents.Window(model, null, current.At));
        Assert.Empty(results.Accepted);
        Assert.Equal(result.Id, Assert.Single(results.NotAccepted).Id);

        RecordEnvelope acceptance = estate.Add("acceptance", estate.OperatorId, """{"statement":"Accepted this result.","basis":[],"self":false}""", result.Reference);
        model = estate.Load();
        Assert.False(model.Findings.HasErrors, string.Join("\n", model.Findings));
        results = ReleaseContents.Results(model, ReleaseContents.Window(model, null, acceptance.At));
        Assert.Equal(acceptance.Id, Assert.Single(results.Accepted).Acceptance.Id);
        Assert.Empty(results.NotAccepted);
    }
}
