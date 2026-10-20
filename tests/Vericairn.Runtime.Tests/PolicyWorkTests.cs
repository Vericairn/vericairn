// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using Vericairn.Records.Estate;
using Vericairn.Records.Model;
using Vericairn.Runtime.Policies;
using static Vericairn.Runtime.Tests.FoundationPolicyTests;

namespace Vericairn.Runtime.Tests;

public class PolicyWorkTests
{
    private static readonly string[] AcceptedCase = ["accepted-still-open"];
    private static readonly string[] PublishedCase = ["published-plan-still-open"];
    private static readonly DateTimeOffset EvaluationTime = new(2026, 1, 4, 0, 0, 0, TimeSpan.Zero);

    // Synthetic records preserve the result-bearing assignment counts in these estate work orders.
    // One accepted result settles the order, including results pinned through an earlier revision.
    public static TheoryData<string, int> EstateShapes => new()
    {
        { "FIX-13", 2 }, { "FIX-28", 3 }, { "POLICY-03", 2 }, { "READ-02", 2 }, { "READ-03", 2 },
    };

    [Theory]
    [MemberData(nameof(EstateShapes))]
    public void ParallelResultsAndAcceptanceAreResolvedPerAssignment(string key, int count)
    {
        RecordEnvelope[] records = ParallelWork(key, count);
        foreach (IEnumerable<RecordEnvelope> ordered in new[] { records, records.Reverse() })
        {
            EstateModel model = PolicyBasisTests.Model(ordered);
            RecordEnvelope order = model.ById["current-order"];
            Assert.Equal(count, PolicyWork.Results(model, order, EvaluationTime).Count);
            Assert.Equal(records.Where(r => r.Type == "result").Select(r => r.Reference).OrderBy(r => r.Id, StringComparer.Ordinal),
                PolicyWork.Results(model, order, EvaluationTime).Select(r => r.Reference).OrderBy(r => r.Id, StringComparer.Ordinal));
            Assert.Null(PolicyWork.Acceptance(model, order, EvaluationTime));
            RecordEnvelope acceptance = Accept(records.Last(r => r.Type == "result"));
            EstateModel accepted = PolicyBasisTests.Model([.. ordered, acceptance]);
            Assert.Equal(acceptance.Reference, PolicyWork.Acceptance(accepted, order, EvaluationTime)?.Reference);
        }
    }

    [Theory]
    [MemberData(nameof(EstateShapes))]
    public void OneAcceptedParallelResultSettlesPlacementAgeAndAssignmentStart(string key, int count)
    {
        RecordEnvelope[] parallel = ParallelWork(key, count);
        RecordEnvelope order = parallel.Single(r => r.Id == "current-order");
        RecordEnvelope[] records = [.. parallel, Accept(parallel.Last(r => r.Type == "result")),
            R("assignment", "unstarted", new { contributor = "author" }, subject: order.Reference)];
        PolicyEvaluation[] evaluations =
        [
            Run(FoundationPolicyKinds.Placement, new { backlogs = Array.Empty<object>() }, records),
            Run(FoundationPolicyKinds.Staleness, new { @class = "planned-without-release", maxAgeDays = 0 }, records),
            Run(FoundationPolicyKinds.Staleness, new { @class = "assignment-without-attempt", maxAgeDays = 0 }, records),
        ];
        foreach (PolicyEvaluation evaluation in evaluations)
        {
            Assert.True(evaluation.Complete);
            Assert.Equal(0, Assert.Single(evaluation.Rules).Subjects);
            Assert.Equal(0, evaluation.Holds);
        }
        RecordEnvelope status = R("work-status", "open", new { status = "submitted" }, subject: order.Reference);
        PolicyEvaluation consistency = Run(FoundationPolicyKinds.Consistency, new { cases = AcceptedCase }, [.. records, status]);
        Assert.True(consistency.Complete);
        Assert.Equal("unmet", Assert.Single(consistency.Findings).Verdict);
        Assert.Equal("acceptance", Assert.Single(Assert.Single(consistency.Findings).Evidence).Id);
    }

