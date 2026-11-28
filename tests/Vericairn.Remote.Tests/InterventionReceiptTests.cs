// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using Vericairn.Protocol;
using Vericairn.Records.Model;
using Vericairn.Records.Tests;
using Vericairn.Runtime.Workers;
using Vericairn.Sdk;

namespace Vericairn.Remote.Tests;

public sealed class InterventionReceiptTests(HeadlessFixture fixture)
{
    [Fact]
    public async Task RefusedCancelCanBeReviewedAgainAgainstTheCurrentOperation()
    {
        using TestEstate estate = RemoteFixtures.Seeded(3);
        using var connections = new FakeConnections(estate.Root) { EnableSignedWrites = true };
        RecordEnvelope run = RemoteInspectionSeed.Run(estate, RemoteInspectionSeed.Assign(estate));
        await fixture.UiAsync(async () =>
        {
            RemoteViewModel model = await OpenRun(connections);
            await model.ReviewCancelCommand.ExecuteAsync();
            Assert.NotNull(model.Intervention);
            connections.BeforeApiCall = name =>
            {
                if (name != nameof(IEstateApi.NewSignedRecordAsync)) return;
                RecordEnvelope latest = estate.Load().Latest(run);
                JsonObject body = JsonNode.Parse(latest.Body.GetRawText())!.AsObject();
                body["status"] = "uncertain";
                estate.Add("operation", estate.OperatorId, body.ToJsonString(), supersedes: latest.Id);
            };

            await model.SubmitInterventionCommand.ExecuteAsync();
            RemoteInterventionReceipt refused = Assert.Single(model.Interventions);
            Assert.Equal("rejected", refused.State);
            Assert.Contains("The reviewed run operation changed", refused.Observation, StringComparison.Ordinal);

            connections.BeforeApiCall = null;
            await model.ReviewCancelCommand.ExecuteAsync();
            Assert.Equal(estate.Load().Latest(run).ComputedHash, model.Intervention?.Revision);
            await model.SubmitInterventionCommand.ExecuteAsync();
            RemoteInterventionReceipt next = Assert.IsType<RemoteInterventionReceipt>(model.SelectedIntervention);
            Assert.True(next.State == "requested", next.Observation);
            Assert.NotEqual(refused.Id, next.Id);
            Assert.Contains(refused, model.Interventions);
            Assert.Equal(next.Marker, new RunDirectory(connections.Service.Session.Manifest.Estate, run.Id).ReadStop()!.Reason);
            return true;
        });
    }

    [Fact]
    public async Task RefusedPauseCanBeReviewedAgainWithTheSameActiveStatus()
    {
        using TestEstate estate = RemoteFixtures.Seeded(3);
        using var connections = new FakeConnections(estate.Root) { EnableSignedWrites = true };
        RecordEnvelope goal = RemoteInspectionSeed.Goal(estate);
        RecordEnvelope active = estate.Add("goal-status", estate.OperatorId, """{"status":"active","note":"Started for the phone check."}""", goal.Reference);
        _ = RemoteInspectionSeed.Run(estate, RemoteInspectionSeed.Assign(estate), goal);
        await fixture.UiAsync(async () =>
        {
            RemoteViewModel model = await OpenRun(connections);
            await model.ReviewPauseCommand.ExecuteAsync();
            Assert.Equal(active.ComputedHash, model.Intervention?.Revision);
            connections.BeforeApiCall = name =>
            {
                if (name == nameof(IEstateApi.NewSignedRecordAsync))
                    throw new HttpRequestException("The runtime refused this pause.", null, HttpStatusCode.Unauthorized);
            };
            await model.SubmitInterventionCommand.ExecuteAsync();
            RemoteInterventionReceipt refused = Assert.Single(model.Interventions);
            Assert.Equal("rejected", refused.State);
            Assert.Single(estate.Load().About(goal.Id, "goal-status"));

            connections.BeforeApiCall = null;
            await model.ReviewPauseCommand.ExecuteAsync();
            Assert.Equal(active.ComputedHash, model.Intervention?.Revision);
            await model.SubmitInterventionCommand.ExecuteAsync();
            RemoteInterventionReceipt next = Assert.IsType<RemoteInterventionReceipt>(model.SelectedIntervention);
            Assert.Equal("completed", next.State);
            Assert.NotEqual(refused.Id, next.Id);
            Assert.Contains(refused, model.Interventions);
            Assert.Equal(2, estate.Load().About(goal.Id, "goal-status").Count());
            return true;
        });
    }

