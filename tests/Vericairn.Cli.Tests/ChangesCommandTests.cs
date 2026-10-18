// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.CommandLine;
using Vericairn.Cli;

namespace Vericairn.Cli.Tests;

[Collection("cli")]
public sealed class ChangesCommandTests
{
    private static readonly string[] Actions = ["merge", "observe", "open", "push"];
    [Theory]
    [InlineData("push", "--branch")]
    [InlineData("open", "--branch")]
    [InlineData("merge", "--pull-request")]
    public void MutationsRequireAnExplicitHeadProductAndActor(string action, string selection)
    {
        RootCommand root = CliApp.Build();
        Command changes = root.Subcommands.Single(c => c.Name == "changes");
        Command command = changes.Subcommands.Single(c => c.Name == action);
        foreach (string option in new[] { "--head", "--product", "--by", selection })
            Assert.True(command.Options.Single(o => o.Name == option).Required);
        Assert.Contains(command.Options, o => o.Name == "--reconciliation" && !o.Required);
        Assert.NotEmpty(root.Parse(["changes", action]).Errors);
    }

    [Fact]
    public void ObservationKeepsTheOriginalIdentityAndMakesOperatorReconciliationExplicit()
    {
        Command changes = CliApp.Build().Subcommands.Single(c => c.Name == "changes");
        Assert.Equal(Actions, changes.Subcommands.Select(c => c.Name).Order());
        Command observe = changes.Subcommands.Single(c => c.Name == "observe");
        Assert.True(observe.Options.Single(o => o.Name == "--operation").Required);
        Assert.True(observe.Options.Single(o => o.Name == "--by").Required);
        Assert.False(observe.Options.Single(o => o.Name == "--cancel").Required);
        Assert.DoesNotContain(observe.Options, o => o.Name is "--product" or "--head" or "--provider");
    }
}
