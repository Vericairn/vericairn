// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using Vericairn.Protocol;
using Vericairn.Records.Model;
using Vericairn.Records.Tests;
using Vericairn.Remote;
using Vericairn.Runtime.Workers;
using Vericairn.Sdk;

namespace Vericairn.Remote.Tests;

public sealed class RemoteSupervisionTests(HeadlessFixture fixture)
{
    [Fact]
    public async Task CancelShowsEachObservedFactAndReconcilesTheRestoredOriginal()
    {
        using TestEstate estate = RemoteFixtures.Seeded(3);
        using var connections = new FakeConnections(estate.Root) { EnableSignedWrites = true };
        RecordEnvelope run = RemoteInspectionSeed.Run(estate, RemoteInspectionSeed.Assign(estate));
        await fixture.UiAsync(async () =>
        {
            var clock = new InspectionClock();
            RemoteViewModel model = await OpenRun(connections, clock);
            int writes = 0;
            connections.BeforeApiCall = name => { if (name == nameof(IEstateApi.NewSignedRecordAsync)) writes++; };
            await model.ReviewCancelCommand.ExecuteAsync();
            Assert.Contains("configured grace period", model.Intervention!.Review, StringComparison.Ordinal);
            Assert.Equal(0, writes);
            await model.SubmitInterventionCommand.ExecuteAsync();
            RemoteInterventionReceipt original = Assert.Single(model.Interventions);
            Assert.True(original.State == "requested", original.Observation);
            Assert.Contains("Containment not observed", original.Observation, StringComparison.Ordinal);
            Assert.Equal(1, writes);
            Assert.False(model.SubmitInterventionCommand.CanExecute(null));

            var directory = new RunDirectory(connections.Service.Session.Manifest.Estate, run.Id);
            StopRequest request = Assert.IsType<StopRequest>(directory.ReadStop());
            Assert.Null(request.GraceSeconds);
            directory.Write(new RunObservedState
            {
                Run = run.Id, Heartbeat = request.RequestedAt,
                Stop = new(request.RequestedAt, request.RequestedBy, request.Reason, request.RequestedAt, "worker sent stop", request.RequestedAt, "harness reported stop", ContainedAt: request.RequestedAt),
            });
            var restored = new RemoteViewModel(connections, clock);
            restored.SelectedIntervention = Assert.Single(restored.Interventions);
            Assert.Equal(original.Id, restored.SelectedIntervention.Id);
            Assert.False(restored.ReconcileInterventionCommand.CanExecute(null));
            await restored.RefreshCommand.ExecuteAsync();
            await restored.ReconcileInterventionCommand.ExecuteAsync();
            RemoteInterventionReceipt observed = Assert.Single(restored.Interventions);
            Assert.Equal("contained", observed.State);
            Assert.Contains("Transmitted", observed.Observation, StringComparison.Ordinal);
            Assert.Contains("Harness acknowledgment", observed.Observation, StringComparison.Ordinal);
            Assert.Contains("Containment observed", observed.Observation, StringComparison.Ordinal);
            Assert.Equal(1, writes);
            return true;
        });
    }

    [Fact]
    public async Task LostContainmentResponseStaysUncertainUntilTheOriginalIsObserved()
    {
        using TestEstate estate = RemoteFixtures.Seeded(3);
        using var connections = new FakeConnections(estate.Root) { EnableSignedWrites = true, LoseRunStopResponse = true };
        RecordEnvelope run = RemoteInspectionSeed.Run(estate, RemoteInspectionSeed.Assign(estate));
        await fixture.UiAsync(async () =>
        {
            RemoteViewModel model = await OpenRun(connections, new InspectionClock());
            await model.ReviewContainCommand.ExecuteAsync();
            Assert.Contains("without an additional grace period", model.Intervention!.Effect, StringComparison.Ordinal);
            await model.SubmitInterventionCommand.ExecuteAsync();
            RemoteInterventionReceipt original = Assert.Single(model.Interventions);
            Assert.Equal("uncertain", original.State);
            var directory = new RunDirectory(connections.Service.Session.Manifest.Estate, run.Id);
            Assert.Equal(0, directory.ReadStop()!.GraceSeconds);
            Assert.False(model.SubmitInterventionCommand.CanExecute(null));
            await model.ReviewCancelCommand.ExecuteAsync();
            Assert.Null(model.Intervention);

            await model.ReconcileInterventionCommand.ExecuteAsync();
            Assert.Equal(original.Id, model.SelectedIntervention!.Id);
            Assert.Equal("requested", model.SelectedIntervention.State);
            Assert.Contains("Containment not observed", model.SelectedIntervention.Observation, StringComparison.Ordinal);
            return true;
        });
    }

