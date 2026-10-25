// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;

namespace Vericairn.Cli.Tests;

/// <summary>Recording an agent whose product no installed launcher routes to warns with the accepted values and still writes.</summary>
[Collection("cli")]
public sealed class AgentProductWarningTests
{
    private const string Commit = "0123456789abcdef0123456789abcdef01234567";
    private static readonly string[] BuilderRole = ["builder"];

    [Theory]
    [InlineData("Codex CLI", true)]
    [InlineData("Codex", false)]
    [InlineData("codex", false)]
    [InlineData("claude code", false)]
    [InlineData("claude-code", false)]
    public void AnAgentProductRoutingCannotMatchWarnsWithTheAcceptedValues(string product, bool warns)
    {
        string root = CliHarness.TempDirectory("agent-product");
        string scratch = CliHarness.TempDirectory("agent-product-scratch");
        try
        {
            string op = CliHarness.Ok("--estate", root, "--json", "estate", "init", "--name", "Example", "--slug", "example", "--purpose", "Agent products.",
                "--operator-name", "Operator", "--organization", "Example", "--commit", Commit).Json.GetProperty("operator").GetString()!;
            string body = CliHarness.WriteJson(scratch, "agent.json", JsonSerializer.Serialize(new
            {
                kind = "agent", name = "Builder", roles = BuilderRole, agent = new { vendor = "Example", product },
            }));
            CliHarness.Outcome recorded = CliHarness.Run("--estate", root, "--json", "records", "new", "principal", "--by", op, "--body", body);
            Assert.Equal(0, recorded.Code);
            Assert.False(string.IsNullOrEmpty(recorded.Json.GetProperty("id").GetString()));
            if (warns)
            {
                Assert.Contains($"'{product}' matches no installed launcher", recorded.Err, StringComparison.Ordinal);
                Assert.Contains("Codex or codex", recorded.Err, StringComparison.Ordinal);
                Assert.Contains("Claude Code or claude-code", recorded.Err, StringComparison.Ordinal);
            }
            else Assert.DoesNotContain("warning:", recorded.Err, StringComparison.Ordinal);
        }
        finally { CliHarness.Delete(root, scratch); }
    }

    [Fact]
    public void HumansAndAgentsWithoutAnyRoutedLauncherGetNoWarning()
    {
        JsonElement human = JsonSerializer.SerializeToElement(new { kind = "human", name = "Person" });
        Assert.Null(RecordsNewCommand.AgentProductWarning(human, Vericairn.Runtime.Providers.ProviderRegistry.Current.Launchers));
        JsonElement agent = JsonSerializer.SerializeToElement(new { kind = "agent", name = "Builder", agent = new { vendor = "Example", product = "Anything" } });
        Assert.Null(RecordsNewCommand.AgentProductWarning(agent, []));
        JsonElement missing = JsonSerializer.SerializeToElement(new { kind = "agent", name = "Builder", agent = new { vendor = "Example" } });
        Assert.Contains("is missing", RecordsNewCommand.AgentProductWarning(missing, Vericairn.Runtime.Providers.ProviderRegistry.Current.Launchers), StringComparison.Ordinal);
    }
}
