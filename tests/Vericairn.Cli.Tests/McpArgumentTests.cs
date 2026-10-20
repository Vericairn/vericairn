// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.CommandLine;

namespace Vericairn.Cli.Tests;

/// <summary>Registry clients can put the MCP identity before the command without lending it to other commands.</summary>
public class McpArgumentTests
{
    [Theory]
    [InlineData("--estate example --as principal mcp serve")]
    [InlineData("--estate example mcp serve --as principal")]
    public void EitherMcpInputOrderSelectsTheSamePrincipal(string arguments)
    {
        ParseResult parsed = CliApp.Build(cancellationToken: TestContext.Current.CancellationToken).Parse(arguments);
        Assert.Empty(parsed.Errors);
        Assert.IsType<McpServeCommand>(parsed.CommandResult.Command);
        Assert.Equal("principal", parsed.GetValue<string>("--as"));
        Assert.Equal("example", parsed.GetValue<string>("--estate"));
    }

    [Theory]
    [InlineData("--as principal status")]
    [InlineData("--as principal mcp orientation --out example")]
    [InlineData("--as principal records describe")]
    public void TheRootIdentityCannotBeSilentlyIgnoredByOtherCommands(string arguments)
    {
        Assert.Contains(CliApp.Build(cancellationToken: TestContext.Current.CancellationToken).Parse(arguments).Errors, error => error.Message.Contains("only to mcp serve", StringComparison.Ordinal));
    }

    [Fact]
    public void ConflictingPrincipalsAreRejected()
    {
        Assert.NotEmpty(CliApp.Build(cancellationToken: TestContext.Current.CancellationToken).Parse("--as first mcp serve --as second").Errors);
    }

    [Theory]
    [InlineData("mcp orientation --out example --as principal")]
    [InlineData("communication set --as principal --status short")]
    public void OtherCommandsKeepTheirOwnIdentityOptions(string arguments)
    {
        ParseResult parsed = CliApp.Build(cancellationToken: TestContext.Current.CancellationToken).Parse(arguments);
        Assert.Empty(parsed.Errors);
        Assert.Equal("principal", parsed.GetValue<string>("--as"));
    }
}
