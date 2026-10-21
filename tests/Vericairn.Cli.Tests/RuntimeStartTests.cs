// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Diagnostics;
using System.Text.Json;
using Vericairn.Runtime.Store;

namespace Vericairn.Cli.Tests;

/// <summary>
/// 'runtime start' run the way Workbench and scripts run it: as a separate process whose output is read to the end. The daemon
/// it starts must not hold that output open, or every caller waits for as long as the daemon serves (the 0.3.0 first-launch hang).
/// </summary>
[Collection("cli")]
public class RuntimeStartTests
{
    private const string Commit = "0123456789abcdef0123456789abcdef01234567";

    [Fact]
    public async Task StartingTheDaemonReleasesTheCallersOutputWhileTheDaemonServes()
    {
        string root = CliHarness.TempDirectory("runtime-start");
        try
        {
            CliHarness.RequireDaemonTransport();
            CliHarness.Ok("--estate", root, "--json", "estate", "init", "--name", "Example", "--slug", "example", "--purpose", "Runtime start.", "--operator-name", "Operator", "--organization", "Example", "--commit", Commit);
            string executable = CliHarness.BuiltExecutable;
            Assert.True(File.Exists(executable), "Missing prerequisite: the built vericairn CLI executable.");

            var info = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
            foreach (string argument in new[] { "--estate", root, "runtime", "start" })
            {
                info.ArgumentList.Add(argument);
            }

            using Process process = Process.Start(info)!;
            Task<string> output = process.StandardOutput.ReadToEndAsync(TestContext.Current.CancellationToken);
            Task<string> error = process.StandardError.ReadToEndAsync(TestContext.Current.CancellationToken);
            Task both = Task.WhenAll(output, error);
            bool finished = await TestTiming.CompletesAsync(both, TestContext.Current.CancellationToken);
            try
            {
                Assert.True(finished, "The output of 'runtime start' did not end before the hang guard: the daemon holds the caller's output open.");
                await process.WaitForExitAsync(TestContext.Current.CancellationToken).WaitAsync(TestTiming.HangTimeout, TestContext.Current.CancellationToken);
                Assert.True(process.ExitCode == 0, await output + await error);
                Assert.Contains("serves", await output, StringComparison.Ordinal);
                Assert.Contains("daemon", CliHarness.Ok("--estate", root, "runtime", "status").Out, StringComparison.Ordinal);
            }
            finally
            {
                CliHarness.Run("--estate", root, "runtime", "stop");
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }
            }

            Assert.Contains("no daemon", CliHarness.Ok("--estate", root, "runtime", "status").Out, StringComparison.Ordinal);
        }
        finally
        {
            CliHarness.Delete(root);
        }
    }

    [Fact]
    public async Task AStartThatFailsSaysWhyAndWhereTheDaemonsLogIs()
    {
        string root = CliHarness.TempDirectory("runtime-start-fails");
        try
        {
            CliHarness.Ok("--estate", root, "--json", "estate", "init", "--name", "Example", "--slug", "example", "--purpose", "Runtime start.", "--operator-name", "Operator", "--organization", "Example", "--commit", Commit);
            using JsonDocument manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "estate", "manifest.json")));
            string live = EstateSession.LiveStorePath(root, manifest.RootElement.GetProperty("estate").GetString()!, manifest.RootElement.GetProperty("profile").GetString()!);

            // A directory where the live store's file belongs: the daemon cannot open its store and stops before it serves.
            Directory.CreateDirectory(live);
            string executable = CliHarness.BuiltExecutable;
            var info = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
            foreach (string argument in new[] { "--estate", root, "runtime", "start" })
            {
                info.ArgumentList.Add(argument);
            }

            using Process process = Process.Start(info)!;
            Task<string> output = process.StandardOutput.ReadToEndAsync(TestContext.Current.CancellationToken);
            Task<string> error = process.StandardError.ReadToEndAsync(TestContext.Current.CancellationToken);
            Task both = Task.WhenAll(output, error);
            bool finished = await TestTiming.CompletesAsync(both, TestContext.Current.CancellationToken);
            try
            {
                Assert.True(finished, "The output of a failed 'runtime start' did not end before the hang guard.");
                await process.WaitForExitAsync(TestContext.Current.CancellationToken).WaitAsync(TestTiming.HangTimeout, TestContext.Current.CancellationToken);
                string log = Path.Combine(Path.GetDirectoryName(live)!, "daemon.log");
                string said = await error;
                Assert.True(process.ExitCode != 0, await output + said);
                Assert.Contains("before serving: ", said, StringComparison.Ordinal);
                Assert.Contains($"Its log is {log}.", said, StringComparison.Ordinal);
                Assert.False(string.IsNullOrWhiteSpace(File.ReadAllText(log)), "The daemon wrote nothing to its log.");
            }
            finally
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }
            }

            Assert.Contains("no daemon", CliHarness.Ok("--estate", root, "runtime", "status").Out, StringComparison.Ordinal);
        }
        finally
        {
            CliHarness.Delete(root);
        }
    }
}
