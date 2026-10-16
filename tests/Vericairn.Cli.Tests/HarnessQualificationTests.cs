// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using Vericairn.Daemon;
using Vericairn.Packs.Software;
using Vericairn.Protocol;
using Vericairn.Runtime.Tests;
using Vericairn.Runtime.Workers;

namespace Vericairn.Cli.Tests;

[Collection("cli")]
public class HarnessQualificationTests
{
    [Fact]
    public void QualifyCommandExplainsExactLocalProbesAndHumanAuthority()
    {
        CliHarness.Outcome result = CliHarness.Run("worker", "qualify", "--help");
        Assert.Equal(0, result.Code);
        foreach (string word in new[] { "--launcher", "--by", "human", "exact", "probe", "Remote" })
            Assert.Contains(word, result.Out, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("codex")]
    [InlineData("claude-code")]
    public async Task QualifyRunsTheNewerStandInThroughTheCliSdkAndDaemon(string launcher)
    {
        string root = CliHarness.TempDirectory("qualification-estate");
        string scratch = CliHarness.TempDirectory("qualification-scratch");
        try
        {
            CliHarness.Outcome initialized = CliHarness.Ok("--estate", root, "--json", "estate", "init", "--name", "Example", "--slug", "example", "--purpose", "Qualification journey.",
                "--operator-name", "Operator", "--organization", "Example", "--commit", "0123456789abcdef0123456789abcdef01234567");
            string op = initialized.Json.GetProperty("operator").GetString()!;
            string device = CliHarness.Ok("--estate", root, "--json", "devices", "create", "--principal", op, "--name", "Qualifier", "--custody", "file").Id;
            CliHarness.Ok("--estate", root, "devices", "admit", device, "--by", op, "--note", "Admit the operator.");
            IAgentLauncher native = launcher == "codex" ? new CodexLauncher() : new ClaudeCodeLauncher();
            string login = Directory.CreateDirectory(Path.Combine(scratch, "login")).FullName;
            string user = Directory.CreateDirectory(Path.Combine(scratch, "user")).FullName;
            string report = Path.Combine(scratch, "dispatch.jsonl");
            File.WriteAllText(Path.Combine(login, "auth.json"), "synthetic-login");
            File.WriteAllText(Path.Combine(login, ".credentials.json"), "{\"claudeAiOauth\":{\"subscriptionType\":\"pro\"}}");
            File.WriteAllText(report + ".behavior", JsonSerializer.Serialize(new { version = launcher == "codex" ? "codex-cli 0.160.0" : "2.1.297 (Claude Code)" }));
            var environment = WorkerEnvironment.Build(WorkerEnvironment.Current(), native.EnvironmentNames);
            environment["HOME"] = user;
            environment["USERPROFILE"] = user;
            environment[launcher == "codex" ? "CODEX_HOME" : "CLAUDE_CONFIG_DIR"] = login;
            await using DaemonHost host = await DaemonHost.StartAsync(root, loopback: false, cancellationToken: TestContext.Current.CancellationToken);
            host.Service.Providers.Register(native);
            host.Service.WorkerOptions = new WorkerOptions { Environment = environment, Vericairn = FakeHarness.Command("native-server", Path.Combine(scratch, "server.jsonl")) };
            CliHarness.Outcome qualified = CliHarness.Ok("--estate", root, "--json", "worker", "qualify", "--launcher", launcher, "--by", op,
                "--setting", "command=" + FakeHarness.Setting(launcher == "codex" ? "native-codex" : "native-claude", report));
            RecordResponse record = host.Service.Record(qualified.Id)!;
            Assert.Equal("harness-qualification", record.Type);
            Assert.Contains(launcher == "codex" ? "0.160.0" : "2.1.297", record.Envelope.GetRawText(), StringComparison.Ordinal);
            Assert.DoesNotContain(scratch, record.Envelope.GetRawText(), StringComparison.Ordinal);
            Assert.DoesNotContain("synthetic-login", record.Envelope.GetRawText(), StringComparison.Ordinal);
            Assert.False(File.Exists(report));
            Assert.Equal(0, host.Service.Check().Errors);
        }
        finally
        {
            CliHarness.Delete(root, scratch);
        }
    }
}
