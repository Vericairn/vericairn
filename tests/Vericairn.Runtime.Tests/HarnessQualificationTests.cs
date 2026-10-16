// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using Vericairn.Protocol;
using Vericairn.Records.Hashing;
using Vericairn.Records.Types;
using Vericairn.Records.Model;

namespace Vericairn.Runtime.Tests;

[Collection("home")]
public class HarnessQualificationTests
{
    [Theory]
    [InlineData("codex", "human", "none", false)]
    [InlineData("claude", "human", "none", false)]
    [InlineData("codex", "human", "local-engineering", false)]
    [InlineData("claude", "human", "local-engineering", false)]
    [InlineData("codex", "agent", "qualify", false)]
    [InlineData("claude", "agent", "qualify", false)]
    [InlineData("codex", "service", "qualify", false)]
    [InlineData("claude", "service", "qualify", false)]
    [InlineData("codex", "human", "qualify", true)]
    [InlineData("claude", "human", "qualify", true)]
    public void OnlyAHumanOperatorOrExplicitHumanDelegateCanRunQualificationProbes(string harness, string kind, string scope, bool allowed)
    {
        using var probe = new NativeProbe(harness);
        using var run = new RunEstate(keepOperatorKey: true);
        run.Service.Providers.Register(probe.Launcher);
        run.Service.WorkerOptions = run.Service.WorkerOptions with { Environment = probe.Context.Environment, Vericairn = probe.Context.McpServer };
        probe.Behavior("version", harness == "codex" ? "codex-cli 0.160.0" : "2.1.297 (Claude Code)");
        string author = kind == "agent" ? run.Estate.BuilderId : RunEstate.Ok(run.Service.NewRecord(new("principal", run.Estate.OperatorId,
            RunEstate.Json($$"""{"kind":"{{kind}}","name":"Qualifier","roles":["contributor"]}""")))).Id;
        if (scope != "none") Grant(run, author, scope);
        if (allowed)
        {
            string device = RunEstate.Ok(run.Service.DeviceCreate(new(author, "Qualifier device", "file"))).Id;
            RunEstate.Ok(run.Service.DeviceAdmit(new(device, run.Estate.OperatorId, "Admit the human delegate.")));
        }
        WriteOutcome result = run.Service.QualifyHarness(new(probe.Launcher.Launcher, author, probe.Context.Settings));
        Assert.Equal(allowed, result.Ok);
        if (allowed)
        {
            RecordEnvelope record = run.Estate.Load().ById[result.Written!.Id];
            Assert.Equal(author, record.By);
            Assert.NotEmpty(record.Signatures);
            Assert.True(File.Exists(probe.Report + ".probed"));
        }
        else
        {
            Assert.Contains("human", result.Problem!.Message, StringComparison.OrdinalIgnoreCase);
            Assert.False(File.Exists(probe.Report + ".probed"));
            Assert.Empty(run.Service.Records("harness-qualification"));
        }
        Assert.False(File.Exists(probe.Report));
        Assert.Equal(0, run.Service.Validate().Errors);
    }

    [Theory]
    [InlineData("codex", "missingFlag", "--config")]
    [InlineData("claude", "missingFlag", "--tools")]
    [InlineData("codex", "probeFailure", "--version")]
    [InlineData("claude", "probeFailure", "--help")]
    [InlineData("codex", "mcp", "ambient")]
    [InlineData("codex", "featuresSuffix", "unqualified_extension stable true")]
    [InlineData("codex", "version", "codex-cli 0.160.0\nsecret-synthetic-token")]
    [InlineData("claude", "version", "2.1.297 /machine/path (Claude Code)")]
    public void IncompleteOrUnboundedEvidenceDoesNotWriteAQualification(string harness, string key, string value)
    {
        using var probe = new NativeProbe(harness);
        using var run = new RunEstate(keepOperatorKey: true);
        run.Service.Providers.Register(probe.Launcher);
        run.Service.WorkerOptions = run.Service.WorkerOptions with { Environment = probe.Context.Environment, Vericairn = probe.Context.McpServer };
        probe.Behavior("version", harness == "codex" ? "codex-cli 0.160.0" : "2.1.297 (Claude Code)");
        probe.Behavior(key, value);
        WriteOutcome result = run.Service.QualifyHarness(new(probe.Launcher.Launcher, run.Estate.OperatorId, probe.Context.Settings));
        Assert.False(result.Ok);
        Assert.Contains("probes failed", result.Problem!.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(value, result.Problem.Message, StringComparison.Ordinal);
        Assert.True(File.Exists(probe.Report + ".probed"));
        Assert.Empty(run.Service.Records("harness-qualification"));
        Assert.False(File.Exists(probe.Report));
    }

    private static void Grant(RunEstate run, string author, string scope)
    {
        string decision = RunEstate.Ok(run.Service.NewRecord(new("decision", run.Estate.OperatorId, RunEstate.Json($$"""{"title":"Grant qualification","stake":"low","decidedBy":"{{run.Estate.OperatorId}}","decidedAt":"2026-10-13T12:00:00Z","statement":"Grant the scope.","summary":"Grant the scope.","synthesis":"Grant the scope.","effects":["Scoped authority granted."],"affects":[],"notAuthorized":[]}""")))).Id;
        RunEstate.Ok(run.Service.NewRecord(new("authority", run.Estate.OperatorId, RunEstate.Json($$"""{"grantee":"{{author}}","scope":["{{scope}}"],"reserved":[],"conditions":[],"basis":"{{decision}}"}"""))));
    }

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
