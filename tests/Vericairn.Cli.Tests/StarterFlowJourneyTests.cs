// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using Vericairn.Records.Signing;
using Vericairn.Records.Templates;

namespace Vericairn.Cli.Tests;

/// <summary>
/// The starter flow from the command line (from 0.9.4, TPL-13; the estate's design/flow.md §§ 5, 6 and 8): an operator's new estate
/// lists the built-in flow, runs a sample service request through an agent's draft and a person's approval of its exact result to an
/// approved appointment, sees a revised draft leave the approval stale and the approval asked again, inspects which flow revision ran
/// and who approved which result revision, and restores an export into a second home and estate directory, where recovery inspection
/// and the run's position show the approved appointment and its history. A stand-in agent records the draft with its own device, as
/// the product's flow tests do; nothing launches a harness.
/// </summary>
[Collection("cli")]
public class StarterFlowJourneyTests
{
    private const string Commit = "0123456789abcdef0123456789abcdef01234567";
    private const string Flow = "vericairn.starter/service-appointment";
    private const string Request = "SR-0001: Sample Customer asks for a pool cleaning visit at 1 Sample Lane, Exampletown, any weekday morning in the week of 2026-12-07.";

    [Fact]
    public void AServiceRequestBecomesAnApprovedAppointmentThatARevisionMakesStaleAndARestoreKeeps()
    {
        string root = CliHarness.TempDirectory("starter");
        string restored = CliHarness.TempDirectory("starter-restored");
        string homeA = CliHarness.TempDirectory("starter-home-a");
        string homeB = CliHarness.TempDirectory("starter-home-b");
        string scratch = CliHarness.TempDirectory("starter-scratch");
        string? previousHome = Environment.GetEnvironmentVariable(VericairnHome.EnvironmentVariable);
        try
        {
            Environment.SetEnvironmentVariable(VericairnHome.EnvironmentVariable, homeA);
            string op = CliHarness.Ok("--estate", root, "--json", "estate", "init", "--name", "Example Services", "--slug", "example-services", "--purpose", "Service visits.", "--operator-name", "Sample Operator", "--organization", "Example Services", "--commit", Commit).Json.GetProperty("operator").GetString()!;

            // Installed with the build: the flow is listed and shown without copying a file into the estate.
            string hash = Assert.Single(StarterFlows.Definitions).Hash;
            Assert.Contains($"{Flow} 1.0.0 at {hash}, a flow", CliHarness.Ok("--estate", root, "templates", "list").Out, StringComparison.Ordinal);
            string shown = CliHarness.Ok("--estate", root, "templates", "show", Flow, "--version", "1.0.0").Out;
            Assert.Contains("the operator's own coding-agent harness, under the operator's own account", shown, StringComparison.Ordinal);
            Assert.False(Directory.Exists(Path.Combine(root, "docs", "templates")));

            // The operator's device and the scheduler agent's, both admitted here; the roster names who fills each role.
            string device = CliHarness.Ok("--estate", root, "--json", "devices", "create", "--principal", op, "--name", "Operator laptop", "--custody", "file").Id;
            CliHarness.Ok("--estate", root, "--json", "devices", "admit", device, "--by", op, "--note", "The first authority.");
            string agent = CliHarness.Ok("--estate", root, "--json", "records", "new", "principal", "--by", op, "--body", CliHarness.WriteJson(scratch, "agent.json",
                """{"kind":"agent","name":"Scheduler","roles":["contributor"],"agent":{"vendor":"Example","product":"Stand-in Agent"}}""")).Id;
            string agentDevice = CliHarness.Ok("--estate", root, "--json", "devices", "create", "--principal", agent, "--name", "Scheduler device", "--custody", "file").Id;
            CliHarness.Ok("--estate", root, "--json", "devices", "admit", agentDevice, "--by", op, "--note", "The scheduler's device.");
            CliHarness.Ok("--estate", root, "--json", "records", "new", "roster", "--by", op, "--body", CliHarness.WriteJson(scratch, "roster.json",
                $$"""{"roles":[{"role":"scheduler","principals":["{{agent}}"]},{"role":"approver","principals":["{{op}}"]}]}"""));
            CliHarness.Outcome order = CliHarness.Ok("--estate", root, "--json", "records", "new", "work-order", "--by", op, "--body", CliHarness.WriteJson(scratch, "order.json",
                """{"key":"SR-0001","kind":"ordinary","title":"Pool service visit","intent":"Turn the sample service request into an approved appointment.","scope":["the sample service request SR-0001"],"exitQuestion":"Is an appointment for SR-0001 approved?","inputs":[],"deliverables":["an approved appointment"]}"""));

            // A run of the starter flow for the request, proposed and activated by its sponsor.
            string goal = CliHarness.Ok("--estate", root, "--json", "goal", "propose", "--by", op, "--title", "Appointment for SR-0001", "--objective", "Turn the sample service request into an approved appointment.",
                "--done", """{"type":"work-order","fields":{"key":"SR-0001"}}""", "--work-order", order.Id, "--budget", "wallMinutes=30", "--budget", "attemptsPerStep=2",
                "--flow", Flow + "@1.0.0", "--subject", "request=" + Request).Id;
            CliHarness.Ok("--estate", root, "--json", "goal", "activate", goal, "--by", op);
            string position = CliHarness.Ok("--estate", root, "goal", "show", goal).Out;
            Assert.Contains($"Flow: {Flow} 1.0.0 ({hash}), 0 of 2 steps done", position, StringComparison.Ordinal);
            Assert.Contains("1. draft  next  Appointment proposed [work, scheduler]", position, StringComparison.Ordinal);

            // The draft is the scheduler agent's: no harness launches here, so goal step records nothing, and the stand-in agent carries the
            // step under an assignment naming it, recording its attempt and its result with its own device.
            Assert.Contains("candidate Scheduler (agent): not available: no launcher for its harness", CliHarness.Ok("--estate", root, "goal", "next", goal).Out, StringComparison.Ordinal);
            Assert.Contains("Nothing to dispatch", CliHarness.Ok("--estate", root, "goal", "step", goal, "--by", op).Out, StringComparison.Ordinal);
            string drafting = CliHarness.Ok("--estate", root, "--json", "records", "new", "assignment", "--by", op, "--subject", $"work-order:{order.Id}:{order.Hash}", "--body", CliHarness.WriteJson(scratch, "assignment.json",
                $$$"""{"contributor":"{{{agent}}}","step":{"key":"draft"},"brief":{"outcome":"Draft a proposed appointment for SR-0001.","sources":[],"permittedChanges":["the run's proposed appointment"],"prohibitedEffects":["contacting the customer"],"workspace":["the run's appointments"],"limits":[],"expectedOutputs":["appointment"],"reviewCriteria":["the approver approves the exact draft"],"recovery":"Draft again from the request."}}""")).Id;
            string attempt = CliHarness.Ok("--estate", root, "--json", "work", "attempt", drafting, "--by", agent, "--workspace", "the run's appointments", "--note", "Draft the appointment.").Id;
            string morning = Put(root, scratch, "appointment.json", """{"request":"SR-0001","service":"pool cleaning","site":"1 Sample Lane, Exampletown","start":"2026-12-08T09:00:00-06:00","durationMinutes":60,"technician":"Sample Technician"}""");
            CliHarness.Outcome draft = CliHarness.Ok("--estate", root, "--json", "work", "result", attempt, "--by", agent, "--body", CliHarness.WriteJson(scratch, "result.json", Result("Proposed Tuesday morning for SR-0001.", morning)));

            // The approval is the person's: goal step assigns it to the approver and asks; the approver's review of the exact result approves it.
            JsonElement asked = CliHarness.Ok("--estate", root, "--json", "goal", "step", goal, "--by", op).Json.GetProperty("step");
            Assert.Equal(("ask", "approve", "person"), (asked.GetProperty("outcome").GetString(), asked.GetProperty("step").GetString(), asked.GetProperty("route").GetString()));
            string approval = Approve(root, scratch, op, draft, "approval.json", "The Tuesday morning visit matches the request. Approved.");
            Assert.Contains("2 of 2 steps done; the run is complete", CliHarness.Ok("--estate", root, "goal", "show", goal).Out, StringComparison.Ordinal);

            // The agent revises its draft: the approval names the earlier revision's hash, so it no longer counts and the approval step is stale.
            string afternoon = Put(root, scratch, "appointment-2.json", """{"request":"SR-0001","service":"pool cleaning","site":"1 Sample Lane, Exampletown","start":"2026-12-09T13:00:00-06:00","durationMinutes":60,"technician":"Sample Technician"}""");
            string attemptHash = CliHarness.Ok("--estate", root, "--json", "records", "show", attempt).Json.GetProperty("hash").GetString()!;
            CliHarness.Outcome revised = CliHarness.Ok("--estate", root, "--json", "records", "new", "result", "--by", agent, "--supersedes", draft.Id, "--subject", $"attempt:{attempt}:{attemptHash}",
                "--body", CliHarness.WriteJson(scratch, "result-2.json", Result("Moved SR-0001 to Wednesday afternoon.", afternoon)));
            string stale = CliHarness.Ok("--estate", root, "goal", "show", goal).Out;
            Assert.Contains("1 of 2 steps done", stale, StringComparison.Ordinal);
            Assert.Contains("2. approve  stale  Appointment approved [review, approver]", stale, StringComparison.Ordinal);
            Assert.Contains($"its brief pinned result {draft.Id} at {morning}, and draft output appointment is now {afternoon} in result {revised.Id}", stale, StringComparison.Ordinal);
            CliHarness.Outcome check = CliHarness.Run("--estate", root, "estate", "check");
            Assert.True(check.Code == 0, check.Out + check.Err);
            Assert.Contains($"WARNING record.subject-stale records/review/", check.Out, StringComparison.Ordinal);
            Assert.Contains($"{approval}.json: The subject has been superseded by {revised.Id}", check.Out, StringComparison.Ordinal);

            // The flow asks for a fresh approval itself, once goal step is run: a new assignment of the approval and an ask to the approver.
            JsonElement again = CliHarness.Ok("--estate", root, "--json", "goal", "step", goal, "--by", op).Json.GetProperty("step");
            Assert.Equal(("ask", "approve", "person"), (again.GetProperty("outcome").GetString(), again.GetProperty("step").GetString(), again.GetProperty("route").GetString()));
            Assert.NotEqual(asked.GetProperty("assignment").GetProperty("id").GetString(), again.GetProperty("assignment").GetProperty("id").GetString());
            string reapproval = Approve(root, scratch, op, revised, "approval-2.json", "The Wednesday afternoon visit matches the request. Approved.");

            // Inspection: the flow revision that ran, by its exact pin, and who approved which result revision.
            JsonElement run = CliHarness.Ok("--estate", root, "--json", "goal", "show", goal).Json.GetProperty("flow");
            Assert.Equal((Flow, "1.0.0", hash), (run.GetProperty("pin").GetProperty("id").GetString(), run.GetProperty("pin").GetProperty("version").GetString(), run.GetProperty("pin").GetProperty("hash").GetString()));
            Assert.True(run.GetProperty("complete").GetBoolean());
            JsonElement approve = run.GetProperty("steps")[1];
            Assert.Equal(("done", reapproval), (approve.GetProperty("state").GetString(), approve.GetProperty("review").GetProperty("id").GetString()));
            Assert.Equal((revised.Id, revised.Hash), (run.GetProperty("steps")[0].GetProperty("result").GetProperty("id").GetString(), run.GetProperty("steps")[0].GetProperty("result").GetProperty("hash").GetString()));
            Assert.Equal((op, "result", draft.Id, draft.Hash), Judged(root, approval));
            Assert.Equal((op, "result", revised.Id, revised.Hash), Judged(root, reapproval));
            string before = CliHarness.Ok("--estate", root, "goal", "show", goal).Out;

            // Export, then restore into a second home and estate directory, standing in for a second machine.
            string zip = Path.Combine(scratch, "estate.zip");
            CliHarness.Ok("--estate", root, "--json", "estate", "export", "--out", zip, "--by", op);
            Environment.SetEnvironmentVariable(VericairnHome.EnvironmentVariable, homeB);
            Assert.Equal(0, CliHarness.Ok("--json", "estate", "restore", "--from", zip, "--to", restored).Json.GetProperty("errors").GetInt32());
            string inspected = CliHarness.Ok("--estate", restored, "recover", "inspect").Out;
            Assert.Contains($"{device}  Sample Operator  Operator laptop  admitted  key not held here", inspected, StringComparison.Ordinal);
            Assert.Contains($"{agentDevice}  Scheduler  Scheduler device  admitted  key not held here", inspected, StringComparison.Ordinal);
            Assert.Contains("Unresolved external effects:\n  (none)", inspected, StringComparison.Ordinal);

            // The restored copy shows the same run: the approved appointment, the earlier approval kept and reported stale, the same pin.
            Assert.Equal(before, CliHarness.Ok("--estate", restored, "goal", "show", goal).Out);
            Assert.Equal((op, "result", draft.Id, draft.Hash), Judged(restored, approval));
            Assert.Equal(draft.Id, CliHarness.Ok("--estate", restored, "--json", "records", "show", revised.Id).Json.GetProperty("supersedes").GetString());
            CliHarness.Outcome restoredCheck = CliHarness.Run("--estate", restored, "estate", "check");
            Assert.True(restoredCheck.Code == 0, restoredCheck.Out + restoredCheck.Err);
            Assert.Contains($"{approval}.json: The subject has been superseded by {revised.Id}", restoredCheck.Out, StringComparison.Ordinal);
            string observed = CliHarness.Ok("--estate", restored, "work", "observe", "SR-0001").Out;
            Assert.Contains($"output appointment {afternoon}", observed, StringComparison.Ordinal);
            Assert.Contains($"review {reapproval} (operator, pass) by Sample Operator", observed, StringComparison.Ordinal);
        }
        finally
        {
            Environment.SetEnvironmentVariable(VericairnHome.EnvironmentVariable, previousHome);
            CliHarness.Delete(root, restored, homeA, homeB, scratch);
        }
    }

