// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text;
using System.Text.Json.Nodes;
using Vericairn.Records.Estate;
using Vericairn.Records.Hashing;
using Vericairn.Records.Model;
using Vericairn.Records.Tests;
using Vericairn.Records.Types;
using Vericairn.Runtime.Policies;
using static Vericairn.Runtime.Tests.RecurringDutyEvaluationTests;

namespace Vericairn.Runtime.Tests;

public class DutyScheduleSourceTests
{
    [Theory]
    [InlineData(0, true)]
    [InlineData(1, true)]
    [InlineData(2, true)]
    [InlineData(0, false)]
    [InlineData(1, false)]
    [InlineData(2, false)]
    public void ReleaseScheduleMatchesAnyProductRevision(int revision, bool matches)
    {
        using var f = new PolicyFixture();
        RecordEnvelope product = Product(f, "example");
        RecordEnvelope revised = f.Add("product", PolicyFixture.Body(product), supersedes: product.Id);
        RecordEnvelope latest = f.Add("product", PolicyFixture.Body(revised), supersedes: revised.Id);
        RecordEnvelope other = Product(f, "other");
        RecordEnvelope otherRevised = f.Add("product", PolicyFixture.Body(other), supersedes: other.Id);
        RecordEnvelope otherLatest = f.Add("product", PolicyFixture.Body(otherRevised), supersedes: otherRevised.Id);
        RecordEnvelope[] subjects = matches ? [product, revised, latest] : [other, otherRevised, otherLatest];
        JsonObject body = f.PolicyBody();
        Parameters(body)["schedule"] = new JsonObject { ["kind"] = "release", ["product"] = product.Id };
        Parameters(body)["graceDays"] = 0;
        RecordEnvelope policy = f.Policy(body);
        f.Estate.SetClock(At("2026-10-13T14:00:00Z"));
        RecordEnvelope publication = Publish(f, Candidate(f, subjects[revision], "1.0.0"));
        PolicyEvaluation result = Evaluate(f);
        Assert.True(result.Complete);
        Assert.Equal(matches ? 1 : 0, Assert.Single(result.Rules).Subjects);
        Assert.Equal(matches ? 1 : 0, result.Holds);
        if (matches)
        {
            PolicyFinding finding = Assert.Single(result.Findings);
            Assert.Equal("unmet", finding.Verdict);
            Assert.Equal(publication.Id, finding.Duty!.Occurrence.GetProperty("publication").GetProperty("id").GetString());
            Assert.Equal(publication.ComputedHash, finding.Duty.Occurrence.GetProperty("publication").GetProperty("hash").GetString());
            Assert.Equal(publication.At, finding.Duty.DueAt);
        }
        JsonObject link = f.DutyLink(policy);
        link["occurrence"] = new JsonObject { ["kind"] = "release", ["publication"] = PolicyFixture.Ref(publication) };
        RecordEnvelope completion = f.Result(link);
        if (matches)
        {
            f.Review(completion);
            Assert.True(Assert.Single(Evaluate(f).Findings).Duty!.Completed);
        }
        else Assert.Contains(f.Estate.Load().Findings, finding => finding.Code == "duty.publication" &&
            finding.Location.StartsWith(completion.ExpectedPath, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ReleaseScheduleRetirementAccountsForDebtAcrossProductRevisions(bool endDebt)
    {
        using var f = new PolicyFixture();
        RecordEnvelope product = Product(f, "example");
        RecordEnvelope revised = f.Add("product", PolicyFixture.Body(product), supersedes: product.Id);
        JsonObject body = f.PolicyBody();
        Parameters(body)["schedule"] = new JsonObject { ["kind"] = "release", ["product"] = product.Id };
        RecordEnvelope policy = f.Policy(body);
        f.Estate.SetClock(At("2026-10-13T14:00:00Z"));
        RecordEnvelope publication = Publish(f, Candidate(f, revised, "1.0.0"));
        f.AssertValid();
        JsonObject retirement = Retired(policy);
        if (endDebt)
        {
            JsonObject disposition = f.Disposition(policy);
            disposition["occurrence"] = new JsonObject { ["kind"] = "release", ["publication"] = PolicyFixture.Ref(publication) };
            retirement["dutyDispositions"] = new JsonArray(disposition);
        }
        RecordEnvelope retired = f.Policy(retirement, policy);
        if (endDebt)
        {
            PolicyFinding finding = Assert.Single(Evaluate(f).Findings);
            Assert.Equal("ended-unmet", finding.Disposition);
            Assert.False(finding.Duty!.Completed);
        }
        else Assert.Contains(f.Estate.Load().Findings, finding => finding.Code == "duty.inventory" &&
            finding.Location.StartsWith(retired.ExpectedPath, StringComparison.Ordinal));
    }

    internal static JsonObject CredentialBody(PolicyFixture f, string name = "fixture-reference", string date = "2026-10-14T00:00:00Z") => new()
    {
        ["name"] = name, ["purpose"] = "Synthetic date evidence.", ["kind"] = "other", ["holder"] = f.Estate.OperatorId, ["store"] = "Fixture reference only",
        ["scopes"] = new JsonArray(), ["status"] = "active", ["rotateBy"] = date,
    };
    internal static JsonObject DateBody(PolicyFixture f, RecordEnvelope credential, bool continuing = true)
    {
        JsonObject body = f.PolicyBody(); JsonObject parameters = Parameters(body);
        parameters["subject"] = PolicyFixture.Ref(credential, false);
        parameters["schedule"] = new JsonObject { ["kind"] = "date", ["at"] = EstateModel.BodyString(credential, "rotateBy"), ["leadDays"] = 1,
            ["dateSource"] = new JsonObject { ["kind"] = "credential-rotate-by", ["record"] = PolicyFixture.Ref(credential) } };
        parameters["graceDays"] = 0; parameters["renewal"] = continuing ? "next-date-required" : "none";
        return body;
    }
    internal static JsonObject DateLink(PolicyFixture f, RecordEnvelope policy, RecordEnvelope subject)
    {
        JsonObject link = f.DutyLink(policy); link["subject"] = PolicyFixture.Ref(subject);
        link["occurrence"] = new JsonObject { ["kind"] = "date", ["at"] = policy.Body.GetProperty("rules")[0].GetProperty("parameters").GetProperty("schedule").GetProperty("at").GetString() };
        return link;
    }

    [Fact]
    public void AnAlreadyDueDateKeepsItsRealDeadlineAndDoesNotMoveWithItsSource()
    {
        using var f = new PolicyFixture();
        RecordEnvelope source = f.Add("credential-reference", CredentialBody(f));
        RecordEnvelope policy = f.Policy(DateBody(f, source, continuing: false));
        PolicyFinding initial = Assert.Single(Evaluate(f, FirstDue).Findings);
        Assert.True(initial.Duty!.AlreadyDueAtAdoption);
        Assert.True(initial.ReleaseHold);
        Assert.Equal("2026-10-13T00:00:00Z", initial.Duty.DueAt);
        f.Estate.SetClock(At("2026-10-13T14:00:00Z"));
        JsonObject changed = PolicyFixture.Body(source); changed["rotateBy"] = "2026-11-14T00:00:00Z";
        f.Add("credential-reference", changed, supersedes: source.Id);
        Assert.Equal(initial.Duty.DueAt, Assert.Single(Evaluate(f).Findings).Duty!.DueAt);
        RecordEnvelope review = f.Review(f.Result(DateLink(f, policy, source)));
        Assert.Equal(review.At, Assert.Single(Evaluate(f).Findings).Duty!.CompletedAt);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ContinuingRenewalNeedsNewExactEvidenceAndTheSameSubject(bool changeSubject)
    {
        using var f = new PolicyFixture();
        RecordEnvelope source = f.Add("credential-reference", CredentialBody(f));
        RecordEnvelope policy = f.Policy(DateBody(f, source)); f.Estate.SetClock(At("2026-10-13T14:00:00Z"));
        f.Review(f.Result(DateLink(f, policy, source)));
        PolicyFinding pending = Assert.Single(Evaluate(f).Findings);
        Assert.False(pending.Duty!.Completed);
        Assert.Equal("Renewal observed; next date needs declaration.", pending.NextAction);
        RecordEnvelope nextSource = f.Add("credential-reference", CredentialBody(f, changeSubject ? "another-reference" : "fixture-reference", "2026-11-14T00:00:00Z"), supersedes: changeSubject ? null : source.Id);
        JsonObject body = PolicyFixture.Body(policy); JsonObject parameters = Parameters(body);
        if (changeSubject) parameters["subject"] = PolicyFixture.Ref(nextSource, false);
        parameters["schedule"]!["at"] = "2026-11-14T00:00:00Z";
        parameters["schedule"]!["dateSource"]!["record"] = PolicyFixture.Ref(nextSource);
        RecordEnvelope successor = f.Policy(body, policy);
        PolicyFinding previous = Assert.Single(Evaluate(f).Findings, x => x.Duty!.Origin == policy.Reference);
        Assert.Equal(!changeSubject, previous.Duty!.Completed);
        if (!changeSubject)
        {
            Assert.Equal(successor.At, previous.Duty.CompletedAt);
            Assert.Contains(successor.Reference, previous.Evidence);
        }
        Assert.Equal(source.Id, previous.Duty.SubjectIdentity);
    }

    [Theory]
    [InlineData("missing-bytes")]
    [InlineData("adverse-review")]
    [InlineData("superseded-report")]
    public void ReviewedDateReportsRequireRetainedBytesAndCurrentExactReview(string unavailable)
    {
        using var f = new PolicyFixture();
        byte[] bytes = Encoding.UTF8.GetBytes("Fixture provider expiry report."); string hash = ContentHash.Sha256(bytes);
        RecordEnvelope initial = f.Result(); JsonObject reportBody = PolicyFixture.Body(initial);
        reportBody["outputs"] = new JsonArray(new JsonObject { ["name"] = "date report", ["hash"] = hash });
        RecordEnvelope report = f.Add("result", reportBody, builder: true, subject: initial.Subject, supersedes: initial.Id);
        RecordEnvelope review = f.Review(report);
        JsonObject body = f.PolicyBody(); JsonObject parameters = Parameters(body);
        parameters["schedule"] = new JsonObject { ["kind"] = "date", ["at"] = "2026-10-20T00:00:00Z", ["leadDays"] = 2,
            ["dateSource"] = new JsonObject { ["kind"] = "reviewed-report", ["result"] = PolicyFixture.Ref(report), ["review"] = PolicyFixture.Ref(review) } };
        parameters["graceDays"] = 0;
        f.Policy(body); f.Estate.SetClock(At("2026-10-13T14:00:00Z"));
        PolicyObject retained = new(hash, [.. bytes]);
        Assert.Equal("scheduled", Assert.Single(Evaluate(f, objects: [retained]).Findings).Disposition);
        if (unavailable == "adverse-review") f.Review(report, outcome: "fail");
        if (unavailable == "superseded-report") f.Add("result", reportBody, builder: true, subject: report.Subject, supersedes: report.Id);
        PolicyFinding finding = Assert.Single(Evaluate(f, objects: unavailable == "missing-bytes" ? [] : [retained]).Findings);
        Assert.Equal("unavailable", finding.Verdict);
        Assert.True(finding.ReleaseHold);
        Assert.False(finding.Duty!.Completed);
    }

    [Fact]
    public void DayIntervalsCrossDaylightSavingWithoutChangingElapsedSeconds()
    {
        using var f = new PolicyFixture();
        JsonObject body = f.PolicyBody(); Parameters(body)["schedule"]!["unit"] = "days";
        Parameters(body)["schedule"]!["firstDueAt"] = "2026-10-31T08:00:00Z";
        f.Policy(body);
        PolicyEvaluation result = Evaluate(f, "2026-11-02T08:00:00Z");
        Assert.Equal("2026-11-01T08:00:00Z", Number(result, 1).Duty!.DueAt);
        Assert.Equal(86400, (At(Number(result, 2).Duty!.DueAt) - At(Number(result, 1).Duty!.DueAt)).TotalSeconds);
    }

    [Fact]
    public void OnlyTheFirstObservedPublicationOpensAnOccurrenceAndWithdrawalPreservesIt()
    {
        using var f = new PolicyFixture();
        RecordEnvelope product = Product(f, "example"); RecordEnvelope another = Product(f, "another");
        JsonObject body = f.PolicyBody(); Parameters(body)["schedule"] = new JsonObject { ["kind"] = "release", ["product"] = product.Id }; Parameters(body)["graceDays"] = 0;
        RecordEnvelope policy = f.Policy(body); f.Estate.SetClock(At("2026-10-13T14:00:00Z"));
        RecordEnvelope candidate = Candidate(f, product, "1.0.0");
        Assert.Equal(0, Assert.Single(Evaluate(f).Rules).Subjects);
        Publish(f, Candidate(f, another, "1.0.0"));
        RecordEnvelope withdrawn = Candidate(f, product, "1.0.1");
        JsonObject removed = PolicyFixture.Body(withdrawn); removed["status"] = "withdrawn"; f.Add("release", removed, subject: withdrawn.Subject, supersedes: withdrawn.Id);
        Assert.Equal(0, Assert.Single(Evaluate(f).Rules).Subjects);
        RecordEnvelope publication = Publish(f, candidate);
        PolicyFinding first = Assert.Single(Evaluate(f).Findings);
        Assert.Equal(publication.At, first.Duty!.DueAt);
        Assert.True(first.ReleaseHold);
        RecordEnvelope correction = Publish(f, publication);
        Assert.Equal(publication.Id, Assert.Single(Evaluate(f).Findings).Duty!.Occurrence.GetProperty("publication").GetProperty("id").GetString());
        removed = PolicyFixture.Body(correction); removed["status"] = "withdrawn";
        f.Add("release", removed, subject: correction.Subject, supersedes: correction.Id);
        PolicyEvaluation evaluation = Evaluate(f);
        Assert.Single(evaluation.Findings); Assert.Equal(1, evaluation.Holds);
        JsonObject link = f.DutyLink(policy); link["occurrence"] = new JsonObject { ["kind"] = "release", ["publication"] = PolicyFixture.Ref(publication) };
        f.Review(f.Result(link));
        Assert.True(Assert.Single(Evaluate(f).Findings).Duty!.Completed);
    }

    [Fact]
    public void ARevisedReleaseScheduleStartsStrictlyAfterItsBoundaryAndRetainsOldDebt()
    {
        using var f = new PolicyFixture();
        RecordEnvelope product = Product(f, "example");
        JsonObject body = f.PolicyBody(); Parameters(body)["schedule"] = new JsonObject { ["kind"] = "release", ["product"] = product.Id };
        RecordEnvelope policy = f.Policy(body); f.Estate.SetClock(At("2026-10-13T14:00:00Z"));
        RecordEnvelope first = Publish(f, Candidate(f, product, "1.0.0"));
        body = PolicyFixture.Body(policy); Parameters(body)["graceDays"] = 0;
        RecordEnvelope revised = f.Policy(body, policy);
        PolicyFinding retained = Assert.Single(Evaluate(f).Findings);
        Assert.Equal(policy.Reference, retained.Duty!.Origin);
        Assert.Equal("due-within-grace", retained.Disposition);
        RecordEnvelope second = Publish(f, Candidate(f, product, "1.0.1"));
        PolicyEvaluation evaluation = Evaluate(f);
        Assert.Equal(2, evaluation.Findings.Length);
        Assert.Contains(evaluation.Findings, x => x.Duty!.Origin == policy.Reference && x.Duty.DueAt == first.At);
        Assert.Contains(evaluation.Findings, x => x.Duty!.Origin == revised.Reference && x.Duty.DueAt == second.At && x.ReleaseHold);
    }

    internal static RecordEnvelope Product(PolicyFixture f, string slug) => f.Add("product", new JsonObject
    {
        ["name"] = "Fixture product", ["slug"] = slug, ["summary"] = "Synthetic publication history.",
        ["editions"] = new JsonArray(new JsonObject { ["name"] = "Community", ["slug"] = "community", ["summary"] = "Fixture edition." }),
        ["surfaces"] = new JsonArray(new JsonObject { ["name"] = "CLI", ["slug"] = "cli", ["summary"] = "Fixture surface." }),
    });

    internal static RecordEnvelope Candidate(PolicyFixture f, RecordEnvelope product, string version) => f.Add("release", new JsonObject
    {
        ["version"] = version, ["repository"] = "Example/example", ["commit"] = new string('1', 40), ["status"] = "candidate",
        ["artifacts"] = new JsonArray(new JsonObject { ["name"] = "fixture", ["hash"] = PolicyBasis.Hash("fixture bytes"), ["bytes"] = 0, ["platform"] = "any" }),
        ["recordFormat"] = new JsonObject { ["reads"] = new JsonArray(1, 2, 3), ["writes"] = 3 }, ["evidence"] = new JsonArray(), ["qualified"] = new JsonArray(), ["signing"] = "Fixture only", ["limitations"] = new JsonArray(),
    }, subject: new("product", product.Id, null));

    private static RecordEnvelope Publish(PolicyFixture f, RecordEnvelope candidate)
    {
        f.Add("acceptance", new JsonObject { ["statement"] = "Accept the fixture publication.", ["basis"] = new JsonArray(), ["self"] = true }, subject: candidate.Reference);
        JsonObject body = PolicyFixture.Body(candidate); body["status"] = "published"; body["publishedAt"] = RecordParser.FormatTimestamp(f.Estate.Clock); body["url"] = "https://example.invalid/releases/fixture";
        return f.Add("release", body, subject: candidate.Subject, supersedes: candidate.Id);
    }
}
