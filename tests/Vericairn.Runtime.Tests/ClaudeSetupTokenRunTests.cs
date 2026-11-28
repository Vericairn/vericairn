// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.IO.Compression;
using Vericairn.Protocol;
using Vericairn.Runtime.Workers;

namespace Vericairn.Runtime.Tests;

public class ClaudeSetupTokenRunTests
{
    [Theory]
    [InlineData("rewrite", "observed-success")]
    [InlineData("failure", "known-failure")]
    public void RunRemovesItsSignInAndAnyHarnessCopyOnSettlement(string scenario, string outcome)
    {
        string token = FakeHarness.NewClaudeSetupToken();
        using var fixture = new NativeRun("claude", qualify: true, recordFormat: 3, setupToken: token);
        fixture.Probe.Behavior("setupToken", scenario);
        RunResponse started = fixture.Start();
        Assert.True(started.Ok, started.Message);
        Assert.Equal(outcome, fixture.Run.Settled(started.Run).Status);

        var directory = new RunDirectory(fixture.Run.Service.Session.Manifest.Estate, started.Run);
        string staged = File.ReadAllText(fixture.Probe.Report + ".signed-in");
        Assert.Equal(RunDirectory.ClaudeConfigurationDirectory(directory.McpConfiguration), staged);
        Assert.NotEqual(fixture.Probe.Login, staged);
        Assert.False(File.Exists(Path.Combine(fixture.Probe.Login, ".credentials.json")));
        Assert.False(Directory.Exists(staged));
        Assert.False(Directory.Exists(directory.Temporary));
        var provision = new WorkerConfigurationStore(fixture.Probe.Context.Environment["VERICAIRN_HOME"]);
        Assert.True(provision.ReadClaudeSetupToken(fixture.Probe.Context.Environment)?.AccessToken == token);
    }

    [Fact]
    public void TokenEchoNeverEntersTheLogBriefRecordsOrEstateExport()
    {
        string token = FakeHarness.NewClaudeSetupToken();
        using var fixture = new NativeRun("claude", qualify: true, recordFormat: 3, setupToken: token);
        fixture.Probe.Behavior("setupToken", "echo");
        RunResponse started = fixture.Start();
        Assert.True(started.Ok, started.Message);
        Assert.Equal("observed-success", fixture.Run.Settled(started.Run).Status);

        var directory = new RunDirectory(fixture.Run.Service.Session.Manifest.Estate, started.Run);
        string output = File.ReadAllText(directory.OutputLog);
        Assert.True(output.Contains("stand-in output:", StringComparison.Ordinal));
        Assert.True(output.Contains("[stderr] stand-in error:", StringComparison.Ordinal));
        Assert.False(output.Contains(token, StringComparison.Ordinal));
        Assert.False(output.Contains("\\u0073" + token[1..], StringComparison.Ordinal));
        Assert.False(File.ReadAllText(directory.BriefFile).Contains(token, StringComparison.Ordinal));
        Assert.False(File.ReadAllText(directory.ContextFile).Contains(token, StringComparison.Ordinal));
        Assert.False(File.ReadAllText(directory.StateFile).Contains(token, StringComparison.Ordinal));
        Assert.All(Directory.EnumerateFiles(Path.Combine(fixture.Run.Estate.Root, "records"), "*.json", SearchOption.AllDirectories),
            record => Assert.False(File.ReadAllText(record).Contains(token, StringComparison.Ordinal)));

        string destination = Path.Combine(fixture.Run.Scratch, "estate.zip");
        (ExportResponse? exported, Problem? problem) = fixture.Run.Service.Export(new ExportRequest(destination, fixture.Run.Estate.OperatorId));
        Assert.True(exported is not null, problem?.Message);
        using ZipArchive zip = ZipFile.OpenRead(destination);
        Assert.All(zip.Entries, entry =>
        {
            Assert.False(entry.FullName.EndsWith(".credentials.json", StringComparison.Ordinal));
            using var text = new StreamReader(entry.Open());
            Assert.False(text.ReadToEnd().Contains(token, StringComparison.Ordinal));
        });
    }

    [Fact]
    public void RefusedRunPreflightRemovesItsCopyBeforeAnyIntentIsRecorded()
    {
        using var fixture = new NativeRun("claude", setupToken: FakeHarness.NewClaudeSetupToken());
        fixture.Probe.Behavior("missingFlag", "--strict-mcp-config");
        RunResponse refused = fixture.Start();
        Assert.False(refused.Ok);
        Assert.Contains("--strict-mcp-config", refused.Message, StringComparison.Ordinal);
        Assert.Empty(fixture.Run.Service.Runs().Runs);
        string home = Vericairn.Records.Signing.VericairnHome.Path();
        string runs = Path.Combine(home, "estates", fixture.Run.Service.Session.Manifest.Estate, "runs");
        Assert.DoesNotContain(Directory.EnumerateDirectories(runs), run =>
            Directory.Exists(RunDirectory.ClaudeConfigurationDirectory(Path.Combine(run, "mcp.json"))));
    }
}
