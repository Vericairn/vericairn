// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.CommandLine;
using Vericairn.Cli;
using Vericairn.Mcp;
using Vericairn.Runtime.Service;

namespace Vericairn.Cli.Tests;

[Collection("cli")]
public sealed class OperatorRecordCommandTests
{
    [Fact]
    public void RecordIsAnAdditionalCommandClassWithExplicitAuthorAndBody()
    {
        RootCommand root = CliApp.Build(cancellationToken: TestContext.Current.CancellationToken);
        Command family = root.Subcommands.Single(c => c.Name == "operator");
        Command record = Assert.IsType<OperatorRecordCommand>(family.Subcommands.Single(c => c.Name == "record"));
        Assert.Contains("commit it with its generated register members", record.Description, StringComparison.Ordinal);
        Assert.Contains(family.Subcommands, c => c is OperatorListCommand);
        Assert.NotEmpty(root.Parse(["operator", "record", "acceptance"]).Errors);
        Assert.All(record.Options.Where(o => o.Name is "--by" or "--body"), o => Assert.True(o.Required));
        Assert.DoesNotContain(record.Options, o => o.Name.Contains("key", StringComparison.Ordinal) || o.Name.Contains("token", StringComparison.Ordinal));
    }

    [Fact]
    public void TheShownOneStepCommandAcceptsTheSameBodyFromAFileAndReportsItsRefusalAsJson()
    {
        string root = CliHarness.TempDirectory("operator-record-estate");
        string scratch = CliHarness.TempDirectory("operator-record-input");
        try
        {
            string op = CliHarness.Ok("--estate", root, "--json", "estate", "init", "--name", "Example", "--slug", "example", "--purpose", "Operator records.", "--operator-name", "Operator", "--organization", "Example", "--commit", "0123456789abcdef0123456789abcdef01234567").Json.GetProperty("operator").GetString()!;
            CliHarness.Ok("--estate", root, "--json", "records", "new", "ask", "--by", op, "--body", CliHarness.WriteJson(scratch, "ask.json",
                $$"""{"to":"{{op}}","question":"Proceed?","recommendation":"Review.","consequence":"Work waits.","state":"open"}"""));
            CliHarness.Outcome listed = CliHarness.Ok("--estate", root, "--json", "operator", "list");
            string[] args = listed.Json.GetProperty("items")[0].GetProperty("commands")[0].GetProperty("oneStep").GetProperty("arguments").EnumerateArray().Select(a => a.GetString()!).ToArray();
            Assert.Empty(CliApp.Build(cancellationToken: TestContext.Current.CancellationToken).Parse(args).Errors);
            int body = Array.IndexOf(args, "--body");
            Assert.Equal("-", args[body + 1]);
            // The in-process harness supplies standard input through the equivalent file option.
            args[body + 1] = CliHarness.WriteJson(scratch, "body.json", "{}");
            CliHarness.Outcome refused = CliHarness.Run(["--estate", root, "--json", .. args]);
            Assert.Equal(1, refused.Code);
            Assert.False(refused.Json.GetProperty("ok").GetBoolean());
            Assert.Contains("own admitted signing device", refused.Json.GetProperty("message").GetString()!, StringComparison.Ordinal);
            Assert.Contains("operator record decision", refused.Json.GetProperty("nextStep").GetString()!, StringComparison.Ordinal);
            using EstateService service = EstateService.Open(root);
            Assert.Empty(service.Session.Load().OfType("decision"));
            Assert.DoesNotContain(new ToolRegistry(service, op).All, t => t.Name.StartsWith("operator_", StringComparison.Ordinal));
        }
        finally { CliHarness.Delete(root); CliHarness.Delete(scratch); }
    }
}
