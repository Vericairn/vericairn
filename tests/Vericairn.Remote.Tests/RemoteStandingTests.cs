// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using Avalonia.Controls;
using Vericairn.Presentation;
using Vericairn.Records.Tests;
using Vericairn.Remote;

namespace Vericairn.Remote.Tests;

/// <summary>
/// From 0.9.4 (GOAL-07): Remote shows each standing agent's state, next occurrence, occurrences by state and period spend from the same
/// canonical status every surface reads; it carries no occurrence.
/// </summary>
public sealed class RemoteStandingTests(HeadlessFixture fixture)
{
    [Fact]
    public async Task RemoteShowsAStandingAgentAsCanonicalStatusDoes()
    {
        using var standing = new StandingFixture();
        using var connections = new FakeConnections(standing.Estate.Root);
        IReadOnlyList<StandingRow> expected = EstateView.From(connections.Service.Status(), "now").Standing;
        Assert.Single(expected);
        await fixture.UiAsync(async () =>
        {
            RemoteViewModel model = await RemoteInspectionSeed.Pair(connections, new InspectionClock());
            (Window window, RemoteView view) = RemoteFixtures.Show(model);
            Assert.True(model.HasStanding);
            Assert.Equal(expected, model.Standing);
            ListBox list = Assert.Single(view.GetLogicalDescendantsOfType<ListBox>().Distinct(), l => Avalonia.Automation.AutomationProperties.GetName(l)?.StartsWith("Standing agents", StringComparison.Ordinal) == true);
            Assert.True(list.IsVisible);
            Assert.Same(model.Standing, list.ItemsSource);
            window.Close();
            return true;
        });
    }
}
