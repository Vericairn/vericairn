// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using System.Text.Json.Nodes;
using Vericairn.Records.Model;
using Vericairn.Runtime.Policies;
using static Vericairn.Runtime.Tests.FoundationPolicyTests;

namespace Vericairn.Runtime.Tests;

public class PolicyActivationTests
{
    private static readonly string[] Paths = ["docs/*.md"];
    private static RecordEnvelope Future(string kind, object parameters, object? selection = null)
    {
        RecordEnvelope policy = P(kind, parameters, selection: selection);
        JsonObject body = JsonNode.Parse(policy.Body.GetRawText())!.AsObject(); body["startsAt"] = "2026-01-03T00:00:00Z";
        return R(policy.Type, policy.Id, body);
    }

    [Fact]
    public void FutureActivationCannotHideAnUnavailableKindOrImplementationPin()
    {
        RecordEnvelope unknown = Future("example.policy/missing", new { });
        PolicyFinding missing = Assert.Single(Evaluate([unknown]).Findings);
        Assert.Equal("unavailable", missing.Verdict);
        Assert.False(missing.Enforced); Assert.False(missing.ReleaseHold);
        Assert.True(Assert.Single(Evaluate([unknown], "2026-01-03T00:00:00Z").Findings).ReleaseHold);
        RecordEnvelope known = Future(FoundationPolicyKinds.Placement, new { backlogs = Array.Empty<object>() });
        PolicyBasis basis = Basis([known]);
        PolicyFinding unpinned = Assert.Single(PolicyEvaluator.Evaluate(new(basis.Model, basis.AsOf, basis.Origin), Kinds).Findings);
        Assert.Equal("unavailable", unpinned.Verdict); Assert.False(unpinned.Enforced);
        Assert.Equal("unavailable", Assert.Single(PolicyEvaluator.Evaluate(basis).Findings).Verdict);
    }

    [Fact]
    public void AValidFutureRuleIsScheduledAndAppliesExactlyAtActivation()
    {
        RecordEnvelope policy = Future(FoundationPolicyKinds.Placement, new { backlogs = Array.Empty<object>() });
        PolicyFinding waiting = Assert.Single(Evaluate([policy, Order()], "2026-01-02T23:59:59Z").Findings);
        Assert.Equal("scheduled", waiting.Disposition); Assert.False(waiting.Enforced);
        Assert.Equal(0, waiting.SubjectCount);
        PolicyFinding due = Assert.Single(Evaluate([policy, Order()], "2026-01-03T00:00:00Z").Findings);
        Assert.Equal("unmet", due.Verdict); Assert.True(due.Enforced); Assert.True(due.ReleaseHold);
    }

    [Theory]
    [InlineData(FoundationPolicyKinds.Placement, "{\"backlogs\":[],\"unknown\":true}")]
    [InlineData(FoundationPolicyKinds.Staleness, "{\"class\":\"open-ask\",\"maxAgeDays\":-1}")]
    [InlineData(FoundationPolicyKinds.Staleness, "{\"class\":\"result-awaiting-review\",\"maxAgeDays\":0,\"reviewRequirement\":\"guess\"}")]
    [InlineData(FoundationPolicyKinds.KeyFamily, "{\"families\":[{\"prefix\":\".*\",\"separator\":\"-\",\"digits\":2,\"suffixes\":[\"\"]}],\"createdFrom\":\"2026-01-01T00:00:00Z\"}")]
    [InlineData(FoundationPolicyKinds.Consistency, "{\"cases\":[\"guess\"]}")]
    public void StrictKindParametersRemainErrorsBeforeActivation(string kind, string parameters)
    {
        PolicyEvaluation result = Evaluate([Future(kind, JsonSerializer.Deserialize<JsonElement>(parameters))]);
        Assert.Equal(1, result.Errors);
        Assert.Equal("unavailable", Assert.Single(result.Findings).Verdict);
        Assert.False(result.Findings[0].Enforced);
    }

    [Fact]
    public void RevisionParametersAreCheckedBeforeSourceBytesAreNeeded()
    {
        var selection = new { kind = "repository", repository = "estate", selectors = Paths };
        RecordEnvelope invalid = Future(FoundationPolicyKinds.RevisionLine, new { selectors = Paths, style = "unknown" }, selection);
        Assert.Equal(1, Evaluate([invalid]).Errors);
        RecordEnvelope valid = Future(FoundationPolicyKinds.RevisionLine, new { selectors = Paths, style = "estate-design/1" }, selection);
        Assert.Equal("scheduled", Assert.Single(Evaluate([valid]).Findings).Disposition);
        Assert.Equal("unavailable", Assert.Single(Evaluate([valid], "2026-01-03T00:00:00Z").Findings).Verdict);
    }
}
