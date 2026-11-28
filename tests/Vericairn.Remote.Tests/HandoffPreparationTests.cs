// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using Vericairn.Protocol;
using Vericairn.Records.Tests;
using Vericairn.Remote;

namespace Vericairn.Remote.Tests;

public sealed class HandoffPreparationTests(HeadlessFixture fixture)
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UncertainPreparationReusesItsOriginalAfterBackOrRestart(bool restart)
    {
        using TestEstate estate = RemoteFixtures.Seeded(3);
        using var connections = new FakeConnections(estate.Root) { LoseDesktopHandoffResponse = true };
        RemoteInspectionSeed.Assign(estate);
        await fixture.UiAsync(async () =>
        {
            var clock = new InspectionClock();
            RemoteViewModel model = await RemoteInspectionSeed.Pair(connections, clock);
            model.SelectedAssignment = Assert.Single(model.Assignments);
            await model.OpenAssignmentCommand.ExecuteAsync();
            await model.ComposeWorkHandoffCommand.ExecuteAsync();
            model.HandoffTask = "The original task";
            model.HandoffReason = "Need the current originals";
            model.HandoffDraft = "Retain this draft";
            await model.PrepareHandoffCommand.ExecuteAsync();
            Assert.Contains("uncertain", model.HandoffMessage, StringComparison.Ordinal);
            DesktopHandoff original = Assert.Single(connections.Service.DesktopHandoffs(estate.OperatorId).Handoffs);

            if (restart)
            {
                model = new RemoteViewModel(connections, clock);
                await model.RefreshCommand.ExecuteAsync();
                model.SelectedAssignment = Assert.Single(model.Assignments);
                await model.OpenAssignmentCommand.ExecuteAsync();
            }
            else await model.CloseHandoffCommand.ExecuteAsync();

            Assert.Contains("uncertain", model.HandoffMessage, StringComparison.Ordinal);
            await model.ComposeWorkHandoffCommand.ExecuteAsync();
            Assert.Equal(original.Task, model.HandoffTask);
            Assert.Equal(original.Reason, model.HandoffReason);
            Assert.Equal(original.Draft, model.HandoffDraft);
            Assert.False(model.HandoffEditable);
            connections.LoseDesktopHandoffResponse = false;
            await model.PrepareHandoffCommand.ExecuteAsync();

            DesktopHandoff kept = Assert.Single(connections.Service.DesktopHandoffs(estate.OperatorId).Handoffs);
            Assert.Equal(original.Id, kept.Id);
            Assert.Equal(original.PreparedAt, kept.PreparedAt);
            Assert.Equal(original.Draft, Assert.Single(model.Handoffs).Draft);
            Assert.Empty(connections.LoadPendingHandoffs(Assert.Single(connections.Kept)));
            return true;
        });
    }
}
