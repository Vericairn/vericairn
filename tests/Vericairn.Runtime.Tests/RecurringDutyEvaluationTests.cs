// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Globalization;
using System.Text.Json.Nodes;
using Vericairn.Records.Estate;
using Vericairn.Records.Model;
using Vericairn.Records.Tests;
using Vericairn.Records.Types;
using Vericairn.Runtime.Policies;

namespace Vericairn.Runtime.Tests;

public class RecurringDutyEvaluationTests
{
    internal const string FirstDue = "2026-10-13T13:00:00Z";
    internal static DateTimeOffset At(string value) => DutyRecords.Time(value)!.Value;
    internal static JsonObject Parameters(JsonObject body) => body["rules"]![0]!["parameters"]!.AsObject();
    internal static PolicyEvaluation Evaluate(PolicyFixture fixture, string? at = null, IEnumerable<PolicyObject>? objects = null, IEnumerable<PolicyRepository>? repositories = null)
    {
        fixture.AssertValid();
        return PolicyEvaluator.Evaluate(new(fixture.Estate.Load(), at ?? RecordParser.FormatTimestamp(fixture.Estate.Clock), PolicyTimeOrigin.Preview,
            repositories, objects, FoundationPolicyTests.Kinds.Implementations), FoundationPolicyTests.Kinds);
    }
    internal static PolicyFinding Number(PolicyEvaluation evaluation, long number) => Assert.Single(evaluation.Findings,
        f => f.Duty?.Occurrence.TryGetProperty("number", out var value) == true && value.GetInt64() == number);
    internal static JsonObject Retired(RecordEnvelope policy)
    {
        JsonObject body = PolicyFixture.Body(policy);
        body["state"] = "retired";
        body["retirement"] = new JsonObject { ["reason"] = "End this requirement.", ["findings"] = "Retain completed and explicitly ended debt." };
        return body;
    }

    [Theory]
    [InlineData("2026-10-13T12:59:59Z", "scheduled", false)]
    [InlineData(FirstDue, "due-within-grace", false)]
    [InlineData("2026-10-16T12:59:59Z", "due-within-grace", false)]
    [InlineData("2026-10-16T13:00:00Z", "none", true)]
    [InlineData("2026-10-16T13:00:01Z", "none", true)]
    public void GraceAndDeadlineUseExactExplicitInstants(string at, string disposition, bool missed)
    {
        using var f = new PolicyFixture();
        JsonObject body = f.PolicyBody(); body["startsAt"] = "2026-10-13T12:30:00Z";
        f.Policy(body);
        PolicyEvaluation evaluation = Evaluate(f, at);
        PolicyFinding finding = Number(evaluation, 0);
        Assert.Equal(disposition, finding.Disposition);
        Assert.Equal(missed, finding.Duty!.Missed);
        Assert.Equal(missed, finding.ReleaseHold);
        Assert.Equal(missed ? "unmet" : "satisfied", finding.Verdict);
        Assert.Equal("2026-10-16T13:00:00Z", finding.Duty.Deadline);
        Assert.Equal(missed ? 1 : 0, evaluation.Holds);
    }

    [Fact]
    public void ZeroGraceAppliesImmediatelyAndCompletionAtTheDeadlineIsOnTime()
    {
        using var f = new PolicyFixture();
        RecordEnvelope policy = f.Policy();
        f.Estate.SetClock(At("2026-10-16T12:59:55Z"));
        RecordEnvelope result = f.Result(f.DutyLink(policy));
        RecordEnvelope review = f.Review(result);
        Assert.Equal("2026-10-16T13:00:00Z", review.At);
        PolicyFinding finding = Number(Evaluate(f), 0);
        Assert.Equal("discharged", finding.Disposition);
        Assert.Equal(review.At, finding.Duty!.CompletedAt);
        Assert.False(finding.Duty.Missed);
        Assert.Equal(0, finding.Duty.LateSeconds);
        using var zero = new PolicyFixture();
        JsonObject body = zero.PolicyBody(); Parameters(body)["graceDays"] = 0;
        zero.Policy(body);
        Assert.True(Number(Evaluate(zero, FirstDue), 0).ReleaseHold);
    }

