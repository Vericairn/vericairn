// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json.Nodes;
using Avalonia.Headless;
using Avalonia.Input;
using Vericairn.Presentation;
using Vericairn.Records.Model;
using Vericairn.Records.Tests;
using Vericairn.Runtime.Service;

namespace Vericairn.Workbench.Tests;

/// <summary>
/// A decision step's decision on Workbench (the estate's design/flow.md § 5; PROC-19): the home page and the Goals workspace show who
/// decided what and why, as status and the goal view give it, and derive nothing of their own.
/// </summary>
[Collection("workbench")]
public class FlowDecisionSurfaceTests(HeadlessFixture fixture)
{
    private static readonly string[] Dropped = ["drop"];

    [Fact]
    public async Task HomeAndTheGoalsWorkspaceShowWhoDecidedWhatAndWhyAsStatusAndTheGoalViewGiveIt()
    {
        using var run = new FlowFixture(FlowFixture.Gate(), subject: f => new JsonObject { ["recording"] = new JsonObject { ["type"] = "work-order", ["id"] = f.Work.Id } },
            rolesOf: f => new JsonArray(FlowFixture.Role("writer", f.Estate.BuilderId), FlowFixture.Role("approver", f.Estate.OperatorId)));
        RecordEnvelope lyrics = run.Result(run.Assign("draft", run.Estate.BuilderId), "completed", ("lyrics", FlowFixture.Hash('a'), null));
        RecordEnvelope decided = run.Decide(run.Assign("gate", run.Estate.OperatorId), ["ship"], "The lyrics are ready.", lyrics);
        IReadOnlyList<DecisionRow> fromStatus, fromGoal;
        using (EstateService service = EstateService.Open(run.Estate.Root))
        {
            fromStatus = EstateView.From(service.Status(), "2026-11-07T00:00:00Z").Decisions;
            fromGoal = GoalPresentation.From(service.Goal(run.Goal.Id)!.Json, service.GoalNext(run.Goal.Id)!.Json).Decisions;
        }

        DecisionRow decision = Assert.Single(fromGoal);
        Assert.Equal((decision.Heading, decision.Detail), (Assert.Single(fromStatus).Heading, fromStatus[0].Detail));
        Assert.StartsWith("gate: path ship, by Test Operator, a person at ", decision.Heading, StringComparison.Ordinal);
        Assert.EndsWith(" (taken)", decision.Heading, StringComparison.Ordinal);
        Assert.Equal($"The lyrics are ready. Recorded as result {decided.Id}. Evidence: result {lyrics.Id} at {lyrics.ComputedHash}.", decision.Detail);

        var shell = new ShellViewModel(r => new ServiceConnection(r), new FakeRuntimeManager(), new Preferences(Seed.Temp("wb-prefs-decisions")));
        MainWindow window = await fixture.Ui(() => { var w = new MainWindow(shell); w.Show(); return w; });
        try
        {
            await fixture.UiAsync(() => shell.OpenEstateAsync(run.Estate.Root, cancellationToken: TestContext.Current.CancellationToken));
            Assert.True(shell.HasDecisions);
            Assert.Equal(fromStatus.Select(d => d.Heading), shell.Decisions.Select(d => d.Heading));

            // The Goals workspace shows the run's decisions as the goal view gives them.
            await fixture.UiAsync(async () =>
            {
                window.Focus(); window.KeyPress(Key.G, RawInputModifiers.Control | RawInputModifiers.Shift, PhysicalKey.G, null);
                await shell.PendingOperation;
            });
            GoalsWorkspace page = Assert.IsType<GoalsWorkspace>(shell.SelectedDocument);
            Assert.Equal(run.Goal.Id, page.Selected!.Id);
            Assert.True(page.HasDecisions);
            Assert.Equal(fromGoal.Select(d => d.Detail), page.Decisions.Select(d => d.Detail));
        }
        finally
        {
            await fixture.Ui(window.Close);
        }
    }

    [Fact]
    public void DecisionRowsComeOnlyFromTheRuntimesDecisions()
    {
        Assert.Empty(DecisionRow.From(default));
        Assert.Empty(GoalPresentation.From(default, default).Decisions);
        Assert.Empty(EstateView.From(default(System.Text.Json.JsonElement), "now").Decisions);
        DecisionRow pending = Assert.Single(DecisionRow.From(System.Text.Json.JsonSerializer.SerializeToElement(new[]
        {
            new { name = "gate", state = "pending", decider = "rule-then-role", paths = Array.Empty<string>(), evidence = Array.Empty<object>() },
        })));
        Assert.Equal(("gate: decision pending, by rule-then-role", "It waits on its decider."), (pending.Heading, pending.Detail));
        DecisionRow ruled = Assert.Single(DecisionRow.From(System.Text.Json.JsonSerializer.SerializeToElement(new[]
        {
            new { name = "gate", state = "taken", decider = "rule", paths = Dropped, decidedBy = "default", at = "2026-10-16T09:00:00Z", reasons = "No rule holds.",
                evidence = new[] { new { type = "work-order", id = "01M4TRNE1GYEK4P8MXQAB3C8Q4", hash = "sha256:" + new string('a', 64) } } },
        })));
        Assert.Equal("gate: path drop, by its rule's default at 2026-10-16T09:00:00Z (taken)", ruled.Heading);
        Assert.Equal($"No rule holds. Records read: work-order 01M4TRNE1GYEK4P8MXQAB3C8Q4 at sha256:{new string('a', 64)}.", ruled.Detail);
    }
}
