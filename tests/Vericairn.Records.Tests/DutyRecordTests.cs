// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using System.Text.Json.Nodes;
using Vericairn.Records.Estate;
using Vericairn.Records.Model;
using Vericairn.Records.Types;

namespace Vericairn.Records.Tests;

public class DutyRecordTests
{
    private static void Due(PolicyFixture f) => f.Estate.SetClock(new DateTimeOffset(2026, 10, 13, 14, 0, 0, TimeSpan.Zero));

    [Fact]
    public void ResultsAndOrdinaryReviewsCarryExactSignedDutyEvidence()
    {
        using var f = new PolicyFixture();
        RecordEnvelope policy = f.Policy();
        Due(f);
        RecordEnvelope result = f.Result(f.DutyLink(policy));
        f.Review(result, new JsonArray(PolicyFixture.Ref(policy), PolicyFixture.Ref(f.Decision)));
        f.AssertValid();
        Assert.Empty(f.Estate.Load().OfType("acceptance"));
    }

    [Fact]
    public void UnsignedResultsAndReviewsNeverEstablishDutyEvidence()
    {
        using var f = new PolicyFixture();
        RecordEnvelope policy = f.Policy();
        Due(f);
        RecordEnvelope result = f.Result(f.DutyLink(policy), sign: false);
        PolicyRecordsTests.Has(f, result, "signature.required");
        RecordEnvelope review = f.Review(result, sign: false);
        PolicyRecordsTests.Has(f, review, "signature.required");
    }

    [Theory]
    [InlineData("hash")]
    [InlineData("future")]
    [InlineData("rule")]
    [InlineData("subject")]
    [InlineData("unknown")]
    [InlineData("duplicate")]
    [InlineData("proof")]
    public void DutyLinksRejectInventedOrLossyReferences(string failure)
    {
        using var f = new PolicyFixture();
        RecordEnvelope policy = f.Policy();
        Due(f);
        JsonObject link = f.DutyLink(policy);
        switch (failure)
        {
            case "hash": link["policy"]!.AsObject().Remove("hash"); break;
            case "future": link["occurrence"]!["number"] = 10; break;
            case "rule": link["rule"] = "another"; break;
            case "subject": link["subject"] = PolicyFixture.Ref(f.Estate.Builder); break;
            case "unknown": link["performedAt"] = "2026-10-01T00:00:00Z"; break;
            case "proof": link["proofs"] = new JsonArray(new JsonObject { ["clause"] = "invented", ["records"] = new JsonArray(PolicyFixture.Ref(f.Decision)) }); break;
        }
        RecordEnvelope result = f.Result(link);
        if (failure == "duplicate")
        {
            JsonObject body = PolicyFixture.Body(result);
            body["duties"]!.AsArray().Add(link.DeepClone());
            result = f.Add("result", body, builder: true, subject: result.Subject, supersedes: result.Id);
        }
        Assert.Contains(f.Estate.Load().Findings, x => x.Severity == Severity.Error && x.Location.StartsWith(result.ExpectedPath, StringComparison.Ordinal));
    }

    [Fact]
    public void TheMonthAnchorNeverDriftsAndCalendarOverflowIsRefused()
    {
        JsonElement schedule = JsonSerializer.SerializeToElement(new { kind = "interval", unit = "months", every = 1, firstDueAt = "2028-01-31T08:00:00Z" });
        Assert.Equal("2028-02-29T08:00:00Z", RecordParser.FormatTimestamp(DutyRecords.IntervalDue(schedule, 1)!.Value));
        Assert.Equal("2028-03-31T08:00:00Z", RecordParser.FormatTimestamp(DutyRecords.IntervalDue(schedule, 2)!.Value));
        Assert.Null(DutyRecords.IntervalDue(schedule, long.MaxValue));
    }

