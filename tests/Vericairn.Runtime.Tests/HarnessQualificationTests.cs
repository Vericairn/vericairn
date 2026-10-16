// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using Vericairn.Protocol;
using Vericairn.Records.Hashing;
using Vericairn.Records.Types;

namespace Vericairn.Runtime.Tests;

[Collection("home")]
public class HarnessQualificationTests
{
    [Theory]
    [InlineData("codex")]
    [InlineData("claude")]
    public void QualifiesANewerHarnessThroughItsOwnProbesWithoutDispatch(string harness)
    {
        using var probe = new NativeProbe(harness);
        using var run = new RunEstate(keepOperatorKey: true);
        run.Service.Providers.Register(probe.Launcher);
        run.Service.WorkerOptions = run.Service.WorkerOptions with { Environment = probe.Context.Environment, Vericairn = probe.Context.McpServer };
        probe.Behavior("version", harness == "codex" ? "codex-cli 0.160.0" : "2.1.297 (Claude Code)");
        RecordWritten written = RunEstate.Ok(run.Service.QualifyHarness(new(probe.Launcher.Launcher, run.Estate.OperatorId, probe.Context.Settings)));
        var record = run.Estate.Load().ById[written.Id];
        Assert.Equal("harness-qualification", record.Type);
        Assert.Equal(2, record.Format);
        Assert.NotEmpty(record.Signatures);
        Assert.Equal(harness == "codex" ? "0.160.0" : "2.1.297", record.Body.GetProperty("version").GetString());
        Assert.Equal("qualified", record.Body.GetProperty("status").GetString());
        Assert.Equal(HarnessQualificationType.ProbeNames.Order(StringComparer.Ordinal), record.Body.GetProperty("probes").EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal));
        Assert.All(record.Body.GetProperty("probes").EnumerateObject(), p => Assert.True(ContentHash.IsValid(p.Value.GetString())));
        Assert.DoesNotContain(probe.Root, record.Body.GetRawText(), StringComparison.Ordinal);
        Assert.DoesNotContain("synthetic-login", record.Body.GetRawText(), StringComparison.Ordinal);
        Assert.False(File.Exists(probe.Report));
    }

    [Theory]
    [InlineData("codex")]
    [InlineData("claude")]
    public void QualificationNeverRecordsRawHelpEvenWhenItContainsPathsOrSecrets(string harness)
    {
        using var probe = new NativeProbe(harness);
        using var run = new RunEstate(keepOperatorKey: true);
        run.Service.Providers.Register(probe.Launcher);
        run.Service.WorkerOptions = run.Service.WorkerOptions with { Environment = probe.Context.Environment, Vericairn = probe.Context.McpServer };
        probe.Behavior("helpSuffix", probe.Root + " secret-synthetic-qualification-token");
        RecordWritten written = RunEstate.Ok(run.Service.QualifyHarness(new(probe.Launcher.Launcher, run.Estate.OperatorId, probe.Context.Settings)));
        string body = run.Estate.Load().ById[written.Id].Body.GetRawText();
        Assert.DoesNotContain(probe.Root, body, StringComparison.Ordinal);
        Assert.DoesNotContain("secret-synthetic-qualification-token", body, StringComparison.Ordinal);
        Assert.False(File.Exists(probe.Report));
    }
}
