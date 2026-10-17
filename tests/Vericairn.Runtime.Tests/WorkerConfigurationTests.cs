// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.Json;
using Vericairn.Packs.Software;
using Vericairn.Runtime.Providers;
using Vericairn.Runtime.Workers;

namespace Vericairn.Runtime.Tests;

public sealed class WorkerConfigurationTests : IDisposable
{
    private readonly string home = Path.Combine(Path.GetTempPath(), "worker-config-" + Guid.NewGuid().ToString("N"));
    private static readonly JsonElement Cap = JsonSerializer.SerializeToElement(new { wallMinutes = 1 });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CodexMaxDefaultsAndAllowlistsReachAdmission(bool allowlist)
    {
        var launcher = new CodexLauncher();
        var store = new WorkerConfigurationStore(home);
        store.Write(launcher, new("gpt-6-astra", "max", AllowedEfforts: allowlist ? ["high", "max"] : null));
        WorkerConfiguration stored = store.Read(launcher)!;
        Assert.Equal("gpt-6-astra", stored.Model);
        Assert.Equal("max", stored.Effort);
        Assert.Equal(allowlist ? ["high", "max"] : null, stored.AllowedEfforts);
        RunConfiguration configuration = store.Resolve(launcher, "codex")!;
        var selection = RunSelection.Resolve(configuration, launcher.ExecutionCapabilities, [], null, Cap);
        Assert.Equal("gpt-6-astra", selection["requested"]!["model"]!.GetValue<string>());
        Assert.Equal("max", selection["requested"]!["effort"]!.GetValue<string>());
        AssertOwnerOnly(Path.Combine(home, "worker", "codex.json"));
        Assert.Throws<ProviderRefusedException>(() => store.Write(launcher, new("gpt-6-astra", "unlimited")));
        Assert.Equal("max", store.Read(launcher)!.Effort);
        Assert.Throws<ProviderRefusedException>(() => RunSelection.Resolve(configuration, launcher.ExecutionCapabilities, [],
            JsonSerializer.SerializeToElement(new { effort = "unlimited" }), Cap));
    }

    [Theory]
    [InlineData("codex")]
    [InlineData("claude-code")]
    public void LocalDefaultsAndAllowlistsUseRunOnePrecedenceWithoutAddingCapabilities(string name)
    {
        IAgentLauncher launcher = name == "codex" ? new CodexLauncher() : new ClaudeCodeLauncher();
        var store = new WorkerConfigurationStore(home);
        Assert.Null(store.Read(launcher));
        store.Write(launcher, new("model-a", "high", ["model-a", "model-b"], ["high", "low"]));
        RunConfiguration configuration = store.Resolve(launcher, name)!;
        Assert.Null(configuration.EnvironmentPolicy);
        var defaults = RunSelection.Resolve(configuration, launcher.ExecutionCapabilities, [], null, Cap);
        Assert.Equal("model-a", defaults["requested"]!["model"]!.GetValue<string>());
        Assert.Equal("high", defaults["requested"]!["effort"]!.GetValue<string>());
        JsonElement choices = JsonSerializer.SerializeToElement(new { model = "model-b", effort = "low" });
        Assert.Equal("model-b", RunSelection.Resolve(configuration, launcher.ExecutionCapabilities, [], choices, Cap)["requested"]!["model"]!.GetValue<string>());
        RunPolicySource inherited = new("goal", "goal", JsonSerializer.SerializeToElement(new { restrictions = new { requiredModel = "model-a" } }));
        Assert.Throws<ProviderRefusedException>(() => RunSelection.Resolve(configuration, launcher.ExecutionCapabilities, [inherited], choices, Cap));
        Assert.Throws<ProviderRefusedException>(() => RunSelection.Resolve(configuration, launcher.ExecutionCapabilities, [], JsonSerializer.SerializeToElement(new { effort = "medium" }), Cap));
        Assert.Null(store.Resolve(launcher, "other-name"));
        AssertOwnerOnly(Path.Combine(home, "worker", name + ".json"));
        store.Write(launcher, new("model-b", "low"));
        Assert.Equal("model-b", store.Read(launcher)!.Model);
        Assert.Equal(["model-b"], store.Resolve(launcher, null)!.Models);
        AssertOwnerOnly(Path.Combine(home, "worker", name + ".json"));
    }

