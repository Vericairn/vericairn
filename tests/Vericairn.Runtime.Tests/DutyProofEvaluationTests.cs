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

public class DutyProofEvaluationTests
{
    [Fact]
    public void EndingDebtRetainsTheEarlierWithdrawnJudgment()
    {
        using var f = new PolicyFixture();
        RecordEnvelope policy = f.Policy(); f.Estate.SetClock(At("2026-10-13T14:00:00Z"));
        RecordEnvelope result = f.Result(f.DutyLink(policy)); RecordEnvelope passed = f.Review(result);
        RecordEnvelope adverse = f.Review(result, outcome: "fail");
        JsonObject body = PolicyFixture.Body(policy); body["dutyDispositions"] = new JsonArray(f.Disposition(policy));
        f.Policy(body, policy);
        PolicyFinding ended = Number(Evaluate(f), 0);
        Assert.Equal("ended-unmet", ended.Disposition);
        Assert.False(ended.Duty!.Completed);
        DutyCompletionPeriod history = Assert.Single(ended.Duty.Completions);
        Assert.Equal(passed.At, history.From); Assert.Equal(adverse.At, history.Until);
    }

    [Fact]
    public void ADeclaredUnreviewedRouteIsLabeledAndLaterAdmissionLossDoesNotEraseSignedWork()
    {
        using var f = new PolicyFixture();
        JsonObject body = f.PolicyBody(); Parameters(body)["discharge"]!["review"] = "none";
        RecordEnvelope policy = f.Policy(body); f.Estate.SetClock(At("2026-10-13T14:00:00Z"));
        RecordEnvelope result = f.Result(f.DutyLink(policy));
        PolicyFinding first = Number(Evaluate(f), 0);
        Assert.Equal(result.At, first.Duty!.CompletedAt);
        Assert.Contains("unreviewed claim", first.Actual, StringComparison.Ordinal);
        RecordEnvelope admission = Assert.Single(f.Estate.Load().Current("admission"), a => a.Subject == f.BuilderDevice.Reference);
        JsonObject revoked = PolicyFixture.Body(admission); revoked["status"] = "revoked"; revoked["revokedAt"] = RecordParser.FormatTimestamp(f.Estate.Clock.AddSeconds(1));
        f.Add("admission", revoked, subject: admission.Subject, supersedes: admission.Id);
        Assert.Equal(result.At, Number(Evaluate(f), 0).Duty!.CompletedAt);
    }

    [Theory]
    [InlineData("valid", "satisfied")]
    [InlineData("proof-commit", "unmet")]
    [InlineData("inventory-commit", "unavailable")]
    [InlineData("missing-inventory", "unavailable")]
    public void RepositoryVerificationRequiresTheExactDeclaredCommitAndCompleteInventory(string fault, string verdict)
    {
        using var f = new PolicyFixture();
        RecordEnvelope repository = f.Add("resource", new JsonObject { ["kind"] = "repository", ["name"] = "Fixture source", ["role"] = "Test exact commit proof.", ["location"] = new JsonObject { ["github"] = "Example/example" }, ["visibility"] = "private" });
        JsonObject body = f.PolicyBody(); Parameters(body)["subject"] = PolicyFixture.Ref(repository, false);
        JsonObject clause = VerificationClause("duty-subject"); clause["commitSource"] = "result-scope"; clause["repository"] = repository.Id;
        Parameters(body)["discharge"]!["records"] = new JsonArray(clause);
        RecordEnvelope policy = f.Policy(body); f.Estate.SetClock(At("2026-10-13T14:00:00Z"));
        string commit = new('1', 40); JsonObject observation = VerificationBody();
        if (fault == "proof-commit") observation["commit"] = new string('2', 40);
        RecordEnvelope verification = f.Add("verification", observation, subject: repository.Reference);
        JsonObject link = f.DutyLink(policy); link["subject"] = PolicyFixture.Ref(repository);
        link["scope"]!["repositories"]!.AsArray().Add(new JsonObject { ["record"] = PolicyFixture.Ref(repository), ["commit"] = commit });
        link["proofs"] = new JsonArray(new JsonObject { ["clause"] = "restore", ["records"] = new JsonArray(PolicyFixture.Ref(verification)) });
        f.Review(f.Result(link));
        PolicyRepository source = new(repository.Id, fault == "inventory-commit" ? new string('2', 40) : commit, PolicyBasis.RepositoryContract, true, []);
        PolicyFinding finding = Number(Evaluate(f, "2026-10-20T14:00:00Z", repositories: fault == "missing-inventory" ? [] : [source]), 0);
        Assert.Equal(verdict, finding.Verdict);
        Assert.Equal(fault == "valid", finding.Duty!.Completed);
    }

