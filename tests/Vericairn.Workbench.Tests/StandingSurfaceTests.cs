// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using Vericairn.Presentation;
using Vericairn.Protocol;
using Vericairn.Records.Tests;
using Vericairn.Runtime.Service;

namespace Vericairn.Workbench.Tests;

/// <summary>
/// From 0.9.4 (GOAL-07): a standing agent on Workbench (the estate's design/standing-agents.md § 11): the home page shows each agent's
/// state, next occurrence, occurrences by state and period spend as canonical status renders them, and the goal view gives the same
/// agent; nothing here carries an occurrence.
/// </summary>
[Collection("workbench")]
public class StandingSurfaceTests(HeadlessFixture fixture)
{
    [Fact]
    public async Task HomeAndTheGoalViewShowAStandingAgentAsStatusRendersIt()
    {
        using var standing = new StandingFixture();
        IReadOnlyList<StandingRow> fromStatus, fromGoal;
        using (EstateService service = EstateService.Open(standing.Estate.Root))
        {
            fromStatus = EstateView.From(service.Status(), "2026-11-17T00:00:00Z").Standing;
            fromGoal = GoalPresentation.From(service.Goal(standing.Goal.Id)!.Json, service.GoalNext(standing.Goal.Id)!.Json).Standing;
        }

        StandingRow row = Assert.Single(fromStatus);
        Assert.Equal((standing.Goal.Id, "Research the widget", "active", 1), (row.Goal, row.Title, row.State, row.Due));
        Assert.StartsWith("Research the widget: active: its occurrences are carried one per goal step; next occurrence ", row.Heading, StringComparison.Ordinal);
        Assert.Contains("this period runs 0 of 4, wallMinutes 0 of 120", row.Detail, StringComparison.Ordinal);
        Assert.Equal((row.Goal, row.State), (Assert.Single(fromGoal).Goal, Assert.Single(fromGoal).State));

        var shell = new ShellViewModel(r => new ServiceConnection(r), new FakeRuntimeManager(), new Preferences(Seed.Temp("wb-prefs-standing")));
        MainWindow window = await fixture.Ui(() => { var w = new MainWindow(shell); w.Show(); return w; });
        try
        {
            await fixture.UiAsync(() => shell.OpenEstateAsync(standing.Estate.Root, cancellationToken: TestContext.Current.CancellationToken));
            Assert.True(shell.HasStanding);
            Assert.Equal(fromStatus, shell.Standing);
        }
        finally
        {
            await fixture.Ui(window.Close);
        }
    }

    [Fact]
    public void AStandingRowComesOnlyFromTheRuntimesSummaryAndAHeldAgentNeedsItsSponsor()
    {
        Assert.Empty(StandingRow.From(default));
        Assert.Empty(GoalPresentation.From(default, default).Standing);
        JsonElement status = JsonSerializer.SerializeToElement(new
        {
            standingAgents = new[] { new { goal = new { id = "01M7ASMJ43WMQ5VDASDYVZ7BGV", title = "Research", status = "active" }, state = "held", reason = "three runs failed", next = (string?)null, counts = new { due = 0, deferred = 1, running = 0, settled = 0 } } },
        }, ProtocolJson.Options);
        EstateView view = EstateView.From(status, "now");
        StandingRow held = Assert.Single(view.Standing);
        Assert.Equal("Research: held by its failures: its sponsor is asked; next occurrence on a record or a request", held.Heading);
        Assert.Contains(view.Attention, a => a.Kind == "decision needed" && a.Subject == "standing agent Research" && a.SubjectId == held.Goal);
    }
}
