// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using Vericairn.Records.Estate;
using Vericairn.Records.Model;

namespace Vericairn.Records.Tests;

public class SupersededJudgmentTests
{
    private const string Order = """{"key":"FIX-05","kind":"ordinary","title":"Follow-up fixes","intent":"Exercise reconsideration.","scope":["fixes"],"exitQuestion":"Fixed?","inputs":[],"deliverables":["tested fixes"]}""";

    [Theory]
    [InlineData(1, "acceptance")]
    [InlineData(2, "acceptance")]
    [InlineData(1, "review")]
    [InlineData(2, "review")]
    public void Fix05ReconsiderationStopsFlaggingTheSupersededJudgment(long format, string type)
    {
        using var estate = new TestEstate(format);
        RecordEnvelope earlier = estate.Add("work-order", estate.BuilderId, Order);
        RecordEnvelope original = estate.Add(type, estate.OperatorId, Body(type), earlier.Reference);
        RecordEnvelope current = estate.Add("work-order", estate.BuilderId, Order.Replace("Follow-up fixes", "Follow-up fixes, revised", StringComparison.Ordinal), supersedes: earlier.Id);
        EstateModel model = estate.Load();
        Assert.False(model.Findings.HasErrors, string.Join('\n', model.Findings));
        AssertStale(model, original, type);

        // Superseding a judgment does not by itself make its replacement current with the subject.
        RecordEnvelope stillStale = estate.Add(type, estate.OperatorId, Body(type), earlier.Reference, supersedes: original.Id);
        model = estate.Load();
        Assert.False(model.Findings.HasErrors, string.Join('\n', model.Findings));
        Assert.DoesNotContain(model.Findings, f => f.Location == original.ExpectedPath && IsStale(f));
        AssertStale(model, stillStale, type);

        // FIX-05's replacement acceptance pins the current work-order revision, answering the earlier one.
        RecordEnvelope replacement = estate.Add(type, estate.OperatorId, Body(type), current.Reference, supersedes: stillStale.Id);
        model = estate.Load();
        Assert.False(model.Findings.HasErrors, string.Join('\n', model.Findings));
        Assert.DoesNotContain(model.Findings, IsStale);
        Assert.True(model.IsCurrent(replacement));
        Assert.False(model.IsCurrent(original));
        Assert.False(model.IsCurrent(stillStale));
        if (type == "acceptance")
        {
            Assert.Equal("accepted", Assert.Single(EstateStatus.WorkItems(model)).Status);
        }
    }

    [Theory]
    [InlineData("acceptance")]
    [InlineData("review")]
    public void SupersessionDoesNotHideAnInvalidHistoricalSubjectHash(string type)
    {
        using var estate = new TestEstate(2);
        RecordEnvelope earlier = estate.Add("work-order", estate.BuilderId, Order);
        RecordEnvelope invalid = estate.Add(type, estate.OperatorId, Body(type), new RecordReference("work-order", earlier.Id, "sha256:" + new string('f', 64)));
        RecordEnvelope current = estate.Add("work-order", estate.BuilderId, Order.Replace("Follow-up fixes", "Follow-up fixes, revised", StringComparison.Ordinal), supersedes: earlier.Id);
        estate.Add(type, estate.OperatorId, Body(type), current.Reference, supersedes: invalid.Id);

        EstateModel model = estate.Load();
        Assert.Contains(model.Findings, f => f.Location == invalid.ExpectedPath && f.Code == "record.subject-hash" && f.Severity == Severity.Error);
    }

    private static string Body(string type) => type == "acceptance"
        ? """{"statement":"Accepted this revision.","basis":[],"self":false}"""
        : """{"independence":"operator","outcome":"pass","summary":"Reviewed this revision.","findings":[]}""";

    private static bool IsStale(Finding finding) => finding.Code is "record.subject-stale" or "acceptance.unresolved";

    private static void AssertStale(EstateModel model, RecordEnvelope judgment, string type)
    {
        Assert.Contains(model.Findings, f => f.Location == judgment.ExpectedPath && f.Code == "record.subject-stale");
        if (type == "acceptance")
        {
            Assert.Contains(model.Findings, f => f.Location == judgment.ExpectedPath && f.Code == "acceptance.unresolved");
        }
    }
}
