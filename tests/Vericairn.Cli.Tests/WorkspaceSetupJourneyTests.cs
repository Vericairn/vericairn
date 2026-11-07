// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Diagnostics;
using System.Text.Json;
using Vericairn.Runtime.Tests;
using Vericairn.Runtime.Workers;

namespace Vericairn.Cli.Tests;

/// <summary>
/// Preparing a run's workspace before the agent starts, from the command line (FIX-86, issue 238): worker run --prepare-only prints
/// where it is and records nothing, and worker configure sets, shows and clears a repository's setup command, which the next run runs.
/// Every harness and setup command is the tests' stand-in.
/// </summary>
[Collection("cli")]
public sealed class WorkspaceSetupJourneyTests : IDisposable
{
    private static readonly string[] GitIdentity = ["-c", "user.name=Test", "-c", "user.email=test@example.invalid", "-c", "commit.gpgsign=false"];

    private readonly IReadOnlyList<IAgentLauncher> originalLaunchers;

    public WorkspaceSetupJourneyTests()
    {
        _ = CliHarness.Run("--help");
        var registry = Vericairn.Runtime.Providers.ProviderRegistry.Current;
        originalLaunchers = registry.Launchers.ToArray();
        foreach (IAgentLauncher launcher in originalLaunchers) registry.Register(new ReportedLauncher(launcher));
    }

    public void Dispose()
    {
        foreach (IAgentLauncher launcher in originalLaunchers) Vericairn.Runtime.Providers.ProviderRegistry.Current.Register(launcher);
    }

