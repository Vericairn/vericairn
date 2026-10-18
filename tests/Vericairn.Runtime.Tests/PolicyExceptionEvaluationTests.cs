// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json.Nodes;
using Vericairn.Records.Estate;
using Vericairn.Records.Model;
using Vericairn.Records.Tests;
using Vericairn.Records.Types;
using Vericairn.Runtime.Policies;

namespace Vericairn.Runtime.Tests;

public class PolicyExceptionEvaluationTests
{
    [Fact]
    public void AGrantExceptsOnlyItsExactRuleAndTargetRetainingTheUnderlyingVerdict()
    {
        using var fixture = new PolicyFixture();
        RecordEnvelope policy = Policy(fixture, twoRules: true);
        RecordEnvelope order = Work(fixture);
        RecordEnvelope other = Work(fixture);
        RecordEnvelope first = Grant(fixture, policy, order);
        RecordEnvelope second = Grant(fixture, policy, order);
        PolicyEvaluation result = Evaluate(fixture);
        PolicyFinding excepted = Assert.Single(result.Findings, f => f.Disposition == "excepted");
        Assert.Equal("unmet", excepted.Verdict);
        Assert.Equal(order.Reference, excepted.Target.Record);
        Assert.Equal("placement", excepted.Rule);
        Assert.Equal(new[] { first.Reference, second.Reference }.OrderBy(r => r.Id, StringComparer.Ordinal), excepted.Exceptions);
        Assert.False(excepted.ReleaseHold);
        Assert.Contains(result.Findings, f => f.Rule == "age" && f.Target.Record == order.Reference && f.ReleaseHold);
        Assert.Contains(result.Findings, f => f.Target.Record == other.Reference && f.ReleaseHold);
        Assert.Equal(3, result.Holds);
        Assert.Equal(3, result.Warnings);
    }

    [Fact]
    public void ExpiryIsExclusiveAndRevocationTakesEffectImmediately()
    {
        using var fixture = new PolicyFixture();
        RecordEnvelope policy = Policy(fixture);
        RecordEnvelope order = Work(fixture);
        RecordEnvelope grant = Grant(fixture, policy, order);
        DateTimeOffset expiry = DutyRecords.Time(EstateModel.BodyString(grant, "expiresAt"))!.Value;
        Assert.Equal("excepted", Assert.Single(Evaluate(fixture, expiry.AddSeconds(-1)).Findings).Disposition);
        Assert.Equal("none", Assert.Single(Evaluate(fixture, expiry).Findings).Disposition);
        JsonObject revoked = PolicyFixture.Body(grant); revoked["state"] = "revoked";
        fixture.Add("policy-exception", revoked, subject: policy.Reference, supersedes: grant.Id);
        Assert.Equal("none", Assert.Single(Evaluate(fixture).Findings).Disposition);
    }

    [Fact]
    public void PolicyAndSubjectRevisionsNeedFreshGrants()
    {
        using var fixture = new PolicyFixture();
        RecordEnvelope policy = Policy(fixture);
        RecordEnvelope order = Work(fixture);
        Grant(fixture, policy, order);
        fixture.Add("work-order", PolicyFixture.Body(order), supersedes: order.Id);
        Assert.All(Evaluate(fixture).Findings, f => Assert.Empty(f.Exceptions));
        JsonObject revised = PolicyFixture.Body(policy); revised["title"] = "Edited policy title";
        fixture.Policy(revised, policy);
        Assert.All(Evaluate(fixture).Findings, f => Assert.Empty(f.Exceptions));
    }

    [Fact]
    public void LostDelegatedAuthorityEndsAnOutstandingException()
    {
        using var fixture = new PolicyFixture();
        RecordEnvelope policy = Policy(fixture);
        RecordEnvelope order = Work(fixture);
        var bounds = new JsonObject
        {
            ["scope"] = Scopes.GrantPolicyExceptions, ["actions"] = new JsonArray("grant", "revoke"), ["policies"] = new JsonArray("placement-policy"),
            ["levels"] = new JsonArray("refuse"), ["rules"] = new JsonArray("placement"), ["targets"] = new JsonArray(Target(order)), ["maxExceptionDays"] = 30,
        };
        RecordEnvelope authority = fixture.Grant(Scopes.GrantPolicyExceptions, bounds);
        Grant(fixture, policy, order, authority);
        Assert.Equal("excepted", Assert.Single(Evaluate(fixture).Findings).Disposition);
        JsonObject removed = PolicyFixture.Body(authority); removed["scope"] = new JsonArray(); removed.Remove("bounds");
        fixture.Add("authority", removed, supersedes: authority.Id);
        Assert.Equal("none", Assert.Single(Evaluate(fixture).Findings).Disposition);
    }