    [Fact]
    public async Task AnUndeliveredPauseCanBeRetriedAfterReconciliation()
    {
        using TestEstate estate = RemoteFixtures.Seeded(3);
        using var connections = new FakeConnections(estate.Root) { EnableSignedWrites = true };
        RecordEnvelope goal = RemoteInspectionSeed.Goal(estate);
        RecordEnvelope active = estate.Add("goal-status", estate.OperatorId, """{"status":"active","note":"Started for the phone check."}""", goal.Reference);
        _ = RemoteInspectionSeed.Run(estate, RemoteInspectionSeed.Assign(estate), goal);
        await fixture.UiAsync(async () =>
        {
            RemoteViewModel model = await OpenRun(connections);
            await model.ReviewPauseCommand.ExecuteAsync();
            connections.BeforeApiCall = name =>
            {
                if (name == nameof(IEstateApi.NewSignedRecordAsync)) throw new HttpRequestException("Never delivered.");
            };
            await model.SubmitInterventionCommand.ExecuteAsync();
            RemoteInterventionReceipt original = Assert.Single(model.Interventions);
            Assert.Equal("uncertain", original.State);
            Assert.NotNull(original.Record);
            Assert.Single(estate.Load().About(goal.Id, "goal-status"));

            connections.BeforeApiCall = null;
            connections.FailRecordRead = true;
            await model.ReconcileInterventionCommand.ExecuteAsync();
            Assert.Equal(original, model.SelectedIntervention);
            connections.FailRecordRead = false;
            await model.ReconcileInterventionCommand.ExecuteAsync();
            Assert.Equal("not-delivered", model.SelectedIntervention?.State);
            await model.ReviewPauseCommand.ExecuteAsync();
            Assert.Equal(active.ComputedHash, model.Intervention?.Revision);
            await model.SubmitInterventionCommand.ExecuteAsync();

            RemoteInterventionReceipt retried = Assert.Single(model.Interventions);
            Assert.Equal(original.Id, retried.Id);
            Assert.Equal("completed", retried.State);
            Assert.Equal(2, estate.Load().About(goal.Id, "goal-status").Count());
            return true;
        });
    }

    [Fact]
    public async Task ALatePauseAfterNotDeliveredIsObservedWhenTheTargetIsReviewedAgain()
    {
        using TestEstate estate = RemoteFixtures.Seeded(3);
        using var connections = new FakeConnections(estate.Root) { EnableSignedWrites = true };
        RecordEnvelope goal = RemoteInspectionSeed.Goal(estate);
        estate.Add("goal-status", estate.OperatorId, """{"status":"active","note":"Started for the phone check."}""", goal.Reference);
        _ = RemoteInspectionSeed.Run(estate, RemoteInspectionSeed.Assign(estate), goal);
        await fixture.UiAsync(async () =>
        {
            RemoteViewModel model = await OpenRun(connections);
            Func<WriteOutcome>? delayed = null;
            connections.DispatchSignedIntervention = send => { delayed = send; throw new HttpRequestException("Not arrived yet."); };
            await model.ReviewPauseCommand.ExecuteAsync();
            await model.SubmitInterventionCommand.ExecuteAsync();
            connections.DispatchSignedIntervention = null;
            await model.ReconcileInterventionCommand.ExecuteAsync();
            Assert.Equal("not-delivered", Assert.Single(model.Interventions).State);

            // The held request arrives within its plan's lifetime; the next review reads the receipt again before anything else.
            Assert.True(delayed!().Ok);
            await model.ReviewPauseCommand.ExecuteAsync();
            Assert.Equal("completed", Assert.Single(model.Interventions).State);
            Assert.Null(model.Intervention);
            return true;
        });
    }

