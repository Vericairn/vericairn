// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using Vericairn.Records.Identity;
using Vericairn.Runtime.Providers;
using Vericairn.Runtime.Workers;

namespace Vericairn.Runtime.Tests;

/// <summary>
/// Workspace setup commands in the machine's worker configuration (FIX-86): one per launcher or repository resource, a repository's
/// before its launcher's, owner-only, validated, cleared, and never a fallback when unreadable.
/// </summary>
public sealed class WorkspaceSetupStoreTests : IDisposable
{
    private readonly string home = Directory.CreateTempSubdirectory("vericairn-setup-home-").FullName;

    public void Dispose() => WorkspaceCleanup.Delete(home);

    [Fact]
    public void ARepositorysSetupComesBeforeItsLaunchersAndClearingLeavesTheOther()
    {
        var store = new WorkerConfigurationStore(home);
        string first = Ulid.NewUlid().Value;
        string latest = Ulid.NewUlid().Value;
        Assert.Null(store.ReadSetup("codex", [first, latest]));
        Assert.Empty(store.ListSetup());

        store.WriteSetup("codex", null, new() { Command = ["npm", "ci", "--ignore-scripts", "--no-bin-links"] });
        store.WriteSetup(null, first, new() { Command = ["pnpm", "install", "--frozen-lockfile"], TimeoutSeconds = 120 });

        WorkspaceSetupConfiguration chosen = store.ReadSetup("codex", [first, latest])!;
        Assert.Equal(["pnpm", "install", "--frozen-lockfile"], chosen.Command);
        Assert.Equal(120, chosen.TimeoutSeconds);
        Assert.Equal(["npm", "ci", "--ignore-scripts", "--no-bin-links"], store.ReadSetup("codex", [latest])!.Command);
        Assert.Equal(WorkspaceSetupConfiguration.DefaultTimeoutSeconds, store.ReadSetup("codex", [latest])!.TimeoutSeconds);
        Assert.Null(store.ReadSetup("claude-code", [latest]));
        Assert.Null(store.ReadSetup("Host.Launcher", [latest]));
        Assert.Equal([("codex", null), (null, first)], store.ListSetup().Select(e => (e.Launcher, e.Repository)));
        if (!OperatingSystem.IsWindows())
            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(Path.Combine(home, "worker", "setup-repository-" + first + ".json")));

        Assert.True(store.ClearSetup(null, first));
        Assert.False(store.ClearSetup(null, first));
        Assert.Equal(["npm", "ci", "--ignore-scripts", "--no-bin-links"], store.ReadSetup("codex", [first])!.Command);
    }

    [Fact]
    public void ACommandIsAnExecutableOrAJsonArrayAndInvalidSetupsAreRefused()
    {
        Assert.Equal(["make"], WorkerConfigurationStore.SetupCommand(" make "));
        Assert.Equal(["npm", "ci"], WorkerConfigurationStore.SetupCommand("[\"npm\",\"ci\"]"));
        foreach (string malformed in new[] { "", "[]", "[\"\"]", "[\"npm\",1]", "[\"npm\"" })
            Assert.Throws<ProviderRefusedException>(() => WorkerConfigurationStore.SetupCommand(malformed));

        var store = new WorkerConfigurationStore(home);
        string repository = Ulid.NewUlid().Value;
        foreach (WorkspaceSetupConfiguration invalid in new WorkspaceSetupConfiguration[]
        {
            new(), new() { Command = [" "] }, new() { Command = ["tool", "a\0b"] },
            new() { Command = ["tool"], TimeoutSeconds = 0 }, new() { Command = ["tool"], TimeoutSeconds = WorkspaceSetupConfiguration.MaximumTimeoutSeconds + 1 },
        })
            Assert.Throws<ProviderRefusedException>(() => store.WriteSetup(null, repository, invalid));
        Assert.Throws<ProviderRefusedException>(() => store.WriteSetup("codex", repository, new() { Command = ["tool"] }));
        Assert.Throws<ProviderRefusedException>(() => store.WriteSetup(null, null, new() { Command = ["tool"] }));
        Assert.Throws<ProviderRefusedException>(() => store.WriteSetup(null, "not-a-resource", new() { Command = ["tool"] }));
        Assert.Throws<ProviderRefusedException>(() => store.WriteSetup("../codex", null, new() { Command = ["tool"] }));
        Assert.Empty(store.ListSetup());

        // A file edited by hand into something else refuses, names itself, and nothing else is used in its place.
        store.WriteSetup("codex", null, new() { Command = ["tool"] });
        File.WriteAllText(Path.Combine(home, "worker", "setup-repository-" + repository + ".json"), "{\"command\":[\"tool\"],\"environment\":{}}");
        ProviderRefusedException refused = Assert.Throws<ProviderRefusedException>(() => store.ReadSetup("codex", [repository]));
        Assert.Contains($"'setup-repository-{repository}.json' could not be read", refused.Message, StringComparison.Ordinal);
        WorkspaceSetupEntry listed = Assert.Single(store.ListSetup(), e => e.Repository == repository);
        Assert.Null(listed.Setup);
        Assert.NotNull(listed.Problem);
    }

    [Fact]
    public void SetupRunsWithThePersonsEnvironmentLessVericairnsBindings()
    {
        var source = new Dictionary<string, string>(WorkerEnvironment.Names)
        {
            ["PATH"] = "/usr/bin", ["NPM_TOKEN"] = "registry-" + Ulid.NewUlid().Value, ["DOTNET_ROOT"] = "/opt/dotnet",
            ["VERICAIRN_HOME"] = "/home", ["vericairn_run"] = "run", ["VERICAIRN_ENCLAVE_HELPER"] = "helper",
        };
        Dictionary<string, string> environment = WorkspaceSetup.Environment(source);
        Assert.Equal(["DOTNET_ROOT", "NPM_TOKEN", "PATH"], environment.Keys.Order(StringComparer.Ordinal));
    }
}
