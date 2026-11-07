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
    public void SetupRunsWithAHarnesssFilteredEnvironmentPlusOnlyTheVariablesItNames()
    {
        // Credential-shaped names are assembled here, so no literal credential name or value sits in the source.
        string token = "NPM_" + "TOKEN", key = "ANTHROPIC_" + "API_KEY", value = "registry-" + Ulid.NewUlid().Value;
        var source = new Dictionary<string, string>(WorkerEnvironment.Names)
        {
            ["PATH"] = "/usr/bin", ["HOME"] = "/home/person", ["LC_ALL"] = "C", ["DOTNET_ROOT"] = "/opt/dotnet", [token] = value, [key] = value,
            ["NODE_OPTIONS"] = "--require ./hook.js", ["DYLD_INSERT_LIBRARIES"] = "hook.dylib", ["PERSON_SETTING"] = "on", ["NPM_CONFIG_REGISTRY"] = "https://registry.example.invalid",
            ["VERICAIRN_HOME"] = "/home", ["vericairn_run"] = "run", ["VERICAIRN_ENCLAVE_HELPER"] = "helper",
        };

        // Without names, it receives what a harness would: no credential, no loader directive, nothing outside the allowlist.
        Assert.Equal(["DOTNET_ROOT", "HOME", "LC_ALL", "PATH"], WorkspaceSetup.Environment(source, []).Keys.Order(StringComparer.Ordinal));

        // A named variable passes as the person holds it, even a credential or a loader directive; an absent name adds nothing.
        Dictionary<string, string> named = WorkspaceSetup.Environment(source, [token, "NODE_OPTIONS", "NPM_CONFIG_REGISTRY", "ABSENT_SETTING"]);
        Assert.Equal(["DOTNET_ROOT", "HOME", "LC_ALL", "NODE_OPTIONS", "NPM_CONFIG_REGISTRY", token, "PATH"], named.Keys.Order(StringComparer.Ordinal));
        Assert.Equal(value, named[token]);

        // Vericairn's own bindings never pass, even when the source holds them under an allowed name.
        Assert.DoesNotContain(WorkspaceSetup.Environment(source, ["vericairn_run"]).Keys, name => name.StartsWith("VERICAIRN_", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void TheVariablesASetupNamesAreStoredValidatedAndOptional()
    {
        var store = new WorkerConfigurationStore(home);
        string repository = Ulid.NewUlid().Value;
        string file = Path.Combine(home, "worker", "setup-repository-" + repository + ".json");

        // Without names, the file is what 0.9.1 wrote and read.
        store.WriteSetup(null, repository, new() { Command = ["npm", "ci"] });
        Assert.DoesNotContain("variables", File.ReadAllText(file), StringComparison.Ordinal);
        Assert.Null(store.ReadSetup("codex", [repository])!.Variables);
        File.WriteAllText(file, "{\"command\":[\"npm\",\"ci\"],\"timeoutSeconds\":120}");
        Assert.Equal(120, store.ReadSetup("codex", [repository])!.TimeoutSeconds);

        store.WriteSetup(null, repository, new() { Command = ["npm", "ci"], Variables = ["NPM_CONFIG_REGISTRY", "ProgramFiles(x86)"] });
        Assert.Equal(["NPM_CONFIG_REGISTRY", "ProgramFiles(x86)"], store.ReadSetup("codex", [repository])!.Variables);
        Assert.Equal(["NPM_CONFIG_REGISTRY", "ProgramFiles(x86)"], Assert.Single(store.ListSetup()).Setup!.Variables);

        foreach (IReadOnlyList<string> invalid in new IReadOnlyList<string>[]
        {
            ["VERICAIRN_HOME"], ["vericairn_run"], ["NAME=VALUE"], [""], ["A B"], [new string('A', 161)], ["SAME", "SAME"],
            [.. Enumerable.Range(0, WorkspaceSetupConfiguration.MaximumVariables + 1).Select(i => "NAME_" + i.ToString(System.Globalization.CultureInfo.InvariantCulture))],
        })
            Assert.Throws<ProviderRefusedException>(() => store.WriteSetup(null, repository, new() { Command = ["tool"], Variables = invalid }));
        Assert.Equal(["NPM_CONFIG_REGISTRY", "ProgramFiles(x86)"], store.ReadSetup("codex", [repository])!.Variables);

        // A file edited by hand to name one refuses, as any invalid file does.
        File.WriteAllText(file, "{\"command\":[\"tool\"],\"variables\":[\"VERICAIRN_HOME\"]}");
        Assert.Throws<ProviderRefusedException>(() => store.ReadSetup("codex", [repository]));
    }
}
