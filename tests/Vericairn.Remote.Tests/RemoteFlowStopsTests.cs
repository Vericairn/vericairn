// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using Avalonia.Controls;
using Vericairn.Presentation;
using Vericairn.Protocol;
using Vericairn.Records.Tests;
using Vericairn.Remote;

namespace Vericairn.Remote.Tests;

/// <summary>
/// Remote shows a run's stops (the estate's design/flow.md § 8; design/release-procedure.md § 8): the operator's next reserved step with
/// its exact command, from the same status every surface reads, in the state language; Remote carries none of them out.
/// </summary>
public sealed class RemoteFlowStopsTests(HeadlessFixture fixture)
{
    [Fact]
    public async Task RemoteShowsTheOperatorsNextReservedStepWithItsCommandAsStatusDoes()
    {
        using FlowFixture run = FlowFixture.AtAcceptance();
        using var connections = new FakeConnections(run.Estate.Root);
        IReadOnlyList<StopRow> expected = EstateView.From(connections.Service.Status(), "now").Stops;
        await fixture.UiAsync(async () =>
        {
            RemoteViewModel model = await RemoteInspectionSeed.Pair(connections, new InspectionClock());
            (Window window, RemoteView view) = RemoteFixtures.Show(model);
            Assert.True(model.HasStops);
            Assert.Equal(expected, model.Stops);
            StopRow stop = Assert.Single(model.Stops);
            Assert.Equal(("reserved", "accept", run.AcceptCommandNow), (stop.Kind, stop.Name, stop.Command));
            Assert.Equal(StateLanguage.Label("flow-stop", "reserved"), stop.KindLabel);
            Assert.EndsWith("Command: " + run.AcceptCommandNow, stop.Detail, StringComparison.Ordinal);

            // The list is shown on the phone's home, and nothing there acts on a stop.
            ListBox list = Assert.Single(view.GetLogicalDescendantsOfType<ListBox>().Distinct(), l => Avalonia.Automation.AutomationProperties.GetName(l)?.StartsWith("Stops of runs", StringComparison.Ordinal) == true);
            Assert.True(list.IsVisible);
            Assert.Same(model.Stops, list.ItemsSource);
            window.Close();
            return true;
        });
    }
}

internal static class LogicalTree
{
    public static IEnumerable<T> GetLogicalDescendantsOfType<T>(this Avalonia.LogicalTree.ILogical root) where T : class =>
        Avalonia.LogicalTree.LogicalExtensions.GetLogicalDescendants(root).OfType<T>();
}