    [Theory]
    [InlineData("2027-01-31T08:00:00Z", "2027-02-28T08:00:00Z", "2027-03-31T08:00:00Z")]
    [InlineData("2028-01-31T08:00:00Z", "2028-02-29T08:00:00Z", "2028-03-31T08:00:00Z")]
    public void CalendarMonthsKeepTheirOriginalAnchor(string anchor, string february, string march)
    {
        using var f = new PolicyFixture();
        JsonObject body = f.PolicyBody(); Parameters(body)["schedule"]!["firstDueAt"] = anchor;
        f.Policy(body);
        PolicyEvaluation result = Evaluate(f, march);
        Assert.Equal(february, Number(result, 1).Duty!.DueAt);
        Assert.Equal(march, Number(result, 2).Duty!.DueAt);
        Assert.Equal(anchor, Number(result, 0).Duty!.DueAt);
    }

    [Fact]
    public void EveryMissedDayRemainsVisibleAndTheBoundCannotReturnATruncatedPass()
    {
        using var f = new PolicyFixture();
        JsonObject body = f.PolicyBody(); Parameters(body)["schedule"]!["unit"] = "days"; Parameters(body)["graceDays"] = 0;
        f.Policy(body);
        PolicyEvaluation result = Evaluate(f, RecordParser.FormatTimestamp(At(FirstDue).AddDays(399)));
        Assert.Equal(401, result.Findings.Length);
        Assert.Equal(400, result.Holds);
        Assert.Equal(400, Assert.Single(result.Rules).Unmet);
        Assert.Equal(1, Assert.Single(result.Rules).Subjects);
        Assert.Equal(Enumerable.Range(0, 401).Select(i => (long)i), result.Findings.Select(x => x.Duty!.Occurrence.GetProperty("number").GetInt64()));
        Assert.Equal("scheduled", Number(result, 400).Disposition);
        PolicyFinding unavailable = Assert.Single(Evaluate(f, RecordParser.FormatTimestamp(At(FirstDue).AddDays(10000))).Findings);
        Assert.Equal("unavailable", unavailable.Verdict);
        Assert.Contains("complete debt", unavailable.Actual, StringComparison.Ordinal);
        Assert.True(unavailable.ReleaseHold);
    }

    [Fact]
    public void CalendarOverflowMakesTheWholeRuleUnavailable()
    {
        using var f = new PolicyFixture();
        JsonObject body = f.PolicyBody(); Parameters(body)["schedule"]!["firstDueAt"] = "9999-12-31T13:00:00Z"; Parameters(body)["graceDays"] = 0;
        f.Policy(body);
        PolicyFinding finding = Assert.Single(Evaluate(f, "9999-12-31T13:00:00Z").Findings);
        Assert.Equal("unavailable", finding.Verdict);
        Assert.Contains("overflows", finding.Actual, StringComparison.Ordinal);
    }

    [Fact]
    public void CatchUpNamesEachOccurrenceAndNeverPrepaysOrForgivesAnother()
    {
        using var f = new PolicyFixture();
        RecordEnvelope policy = f.Policy();
        f.Estate.SetClock(At("2026-12-20T14:00:00Z"));
        RecordEnvelope result = f.Result(f.DutyLink(policy, 0));
        JsonObject body = PolicyFixture.Body(result); body["duties"]!.AsArray().Add(f.DutyLink(policy, 1));
        RecordEnvelope revised = f.Add("result", body, builder: true, subject: result.Subject, supersedes: result.Id);
        f.Review(revised);
        PolicyEvaluation evaluation = Evaluate(f);
        Assert.Equal("discharged-late", Number(evaluation, 0).Disposition);
        Assert.Equal("discharged-late", Number(evaluation, 1).Disposition);
        Assert.True(Number(evaluation, 2).ReleaseHold);
        Assert.Equal("2027-01-13T13:00:00Z", Number(evaluation, 3).Duty!.DueAt);
        Assert.Equal(1, evaluation.Holds);
        Assert.True(Number(evaluation, 0).Duty!.LateSeconds > Number(evaluation, 1).Duty!.LateSeconds);
    }

