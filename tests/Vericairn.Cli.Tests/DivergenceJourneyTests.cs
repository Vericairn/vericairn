// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jayson Furr

using System.Text.Json;

namespace Vericairn.Cli.Tests;

/// <summary>Two clones revise the same record; the merge is a pure file addition and the divergence is a reconcile task, settled by a record.</summary>
[Collection("cli")]
public class DivergenceJourneyTests
{
    private const string Commit = "0123456789abcdef0123456789abcdef01234567";

    [Fact]
    public void TwoClonesDivergeAndReconcileByRecordNotBySilentMerge()
    {
        string a = CliHarness.TempDirectory("div-a");
        string scratch = CliHarness.TempDirectory("div-scratch");
        string b = string.Empty;
        try
        {
            string op = CliHarness.Ok("--estate", a, "--json", "estate", "init", "--name", "Example", "--slug", "example", "--purpose", "Divergence.", "--operator-name", "Operator", "--organization", "Example", "--commit", Commit).Json.GetProperty("operator").GetString()!;
            string builder = CliHarness.Ok("--estate", a, "--json", "records", "new", "principal", "--by", op, "--body", CliHarness.WriteJson(scratch, "builder.json", """{"kind":"agent","name":"Builder","roles":["builder"],"agent":{"vendor":"Example","product":"Example Agent"}}""")).Id;
            string orderBody = """{"key":"W-1","kind":"ordinary","title":"Original","intent":"Diverge.","scope":["x"],"exitQuestion":"?","inputs":[],"deliverables":["y"]}""";
            string original = CliHarness.Ok("--estate", a, "--json", "records", "new", "work-order", "--by", builder, "--body", CliHarness.WriteJson(scratch, "order.json", orderBody)).Id;
            b = CliHarness.Clone(a, "div-b");

            string fromA = CliHarness.Ok("--estate", a, "--json", "records", "new", "work-order", "--by", builder, "--supersedes", original, "--body", CliHarness.WriteJson(scratch, "a.json", orderBody.Replace("Original", "Revised on A", StringComparison.Ordinal))).Id;
            string fromB = CliHarness.Ok("--estate", b, "--json", "records", "new", "work-order", "--by", op, "--supersedes", original, "--body", CliHarness.WriteJson(scratch, "b.json", orderBody.Replace("Original", "Revised on B", StringComparison.Ordinal))).Id;
            Assert.Equal(1, CliHarness.MergeRecords(b, a));

            CliHarness.Outcome check = CliHarness.Run("--estate", a, "estate", "check");
            Assert.Equal(1, check.Code);
            Assert.Contains("reconcile.successors", check.Out, StringComparison.Ordinal);
            Assert.Contains("Reconcile:", CliHarness.Run("--estate", a, "status").Out, StringComparison.Ordinal);
            CliHarness.Outcome tasks = CliHarness.Run("--estate", a, "--json", "estate", "reconcile");
            Assert.Equal(1, tasks.Code);
            Assert.Contains(tasks.Json.EnumerateArray(), t => t.GetProperty("Code").GetString() == "reconcile.successors");

            CliHarness.Ok("--estate", a, "--json", "records", "new", "reconciliation", "--by", op, "--subject", $"work-order:{original}", "--body", CliHarness.WriteJson(scratch, "rec.json", $$"""{"kept":"{{fromB}}","withdrawn":["{{fromA}}"],"note":"B's revision is kept; A's wording is folded in later."}"""));
            Assert.Equal(0, CliHarness.Run("--estate", a, "estate", "check").Code);
            Assert.Equal(0, CliHarness.Run("--estate", a, "estate", "reconcile").Code);
            JsonElement status = CliHarness.Ok("--estate", a, "--json", "status").Json;
            JsonElement order = status.GetProperty("workOrders").EnumerateArray().Single();
            Assert.Equal("Revised on B", order.GetProperty("title").GetString());
            Assert.Equal(fromB, order.GetProperty("id").GetString());
            Assert.Empty(status.GetProperty("reconcile").EnumerateArray());
        }
        finally
        {
            CliHarness.Delete(a, b, scratch);
        }
    }
}
