// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Diagnostics;
using System.Text.Json;
using Vericairn.Records.Signing;
using Vericairn.Runtime.Tests;

namespace Vericairn.Cli.Tests;

/// <summary>The built CLI and daemon use only their stock composition and native-protocol stand-ins.</summary>
[Collection("cli")]
public class WorkerConfigurationJourneyTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task StockWorkerLoadsLocalConfigurationBeforeDispatch(bool daemon)
    {
        _ = CliHarness.Run("--help");
        string scratch = CliHarness.TempDirectory("stock-worker");
        string root = Path.Combine(scratch, "estate");
        string home = Path.Combine(scratch, "home");
        string user = Path.Combine(scratch, "user");
        string report = Path.Combine(scratch, "dispatch.json");
        Directory.CreateDirectory(user);
        string? previousHome = Environment.GetEnvironmentVariable(VericairnHome.EnvironmentVariable);
        Environment.SetEnvironmentVariable(VericairnHome.EnvironmentVariable, home);
        try
        {
            (string op, string agent, _, string assignment, string repository) = WorkerJourneyTests.Estate(root, scratch);
            if (daemon) Success(await Binary("runtime", "start"));
            string[] run = ["worker", "run", assignment, "--launcher", "codex", "--agent", agent, "--by", op, "--repository", repository,
                "--setting", "command=" + FakeHarness.Setting("native-codex", report)];
            CliHarness.Outcome missing = await Binary([.. run, "--execution", "model=model-a", "--execution", "effort=high"]);
            Assert.Equal(2, missing.Code);
            Assert.Contains("vericairn worker configure --launcher codex --model <model> --effort <effort>", missing.Err, StringComparison.Ordinal);
            Assert.False(File.Exists(report + ".probed"));
            Assert.Empty((await Binary("--json", "worker", "runs")).Json.GetProperty("runs").EnumerateArray());

            Success(await Binary("worker", "configure", "--launcher", "codex", "--model", "model-a", "--effort", "high",
                "--allow-model", "model-a", "--allow-model", "model-b", "--allow-effort", "high"));
            JsonElement shown = Success(await Binary("--json", "worker", "configure", "--launcher", "codex")).Json;
            Assert.Equal("model-a", shown.GetProperty("model").GetString());
            Assert.Equal("high", shown.GetProperty("effort").GetString());
            Assert.Equal(["model-a", "model-b"], shown.GetProperty("allowedModels").EnumerateArray().Select(v => v.GetString()));
            foreach (string choice in new[] { "model=model-c", "effort=low", "destination=unapproved" })
            {
                Assert.Equal(2, (await Binary([.. run, "--execution", choice])).Code);
                Assert.False(File.Exists(report + ".probed"));
            }

            // A configured model never grants another server, even at the shipped baseline version.
            File.WriteAllText(report + ".behavior", """{"mcp":"ambient"}""");
            Assert.Equal(2, (await Binary(run)).Code);
            Assert.False(File.Exists(report));
            Assert.Empty((await Binary("--json", "worker", "runs")).Json.GetProperty("runs").EnumerateArray());
            File.Delete(report + ".behavior");

            Success(await Binary([.. run, "--execution", "model=model-b"]));
            string[] arguments = JsonSerializer.Deserialize<string[]>(File.ReadAllText(report))!;
            Assert.Equal("model-b", arguments[Array.IndexOf(arguments, "--model") + 1]);
            Assert.Contains("model_reasoning_effort=\"high\"", arguments);
            JsonElement recorded = Assert.Single(Success(await Binary("--json", "worker", "runs")).Json.GetProperty("runs").EnumerateArray());
            Assert.Equal("observed-success", recorded.GetProperty("status").GetString());
            Assert.Equal("baseline", recorded.GetProperty("execution").GetProperty("harnessAdmission").GetProperty("kind").GetString());
            Assert.Contains("qualified-codex", recorded.GetProperty("execution").GetProperty("environment").GetProperty("policy").GetString(), StringComparison.Ordinal);
            Assert.DoesNotContain(home, recorded.GetRawText(), StringComparison.Ordinal);
            Assert.DoesNotContain(Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories), f => Path.GetFileName(f) == "codex.json");
            Success(await Binary("estate", "check"));
        }
        finally
        {
            if (daemon) await Binary("runtime", "stop");
            Environment.SetEnvironmentVariable(VericairnHome.EnvironmentVariable, previousHome);
            CliHarness.Delete(scratch);
        }

        async Task<CliHarness.Outcome> Binary(params string[] arguments)
        {
            var info = new ProcessStartInfo(Path.Combine(AppContext.BaseDirectory, OperatingSystem.IsWindows() ? "vericairn.exe" : "vericairn"))
            {
                UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true,
            };
            foreach (string argument in new[] { "--estate", root }.Concat(arguments)) info.ArgumentList.Add(argument);
            info.Environment[VericairnHome.EnvironmentVariable] = home;
            info.Environment["HOME"] = user;
            info.Environment["USERPROFILE"] = user;
            info.Environment["CODEX_HOME"] = Path.Combine(user, "codex");
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
    }

    private static CliHarness.Outcome Success(CliHarness.Outcome outcome)
    {
        Assert.True(outcome.Code == 0, outcome.Out + outcome.Err);
        return outcome;
    }
}
