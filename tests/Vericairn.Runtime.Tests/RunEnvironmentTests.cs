// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using Vericairn.Runtime.Providers;
using Vericairn.Runtime.Workers;
using Vericairn.Protocol;
using Vericairn.Packs.Software;

namespace Vericairn.Runtime.Tests;

[Collection("home")]
public class RunEnvironmentTests
{
    private static readonly IReadOnlyDictionary<string, string> EmptyEnvironment = new Dictionary<string, string>(StringComparer.Ordinal);

    [Fact]
    public void VariableNamesKeepTheAdmittedSpellingAcrossPlatformComparisonRules()
    {
        RunComponent harness = ReportedLauncher.Inventory.Single(c => c.Kind == "harness") with { Environment = ["path"] };
        var policy = new RunEnvironmentPolicy("test", [harness]);
        var environment = new Dictionary<string, string>(WorkerEnvironment.Names) { ["PATH"] = "synthetic-value" };
        if (!OperatingSystem.IsWindows())
        {
            Assert.Throws<ProviderRefusedException>(() => RunEnvironment.Admit(policy, new RunPreflight(policy.Components, true, true), environment));
            return;
        }
        var admitted = RunEnvironment.Admit(policy, new RunPreflight(policy.Components, true, true), environment);
        Assert.Equal("PATH", admitted["inventory"]![0]!["environment"]![0]!.GetValue<string>());
        Assert.Equal("PATH", admitted["variables"]![0]!.GetValue<string>());
    }

    [Theory]
    [InlineData("dependency")]
    [InlineData("destination")]
    [InlineData("environment")]
    [InlineData("identity")]
    [InlineData("digest")]
    [InlineData("kind")]
    public void MatchingDeclarationsStillRefuseUnclosedOrInvalidInventories(string defect)
    {
        RunComponent component = ReportedLauncher.Inventory[0];
        component = defect switch
        {
            "dependency" => component with { Dependencies = ["unlisted-helper"] },
            "destination" => component with { Destinations = ["unadmitted-cloud"] },
            "environment" => component with { Environment = ["UNDECLARED_HELPER_SETTING"] },
            "identity" => component with { Id = "../local-binding" },
            "digest" => component with { Hash = "unknown" },
            _ => component with { Kind = "unrecognized" },
        };
        var policy = new RunEnvironmentPolicy("test", [component]);
        Assert.Throws<ProviderRefusedException>(() => RunEnvironment.Admit(policy, new RunPreflight([component], true, true), EmptyEnvironment));
    }

    [Theory]
    [InlineData("API_KEY")]
    [InlineData("BASH_ENV")]
    [InlineData("NODE_OPTIONS")]
    [InlineData("DYLD_INSERT_LIBRARIES")]
    [InlineData("PYTHONPATH")]
    [InlineData("DOTNET_STARTUP_HOOKS")]
    public void AToolCannotReceiveAmbientCredentialsOrUndeclaredLoaderConfiguration(string variable)
    {
        var environment = new Dictionary<string, string>(StringComparer.Ordinal) { [variable] = "synthetic-value" };
        var policy = new RunEnvironmentPolicy("test", ReportedLauncher.Inventory);
        Assert.Throws<ProviderRefusedException>(() => RunEnvironment.Admit(policy, new RunPreflight(policy.Components, true, true), environment));
        Assert.Empty(WorkerEnvironment.Build(environment, [variable]));
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public void IncompleteObservationOrWiderChildEnvironmentsAreUnsupported(bool complete, bool inherited)
    {
        var policy = new RunEnvironmentPolicy("test", ReportedLauncher.Inventory);
        Assert.Throws<ProviderRefusedException>(() => RunEnvironment.Admit(policy, new RunPreflight(policy.Components, complete, inherited), EmptyEnvironment));
    }

    [Fact]
    public void AnInstalledToolIsNotAdmittedByItsDiscovery()
    {
        var policy = new RunEnvironmentPolicy("test", ReportedLauncher.Inventory);
        RunComponent extra = policy.Components[0] with { Id = "installed-hook", Kind = "hook" };
        Assert.Throws<ProviderRefusedException>(() => RunEnvironment.Admit(policy, new RunPreflight([.. policy.Components, extra], true, true), EmptyEnvironment));
    }

    [Theory]
    [InlineData("inventory-unexpected")]
    [InlineData("inventory-route")]
    [InlineData("inventory-incomplete")]
    [InlineData("inventory-wider-environment")]
    public void StandInReportsItsEffectiveInventoryAndRefusalLeavesNoIntent(string scenario)
    {
        using var run = new RunEstate();
        string report = run.Report(scenario);
        RunResponse refused = run.Start("claude-code", scenario, report);
        Assert.False(refused.Ok, refused.Message);
        Assert.Equal("not-started", refused.Status);
        Assert.Empty(run.Service.Runs().Runs);
        Assert.False(File.Exists(report));
    }

    [Fact]
    public void AnInventoryResolutionThatChangesAfterIntentCannotDispatch()
    {
        using var run = new RunEstate();
        string report = run.Report("changed-inventory");
        RunResponse refused = run.Start("claude-code", "inventory-changes", report);
        Assert.False(refused.Ok);
        Assert.Equal("known-failure", refused.Status);
        Assert.False(File.Exists(report));
        Assert.Empty(run.Row(refused.Run).Charged.EnumerateObject());
        Assert.Equal(0, run.Service.Check().Errors);
    }

    [Fact]
    public void InventoryOrderDoesNotChangeItsIdentityAndNoEnvironmentValueEntersIt()
    {
        var environment = new Dictionary<string, string>(StringComparer.Ordinal) { ["HOME"] = "synthetic-private-value" };
        var policy = new RunEnvironmentPolicy("test", ReportedLauncher.Inventory);
        var first = RunEnvironment.Admit(policy, new RunPreflight(policy.Components, true, true), environment);
        var second = RunEnvironment.Admit(policy, new RunPreflight(policy.Components.Reverse().ToArray(), true, true), environment);
        Assert.Equal(first.ToJsonString(), second.ToJsonString());
        Assert.DoesNotContain("synthetic-private-value", first.ToJsonString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("codex")]
    [InlineData("claude-code")]
    public void UnqualifiedNativeCommandPrefixesRefuseBeforeIntent(string launcher)
    {
        using var run = new RunEstate();
        IAgentLauncher native = launcher == "codex" ? new CodexLauncher() : new ClaudeCodeLauncher();
        RunConfiguration configuration = new ReportedLauncher(native).Configuration;
        run.Service.Providers.Register(native);
        run.Service.WorkerOptions = run.Service.WorkerOptions with
        {
            Configurations = new Dictionary<string, RunConfiguration>(StringComparer.Ordinal) { [launcher] = configuration },
            Vericairn = FakeHarness.Command("native-server", run.Report("server")),
            Environment = new Dictionary<string, string>(run.Service.WorkerOptions.Environment!, WorkerEnvironment.Names) { ["HOME"] = run.Scratch, ["USERPROFILE"] = run.Scratch },
        };
        string report = run.Report("unqualified");
        RunResponse refused = run.Start(launcher, "codex", report);
        Assert.False(refused.Ok);
        Assert.Contains("leading harness options", refused.Message, StringComparison.Ordinal);
        Assert.False(File.Exists(report));
        Assert.Empty(run.Service.Runs().Runs);
    }
}