    [Theory]
    [InlineData("{\"model\":\"model-a\",\"effort\":\"high\",\"environmentPolicy\":{\"tools\":[\"extra\"]}}")]
    [InlineData("{\"model\":\"model-a\",\"effort\":\"high\",\"settings\":{\"command\":\"extra\"}}")]
    [InlineData("{\"model\":\"model-a\",\"effort\":\"high\",\"allowedEfforts\":[\"high\",\"unlimited\"]}")]
    [InlineData("{\"model\":\"model-a\",\"effort\":\"high\",\"allowedModels\":[]}")]
    [InlineData("{\"model\":\"model-a\",\"effort\":\"high\",\"allowedModels\":[\"model-b\"]}")]
    [InlineData("{\"model\":\"model-a\",\"effort\":\"high\",\"allowedEfforts\":[null]}")]
    [InlineData("{\"model\":\"model-a\",\"model\":\"model-b\",\"effort\":\"high\"}")]
    [InlineData("{\"model\":\"relative/path\",\"effort\":\"high\"}")]
    [InlineData("null")]
    [InlineData("{")]
    public void MalformedOrWideningFilesRefuseInsteadOfFallingBack(string content)
    {
        Directory.CreateDirectory(Path.Combine(home, "worker"));
        File.WriteAllText(Path.Combine(home, "worker", "codex.json"), content);
        Assert.Throws<ProviderRefusedException>(() => new WorkerConfigurationStore(home).Resolve(new CodexLauncher(), null));
    }

    [Fact]
    public void InvalidReplacementLeavesTheLastConfigurationIntact()
    {
        var store = new WorkerConfigurationStore(home);
        var launcher = new CodexLauncher();
        store.Write(launcher, new("model-a", "high"));
        Assert.Throws<ProviderRefusedException>(() => store.Write(launcher, new("model-a", "high", AllowedEfforts: ["high", "unlimited"])));
        Assert.Equal(new WorkerConfiguration("model-a", "high"), store.Read(launcher));
        Assert.Throws<ProviderRefusedException>(() => store.Write(new GenericLauncher(), new("model-a", "high")));
    }

