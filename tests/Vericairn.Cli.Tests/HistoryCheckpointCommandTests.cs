// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using Vericairn.Protocol;
using Vericairn.Records.Signing;
using Vericairn.Runtime.Service;

namespace Vericairn.Cli.Tests;

/// <summary>
/// <c>vericairn estate checkpoint</c> (design/signed-history.md §§ 9 and 10; OPS-07): a new estate adopts at birth, an existing one as the
/// operator's step after reading what adoption would witness and anchor, never by an agent; afterwards any admitted device writes one, and
/// status shows the history line.
/// </summary>
[Collection("cli")]
public class HistoryCheckpointCommandTests
{
    private const string Commit = "0123456789abcdef0123456789abcdef01234567";

    [Fact]
    public void ANewEstateAdoptsAtBirthAndThenWritesCheckpointsOnRequest()
    {
        string root = CliHarness.TempDirectory("checkpoint-birth");
        string home = CliHarness.TempDirectory("checkpoint-birth-home");
        string? previousHome = Environment.GetEnvironmentVariable(VericairnHome.EnvironmentVariable);
        try
        {
            Environment.SetEnvironmentVariable(VericairnHome.EnvironmentVariable, home);
            string op = Init(root);
            string device = CliHarness.Ok("--estate", root, "--json", "devices", "create", "--principal", op, "--name", "Operator laptop", "--custody", "file").Id;
            CliHarness.Outcome admitted = CliHarness.Ok("--estate", root, "devices", "admit", device, "--by", op, "--note", "The first authority.");
            Assert.Contains("(history checkpoint, trigger adoption)", admitted.Out, StringComparison.Ordinal);
            Assert.Contains("History: adopted · head ", CliHarness.Ok("--estate", root, "status").Out, StringComparison.Ordinal);

            CliHarness.Outcome plan = CliHarness.Ok("--estate", root, "estate", "checkpoint", "--check");
            Assert.StartsWith("would write a history checkpoint: witnesses 5 records, listing the 1 no checkpoint witnessed yet; follows ", plan.Out, StringComparison.Ordinal);
            CliHarness.Outcome written = CliHarness.Ok("--estate", root, "--json", "estate", "checkpoint", "--by", op);
            HistoryCheckpointResponse response = written.Json.Deserialize<HistoryCheckpointResponse>(ProtocolJson.Options)!;
            Assert.True(response.Applied);
            Assert.Equal("manual", response.Trigger);
            Assert.Equal(5, response.Witnessed);
            Assert.Equal(0, CliHarness.Run("--estate", root, "estate", "check").Code);

            CliHarness.Outcome again = CliHarness.Run("--estate", root, "estate", "checkpoint", "--adopt", "--by", op);
            Assert.Equal(2, again.Code);
            Assert.Contains("adopted history checkpoints already", again.Err, StringComparison.Ordinal);
            Assert.Contains("--by is required unless --check", CliHarness.Run("--estate", root, "estate", "checkpoint").Err, StringComparison.Ordinal);
        }
        finally
        {
            Environment.SetEnvironmentVariable(VericairnHome.EnvironmentVariable, previousHome);
            CliHarness.Delete(root, home);
        }
    }

