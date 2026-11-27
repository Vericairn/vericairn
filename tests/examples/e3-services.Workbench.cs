// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using Vericairn.Presentation;
using Vericairn.Protocol;
using Vericairn.Records.Signing;
using Vericairn.Records.Updates;
using Vericairn.Runtime.Service;

namespace Vericairn.Workbench.Tests;

public partial class GoalsTests
{
    [Fact]
    public async Task TheStarterFlowsGoalsWorkspaceShowsTheApproversPendingAsk()
    {
        string root = Seed.Temp("services-workbench");
        string? previousHome = Environment.GetEnvironmentVariable(VericairnHome.EnvironmentVariable);
        try
        {
            string estate = Path.Combine(root, "estate");
            Directory.CreateDirectory(estate);
            Environment.SetEnvironmentVariable(VericairnHome.EnvironmentVariable, Path.Combine(root, "home"));
            new ReleaseUpdateStore().Configure(false);
            string Cli(params string[] args) => Seed.Cli(["--estate", estate, "--json", .. args]);
            string by = Seed.Init(estate, "Example Services");
            string agent = Seed.Admit(estate, by, root, "Scheduler", "agent", "contributor");
            foreach ((string principal, string name) in new[] { (by, "Operator device"), (agent, "Scheduler device") })
            {
                string device = Seed.Id(Cli("devices", "create", "--principal", principal, "--name", name, "--custody", "file"));
                Cli("devices", "admit", device, "--by", by, "--note", "The sample estate's device.");
            }

            string roster = Seed.Write(root, "roster.json", $$$"""{"roles":[{"role":"scheduler","principals":["{{{agent}}}"]},{"role":"approver","principals":["{{{by}}}"]}]}""");
            Cli("records", "new", "roster", "--by", by, "--body", roster);
            JsonElement orderWritten = Seed.Json(Cli("work", "new", "--by", by, "--key", "SR-0001", "--title", "Pool cleaning visit",
                "--intent", "Prepare the appointment.", "--scope", "Draft a pool cleaning visit.", "--deliverable", "An approved appointment",
                "--exit-question", "Is the visit approved?"));
            string order = orderWritten.GetProperty("id").GetString()!;
            string orderHash = orderWritten.GetProperty("hash").GetString()!;
            string goal = Seed.Id(Cli("goal", "propose", "--by", by, "--title", "Appointment for SR-0001", "--objective", "Prepare the appointment.",
                "--done", """{"type":"work-order","fields":{"key":"SR-0001"}}""", "--work-order", order, "--budget", "wallMinutes=30",
                "--budget", "attemptsPerStep=2", "--flow", "vericairn.starter/service-appointment@1.0.0",
                "--subject", "request=SR-0001: a pool cleaning visit.", "--flow-path", "appointments/**"));
            Cli("goal", "activate", goal, "--by", by);

            using EstateService service = EstateService.Open(estate);
            // Seed the scheduler's completed draft; the registered journey exercises dispatch.
            WriteOutcome drafting = service.NewRecord(new NewRecordRequest("assignment", by, Seed.Json($$$"""
                {"contributor":"{{{agent}}}","step":{"key":"draft"},"brief":{"outcome":"Propose an appointment.",
                 "sources":[],"permittedChanges":["path:appointments/**"],"prohibitedEffects":["no outside effects"],
                 "workspace":["Example services workspace"],"limits":[],"expectedOutputs":["appointment"],
                 "reviewCriteria":["The exact draft is reviewed."],"recovery":"Draft again from the request."}}
                """), $"work-order:{order}:{orderHash}"));
            Assert.True(drafting.Ok, drafting.Problem is { } rejected
                ? string.Join('\n', rejected.Findings.Select(finding => finding.Message)) : null);
            WriteOutcome attempt = service.Attempt(new AttemptRequest(drafting.Written!.Id, agent, "Example services workspace", "Draft the appointment."));
            Assert.True(attempt.Ok, attempt.Problem?.Message);
            (ObjectStored? appointment, Problem? storageProblem) = service.ObjectPut("""{"request":"SR-0001","service":"pool cleaning"}"""u8.ToArray());
            Assert.True(appointment is not null, storageProblem?.Message);
            WriteOutcome result = service.Result(new ResultRequest(attempt.Written!.Id, agent, Seed.Json($$$"""
                {"outcome":"completed","summary":"Proposed an appointment.","outputs":[{"name":"appointment","hash":"{{{appointment!.Hash}}}","mediaType":"application/json"}],
                 "evidence":[],"checks":[],"limitations":[],"unresolvedEffects":[],"nextStep":"Ask the approver."}
                """)));
            Assert.True(result.Ok, result.Problem?.Message);
            GoalStepResponse approval = service.GoalStep(goal, new GoalStepRequest(by));
            Assert.True(approval.Ok, approval.Message);
            Assert.Equal("ask", approval.Outcome);

            var workspace = new GoalsWorkspace(() => service);
            await workspace.FillAsync(goal, TestContext.Current.CancellationToken);
            Assert.Equal(goal, workspace.Selected?.Id);
            Assert.Equal("Appointment for SR-0001: active", workspace.Heading);
            StepRow approvalRow = Assert.Single(workspace.Steps, step => step.Key == "approve");
            Assert.Equal(("approver", "stopped", "Operator"), (approvalRow.Role, approvalRow.Status, approvalRow.Principal));
            AskRow ask = Assert.Single(workspace.Asks);
            Assert.Contains("Operator", ask.Detail, StringComparison.Ordinal);
            Assert.Contains("Appointment approved", ask.Question, StringComparison.Ordinal);
        }
        finally
        {
            Environment.SetEnvironmentVariable(VericairnHome.EnvironmentVariable, previousHome);
            Directory.Delete(root, recursive: true);
        }
    }
}