    [Theory]
    [InlineData("cron")]
    [InlineData("grace")]
    [InlineData("check")]
    [InlineData("clause")]
    [InlineData("renewal")]
    [InlineData("selection")]
    public void DutyDeclarationsUseClosedParameterAndClauseShapes(string failure)
    {
        using var f = new PolicyFixture();
        JsonObject body = f.PolicyBody();
        JsonObject parameters = body["rules"]![0]!["parameters"]!.AsObject();
        switch (failure)
        {
            case "cron": parameters["schedule"]!["kind"] = "cron"; break;
            case "grace": parameters["graceDays"] = -1; break;
            case "check": parameters["discharge"]!["checks"]!.AsArray().Add("inventory"); break;
            case "clause": parameters["discharge"]!["records"]!.AsArray().Add(new JsonObject { ["key"] = "proof", ["kind"] = "arbitrary-query", ["expression"] = "all" }); break;
            case "renewal": parameters["renewal"] = "next-date-required"; break;
            case "selection": body["appliesTo"] = new JsonObject { ["kind"] = "repository", ["repository"] = f.Estate.Estate.Id, ["selectors"] = new JsonArray("docs/**") }; break;
        }
        RecordEnvelope policy = f.Policy(body);
        Assert.Contains(f.Estate.Load().Findings, x => x.Severity == Severity.Error && x.Location.StartsWith(policy.ExpectedPath, StringComparison.Ordinal));
    }

    [Fact]
    public void ExactDateEvidenceAndGraceAreCheckedWithoutInventingARealRenewal()
    {
        using var f = new PolicyFixture();
        RecordEnvelope credential = f.Add("credential-reference", new JsonObject { ["name"] = "example-reference", ["purpose"] = "Test date evidence.", ["kind"] = "other", ["holder"] = f.Estate.OperatorId, ["store"] = "test reference only", ["scopes"] = new JsonArray(), ["status"] = "active", ["rotateBy"] = "2026-11-01T00:00:00Z" });
        JsonObject body = f.PolicyBody();
        JsonObject parameters = body["rules"]![0]!["parameters"]!.AsObject();
        parameters["subject"] = PolicyFixture.Ref(credential, false);
        parameters["schedule"] = new JsonObject { ["kind"] = "date", ["at"] = "2026-11-01T00:00:00Z", ["leadDays"] = 7, ["dateSource"] = new JsonObject { ["kind"] = "credential-rotate-by", ["record"] = PolicyFixture.Ref(credential) } };
        parameters["renewal"] = "next-date-required";
        f.Policy(body);
        f.AssertValid();
        JsonObject wrong = body.DeepClone().AsObject();
        wrong["key"] = "wrong-date";
        wrong["rules"]![0]!["parameters"]!["schedule"]!["at"] = "2026-11-02T00:00:00Z";
        PolicyRecordsTests.Has(f, f.Policy(wrong), "duty.date-source");
    }

    [Fact]
    public void ResultSubjectVerificationAvoidsAHashCycleAndRequiresExactReviewRead()
    {
        using var f = new PolicyFixture();
        JsonObject body = f.PolicyBody();
        body["rules"]![0]!["parameters"]!["discharge"]!["records"] = new JsonArray(new JsonObject { ["key"] = "restore", ["kind"] = "verification", ["method"] = "restore", ["subject"] = "result" });
        RecordEnvelope policy = f.Policy(body);
        Due(f);
        JsonObject link = f.DutyLink(policy);
        link["proofs"] = new JsonArray(new JsonObject { ["kind"] = "result-verification", ["clause"] = "restore" });
        RecordEnvelope result = f.Result(link);
        f.AssertValid(); // A result may be recorded while its verification and review are still pending.
        RecordEnvelope verification = f.Add("verification", VerificationBody("restore"), subject: result.Reference);
        f.Review(result, new JsonArray(PolicyFixture.Ref(verification)));
        f.AssertValid();
        JsonObject retirement = Retired(policy);
        f.Policy(retirement, policy);
        f.AssertValid(); // Completed work is not relabeled ended-unmet.
    }

