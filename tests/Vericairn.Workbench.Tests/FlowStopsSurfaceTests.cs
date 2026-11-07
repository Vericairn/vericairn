// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using Avalonia.Headless;
using Avalonia.Input;
using Vericairn.Presentation;
using Vericairn.Protocol;
using Vericairn.Records.Tests;
using Vericairn.Runtime.Service;

namespace Vericairn.Workbench.Tests;

/// <summary>
/// A run's stops on Workbench (the estate's design/flow.md § 8; design/release-procedure.md § 8; design/cohesion-contract.md § 4): the
/// home page and the Goals workspace show the same stops status and the goal view render, in the state language, with the operator's
/// next reserved step and its exact command; neither offers to carry one out.
/// </summary>
[Collection("workbench")]
public class FlowStopsSurfaceTests(HeadlessFixture fixture)
{
    [Fact]
    public async Task HomeAndTheGoalsWorkspaceShowARunsStopsAsStatusAndTheGoalViewRenderThem()
    {
        using FlowFixture run = FlowFixture.AtAcceptance();
        IReadOnlyList<StopRow> fromStatus, fromGoal;
        using (EstateService service = EstateService.Open(run.Estate.Root))
        {
            fromStatus = EstateView.From(service.Status(), "2026-11-07T00:00:00Z").Stops;
            fromGoal = GoalPresentation.From(service.Goal(run.Goal.Id)!.Json, service.GoalNext(run.Goal.Id)!.Json).Stops;
        }

        // Status and the goal view give the same stop, worded in the state language, the command among it.
        StopRow stop = Assert.Single(fromStatus);
        Assert.Equal(fromStatus, fromGoal);
        Assert.Equal(("reserved", "accept", "delivered", run.AcceptCommandNow), (stop.Kind, stop.Name, stop.Delivery, stop.Command));
        Assert.Equal(StateLanguage.Label("flow-stop", "reserved"), stop.KindLabel);
        Assert.Equal(StateLanguage.Label("flow-delivery", "delivered"), stop.DeliveryLabel);
        Assert.Equal("accept: a reserved act: it waits on the operator with its exact command; waits on Test Operator", stop.Heading);
        Assert.Contains($"[operator-request {run.Request!.Id}]", stop.Detail, StringComparison.Ordinal);
        Assert.EndsWith("Command: " + run.AcceptCommandNow, stop.Detail, StringComparison.Ordinal);

        var shell = new ShellViewModel(r => new ServiceConnection(r), new FakeRuntimeManager(), new Preferences(Seed.Temp("wb-prefs-stops")));
        MainWindow window = await fixture.Ui(() => { var w = new MainWindow(shell); w.Show(); return w; });
        try
        {
            await fixture.UiAsync(() => shell.OpenEstateAsync(run.Estate.Root, cancellationToken: TestContext.Current.CancellationToken));
            Assert.True(shell.HasStops);
            Assert.Equal(fromStatus, shell.Stops);

            // The Goals workspace shows the run's stops as the goal view gives them.
            await fixture.UiAsync(async () =>
            {
                window.Focus(); window.KeyPress(Key.G, RawInputModifiers.Control | RawInputModifiers.Shift, PhysicalKey.G, null);
                await shell.PendingOperation;
            });
            GoalsWorkspace page = Assert.IsType<GoalsWorkspace>(shell.SelectedDocument);
            Assert.Equal(run.Goal.Id, page.Selected!.Id);
            Assert.True(page.HasStops);
            Assert.Equal(fromStatus, page.Stops);
        }
        finally
        {
            await fixture.Ui(window.Close);
        }
    }

    [Fact]
    public void AStopsRowsComeOnlyFromTheRuntimesStops()
    {
        Assert.Empty(StopRow.From(default));
        Assert.Empty(GoalPresentation.From(default, default).Stops);
        Assert.Empty(EstateView.From(default(System.Text.Json.JsonElement), "now").Stops);
        StopRow unnamed = Assert.Single(StopRow.From(System.Text.Json.JsonSerializer.SerializeToElement(new[] { new { kind = "accept", name = "the goal", title = "the goal's acceptance", reason = "ready", delivery = "outside" } }, ProtocolJson.Options)));
        Assert.Equal("the goal: the run is complete: the goal is ready to accept; waits on a person the records do not name", unnamed.Heading);
        Assert.Equal("carried outside goal step: nothing is recorded to reach its person. ready", unnamed.Detail);
    }
}
