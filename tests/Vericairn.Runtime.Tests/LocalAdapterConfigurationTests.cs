// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using Vericairn.Packs.Software;
using Vericairn.Records.Representation;
using Vericairn.Runtime.Providers;
using Vericairn.Runtime.Workers;

namespace Vericairn.Runtime.Tests;

public sealed class LocalAdapterConfigurationTests : IDisposable
{
    private readonly string home = Path.Combine(Path.GetTempPath(), "adapter-config-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void CallerMutationCannotWidenTheAdmittedLaunchersOrSupportedAdapters()
    {
        var launchers = new List<string> { "codex" };
        var adapters = new List<string> { RepresentationContract.LocalLossless };
        var permission = new EfficiencyAdapterConfiguration { Adapter = RepresentationContract.LocalLossless, Launchers = launchers };
        RunCapabilities capabilities = RunCapabilities.Unsupported with { Adapters = adapters };
        launchers.Add("claude-code");
        adapters[0] = "other";
        Assert.Equal(["codex"], permission.Launchers);
        Assert.Equal([RepresentationContract.LocalLossless], capabilities.Adapters);
        Assert.False(permission.Allows(RepresentationContract.LocalLossless, "claude-code"));
    }

    [Theory]
    [InlineData("codex")]
    [InlineData("claude-code")]
    public void MachineAdmissionAndAnExplicitRunChoiceAreBothRequired(string name)
    {
        IAgentLauncher launcher = name == "codex" ? new CodexLauncher() : new ClaudeCodeLauncher();
        var store = new WorkerConfigurationStore(home);
        store.Write(launcher, new("selected", "high"));
        Assert.Null(store.ReadAdapter());
        JsonElement cap = RunEstate.Json("{}");
        RunConfiguration configuration = store.Resolve(launcher, null)!;
        JsonElement choice = JsonSerializer.SerializeToElement(new { adapter = RepresentationContract.LocalLossless });
        foreach (string scope in new[] { "goal", "assignment", "roster" })
        {
            var source = new RunPolicySource(scope, scope, JsonSerializer.SerializeToElement(new { choices = choice }));
            Assert.Contains("efficiency-adapter", Assert.Throws<ProviderRefusedException>(() => RunSelection.Resolve(configuration, launcher.ExecutionCapabilities, [source], null, cap)).Message, StringComparison.Ordinal);
        }
        store.WriteAdapter(new() { Adapter = RepresentationContract.LocalLossless, Launchers = [name] });
        configuration = store.Resolve(launcher, null)!;
        Assert.Equal("disabled", RunSelection.Resolve(configuration, launcher.ExecutionCapabilities, [], null, cap)["requested"]!["adapter"]!.GetValue<string>());
        Assert.Equal(RepresentationContract.LocalLossless, RunSelection.Resolve(configuration, launcher.ExecutionCapabilities, [], choice, cap)["requested"]!["adapter"]!.GetValue<string>());
        Assert.Throws<ProviderRefusedException>(() => RunSelection.Resolve(configuration, launcher.ExecutionCapabilities with { Adapters = [] }, [], choice, cap));
        store.WriteAdapter(new() { Adapter = RepresentationContract.LocalLossless, Launchers = [name == "codex" ? "claude-code" : "codex"] });
        Assert.Throws<ProviderRefusedException>(() => RunSelection.Resolve(store.Resolve(launcher, null)!, launcher.ExecutionCapabilities, [], choice, cap));
        store.WriteAdapter(new());
        Assert.Throws<ProviderRefusedException>(() => RunSelection.Resolve(store.Resolve(launcher, null)!, launcher.ExecutionCapabilities, [], choice, cap));
        Assert.Equal("selected", store.Read(launcher)!.Model);
    }

    [Fact]
    public void MalformedOrUnknownConfigurationFailsClosedAndDoesNotReplacePriorAdmission()
    {
        var store = new WorkerConfigurationStore(home);
        store.WriteAdapter(new());
        Assert.Throws<ProviderRefusedException>(() => store.WriteAdapter(new() { Adapter = "other", Launchers = ["codex"] }));
        Assert.Throws<ProviderRefusedException>(() => store.WriteAdapter(new() { Adapter = RepresentationContract.LocalLossless }));
        Assert.Equal("disabled", store.ReadAdapter()!.Adapter);
        File.WriteAllText(Path.Combine(home, "worker", "efficiency-adapter.json"), "{\"adapter\":\"local-lossless\",\"launchers\":[\"codex\"],\"upstream\":\"https://elsewhere.invalid\"}");
        Assert.Throws<ProviderRefusedException>(() => store.ReadAdapter());
    }

    public void Dispose()
    {
        if (Directory.Exists(home)) WorkspaceCleanup.Delete(home);
    }
}
