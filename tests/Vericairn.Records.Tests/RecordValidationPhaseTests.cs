// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json.Nodes;
using Vericairn.Records.Authoring;
using Vericairn.Records.Estate;
using Vericairn.Records.Model;

namespace Vericairn.Records.Tests;

public class RecordValidationPhaseTests
{
    [Theory]
    [InlineData("duty", "exception.occurrence", "targets[0].kind")]
    [InlineData("pair", "exception.pair", "targets[0].kind")]
    [InlineData("publication", "exception.publication", "targets[0].publication")]
    public void ExceptionTargetsThatDependOnOtherRecordsAreCheckedInTheFullJudgment(string scenario, string code, string field)
    {
        using var f = new PolicyFixture();
        JsonObject definition = f.PolicyBody(duty: scenario == "duty");
        if (scenario == "pair")
            definition["rules"]![0]!["kind"] = AssignmentOverlaps.PolicyKind;
        RecordEnvelope policy = f.Policy(definition);
        f.Estate.SetClock(new DateTimeOffset(2026, 10, 13, 14, 0, 0, TimeSpan.Zero));
        JsonObject exception = f.ExceptionBody(policy);
        exception["rule"] = definition["rules"]![0]!["key"]!.GetValue<string>();
        var target = new JsonObject { ["kind"] = "record", ["record"] = PolicyFixture.Ref(f.Decision) };
        if (scenario == "publication")
        {
            RecordEnvelope product = ProgressReportTests.Product(f.Estate);
            string release = ProgressReportTests.ReleaseBody("1.0.0", ProgressReportTests.Commit('a'), "candidate");
            RecordEnvelope candidate = f.Estate.Add("release", f.Estate.OperatorId, release, product.Reference);
            target = new JsonObject { ["kind"] = "publication", ["publication"] = PolicyFixture.Ref(candidate) };
        }
        exception["targets"] = new JsonArray(target);

        AssertFullJudgmentFinding(f, "policy-exception", exception, code, field, subject: policy.Reference);
    }

    [Theory]
    [InlineData("date-source", "duty.date-source", "rules[0].parameters.schedule.dateSource.result")]
    [InlineData("commit-source", "duty.commit", "rules[0].parameters.discharge.records[0].commitSource")]
    [InlineData("activation", "policy.activation", "startsAt")]
    public void PolicyRulesThatDependOnOtherRecordsAreCheckedInTheFullJudgment(string scenario, string code, string field)
    {
        using var f = new PolicyFixture();
        JsonObject definition = f.PolicyBody(duty: scenario != "activation");
        RecordEnvelope? previous = null;
        switch (scenario)
        {
            case "date-source":
                RecordEnvelope result = f.Result();
                RecordEnvelope review = f.Review(result);
                definition["rules"]![0]!["parameters"]!["schedule"] = new JsonObject
                {
                    ["kind"] = "date", ["at"] = "2026-11-01T00:00:00Z", ["leadDays"] = 7,
                    ["dateSource"] = new JsonObject { ["kind"] = "reviewed-report", ["result"] = PolicyFixture.Ref(result), ["review"] = PolicyFixture.Ref(review) },
                };
                break;
            case "commit-source":
                JsonNode parameters = definition["rules"]![0]!["parameters"]!;
                parameters["subject"] = PolicyFixture.Ref(Repository(f), exact: false);
                parameters["discharge"]!["records"] = new JsonArray(new JsonObject { ["key"] = "inspect", ["kind"] = "verification", ["method"] = "inventory", ["subject"] = "duty-subject" });
                break;
            case "activation":
                previous = f.Policy(f.PolicyBody(duty: false));
                definition["startsAt"] = RecordParser.FormatTimestamp(previous.AtTime.AddSeconds(-1));
                break;
        }

        RecordEnvelope approval = f.Approve(definition, previous);
        definition["basis"] = new JsonArray(new JsonObject { ["decision"] = PolicyFixture.Ref(approval), ["requirement"] = "definition" });
        AssertFullJudgmentFinding(f, "estate-policy", definition, code, field, supersedes: previous?.Id);
    }

    [Fact]
    public void DuplicateDutyOccurrencesAcrossSubjectRevisionsAreCheckedInTheFullJudgment()
    {
        using var f = new PolicyFixture();
        RecordEnvelope subject = Repository(f);
        RecordEnvelope revision = f.Add("resource", PolicyFixture.Body(subject), supersedes: subject.Id);
        JsonObject definition = f.PolicyBody();
        definition["rules"]![0]!["parameters"]!["subject"] = PolicyFixture.Ref(subject, exact: false);
        RecordEnvelope policy = f.Policy(definition);
        f.Estate.SetClock(new DateTimeOffset(2026, 10, 13, 14, 0, 0, TimeSpan.Zero));
        JsonObject occurrence = f.DutyLink(policy);
        occurrence["subject"] = PolicyFixture.Ref(subject);
        JsonObject duplicate = occurrence.DeepClone().AsObject();
        duplicate["subject"] = PolicyFixture.Ref(revision);
        JsonObject body = PolicyFixture.ResultBody();
        body["duties"] = new JsonArray(occurrence, duplicate);
        RecordEnvelope attempt = f.BeginAttempt(builder: false);

        AssertFullJudgmentFinding(f, "result", body, "duty.duplicate", "duties[1].occurrence", subject: attempt.Reference);
    }

    private static RecordEnvelope Repository(PolicyFixture f) => f.Add("resource", new JsonObject
    {
        ["kind"] = "repository", ["name"] = "Test repository", ["role"] = "Source for test verification.",
        ["location"] = new JsonObject { ["github"] = "example/sample" }, ["visibility"] = "public",
    });

    private static void AssertFullJudgmentFinding(PolicyFixture f, string type, JsonObject body, string code, string field,
        RecordReference? subject = null, string? supersedes = null)
    {
        string root = f.Estate.Root;
        IReadOnlyList<RecordFile> files = EstateLoader.EnumerateRecordFiles(root);
        EstateModel before = EstateLoader.Load(root, files);
        var draft = new RecordDraft(type, f.Estate.OperatorId, PolicyFixture.Element(body), subject, supersedes,
            f.Estate.Clock.AddSeconds(1), Format: f.Estate.RecordFormat);
        RecordEnvelope unsigned = RecordAuthor.Create(draft).Envelope;
        string location = unsigned.ExpectedPath + "#body." + field;
        bool IsExpected(Finding finding) => finding.Severity == Severity.Error && finding.Code == code && finding.Location == location;
        Assert.DoesNotContain(EstateLoader.CheckRecordBody(before, unsigned), IsExpected);

        AuthoredRecord signed = RecordAuthor.Create(draft with { Id = unsigned.Id }, f.OperatorSigner);
        EstateModel after = EstateLoader.Load(root, [.. files, new RecordFile(signed.RelativePath, signed.Bytes)]);
        Assert.Contains(after.Findings, IsExpected);
    }
}