    /// <summary>Stores a proposed appointment as an object and returns its content hash.</summary>
    private static string Put(string root, string scratch, string name, string json) =>
        CliHarness.Ok("--estate", root, "--json", "objects", "put", CliHarness.WriteJson(scratch, name, json)).Json.GetProperty("hash").GetString()!;

    private static string Result(string summary, string appointment) =>
        $$"""{"outcome":"completed","summary":"{{summary}}","outputs":[{"name":"appointment","hash":"{{appointment}}","mediaType":"application/json"}],"evidence":[],"checks":[],"limitations":[],"unresolvedEffects":[],"nextStep":"The approver reviews the exact draft."}""";

    /// <summary>The approver's review that passes the exact result; returns the review's id.</summary>
    private static string Approve(string root, string scratch, string by, CliHarness.Outcome result, string name, string summary) =>
        CliHarness.Ok("--estate", root, "--json", "records", "new", "review", "--by", by, "--subject", $"result:{result.Id}:{result.Hash}",
            "--body", CliHarness.WriteJson(scratch, name, $$"""{"independence":"operator","outcome":"pass","summary":"{{summary}}","findings":[]}""")).Id;

    /// <summary>Who recorded a review and the exact revision it judged.</summary>
    private static (string? By, string? Type, string? Id, string? Hash) Judged(string root, string review)
    {
        JsonElement record = CliHarness.Ok("--estate", root, "--json", "records", "show", review).Json;
        JsonElement subject = record.GetProperty("subject");
        return (record.GetProperty("by").GetString(), subject.GetProperty("type").GetString(), subject.GetProperty("id").GetString(), subject.GetProperty("hash").GetString());
    }
}
