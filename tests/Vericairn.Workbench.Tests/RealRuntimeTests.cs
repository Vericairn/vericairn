// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using Vericairn.Records.Signing;

namespace Vericairn.Workbench.Tests;

/// <summary>
/// The production path on a first launch: no daemon serves the estate, so Workbench starts one through the real command line
/// beside it (not a stand-in), connects over the real transport and opens the estate. Release 0.3.0 waited here forever.
/// </summary>
[Collection("workbench")]
public class RealRuntimeTests(HeadlessFixture fixture)
{
    static RealRuntimeTests()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable(VericairnHome.EnvironmentVariable)))
        {
            Environment.SetEnvironmentVariable(VericairnHome.EnvironmentVariable, Seed.Temp("workbench-home"));
        }
    }

    [Fact]
    public async Task OnAFirstLaunchWorkbenchStartsTheDaemonThroughTheCommandLineAndOpensTheEstate()
    {
        string root = Seed.Temp("wb-real-runtime");
        Seed.Init(root);
        string cli = Path.Combine(AppContext.BaseDirectory, OperatingSystem.IsWindows() ? "vericairn.exe" : "vericairn");
        Assert.True(File.Exists(cli), cli);
        string? previous = Environment.GetEnvironmentVariable("VERICAIRN_CLI");
        Environment.SetEnvironmentVariable("VERICAIRN_CLI", cli);
        var shell = new ShellViewModel(r => new DaemonConnection(r), new CliRuntimeManager(), new Preferences(Seed.Temp("wb-prefs-real-runtime")));
        bool opened = false;
        try
        {
            Assert.Equal(0, Seed.Run("--estate", root, "runtime", "status"));
            Task open = fixture.UiAsync(() => shell.OpenEstateAsync(root));
            opened = await Task.WhenAny(open, Task.Delay(TimeSpan.FromSeconds(120), TestContext.Current.CancellationToken)) == open;
            Assert.True(opened, $"Workbench did not finish opening the estate within 120 seconds; status: {shell.Status}; activity: {string.Join(" / ", shell.Activity)}");
            await open;
            Assert.True(shell.HasEstate, shell.Status);
            Assert.Equal("daemon", shell.RuntimeMode);
            Assert.Contains("process", shell.RuntimeWhere, StringComparison.Ordinal);
            Assert.Equal("Example Estate", shell.EstateName);

            ReleaseWorkspace page = await fixture.Ui(shell.OpenRelease);
            Assert.Same(page, shell.SelectedDocument);

            // Stopping goes through the same runtime manager and the real command line.
            await fixture.UiAsync(shell.StopRuntimeAsync);
            Assert.Equal("unavailable", shell.RuntimeMode);
        }
        finally
        {
            // The daemon is stopped directly as well: when opening never finished, the UI dispatcher is still busy with it, and
            // stopping the daemon is what lets that work end.
            Seed.Run("--estate", root, "runtime", "stop");
            Environment.SetEnvironmentVariable("VERICAIRN_CLI", previous);
        }

        Assert.Contains("no daemon", Seed.Cli("--estate", root, "runtime", "status"), StringComparison.Ordinal);
    }
}