    internal static JsonObject VerificationBody(string method = "restore") => new()
    {
        ["method"] = method, ["commit"] = new string('1', 40), ["environment"] = new JsonObject { ["platform"] = "test" }, ["role"] = "operator", ["outcome"] = "passed",
        ["summary"] = "Observed the declared fixture check.", ["evidence"] = new JsonArray(), ["limitations"] = new JsonArray(), ["evidenceClass"] = "observation", ["observer"] = "person",
    };
    internal static JsonObject VerificationClause(string subject = "result") => new() { ["key"] = "restore", ["kind"] = "verification", ["method"] = "restore", ["subject"] = subject };
    private static (RecordEnvelope Policy, RecordEnvelope Result) ResultVerification(PolicyFixture f)
    {
        JsonObject body = f.PolicyBody(); Parameters(body)["discharge"]!["records"] = new JsonArray(VerificationClause());
        RecordEnvelope policy = f.Policy(body); f.Estate.SetClock(At("2026-10-13T14:00:00Z"));
        JsonObject link = f.DutyLink(policy); link["proofs"] = new JsonArray(new JsonObject { ["clause"] = "restore", ["kind"] = "result-verification" });
        return (policy, f.Result(link));
    }

    [Fact]
    public void ExactIndependentReviewIsRequiredAndAffirmationDoesNotMoveCompletion()
    {
        using var f = new PolicyFixture();
        RecordEnvelope policy = f.Policy(); f.Estate.SetClock(At("2026-10-13T14:00:00Z"));
        RecordEnvelope result = f.Result(f.DutyLink(policy));
        f.Review(f.Result());
        Assert.False(Number(Evaluate(f), 0).Duty!.Completed);
        RecordEnvelope first = f.Review(result);
        f.Estate.SetClock(At("2026-10-20T14:00:00Z"));
        RecordEnvelope later = f.Review(result);
        PolicyFinding finding = Number(Evaluate(f), 0);
        Assert.Equal("discharged", finding.Disposition);
        Assert.Equal(first.At, finding.Duty!.CompletedAt);
        Assert.Contains(first.Reference, Assert.Single(finding.Duty.Completions).Evidence);
        Assert.Contains(later.Reference, finding.Evidence);
        Assert.Empty(f.Estate.Load().OfType("acceptance"));
    }

    [Fact]
    public void AdverseReviewWithdrawsCompletionAndRecoveryHasItsOwnLateTime()
    {
        using var f = new PolicyFixture();
        RecordEnvelope policy = f.Policy(); f.Estate.SetClock(At("2026-10-13T14:00:00Z"));
        RecordEnvelope result = f.Result(f.DutyLink(policy)); RecordEnvelope first = f.Review(result);
        f.Estate.SetClock(At("2026-10-20T14:00:00Z"));
        RecordEnvelope adverse = f.Review(result, outcome: "fail");
        PolicyFinding withdrawn = Number(Evaluate(f), 0);
        Assert.False(withdrawn.Duty!.Completed);
        Assert.True(withdrawn.ReleaseHold);
        Assert.Contains(adverse.Reference, withdrawn.Evidence);
        Assert.Equal(adverse.At, Assert.Single(withdrawn.Duty.Completions).Until);
        RecordEnvelope recovered = f.Review(result);
        PolicyFinding finding = Number(Evaluate(f), 0);
        Assert.Equal("discharged-late", finding.Disposition);
        Assert.Equal(recovered.At, finding.Duty!.CompletedAt);
        Assert.Equal(2, finding.Duty.Completions.Length);
        Assert.Equal(first.At, finding.Duty.Completions[0].From);
        Assert.Equal((At(recovered.At) - At(finding.Duty.Deadline)).TotalSeconds, (double)finding.Duty.LateSeconds);
    }

