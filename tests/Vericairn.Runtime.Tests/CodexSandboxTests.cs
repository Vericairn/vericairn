// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using Vericairn.Runtime.Providers;
using Vericairn.Runtime.Workers;

namespace Vericairn.Runtime.Tests;

[Collection("home")]
public class CodexSandboxTests
{
    private const string Prefix = "sandbox_workspace_write={writable_roots=";

    [Fact]
    public void OnlyWorkspaceGitIsWritableInTheGeneratedConfigurationLaunchAndProbe()
    {
        using var fixture = new NativeProbe("codex");
        Directory.CreateDirectory(Path.Combine(fixture.Context.Workspace, ".git"));
        LaunchContext context = fixture.Prepare();
        string configuration = File.ReadAllText(Path.Combine(context.Environment["CODEX_HOME"], "config.toml"));
        IReadOnlyList<string> command = fixture.Launcher.Command(context);
        string[] expected = [Path.Combine(context.Workspace, ".git")];
        Assert.Equal(expected, Roots(configuration.Split('\n')));
        Assert.Equal(expected, Roots(command));
        Assert.DoesNotContain("--add-dir", command);
        Assert.DoesNotContain("approval_policy", configuration, StringComparison.Ordinal);
        Assert.DoesNotContain("approvals_reviewer", configuration, StringComparison.Ordinal);
        Assert.True(fixture.Launcher.Preflight(context).Complete);
        Assert.Equal(expected, Roots(JsonSerializer.Deserialize<string[]>(File.ReadAllText(fixture.Report + ".feature-arguments.json"))!));
        Assert.False(File.Exists(fixture.Report));
    }

    [Theory]
    [InlineData("outside")]
    [InlineData("clone")]
    [InlineData("workspace")]
    [InlineData("extra-inside")]
    [InlineData("codex")]
    [InlineData("agents")]
    [InlineData("duplicate")]
    [InlineData("none")]
    public void TheConfigurationProbeRefusesEveryOtherWritableRoot(string change)
    {
        using var fixture = new NativeProbe("codex");
        LaunchContext context = fixture.Prepare();
        string git = Path.Combine(context.Workspace, ".git");
        string[] roots = change switch
        {
            "outside" => [git, fixture.Root],
            "clone" => [Path.Combine(fixture.Root, "registered-clone", ".git")],
            "workspace" => [context.Workspace],
            "extra-inside" => [git, Path.Combine(context.Workspace, "nested")],
            "codex" => [git, Path.Combine(context.Workspace, ".codex")],
            "agents" => [git, Path.Combine(context.Workspace, ".agents")],
            "duplicate" => [git, git],
            _ => [],
        };
        string path = Path.Combine(context.Environment["CODEX_HOME"], "config.toml");
        string[] configuration = File.ReadAllLines(path);
        string original = Assert.Single(configuration, line => line.StartsWith(Prefix, StringComparison.Ordinal));
        File.WriteAllText(path, string.Join('\n', configuration.Select(line => line == original ? Prefix + JsonSerializer.Serialize(roots) + "}" : line)) + "\n");
        Assert.Contains("isolated configuration", Assert.Throws<ProviderRefusedException>(() => fixture.Launcher.Preflight(context)).Message, StringComparison.Ordinal);
        Assert.False(File.Exists(fixture.Report + ".probed"));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void SharedGitAdministrationCannotBeGrantedWriteAccess(bool gitFile, bool afterPreparation)
    {
        using var fixture = new NativeProbe("codex");
        LaunchContext? prepared = afterPreparation ? fixture.Prepare() : null;
        string git = Path.Combine(fixture.Context.Workspace, ".git");
        string cloneGit = Path.Combine(fixture.Root, "registered-clone", ".git");
        Directory.CreateDirectory(cloneGit);
        if (gitFile) File.WriteAllText(git, "gitdir: " + cloneGit + "\n");
        else
        {
            Directory.CreateDirectory(git);
            File.WriteAllText(Path.Combine(git, "commondir"), cloneGit + "\n");
        }
        if (prepared is not null) Assert.Throws<ProviderRefusedException>(() => fixture.Launcher.Preflight(prepared));
        else Assert.Throws<ProviderRefusedException>(() => fixture.Prepare());
        Assert.False(File.Exists(fixture.Report + ".probed"));
    }

    [Fact]
    public void ALinkedGitDirectoryCannotGrantAccessToTheClone()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Symbolic-link creation requires a Windows privilege not granted by this fixture.");
        using var fixture = new NativeProbe("codex");
        string cloneGit = Path.Combine(fixture.Root, "registered-clone", ".git");
        Directory.CreateDirectory(cloneGit);
        Directory.CreateSymbolicLink(Path.Combine(fixture.Context.Workspace, ".git"), cloneGit);
        Assert.Throws<ProviderRefusedException>(() => fixture.Prepare());
    }

    [Fact]
    public void AWorkerDispatchGrantsOnlyItsOwnGitDirectory()
    {
        using var fixture = new NativeRun("codex");
        var started = fixture.Start();
        Assert.True(started.Ok, started.Message);
        Assert.Equal("observed-success", fixture.Run.Settled(started.Run).Status);
        string[] command = JsonSerializer.Deserialize<string[]>(File.ReadAllText(fixture.Probe.Report))!;
        string workspace = command[Array.IndexOf(command, "--cd") + 1];
        Assert.Equal([Path.Combine(workspace, ".git")], Roots(command));
        Assert.DoesNotContain(Path.Combine(fixture.Run.Clone, ".git"), Roots(command));
        Assert.Equal(0, fixture.Run.Service.Check().Errors);
    }

    private static string[] Roots(IEnumerable<string> configuration)
    {
        string value = Assert.Single(configuration, line => line.StartsWith(Prefix, StringComparison.Ordinal));
        return JsonSerializer.Deserialize<string[]>(value[Prefix.Length..^1])!;
    }
}
