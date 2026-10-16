// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using System.Text.Json.Nodes;
using Vericairn.Protocol;
using Vericairn.Records.Estate;
using Vericairn.Records.Model;

namespace Vericairn.Runtime.Tests;

[Collection("home")]
public class RunPolicyIntegrationTests
{
    [Theory]
    [InlineData("inventory")]
    [InlineData("provenance")]
    [InlineData("adapter")]
    [InlineData("requested")]
    public void AnImportedIntentCannotMisstateItsInventoryOrItsPinnedSelection(string change)
    {
        using var run = new RunEstate();
        Roster(run, """{"choices":{"effort":"high"}}""");
        RunResponse started = run.Start("claude-code", "claude", run.Report("original"));
        Assert.True(started.Ok, started.Message);
        run.Settled(started.Run);
        AgentRuns.Run original = AgentRuns.Find(run.Estate.Load(), started.Run)!;
        JsonObject body = JsonNode.Parse(original.Intent.Body.GetRawText())!.AsObject();
        body["identity"] = "agent-run:" + Vericairn.Records.Identity.Ulid.NewUlid().Value;
        body["cap"]!["wallMinutes"] = 1;
        JsonNode execution = body["execution"]!;
        switch (change)
        {
            case "inventory": execution["environment"]!["inventoryHash"] = Vericairn.Records.Hashing.ContentHash.Sha256("different"u8); break;
            case "provenance": execution["provenance"]![1]!["policy"]!["choices"]!["effort"] = "low"; break;
            case "adapter": execution["requested"]!["adapter"] = "local"; break;
            case "requested": execution["requested"]!["model"] = "model-b"; break;
        }
        WriteOutcome refused = run.Service.NewRecord(new NewRecordRequest("operation", run.Estate.OperatorId, RunEstate.Json(body.ToJsonString()), Subject: "assignment:" + run.Assignment));
        Assert.False(refused.Ok);
        Assert.Contains(refused.Problem!.Findings, f => f.Code == "operation.execution");
    }

