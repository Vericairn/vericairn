// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using System.Text.Json.Nodes;
using Vericairn.Protocol;
using Vericairn.Runtime.Workers;

namespace Vericairn.Runtime.Tests;

[Collection("home")]
public class WorkerScopeTests
{
    [Theory]
    [InlineData("NOTES.md at the repository's root")]
    [InlineData("the widget")]
    [InlineData("path:../NOTES.md")]
    [InlineData("path:")]
    public void AdmissionRefusesBriefsWithoutAnInterpretablePatternBeforeRecordingOrLaunching(string entry)
    {
        using var run = new RunEstate(brief: Brief(entry));
        string report = run.Report("invalid-scope");
        RunResponse refused = run.Start("codex", "codex", report);
        if (refused.Ok) run.Settled(refused.Run);
        Assert.False(refused.Ok);
        Assert.Contains("permittedChanges", refused.Message, StringComparison.Ordinal);
        Assert.Contains("path:NOTES.md", refused.Message, StringComparison.Ordinal);
        Assert.Empty(run.Service.Records("operation"));
        Assert.False(File.Exists(report));
    }

    [Fact]
    public void AdmissionAcceptsProseBesideAnExplicitPattern()
    {
        using var run = new RunEstate(brief: Brief("NOTES.md at the repository's root", "path:NOTES.md"));
        RunResponse started = run.Start("codex", "codex", run.Report("mixed-scope"));
        Assert.True(started.Ok, started.Message);
        Assert.Equal("observed-success", run.Settled(started.Run).Status);
        Assert.True(PermittedPaths.Allows("NOTES.md", run.Clone, ["NOTES.md at the repository's root", "path:NOTES.md"]));
        Assert.False(PermittedPaths.Allows("NOTES.md at the repository's root", run.Clone, ["NOTES.md at the repository's root", "path:NOTES.md"]));
        Assert.Equal(0, run.Service.Check().Errors);
    }

    [Fact]
    public void AParentDirectoryReadSettlesWithoutAStopAndRetainsItsWarningAndAttention()
    {
        using var run = new RunEstate(brief: Brief("path:NOTES.md"));
        RunResponse started = run.Start("codex", "watch-codex-parent-search", run.Report("parent-search"));
        Assert.True(started.Ok, started.Message);
        RunRow settled = run.Settled(started.Run);
        Assert.Equal("observed-success", settled.Status);
        Assert.Null(settled.Stop);
        RunSignal signal = Assert.Single(settled.Signals!, s => s.Code == "scope-read");
        Assert.Equal("warning", signal.Severity);
        Assert.NotNull(signal.Attention);
        Assert.DoesNotContain(settled.Signals!, s => s.Code == "scope");
        Assert.Equal(0, run.Service.Check().Errors);
        run.Reopen();
        Assert.Contains(run.Row(started.Run).Signals!, s => s == signal);
    }

    private static string Brief(params string[] entries)
    {
        JsonObject brief = JsonNode.Parse(RunEstate.Brief)!.AsObject();
        brief["permittedChanges"] = JsonSerializer.SerializeToNode(entries);
        return brief.ToJsonString();
    }
}