    [Fact]
    public async Task ALateStopAfterNotDeliveredIsObservedWhenTheTargetIsReviewedAgain()
    {
        using TestEstate estate = RemoteFixtures.Seeded(3);
        using var connections = new FakeConnections(estate.Root) { EnableSignedWrites = true };
        _ = RemoteInspectionSeed.Run(estate, RemoteInspectionSeed.Assign(estate));
        await fixture.UiAsync(async () =>
        {
            RemoteViewModel model = await OpenRun(connections);
            Func<WriteOutcome>? delayed = null;
            connections.DispatchSignedIntervention = send => { delayed = send; throw new HttpRequestException("Not arrived yet."); };
            await model.ReviewCancelCommand.ExecuteAsync();
            await model.SubmitInterventionCommand.ExecuteAsync();
            connections.DispatchSignedIntervention = null;
            await model.ReconcileInterventionCommand.ExecuteAsync();
            Assert.Equal("not-delivered", Assert.Single(model.Interventions).State);

            Assert.True(delayed!().Ok);
            await model.ReviewCancelCommand.ExecuteAsync();
            Assert.NotEqual("not-delivered", Assert.Single(model.Interventions).State);
            Assert.Single(estate.Load().OfType("run-stop-request"));
            return true;
        });
    }

    [Fact]
    public async Task RestartAtThePresencePromptFindsNoBlockingReceipt()
    {
        using TestEstate estate = RemoteFixtures.Seeded(3);
        using var connections = new FakeConnections(estate.Root) { EnableSignedWrites = true };
        _ = RemoteInspectionSeed.Run(estate, RemoteInspectionSeed.Assign(estate));
        await fixture.UiAsync(async () =>
        {
            RemoteViewModel model = await OpenRun(connections);
            await model.ReviewCancelCommand.ExecuteAsync();
            IReadOnlyList<RemoteInterventionReceipt>? savedAtPrompt = null;
            connections.BeforeInterventionSignature = () =>
            {
                savedAtPrompt = connections.LoadInterventions(connections.Kept.Single());
                throw new IOException("The application closed at the presence prompt.");
            };
            await model.SubmitInterventionCommand.ExecuteAsync();
            Assert.Empty(Assert.IsAssignableFrom<IReadOnlyList<RemoteInterventionReceipt>>(savedAtPrompt));
            Assert.Empty(estate.Load().OfType("run-stop-request"));

            connections.BeforeInterventionSignature = null;
            var restarted = new RemoteViewModel(connections, new InspectionClock());
            await restarted.RefreshCommand.ExecuteAsync();
            await OpenAssignmentRun(restarted);
            await restarted.ReviewCancelCommand.ExecuteAsync();
            Assert.NotNull(restarted.Intervention);
            await restarted.SubmitInterventionCommand.ExecuteAsync();
            Assert.Equal("requested", restarted.SelectedIntervention?.State);
            Assert.Single(estate.Load().OfType("run-stop-request"));
            return true;
        });
    }

