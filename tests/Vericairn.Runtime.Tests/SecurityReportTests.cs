// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Vericairn.Protocol;
using Vericairn.Records.Estate;
using Vericairn.Records.Model;
using Vericairn.Runtime.Providers;

namespace Vericairn.Runtime.Tests;

[Collection("policy-sources")]
public sealed class SecurityReportTests
{
    [Fact]
    public void WithoutADeclarationNothingIsReadAndNoReleaseWaits()
    {
        using var f = new SecurityReportFixture(declare: false);
        f.Api.SecurityReportsScheduled();
        ReleaseResponse check = f.Api.ReleaseCheck(f.Request);
        Assert.True(check.Ok, check.Message);
        Assert.Empty(f.Gh.Calls);
        Assert.DoesNotContain(f.Api.AttentionGet(f.Operator).Items, n => n.SubjectType is "security-report" or "security-reports");
        Assert.False(f.Api.SecurityReportsPoll(new(f.Operator)).Held);
    }

    [Fact]
    public void ANewReportIsIncidentAttentionAfterOnePollAndHoldsEveryReleaseUntilTriaged()
    {
        using var f = new SecurityReportFixture();
        f.Api.SecurityReportsScheduled();

        AttentionNotice[] notices = f.Api.AttentionGet(f.Operator).Items.Where(n => n.SubjectType == "security-report").ToArray();
        Assert.Equal(3, notices.Length);
        AttentionNotice triage = Assert.Single(notices, n => n.SubjectId == FakeGh.Repository + ":" + FakeGh.Triaged);
        Assert.Equal("incident", triage.Kind);
        Assert.Equal("triage", triage.State);
        Assert.Contains("severity high", triage.Why, StringComparison.Ordinal);
        Assert.Contains(FakeGh.Triaged, triage.Subject, StringComparison.Ordinal);
        Assert.Equal(new AttentionReference("resource", f.Resource.Id, f.Resource.ComputedHash), triage.Target);
        Assert.Contains("security-reports triage " + FakeGh.Triaged, triage.NextAction, StringComparison.Ordinal);

        ReleaseResponse check = f.Api.ReleaseCheck(f.Request);
        Assert.False(check.Ok);
        string[] held = check.Check!.Objections.Where(o => o.Code == "release.security-report").Select(o => o.Message).ToArray();
        Assert.Equal(3, held.Length);
        Assert.Contains(held, m => m.Contains(FakeGh.Triaged, StringComparison.Ordinal) && m.Contains("state triage, severity high", StringComparison.Ordinal));
        ReleaseResponse refused = f.Api.ReleaseSubmit(f.Request);
        Assert.False(refused.Ok);
        Assert.Contains("release.security-report", refused.Message, StringComparison.Ordinal);
        Assert.Equal(0, f.Releases.Calls);
        Assert.Empty(f.Api.Records("operation"));

        f.TriageAll();
        Assert.DoesNotContain(f.Api.AttentionGet(f.Operator).Items, n => n.SubjectType == "security-report");
        ReleaseResponse passed = f.Api.ReleaseCheck(f.Request);
        Assert.True(passed.Ok, passed.Message);
        Assert.Equal(0, f.Api.Validate().Errors);
    }

    [Fact]
    public void AChangedStateOrSeverityAfterTriageNeedsTriageAgain()
    {
        using var f = new SecurityReportFixture();
        f.TriageAll();
        Assert.True(f.Api.ReleaseCheck(f.Request).Ok);

        f.Gh.Advisories[0]["severity"] = "critical";
        f.Clock.Now = f.Clock.Now.AddMinutes(5);
        f.Api.SecurityReportsScheduled();
        AttentionNotice raised = Assert.Single(f.Api.AttentionGet(f.Operator).Items, n => n.SubjectType == "security-report");
        Assert.Contains("severity critical", raised.Why, StringComparison.Ordinal);
        ReleaseObjection objection = Assert.Single(f.Api.ReleaseCheck(f.Request).Check!.Objections, o => o.Code == "release.security-report");
        Assert.Contains(FakeGh.Triaged, objection.Message, StringComparison.Ordinal);

        Assert.True(f.Triage(FakeGh.Triaged).Ok);
        f.Gh.Advisories[1]["state"] = "published";
        f.Clock.Now = f.Clock.Now.AddMinutes(5);
        Assert.Contains(f.Api.ReleaseCheck(f.Request).Check!.Objections, o => o.Code == "release.security-report" && o.Message.Contains(FakeGh.Drafted, StringComparison.Ordinal));
        Assert.True(f.Triage(FakeGh.Drafted, "no-action", reason: "Published after the fix shipped.").Ok);
        Assert.True(f.Api.ReleaseCheck(f.Request).Ok);
    }

