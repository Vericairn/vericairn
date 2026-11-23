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

    // From 0.9.4 (FIX-165): a setup command that is a batch program the workspace holds, named relative to it, runs through cmd.exe
    // from the workspace, receives its arguments, gives its exit code, and what it leaves running is ended with the job.
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void OnWindowsARelativeBatchProgramRunsFromTheWorkspaceWithItsArgumentsAndItsExitCode(int code)
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "A batch program runs on Windows only; a POSIX script of the workspace is checked above.");
        string workspace = Directory.CreateDirectory(Path.Combine(root, "workspace")).FullName;
        string scripts = Directory.CreateDirectory(Path.Combine(workspace, "scripts")).FullName;
        string report = Path.Combine(root, "setup.jsonl");
        string child = Path.Combine(root, "descendant.jsonl");
        BatchScript.Write(scripts, "probe.cmd",
        [
            "@echo off",
            ">given.txt echo(%~1",
            BatchScript.Line(FakeHarness.Command("generic", report)) + " %*",
            .. BatchScript.Descendant(child),
            $"exit /b {code}",
        ]);

        WorkspaceSetupResult result = WorkspaceSetup.Run(new() { Command = [@"scripts\probe.cmd", "a b"] }, workspace, WorkerEnvironment.Current());

        Assert.Equal("exited", result.Status);
        Assert.Equal(code, result.ExitCode);
        Assert.Equal(code == 0, result.Succeeded);
        Assert.Equal("a b", File.ReadAllText(Path.Combine(workspace, "given.txt")).TrimEnd('\r', '\n'));
        Assert.Equal(["a b"], Assert.Single(FakeHarness.Reports(report))["arguments"]!.AsArray().Select(a => a!.GetValue<string>()));
        Assert.True(BatchScript.DescendantEnded(child), "The setup command's descendant outlived it.");
    }

    [Fact]
    public void OnWindowsABatchSetupArgumentCmdCannotBeGivenAsDataRunsNothing()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "A batch program runs on Windows only; the refusals are checked on every platform.");
        string workspace = Directory.CreateDirectory(Path.Combine(root, "workspace")).FullName;
        string marker = Path.Combine(root, "ran.txt");
        BatchScript.Write(workspace, "probe.cmd", $">\"{marker}\" echo ran", "exit /b 0");
        foreach (string argument in new[] { "100%", "a\nb" })
        {
            Assert.Equal("not-started", WorkspaceSetup.Run(new() { Command = [@".\probe.cmd", argument] }, workspace, WorkerEnvironment.Current()).Status);
            Assert.False(File.Exists(marker), "The batch program ran.");
        }

        Assert.True(WorkspaceSetup.Run(new() { Command = [@".\probe.cmd", "a b"] }, workspace, WorkerEnvironment.Current()).Succeeded);
        Assert.True(File.Exists(marker));
    }

    [Fact]
    public void ABatchSetupCommandsArgumentsCmdCannotBeGivenAsDataAreRefusedWhenConfiguredOnWindows()
    {
        var store = new WorkerConfigurationStore(Directory.CreateDirectory(Path.Combine(root, "home")).FullName);
        string repository = Ulid.NewUlid().Value;
        foreach (string[] command in new string[][] { [@"scripts\setup.cmd", "100%"], ["setup.bat", "a", "line\nbreak"] })
        {
            if (OperatingSystem.IsWindows())
            {
                ProviderRefusedException refused = Assert.Throws<ProviderRefusedException>(() => store.WriteSetup(null, repository, new() { Command = command }));
                Assert.Contains("batch program can never be started", refused.Message, StringComparison.Ordinal);
            }
            else
            {
                // Linux and macOS run no batch program: the name is an ordinary executable's.
                store.WriteSetup(null, repository, new() { Command = command });
            }
        }

        store.WriteSetup(null, repository, new() { Command = [@"scripts\setup.cmd", "a b", "x\"y", "&|<>^!"] });
        Assert.Equal([@"scripts\setup.cmd", "a b", "x\"y", "&|<>^!"], Assert.Single(store.ListSetup()).Setup!.Command);
    }

    // From 0.9.4 (FIX-165): an absolute path without an extension starts the .exe, else the .com, of that name in that folder.
    [Fact]
    public void OnWindowsAnAbsoluteSetupExecutableWithoutAnExtensionStartsItsExeOrItsCom()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Windows reads a program's extension; the rule is checked on every platform.");
        string workspace = Directory.CreateDirectory(Path.Combine(root, "workspace")).FullName;
        string report = Path.Combine(root, "setup.jsonl");
        List<string> command = FakeHarness.Command("generic", report);
        Assert.EndsWith(".exe", command[0], StringComparison.OrdinalIgnoreCase);

        WorkspaceSetupResult result = WorkspaceSetup.Run(new() { Command = [Path.ChangeExtension(command[0], null), .. command.Skip(1)] }, workspace, WorkerEnvironment.Current());

        Assert.True(result.Succeeded, result.Facts);
        Assert.Equal("generic", Assert.Single(FakeHarness.Reports(report))["scenario"]!.GetValue<string>());

        string chcp = Path.Combine(Environment.SystemDirectory, "chcp");
        Assert.SkipUnless(File.Exists(chcp + ".com") && !File.Exists(chcp + ".exe"), "This Windows has no chcp.com alone in its system directory.");
        Assert.Equal("exited", WorkspaceSetup.Run(new() { Command = [chcp] }, workspace, WorkerEnvironment.Current()).Status);
    }
}
