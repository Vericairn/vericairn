// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using Vericairn.Protocol;
using Vericairn.Records.Estate;
using Vericairn.Records.Identity;
using Vericairn.Runtime.Workers;

namespace Vericairn.Runtime.Tests;

public class ClaudeSetupTokenRecoveryTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void StartAndRecoveryRemoveOnlyAbandonedPreparations(bool reopening)
    {
        using var run = new RunEstate();
        string estate = run.Service.Session.Manifest.Estate;
        var legacy = new RunDirectory(estate, Ulid.NewUlid().Value);
        var interrupted = new RunDirectory(estate, Ulid.NewUlid().Value);
        var active = new RunDirectory(estate, Ulid.NewUlid().Value);
        var dispatched = new RunDirectory(estate, Ulid.NewUlid().Value);
        foreach (RunDirectory directory in new[] { legacy, interrupted, active, dispatched })
        {
            directory.Create();
        }

        StageCopy(legacy);
        using (interrupted.AcquireClaudeConfigurationLease()) StageCopy(interrupted);
        using var activeLease = active.AcquireClaudeConfigurationLease();
        StageCopy(active);
        using (FileStream lease = dispatched.AcquireClaudeConfigurationLease())
        {
            StageCopy(dispatched);
            RunDirectory.MarkClaudeConfigurationDispatched(lease);
        }

        if (reopening)
        {
            run.Reopen();
        }
        else
        {
            RunResponse started = run.Start("generic", "generic", run.Report("after-orphan-cleanup"));
            Assert.True(started.Ok, started.Message);
            Assert.Equal("observed-success", run.Settled(started.Run).Status);
        }

        foreach (RunDirectory directory in new[] { legacy, interrupted })
        {
            Assert.False(Directory.Exists(ClaudeConfiguration(directory)));
            Assert.False(Directory.Exists(directory.Temporary));
        }
        Assert.True(Directory.Exists(ClaudeConfiguration(active)));
        Assert.True(Directory.Exists(ClaudeConfiguration(dispatched)));
    }

    [Fact]
    public void RecoveryRemovesTheSignInOfARunWithARetainedExitBeforeSettling()
    {
        using var fixture = new NativeRun("claude", qualify: true, recordFormat: 3, setupToken: FakeHarness.NewClaudeSetupToken());
        RunEstate run = fixture.Run;
        run.Service.BeforeValidatingRecords = records =>
        {
            if (records.Select(r => r.Envelope).Any(r => r.Type == "operation"
                && EstateModel.BodyString(r, "status") == "observed-success"))
                throw new IOException("Synthetic interruption of settlement.");
        };
        RunResponse started = fixture.Start();
        Assert.True(started.Ok, started.Message);
        var directory = new RunDirectory(run.Service.Session.Manifest.Estate, started.Run);
        run.WaitUntil(() => directory.Read() is { ExitedAt: not null, Problem: not null, Settled: null });
        // Recreate the copy to exercise a retained exit from a worker that stopped before disposing its sign-in.
        StageCopy(directory);
        Assert.True(File.Exists(Path.Combine(ClaudeConfiguration(directory), ".credentials.json")));
        Assert.Equal("dispatched", run.Row(started.Run).Status);

        run.Reopen();

        Assert.Equal("observed-success", run.Row(started.Run).Status);
        Assert.False(Directory.Exists(ClaudeConfiguration(directory)));
    }

    private static string ClaudeConfiguration(RunDirectory directory) => RunDirectory.ClaudeConfigurationDirectory(directory.McpConfiguration);

    private static void StageCopy(RunDirectory directory)
    {
        string configuration = ClaudeConfiguration(directory);
        WorkerConfigurationStore.CreateOwnerDirectory(configuration);
        using FileStream copy = WorkerConfigurationStore.CreateOwnerFile(Path.Combine(configuration, ".credentials.json"));
        JsonSerializer.Serialize(copy, new { claudeAiOauth = new { accessToken = FakeHarness.NewClaudeSetupToken() } });
    }
}
