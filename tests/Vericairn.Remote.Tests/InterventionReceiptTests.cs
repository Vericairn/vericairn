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
        using var connections = new FakeConnections(estate.Root);
        RecordEnvelope run = RemoteInspectionSeed.Run(estate, RemoteInspectionSeed.Assign(estate));
        await fixture.UiAsync(async () =>
        {
            RemoteViewModel model = await OpenRun(connections);
            await model.ReviewCancelCommand.ExecuteAsync();
            Assert.NotNull(model.Intervention);
            connections.BeforeApiCall = name =>
            {
                if (name != nameof(IEstateApi.RunStopAsync)) return;
                RecordEnvelope latest = estate.Load().Latest(run);
                JsonObject body = JsonNode.Parse(latest.Body.GetRawText())!.AsObject();
                body["dispatchedAt"] = RecordParser.FormatTimestamp(estate.Clock.AddSeconds(2));
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
            Assert.Equal("requested", next.State);
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
        using var connections = new FakeConnections(estate.Root);
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
                if (name == nameof(IEstateApi.GoalPauseAsync))
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
    public async Task AnUncertainCancelBlocksOnlyAnotherCancelAcrossRestart()
    {
        using TestEstate estate = RemoteFixtures.Seeded(3);
        using var connections = new FakeConnections(estate.Root);
        RecordEnvelope run = RemoteInspectionSeed.Run(estate, RemoteInspectionSeed.Assign(estate));
        await fixture.UiAsync(async () =>
        {
            RemoteViewModel model = await OpenRun(connections);
            int writes = 0;
            connections.BeforeApiCall = name =>
            {
                if (name != nameof(IEstateApi.RunStopAsync)) return;
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
            Assert.Equal("requested", containment.State);
            Assert.NotEqual(original.Id, containment.Id);
            Assert.Equal(0, new RunDirectory(connections.Service.Session.Manifest.Estate, run.Id).ReadStop()!.GraceSeconds);
            return true;
        });
    }

    [Fact]
    public async Task ContainmentAfterAPendingCancelKeepsBothRequests()
    {
        using TestEstate estate = RemoteFixtures.Seeded(3);
        using var connections = new FakeConnections(estate.Root);
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
            Assert.Equal("requested", containment.State);
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
        using var connections = new FakeConnections(estate.Root);
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
        using var connections = new FakeConnections(estate.Root);
        RecordEnvelope run = RemoteInspectionSeed.Run(estate, RemoteInspectionSeed.Assign(estate));
        await fixture.UiAsync(async () =>
        {
            RemoteViewModel model = await OpenRun(connections);
            await model.ReviewCancelCommand.ExecuteAsync();
            connections.BeforeApiCall = name =>
            {
                if (name != nameof(IEstateApi.RunStopAsync)) return;
                RecordEnvelope latest = estate.Load().Latest(run);
                JsonObject body = JsonNode.Parse(latest.Body.GetRawText())!.AsObject();
                string time = RecordParser.FormatTimestamp(estate.Clock.AddSeconds(2));
                body["status"] = "canceled";
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