    [Fact]
    public void ACorrectedResultNeedsItsOwnReviewAndAllQualifyingResultsRemainEvidence()
    {
        using var f = new PolicyFixture();
        RecordEnvelope policy = f.Policy(); f.Estate.SetClock(At("2026-10-13T14:00:00Z"));
        RecordEnvelope first = f.Result(f.DutyLink(policy)); f.Review(first);
        RecordEnvelope replacement = f.Add("result", PolicyFixture.Body(first), builder: true, subject: first.Subject, supersedes: first.Id);
        Assert.False(Number(Evaluate(f), 0).Duty!.Completed);
        RecordEnvelope replacementReview = f.Review(replacement);
        RecordEnvelope another = f.Result(f.DutyLink(policy)); f.Review(another);
        PolicyFinding finding = Number(Evaluate(f), 0);
        Assert.Equal(replacementReview.At, finding.Duty!.CompletedAt);
        Assert.Equal(3, finding.Duty.Completions.Length);
        Assert.Equal(2, finding.Duty.Completions.Count(p => p.Until is null));
        Assert.Contains(first.Reference, finding.Evidence);
        Assert.Contains(replacement.Reference, finding.Evidence);
        Assert.Contains(another.Reference, finding.Evidence);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("duplicate")]
    [InlineData("failed")]
    [InlineData("skipped")]
    [InlineData("not-performed")]
    [InlineData("unresolved")]
    [InlineData("partial")]
    public void AReviewedResultCannotHideAnIncompleteRequiredCheck(string fault)
    {
        using var f = new PolicyFixture();
        RecordEnvelope policy = f.Policy(); f.Estate.SetClock(At("2026-10-20T14:00:00Z"));
        RecordEnvelope result = f.Result(f.DutyLink(policy)); JsonObject body = PolicyFixture.Body(result);
        switch (fault)
        {
            case "missing": body["checks"] = new JsonArray(); break;
            case "duplicate": body["checks"]!.AsArray().Add(body["checks"]![0]!.DeepClone()); break;
            case "unresolved": body["unresolvedEffects"]!.AsArray().Add("Fixture observation remains uncertain."); break;
            case "partial": body["outcome"] = "partial"; break;
            default: body["checks"]![0]!["outcome"] = fault; break;
        }
        RecordEnvelope revised = f.Add("result", body, builder: true, subject: result.Subject, supersedes: result.Id); f.Review(revised);
        PolicyFinding finding = Number(Evaluate(f), 0);
        Assert.False(finding.Duty!.Completed);
        Assert.True(finding.ReleaseHold);
        Assert.Contains(revised.Reference, finding.Evidence);
    }

    [Fact]
    public void ResultVerificationIsAcyclicAndTheReviewReadsTheExactSelectedProof()
    {
        using var f = new PolicyFixture();
        (_, RecordEnvelope result) = ResultVerification(f);
        RecordEnvelope verification = f.Add("verification", VerificationBody(), subject: result.Reference);
        f.Review(result);
        Assert.False(Number(Evaluate(f), 0).Duty!.Completed);
        RecordEnvelope review = f.Review(result, new JsonArray(PolicyFixture.Ref(verification)));
        Assert.Equal(review.At, Number(Evaluate(f), 0).Duty!.CompletedAt);
        JsonObject failed = VerificationBody(); failed["outcome"] = "failed";
        RecordEnvelope adverse = f.Add("verification", failed, subject: result.Reference);
        PolicyFinding finding = Number(Evaluate(f), 0);
        Assert.False(finding.Duty!.Completed);
        Assert.Contains(adverse.Reference, finding.Evidence);
        RecordEnvelope recovered = f.Add("verification", VerificationBody(), subject: result.Reference);
        f.Review(result, new JsonArray(PolicyFixture.Ref(verification)));
        Assert.False(Number(Evaluate(f), 0).Duty!.Completed);
        RecordEnvelope final = f.Review(result, new JsonArray(PolicyFixture.Ref(recovered)));
        Assert.Equal(final.At, Number(Evaluate(f), 0).Duty!.CompletedAt);
    }