    [Fact]
    public async Task ALostStopResponseIsReconciledFromTheSignedRecordBeforeObservingTheWorker()
    {
        using TestEstate estate = RemoteFixtures.Seeded(3);
        using var connections = new FakeConnections(estate.Root) { EnableSignedWrites = true, LoseRunStopResponse = true };
        _ = RemoteInspectionSeed.Run(estate, RemoteInspectionSeed.Assign(estate));
        await fixture.UiAsync(async () =>
        {
            RemoteViewModel model = await OpenRun(connections);
            await model.ReviewCancelCommand.ExecuteAsync();
            await model.SubmitInterventionCommand.ExecuteAsync();
            RemoteInterventionReceipt original = Assert.Single(model.Interventions);
            RecordEnvelope signed = Assert.Single(estate.Load().OfType("run-stop-request"));
            Assert.Equal("uncertain", original.State);
            Assert.Equal(signed.Id, original.Record);
            Assert.Equal(signed.ComputedHash, original.RecordHash);

            var restarted = new RemoteViewModel(connections, new InspectionClock());
            await restarted.RefreshCommand.ExecuteAsync();
            await OpenAssignmentRun(restarted);
            restarted.SelectedIntervention = Assert.Single(restarted.Interventions);
            connections.FailRecordRead = true;
            await restarted.ReconcileInterventionCommand.ExecuteAsync();
            Assert.Equal(original, restarted.SelectedIntervention);

            connections.FailRecordRead = false;
            await restarted.ReconcileInterventionCommand.ExecuteAsync();
            Assert.Equal(original.Id, restarted.SelectedIntervention?.Id);
            Assert.Equal(signed.Id, restarted.SelectedIntervention?.Record);
            Assert.Equal("requested", restarted.SelectedIntervention?.State);
            await restarted.ReviewCancelCommand.ExecuteAsync();
            Assert.Null(restarted.Intervention);
            Assert.False(restarted.SubmitInterventionCommand.CanExecute(null));
            Assert.Equal(signed.Id, Assert.Single(estate.Load().OfType("run-stop-request")).Id);
            return true;
        });
    }

    [Fact]
    public async Task ADelayedStopAndItsRetryCannotBothWriteARecord()
    {
        using TestEstate estate = RemoteFixtures.Seeded(3);
        using var connections = new FakeConnections(estate.Root) { EnableSignedWrites = true };
        _ = RemoteInspectionSeed.Run(estate, RemoteInspectionSeed.Assign(estate));
        await fixture.UiAsync(async () =>
        {
            RemoteViewModel model = await OpenRun(connections);
            Func<WriteOutcome>? delayed = null;
            connections.DispatchSignedIntervention = send =>
            {
                delayed = send;
                throw new HttpRequestException("The old request has not arrived yet.");
            };
            await model.ReviewCancelCommand.ExecuteAsync();
            await model.SubmitInterventionCommand.ExecuteAsync();
            RemoteInterventionReceipt original = Assert.Single(model.Interventions);
            Assert.Empty(estate.Load().OfType("run-stop-request"));
            await model.ReconcileInterventionCommand.ExecuteAsync();
            Assert.Equal("not-delivered", model.SelectedIntervention?.State);

            await model.ReviewCancelCommand.ExecuteAsync();
            connections.DispatchSignedIntervention = send =>
            {
                Assert.True(Assert.IsAssignableFrom<Func<WriteOutcome>>(delayed)().Ok);
                return send();
            };
            await model.SubmitInterventionCommand.ExecuteAsync();

            RemoteInterventionReceipt observed = Assert.Single(model.Interventions);
            RecordEnvelope signed = Assert.Single(estate.Load().OfType("run-stop-request"));
            Assert.Equal(original.Id, observed.Id);
            Assert.Equal(original.Record, signed.Id);
            Assert.Equal(signed.Id, observed.Record);
            Assert.Equal("requested", observed.State);
            return true;
        });
    }

