// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using Vericairn.Runtime.Providers;
using Vericairn.Runtime.Workers;

namespace Vericairn.Runtime.Tests;

// A desktop app updates its bundled Codex in place, so each shipped baseline carries its own exact feature inventory.
public class CodexBaselineFeatureTests
{
    private const string Newer = "0.163.0-alpha.10.2";
    private const string NewerDefaults = "cli_daybreak stable false\napi_key_cyber_access_programs stable true\napi_key_model_discovery stable true\n"
        + "browser_annotation_api stable true\ncode_mode_interrupt stable true\nin_app_voice stable true\ninstant_interrupt under-development true\nultrafast_mode stable true";

    [Theory]
    [InlineData(Newer)]
    [InlineData("0.163.0")]
    public void TheNewerBaselinesRunWithTheirOwnInventoryAsShippedBaselines(string version)
    {
        using var fixture = new NativeProbe("codex");
        LaunchContext context = fixture.Prepare();
        fixture.Behavior("version", "codex-cli " + version);
        fixture.Behavior("featuresSuffix", NewerDefaults);
        RunPreflight inventory = fixture.Launcher.Preflight(context);
        Assert.True(inventory.Complete);
        Assert.Equal(version, inventory.HarnessVersion);
        Assert.Equal(version, inventory.Probes!.BaselineVersion);
        Assert.Contains("features.cli_daybreak=false", fixture.Launcher.Command(context));
        Assert.Contains("cli_daybreak = false", File.ReadAllText(Path.Combine(context.Environment["CODEX_HOME"], "config.toml")), StringComparison.Ordinal);
    }

    [Fact]
    public void TheEarlierBaselineStillRunsWithTheSwitchItDoesNotKnow()
    {
        using var fixture = new NativeProbe("codex");
        LaunchContext context = fixture.Prepare();
        RunPreflight inventory = fixture.Launcher.Preflight(context);
        Assert.Equal("0.159.0-alpha.12.1", inventory.HarnessVersion);
        Assert.Equal("0.159.0-alpha.12.1", inventory.Probes!.BaselineVersion);
        Assert.Contains("features.cli_daybreak=false", fixture.Launcher.Command(context));
    }

    [Fact]
    public void ARefusalNamesEachUnqualifiedFeatureAndTheListItWasCheckedAgainst()
    {
        using var fixture = new NativeProbe("codex");
        LaunchContext context = fixture.Prepare();
        fixture.Behavior("version", "codex-cli " + Newer);
        fixture.Behavior("featuresSuffix", NewerDefaults + "\nzeta_tools stable true\nalpha_discovery experimental true");
        string message = Assert.Throws<ProviderRefusedException>(() => fixture.Launcher.Preflight(context)).Message;
        Assert.Contains("Codex " + Newer + " enables features this launcher has not qualified: alpha_discovery, zeta_tools.", message, StringComparison.Ordinal);
        Assert.Contains("checked against " + Newer + "'s admitted enabled features:", message, StringComparison.Ordinal);
        Assert.Contains("ultrafast_mode", message, StringComparison.Ordinal);
        Assert.Contains("0.159.0-alpha.12.1 or " + Newer + " or 0.163.0", message, StringComparison.Ordinal);
        Assert.False(File.Exists(fixture.Report));
    }

    [Fact]
    public void TheEarlierBaselineDoesNotAdmitTheNewerBaselinesDefaults()
    {
        using var fixture = new NativeProbe("codex");
        LaunchContext context = fixture.Prepare();
        fixture.Behavior("featuresSuffix", "ultrafast_mode stable true");
        string message = Assert.Throws<ProviderRefusedException>(() => fixture.Launcher.Preflight(context)).Message;
        Assert.Contains("not qualified: ultrafast_mode.", message, StringComparison.Ordinal);
        Assert.Contains("checked against 0.159.0-alpha.12.1's", message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("enabled")]
    public void TheNewerBaselineRequiresItsSwitchedOffFeatureToBeOff(string state)
    {
        using var fixture = new NativeProbe("codex");
        LaunchContext context = fixture.Prepare();
        fixture.Behavior("version", "codex-cli " + Newer);
        fixture.Behavior("featuresSuffix", state == "missing" ? NewerDefaults.Replace("cli_daybreak stable false\n", "", StringComparison.Ordinal)
            : NewerDefaults.Replace("cli_daybreak stable false", "cli_daybreak stable true", StringComparison.Ordinal));
        Assert.Contains("cli_daybreak", Assert.Throws<ProviderRefusedException>(() => fixture.Launcher.Preflight(context)).Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AnUnshippedVersionNamesBothBaselines()
    {
        using var fixture = new NativeProbe("codex");
        LaunchContext context = fixture.Prepare();
        fixture.Behavior("version", "codex-cli 0.170.0");
        string message = Assert.Throws<ProviderRefusedException>(() => fixture.Launcher.Preflight(context)).Message;
        Assert.Contains("0.170.0 is not qualified", message, StringComparison.Ordinal);
        Assert.Contains("0.159.0-alpha.12.1 and " + Newer + " and 0.163.0", message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("arg0", false, true)]
    [InlineData("arg0-macos", false, true)]
    [InlineData("arg0-linux", false, true)]
    [InlineData("arg0-macos", true, false)]
    [InlineData("arg0-MacOS", false, false)]
    [InlineData("arg1-macos", false, false)]
    public void ProbeScratchAdmitsOnlyAnEmptyArgumentDirectory(string name, bool withFile, bool admitted)
    {
        using var fixture = new NativeProbe("codex");
        LaunchContext context = fixture.Prepare();
        fixture.Behavior("version", "codex-cli " + Newer);
        fixture.Behavior("featuresSuffix", NewerDefaults);
        string scratch = Directory.CreateDirectory(Path.Combine(context.Environment["CODEX_HOME"], "tmp", name)).FullName;
        if (withFile) File.WriteAllText(Path.Combine(scratch, "note"), "x");
        if (admitted) Assert.True(fixture.Launcher.Preflight(context).Complete);
        else Assert.Contains("scratch", Assert.Throws<ProviderRefusedException>(() => fixture.Launcher.Preflight(context)).Message, StringComparison.Ordinal);
    }
}
