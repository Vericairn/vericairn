// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json.Nodes;
using Vericairn.Records.Estate;
using Vericairn.Records.Model;
using Vericairn.Records.Tests;
using Vericairn.Runtime.Policies;

namespace Vericairn.Runtime.Tests;

/// <summary>
/// The flow-steps rule kind (the estate's design/flow.md § 5): an estate policy requires named steps of a flow; validation refuses a
/// configuration that turns one off, and the evaluation reports each run in scope that leaves one out anyway, from the records and the
/// template content the basis retains, never from the process's working directory.
/// </summary>
public class FlowPolicyEvaluationTests
{
    private static readonly string[] Review = ["review"];

    [Fact]
    public void ARunRecordedBeforeThePolicyThatLeavesOutARequiredStepIsUnmet()
    {
        using var run = new FlowFixture(run: false);
        RecordEnvelope before = run.Add("goal", run.GoalBody(new JsonObject { ["step.shorts"] = "off" }));
        JsonObject body = run.Policies.PolicyBody("song-steps", duty: false);
        body["rules"] = new JsonArray(new JsonObject
        {
            ["key"] = "steps", ["kind"] = FlowRuns.PolicyKind, ["version"] = 1,
            ["parameters"] = new JsonObject { ["flow"] = "example/song", ["steps"] = new JsonArray("shorts", "review-lyrics") },
        });
        run.Policies.Policy(body);
        RecordEnvelope after = run.Add("goal", run.GoalBody());
        EstateModel model = run.Estate.Load();
        Assert.False(model.Findings.HasErrors, string.Join('\n', model.Findings));

        List<PolicyFinding> findings = Evaluate(model, [PolicySources.Estate(model)]);
        Assert.Equal(4, findings.Count);
        PolicyFinding unmet = Assert.Single(findings, f => f.Verdict == "unmet");
        Assert.Equal((before.Id, "shorts"), (unmet.Target.Identity, unmet.Item));
        Assert.Equal("The run leaves out shorts: configured off by the goal's plan.", unmet.Actual);
        Assert.All(findings.Where(f => f.Target.Identity == after.Id), f => Assert.Equal("satisfied", f.Verdict));

        // Without the template content in the basis, the runs are unavailable, never an empty pass.
        List<PolicyFinding> blind = Evaluate(model, []);
        Assert.All(blind, f => Assert.Equal("unavailable", f.Verdict));
        Assert.Equal(4, blind.Count);
    }

    [Fact]
    public void TheRulesParametersNameAFlowAndDistinctSteps()
    {
        foreach (object parameters in new object[] { new { flow = "song", steps = new[] { "a" } }, new { flow = "example/song", steps = Array.Empty<string>() }, new { flow = "example/song", steps = new[] { "a", "a" } }, new { flow = "example/song" } })
        {
            PolicyEvaluation result = FoundationPolicyTests.Run(FoundationPolicyKinds.FlowSteps, parameters, []);
            Assert.Equal("unavailable", Assert.Single(result.Findings).Verdict);
        }

        Assert.Equal("satisfied", Assert.Single(FoundationPolicyTests.Run(FoundationPolicyKinds.FlowSteps, new { flow = "example/song", steps = Review }, []).Findings).Verdict);
    }

    private static List<PolicyFinding> Evaluate(EstateModel model, PolicyRepository[] repositories)
    {
        var basis = new PolicyBasis(model, "2026-10-14T00:00:00Z", PolicyTimeOrigin.Preview, repositories, implementations: FoundationPolicyTests.Kinds.Implementations);
        return PolicyEvaluator.Evaluate(basis, FoundationPolicyTests.Kinds).Findings.Where(f => f.Kind == FlowRuns.PolicyKind).ToList();
    }
}
