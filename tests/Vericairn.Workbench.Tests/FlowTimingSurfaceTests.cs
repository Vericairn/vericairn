// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json.Nodes;
using Avalonia.Headless;
using Avalonia.Input;
using Vericairn.Presentation;
using Vericairn.Records.Tests;
using Vericairn.Runtime.Service;

namespace Vericairn.Workbench.Tests;

/// <summary>
/// A timed step's due time on Workbench (the estate's design/flow.md § 7; PROC-18): the home page and the Goals workspace show it before
/// the step runs, as status and the goal view give it, and derive nothing of their own.
/// </summary>
[Collection("workbench")]
public class FlowTimingSurfaceTests(HeadlessFixture fixture)
{
    [Fact]
    public async Task HomeAndTheGoalsWorkspaceShowATimedStepsDueTimeAsStatusAndTheGoalViewGiveIt()
    {
        using var run = new FlowFixture(FlowFixture.Timed(new JsonObject { ["at"] = new JsonObject { ["time"] = "2099-01-01T09:00:00Z" } }));
        IReadOnlyList<DueRow> fromStatus, fromGoal;
        using (EstateService service = EstateService.Open(run.Estate.Root))
        {
            fromStatus = EstateView.From(service.Status(), "2026-11-07T00:00:00Z").Due;
            fromGoal = GoalPresentation.From(service.Goal(run.Goal.Id)!.Json, service.GoalNext(run.Goal.Id)!.Json).Due;
        }

        DueRow due = Assert.Single(fromGoal);
        Assert.Equal(fromStatus, fromGoal);
        Assert.Equal("timed: due 2099-01-01T09:00:00Z", due.Heading);
        Assert.Equal("at 2099-01-01T09:00:00Z. Timing: at 2099-01-01T09:00:00Z, in UTC.", due.Detail);

        var shell = new ShellViewModel(r => new ServiceConnection(r), new FakeRuntimeManager(), new Preferences(Seed.Temp("wb-prefs-due")));
        MainWindow window = await fixture.Ui(() => { var w = new MainWindow(shell); w.Show(); return w; });
        try
        {
            await fixture.UiAsync(() => shell.OpenEstateAsync(run.Estate.Root, cancellationToken: TestContext.Current.CancellationToken));
            Assert.True(shell.HasDue);
            Assert.Equal(fromStatus, shell.Due);

            // The Goals workspace shows the run's due times as the goal view gives them.
            await fixture.UiAsync(async () =>
            {
                window.Focus(); window.KeyPress(Key.G, RawInputModifiers.Control | RawInputModifiers.Shift, PhysicalKey.G, null);
                await shell.PendingOperation;
            });
            GoalsWorkspace page = Assert.IsType<GoalsWorkspace>(shell.SelectedDocument);
            Assert.Equal(run.Goal.Id, page.Selected!.Id);
            Assert.True(page.HasDue);
            Assert.Equal(fromGoal, page.Due);
        }
        finally
        {
            await fixture.Ui(window.Close);
        }
    }

    [Fact]
    public void DueRowsComeOnlyFromTheRuntimesDueTimes()
    {
        Assert.Empty(DueRow.From(default));
        Assert.Empty(GoalPresentation.From(default, default).Due);
        Assert.Empty(EstateView.From(default(System.Text.Json.JsonElement), "now").Due);
        DueRow missed = Assert.Single(DueRow.From(System.Text.Json.JsonSerializer.SerializeToElement(new[]
        {
            new { name = "post", at = "2026-10-16T09:00:00Z", reason = "on its schedule", timing = "on its schedule, mon at 09:00", zone = "Europe/London", deadline = "2026-10-16T10:00:00Z", missed = "attention" },
        })));
        Assert.Equal("post: due 2026-10-16T09:00:00Z", missed.Heading);
        Assert.Equal("on its schedule. Timing: on its schedule, mon at 09:00, in Europe/London. Deadline 2026-10-16T10:00:00Z. Missed its deadline; its rule: attention.", missed.Detail);
    }
}