    [Theory]
    [InlineData(".git")]
    [InlineData("estate/manifest.json")]
    public void ConfigurationCannotBeWrittenIntoARepositoryOrEstate(string marker)
    {
        string path = Path.Combine(home, marker);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "marker");
        Assert.Throws<ProviderRefusedException>(() => new WorkerConfigurationStore(Path.Combine(home, "local")).Write(new CodexLauncher(), new("model-a", "high")));
        Assert.False(Directory.Exists(Path.Combine(home, "local")));
    }

    [Fact]
    public void WorkspaceRootsAreMachineLocalOwnerOnlyAndReadAfresh()
    {
        var store = new WorkerConfigurationStore(home);
        Assert.Null(store.ReadWorkspaceRoot());
        store.Write(new CodexLauncher(), new("model-a", "high"));
        string first = Path.Combine(home, "runs");
        store.WriteWorkspaceRoot(first);
        Assert.Equal(HarnessWorkspace.Canonical(first), store.ReadWorkspaceRoot());
        AssertOwnerOnly(Path.Combine(home, "worker", "workspaces.json"));
        if (!OperatingSystem.IsWindows())
            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute, File.GetUnixFileMode(first));
        else
        {
            using WindowsIdentity identity = WindowsIdentity.GetCurrent();
            foreach (FileSystemAccessRule rule in new DirectoryInfo(first).GetAccessControl().GetAccessRules(true, true, typeof(SecurityIdentifier)))
                if (rule.AccessControlType == AccessControlType.Allow) Assert.Equal(identity.User, rule.IdentityReference);
        }
        string second = Path.Combine(home, "other-runs");
        new WorkerConfigurationStore(home).WriteWorkspaceRoot(second);
        Assert.Equal(HarnessWorkspace.Canonical(second), store.ReadWorkspaceRoot());
        Assert.Equal("model-a", store.Read(new CodexLauncher())!.Model);
        Assert.Throws<ProviderRefusedException>(() => store.WriteWorkspaceRoot("relative"));
        Assert.Equal(HarnessWorkspace.Canonical(second), store.ReadWorkspaceRoot());
        Directory.CreateDirectory(Path.Combine(second, ".git"));
        Assert.Throws<ProviderRefusedException>(() => store.ReadWorkspaceRoot());
    }

    [Theory]
    [InlineData(".git")]
    [InlineData("estate/manifest.json")]
    public void WorkspaceRootsCannotBeInsideRepositoriesOrEstates(string marker)
    {
        string parent = Path.Combine(home, "source");
        string path = Path.Combine(parent, marker);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "marker");
        var store = new WorkerConfigurationStore(home);
        Assert.Throws<ProviderRefusedException>(() => store.WriteWorkspaceRoot(Path.Combine(parent, "runs")));
        Assert.False(Directory.Exists(Path.Combine(parent, "runs")));
        Assert.Null(store.ReadWorkspaceRoot());
    }

    [Fact]
    public void BareRepositoriesAreNotMachineLocalConfigurationOrWorkspaceRoots()
    {
        string repository = Path.Combine(home, "bare");
        Directory.CreateDirectory(repository);
        RunEstate.Git(repository, "init", "--bare", "-q");
        Assert.Throws<ProviderRefusedException>(() => new WorkerConfigurationStore(home).WriteWorkspaceRoot(Path.Combine(repository, "runs")));
        Assert.Throws<ProviderRefusedException>(() => new WorkerConfigurationStore(Path.Combine(repository, "local")).WriteWorkspaceRoot(Path.Combine(home, "runs")));
    }

    [Theory]
    [InlineData(".claude/settings.json")]
    [InlineData(".codex/config.toml")]
    [InlineData(".mcp.json")]
    [InlineData(".agents/skills/extra/SKILL.md")]
    public void WorkspaceRootsRefuseTheirOwnAndAncestorHarnessConfiguration(string relative)
    {
        string parent = Path.Combine(home, "user");
        string path = Path.Combine(parent, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "unqualified");
        var store = new WorkerConfigurationStore(home);
        foreach (string root in new[] { parent, Path.Combine(parent, "runs") })
        {
            string message = Assert.Throws<ProviderRefusedException>(() => store.WriteWorkspaceRoot(root)).Message;
            Assert.Contains(relative.Split('/')[0], message, StringComparison.Ordinal);
            Assert.Contains("vericairn worker configure --workspaces", message, StringComparison.Ordinal);
        }
        Assert.Null(store.ReadWorkspaceRoot());
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("null")]
    [InlineData("{")]
    [InlineData("{\"root\":null}")]
    [InlineData("{\"root\":\"relative\"}")]
    [InlineData("{\"root\":\"relative\",\"command\":\"extra\"}")]
    public void InvalidWorkspaceFilesNeverFallBackToTheDefault(string json)
    {
        Directory.CreateDirectory(Path.Combine(home, "worker"));
        File.WriteAllText(Path.Combine(home, "worker", "workspaces.json"), json);
        Assert.Throws<ProviderRefusedException>(() => new WorkerConfigurationStore(home).ReadWorkspaceRoot());
    }

    [Fact]
    public void WorkspaceFilesRejectUnknownAndDuplicateFieldsEvenWithAValidRoot()
    {
        var store = new WorkerConfigurationStore(home);
        store.WriteWorkspaceRoot(Path.Combine(home, "runs"));
        string path = Path.Combine(home, "worker", "workspaces.json");
        string root = JsonSerializer.Serialize(Path.Combine(home, "runs"));
        foreach (string content in new[] { "{\"root\":" + root + ",\"tools\":[]}", "{\"root\":" + root + ",\"root\":" + root + "}" })
        {
            File.WriteAllText(path, content);
            Assert.Throws<ProviderRefusedException>(() => store.ReadWorkspaceRoot());
        }
    }

    [Fact]
    public void WorkspaceConfigurationAndRootsCannotUseLinks()
    {
        if (OperatingSystem.IsWindows()) return; // Link creation needs privileges not assumed by the test runner.
        Directory.CreateDirectory(Path.Combine(home, "target"));
        string alias = Path.Combine(home, "alias");
        Directory.CreateSymbolicLink(alias, Path.Combine(home, "target"));
        var store = new WorkerConfigurationStore(home);
        Assert.Throws<ProviderRefusedException>(() => store.WriteWorkspaceRoot(alias));
        Directory.CreateDirectory(Path.Combine(home, "worker"));
        File.CreateSymbolicLink(Path.Combine(home, "worker", "workspaces.json"), Path.Combine(home, "target", "elsewhere.json"));
        Assert.Throws<ProviderRefusedException>(() => store.ReadWorkspaceRoot());
        Assert.Throws<ProviderRefusedException>(() => store.WriteWorkspaceRoot(Path.Combine(home, "runs")));
        Assert.False(File.Exists(Path.Combine(home, "target", "elsewhere.json")));
    }

    [Fact]
    public void ALinkedAncestorOfTheHomeAllowsAbsentAndConfiguredWorkerSettings()
    {
        if (OperatingSystem.IsWindows()) return; // Link creation needs privileges not assumed by the test runner.
        string physical = Path.Combine(home, "physical");
        Directory.CreateDirectory(physical);
        string alias = Path.Combine(home, "alias");
        Directory.CreateSymbolicLink(alias, physical);
        var store = new WorkerConfigurationStore(Path.Combine(alias, "local"));
        var launcher = new CodexLauncher();
        Assert.Null(store.Read(launcher));
        Assert.Null(store.ReadWorkspaceRoot());
        store.Write(launcher, new("model-a", "high"));
        Assert.Equal("model-a", store.Read(launcher)!.Model);
        string root = Path.Combine(home, "runs");
        store.WriteWorkspaceRoot(root);
        Assert.Equal(HarnessWorkspace.Canonical(root), store.ReadWorkspaceRoot());
        AssertOwnerOnly(Path.Combine(physical, "local", "worker", "codex.json"));
        AssertOwnerOnly(Path.Combine(physical, "local", "worker", "workspaces.json"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AWorkspaceRootWithALinkedAncestorIsStoredAndReadAsItsCanonicalPath(bool exists)
    {
        if (OperatingSystem.IsWindows()) return; // Link creation needs privileges not assumed by the test runner.
        string physical = Path.Combine(home, "physical");
        Directory.CreateDirectory(physical);
        string alias = Path.Combine(home, "alias");
        Directory.CreateSymbolicLink(alias, physical);
        string root = Path.Combine(alias, "nested", "runs");
        if (exists) Directory.CreateDirectory(root);
        var store = new WorkerConfigurationStore(home);
        store.WriteWorkspaceRoot(root);
        string canonical = HarnessWorkspace.Canonical(Path.Combine(physical, "nested", "runs"));
        Assert.Equal(canonical, store.ReadWorkspaceRoot());
        using JsonDocument configuration = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(home, "worker", "workspaces.json")));
        Assert.Equal(canonical, configuration.RootElement.GetProperty("root").GetString());
        if (!exists)
            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute, File.GetUnixFileMode(canonical));

        // Once stored, moving the alias cannot redirect a run to another root.
        Directory.Delete(alias);
        string other = Path.Combine(home, "other");
        Directory.CreateDirectory(other);
        Directory.CreateSymbolicLink(alias, other);
        Assert.Equal(canonical, store.ReadWorkspaceRoot());
        Assert.False(Directory.Exists(Path.Combine(other, "nested")));
    }

    [Theory]
    [InlineData(".git")]
    [InlineData("estate/manifest.json")]
    [InlineData(".claude/settings.json")]
    [InlineData(".codex/config.toml")]
    [InlineData(".mcp.json")]
    [InlineData(".agents/skills/extra/SKILL.md")]
    public void LinkedAncestorsCannotHideRepositoryEstateOrHarnessConfiguration(string marker)
    {
        if (OperatingSystem.IsWindows()) return; // Link creation needs privileges not assumed by the test runner.
        string physical = Path.Combine(home, "physical");
        Directory.CreateDirectory(Path.Combine(physical, "nested"));
        string path = Path.Combine(physical, marker);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "marker");
        string alias = Path.Combine(home, "alias");
        Directory.CreateSymbolicLink(alias, Path.Combine(physical, "nested"));
        string root = Path.Combine(alias, "runs");
        var store = new WorkerConfigurationStore(home);
        string refusal = Assert.Throws<ProviderRefusedException>(() => store.WriteWorkspaceRoot(root)).Message;
        if (marker is ".git" or "estate/manifest.json")
        {
            Assert.Contains("outside repositories and estates", refusal, StringComparison.Ordinal);
            var hidden = new WorkerConfigurationStore(Path.Combine(alias, "local"));
            Assert.Contains("outside repositories and estates", Assert.Throws<ProviderRefusedException>(() => hidden.ReadWorkspaceRoot()).Message, StringComparison.Ordinal);
            Assert.Contains("outside repositories and estates", Assert.Throws<ProviderRefusedException>(() => hidden.Write(new CodexLauncher(), new("model-a", "high"))).Message, StringComparison.Ordinal);
        }
        else
        {
            Assert.Contains(Path.Combine(HarnessWorkspace.Canonical(physical), marker.Split('/')[0]), refusal, StringComparison.Ordinal);
        }
        Assert.False(Directory.Exists(root));
        Assert.Null(store.ReadWorkspaceRoot());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TheHomeAndWorkerDirectoryThemselvesStillRefuseLinks(bool worker)
    {
        if (OperatingSystem.IsWindows()) return; // Link creation needs privileges not assumed by the test runner.
        string target = Path.Combine(home, "target");
        Directory.CreateDirectory(target);
        string local = Path.Combine(home, "local");
        if (worker) Directory.CreateDirectory(local);
        Directory.CreateSymbolicLink(worker ? Path.Combine(local, "worker") : local, target);
        var store = new WorkerConfigurationStore(local);
        Assert.Throws<ProviderRefusedException>(() => store.ReadWorkspaceRoot());
        Assert.Throws<ProviderRefusedException>(() => store.WriteWorkspaceRoot(Path.Combine(home, "runs")));
        Assert.Throws<ProviderRefusedException>(() => store.Read(new CodexLauncher()));
        Assert.Throws<ProviderRefusedException>(() => store.Write(new CodexLauncher(), new("model-a", "high")));
        Assert.Empty(Directory.EnumerateFileSystemEntries(target));
    }

    private static void AssertOwnerOnly(string path)
    {
        if (OperatingSystem.IsWindows())
        {
            using WindowsIdentity identity = WindowsIdentity.GetCurrent();
            FileSecurity access = new FileInfo(path).GetAccessControl();
            foreach (FileSystemAccessRule rule in access.GetAccessRules(true, true, typeof(SecurityIdentifier)))
                if (rule.AccessControlType == AccessControlType.Allow) Assert.Equal(identity.User, rule.IdentityReference);
        }
        else
        {
            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(path));
            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute, File.GetUnixFileMode(Path.GetDirectoryName(path)!));
        }
    }

    [Fact]
    public void AConfigurationLinkCannotWriteElsewhereOrHideARepository()
    {
        if (OperatingSystem.IsWindows()) return; // Creating links requires privileges not assumed by the test runner.
        string repository = Path.Combine(home, "repository");
        Directory.CreateDirectory(Path.Combine(repository, "nested"));
        Directory.CreateDirectory(Path.Combine(repository, ".git"));
        string alias = Path.Combine(home, "alias");
        Directory.CreateSymbolicLink(alias, Path.Combine(repository, "nested"));
        Assert.Throws<ProviderRefusedException>(() => new WorkerConfigurationStore(Path.Combine(alias, "local")).Write(new CodexLauncher(), new("model-a", "high")));
        var store = new WorkerConfigurationStore(home);
        Directory.CreateDirectory(Path.Combine(home, "worker"));
        File.CreateSymbolicLink(Path.Combine(home, "worker", "codex.json"), Path.Combine(repository, "target.json"));
        Assert.Throws<ProviderRefusedException>(() => store.Write(new CodexLauncher(), new("model-a", "high")));
        Assert.Throws<ProviderRefusedException>(() => store.Read(new CodexLauncher()));
        Assert.False(File.Exists(Path.Combine(repository, "target.json")));
    }

    public void Dispose()
    {
        if (Directory.Exists(home)) Directory.Delete(home, recursive: true);
    }
}
