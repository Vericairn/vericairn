// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.IO.Compression;
using Vericairn.Packs.Software;
using Vericairn.Protocol;
using Vericairn.Records.Identity;
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
    public void ProvisionFragmentsStayOutOfBothStreamsAndReportedExecution()
    {
        string token = FakeHarness.NewClaudeSetupToken();
        using var fixture = new NativeRun("claude", qualify: true, recordFormat: 3, setupToken: token);
        fixture.Probe.Behavior("setupToken", "provision-fragments");
        RunResponse started = fixture.Start();
        Assert.True(started.Ok, started.Message);
        var settled = fixture.Run.Settled(started.Run);

        var directory = new RunDirectory(fixture.Run.Service.Session.Manifest.Estate, started.Run);
        string output = File.ReadAllText(directory.OutputLog);
        Assert.Contains("end of synthetic stdout", output, StringComparison.Ordinal);
        Assert.Contains("[stderr] end of synthetic stderr", output, StringComparison.Ordinal);
        string state = File.ReadAllText(directory.StateFile);
        for (int offset = 0; offset <= token.Length - 12; offset++)
        {
            Assert.False(output.AsSpan().Contains(token.AsSpan(offset, 12), StringComparison.Ordinal));
            Assert.False(state.AsSpan().Contains(token.AsSpan(offset, 12), StringComparison.Ordinal));
        }
        Assert.Contains("complete-report", state, StringComparison.Ordinal);
        Assert.Equal("observed-success", settled.Status);
    }

    [Fact]
    public void AnotherLauncherWithholdsFragmentsOfAProvisionHeldByTheWorker()
    {
        string token = FakeHarness.NewClaudeSetupToken();
        using var overlap = ClaudeOutputSecrets.Protect(new WorkerConfigurationStore.ClaudeSetupToken(token, "pro", long.MaxValue));
        using var fixture = new NativeRun("codex");
        fixture.Probe.Behavior("overlappingFragment", token[60..80]);
        RunResponse started = fixture.Start();
        Assert.True(started.Ok, started.Message);
        Assert.Equal("observed-success", fixture.Run.Settled(started.Run).Status);

        var directory = new RunDirectory(fixture.Run.Service.Session.Manifest.Estate, started.Run);
        string output = File.ReadAllText(directory.OutputLog);
        Assert.False(output.Contains(token[60..80], StringComparison.Ordinal));
        Assert.Contains("<secret>", output, StringComparison.Ordinal);
        Assert.Contains("[stderr] <secret>", output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheOutputPumpAndSupervisorProtectTheProvisionWhileTheyObserve()
    {
        string token = FakeHarness.NewClaudeSetupToken();
        string fragment = token[60..80];
        string run = Ulid.NewUlid().Value;
        var directory = new RunDirectory(Ulid.NewUlid().Value, run);
        directory.Create();
        var pumpRegistration = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        bool protectedBySupervisor = false;
        var options = new WorkerOptions
        {
            SetupToken = new WorkerConfigurationStore.ClaudeSetupToken(token, "pro", long.MaxValue),
            Heartbeat = TimeSpan.FromMilliseconds(20),
            AfterEvent = () =>
            {
                // Supervise has yet to start, so any registration here belongs to an output pump.
                pumpRegistration.TrySetResult(ClaudeOutputSecrets.ContainsActiveFragment(fragment));
            },
            StopAdapter = () =>
            {
                // Supervise calls this after the process exits and both pumps finish.
                protectedBySupervisor = ClaudeOutputSecrets.ContainsActiveFragment(fragment);
            },
        };

        string report = Path.Combine(directory.Path, "harness.jsonl");
        var supervisor = RunSupervisor.Start(run, FakeHarness.Command("generic", report), directory.Path,
            WorkerEnvironment.Build(WorkerEnvironment.Current()), new GenericLauncher(), directory, options, 60, null);
        bool protectedByPump;
        RunOutcome? outcome;
        try
        {
            protectedByPump = await pumpRegistration.Task.WaitAsync(TestTiming.HangTimeout, TestContext.Current.CancellationToken);
        }
        finally
        {
            outcome = supervisor.Supervise(TestContext.Current.CancellationToken);
        }

        Assert.Equal(0, outcome?.ExitCode);
        Assert.True(protectedByPump, "The output pump did not register the provision.");
        Assert.True(protectedBySupervisor, "The supervisor did not register the provision.");
        Assert.False(ClaudeOutputSecrets.ContainsActiveFragment(fragment));
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