    [Fact]
    public void InvalidUnsignedOrOverlongGrantsCannotQuietAnyErrorOrUnavailableFinding()
    {
        using var fixture = new PolicyFixture();
        RecordEnvelope policy = Policy(fixture);
        RecordEnvelope order = Work(fixture);
        JsonObject invalid = Body(fixture, order);
        invalid["expiresAt"] = RecordParser.FormatTimestamp(fixture.Estate.Clock.AddDays(31));
        fixture.Add("policy-exception", invalid, subject: policy.Reference, sign: false);
        PolicyEvaluation result = Evaluate(fixture);
        Assert.True(result.Errors > 0);
        Assert.Equal("unavailable", Assert.Single(result.Findings).Verdict);
        Assert.Empty(result.Findings[0].Exceptions);
    }

    [Fact]
    public void FileExceptionsPinRepositoryPathAndExactBytes()
    {
        using var fixture = new PolicyFixture();
        JsonObject definition = fixture.PolicyBody("revision", duty: false);
        definition["appliesTo"] = new JsonObject { ["kind"] = "repository", ["repository"] = fixture.Estate.Estate.Id, ["selectors"] = new JsonArray("docs/**") };
        definition["rules"] = new JsonArray(new JsonObject { ["key"] = "placement", ["kind"] = FoundationPolicyKinds.RevisionLine, ["version"] = 1, ["parameters"] = new JsonObject { ["selectors"] = new JsonArray("docs/**"), ["style"] = "estate-design/1" } });
        RecordEnvelope policy = fixture.Policy(definition);
        fixture.Estate.SetClock(DutyRecords.Time("2026-10-13T14:00:00Z")!.Value);
        PolicyFile file = PolicyBasisTests.File("docs/test.md", "# Title\nMissing revision.");
        JsonObject body = Body(fixture, policy);
        body["targets"] = new JsonArray(new JsonObject { ["kind"] = "file", ["repository"] = fixture.Estate.Estate.Id, ["path"] = file.Path, ["hash"] = file.Hash });
        fixture.Add("policy-exception", body, subject: policy.Reference);
        PolicyEvaluation EvaluateFile(PolicyFile value) => PolicyEvaluator.Evaluate(new(fixture.Estate.Load(), RecordParser.FormatTimestamp(fixture.Estate.Clock), PolicyTimeOrigin.Preview,
            [new(fixture.Estate.Estate.Id, "working-tree", PolicyBasis.RepositoryContract, true, [value])], implementations: FoundationPolicyTests.Kinds.Implementations), FoundationPolicyTests.Kinds);
        Assert.Equal("excepted", Assert.Single(EvaluateFile(file).Findings).Disposition);
        Assert.Equal("none", Assert.Single(EvaluateFile(PolicyBasisTests.File(file.Path, "# Changed\nStill missing." )).Findings).Disposition);
        Assert.Equal("none", Assert.Single(EvaluateFile(file with { Path = "docs/renamed.md" }).Findings).Disposition);
        Assert.Equal("unavailable", Assert.Single(EvaluateFile(file with { Available = false }).Findings).Verdict);
    }