    [Fact]
    public void NoAdvisoryTextReachesARecordTheLiveStoreAttentionStatusOrARefusal()
    {
        using var f = new SecurityReportFixture();
        var seen = new StringBuilder();
        f.Api.SecurityReportsScheduled();
        seen.Append(JsonSerializer.Serialize(f.Api.AttentionGet(f.Operator), ProtocolJson.Options));
        StatusResponse status = f.Api.Status();
        seen.Append(status.Text).Append(status.Json.GetRawText());
        seen.Append(JsonSerializer.Serialize(f.Api.ReleaseCheck(f.Request), ProtocolJson.Options));
        seen.Append(JsonSerializer.Serialize(f.Api.ReleaseSubmit(f.Request), ProtocolJson.Options));
        seen.Append(JsonSerializer.Serialize(f.Api.SecurityReportsPoll(new(f.Operator)), ProtocolJson.Options));
        seen.Append(JsonSerializer.Serialize(f.Triage(FakeGh.Triaged, "no-action", reason: "Not reachable in any shipped configuration."), ProtocolJson.Options));
        seen.Append(f.Api.Session.Live.Meta("security-reports"));
        f.Api.Dispose();
        foreach (string file in Directory.EnumerateFiles(f.Records.Estate.Root, "*", SearchOption.AllDirectories).Where(p => !p.Contains(Path.DirectorySeparatorChar + ".git" + Path.DirectorySeparatorChar, StringComparison.Ordinal)))
            seen.Append(Encoding.UTF8.GetString(File.ReadAllBytes(file)));
        foreach (string file in Directory.EnumerateFiles(Path.GetDirectoryName(f.Api.Session.Live.Path)!))
            seen.Append(Encoding.UTF8.GetString(File.ReadAllBytes(file)));

        string all = seen.ToString();
        Assert.Contains(FakeGh.Triaged, all, StringComparison.Ordinal);
        Assert.All(FakeGh.OutsideText, text => Assert.DoesNotContain(text, all, StringComparison.Ordinal));
        RecordEnvelope triage = Assert.Single(f.Records.Estate.Load().Current("observation"), o => EstateModel.BodyString(o, "kind") == "triage");
        Assert.Equal(["advisory", "contentHash", "disposition", "reason", "repository"], triage.Body.GetProperty("facts").EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal));
        Assert.Equal(f.Resource.Reference, triage.Subject);
    }

    [Theory]
    [InlineData("no-login", "not logged in")]
    [InlineData("network", "could not be read completely")]
    [InlineData("rate", "rate limit")]
    [InlineData("not-started", "could not be started")]
    public void AReaderThatCannotCompleteHoldsReleasesAndSaysWhy(string failure, string reason)
    {
        using var f = new SecurityReportFixture();
        f.Gh.Failure = failure;
        f.Api.SecurityReportsScheduled();
        AttentionNotice unconfirmed = Assert.Single(f.Api.AttentionGet(f.Operator).Items, n => n.SubjectType == "security-reports");
        Assert.Equal("failed check", unconfirmed.Kind);
        Assert.Contains(reason, unconfirmed.Why, StringComparison.Ordinal);
        ReleaseResponse check = f.Api.ReleaseCheck(f.Request);
        Assert.False(check.Ok);
        ReleaseObjection objection = Assert.Single(check.Check!.Objections, o => o.Code == "release.security-reports-unavailable");
        Assert.Contains(reason, objection.Message, StringComparison.Ordinal);
        Assert.Contains("security-reports poll", objection.NextAction, StringComparison.Ordinal);
        Assert.False(f.Api.ReleaseSubmit(f.Request).Ok);
        Assert.Equal(0, f.Releases.Calls);
        Assert.All(FakeGh.OutsideText, text => Assert.DoesNotContain(text, JsonSerializer.Serialize(check, ProtocolJson.Options), StringComparison.Ordinal));
    }

    [Fact]
    public void AnIncompleteReadKeepsEarlierRevisionsHeldAndAMissingReaderRefuses()
    {
        using var f = new SecurityReportFixture();
        f.Api.SecurityReportsScheduled();
        f.Gh.Failure = "network";
        f.Clock.Now = f.Clock.Now.AddMinutes(5);
        f.Api.SecurityReportsScheduled();
        Assert.Equal(3, f.Api.AttentionGet(f.Operator).Items.Count(n => n.SubjectType == "security-report"));

        using var missing = new SecurityReportFixture(provider: "uninstalled-reader");
        ReleaseObjection objection = Assert.Single(missing.Api.ReleaseCheck(missing.Request).Check!.Objections, o => o.Code == "release.security-reports-unavailable");
        Assert.Contains("no implementation", objection.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AStaleReadIsUnconfirmedAndTheGateReadsAgain()
    {
        using var f = new SecurityReportFixture();
        f.TriageAll();
        int calls = f.Gh.Calls.Count;
        Assert.True(f.Api.ReleaseCheck(f.Request).Ok);
        Assert.Equal(calls, f.Gh.Calls.Count);
        f.Clock.Now = f.Clock.Now.AddMinutes(20);
        Assert.Contains(f.Api.AttentionGet(f.Operator).Items, n => n.SubjectType == "security-reports" && n.Why.Contains("no read has completed since", StringComparison.Ordinal));
        Assert.True(f.Api.ReleaseCheck(f.Request).Ok);
        Assert.Equal(calls + 3, f.Gh.Calls.Count);
        // A second check within the minute reuses the first check's read, which stays in memory and leaves the live store as it was.
        Assert.True(f.Api.ReleaseCheck(f.Request).Ok);
        Assert.Equal(calls + 3, f.Gh.Calls.Count);
        Assert.Contains(f.Api.AttentionGet(f.Operator).Items, n => n.SubjectType == "security-reports");
        f.Api.SecurityReportsScheduled();
        Assert.DoesNotContain(f.Api.AttentionGet(f.Operator).Items, n => n.SubjectType == "security-reports");
    }

    [Fact]
    public void OnlyTheOperatorSeesOrTriagesReportsAndOthersAreAnsweredAlike()
    {
        using var f = new SecurityReportFixture();
        using var empty = new SecurityReportFixture();
        empty.Gh.Advisories.Clear();
        empty.Gh.Failure = "network";

        SecurityReportsResponse denied = f.Api.SecurityReportsPoll(new(f.Builder));
        SecurityReportsResponse deniedEmpty = empty.Api.SecurityReportsPoll(new(empty.Builder));
        Assert.False(denied.Ok);
        Assert.Equal(JsonSerializer.Serialize(denied, ProtocolJson.Options), JsonSerializer.Serialize(deniedEmpty, ProtocolJson.Options));
        Assert.Empty(f.Gh.Calls);

        string hash = f.Api.SecurityReportsPoll(new(f.Operator)).Reports.Single(r => r.Advisory == FakeGh.Triaged).ContentHash;
        SecurityReportsResponse triage = f.Api.SecurityReportsTriage(new(f.Builder, FakeGh.Triaged, FakeGh.Repository, hash, "security"));
        SecurityReportsResponse triageUnknown = f.Api.SecurityReportsTriage(new(f.Builder, "GHSA-x2x2-x2x2-x2x2", FakeGh.Repository, hash, "security"));
        Assert.Equal(JsonSerializer.Serialize(triage, ProtocolJson.Options), JsonSerializer.Serialize(triageUnknown, ProtocolJson.Options));
        Assert.Equal("denied", triage.Problem!.Code);
        Assert.DoesNotContain(f.Records.Estate.Load().Current("observation"), o => EstateModel.BodyString(o, "kind") == "triage");

        Assert.DoesNotContain(f.Api.AttentionGet(f.Builder).Items, n => n.SubjectType is "security-report" or "security-reports");
        ReleaseResponse builderCheck = f.Api.ReleaseCheck(f.Request with { By = f.Builder });
        ReleaseResponse builderCheckEmpty = empty.Api.ReleaseCheck(empty.Request with { By = empty.Builder });
        ReleaseObjection held = Assert.Single(builderCheck.Check!.Objections, o => o.Code.StartsWith("release.security", StringComparison.Ordinal));
        ReleaseObjection heldEmpty = Assert.Single(builderCheckEmpty.Check!.Objections, o => o.Code.StartsWith("release.security", StringComparison.Ordinal));
        Assert.Equal(held, heldEmpty);
        Assert.Equal("release.security-reports", held.Code);
        Assert.DoesNotContain("GHSA", held.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TriageNamesTheExactObservedRevision()
    {
        using var f = new SecurityReportFixture();
        SecurityReportsResponse poll = f.Api.SecurityReportsPoll(new(f.Operator));
        Assert.True(poll.Held);
        SecurityReportRow row = poll.Reports.Single(r => r.Advisory == FakeGh.Triaged);
        Assert.Equal("conflict", f.Api.SecurityReportsTriage(new(f.Operator, FakeGh.Triaged, FakeGh.Repository, "sha256:" + new string('0', 64), "security")).Problem!.Code);
        Assert.Equal("not-found", f.Api.SecurityReportsTriage(new(f.Operator, "GHSA-x2x2-x2x2-x2x2", FakeGh.Repository, row.ContentHash, "security")).Problem!.Code);
        Assert.Equal("refused", f.Api.SecurityReportsTriage(new(f.Operator, FakeGh.Triaged, FakeGh.Repository, row.ContentHash, "no-action")).Problem!.Code);
        Assert.Equal("refused", f.Api.SecurityReportsTriage(new(f.Operator, FakeGh.Triaged, FakeGh.Repository, row.ContentHash, "ignored")).Problem!.Code);
        SecurityReportsResponse recorded = f.Api.SecurityReportsTriage(new(f.Operator, FakeGh.Triaged, FakeGh.Repository.ToUpperInvariant(), row.ContentHash, "duplicate"));
        Assert.True(recorded.Ok, recorded.Message);
        Assert.Equal(recorded.Triage!.Id, recorded.Reports.Single(r => r.Advisory == FakeGh.Triaged).Triage);
        Assert.Equal(0, f.Api.Validate().Errors);
    }

    [Fact]
    public void TheDeclarationNamesRepositoryResourcesOnGitHubAndOnlyTheOperatorChangesIt()
    {
        using var f = new SecurityReportFixture();
        JsonObject changed = f.ProductBody(null);
        RecordEnvelope removed = f.Records.Add("product", changed, builder: true, supersedes: f.Product.Id);
        EstateModel model = f.Records.Estate.Load();
        Assert.Contains(model.Findings, x => x.Code == "product.security-reports-author" && x.Location.Contains(removed.Id, StringComparison.Ordinal));

        using var g = new SecurityReportFixture(declare: false);
        RecordEnvelope service = g.Records.Add("resource", new JsonObject
        {
            ["kind"] = "service", ["name"] = "Site", ["role"] = "Website.", ["location"] = new JsonObject { ["url"] = "https://example.invalid" }, ["visibility"] = "public",
        });
        JsonObject body = g.ProductBody("github-security-advisories");
        body["securityReports"]!["repositories"] = new JsonArray(service.Id, service.Id);
        RecordEnvelope invalid = g.Records.Add("product", body, supersedes: g.Product.Id);
        string[] codes = g.Records.Estate.Load().Findings.Where(x => x.Location.Contains(invalid.Id, StringComparison.Ordinal)).Select(x => x.Code).ToArray();
        Assert.Contains("product.security-reports", codes);
    }

    [Fact]
    public void OnlyTheOperatorsTriageRecordStandsAndItCarriesNoText()
    {
        using var f = new SecurityReportFixture();
        string hash = f.Api.SecurityReportsPoll(new(f.Operator)).Reports.Single(r => r.Advisory == FakeGh.Triaged).ContentHash;
        JsonObject Body(JsonObject facts) => new()
        {
            ["kind"] = "triage", ["asOf"] = "2026-11-02T03:00:00Z", ["summary"] = "Triaged.", ["facts"] = facts, ["limitations"] = new JsonArray(),
        };
        JsonObject Facts() => new() { ["advisory"] = FakeGh.Triaged, ["repository"] = FakeGh.Repository, ["contentHash"] = hash, ["disposition"] = "security" };

        RecordEnvelope builder = f.Records.Add("observation", Body(Facts()), builder: true, subject: f.Resource.Reference);
        JsonObject extra = Facts();
        extra["summary"] = FakeGh.Summary;
        RecordEnvelope copied = f.Records.Add("observation", Body(extra), subject: f.Resource.Reference);
        JsonObject wrongRepository = Facts();
        wrongRepository["repository"] = "Example/other";
        RecordEnvelope elsewhere = f.Records.Add("observation", Body(wrongRepository), subject: f.Resource.Reference);
        EstateModel model = f.Records.Estate.Load();
        string[] Codes(RecordEnvelope record) => model.Findings.Where(x => x.Location.Contains(record.Id, StringComparison.Ordinal)).Select(x => x.Code).ToArray();
        Assert.Contains("triage.author", Codes(builder));
        Assert.Contains("triage.facts", Codes(copied));
        Assert.Contains("triage.subject", Codes(elsewhere));
        // An agent's triage never clears the hold, even before the estate check refuses it.
        Assert.Contains(f.Api.ReleaseCheck(f.Request).Check!.Objections, o => o.Code == "release.security-report" && o.Message.Contains(FakeGh.Triaged, StringComparison.Ordinal));
    }

    [Fact]
    public void TheMemoryReaderDrivesTheSameGate()
    {
        var reader = new MemorySecurityAdvisoryReader();
        using var f = new SecurityReportFixture(reader: reader, provider: reader.Provider);
        reader.Set(FakeGh.Repository, new(FakeGh.Triaged, "triage", "critical", "sha256:" + new string('b', 64)));
        Assert.Contains(f.Api.ReleaseCheck(f.Request).Check!.Objections, o => o.Code == "release.security-report");
        Assert.Equal(1, reader.Reads);
        f.TriageAll();
        Assert.True(f.Api.ReleaseCheck(f.Request).Ok);
    }
}