    [Fact]
    public void AVerificationClauseCannotSelectNoReviewForItsOwnResult()
    {
        using var f = new PolicyFixture();
        JsonObject body = f.PolicyBody();
        body["rules"]![0]!["parameters"]!["discharge"]!["review"] = "none";
        body["rules"]![0]!["parameters"]!["discharge"]!["records"] = new JsonArray(new JsonObject { ["key"] = "restore", ["kind"] = "verification", ["method"] = "restore", ["subject"] = "result" });
        PolicyRecordsTests.Has(f, f.Policy(body), "duty.review");
    }

    [Theory]
    [InlineData("late-first-review", false)]
    [InlineData("later-pass", true)]
    [InlineData("adverse-then-pass", false)]
    public void VerificationExpiryUsesTheFirstContinuousCompletion(string history, bool completed)
    {
        using var f = new PolicyFixture();
        JsonObject body = f.PolicyBody();
        body["rules"]![0]!["parameters"]!["discharge"]!["records"] = new JsonArray(new JsonObject { ["key"] = "restore", ["kind"] = "verification", ["method"] = "restore", ["subject"] = "result" });
        RecordEnvelope policy = f.Policy(body);
        Due(f);
        JsonObject link = f.DutyLink(policy);
        link["proofs"] = new JsonArray(new JsonObject { ["kind"] = "result-verification", ["clause"] = "restore" });
        RecordEnvelope result = f.Result(link);
        JsonObject observation = VerificationBody("restore");
        observation["expires"] = "2026-10-13T15:00:00Z";
        RecordEnvelope verification = f.Add("verification", observation, subject: result.Reference);
        if (history != "late-first-review") f.Review(result, new JsonArray(PolicyFixture.Ref(verification)));
        f.Estate.SetClock(new DateTimeOffset(2026, 10, 13, 16, 0, 0, TimeSpan.Zero));
        if (history == "adverse-then-pass") f.Review(result, new JsonArray(PolicyFixture.Ref(verification)), outcome: "fail");
        f.Review(result, new JsonArray(PolicyFixture.Ref(verification)));
        RecordEnvelope retired = f.Policy(Retired(policy), policy);
        if (completed) f.AssertValid();
        else PolicyRecordsTests.Has(f, retired, "duty.inventory");
    }

