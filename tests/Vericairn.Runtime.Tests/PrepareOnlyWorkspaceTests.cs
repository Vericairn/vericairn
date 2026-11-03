// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json.Nodes;
using Vericairn.Packs.Software;
using Vericairn.Protocol;
using Vericairn.Runtime.Workers;

namespace Vericairn.Runtime.Tests;

/// <summary>
/// A run's workspace prepared alone (FIX-86, issue 238): prepared and registered as a run would, nothing recorded and nothing started,
/// so that a person installs dependencies there; the run that follows takes it up after checking what changed since.
/// </summary>
[Collection("home")]
public sealed class PrepareOnlyWorkspaceTests
{
    [Fact]
    public void APreparationAloneRecordsNothingAndItsRunTakesTheWorkspaceUp()
    {
        using RunEstate run = Ignoring(new RunEstate());
        int records = run.Estate.Load().ById.Count;
        string report = run.Report("prepared");

        RunResponse prepared = run.Service.RunPrepare(Request(run, report));

        Assert.True(prepared.Ok, prepared.Message);
        Assert.Equal("prepared", prepared.Status);
        Assert.Null(prepared.Operation);
        string workspace = prepared.Workspace!;
        Assert.Equal("work/w-1", RunEstate.Git(workspace, "symbolic-ref", "--short", "HEAD"));
        Assert.True(File.Exists(Path.Combine(workspace, ".vericairn", "brief.md")));
        Assert.Contains("Nothing was recorded, and no setup command or harness ran.", prepared.Message, StringComparison.Ordinal);
        Assert.Equal(records, run.Estate.Load().ById.Count);
        Assert.Empty(run.Service.Runs().Runs);
        Assert.False(File.Exists(report));
        string checkpoint = Checkpoint(run);
        Assert.True(File.Exists(checkpoint));
        Assert.False(checkpoint.StartsWith(workspace, StringComparison.Ordinal));
        if (!OperatingSystem.IsWindows()) Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(checkpoint));

        // A person installs where the repository ignores it. Preparing again checks that, as the run will, and keeps the checkpoint.
        Install(workspace);
        string saved = File.ReadAllText(checkpoint);
        RunResponse again = run.Service.RunPrepare(Request(run, report));
        Assert.True(again.Ok, again.Message);
        Assert.Equal(workspace, again.Workspace);
        Assert.Contains("was already prepared", again.Message, StringComparison.Ordinal);
        Assert.Equal(saved, File.ReadAllText(checkpoint));
        Assert.Equal(records, run.Estate.Load().ById.Count);

        RunResponse started = run.Start("claude-code", "claude", report);
        Assert.True(started.Ok, started.Message);
        RunRow settled = run.Settled(started.Run);
        Assert.Equal("observed-success", settled.Status);
        Assert.Equal(HarnessWorkspace.Canonical(workspace), HarnessWorkspace.Canonical(FakeHarness.Reports(report)[^1]["directory"]!.GetValue<string>()));
        Assert.True(File.Exists(Path.Combine(workspace, "node_modules", "left-pad", "index.js")));
        Assert.Empty(settled.Scope!.Outside);
        Assert.False(File.Exists(checkpoint));
    }

    [Theory]
    [InlineData("tracked", "README.md")]
    [InlineData("untracked", "notes.txt")]
    [InlineData("committed", "docs/notes.md")]
    [InlineData("link", "node_modules/.bin/left-pad")]
    public void AChangeOutsideThePermittedPathsOrANewLinkSincePreparingRefusesTheRunAndNamesIt(string change, string named)
    {
        using RunEstate run = Ignoring(new RunEstate());
        RunResponse prepared = run.Service.RunPrepare(Request(run, run.Report("prepared")));
        Assert.True(prepared.Ok, prepared.Message);
        string workspace = prepared.Workspace!;
        Install(workspace);
        switch (change)
        {
            case "tracked":
                File.AppendAllText(Path.Combine(workspace, "README.md"), "changed by hand\n");
                break;
            case "untracked":
                File.WriteAllText(Path.Combine(workspace, "notes.txt"), "outside the brief\n");
                break;
            case "committed":
                Directory.CreateDirectory(Path.Combine(workspace, "docs"));
                File.WriteAllText(Path.Combine(workspace, "docs", "notes.md"), "outside the brief\n");
                RunEstate.Git(workspace, "add", "docs/notes.md");
                RunEstate.Git(workspace, "commit", "-q", "-m", "A commit outside the brief");
                break;
            default:
                Directory.CreateDirectory(Path.Combine(workspace, "node_modules", ".bin"));
                File.CreateSymbolicLink(Path.Combine(workspace, "node_modules", ".bin", "left-pad"), Path.Combine("..", "left-pad", "index.js"));
                break;
        }

        int records = run.Estate.Load().ById.Count;
        RunResponse refused = run.Start("claude-code", "claude", run.Report("refused"));

        Assert.False(refused.Ok);
        Assert.Equal("refused", refused.Problem!.Code);
        Assert.Contains(named, refused.Message, StringComparison.Ordinal);
        Assert.Contains(change == "link" ? "symbolic link" : "changed outside the assignment's permitted changes since it was prepared", refused.Message, StringComparison.Ordinal);
        Assert.Contains("Nothing was recorded.", refused.Message, StringComparison.Ordinal);
        Assert.Equal(records, run.Estate.Load().ById.Count);
        Assert.Empty(run.Service.Runs().Runs);
        // The person's workspace, what they installed and the checkpoint all stay.
        Assert.True(File.Exists(Path.Combine(workspace, "node_modules", "left-pad", "index.js")));
        Assert.True(File.Exists(Checkpoint(run)));
    }

    /// <summary>The run of the fixture's assignment through the claude-code launcher, the stand-in reporting to a file.</summary>
    internal static RunStartRequest Request(RunEstate run, string report) =>
        new(run.Assignment, "claude-code", run.Estate.BuilderId, run.Estate.OperatorId, run.Resource,
            Settings: new Dictionary<string, string>(StringComparer.Ordinal) { ["command"] = FakeHarness.Setting("claude", report) });

    /// <summary>Where this machine keeps the preparation checkpoint of the fixture's repository and default branch.</summary>
    internal static string Checkpoint(RunEstate run) => new WorkspacePreparation(run.Service.Session.Manifest.Estate, run.Resource, "work/w-1").Location;

    /// <summary>The fixture's clone with a committed rule that ignores installed dependencies, as most repositories have.</summary>
    internal static RunEstate Ignoring(RunEstate run)
    {
        File.WriteAllText(Path.Combine(run.Clone, ".gitignore"), "node_modules/\n");
        RunEstate.Git(run.Clone, "add", ".gitignore");
        RunEstate.Git(run.Clone, "commit", "-q", "-m", "Ignore installed dependencies");
        return run;
    }

    /// <summary>Dependencies installed by hand into the ignored directory, without links.</summary>
    internal static void Install(string workspace)
    {
        Directory.CreateDirectory(Path.Combine(workspace, "node_modules", "left-pad"));
        File.WriteAllText(Path.Combine(workspace, "node_modules", "left-pad", "index.js"), "module.exports = text => text;\n");
        File.WriteAllText(Path.Combine(workspace, "node_modules", "left-pad", "package.json"), new JsonObject { ["name"] = "left-pad", ["version"] = "1.3.0" }.ToJsonString());
    }
}