    [Fact]
    public void AnExistingEstateAdoptsAsTheOperatorsStepAfterItsCheckAndNeverByAnAgent()
    {
        string root = CliHarness.TempDirectory("checkpoint-adopt");
        string home = CliHarness.TempDirectory("checkpoint-adopt-home");
        string scratch = CliHarness.TempDirectory("checkpoint-adopt-scratch");
        string? previousHome = Environment.GetEnvironmentVariable(VericairnHome.EnvironmentVariable);
        try
        {
            Environment.SetEnvironmentVariable(VericairnHome.EnvironmentVariable, home);
            string op = Init(root);
            // A work order recorded before any device: the estate holds more than its birth, so it does not adopt at its first authority.
            string order = CliHarness.Ok("--estate", root, "--json", "records", "new", "work-order", "--by", op, "--body",
                CliHarness.WriteJson(scratch, "order.json", """{"key":"W-1","kind":"ordinary","title":"Recorded early","intent":"Precede adoption.","scope":["x"],"exitQuestion":"Witnessed?","inputs":[],"deliverables":["y"]}""")).Id;
            string device = CliHarness.Ok("--estate", root, "--json", "devices", "create", "--principal", op, "--name", "Operator laptop", "--custody", "file").Id;
            Assert.DoesNotContain("history checkpoint", CliHarness.Ok("--estate", root, "devices", "admit", device, "--by", op, "--note", "The first authority.").Out, StringComparison.Ordinal);
            string builder = CliHarness.Ok("--estate", root, "--json", "records", "new", "principal", "--by", op, "--body",
                CliHarness.WriteJson(scratch, "builder.json", """{"kind":"agent","name":"Builder","roles":["builder"],"agent":{"vendor":"Example","product":"Example Agent"}}""")).Id;
            string builderDevice = CliHarness.Ok("--estate", root, "--json", "devices", "create", "--principal", builder, "--name", "Build machine", "--custody", "file").Id;
            CliHarness.Ok("--estate", root, "devices", "admit", builderDevice, "--by", op, "--note", "The builder's machine.");
            Assert.Contains("History: not adopted · no heads · 0 witnessed", CliHarness.Ok("--estate", root, "status").Out, StringComparison.Ordinal);
            Assert.Contains("has not adopted history checkpoints", CliHarness.Run("--estate", root, "estate", "checkpoint", "--by", builder).Err, StringComparison.Ordinal);

            CliHarness.Outcome check = CliHarness.Ok("--estate", root, "estate", "checkpoint", "--adopt", "--check");
            Assert.StartsWith("would adopt: witnesses 8 records; anchors 3 unsigned records that count today\nanchors:\n", check.Out, StringComparison.Ordinal);
            Assert.Contains($"work-order  ", check.Out, StringComparison.Ordinal);
            Assert.Contains(order + ".json  added in an unknown commit", check.Out, StringComparison.Ordinal);
            Assert.Contains("unavailable: This estate is not a git repository", check.Out, StringComparison.Ordinal);

            CliHarness.Outcome agent = CliHarness.Run("--estate", root, "estate", "checkpoint", "--adopt", "--by", builder);
            Assert.Equal(2, agent.Code);
            Assert.Contains("never an agent", agent.Err, StringComparison.Ordinal);
            Assert.Contains("History: not adopted · no heads", CliHarness.Ok("--estate", root, "status").Out, StringComparison.Ordinal);

            // The adoption is bound to the check its signer read, by the plan hash the check printed.
            string plan = check.Out.Split('\n').Single(l => l.StartsWith("plan: sha256:", StringComparison.Ordinal))["plan: ".Length..];
            CliHarness.Outcome unbound = CliHarness.Run("--estate", root, "estate", "checkpoint", "--adopt", "--by", op);
            Assert.Equal(2, unbound.Code);
            Assert.Contains("--expect-plan", unbound.Err, StringComparison.Ordinal);
            CliHarness.Outcome adopted = CliHarness.Ok("--estate", root, "--json", "estate", "checkpoint", "--adopt", "--expect-plan", plan, "--by", op);
            HistoryCheckpointResponse response = adopted.Json.Deserialize<HistoryCheckpointResponse>(ProtocolJson.Options)!;
            Assert.True(response.Applied && response.Adopted);
            Assert.Equal(plan, response.PlanHash);
            Assert.Equal(3, response.Anchored.Count);
            Assert.Contains(response.Anchored, a => a.Id == order && a.Type == "work-order");
            Assert.Contains("History: adopted · head ", CliHarness.Ok("--estate", root, "status").Out, StringComparison.Ordinal);
            Assert.Equal(0, CliHarness.Run("--estate", root, "estate", "check").Code);

            // Afterwards the agent's checkpoint witnesses, and a write it makes reports no checkpoint unless a trigger falls due.
            Assert.Contains("wrote a history checkpoint", CliHarness.Ok("--estate", root, "estate", "checkpoint", "--by", builder).Out, StringComparison.Ordinal);

            // A revocation in the adopted estate is stored after its closing, which the command prints after the revocation.
            string spare = CliHarness.Ok("--estate", root, "--json", "devices", "create", "--principal", builder, "--name", "Spare build machine", "--custody", "file").Id;
            CliHarness.Ok("--estate", root, "devices", "admit", spare, "--by", op, "--note", "The builder's spare machine.");
            CliHarness.Outcome revoked = CliHarness.Ok("--estate", root, "devices", "revoke", spare, "--by", op, "--reason", "The spare machine is retired.");
            Assert.Contains("(history checkpoint, trigger closing)", revoked.Out, StringComparison.Ordinal);
            Assert.Equal(0, CliHarness.Run("--estate", root, "estate", "check").Code);
        }
        finally
        {
            Environment.SetEnvironmentVariable(VericairnHome.EnvironmentVariable, previousHome);
            CliHarness.Delete(root, home, scratch);
        }
    }