    [Fact]
    public void DispositionsEnumerateEveryUncompletedOccurrenceIncludingGrace()
    {
        using var f = new PolicyFixture();
        RecordEnvelope policy = f.Policy();
        Due(f); // The first occurrence is due, but is still within its three days of grace.
        JsonObject retirement = Retired(policy);
        retirement["dutyDispositions"] = new JsonArray(f.Disposition(policy));
        RecordEnvelope retired = f.Policy(retirement, policy);
        f.AssertValid();
        Assert.Equal("ended-unmet", retired.Body.GetProperty("dutyDispositions")[0].GetProperty("disposition").GetString());
        Assert.Empty(f.Estate.Load().OfType("result"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AContinuingRenewalNeedsNewDateEvidenceForTheSameSubject(bool changeSubject)
    {
        using var f = new PolicyFixture();
        JsonObject credentialBody = new() { ["name"] = "example-reference", ["purpose"] = "Synthetic renewal evidence.", ["kind"] = "other", ["holder"] = f.Estate.OperatorId, ["store"] = "test reference only", ["scopes"] = new JsonArray(), ["status"] = "active", ["rotateBy"] = "2026-10-14T00:00:00Z" };
        RecordEnvelope credential = f.Add("credential-reference", credentialBody);
        JsonObject body = f.PolicyBody();
        JsonObject parameters = body["rules"]![0]!["parameters"]!.AsObject();
        parameters["subject"] = PolicyFixture.Ref(credential, false);
        parameters["schedule"] = new JsonObject { ["kind"] = "date", ["at"] = "2026-10-14T00:00:00Z", ["leadDays"] = 1, ["dateSource"] = new JsonObject { ["kind"] = "credential-rotate-by", ["record"] = PolicyFixture.Ref(credential) } };
        parameters["graceDays"] = 0;
        parameters["renewal"] = "next-date-required";
        RecordEnvelope policy = f.Policy(body);
        Due(f);
        JsonObject link = f.DutyLink(policy);
        link["subject"] = PolicyFixture.Ref(credential);
        link["occurrence"] = new JsonObject { ["kind"] = "date", ["at"] = "2026-10-14T00:00:00Z" };
        f.Review(f.Result(link));
        credentialBody["rotateBy"] = "2026-11-14T00:00:00Z";
        if (changeSubject) credentialBody["name"] = "another-reference";
        RecordEnvelope source = f.Add("credential-reference", credentialBody, supersedes: changeSubject ? null : credential.Id);
        JsonObject next = PolicyFixture.Body(policy);
        JsonObject nextParameters = next["rules"]![0]!["parameters"]!.AsObject();
        if (changeSubject) nextParameters["subject"] = PolicyFixture.Ref(source, false);
        nextParameters["schedule"]!["at"] = "2026-11-14T00:00:00Z";
        nextParameters["schedule"]!["dateSource"]!["record"] = PolicyFixture.Ref(source);
        RecordEnvelope following = f.Policy(next, policy);
        f.AssertValid();
        RecordEnvelope retired = f.Policy(Retired(following), following);
        if (changeSubject) PolicyRecordsTests.Has(f, retired, "duty.inventory");
        else f.AssertValid();
    }

    [Theory]
    [InlineData("before-due")]
    [InlineData("withdrawn")]
    public void AnEditorialOrWithdrawnVerificationCannotEraseDueDebt(string history)
    {
        using var f = new PolicyFixture();
        JsonObject body = f.PolicyBody();
        body["rules"]![0]!["parameters"]!["discharge"]!["records"] = new JsonArray(new JsonObject { ["key"] = "inspect", ["kind"] = "verification", ["method"] = "inspection", ["subject"] = "duty-subject" });
        RecordEnvelope policy = f.Policy(body);
        if (history == "withdrawn") Due(f);
        RecordEnvelope verification = f.Add("verification", VerificationBody("inspection"), subject: f.Estate.Estate.Reference);
        Due(f);
        if (history == "before-due")
        {
            JsonObject edit = PolicyFixture.Body(verification);
            edit["summary"] = "Clarify the earlier observation without repeating the work.";
            verification = f.Add("verification", edit, subject: verification.Subject, supersedes: verification.Id);
        }
        JsonObject link = f.DutyLink(policy);
        link["proofs"] = new JsonArray(new JsonObject { ["clause"] = "inspect", ["records"] = new JsonArray(PolicyFixture.Ref(verification)) });
        f.Review(f.Result(link));
        if (history == "withdrawn")
        {
            JsonObject correction = PolicyFixture.Body(verification);
            correction["outcome"] = "failed";
            f.Add("verification", correction, subject: verification.Subject, supersedes: verification.Id);
        }
        PolicyRecordsTests.Has(f, f.Policy(Retired(policy), policy), "duty.inventory");
    }

    [Fact]
    public void CompletingOneOccurrenceNeverForgivesAnOlderOrLaterDebt()
    {
        using var f = new PolicyFixture();
        RecordEnvelope policy = f.Policy();
        f.Estate.SetClock(new DateTimeOffset(2026, 11, 14, 14, 0, 0, TimeSpan.Zero));
        RecordEnvelope result = f.Result(f.DutyLink(policy, 1));
        f.Review(result);
        JsonObject retirement = Retired(policy);
        retirement["dutyDispositions"] = new JsonArray(f.Disposition(policy, 0));
        f.Policy(retirement, policy);
        f.AssertValid();
    }

    [Fact]
    public void CompletedOrPreviouslyEndedOccurrencesCannotBeEndedAgain()
    {
        using var f = new PolicyFixture();
        RecordEnvelope policy = f.Policy();
        Due(f);
        RecordEnvelope result = f.Result(f.DutyLink(policy));
        f.Review(result);
        JsonObject retirement = Retired(policy);
        retirement["dutyDispositions"] = new JsonArray(f.Disposition(policy));
        PolicyRecordsTests.Has(f, f.Policy(retirement, policy), "duty.disposition");
    }

    [Fact]
    public void AChangedScheduleCarriesOldOccurrencesAndDoesNotBackdateTheNewAnchor()
    {
        using var f = new PolicyFixture();
        RecordEnvelope first = f.Policy();
        Due(f);
        JsonObject next = PolicyFixture.Body(first);
        next["rules"]![0]!["parameters"]!["schedule"]!["firstDueAt"] = "2026-10-20T13:00:00Z";
        RecordEnvelope changed = f.Policy(next, first);
        f.Result(f.DutyLink(first));
        f.AssertValid();
        JsonObject reset = PolicyFixture.Body(changed);
        reset["rules"]![0]!["parameters"]!["schedule"]!["firstDueAt"] = "2026-10-13T13:00:00Z";
        PolicyRecordsTests.Has(f, f.Policy(reset, changed), "duty.boundary");
    }

    [Fact]
    public void EditorialPolicyRevisionsDoNotBecomeNewOccurrenceOrigins()
    {
        using var f = new PolicyFixture();
        RecordEnvelope policy = f.Policy();
        JsonObject edit = PolicyFixture.Body(policy);
        edit["title"] = "A clearer title";
        RecordEnvelope edited = f.Add("estate-policy", edit, supersedes: policy.Id);
        Due(f);
        PolicyRecordsTests.Has(f, f.Result(f.DutyLink(edited)), "duty.origin");
    }

    [Fact]
    public void RenamingARuleCannotReopenItsPastScheduleOrDiscardTheOldDebt()
    {
        using var f = new PolicyFixture();
        RecordEnvelope policy = f.Policy();
        Due(f);
        JsonObject renamed = PolicyFixture.Body(policy);
        renamed["rules"]![0]!["key"] = "renamed";
        RecordEnvelope successor = f.Policy(renamed, policy);
        PolicyRecordsTests.Has(f, successor, "duty.boundary");
        PolicyRecordsTests.Has(f, successor, "duty.inventory");
    }

    [Fact]
    public void MalformedRequiredChecksCannotCrashRetirementValidationOrEraseDebt()
    {
        using var f = new PolicyFixture();
        JsonObject body = f.PolicyBody();
        body["rules"]![0]!["parameters"]!["discharge"]!["checks"] = new JsonArray(42);
        RecordEnvelope policy = f.Policy(body);
        Due(f);
        f.Review(f.Result(f.DutyLink(policy)));
        PolicyRecordsTests.Has(f, f.Policy(Retired(policy), policy), "duty.inventory");
    }

    [Fact]
    public void OrdinaryReadReferencesAreExactUniqueAndCannotReplaceGoalConditions()
    {
        using var f = new PolicyFixture();
        RecordEnvelope result = f.Result();
        RecordEnvelope review = f.Review(result, new JsonArray(PolicyFixture.Ref(f.Decision, false), PolicyFixture.Ref(f.Decision)));
        PolicyRecordsTests.Has(f, review, "policy.reference");
        PolicyRecordsTests.Has(f, review, "policy.duplicate");
    }

    internal static JsonObject VerificationBody(string method) => new()
    {
        ["method"] = method, ["commit"] = new string('1', 40), ["environment"] = new JsonObject { ["platform"] = "test" }, ["role"] = "operator", ["outcome"] = "passed", ["summary"] = "Observed the declared test.", ["evidence"] = new JsonArray(), ["limitations"] = new JsonArray(), ["evidenceClass"] = "observation", ["observer"] = "person",
    };

    internal static JsonObject Retired(RecordEnvelope policy)
    {
        JsonObject body = PolicyFixture.Body(policy);
        body["state"] = "retired";
        body["retirement"] = new JsonObject { ["reason"] = "End the requirement.", ["findings"] = "Completed work keeps its evidence; all other debt is explicitly accounted for." };
        return body;
    }
}