    [Fact]
    public void EditorialRevisionsAndOwnerTransfersKeepTheOriginAndCurrentResponsibility()
    {
        using var f = new PolicyFixture();
        RecordEnvelope policy = f.Policy();
        f.Estate.SetClock(At("2026-10-14T14:00:00Z"));
        JsonObject body = PolicyFixture.Body(policy); body["title"] = "Clarified maintenance"; body["owner"] = f.Estate.BuilderId;
        RecordEnvelope current = f.Policy(body, policy);
        PolicyFinding finding = Number(Evaluate(f), 0);
        Assert.Equal(policy.Reference, finding.Duty!.Origin);
        Assert.Equal(current.Reference, finding.Policy);
        Assert.Equal(f.Estate.BuilderId, finding.Owner);
    }

    [Fact]
    public void ScheduleChangeIncludesTheOldBoundaryAndCarriesItsOldRequirements()
    {
        using var f = new PolicyFixture();
        RecordEnvelope first = f.Policy();
        f.Estate.SetClock(At("2026-11-13T12:59:58Z"));
        JsonObject body = PolicyFixture.Body(first);
        Parameters(body)["schedule"]!["firstDueAt"] = "2026-11-20T13:00:00Z";
        Parameters(body)["graceDays"] = 0;
        Parameters(body)["subject"] = PolicyFixture.Ref(f.OperatorDevice, false);
        RecordEnvelope next = f.Policy(body, first);
        Assert.Equal("2026-11-13T13:00:00Z", next.At);
        PolicyEvaluation result = Evaluate(f);
        Assert.Equal(3, result.Findings.Length);
        PolicyFinding old = Assert.Single(result.Findings, x => x.Duty!.Origin == first.Reference && x.Duty.Occurrence.GetProperty("number").GetInt64() == 1);
        Assert.Equal("due-within-grace", old.Disposition);
        Assert.Equal("2026-11-16T13:00:00Z", old.Duty!.Deadline);
        Assert.Equal(f.Estate.Estate.Id, old.Duty.SubjectIdentity);
        PolicyFinding upcoming = Assert.Single(result.Findings, x => x.Duty!.Origin == next.Reference);
        Assert.Equal(f.OperatorDevice.Id, upcoming.Duty!.SubjectIdentity);
        Assert.Equal("2026-11-20T13:00:00Z", upcoming.Duty.DueAt);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EndedDebtStaysUnmetAndNeverBecomesCompletion(bool retire)
    {
        using var f = new PolicyFixture();
        RecordEnvelope policy = f.Policy(); f.Estate.SetClock(At("2026-10-14T14:00:00Z"));
        JsonObject body = retire ? Retired(policy) : PolicyFixture.Body(policy);
        body["dutyDispositions"] = new JsonArray(f.Disposition(policy));
        f.Policy(body, policy);
        PolicyFinding ended = Number(Evaluate(f), 0);
        Assert.Equal("ended-unmet", ended.Disposition);
        Assert.Equal("unmet", ended.Verdict);
        Assert.False(ended.Duty!.Completed);
        Assert.NotNull(ended.Duty.EndedReason);
        Assert.False(ended.ReleaseHold);
        Assert.Equal(!retire, ended.Enforced);
    }

    [Fact]
    public void ARemovedDutyKeepsItsOwnHistoricalScopeAndCannotQuietItsReplacement()
    {
        using var f = new PolicyFixture();
        RecordEnvelope policy = f.Policy(); f.Estate.SetClock(At("2026-10-14T14:00:00Z"));
        JsonObject body = PolicyFixture.Body(policy);
        body["appliesTo"] = new JsonObject { ["kind"] = "repository", ["repository"] = f.Estate.Estate.Id, ["selectors"] = new JsonArray("docs/**") };
        body["rules"] = new JsonArray(new JsonObject { ["key"] = "files", ["kind"] = FoundationPolicyKinds.RevisionLine, ["version"] = 1,
            ["parameters"] = new JsonObject { ["selectors"] = new JsonArray("docs/**"), ["style"] = "estate-design/1" } });
        body["dutyDispositions"] = new JsonArray(f.Disposition(policy)); f.Policy(body, policy);
        PolicyEvaluation result = Evaluate(f, repositories: [new(f.Estate.Estate.Id, "working-tree", PolicyBasis.RepositoryContract, true, [PolicyBasisTests.File("docs/test.md", "# Title\nMissing revision.")])]);
        Assert.Equal(0, result.Errors); Assert.True(result.Complete);
        PolicyFinding ended = Assert.Single(result.Findings, x => x.Duty is not null);
        Assert.Equal("ended-unmet", ended.Disposition); Assert.False(ended.Enforced);
        Assert.True(Assert.Single(result.Findings, x => x.Rule == "files").ReleaseHold);
    }

    [Fact]
    public void OccurrenceExceptionsRetainDebtAndDoNotTouchAnotherOccurrenceOrError()
    {
        using var f = new PolicyFixture();
        RecordEnvelope policy = f.Policy(); f.Estate.SetClock(At("2026-11-20T14:00:00Z"));
        JsonObject body = f.ExceptionBody(policy);
        RecordEnvelope exception = f.Add("policy-exception", body, subject: policy.Reference);
        PolicyEvaluation evaluation = Evaluate(f);
        PolicyFinding first = Number(evaluation, 0);
        Assert.Equal("excepted", first.Disposition);
        Assert.Equal("unmet", first.Verdict);
        Assert.True(first.Duty!.Missed);
        Assert.False(first.Duty.Completed);
        Assert.Equal(exception.Reference, Assert.Single(first.Exceptions));
        Assert.True(Number(evaluation, 1).ReleaseHold);
        PolicyEvaluation expired = Evaluate(f, body["expiresAt"]!.GetValue<string>());
        Assert.True(Number(expired, 0).ReleaseHold);
        EstateModel model = f.Estate.Load(); model.Findings.Add(new(Severity.Error, "test.builtin", "records/test", "A built-in error remains."));
        PolicyEvaluation invalid = PolicyEvaluator.Evaluate(new(model, RecordParser.FormatTimestamp(f.Estate.Clock), PolicyTimeOrigin.Preview, implementations: FoundationPolicyTests.Kinds.Implementations), FoundationPolicyTests.Kinds);
        Assert.Equal(1, invalid.Errors);
        Assert.All(invalid.Findings, x => { Assert.Equal("unavailable", x.Verdict); Assert.Empty(x.Exceptions); });
    }

    [Fact]
    public void CompleteDutyResultsAndCountsAreIndependentOfCultureAndRecordOrder()
    {
        using var f = new PolicyFixture();
        RecordEnvelope policy = f.Policy(); f.Estate.SetClock(At("2026-12-20T14:00:00Z"));
        f.Review(f.Result(f.DutyLink(policy, 1)));
        EstateModel model = f.Estate.Load();
        string expected = Evaluate(f).Canonical();
        CultureInfo prior = CultureInfo.CurrentCulture, priorUi = CultureInfo.CurrentUICulture;
        try
        {
            foreach (string name in new[] { "en-US", "tr-TR", "ar-SA", "fr-FR" })
            {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(name); CultureInfo.CurrentUICulture = CultureInfo.CurrentCulture;
                var shuffled = new EstateModel(string.Empty, model.Records.Reverse().ToArray(), model.ById, model.SupersededBy, model.Findings) { Manifest = model.Manifest };
                PolicyEvaluation actual = PolicyEvaluator.Evaluate(new(shuffled, RecordParser.FormatTimestamp(f.Estate.Clock), PolicyTimeOrigin.Preview, implementations: FoundationPolicyTests.Kinds.Implementations), FoundationPolicyTests.Kinds);
                Assert.Equal(expected, actual.Canonical());
            }
        }
        finally { CultureInfo.CurrentCulture = prior; CultureInfo.CurrentUICulture = priorUi; }
    }
}
