// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Diagnostics;
using System.Xml.Linq;

namespace Vericairn.Cli.Tests;

/// <summary>
/// Vericairn says it sends no identifiers or usage telemetry, and building it from source keeps that promise (FIX-77). Avalonia brings
/// in Avalonia.BuildServices, whose task sends anonymous build statistics before each compile; tools/BuildTelemetry.targets replaces
/// its target for every project, and every workflow that builds also sets the documented opt-out.
/// </summary>
public class BuildTelemetryTests
{
    private const string Package = "Avalonia.BuildServices";

    [Theory]
    [InlineData("src/Vericairn.Workbench/Vericairn.Workbench.csproj")]
    [InlineData("tests/Vericairn.Workbench.Tests/Vericairn.Workbench.Tests.csproj")]
    public async Task NoTargetThatRunsCanCallAvaloniasBuildStatisticsTask(string project)
    {
        XDocument evaluated = await Preprocess(Path.Combine(RepositoryRoot(), project));
        IEnumerable<XElement> all = evaluated.Descendants().Where(e => e.Name.LocalName is "UsingTask" or "Target");
        string[] tasks = all.Where(e => e.Name.LocalName == "UsingTask" && ((string?)e.Attribute("AssemblyFile") ?? "").Contains(Package, StringComparison.Ordinal))
            .Select(e => (string)e.Attribute("TaskName")!).ToArray();
        // While Avalonia brings the package in, this test must see its task; if a later Avalonia drops it, remove the override and this test.
        Assert.NotEmpty(tasks);
        // MSBuild runs the last definition of each target name.
        XElement[] effective = all.Where(e => e.Name.LocalName == "Target").GroupBy(e => (string)e.Attribute("Name")!).Select(g => g.Last()).ToArray();
        foreach (XElement target in effective)
        {
            string[] calls = target.Descendants().Select(e => e.Name.LocalName).Where(n => tasks.Contains(n, StringComparer.OrdinalIgnoreCase)).ToArray();
            Assert.True(calls.Length == 0, $"Target {(string)target.Attribute("Name")!} calls {string.Join(", ", calls)} from {Package}.");
        }
        Assert.Contains(effective, t => (string)t.Attribute("Name")! == "AvaloniaStats" && !t.HasElements);
    }

    [Fact]
    public void EveryWorkflowThatBuildsSetsTheOptOut()
    {
        string[] building = Directory.GetFiles(Path.Combine(RepositoryRoot(), ".github", "workflows"), "*.yml")
            .Where(f => File.ReadAllText(f).Contains("dotnet ", StringComparison.Ordinal)).ToArray();
        Assert.NotEmpty(building);
        foreach (string file in building)
        {
            string[] lines = File.ReadAllText(file).ReplaceLineEndings("\n").Split('\n');
            int env = Array.IndexOf(lines, "env:");
            Assert.True(env >= 0, $"{Path.GetFileName(file)} has no top-level env.");
            string[] block = lines.Skip(env + 1).TakeWhile(l => l.StartsWith("  ", StringComparison.Ordinal) || l.Length == 0).ToArray();
            Assert.Contains("  AVALONIA_TELEMETRY_OPTOUT: \"1\"", block);
        }
    }

    private static async Task<XDocument> Preprocess(string project)
    {
        string output = Path.Combine(Path.GetTempPath(), "vericairn-preprocess-" + Guid.NewGuid().ToString("N") + ".xml");
        var info = new ProcessStartInfo("dotnet")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = RepositoryRoot(),
        };
        // Preprocessing evaluates the project without running a target; the opt-out is set anyway.
        info.Environment["AVALONIA_TELEMETRY_OPTOUT"] = "1";
        foreach (string argument in new[] { "msbuild", project, "-pp:" + output, "-nologo" }) info.ArgumentList.Add(argument);
        using Process process = Process.Start(info)!;
        Task<string> stdout = process.StandardOutput.ReadToEndAsync(TestContext.Current.CancellationToken);
        Task<string> stderr = process.StandardError.ReadToEndAsync(TestContext.Current.CancellationToken);
        try
        {
            await process.WaitForExitAsync(TestContext.Current.CancellationToken).WaitAsync(TimeSpan.FromMinutes(2), TestContext.Current.CancellationToken);
            Assert.True(process.ExitCode == 0, await stdout + await stderr);
            return XDocument.Load(output);
        }
        finally
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync(CancellationToken.None);
            }
            File.Delete(output);
        }
    }

    private static string RepositoryRoot()
    {
        string? root = AppContext.BaseDirectory;
        while (root is not null && !File.Exists(Path.Combine(root, "Vericairn.slnx")))
        {
            root = Path.GetDirectoryName(root);
        }

        return root ?? throw new InvalidOperationException("The repository root was not found.");
    }
}