    [Fact]
    public async Task PausePinsTheActiveStatusEvenIfTheGoalChangesAtTheWriteBoundary()
    {
        using TestEstate estate = RemoteFixtures.Seeded(3);
        using var connections = new FakeConnections(estate.Root) { EnableSignedWrites = true };
        RecordEnvelope goal = RemoteInspectionSeed.Goal(estate);
        RecordEnvelope active = estate.Add("goal-status", estate.OperatorId, """{"status":"active","note":"Started for the phone check."}""", goal.Reference);
        _ = RemoteInspectionSeed.Run(estate, RemoteInspectionSeed.Assign(estate), goal);
        await fixture.UiAsync(async () =>
        {
            RemoteViewModel model = await OpenRun(connections, new InspectionClock());
            await model.ReviewPauseCommand.ExecuteAsync();
            Assert.Contains(active.ComputedHash, model.Intervention!.Review, StringComparison.Ordinal);
            Assert.Contains("Runs already in flight continue", model.Intervention.Review, StringComparison.Ordinal);

            connections.BeforeApiCall = name =>
            {
                if (name != nameof(IEstateApi.NewSignedRecordAsync)) return;
                _ = estate.Add("goal-status", estate.OperatorId, """{"status":"paused","note":"Changed during the pause request."}""", goal.Reference);
                _ = estate.Add("goal-status", estate.OperatorId, """{"status":"active","note":"Resumed during the pause request."}""", goal.Reference);
            };
            await model.SubmitInterventionCommand.ExecuteAsync();
            Assert.Equal("rejected", Assert.Single(model.Interventions).State);
            Assert.Contains("status changed", model.InterventionMessage, StringComparison.Ordinal);
            Assert.Equal(3, estate.Load().About(goal.Id, "goal-status").Count());

            connections.BeforeApiCall = null;
            await model.ReviewPauseCommand.ExecuteAsync();
            await model.SubmitInterventionCommand.ExecuteAsync();
            Assert.True(model.SelectedIntervention!.State == "completed", model.SelectedIntervention.Observation);
            Assert.Contains("recorded a paused goal", model.SelectedIntervention.Observation, StringComparison.Ordinal);
            Assert.Contains("any run stopped", model.SelectedIntervention.Observation, StringComparison.Ordinal);
            Assert.Equal(4, estate.Load().About(goal.Id, "goal-status").Count());
            return true;
        });
    }

    [Fact]
    public async Task NodesKeepPhoneAccessAndWorkerObservationIndependent()
    {
        using TestEstate estate = RemoteFixtures.Seeded(3);
        using var connections = new FakeConnections(estate.Root);
        RecordEnvelope run = RemoteInspectionSeed.Run(estate, RemoteInspectionSeed.Assign(estate));
        await fixture.UiAsync(async () =>
        {
            var clock = new InspectionClock();
            RemoteViewModel model = await RemoteInspectionSeed.Pair(connections, clock);
            Assert.Equal("Direct local-network connection", RemoteViewModel.NodePath);
            Assert.Equal("current", model.Node!.State);
            Assert.Contains("unavailable", model.WorkersMessage, StringComparison.Ordinal);
            Assert.Empty(model.Workers);

            await model.CheckWorkersCommand.ExecuteAsync();
            RemoteWorkerNode worker = Assert.Single(model.Workers);
            Assert.Equal(run.Id, worker.Run);
            Assert.Contains("No current worker was observed", worker.Observation, StringComparison.Ordinal);
            Assert.StartsWith("Observed ", model.WorkersMessage, StringComparison.Ordinal);

            connections.Checks.Enqueue((ConnectionStates.Cached, "Offline"));
            await model.RefreshCommand.ExecuteAsync();
            Assert.StartsWith("Cached ", model.WorkersMessage, StringComparison.Ordinal);
            Assert.Equal(worker, Assert.Single(model.Workers));
            Assert.False(model.CheckWorkersCommand.CanExecute(null));
            return true;
        });
    }

    [Fact]
    public async Task DesktopHandoffKeepsExactTaskAndDraftAndSeparatelyObservesOpening()
    {
        using TestEstate estate = RemoteFixtures.Seeded(3);
        using var connections = new FakeConnections(estate.Root) { LoseDesktopHandoffResponse = true };
        RecordEnvelope assignment = RemoteInspectionSeed.Assign(estate);
        await fixture.UiAsync(async () =>
        {
            RemoteViewModel model = await OpenAssignment(connections, new InspectionClock());
            await model.ComposeWorkHandoffCommand.ExecuteAsync();
            Assert.Contains(assignment.ComputedHash, model.HandoffSubject, StringComparison.Ordinal);
            model.HandoffTask = "Inspect the exact assignment";
            model.HandoffDraft = "Prepared words for review.";
            await model.PrepareHandoffCommand.ExecuteAsync();
            Assert.Contains("uncertain", model.HandoffMessage, StringComparison.Ordinal);
            Assert.False(model.PrepareHandoffCommand.CanExecute(null));

            await model.RefreshHandoffsCommand.ExecuteAsync();
            Assert.Equal(4, model.SelectedArea);
            RemoteHandoffRow displayed = Assert.Single(model.Handoffs);
            Assert.Equal("Prepared words for review.", displayed.Draft);
            Assert.Contains("No Workbench opening observed", displayed.State, StringComparison.Ordinal);
            DesktopHandoff kept = Assert.Single(connections.Service.DesktopHandoffs(estate.OperatorId).Handoffs);
            Assert.Equal(assignment.ComputedHash, kept.Subject.Hash);
            Assert.Equal("work-order", Assert.Single(kept.Evidence).Type);

            Assert.NotNull(connections.OpenHandoff(new(estate.OperatorId, kept.Id)).Handoff);
            await model.RefreshHandoffsCommand.ExecuteAsync();
            displayed = Assert.Single(model.Handoffs);
            Assert.Contains("Opened in Workbench", displayed.State, StringComparison.Ordinal);
            Assert.Contains("Downstream state has not been observed.", displayed.State, StringComparison.Ordinal);
            Assert.DoesNotContain("completed", displayed.State, StringComparison.Ordinal);
            return true;
        });
    }

    private static async Task<RemoteViewModel> OpenRun(FakeConnections connections, TimeProvider clock)
    {
        RemoteViewModel model = await OpenAssignment(connections, clock);
        model.InterventionTarget = Assert.Single(model.InterventionTargets);
        return model;
    }

    private static async Task<RemoteViewModel> OpenAssignment(FakeConnections connections, TimeProvider clock)
    {
        RemoteViewModel model = await RemoteInspectionSeed.Pair(connections, clock);
        model.SelectedAssignment = Assert.Single(model.Assignments);
        await model.OpenAssignmentCommand.ExecuteAsync();
        Assert.NotNull(model.WorkDetail);
        return model;
    }
}