    [Theory]
    [MemberData(nameof(EstateShapes))]
    public void ReviewWaitExcludesEveryResultOfAnAcceptedOrder(string key, int count)
    {
        RecordEnvelope[] records = ParallelWork(key, count);
        var parameters = new { @class = "result-awaiting-review", maxAgeDays = 0, reviewRequirement = "independent" };
        PolicyEvaluation waiting = Run(FoundationPolicyKinds.Staleness, parameters, records);
        Assert.True(waiting.Complete);
        Assert.Equal(count, waiting.Findings.Length);
        Assert.All(waiting.Findings, f => Assert.Equal("unmet", f.Verdict));
        Assert.Equal(records.Where(r => r.Type == "result").Select(r => r.Reference).OrderBy(r => r.Id, StringComparer.Ordinal),
            waiting.Findings.Select(f => f.Anchor).OrderBy(r => r!.Id, StringComparer.Ordinal));

        EstateModel model = PolicyBasisTests.Model(records);
        foreach (RecordEnvelope accepted in records.Where(r => r.Id == "current-order" ||
            r.Type == "result" && WorkView.OrderOfResult(model, r)?.Id == "current-order"))
        {
            PolicyEvaluation settled = Run(FoundationPolicyKinds.Staleness, parameters, [.. records, Accept(accepted)]);
            Assert.True(settled.Complete);
            Assert.Equal(0, Assert.Single(settled.Rules).Subjects);
            Assert.DoesNotContain(settled.Findings, f => f.Target.Kind == "record");
        }
        PolicyEvaluation earlierRevision = Run(FoundationPolicyKinds.Staleness, parameters,
            [.. records, Accept(records.First(r => r.Type == "result"))]);
        Assert.True(earlierRevision.Complete);
        Assert.Equal(count, Assert.Single(earlierRevision.Rules).Subjects);
    }

    [Theory]
    [InlineData("attempt")]
    [InlineData("result")]
    public void AConflictInsideOneAssignmentStillMakesDependentRulesUnavailable(string conflictType)
    {
        RecordEnvelope[] parallel = ParallelWork("FIX-28", 3);
        RecordEnvelope original = parallel.First(r => r.Type == conflictType);
        RecordEnvelope conflict = R(conflictType, "conflict", new { }, original.At, original.Subject);
        RecordEnvelope[] records = [.. parallel, conflict, Accept(parallel.Last(r => r.Type == "result"))];
        EstateModel model = PolicyBasisTests.Model(records);
        RecordEnvelope order = model.ById["current-order"];
        Assert.Throws<PolicyInputException>(() => PolicyWork.Results(model, order, EvaluationTime));
        Assert.Throws<PolicyInputException>(() => PolicyWork.Acceptance(model, order, EvaluationTime));
        PolicyEvaluation[] evaluations =
        [
            Run(FoundationPolicyKinds.Placement, new { backlogs = Array.Empty<object>() }, records),
            Run(FoundationPolicyKinds.Staleness, new { @class = "planned-without-release", maxAgeDays = 0 }, records),
            Run(FoundationPolicyKinds.Staleness, new { @class = "assignment-without-attempt", maxAgeDays = 0 }, records),
            Run(FoundationPolicyKinds.Staleness, new { @class = "result-awaiting-review", maxAgeDays = 0, reviewRequirement = "any" }, records),
            Run(FoundationPolicyKinds.Consistency, new { cases = AcceptedCase }, records),
        ];
        foreach (PolicyEvaluation evaluation in evaluations)
        {
            Assert.False(evaluation.Complete);
            Assert.Equal("unavailable", Assert.Single(evaluation.Findings).Verdict);
        }
    }

    [Fact]
    public void NewAttemptsAndSupersessionReplaceOnlyTheirOwnAssignmentsResult()
    {
        RecordEnvelope[] records = ParallelWork("READ-03", 2);
        RecordEnvelope first = records.First(r => r.Type == "result");
        RecordEnvelope second = records.Last(r => r.Type == "result");
        RecordEnvelope replacement = R("result", "replacement", new { outcome = "completed" }, "2026-01-01T13:00:00Z", first.Subject, first.Id);
        RecordEnvelope next = R("attempt", "next", new { }, "2026-01-01T14:00:00Z", records.Last(r => r.Type == "assignment").Reference);
        EstateModel model = PolicyBasisTests.Model([.. records, replacement, next, Accept(second)]);
        Assert.Equal(replacement.Reference, Assert.Single(PolicyWork.Results(model, model.ById["current-order"], EvaluationTime)).Reference);
        Assert.Null(PolicyWork.Acceptance(model, model.ById["current-order"], EvaluationTime));
        RecordEnvelope reassigned = R("assignment", "reassigned", new { contributor = "author" }, "2026-01-01T15:00:00Z",
            model.ById["current-order"].Reference, records.First(r => r.Type == "assignment").Id);
        model = PolicyBasisTests.Model([.. model.Records, reassigned]);
        Assert.Empty(PolicyWork.Results(model, model.ById["current-order"], EvaluationTime));
    }

