// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json.Nodes;
using Vericairn.Records.Identity;
using Vericairn.Runtime.Providers;
using Vericairn.Runtime.Workers;

namespace Vericairn.Runtime.Tests;

/// <summary>
/// From 0.9.4 (FIX-161): a workspace setup command's executable given as a relative path with a directory part is the workspace's file,
/// started by its absolute path; a bare name is found in an absolute directory of PATH; an executable no reading can start is refused
/// when it is configured, and a stored one when it is read.
/// </summary>
public sealed class WorkspaceSetupProgramTests : IDisposable
{
    private readonly string root = Directory.CreateTempSubdirectory("vericairn-setup-program-").FullName;

    public void Dispose() => WorkspaceCleanup.Delete(root);

    [Fact]
    public void ARelativeExecutableWithADirectoryPartIsTheWorkspacesFile()
    {
        string workspace = Directory.CreateDirectory(Path.Combine(root, "workspace")).FullName;
        string report = Path.Combine(root, "setup.jsonl");
        List<string> command = FakeHarness.Command("generic", report);
        string relative = Path.GetRelativePath(workspace, command[0]);
        Assert.SkipWhen(Path.IsPathFullyQualified(relative), "The test executable is on another volume than the workspace, so no relative path reaches it.");
        Assert.True(File.Exists(Path.Combine(workspace, relative)));

        WorkspaceSetupResult result = WorkspaceSetup.Run(new() { Command = [relative, .. command.Skip(1)] }, workspace, WorkerEnvironment.Current());

        Assert.True(result.Succeeded, result.Facts);
        JsonObject given = Assert.Single(FakeHarness.Reports(report));
        Assert.Equal("generic", given["scenario"]!.GetValue<string>());
    }

    [Fact]
    public void AScriptOfTheWorkspaceNamedFromItRunsThere()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "A POSIX shell script; a Windows script is verified on a Windows machine.");
        if (OperatingSystem.IsWindows()) return;
        string workspace = Directory.CreateDirectory(Path.Combine(root, "workspace")).FullName;
        string script = Path.Combine(Directory.CreateDirectory(Path.Combine(workspace, "scripts")).FullName, "setup.sh");
        File.WriteAllText(script, "#!/bin/sh\necho ran > setup-ran.txt\n");
        File.SetUnixFileMode(script, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);

        WorkspaceSetupResult result = WorkspaceSetup.Run(new() { Command = ["./scripts/setup.sh"] }, workspace, WorkerEnvironment.Current());

        Assert.True(result.Succeeded, result.Facts);
        Assert.Equal("ran", File.ReadAllText(Path.Combine(workspace, "setup-ran.txt")).Trim());
    }

    [Fact]
    public void ABareExecutableIsLookedUpOnPathAndNeverInTheWorkspace()
    {
        string workspace = Directory.CreateDirectory(Path.Combine(root, "workspace")).FullName;
        string name = "vericairn-absent-" + Guid.NewGuid().ToString("N");
        File.WriteAllText(Path.Combine(workspace, OperatingSystem.IsWindows() ? name + ".exe" : name), "#!/bin/sh\nexit 0\n");
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(Path.Combine(workspace, name), UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);

        Assert.Equal("not-started", WorkspaceSetup.Run(new() { Command = [name] }, workspace, WorkerEnvironment.Current()).Status);
    }

    [Fact]
    public void AnExecutableNoReadingCanStartIsRefusedWhenConfiguredAndWhenRead()
    {
        var store = new WorkerConfigurationStore(Directory.CreateDirectory(Path.Combine(root, "home")).FullName);
        string repository = Ulid.NewUlid().Value;
        List<string> never = ["scripts/", ".", Path.Combine("scripts", "..")];
        if (OperatingSystem.IsWindows()) never.AddRange([@"\tools\setup.cmd", "C:setup.cmd"]);
        foreach (string executable in never)
        {
            ProviderRefusedException refused = Assert.Throws<ProviderRefusedException>(() => store.WriteSetup(null, repository, new() { Command = [executable, "install"] }));
            Assert.Contains("can never be started", refused.Message, StringComparison.Ordinal);
        }

        Assert.Empty(store.ListSetup());
        store.WriteSetup(null, repository, new() { Command = [Path.Combine(".", "scripts", "setup.sh")] });
        store.WriteSetup("codex", null, new() { Command = ["npm", "ci"] });
        Assert.Equal(2, store.ListSetup().Count(e => e.Setup is not null));

        // A stored executable that can never start refuses when read, names its file, and nothing runs in its place.
        File.WriteAllText(Path.Combine(root, "home", "worker", "setup-repository-" + repository + ".json"), "{\"command\":[\"scripts/\"]}");
        ProviderRefusedException unreadable = Assert.Throws<ProviderRefusedException>(() => store.ReadSetup("codex", [repository]));
        Assert.Contains($"'setup-repository-{repository}.json' could not be read", unreadable.Message, StringComparison.Ordinal);
        Assert.Contains("can never be started", unreadable.InnerException!.Message, StringComparison.Ordinal);
    }
}
