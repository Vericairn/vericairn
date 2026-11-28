// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using Vericairn.Packs.Software;
using Vericairn.Protocol;
using Vericairn.Records.Estate;
using Vericairn.Records.Model;
using Vericairn.Runtime.Providers;
using Vericairn.Runtime.Service;
using Vericairn.Runtime.Workers;

namespace Vericairn.Runtime.Tests;

public sealed class RunAdmissionServiceTests
{
    [Fact]
    public async Task ConcurrentRunStartsPrepareTheirWorkspacesWithoutReloadingTheEstate()
    {
        using var verifying = new Barrier(2);
        using var preparingWorkspace = new ThreadLocal<bool>();
        using var run = new RunEstate(budget: """{"wallMinutes":60,"attemptsPerStep":3,"concurrency":2}""");
        CancellationToken cancellation = TestContext.Current.CancellationToken;
        run.Service.Providers.Register(new HookedLauncher(new ReportedLauncher(new GenericLauncher()),
            verify: () =>
            {
                // Both starts have judged the estate and are about to prepare their own workspace.
                Assert.True(verifying.SignalAndWait(TestTiming.HangTimeout, cancellation));
                preparingWorkspace.Value = true;
                return null;
            },
            prepared: _ => preparingWorkspace.Value = false));
        run.Service.Session.Loaded = _ => Assert.False(preparingWorkspace.Value, "Workspace preparation reloaded the estate.");

        RunResponse[] starts = await Task.WhenAll(Enumerable.Range(0, 2).Select(index => Task.Factory.StartNew(
            () => run.Start("generic", "claude", run.Report($"concurrent-{index}"), wallMinutes: 5, branch: $"work/concurrent-{index}"),
            cancellation, TaskCreationOptions.LongRunning, TaskScheduler.Default)));

        Assert.All(starts, started => Assert.True(started.Ok, started.Message));
        Assert.NotEqual(starts[0].Run, starts[1].Run);
        Assert.All(starts, started => Assert.Equal("observed-success", run.Settled(started.Run).Status));
        Assert.Equal(0, run.Service.Check().Errors);
    }

