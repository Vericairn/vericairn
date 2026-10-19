// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using System.Text.Json.Nodes;
using Vericairn.Protocol;
using Vericairn.Records.Tests;
using Vericairn.Runtime.Service;

namespace Vericairn.Runtime.Tests;

public sealed class PolicyRequirementViewTests
{
    [Fact]
    public void DecisionRequirementsStayVisibleUntilTheExactApprovedChangeIsRecorded()
    {
        using var f = new PolicyFixture();
        JsonObject prose = f.DecisionBody();
        prose["policies"] = new JsonArray(new JsonObject { ["key"] = "design", ["action"] = "declare", ["policy"] = "future", ["change"] = "Propose a definition for review." });
        f.Add("decision", prose);
        JsonObject definition = f.PolicyBody();
        var approval = f.Approve(definition);
        f.AssertValid();
        using var api = EstateService.Open(f.Estate.Root);
        int before = api.Session.Load().Records.Count;
        PolicyResponse pending = api.Policies();
        Assert.Equal(2, pending.Requirements!.Count);
        Assert.All(pending.Requirements, r => Assert.Equal("pending", r.State));
        Assert.Equal(0, pending.Holds);
        Assert.Contains(api.Check().Findings, finding => finding.Code == "policy.requirement-pending");
        string expected = JsonSerializer.Serialize(pending, ProtocolJson.Options);
        Assert.Equal(expected, api.Status().Json.GetProperty("policies").GetRawText());
        Assert.Equal(expected, api.Progress().Json.GetProperty("policies").GetRawText());
        Assert.Contains("pending; declare maintenance", pending.Text, StringComparison.Ordinal);
        Assert.Equal(before, api.Session.Load().Records.Count);
        definition["basis"] = new JsonArray(new JsonObject { ["decision"] = PolicyFixture.Ref(approval), ["requirement"] = "definition" });
        var declaration = f.Add("estate-policy", definition);
        f.AssertValid();
        PolicyResponse recorded = api.Policies();
        PolicyRequirementDto implemented = Assert.Single(recorded.Requirements!, r => r.State == "recorded");
        Assert.Equal(approval.ComputedHash, implemented.Decision.Hash);
        Assert.Equal(declaration.ComputedHash, implemented.Recorded!.Hash);
        Assert.Equal("pending", Assert.Single(recorded.Requirements!, r => r.Key == "design").State);
        Assert.Equal(0, recorded.Holds);
        Assert.Contains("Duties owned by " + f.Estate.OperatorId, api.PolicyEvaluate("2026-10-13T13:00:00Z").Text, StringComparison.Ordinal);
    }

    [Fact]
    public void ADeclarationWithTheSameKeyCannotSatisfyAnotherExactDecision()
    {
        using var f = new PolicyFixture();
        JsonObject definition = f.PolicyBody();
        var required = f.Approve(definition);
        definition["reason"] = "A separately approved definition.";
        f.Policy(definition);
        f.AssertValid();
        using var api = EstateService.Open(f.Estate.Root);
        PolicyRequirementDto pending = Assert.Single(api.Policies().Requirements!, r => r.Decision.Id == required.Id);
        Assert.Equal("pending", pending.State);
        Assert.Null(pending.Recorded);
    }

    [Fact]
    public void ConflictingPolicyKeysRemainReadableAndUnavailable()
    {
        using var f = new PolicyFixture();
        f.Policy();
        f.Policy();
        using var api = EstateService.Open(f.Estate.Root);
        int before = api.Session.Load().Records.Count;
        PolicyResponse response = api.Policy("maintenance")!;
        Assert.Equal("conflict", response.Problem!.Code);
        Assert.Equal(2, response.Policies.Count);
        Assert.False(response.Complete);
        Assert.All(response.Findings, finding => Assert.Equal("unavailable", finding.Verdict));
        Assert.Equal(before, api.Session.Load().Records.Count);
    }
}