    [Theory]
    [InlineData("late-first-review", false)]
    [InlineData("later-pass", true)]
    [InlineData("adverse-then-pass", false)]
    [InlineData("expiry-equality", false)]
    public void VerificationExpiresAtCompletionRatherThanAtTodaysExplicitClock(string history, bool completed)
    {
        using var f = new PolicyFixture();
        (_, RecordEnvelope result) = ResultVerification(f);
        JsonObject body = VerificationBody(); body["expires"] = "2026-10-13T15:00:00Z";
        RecordEnvelope verification = f.Add("verification", body, subject: result.Reference);
        RecordEnvelope? first = history is "later-pass" or "adverse-then-pass" ? f.Review(result, new JsonArray(PolicyFixture.Ref(verification))) : null;
        f.Estate.SetClock(At(history == "expiry-equality" ? "2026-10-13T14:59:59Z" : "2026-10-13T16:00:00Z"));
        if (history == "adverse-then-pass") f.Review(result, new JsonArray(PolicyFixture.Ref(verification)), outcome: "fail");
        f.Review(result, new JsonArray(PolicyFixture.Ref(verification)));
        PolicyFinding finding = Number(Evaluate(f, "2026-10-20T14:00:00Z"), 0);
        Assert.Equal(completed, finding.Duty!.Completed);
        if (completed) Assert.Equal(first!.At, finding.Duty.CompletedAt);
        else Assert.Contains("expired", finding.Actual, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("method")]
    [InlineData("subject")]
    [InlineData("inference")]
    [InlineData("omitted-class")]
    [InlineData("before-due")]
    [InlineData("withdrawn")]
    [InlineData("performed-before-due")]
    public void DirectProofsPinTheirSubjectMethodAndNativeActionTime(string fault)
    {
        using var f = new PolicyFixture();
        JsonObject body = f.PolicyBody(); Parameters(body)["discharge"]!["records"] = new JsonArray(VerificationClause("duty-subject"));
        RecordEnvelope policy = f.Policy(body);
        if (fault != "before-due") f.Estate.SetClock(At("2026-10-13T14:00:00Z"));
        JsonObject proof = VerificationBody(fault == "method" ? "another-method" : "restore");
        if (fault == "inference") proof["evidenceClass"] = "inference";
        if (fault == "omitted-class") proof.Remove("evidenceClass");
        if (fault == "performed-before-due") proof["finishedAt"] = "2026-10-13T12:00:00Z";
        RecordEnvelope verification = f.Add("verification", proof, subject: fault == "subject" ? f.OperatorDevice.Reference : f.Estate.Estate.Reference);
        if (fault == "before-due")
        {
            f.Estate.SetClock(At("2026-10-13T14:00:00Z")); proof["summary"] = "Clarify the old action without repeating it.";
            verification = f.Add("verification", proof, subject: verification.Subject, supersedes: verification.Id);
        }
        JsonObject link = f.DutyLink(policy); link["proofs"] = new JsonArray(new JsonObject { ["clause"] = "restore", ["records"] = new JsonArray(PolicyFixture.Ref(verification)) });
        f.Review(f.Result(link));
        if (fault == "withdrawn") { proof["outcome"] = "failed"; f.Add("verification", proof, subject: verification.Subject, supersedes: verification.Id); }
        PolicyFinding finding = Number(Evaluate(f, "2026-10-20T14:00:00Z"), 0);
        Assert.False(finding.Duty!.Completed);
        Assert.True(finding.ReleaseHold);
        Assert.Contains("restore", finding.Actual, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("output")]
    [InlineData("scope")]
    [InlineData("proof")]
    public void MissingOrMutatedRetainedBytesAreUnavailableNeverSuccessful(string location)
    {
        using var f = new PolicyFixture();
        (_, RecordEnvelope result) = ResultVerification(f);
        byte[] bytes = Encoding.UTF8.GetBytes("Retained fixture evidence."); string hash = ContentHash.Sha256(bytes);
        JsonObject body = PolicyFixture.Body(result);
        if (location == "output") body["outputs"]!.AsArray().Add(new JsonObject { ["name"] = "report", ["hash"] = hash });
        if (location == "scope") body["duties"]![0]!["scope"]!["objects"]!.AsArray().Add(hash);
        result = f.Add("result", body, builder: true, subject: result.Subject, supersedes: result.Id);
        JsonObject observation = VerificationBody();
        if (location == "proof") observation["evidence"]!.AsArray().Add(new JsonObject { ["summary"] = "Retained proof.", ["hash"] = hash });
        RecordEnvelope verification = f.Add("verification", observation, subject: result.Reference); f.Review(result, new JsonArray(PolicyFixture.Ref(verification)));
        Assert.Equal("unavailable", Number(Evaluate(f), 0).Verdict);
        Assert.True(Number(Evaluate(f, objects: [new(hash, [.. bytes])]), 0).Duty!.Completed);
        Assert.Equal("unavailable", Number(Evaluate(f, objects: [new(hash, [1, 2, 3])]), 0).Verdict);
        Assert.Equal("unavailable", Number(Evaluate(f, objects: [new(hash, [.. bytes], Available: false)]), 0).Verdict);
    }
}