    // FIX-184, after the Reviewer's review: the workspace is judged under the reviews read with the plan, and a review superseded
    // while it is prepared refuses the run at intent, under the gate.
    [Fact]
    public void AConfigurationReviewSupersededWhileTheWorkspaceIsPreparedRefusesTheRunAtIntent()
    {
        using var run = new RunEstate(recordFormat: 3, keepOperatorKey: true);
        File.AppendAllText(Path.Combine(run.Clone, ".git", "config"), "[future]\n\tsetting = inert-test-data\n");
        string reviewed = new Vericairn.Adapters.Git.GitCommandAdapter().Inspect(run.Clone).ConfigurationHash!;
        string subject = $"resource:{run.Resource}:{run.Service.Record(run.Resource)!.Hash}";
        System.Text.Json.JsonElement Review(string config) => RunEstate.Json($$"""
            {"independence":"self","outcome":"pass","summary":"Read the exact configuration.","findings":[],
             "repositoryConfiguration":{"config":"{{config}}"} }
            """);
        RecordWritten passed = RunEstate.Ok(run.Service.NewRecord(new NewRecordRequest("review", run.Estate.OperatorId, Review(reviewed), subject)));

        // The operator's key leaves the machine before the run, as the custody guard requires, and returns only to sign the change.
        var keys = new Vericairn.Records.Signing.FileKeyStore(Vericairn.Records.Signing.VericairnHome.Path());
        string keyFile = Directory.GetFiles(Path.Combine(Vericairn.Records.Signing.VericairnHome.Path(), "keys"), run.OperatorDevice + "*").Single();
        byte[] key = File.ReadAllBytes(keyFile);
        keys.Remove(run.OperatorDevice);
        int superseded = 0;
        run.Service.Providers.Register(new HookedLauncher(new ReportedLauncher(new GenericLauncher()), verify: () =>
        {
            // The plan was judged under the passing review; the workspace has not yet been acquired.
            if (Interlocked.Increment(ref superseded) != 1) return null;
            File.WriteAllBytes(keyFile, key);
            if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(keyFile, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            RunEstate.Ok(run.Service.NewRecord(new NewRecordRequest("review", run.Estate.OperatorId,
                Review("sha256:" + new string('0', 64)), subject, Supersedes: passed.Id)));
            keys.Remove(run.OperatorDevice);
            return null;
        }));

        RunResponse refused = run.Start("generic", "claude", run.Report("superseded"), wallMinutes: 5, branch: "work/superseded");

        Assert.Equal(1, superseded);
        Assert.False(refused.Ok, refused.Message);
        Assert.Contains("configuration review changed while the workspace was prepared", refused.Message, StringComparison.Ordinal);
        Assert.Empty(run.Service.Runs().Runs);
    }

    [Fact]
    public void APreparedIntentDoesNotAuthorizeDispatchAfterConfigurationChanges()
    {
        using var run = new RunEstate();
        var gate = new object();
        RunAdmissionService admission = Admission(run, gate);
        RunAdmissionService.AdmittedRun admitted = Prepare(run, admission);
        run.Service.WorkerOptions = run.Service.WorkerOptions with
        {
            Configurations = new Dictionary<string, RunConfiguration>(StringComparer.Ordinal)
            {
                [admitted.Launcher.Launcher] = admitted.Configuration with { Name = "changed-configuration" },
            },
        };

        bool launched = false;
        ProviderRefusedException refusal = Assert.Throws<ProviderRefusedException>(() => admission.Launch(admitted, _ =>
        {
            launched = true;
            throw new InvalidOperationException("The launch boundary must not be reached.");
        }, out _, out _));

        Assert.Contains("changed after the intent", refusal.Message, StringComparison.Ordinal);
        Assert.False(launched);
        Assert.False(Monitor.IsEntered(gate));
        Assert.Equal("not-started", run.Row(admitted.Run).Status);
        Assert.False(File.Exists(run.Report("admission")));
    }

    [Fact]
    public void TheLaunchCallbackRunsUnderTheSameGateThatAdmittedTheIntent()
    {
        using var run = new RunEstate();
        var gate = new object();
        RunAdmissionService admission = Admission(run, gate);
        RunAdmissionService.AdmittedRun admitted = Prepare(run, admission);

        InvalidOperationException inspected = Assert.Throws<InvalidOperationException>(() => admission.Launch(admitted, command =>
        {
            Assert.True(Monitor.IsEntered(gate));
            Assert.NotEmpty(command);
            Assert.Equal(admitted.Intent.Id, run.Row(admitted.Run).Run);
            throw new InvalidOperationException("Launch boundary inspected.");
        }, out _, out _));

        Assert.Equal("Launch boundary inspected.", inspected.Message);
        Assert.False(Monitor.IsEntered(gate));
        Assert.Equal("not-started", run.Row(admitted.Run).Status);
        Assert.False(File.Exists(run.Report("admission")));
    }

    internal static RunAdmissionService.AdmittedRun Prepare(RunEstate run, RunAdmissionService admission)
    {
        var request = new RunStartRequest(run.Assignment, "generic", run.Estate.BuilderId, run.Estate.OperatorId, run.Resource,
            Settings: new Dictionary<string, string>(StringComparer.Ordinal) { ["command"] = FakeHarness.Setting("generic", run.Report("admission")) });
        RunResponse? refusal = admission.Prepare(request, null, out RunAdmissionService.AdmittedRun? admitted, out RecordWritten? assigned);
        Assert.Null(refusal);
        Assert.Null(assigned);
        Assert.NotNull(admitted);
        Assert.Equal("not-started", run.Row(admitted.Run).Status);
        Assert.False(File.Exists(run.Report("admission")));
        return admitted;
    }

    // Compose admission on its own. Only generic records and context assembly use the fixture's facade; no run API is used
    // to prepare or launch. The supplied record boundary checks that intent publication retains the admission gate.
    internal static RunAdmissionService Admission(RunEstate run, object gate)
    {
        var records = new EstateRecordAccess(gate, run.Service.Session,
            (draft, _) =>
            {
                Assert.True(Monitor.IsEntered(gate));
                string? subject = draft.Subject is null ? null : $"{draft.Subject.Type}:{draft.Subject.Id}:{draft.Subject.Hash}";
                return run.Service.NewRecord(new NewRecordRequest(draft.Type, draft.By, draft.Body, subject, draft.Supersedes,
                    draft.At is DateTimeOffset at ? RecordParser.FormatTimestamp(at) : null, draft.Id));
            },
            (_, _) => throw new InvalidOperationException("No pending assignment is carried by this test."),
            (_, _, _) => throw new InvalidOperationException("No batch is carried by this test."),
            carried =>
            {
                Assert.True(Monitor.IsEntered(gate));
                Assert.Empty(carried);
                return run.Service.Session.Load();
            },
            _ => null, (_, _) => throw new InvalidOperationException("Release admission is outside this fixture."),
            (_, _) => throw new InvalidOperationException("Release evidence is outside this fixture."),
            (_, _) => throw new InvalidOperationException("No history checkpoint is written by this fixture."),
            _ => throw new InvalidOperationException("Release admission is outside this fixture."),
            new PresenceCounter(), (_, _) => false);
        return new RunAdmissionService(records, () => run.Service.Providers, () => run.Service.WorkerOptions,
            (_, _, order, goal, maxBytes, recipient, assignment, _, _, _) =>
            {
                Assert.Equal(run.Goal, goal.Id);
                return run.Service.BriefContext(order.Id, maxBytes, recipient, assignment)!;
            },
            (_, _, _, _) => throw new InvalidOperationException("This fixture does not select a prompt."));
    }
}