    [Fact]
    public void AClosingStoredWithoutItsActIsPrintedAndTheActWrittenAgainFollowsIt()
    {
        string root = CliHarness.TempDirectory("checkpoint-closing");
        string home = CliHarness.TempDirectory("checkpoint-closing-home");
        string scratch = CliHarness.TempDirectory("checkpoint-closing-scratch");
        string? previousHome = Environment.GetEnvironmentVariable(VericairnHome.EnvironmentVariable);
        try
        {
            Environment.SetEnvironmentVariable(VericairnHome.EnvironmentVariable, home);
            string op = Init(root);
            string device = CliHarness.Ok("--estate", root, "--json", "devices", "create", "--principal", op, "--name", "Operator laptop", "--custody", "file").Id;
            Assert.Contains("(history checkpoint, trigger adoption)", CliHarness.Ok("--estate", root, "devices", "admit", device, "--by", op, "--note", "The first authority.").Out, StringComparison.Ordinal);
            string builder = CliHarness.Ok("--estate", root, "--json", "records", "new", "principal", "--by", op, "--body",
                CliHarness.WriteJson(scratch, "builder.json", """{"kind":"agent","name":"Builder","roles":["builder"],"agent":{"vendor":"Example","product":"Example Agent"}}""")).Id;
            string machine = CliHarness.Ok("--estate", root, "--json", "devices", "create", "--principal", builder, "--name", "Build machine", "--custody", "file").Id;
            CliHarness.Ok("--estate", root, "devices", "admit", machine, "--by", op, "--note", "The builder's machine.");

            // The batch stops after the closing, which stays: the command prints it before saying the revocation was not written.
            using (EstateService api = EstateService.Open(root))
            {
                api.BeforeStoringTogether = record => { if (record.Envelope.Type != "history-checkpoint") throw new IOException("The test stops the batch after its closing."); };
                api.BeforeUndoingClosing = _ => throw new IOException("The test keeps the closing.");
                CliHarness.Outcome stopped = CliHarness.RunWithApi(api, null, "--estate", root, "devices", "revoke", machine, "--by", op, "--reason", "Lost.");
                Assert.Equal(2, stopped.Code);
                Assert.Matches(@"^records/history-checkpoint/\d{4}-\d{2}/[0-9A-Z]{26}\.json  [0-9A-Z]{26}  sha256:[0-9a-f]{64}  \(history checkpoint, trigger closing\)\n$", stopped.Out);
                Assert.Contains("Writing the revocation again as " + op + " uses the stored closing", stopped.Err, StringComparison.Ordinal);

                // The operator's rotation stops the same way; with --json the stored closing is printed with the outcome's code.
                CliHarness.Outcome rotation = CliHarness.RunWithApi(api, null, "--estate", root, "--json", "devices", "rotate", device);
                Assert.Equal(2, rotation.Code);
                Assert.Equal(EstateService.ClosingStoredCode, rotation.Json.GetProperty("code").GetString());
                Assert.Equal("closing", rotation.Json.GetProperty("checkpoint").GetProperty("trigger").GetString());
                Assert.StartsWith("records/history-checkpoint/", rotation.Json.GetProperty("checkpoint").GetProperty("written").GetProperty("path").GetString(), StringComparison.Ordinal);
            }

            // Written again, each act follows its stored closing: the rotation first, since the operator's closed key signs nothing else.
            Assert.Contains("warning: No closing was written with this key rotation: it follows closing ", CliHarness.Ok("--estate", root, "devices", "rotate", device).Err, StringComparison.Ordinal);
            CliHarness.Outcome revoked = CliHarness.Ok("--estate", root, "devices", "revoke", machine, "--by", op, "--reason", "Lost.");
            Assert.DoesNotContain("(history checkpoint", revoked.Out, StringComparison.Ordinal);
            Assert.Contains("warning: No closing was written with this revocation: it follows closing ", revoked.Err, StringComparison.Ordinal);
            Assert.Equal(0, CliHarness.Run("--estate", root, "estate", "check").Code);
        }
        finally
        {
            Environment.SetEnvironmentVariable(VericairnHome.EnvironmentVariable, previousHome);
            CliHarness.Delete(root, home, scratch);
        }
    }

    private static string Init(string root) =>
        CliHarness.Ok("--estate", root, "--json", "estate", "init", "--name", "Example", "--slug", "example", "--purpose", "History checkpoints.", "--operator-name", "Operator", "--organization", "Example", "--commit", Commit)
            .Json.GetProperty("operator").GetString()!;
}
