// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json.Nodes;
using Avalonia.Controls;
using Vericairn.Presentation;
using Vericairn.Records.Model;
using Vericairn.Records.Tests;
using Vericairn.Remote;

namespace Vericairn.Remote.Tests;

/// <summary>
/// Remote shows each run's decisions (the estate's design/flow.md § 5; PROC-19), who decided what and why, from the same canonical status
/// every surface reads; it records nothing.
/// </summary>
public sealed class RemoteFlowDecisionTests(HeadlessFixture fixture)
{
    [Fact]
    public async Task RemoteShowsWhoDecidedEachPathAndWhyAsCanonicalStatusDoes()
    {
        using var run = new FlowFixture(FlowFixture.Gate(), subject: f => new JsonObject { ["recording"] = new JsonObject { ["type"] = "work-order", ["id"] = f.Work.Id } },
            rolesOf: f => new JsonArray(FlowFixture.Role("writer", f.Estate.BuilderId), FlowFixture.Role("approver", f.Estate.OperatorId)));
        RecordEnvelope lyrics = run.Result(run.Assign("draft", run.Estate.BuilderId), "completed", ("lyrics", FlowFixture.Hash('a'), null));
        run.Decide(run.Assign("gate", run.Estate.OperatorId), ["drop"], "The song is shelved.", lyrics);
        using var connections = new FakeConnections(run.Estate.Root);
        IReadOnlyList<DecisionRow> expected = EstateView.From(connections.Service.Status(), "now").Decisions;
        await fixture.UiAsync(async () =>
        {
            RemoteViewModel model = await RemoteInspectionSeed.Pair(connections, new InspectionClock());
            (Window window, RemoteView view) = RemoteFixtures.Show(model);
            Assert.True(model.HasDecisions);
            Assert.Equal(expected.Select(d => d.Heading), model.Decisions.Select(d => d.Heading));
            DecisionRow decision = Assert.Single(model.Decisions);
            Assert.Equal(("gate", "taken", "person"), (decision.Name, decision.State, decision.DecidedBy));
            Assert.StartsWith("The song is shelved.", decision.Detail, StringComparison.Ordinal);
            ListBox list = Assert.Single(view.GetLogicalDescendantsOfType<ListBox>().Distinct(), l => Avalonia.Automation.AutomationProperties.GetName(l)?.StartsWith("Decisions of runs", StringComparison.Ordinal) == true);
            Assert.True(list.IsVisible);
            Assert.Same(model.Decisions, list.ItemsSource);
            window.Close();
            return true;
        });
    }
}