    [Fact]
    public void ReleaseExceptionsCoverOnlyThePinnedSubmissionAndCannotBeReusedForAnotherCommit()
    {
        using var fixture = new PolicyFixture();
        RecordEnvelope policy = Policy(fixture);
        RecordEnvelope order = Work(fixture);
        RecordEnvelope product = fixture.Add("product", new JsonObject
        {
            ["name"] = "Example", ["slug"] = "example", ["summary"] = "A fixture product.",
            ["editions"] = new JsonArray(new JsonObject { ["name"] = "Community", ["slug"] = "community", ["summary"] = "Fixture edition." }),
            ["surfaces"] = new JsonArray(new JsonObject { ["name"] = "CLI", ["slug"] = "cli", ["summary"] = "Fixture surface." }),
        });
        string commit = new('1', 40);
        RecordEnvelope release = fixture.Add("release", new JsonObject
        {
            ["version"] = "1.0.0", ["repository"] = "Example/example", ["commit"] = commit, ["status"] = "candidate", ["artifacts"] = new JsonArray(),
            ["recordFormat"] = new JsonObject { ["reads"] = new JsonArray(1, 2, 3), ["writes"] = 3 }, ["evidence"] = new JsonArray(), ["qualified"] = new JsonArray(), ["signing"] = "Fixture only", ["limitations"] = new JsonArray(),
        }, subject: new("product", product.Id, null));
        JsonObject body = Body(fixture, order);
        body["targets"]![0]!["submission"] = new JsonObject { ["release"] = PolicyFixture.Ref(release), ["product"] = product.Id, ["version"] = "1.0.0" };
        fixture.Add("policy-exception", body, subject: policy.Reference);
        fixture.AssertValid();
        var submission = new PolicySubmission(release.Reference, product.Id, "1.0.0", commit);
        PolicyEvaluation EvaluateSubmission(PolicySubmission value) => PolicyEvaluator.Evaluate(new(fixture.Estate.Load(), RecordParser.FormatTimestamp(fixture.Estate.Clock), PolicyTimeOrigin.Submission, implementations: FoundationPolicyTests.Kinds.Implementations, submission: value), FoundationPolicyTests.Kinds);
        Assert.Equal("excepted", Assert.Single(EvaluateSubmission(submission).Findings).Disposition);
        Assert.Equal("none", Assert.Single(EvaluateSubmission(submission with { Version = "1.0.1" }).Findings).Disposition);
        Assert.Equal("none", Assert.Single(EvaluateSubmission(submission with { Commit = new string('2', 40) }).Findings).Disposition);
        Assert.Equal("none", Assert.Single(Evaluate(fixture).Findings).Disposition);
    }

    internal static RecordEnvelope Policy(PolicyFixture fixture, bool twoRules = false)
    {
        JsonObject definition = fixture.PolicyBody("placement-policy", duty: false);
        var rules = new JsonArray(new JsonObject { ["key"] = "placement", ["kind"] = FoundationPolicyKinds.Placement, ["version"] = 1, ["parameters"] = new JsonObject { ["backlogs"] = new JsonArray() } });
        if (twoRules) rules.Add(new JsonObject { ["key"] = "age", ["kind"] = FoundationPolicyKinds.Staleness, ["version"] = 1, ["parameters"] = new JsonObject { ["class"] = "planned-without-release", ["maxAgeDays"] = 0 } });
        definition["rules"] = rules;
        RecordEnvelope policy = fixture.Policy(definition);
        fixture.Estate.SetClock(DutyRecords.Time("2026-10-13T14:00:00Z")!.Value);
        return policy;
    }

    internal static RecordEnvelope Work(PolicyFixture fixture) => fixture.Add("work-order", new JsonObject
    {
        ["key"] = "TASK-" + fixture.Estate.Load().OfType("work-order").Count().ToString(System.Globalization.CultureInfo.InvariantCulture), ["kind"] = "ordinary", ["title"] = "Test work", ["intent"] = "Test placement.", ["scope"] = new JsonArray("test"), ["exitQuestion"] = "Placed?", ["inputs"] = new JsonArray(), ["deliverables"] = new JsonArray("evidence"),
    });

    internal static JsonObject Target(RecordEnvelope record) => new() { ["kind"] = "record", ["record"] = PolicyFixture.Ref(record) };
    internal static JsonObject Body(PolicyFixture fixture, RecordEnvelope target, RecordEnvelope? authority = null) => new()
    {
        ["rule"] = "placement", ["targets"] = new JsonArray(Target(target)), ["reason"] = "A finite delay.", ["expiresAt"] = RecordParser.FormatTimestamp(fixture.Estate.Clock.AddDays(1)), ["state"] = "granted",
        ["basis"] = authority is null ? new JsonObject { ["decision"] = PolicyFixture.Ref(fixture.Decision) } : new JsonObject { ["decision"] = PolicyFixture.Ref(fixture.Decision), ["authority"] = PolicyFixture.Ref(authority) },
    };

    private static RecordEnvelope Grant(PolicyFixture fixture, RecordEnvelope policy, RecordEnvelope target, RecordEnvelope? authority = null) => fixture.Add("policy-exception", Body(fixture, target, authority), builder: authority is not null, subject: policy.Reference);
    internal static PolicyEvaluation Evaluate(PolicyFixture fixture, DateTimeOffset? at = null) => PolicyEvaluator.Evaluate(new(fixture.Estate.Load(), RecordParser.FormatTimestamp(at ?? fixture.Estate.Clock), PolicyTimeOrigin.Preview, implementations: FoundationPolicyTests.Kinds.Implementations), FoundationPolicyTests.Kinds);
}
