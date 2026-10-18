// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using Vericairn.Cli;

namespace Vericairn.Cli.Tests;

[Collection("cli")]
public class OperatorActionsJourneyTests
{
    [Fact]
    public void QueueIsACommandClassAndRefusesAnAbsentEstate()
    {
        Assert.IsType<OperatorCommand>(CliApp.Build().Subcommands.Single(c => c.Name == "operator"));
        Assert.IsType<OperatorListCommand>(CliApp.Build().Subcommands.Single(c => c.Name == "operator").Subcommands.Single(c => c.Name == "list"));
        string root = CliHarness.TempDirectory("operator-absent");
        try
        {
            Assert.Equal(2, CliHarness.Run("--estate", root, "operator", "list").Code);
            Assert.Empty(Directory.EnumerateFileSystemEntries(root));
        }
        finally { CliHarness.Delete(root); }
    }

    [Fact]
    public void CopyingTheShownCommandWithTheOperatorsInputAnswersExactlyOneAsk()
    {
        string root = CliHarness.TempDirectory("operator-queue");
        string scratch = CliHarness.TempDirectory("operator-input");
        try
        {
            string op = CliHarness.Ok("--estate", root, "--json", "estate", "init", "--name", "Example", "--slug", "example", "--purpose", "Operator queue.", "--operator-name", "Operator", "--organization", "Example", "--commit", "0123456789abcdef0123456789abcdef01234567").Json.GetProperty("operator").GetString()!;
            string ask = CliHarness.Ok("--estate", root, "--json", "records", "new", "ask", "--by", op, "--body", CliHarness.WriteJson(scratch, "ask.json",
                $$"""{"to":"{{op}}","question":"Proceed?","recommendation":"Review first.","consequence":"Work waits.","state":"open"}""")).Id;
            CliHarness.Outcome listed = CliHarness.Ok("--estate", root, "--json", "operator", "list");
            JsonElement item = Assert.Single(listed.Json.GetProperty("items").EnumerateArray());
            Assert.Equal(ask, item.GetProperty("source").GetProperty("id").GetString());
            Assert.Contains(ask, CliHarness.Ok("--estate", root, "operator", "list", "--depth", "short").Out, StringComparison.Ordinal);
            string[] args = item.GetProperty("commands")[0].GetProperty("arguments").EnumerateArray().Select(a => a.GetString()!).ToArray();
            string answer = $$"""{"title":"Answer","statement":"Keep the current behavior.","summary":"No change.","synthesis":"The operator keeps the current behavior.","effects":["Keep it."],"affects":[],"decidedBy":"{{op}}","decidedAt":"2026-10-13T12:00:00Z","stake":"low","notAuthorized":[],"answers":["{{ask}}"]}""";
            CliHarness.Outcome written = CliHarness.RunWithInput(answer, ["--estate", root, "--json", .. args]);
            Assert.Equal(0, written.Code);
            Assert.Empty(CliHarness.Ok("--estate", root, "--json", "operator", "list").Json.GetProperty("items").EnumerateArray());
        }
        finally { CliHarness.Delete(root); CliHarness.Delete(scratch); }
    }
}
