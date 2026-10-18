// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using System.Text.RegularExpressions;
using Vericairn.Protocol;
using Vericairn.Runtime.Workers;

namespace Vericairn.Runtime.Tests;

[Collection("home")]
public class WorkerFix26Tests
{
    [Theory]
    [InlineData("printenv VERICAIRN_HOME", false)]
    [InlineData("echo $VERICAIRN_HOME", false)]
    [InlineData("cat \"$VERICAIRN_HOME/keys/device\"", true)]
    [InlineData("cat \"$VERICAIRN_HOME/connections/pairing.json\"", true)]
    [InlineData("cat \"$VERICAIRN_HOME/estates/estate/local/runtime-identity.json\"", true)]
    [InlineData("cat \"$VERICAIRN_HOME/estates/estate/local/runtime-certificate.p12\"", true)]
    [InlineData("cp -r \"$VERICAIRN_HOME\" copy", true)]
    [InlineData("tar -cf copy.tar \"$VERICAIRN_HOME\"", true)]
    public void ReportedLocationReadsProceedAndCredentialReadsOrCopiesStop(string command, bool stop)
    {
        using var run = new RunEstate();
        string report = run.Report("location");
        // The harness only reports this input. No reported command runs, and the estate owns a test home.
        File.WriteAllText(report + ".command", command);
        RunResponse started = run.Start("codex", "watch-codex-command-input", report);
        Assert.True(started.Ok, started.Message);
        RunRow settled = run.Settled(started.Run);
        Assert.Equal(stop ? "known-failure" : "observed-success", settled.Status);
        Assert.Equal(stop, settled.Signals!.Any(signal => signal.Code == "credentials" && signal.Severity == "critical"));
        if (stop) Assert.NotNull(settled.Stop!.ContainedAt);
        else Assert.Null(settled.Stop);
        Assert.Equal(0, run.Service.Check().Errors);
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(false, true, false)]
    [InlineData(true, false, false)]
    [InlineData(true, true, false)]
    [InlineData(false, false, true)]
    [InlineData(false, true, true)]
    [InlineData(true, false, true)]
    [InlineData(true, true, true)]
    public void CredentialTimeoutsRetryAndAnExhaustedCheckStopsTheRun(bool exhaust, bool credential, bool pattern)
    {
        using var run = new RunEstate();
        var limits = new List<TimeSpan>();
        run.Service.WorkerOptions = run.Service.WorkerOptions with
        {
            BeforeCredentialCheck = pattern ? _ => CommandAudit.MatchWithRetry(
                new Regex("x", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100)), regex => { Attempt(regex.MatchTimeout); return true; }) : Attempt,
        };

        void Attempt(TimeSpan limit)
        {
            limits.Add(limit);
            if (exhaust || limits.Count == 1)
            {
                if (pattern) throw new RegexMatchTimeoutException();
                throw new TimeoutException("Synthetic check deadline.");
            }
        }
        string report = run.Report("timeout");
        File.WriteAllText(report + ".command", credential ? "cat \"$VERICAIRN_HOME/keys/device\"" : "cat README.md");
        RunResponse started = run.Start("codex", "watch-codex-command-input", report);
        Assert.True(started.Ok, started.Message);
        RunRow settled = run.Settled(started.Run);
        Assert.Equal([TimeSpan.FromMilliseconds(100), TimeSpan.FromSeconds(5)], limits);
        Assert.Equal(exhaust || credential ? "known-failure" : "observed-success", settled.Status);
        if (exhaust || credential)
        {
            RunSignal signal = Assert.Single(settled.Signals!, signal => signal.Code == "credentials");
            Assert.Equal("critical", signal.Severity);
            Assert.NotNull(settled.Stop!.ContainedAt);
            if (exhaust) Assert.Contains("after retrying", signal.Summary, StringComparison.Ordinal);
        }
        else Assert.Null(settled.Stop);
        Assert.Equal(exhaust, settled.Signals!.Any(signal => signal.Code == "audit-unverified"));
        Assert.Equal(0, run.Service.Check().Errors);
    }

    [Theory]
    [InlineData("list_mcp_resources", true)]
    [InlineData("list_mcp_resource_templates", true)]
    [InlineData("read_mcp_resource", true)]
    [InlineData("create_goal", true)]
    [InlineData("get_goal", true)]
    [InlineData("update_goal", true)]
    [InlineData("unadmitted_tool", false)]
    public void CodexInternalServerCallsMatchOnlyItsAdmittedNativeTools(string tool, bool admitted)
    {
        using var fixture = new NativeRun("codex");
        fixture.Probe.Behavior("nativeTool", tool);
        RunResponse started = fixture.Start();
        Assert.True(started.Ok, started.Message);
        RunRow settled = fixture.Run.Settled(started.Run);
        Assert.Equal(admitted ? "observed-success" : "known-failure", settled.Status);
        Assert.Equal(!admitted, settled.Signals!.Any(signal => signal.Code == "tool-inventory"));
        if (admitted) Assert.Null(settled.Stop);
        else Assert.NotNull(settled.Stop!.ContainedAt);
        Assert.Equal(0, fixture.Run.Service.Check().Errors);
    }

    [Theory]
    [InlineData("codex", "codex", true)]
    [InlineData("other", "other", false)]
    [InlineData("codex", "extension", false)]
    public void AServerNameCannotSupplyMissingHarnessProvenance(string harness, string provider, bool admitted)
    {
        var inventory = new RunToolInventory(JsonSerializer.SerializeToElement(new
        {
            environment = new
            {
                inventory = new object[]
                {
                    new { id = harness, kind = "harness" },
                    new { id = "list_mcp_resources", kind = "tool", dependencies = new[] { provider } },
                },
            },
        }));
        Assert.Equal(admitted, inventory.Contains("mcp__codex__list_mcp_resources"));
        Assert.False(inventory.Contains("mcp__other__list_mcp_resources"));
        Assert.False(inventory.Contains("mcp__codex__unadmitted_tool"));
    }

    [Fact]
    public void AConfiguredMcpServerNamedCodexIsRefusedBeforeDispatch()
    {
        using var fixture = new NativeRun("codex");
        fixture.Probe.Behavior("mcp", "codex-server");
        RunResponse refused = fixture.Start();
        Assert.False(refused.Ok);
        Assert.False(File.Exists(fixture.Probe.Report));
    }
}