    [Fact]
    public void APersonPreparesTheWorkspaceInstallsThereAndConfiguresARepositorysSetup()
    {
        string root = CliHarness.TempDirectory("worker-prepare");
        string scratch = CliHarness.TempDirectory("worker-prepare-scratch");
        string? resource = null;
        try
        {
            (string op, string builder, _, string assignment, string repository) = WorkerJourneyTests.Estate(root, scratch);
            resource = repository;
            string clone = Path.Combine(scratch, "widget");
            File.WriteAllText(Path.Combine(clone, ".gitignore"), "node_modules/\n");
            Git(clone, "add", ".gitignore");
            Git(clone, "commit", "-q", "-m", "Ignore installed dependencies");
            string report = Path.Combine(scratch, "harness.jsonl");
            string[] run = ["--estate", root, "worker", "run", assignment, "--launcher", "codex", "--agent", builder, "--by", op, "--repository", repository,
                "--setting", "command=" + FakeHarness.Setting("codex", report)];

            Assert.Equal(2, CliHarness.Run([.. run, "--prepare-only", "--detach"]).Code);
            CliHarness.Outcome prepared = CliHarness.Ok([.. run, "--prepare-only"]);
            string workspace = prepared.Out.Split('\n')[0];
            Assert.True(Directory.Exists(Path.Combine(workspace, ".git")), prepared.Out);
            Assert.Contains("Nothing was recorded, and no setup command or harness ran.", prepared.Out, StringComparison.Ordinal);
            Assert.False(File.Exists(report));
            Assert.Empty(CliHarness.Ok("--estate", root, "--json", "worker", "runs").Json.GetProperty("runs").EnumerateArray());
            JsonElement json = CliHarness.Ok(["--json", .. run, "--prepare-only"]).Json;
            Assert.Equal("prepared", json.GetProperty("status").GetString());
            Assert.Equal(workspace, json.GetProperty("workspace").GetString());

            // A setup command for this repository, shown with the machine's other worker configuration.
            string setup = Path.Combine(scratch, "setup.jsonl");
            Assert.Equal(2, CliHarness.Run("worker", "configure", "--repository", repository, "--launcher", "codex", "--setup-command", "make").Code);
            Assert.Equal(2, CliHarness.Run("worker", "configure", "--repository", repository, "--setup-timeout", "60").Code);
            Assert.Equal(2, CliHarness.Run("worker", "configure", "--repository", repository, "--setup-command", "make", "--setup-timeout", "601").Code);
            Assert.Equal(2, CliHarness.Run("worker", "configure", "--repository", repository, "--setup-command", "make", "--model", "model-a").Code);
            Assert.Equal(2, CliHarness.Run("worker", "configure", "--launcher", "no-such-launcher", "--setup-command", "make").Code);
            Assert.Equal(2, CliHarness.Run("worker", "configure", "--repository", repository, "--setup-variable", "PERSON_SETTING").Code);
            Assert.Equal(2, CliHarness.Run("worker", "configure", "--repository", repository, "--setup-command", "make", "--setup-variable", "VERICAIRN_HOME").Code);
            Assert.Equal(2, CliHarness.Run("worker", "configure", "--repository", repository, "--clear-setup", "--setup-variable", "PERSON_SETTING").Code);
            JsonElement unnamed = CliHarness.Ok("--json", "worker", "configure", "--repository", repository, "--setup-command", "make").Json;
            Assert.Empty(unnamed.GetProperty("variables").EnumerateArray());
            Assert.Contains($"Workspace setup for repository {repository}: [\"make\"], deadline 300 seconds, with a harness's environment.",
                CliHarness.Ok("worker", "configure", "--repository", repository).Out, StringComparison.Ordinal);
            JsonElement configured = CliHarness.Ok("--json", "worker", "configure", "--repository", repository, "--setup-command", FakeHarness.Setting("setup-install", setup), "--setup-timeout", "120",
                "--setup-variable", "PERSON_SETTING", "--setup-variable", "NPM_CONFIG_REGISTRY").Json;
            Assert.Equal(repository, configured.GetProperty("repository").GetString());
            Assert.Equal(120, configured.GetProperty("timeoutSeconds").GetInt32());
            Assert.Equal(FakeHarness.Command("setup-install", setup), configured.GetProperty("command").EnumerateArray().Select(a => a.GetString()!));
            Assert.Equal(["PERSON_SETTING", "NPM_CONFIG_REGISTRY"], configured.GetProperty("variables").EnumerateArray().Select(a => a.GetString()!));
            Assert.Contains(CliHarness.Ok("--json", "worker", "configure").Json.GetProperty("setup").EnumerateArray(), e => e.GetProperty("repository").GetString() == repository);
            Assert.Contains($"Workspace setup for repository {repository}: [", CliHarness.Ok("worker", "configure").Out, StringComparison.Ordinal);
            Assert.Contains("passing PERSON_SETTING, NPM_CONFIG_REGISTRY beside a harness's environment.", CliHarness.Ok("worker", "configure").Out, StringComparison.Ordinal);

            CliHarness.Ok(run);
            JsonElement recorded = Assert.Single(CliHarness.Ok("--estate", root, "--json", "worker", "runs").Json.GetProperty("runs").EnumerateArray());
            Assert.Equal("observed-success", recorded.GetProperty("status").GetString());
            Assert.Single(FakeHarness.Reports(setup));
            Assert.True(File.Exists(Path.Combine(workspace, "node_modules", "left-pad", "index.js")));
            Assert.DoesNotContain(setup, recorded.GetRawText(), StringComparison.Ordinal);

            CliHarness.Outcome cleared = CliHarness.Ok("worker", "configure", "--repository", repository, "--clear-setup");
            Assert.Contains("Removed the workspace setup command.", cleared.Out, StringComparison.Ordinal);
            Assert.Contains($"Workspace setup for repository {repository}: none.", cleared.Out, StringComparison.Ordinal);
            Assert.Contains("There was no workspace setup command to remove.", CliHarness.Ok("worker", "configure", "--repository", repository, "--clear-setup").Out, StringComparison.Ordinal);
            Assert.Equal(0, CliHarness.Run("--estate", root, "estate", "check").Code);
        }
        finally
        {
            if (resource is not null) new WorkerConfigurationStore().ClearSetup(null, resource);
            CliHarness.Delete(root, scratch);
        }
    }

    private static void Git(string directory, params string[] arguments)
    {
        var info = new ProcessStartInfo("git") { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, WorkingDirectory = directory };
        foreach (string argument in GitIdentity.Concat(arguments)) info.ArgumentList.Add(argument);
        using Process process = Process.Start(info)!;
        string error = process.StandardError.ReadToEnd();
        Assert.True(process.WaitForExit(TestTiming.HangTimeoutMilliseconds), "The fixture process did not exit.");
        Assert.True(process.ExitCode == 0, $"git {string.Join(' ', arguments)}: {error}");
    }
}
