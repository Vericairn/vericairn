// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Diagnostics;

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
            CliHarness.Ok("--estate", root, "--json", "estate", "init", "--name", "Example", "--slug", "example", "--purpose", "Runtime start.", "--operator-name", "Operator", "--organization", "Example", "--commit", Commit);
            string executable = Path.Combine(AppContext.BaseDirectory, OperatingSystem.IsWindows() ? "vericairn.exe" : "vericairn");
            Assert.True(File.Exists(executable), executable);

            var info = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
            foreach (string argument in new[] { "--estate", root, "runtime", "start" })
            {
                info.ArgumentList.Add(argument);
            }

            using Process process = Process.Start(info)!;
            Task<string> output = process.StandardOutput.ReadToEndAsync(TestContext.Current.CancellationToken);
            Task<string> error = process.StandardError.ReadToEndAsync(TestContext.Current.CancellationToken);
            Task both = Task.WhenAll(output, error);
            Task finished = await Task.WhenAny(both, Task.Delay(TimeSpan.FromSeconds(90), TestContext.Current.CancellationToken));
            try
            {
                Assert.True(finished == both, "The output of 'runtime start' did not end within 90 seconds: the daemon holds the caller's output open.");
                await process.WaitForExitAsync(TestContext.Current.CancellationToken);
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
}
