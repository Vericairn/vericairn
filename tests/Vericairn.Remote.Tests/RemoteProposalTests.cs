// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using Avalonia.Controls;
using Vericairn.Records.Model;
using Vericairn.Records.Tests;
using Vericairn.Remote;

namespace Vericairn.Remote.Tests;

/// <summary>
/// Remote shows the sponsor a work order proposed for a goal whose plan says sponsor-approves, waiting for the sponsor's approval before
/// it joins the goal (the estate's design/goals.md §§ 4 and 6, FIX-117), from the runtime's attention; Remote approves nothing.
/// </summary>
public sealed class RemoteProposalTests(HeadlessFixture fixture)
{
    [Fact]
    public async Task RemoteShowsTheSponsorAProposedWorkOrderWaitingForItsApproval()
    {
        using var estate = new TestEstate(recordFormat: 2);
        RecordEnvelope goal = estate.Add("goal", estate.OperatorId, JsonSerializer.Serialize(new
        {
            title = "Release the guide", objective = "A published guide.", completion = new[] { new { evidence = "The guide is published.", check = "Read it." } },
            constraints = Array.Empty<string>(), kind = "finite", sponsor = estate.OperatorId, budget = new { wallMinutes = 60, attemptsPerStep = 3 },
            plan = new { policy = "proposed-by-planner", approval = "sponsor-approves" }, inputs = Array.Empty<string>(),
        }));
        estate.Add("goal-status", estate.OperatorId, """{"status":"active","note":"Activated."}""", goal.Reference);
        RecordEnvelope order = estate.Add("work-order", estate.BuilderId, $$"""{"key":"W-1","kind":"ordinary","title":"Write the guide","intent":"Work.","scope":["guide"],"exitQuestion":"Done?","inputs":[{"type":"goal","id":"{{goal.Id}}"}],"deliverables":["guide"]}""");
        using var connections = new FakeConnections(estate.Root);
        await fixture.UiAsync(async () =>
        {
            RemoteViewModel model = await RemoteInspectionSeed.Pair(connections, new InspectionClock());
            (Window window, RemoteView _) = RemoteFixtures.Show(model);
            string[] before = estate.Load().Records.Select(r => r.ComputedHash).ToArray();
            var waiting = Assert.Single(model.Attention, a => a.SubjectType == "work-order" && a.SubjectId == order.Id);
            Assert.Equal(("decision needed", "W-1 Write the guide", "waiting for the sponsor"), (waiting.Kind, waiting.Subject, waiting.State));
            Assert.Equal($"Proposed for goal {goal.Id} (Release the guide) by {estate.BuilderId}; its plan says the sponsor approves, so it joins the goal only once you approve it.", waiting.Why);
            Assert.Contains($"--subject work-order:{order.Id}:{order.ComputedHash} --body -", waiting.NextAction, StringComparison.Ordinal);
            Assert.Equal(before, estate.Load().Records.Select(r => r.ComputedHash).ToArray());
            window.Close();
            return true;
        });
    }
}
