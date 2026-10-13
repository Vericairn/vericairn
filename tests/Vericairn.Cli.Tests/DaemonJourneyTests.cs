// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using Vericairn.Daemon;

namespace Vericairn.Cli.Tests;

/// <summary>Journey J03 again, with a daemon owning the estate: every CLI command goes through the API and the records are identical in kind.</summary>
[Collection("cli")]
public class DaemonJourneyTests
{
    private const string Commit = "0123456789abcdef0123456789abcdef01234567";
    private const string Brief = """{"outcome":"A widget that builds and passes its tests.","sources":[],"permittedChanges":["the widget sources and tests"],"prohibitedEffects":["no publication"],"workspace":["example repository, main branch"],"limits":[],"expectedOutputs":["widget.txt"],"reviewCriteria":["tests pass"],"recovery":"Record a result before stopping."}""";

    [Fact]
    public async Task J03_ThroughTheDaemon()
    {
        string root = CliHarness.TempDirectory("daemon-j03");
        string scratch = CliHarness.TempDirectory("daemon-j03-scratch");
        try
        {
            string op = CliHarness.Ok("--estate", root, "--json", "estate", "init", "--name", "Example", "--slug", "example", "--purpose", "Daemon journey.", "--operator-name", "Operator", "--organization", "Example", "--commit", Commit).Json.GetProperty("operator").GetString()!;
            Assert.Contains("no daemon", CliHarness.Ok("--estate", root, "runtime", "status").Out, StringComparison.Ordinal);

            await using DaemonHost host = await DaemonHost.StartAsync(root, cancellationToken: TestContext.Current.CancellationToken);
            JsonElement status = CliHarness.Ok("--estate", root, "--json", "runtime", "status").Json;
            Assert.Equal("daemon", status.GetProperty("mode").GetString());
            Assert.Equal(Environment.ProcessId, status.GetProperty("daemon").GetProperty("processId").GetInt32());

            string agentOne = Admit(root, op, scratch, "Agent One");
            string agentTwo = Admit(root, op, scratch, "Agent Two");
            string builder = Admit(root, op, scratch, "Builder", "builder");
            CliHarness.Ok("--estate", root, "--json", "records", "new", "work-order", "--by", builder, "--body", CliHarness.WriteJson(scratch, "order.json", """{"key":"W-1","kind":"ordinary","title":"Build the widget","intent":"Through the daemon.","scope":["widget"],"exitQuestion":"Does it build?","inputs":[],"deliverables":["widget.txt"]}"""));
            string briefPath = CliHarness.WriteJson(scratch, "brief.json", Brief);
            string assignmentOne = CliHarness.Ok("--estate", root, "--json", "work", "assign", "W-1", "--contributor", agentOne, "--by", op, "--brief", briefPath).Id;
            string attemptOne = CliHarness.Ok("--estate", root, "--json", "work", "attempt", assignmentOne, "--by", agentOne, "--workspace", "a clone", "--note", "Start.").Id;
            CliHarness.Ok("--estate", root, "--json", "work", "result", attemptOne, "--by", agentOne, "--body", CliHarness.WriteJson(scratch, "partial.json", """{"outcome":"partial","summary":"Half.","outputs":[],"evidence":[],"checks":[],"limitations":["Tests not run."],"unresolvedEffects":[],"nextStep":"Finish it."}"""));
            Assert.Contains("next step: Finish it.", CliHarness.Ok("--estate", root, "work", "brief", "W-1").Out, StringComparison.Ordinal);

            string assignmentTwo = CliHarness.Ok("--estate", root, "--json", "work", "assign", "W-1", "--contributor", agentTwo, "--by", op, "--brief", briefPath).Id;
            string attemptTwo = CliHarness.Ok("--estate", root, "--json", "work", "attempt", assignmentTwo, "--by", agentTwo, "--workspace", "another clone", "--note", "Finish it.").Id;
            CliHarness.Outcome result = CliHarness.Ok("--estate", root, "--json", "work", "result", attemptTwo, "--by", agentTwo, "--body", CliHarness.WriteJson(scratch, "done.json", """{"outcome":"completed","summary":"Done.","outputs":[],"evidence":[],"checks":[{"name":"tests","outcome":"passed","summary":"Pass."}],"limitations":[],"unresolvedEffects":[],"nextStep":"none"}"""));
            CliHarness.Ok("--estate", root, "--json", "records", "new", "acceptance", "--by", op, "--subject", $"result:{result.Id}:{result.Hash}", "--body", CliHarness.WriteJson(scratch, "accept.json", """{"statement":"Accepted.","basis":[],"self":false}"""));

            // The same refusal and the same status language through the daemon as embedded.
            CliHarness.Outcome refused = CliHarness.Run("--estate", root, "--json", "work", "attempt", assignmentOne, "--by", agentTwo, "--workspace", "x", "--note", "Not mine.");
            Assert.Equal(1, refused.Code);
            Assert.Contains("attempt.contributor", refused.Out, StringComparison.Ordinal);
            JsonElement estateStatus = CliHarness.Ok("--estate", root, "--json", "status").Json;
            Assert.Equal(2, estateStatus.GetProperty("assignments").GetArrayLength());
            Assert.Contains(estateStatus.GetProperty("assignments").EnumerateArray(), a => a.GetProperty("state").GetString() == "accepted");
            Assert.Equal(0, CliHarness.Run("--estate", root, "estate", "check").Code);

            // Stopping through the CLI, and the embedded runtime takes over.
            Assert.Equal(0, CliHarness.Run("--estate", root, "runtime", "stop").Code);
            Assert.Contains("no daemon", CliHarness.Ok("--estate", root, "runtime", "status").Out, StringComparison.Ordinal);
            Assert.Equal(0, CliHarness.Run("--estate", root, "estate", "check").Code);
        }
        finally
        {
            CliHarness.Delete(root, scratch);
        }
    }

    private static string Admit(string root, string op, string scratch, string name, string role = "contributor")
    {
        string path = CliHarness.WriteJson(scratch, $"principal-{name.Replace(' ', '-')}.json", $$$"""{"kind":"agent","name":"{{{name}}}","roles":["{{{role}}}"],"agent":{"vendor":"Example","product":"Example Agent"}}""");
        return CliHarness.Ok("--estate", root, "--json", "records", "new", "principal", "--by", op, "--body", path).Id;
    }
}
