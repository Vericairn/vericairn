// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json.Nodes;
using Avalonia.Controls;
using Vericairn.Presentation;
using Vericairn.Records.Tests;
using Vericairn.Remote;

namespace Vericairn.Remote.Tests;

/// <summary>
/// Remote shows a timed step's due time (the estate's design/flow.md § 7; PROC-18) from the same canonical status every surface reads,
/// before the step runs; it starts nothing.
/// </summary>
public sealed class RemoteFlowTimingTests(HeadlessFixture fixture)
{
    [Fact]
    public async Task RemoteShowsEachTimedStepsDueTimeAsCanonicalStatusDoes()
    {
        using var run = new FlowFixture(FlowFixture.Timed(new JsonObject { ["at"] = new JsonObject { ["time"] = "2099-01-01T09:00:00Z" } }));
        using var connections = new FakeConnections(run.Estate.Root);
        IReadOnlyList<DueRow> expected = EstateView.From(connections.Service.Status(), "now").Due;
        await fixture.UiAsync(async () =>
        {
            RemoteViewModel model = await RemoteInspectionSeed.Pair(connections, new InspectionClock());
            (Window window, RemoteView view) = RemoteFixtures.Show(model);
            Assert.True(model.HasDue);
            Assert.Equal(expected, model.Due);
            Assert.Equal(("timed", "2099-01-01T09:00:00Z"), (Assert.Single(model.Due).Name, model.Due[0].At));
            ListBox list = Assert.Single(view.GetLogicalDescendantsOfType<ListBox>().Distinct(), l => Avalonia.Automation.AutomationProperties.GetName(l)?.StartsWith("Due times of runs", StringComparison.Ordinal) == true);
            Assert.True(list.IsVisible);
            Assert.Same(model.Due, list.ItemsSource);
            window.Close();
            return true;
        });
    }
}
