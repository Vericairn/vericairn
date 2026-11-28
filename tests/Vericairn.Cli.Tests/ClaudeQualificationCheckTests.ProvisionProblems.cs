// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json.Nodes;
using Vericairn.Runtime.Tests;

namespace Vericairn.Cli.Tests;

public sealed partial class ClaudeQualificationCheckTests
{
    [Theory]
    [InlineData("expired", "has expired")]
    [InlineData("widened", "must have owner-only permissions")]
    [InlineData("corrupt", "file is corrupt")]
    public void CheckAndConfigureExplainWhyAProvisionCannotBeUsed(string condition, string expected)
    {
        string provision = Path.Combine(home, "worker", "claude-code-setup-token.json");
        string token = FakeHarness.NewClaudeSetupToken();
        CliHarness.OkWithInput(token + "\n", "worker", "configure", "--launcher", Launcher,
            "--import-setup-token", "--account-type", "max", "--token-stdin");
        Version(Shipped);
        if (condition == "expired")
        {
            JsonNode stored = JsonNode.Parse(File.ReadAllText(provision))!;
            stored["expiresAt"] = DateTimeOffset.UtcNow.AddDays(-1).ToUnixTimeMilliseconds();
            File.WriteAllText(provision, stored.ToJsonString());
        }
        else if (condition == "widened")
        {
            if (OperatingSystem.IsWindows())
            {
                Assert.Skip("This exercises Unix file permission bits.");
                return;
            }
            File.SetUnixFileMode(provision, File.GetUnixFileMode(provision) | UnixFileMode.GroupRead);
        }
        else
        {
            File.WriteAllText(provision, token + "\n" + token);
        }

        CliHarness.Outcome check = Check("--json");
        Assert.Equal(1, check.Code);
        Assert.Contains(expected, check.Json.GetProperty("problem").GetString(), StringComparison.Ordinal);
        Assert.False((check.Out + check.Err).Contains(token, StringComparison.Ordinal));

        CliHarness.Outcome configure = CliHarness.Run("--json", "worker", "configure", "--launcher", Launcher);
        Assert.Equal(0, configure.Code);
        var status = configure.Json.GetProperty("setupToken");
        Assert.True(status.GetProperty("provisioned").GetBoolean());
        Assert.Contains(expected, status.GetProperty("problem").GetString(), StringComparison.Ordinal);
        Assert.False((configure.Out + configure.Err).Contains(token, StringComparison.Ordinal));
    }
}