    [Fact]
    public async Task AnUncertainCancelBlocksOnlyAnotherCancelAcrossRestart()
    {
        using TestEstate estate = RemoteFixtures.Seeded(3);
        using var connections = new FakeConnections(estate.Root) { EnableSignedWrites = true };
        RecordEnvelope run = RemoteInspectionSeed.Run(estate, RemoteInspectionSeed.Assign(estate));
        await fixture.UiAsync(async () =>
        {
            RemoteViewModel model = await OpenRun(connections);
            int writes = 0;
            connections.BeforeApiCall = name =>
            {
                if (name != nameof(IEstateApi.NewSignedRecordAsync)) return;
                writes++;
                throw new HttpRequestException("The stop response was not received.");
            };
            await model.ReviewCancelCommand.ExecuteAsync();
            await model.SubmitInterventionCommand.ExecuteAsync();

            RemoteInterventionReceipt original = Assert.Single(model.Interventions);
            Assert.Equal("uncertain", original.State);
            Assert.Null(new RunDirectory(connections.Service.Session.Manifest.Estate, run.Id).ReadStop());
            var restarted = new RemoteViewModel(connections, new InspectionClock());
            await restarted.RefreshCommand.ExecuteAsync();
            await OpenAssignmentRun(restarted);
            await restarted.ReviewCancelCommand.ExecuteAsync();
            Assert.Null(restarted.Intervention);
            Assert.Equal(original, restarted.SelectedIntervention);
            Assert.False(restarted.SubmitInterventionCommand.CanExecute(null));
            Assert.Equal(1, writes);

            connections.BeforeApiCall = null;
            await restarted.ReviewContainCommand.ExecuteAsync();
            Assert.NotNull(restarted.Intervention);
            await restarted.SubmitInterventionCommand.ExecuteAsync();
            RemoteInterventionReceipt containment = Assert.IsType<RemoteInterventionReceipt>(restarted.SelectedIntervention);
            Assert.True(containment.State == "requested", containment.Observation);
            Assert.NotEqual(original.Id, containment.Id);
            Assert.Equal(0, new RunDirectory(connections.Service.Session.Manifest.Estate, run.Id).ReadStop()!.GraceSeconds);
            return true;
        });
    }

    [Fact]
    public async Task ContainmentAfterAPendingCancelKeepsBothRequests()
    {
        using TestEstate estate = RemoteFixtures.Seeded(3);
        using var connections = new FakeConnections(estate.Root) { EnableSignedWrites = true };
        RecordEnvelope run = RemoteInspectionSeed.Run(estate, RemoteInspectionSeed.Assign(estate));
        await fixture.UiAsync(async () =>
        {
            RemoteViewModel model = await OpenRun(connections);
            await model.ReviewCancelCommand.ExecuteAsync();
            await model.SubmitInterventionCommand.ExecuteAsync();
            RemoteInterventionReceipt cancel = Assert.Single(model.Interventions);
            var directory = new RunDirectory(connections.Service.Session.Manifest.Estate, run.Id);
            Assert.Equal(cancel.Marker, directory.ReadStop()?.Reason);

            await model.ReviewCancelCommand.ExecuteAsync();
            Assert.Null(model.Intervention);
            await model.ReviewContainCommand.ExecuteAsync();
            Assert.Contains("earlier cancel", model.Intervention?.Review, StringComparison.Ordinal);
            await model.SubmitInterventionCommand.ExecuteAsync();

            RemoteInterventionReceipt containment = Assert.IsType<RemoteInterventionReceipt>(model.SelectedIntervention);
            Assert.True(containment.State == "requested", containment.Observation);
            Assert.NotEqual(cancel.Id, containment.Id);
            StopRequest effective = Assert.IsType<StopRequest>(directory.ReadStop());
            Assert.Equal(0, effective.GraceSeconds);
            Assert.Equal(containment.Marker, effective.Reason);
            Assert.True(File.Exists(directory.StopRequestFile));

            model.SelectedIntervention = cancel;
            await model.ReconcileInterventionCommand.ExecuteAsync();
            Assert.Equal("superseded", model.SelectedIntervention?.State);
            Assert.Equal(2, model.Interventions.Count);
            return true;
        });
    }

