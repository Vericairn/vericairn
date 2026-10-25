// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using Vericairn.Cli;
using Vericairn.Records.Model;
using Vericairn.Runtime.Tests;

namespace Vericairn.Cli.Tests;

[Collection("cli")]
public sealed class OperatorRequestCommandTests
{
    private static readonly string[] Commands = ["list", "record", "close", "request", "carry", "done", "dismiss"];
    public OperatorRequestCommandTests() => _ = CliHarness.BuiltExecutable;

    [Fact]
    public void RequestCommandsAreAdditiveAndDismissRequiresTheOperatorsReason()
    {
        var root = CliApp.Build(cancellationToken: TestContext.Current.CancellationToken);
        var family = root.Subcommands.Single(c => c.Name == "operator");
        Assert.All(Commands, name => Assert.Contains(family.Subcommands, c => c.Name == name));
        Assert.Empty(root.Parse(["operator", "done", "request", "--by", "operator"]).Errors);
        Assert.NotEmpty(root.Parse(["operator", "dismiss", "request", "--by", "operator"]).Errors);
        Assert.Empty(root.Parse(["operator", "dismiss", "request", "--by", "operator", "--reason", "No longer needed."]).Errors);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DoneAndDismissKeepOneSignedOutcomeAndTheOriginalRequest(bool dismiss)
    {
        using var f = new OperatorCloseFixture();
        RecordEnvelope request = OperatorRequestTests.Request(f);
        int before = f.Estate.Load().Records.Count;
        CliHarness.Outcome listed = CliHarness.Run("--estate", f.Estate.Root, "operator", "list");
        Assert.Equal(0, listed.Code);
        Assert.Contains(request.Id, listed.Out, StringComparison.Ordinal);
        string[] reason = dismiss ? ["--reason", "No longer needed."] : [];
        CliHarness.Outcome outcome = CliHarness.Run(["--estate", f.Estate.Root, "--json", "operator", dismiss ? "dismiss" : "done", request.Id, "--by", f.Estate.OperatorId, "--expected", request.ComputedHash, .. reason]);
        Assert.Equal(0, outcome.Code);
        Assert.Equal(Assert.Single(f.Estate.Load().OfType("operator-request-close")).Id, outcome.Json.GetProperty("id").GetString());
        Assert.Equal(before + 1, f.Estate.Load().Records.Count);
        Assert.Equal(request.ComputedHash, f.Estate.Load().ById[request.Id].ComputedHash);
        Assert.Single(f.Estate.Load().OfType("operator-request-close"));
        Assert.DoesNotContain(request.Id, CliHarness.Run("--estate", f.Estate.Root, "operator", "list").Out, StringComparison.Ordinal);
    }
}