    [Fact]
    public void CurrentWorkOrderRoleSelectsItsRequiredModelEvenWhenTheAssignmentPinsAnOlderRevision()
    {
        using var run = new RunEstate();
        Roster(run, """{"choices":{"model":"model-a"}}""");
        RecordEnvelope order = Assert.Single(run.Estate.Load().Current("work-order"));
        JsonObject body = JsonNode.Parse(order.Body.GetRawText())!.AsObject();
        body["role"] = "reviewer";
        RunEstate.Ok(run.Service.NewRecord(new NewRecordRequest("work-order", run.Estate.OperatorId, RunEstate.Json(body.ToJsonString()), Supersedes: order.Id)));
        RunResponse refused = run.Start("claude-code", "claude", run.Report("old-role"));
        Assert.False(refused.Ok);
        Assert.Contains("requiredModel", refused.Message, StringComparison.Ordinal);
        Assert.Empty(run.Service.Runs().Runs);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void APolicyChangeDuringPreparationOrAfterIntentRequiresFreshAdmission(bool afterIntent)
    {
        using var run = new RunEstate();
        string roster = Roster(run, """{"choices":{"model":"model-a"}}""");
        void Revise()
        {
            RecordEnvelope prior = run.Estate.Load().ById[roster];
            JsonObject body = JsonNode.Parse(prior.Body.GetRawText())!.AsObject();
            body["execution"] = JsonNode.Parse("""{"choices":{"effort":"low"}}""");
            RunEstate.Ok(run.Service.NewRecord(new NewRecordRequest("roster", run.Estate.OperatorId, RunEstate.Json(body.ToJsonString()), Supersedes: prior.Id)));
        }
        if (afterIntent) HookedLauncher.Install(run, command: Revise);
        else HookedLauncher.Install(run, verify: () => { Revise(); return null; });
        RunResponse refused = run.Start("claude-code", "claude", run.Report("changed-policy"));
        Assert.False(refused.Ok);
        Assert.False(File.Exists(run.Report("changed-policy")));
        if (afterIntent)
        {
            RunRow settled = Assert.Single(run.Service.Runs().Runs);
            Assert.Equal("known-failure", settled.Status);
            Assert.Empty(settled.Charged.EnumerateObject());
        }
        else Assert.Empty(run.Service.Runs().Runs);
        Assert.Equal(0, run.Service.Check().Errors);
    }

    [Fact]
    public void RecordedDefaultsAndRoleRestrictionsKeepTheirExactProvenance()
    {
        using var run = new RunEstate();
        string roster = Roster(run, """{"choices":{"effort":"low"}}""");
        RecordEnvelope oldGoal = run.Estate.Load().ById[run.Goal];
        JsonObject goal = JsonNode.Parse(oldGoal.Body.GetRawText())!.AsObject();
        goal["execution"] = JsonNode.Parse("""{"choices":{"effort":"high"}}""");
        goal["roles"] = JsonNode.Parse($$$$"""[{"role":"builder","principals":["{{{{run.Estate.BuilderId}}}}"],"execution":{"choices":{"effort":"low"}}}]""");
        string currentGoal = RunEstate.Ok(run.Service.GoalPropose(new GoalProposeRequest(run.Estate.OperatorId, RunEstate.Json(goal.ToJsonString()), run.Goal))).Id;
        RunEstate.Ok(run.Service.GoalActivate(currentGoal, new GoalActionRequest(run.Estate.OperatorId)));
        RecordEnvelope assignment = run.Estate.Load().ById[run.Assignment];
        JsonObject body = JsonNode.Parse(assignment.Body.GetRawText())!.AsObject();
        body["execution"] = JsonNode.Parse("""{"choices":{"effort":"high"}}""");
        string currentAssignment = RunEstate.Ok(run.Service.NewRecord(new NewRecordRequest("assignment", run.Estate.OperatorId, RunEstate.Json(body.ToJsonString()),
            Subject: $"work-order:{assignment.Subject!.Id}:{assignment.Subject.Hash}", Supersedes: assignment.Id))).Id;
        RunResponse started = run.Service.RunStart(new RunStartRequest(currentAssignment, "claude-code", run.Estate.BuilderId, run.Estate.OperatorId, run.Resource,
            Settings: new Dictionary<string, string>(StringComparer.Ordinal) { ["command"] = FakeHarness.Setting("execution", run.Report("provenance")) }, Execution: RunEstate.Json("""{"effort":"low"}""")));
        Assert.True(started.Ok, started.Message);
        JsonElement selection = run.Settled(started.Run).Execution!.Value;
        Assert.Equal("low", selection.GetProperty("requested").GetProperty("effort").GetString());
        JsonElement[] sources = selection.GetProperty("provenance").EnumerateArray().ToArray();
        Assert.Equal(["launcher", "roster", "roster", "goal", "goal", "assignment", "run"], sources.Select(s => s.GetProperty("scope").GetString()));
        EstateModel model = run.Estate.Load();
        foreach (JsonElement source in sources.Where(s => s.TryGetProperty("record", out _)))
        {
            string id = source.GetProperty("record").GetString()!;
            Assert.Contains(id, new[] { roster, currentGoal, currentAssignment });
            Assert.Equal(model.ById[id].ComputedHash, source.GetProperty("hash").GetString());
        }
        Assert.Equal(0, run.Service.Check().Errors);
    }

    private static string Roster(RunEstate run, string policy) => RunEstate.Ok(run.Service.NewRecord(new NewRecordRequest("roster", run.Estate.OperatorId,
        RunEstate.Json($$$$"""{"execution":{{{{policy}}}},"roles":[{"role":"builder","principals":["{{{{run.Estate.BuilderId}}}}"],"execution":{"restrictions":{"requiredModel":"model-a"}}},{"role":"reviewer","principals":["{{{{run.Estate.BuilderId}}}}"],"execution":{"restrictions":{"requiredModel":"model-b"}}}]}""")))).Id;
}
