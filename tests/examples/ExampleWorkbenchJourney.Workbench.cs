// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Diagnostics;
using System.Text.Json;
using Vericairn.Records.Signing;
using Vericairn.Runtime.Workers;

namespace Vericairn.Workbench.Tests;

internal sealed class ExampleWorkbenchJourney(string scratch, IReadOnlyDictionary<string, string> captures) : IDisposable
{
    internal string Scratch => scratch;
    internal string Estate => Captures["estate"];
    internal string Home => Captures["home"];
    internal IReadOnlyDictionary<string, string> Captures { get; } = captures;

    internal ServiceConnection OpenConnection() => new(Estate, Home);

    // Replay the registered journey to the point Workbench inspects. The CLI test process keeps
    // its actual estate and workspaces at their original paths until this inspection finishes.
    internal static async Task<ExampleWorkbenchJourney> RunAsync(string journey, string variant, string checkpoint, CancellationToken cancellationToken)
    {
        string scratch = Seed.Temp("example-workbench");
        bool completed = false;
        try
        {
            File.WriteAllText(Path.Combine(scratch, "request.json"), JsonSerializer.Serialize(new { journey, variant, checkpoint }));
            DirectoryInfo output = new(AppContext.BaseDirectory);
            string driver = Path.Combine(output.Parent!.Parent!.FullName, "Vericairn.Cli.Tests", output.Name, "Vericairn.Cli.Tests.dll");
            Assert.True(File.Exists(driver), $"Missing example journey driver {driver}.");
            var start = new ProcessStartInfo(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet")
            {
                WorkingDirectory = scratch, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true,
            };
            start.ArgumentList.Add(driver);
            start.ArgumentList.Add("--filter-method");
            start.ArgumentList.Add("Vericairn.Cli.Tests.ExampleWorkbenchCheckpointTests.SaveJourneyCheckpointForWorkbench");
            string[] inherited = ["PATH", "SYSTEMROOT", "WINDIR", "COMSPEC", "PATHEXT", "TMP", "TEMP", "TMPDIR",
                "DOTNET_ROOT", "DOTNET_ROOT_X64", "DOTNET_ROOT_ARM64"];
            StringComparer names = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
            foreach (string name in start.Environment.Keys.ToArray())
                if (!inherited.Contains(name, names)) start.Environment.Remove(name);
            start.Environment["VERICAIRN_EXAMPLE_WORKBENCH_SCRATCH"] = scratch;
            start.Environment["HOME"] = scratch;
            start.Environment["USERPROFILE"] = scratch;
            start.Environment[VericairnHome.EnvironmentVariable] = Path.Combine(scratch, "driver-home");
            start.Environment["DOTNET_EnableDiagnostics"] = "0";

            using Process process = Process.Start(start) ?? throw new IOException("The example journey driver did not start.");
            Task<string> stdout = process.StandardOutput.ReadToEndAsync(cancellationToken);
            Task<string> stderr = process.StandardError.ReadToEndAsync(cancellationToken);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromMinutes(2));
            try
            {
                await process.WaitForExitAsync(timeout.Token);
            }
            catch (OperationCanceledException) when (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync(CancellationToken.None);
                throw;
            }
            string report = (await stdout) + (await stderr);
            Assert.True(process.ExitCode == 0, report);
            string captureFile = Path.Combine(scratch, "captures.json");
            Assert.True(File.Exists(captureFile), report);
            var captures = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(captureFile))!;
            var result = new ExampleWorkbenchJourney(scratch, captures);
            completed = true;
            return result;
        }
        finally
        {
            if (!completed) WorkspaceCleanup.Delete(scratch);
        }
    }

    public void Dispose() => WorkspaceCleanup.Delete(Scratch);
}