    [Theory]
    [InlineData("failed")]
    [InlineData("superseded")]
    public async Task AResolvedReceiptNeverBlocksAFreshReview(string state)
    {
        using TestEstate estate = RemoteFixtures.Seeded(3);
        using var connections = new FakeConnections(estate.Root) { EnableSignedWrites = true };
        _ = RemoteInspectionSeed.Run(estate, RemoteInspectionSeed.Assign(estate));
        await fixture.UiAsync(async () =>
        {
            RemoteViewModel model = await OpenRun(connections);
            await model.ReviewCancelCommand.ExecuteAsync();
            var previous = new RemoteInterventionReceipt(model.Intervention!, Guid.NewGuid().ToString("N"), state, "Earlier outcome.");
            connections.SaveIntervention(connections.Kept.Single(), previous);
            var restarted = new RemoteViewModel(connections, new InspectionClock());
            await restarted.RefreshCommand.ExecuteAsync();
            await OpenAssignmentRun(restarted);

            await restarted.ReviewCancelCommand.ExecuteAsync();
            Assert.NotNull(restarted.Intervention);
            await restarted.SubmitInterventionCommand.ExecuteAsync();
            Assert.Equal("requested", restarted.SelectedIntervention?.State);
            Assert.Contains(previous, restarted.Interventions);
            Assert.Equal(2, restarted.Interventions.Count);
            return true;
        });
    }

    [Fact]
    public async Task ARefusedStopOfAnEndedRunReportsTheRefusalEvenWithEarlierStopFacts()
    {
        using TestEstate estate = RemoteFixtures.Seeded(3);
        using var connections = new FakeConnections(estate.Root) { EnableSignedWrites = true };
        RecordEnvelope run = RemoteInspectionSeed.Run(estate, RemoteInspectionSeed.Assign(estate));
        await fixture.UiAsync(async () =>
        {
            RemoteViewModel model = await OpenRun(connections);
            await model.ReviewCancelCommand.ExecuteAsync();
            connections.BeforeApiCall = name =>
            {
                if (name != nameof(IEstateApi.NewSignedRecordAsync)) return;
                RecordEnvelope latest = estate.Load().Latest(run);
                JsonObject body = JsonNode.Parse(latest.Body.GetRawText())!.AsObject();
                string time = RecordParser.FormatTimestamp(estate.Clock.AddSeconds(2));
                body["status"] = "known-failure";
                body["observed"] = JsonSerializer.SerializeToNode(new { summary = "The worker exited after the desktop stop.", at = time });
                body["usage"] = JsonSerializer.SerializeToNode(new { wallMinutes = 1, unreported = Array.Empty<string>() });
                body["stop"] = JsonSerializer.SerializeToNode(new { requestedAt = time, requestedBy = estate.OperatorId, reason = "Earlier desktop stop" });
                estate.Add("operation", estate.OperatorId, body.ToJsonString(), supersedes: latest.Id);
            };

            await model.SubmitInterventionCommand.ExecuteAsync();
            RemoteInterventionReceipt refused = Assert.Single(model.Interventions);
            Assert.Equal("rejected", refused.State);
            Assert.Contains("there is nothing to stop", refused.Observation, StringComparison.Ordinal);
            await model.ReconcileInterventionCommand.ExecuteAsync();
            Assert.Equal("rejected", model.SelectedIntervention?.State);
            return true;
        });
    }

    private static async Task<RemoteViewModel> OpenRun(FakeConnections connections)
    {
        RemoteViewModel model = await RemoteInspectionSeed.Pair(connections, new InspectionClock());
        await OpenAssignmentRun(model);
        return model;
    }

    private static async Task OpenAssignmentRun(RemoteViewModel model)
    {
        model.SelectedAssignment = Assert.Single(model.Assignments);
        await model.OpenAssignmentCommand.ExecuteAsync();
        model.InterventionTarget = Assert.Single(model.InterventionTargets);
    }
}
