// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Diagnostics;
using System.Text.Json;
using Vericairn.Records.Signing;
using Vericairn.Runtime.Tests;

namespace Vericairn.Cli.Tests;

/// <summary>Stock composition, built binaries and native-protocol stand-ins, with ordinary user configuration in HOME.</summary>
[Collection("cli")]
public class WorkspaceRootJourneyTests
{
    [Theory]
    [InlineData(false, "codex")]
    [InlineData(true, "codex")]
    [InlineData(false, "claude-code")]
    [InlineData(true, "claude-code")]
    public async Task AStockNativeRunCanMoveOutsideAUsersConfiguredHome(bool daemon, string launcher)
    {
        await using var journey = new Journey(daemon, launcher);
        await journey.Configure();
        if (daemon) Success(await journey.Binary("runtime", "start"));

        CliHarness.Outcome refused = await journey.Run();
        Assert.Equal(2, refused.Code);
        Assert.Contains(launcher == "codex" ? ".codex/config.toml" : ".claude/settings.json", refused.Err, StringComparison.Ordinal);
        Assert.Contains("vericairn worker configure --workspaces", refused.Err, StringComparison.Ordinal);
        Assert.False(File.Exists(journey.Report));
        Assert.Empty((await journey.Binary("--json", "worker", "runs")).Json.GetProperty("runs").EnumerateArray());

        CliHarness.Outcome invalid = await journey.Binary("worker", "configure", "--workspaces", Path.Combine(journey.User, "workspaces"));
        Assert.Equal(2, invalid.Code);
        Assert.Contains(".codex", invalid.Err, StringComparison.Ordinal);
        Assert.Contains(".claude", invalid.Err, StringComparison.Ordinal);
        Assert.False(Directory.Exists(Path.Combine(journey.User, "workspaces")));
        Success(await journey.Binary("worker", "configure", "--workspaces", journey.Workspaces));
        Assert.Equal(Path.Combine(journey.CanonicalScratch, "workspaces"), Success(await journey.Binary("--json", "worker", "configure")).Json.GetProperty("root").GetString());

        // The refused default run left its worktree for inspection. A new root never silently takes it over outside that root.
        string previous = Directory.EnumerateFiles(Path.Combine(journey.Home, "estates"), ".git", SearchOption.AllDirectories).Single();
        string oldWorkspace = Path.GetDirectoryName(previous)!;
        string canonicalWorkspace = Path.GetFullPath(Git(oldWorkspace, "rev-parse", "--show-toplevel"));
        refused = await journey.Run();
        Assert.Equal(2, refused.Code);
        Assert.Contains($"at '{canonicalWorkspace}'", refused.Err, StringComparison.Ordinal);
        Assert.Contains("worktree remove", refused.Err, StringComparison.Ordinal);
        Git(journey.Clone, "worktree", "remove", "--force", oldWorkspace);

        Success(await journey.Run());
        Assert.True(File.Exists(journey.Report));
        string workspace = Path.GetDirectoryName(Directory.EnumerateFiles(journey.Workspaces, ".git", SearchOption.AllDirectories).Single())!;
        Assert.EndsWith("work-w-1", workspace, StringComparison.Ordinal);
        Assert.Equal("work/w-1", Git(workspace, "symbolic-ref", "--short", "HEAD"));
        JsonElement recorded = Assert.Single(Success(await journey.Binary("--json", "worker", "runs")).Json.GetProperty("runs").EnumerateArray());
        Assert.Equal("observed-success", recorded.GetProperty("status").GetString());
        Assert.DoesNotContain(journey.Scratch, recorded.GetRawText(), StringComparison.Ordinal);
        Success(await journey.Binary("worker", "configure", "--workspaces", journey.Scratch));
        Success(await journey.Run());
        Assert.True(File.Exists(Path.Combine(workspace, ".git")));
        Assert.Equal(2, Success(await journey.Binary("--json", "worker", "runs")).Json.GetProperty("runs").GetArrayLength());
        AssertNoRecordedPaths(journey);
        Success(await journey.Binary("estate", "check"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AWorkspaceRootChangedBetweenIntentAndDispatchRefusesWithoutCharging(bool daemon)
    {
        await using var journey = new Journey(daemon, "codex");
        await journey.Configure();
        Success(await journey.Binary("worker", "configure", "--workspaces", journey.Workspaces));
        if (daemon) Success(await journey.Binary("runtime", "start"));
        File.WriteAllText(journey.Report + ".behavior", "{\"pauseVersion\":true}");
        Task<CliHarness.Outcome> running = journey.Run();
        for (int probe = 1; probe <= 2; probe++)
        {
            string gate = journey.Report + ".version-" + probe.ToString(System.Globalization.CultureInfo.InvariantCulture);
            DateTimeOffset until = DateTimeOffset.UtcNow.AddSeconds(30);
            while (!File.Exists(gate))
            {
                Assert.False(running.IsCompleted, running.IsCompleted ? (await running).Err : "");
                Assert.True(DateTimeOffset.UtcNow < until, "The native stand-in did not reach the probe barrier.");
                await Task.Delay(20, TestContext.Current.CancellationToken);
            }
            if (probe == 2)
            {
                string intent = Assert.Single(Directory.EnumerateFiles(Path.Combine(journey.Root, "records", "operation"), "*.json", SearchOption.AllDirectories));
                using JsonDocument record = JsonDocument.Parse(File.ReadAllBytes(intent));
                Assert.Equal("not-started", record.RootElement.GetProperty("body").GetProperty("status").GetString());
                Success(await journey.Binary("worker", "configure", "--workspaces", Path.Combine(journey.Scratch, "changed")));
            }
            File.WriteAllText(gate + ".continue", "continue");
        }
        CliHarness.Outcome refused = await running;
        Assert.Equal(2, refused.Code);
        Assert.Contains("workspace root changed", refused.Err, StringComparison.Ordinal);
        Assert.False(File.Exists(journey.Report));
        JsonElement settled = Assert.Single(Success(await journey.Binary("--json", "worker", "runs")).Json.GetProperty("runs").EnumerateArray());
        Assert.Equal("known-failure", settled.GetProperty("status").GetString());
        Assert.All(settled.GetProperty("charged").EnumerateObject(), unit => Assert.Equal(0, unit.Value.GetDecimal()));
        Assert.Equal(0, settled.GetProperty("usage").GetProperty("wallMinutes").GetInt64());
        AssertNoRecordedPaths(journey);
    }

    private static void AssertNoRecordedPaths(Journey journey)
    {
        foreach (string record in Directory.EnumerateFiles(Path.Combine(journey.Root, "records"), "*.json", SearchOption.AllDirectories))
        {
            using JsonDocument document = JsonDocument.Parse(File.ReadAllBytes(record));
            Check(document.RootElement);
        }
        void Check(JsonElement value)
        {
            if (value.ValueKind == JsonValueKind.String)
            {
                Assert.DoesNotContain(journey.Scratch, value.GetString()!, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain(journey.CanonicalScratch, value.GetString()!, StringComparison.OrdinalIgnoreCase);
            }
            else if (value.ValueKind == JsonValueKind.Object)
                foreach (JsonProperty property in value.EnumerateObject()) Check(property.Value);
            else if (value.ValueKind == JsonValueKind.Array)
                foreach (JsonElement item in value.EnumerateArray()) Check(item);
        }
    }

    internal sealed class Journey : IAsyncDisposable
    {
        private readonly string? previousHome;
        private readonly bool daemon;
        private readonly string launcher;
        private readonly string[] run;

        public Journey(bool daemon, string launcher)
        {
            _ = CliHarness.Run("--help");
            this.daemon = daemon;
            this.launcher = launcher;
            Scratch = CliHarness.TempDirectory("workspace-root");
            Root = Path.Combine(Scratch, "estate");
            User = Path.Combine(Scratch, "user");
            Home = Path.Combine(User, ".vericairn");
            Workspaces = Path.Combine(Scratch, "workspaces");
            Report = Path.Combine(Scratch, "dispatch.json");
            Directory.CreateDirectory(Path.Combine(User, ".claude"));
            Directory.CreateDirectory(Path.Combine(User, ".codex"));
            File.WriteAllText(Path.Combine(User, ".claude", "settings.json"), "{}");
            File.WriteAllText(Path.Combine(User, ".claude", ".credentials.json"), "{\"claudeAiOauth\":{\"subscriptionType\":\"pro\"}}");
            File.WriteAllText(Path.Combine(User, ".codex", "config.toml"), "# ordinary user configuration\n");
            previousHome = Environment.GetEnvironmentVariable(VericairnHome.EnvironmentVariable);
            Environment.SetEnvironmentVariable(VericairnHome.EnvironmentVariable, Home);
            (string op, string agent, _, string assignment, string repository) = WorkerJourneyTests.Estate(Root, Scratch);
            CanonicalScratch = Path.GetDirectoryName(Path.GetFullPath(Git(Clone, "rev-parse", "--show-toplevel")))!;
            run = ["worker", "run", assignment, "--launcher", launcher, "--agent", agent, "--by", op, "--repository", repository,
                "--setting", "command=" + FakeHarness.Setting(launcher == "codex" ? "native-codex" : "native-claude", Report)];
        }

        public string Scratch { get; }
        public string CanonicalScratch { get; }
        public string Root { get; }
        public string User { get; }
        public string Home { get; }
        public string Workspaces { get; }
        public string Report { get; }
        public string Clone => Path.Combine(Scratch, "widget");

        public async Task Configure() => Success(await Binary("worker", "configure", "--launcher", launcher, "--model", "model-a", "--effort", "high"));
        public Task<CliHarness.Outcome> Run() => Binary(run);

        public async Task<CliHarness.Outcome> Binary(params string[] arguments)
        {
            var info = new ProcessStartInfo(Path.Combine(AppContext.BaseDirectory, OperatingSystem.IsWindows() ? "vericairn.exe" : "vericairn"))
            {
                UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true,
            };
            foreach (string argument in new[] { "--estate", Root }.Concat(arguments)) info.ArgumentList.Add(argument);
            info.Environment[VericairnHome.EnvironmentVariable] = Home;
            info.Environment["HOME"] = User;
            info.Environment["USERPROFILE"] = User;
            info.Environment["CODEX_HOME"] = Path.Combine(User, ".codex");
            info.Environment["CLAUDE_CONFIG_DIR"] = Path.Combine(User, ".claude");
            using Process process = Process.Start(info)!;
            Task<string> output = process.StandardOutput.ReadToEndAsync(TestContext.Current.CancellationToken);
            Task<string> error = process.StandardError.ReadToEndAsync(TestContext.Current.CancellationToken);
            try
            {
                await process.WaitForExitAsync(TestContext.Current.CancellationToken).WaitAsync(TimeSpan.FromSeconds(90), TestContext.Current.CancellationToken);
                return new(process.ExitCode, await output, await error);
            }
            finally
            {
                if (!process.HasExited) process.Kill(entireProcessTree: true);
            }
        }

        public async ValueTask DisposeAsync()
        {
            if (daemon) await Binary("runtime", "stop");
            Environment.SetEnvironmentVariable(VericairnHome.EnvironmentVariable, previousHome);
            CliHarness.Delete(Scratch);
        }
    }

    private static string Git(string directory, params string[] arguments)
    {
        var info = new ProcessStartInfo("git") { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, WorkingDirectory = directory };
        foreach (string argument in arguments) info.ArgumentList.Add(argument);
        using Process process = Process.Start(info)!;
        string output = process.StandardOutput.ReadToEnd();
        string error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        Assert.True(process.ExitCode == 0, error);
        return output.Trim();
    }

    internal static CliHarness.Outcome Success(CliHarness.Outcome outcome)
    {
        Assert.True(outcome.Code == 0, outcome.Out + outcome.Err);
        return outcome;
    }
}