    [Theory]
    [InlineData("accepted-still-open", "2026-01-01T23:59:59Z", false)]
    [InlineData("accepted-still-open", "2026-01-02T00:00:00Z", true)]
    [InlineData("accepted-still-open", "2026-01-02T00:00:01Z", true)]
    [InlineData("published-plan-still-open", "2026-01-01T23:59:59Z", false)]
    [InlineData("published-plan-still-open", "2026-01-02T00:00:00Z", true)]
    [InlineData("published-plan-still-open", "2026-01-02T00:00:01Z", true)]
    public void ConsistencySelectsContradictionsFromTheFirstDeclaration(string item, string eventAt, bool warns)
    {
        RecordEnvelope order = R("work-order", "order", new { key = "REL-01", releasePlan = new { product = "product", version = "1.0.0" } });
        RecordEnvelope status = R("work-status", "open", new { status = "active" }, subject: order.Reference);
        RecordEnvelope fact = item == "accepted-still-open" ? Accept(order, eventAt)
            : R("release", "publication", new { status = "published", version = "1.0.0" }, eventAt, new("product", "product", null));
        RecordEnvelope policy = ConsistencyPolicy(item, "2026-01-02T00:00:00Z");
        foreach (bool revised in new[] { false, true })
        {
            RecordEnvelope[] records = [order, status, fact, policy];
            if (revised) records = [.. records, R("estate-policy", "revised-policy", policy.Body, "2026-01-03T00:00:00Z", supersedes: policy.Id)];
            PolicyEvaluation evaluation = Evaluate(records, "2026-01-04T00:00:00Z");
            Assert.True(evaluation.Complete);
            Assert.Equal(warns ? 1 : 0, Assert.Single(evaluation.Rules).Subjects);
            Assert.Equal(warns ? "unmet" : "satisfied", Assert.Single(evaluation.Findings).Verdict);
            if (warns) Assert.Equal(fact.Reference, Assert.Single(Assert.Single(evaluation.Findings).Evidence));
            else Assert.DoesNotContain(evaluation.Findings, f => f.Target.Kind == "record");
        }
    }

    [Theory]
    [InlineData("accepted-still-open")]
    [InlineData("published-plan-still-open")]
    public void AnOpenStatusRecordedAfterTheBoundaryCanContradictAnEarlierFact(string item)
    {
        RecordEnvelope order = R("work-order", "order", new { key = "REL-01", releasePlan = new { product = "product", version = "1.0.0" } });
        RecordEnvelope fact = item == "accepted-still-open" ? Accept(order)
            : R("release", "publication", new { status = "published", version = "1.0.0" }, subject: new("product", "product", null));
        RecordEnvelope status = R("work-status", "open", new { status = "submitted" }, "2026-01-02T00:00:00Z", order.Reference);
        PolicyEvaluation evaluation = Evaluate([order, fact, status, ConsistencyPolicy(item, status.At)]);
        Assert.True(evaluation.Complete);
        Assert.Equal("unmet", Assert.Single(evaluation.Findings).Verdict);
    }

    private static RecordEnvelope[] ParallelWork(string key, int count)
    {
        RecordEnvelope first = R("work-order", "order", new { key });
        RecordEnvelope current = R("work-order", "current-order", new { key }, "2026-01-01T01:00:00Z", supersedes: first.Id);
        List<RecordEnvelope> records = [first, current];
        for (int i = 0; i < count; i++)
        {
            string suffix = i.ToString(System.Globalization.CultureInfo.InvariantCulture);
            RecordEnvelope assignment = R("assignment", "assignment-" + suffix, new { contributor = "author" },
                "2026-01-01T02:00:00Z", (i == 0 ? first : current).Reference);
            RecordEnvelope attempt = R("attempt", "attempt-" + suffix, new { }, "2026-01-01T03:00:00Z", assignment.Reference);
            RecordEnvelope result = R("result", "result-" + suffix, new { outcome = "completed" }, "2026-01-01T04:00:00Z", attempt.Reference, by: "author");
            records.AddRange([assignment, attempt, result]);
        }
        return [.. records];
    }

    private static RecordEnvelope Accept(RecordEnvelope record, string at = "2026-01-01T12:00:00Z") =>
        R("acceptance", "acceptance", new { }, at, record.Reference);

    private static RecordEnvelope ConsistencyPolicy(string item, string at)
    {
        RecordEnvelope policy = P(FoundationPolicyKinds.Consistency, new { cases = item == "accepted-still-open" ? AcceptedCase : PublishedCase });
        return R(policy.Type, policy.Id, policy.Body, at);
    }
}
