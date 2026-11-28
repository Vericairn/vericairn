// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using Vericairn.Records.Model;
using Vericairn.Records.Tests;
using Vericairn.Runtime.Workers;

namespace Vericairn.Remote.Tests;

public sealed class SignedRemoteInterventionTests(HeadlessFixture fixture)
{
    [Theory]
    [InlineData("pause")]
    [InlineData("cancel")]
    [InlineData("contain")]
    public async Task APhoneWithoutItsPresenceKeyExplainsTheRefusalAndOffersDesktopHandoff(string operation)
    {
        using TestEstate estate = RemoteFixtures.Seeded(3);
        using var connections = new FakeConnections(estate.Root) { EnableSignedWrites = true };
        RecordEnvelope goal = RemoteInspectionSeed.Goal(estate);
        estate.Add("goal-status", estate.OperatorId, """{"status":"active","note":"Activated before the review."}""", goal.Reference);
        RecordEnvelope run = RemoteInspectionSeed.Run(estate, RemoteInspectionSeed.Assign(estate), goal);

        await fixture.UiAsync(async () =>
        {
            RemoteViewModel model = await RemoteInspectionSeed.Pair(connections, new InspectionClock());
            connections.PresenceKeys.Remove(connections.Kept.Single().PresenceDevice!);
            model.SelectedAssignment = Assert.Single(model.Assignments);
            await model.OpenAssignmentCommand.ExecuteAsync();
            model.InterventionTarget = Assert.Single(model.InterventionTargets);
            var review = operation switch
            {
                "pause" => model.ReviewPauseCommand,
                "cancel" => model.ReviewCancelCommand,
                _ => model.ReviewContainCommand,
            };

            await review.ExecuteAsync();
            await model.SubmitInterventionCommand.ExecuteAsync();

            Assert.Contains("presence key", model.InterventionMessage, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("inspect", model.InterventionMessage, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("desktop handoff", model.InterventionMessage, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("401", model.InterventionMessage, StringComparison.Ordinal);
            Assert.Equal("rejected", Assert.Single(model.Interventions).State);
            Assert.Single(estate.Load().About(goal.Id, "goal-status"));
            Assert.Null(new RunDirectory(connections.Service.Session.Manifest.Estate, run.Id).ReadStop());
            return true;
        });
    }
}
