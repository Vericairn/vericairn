// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using Vericairn.Presentation;

namespace Vericairn.Workbench.Tests;

public partial class GoalsTests
{
    [Fact]
    public async Task TheStarterFlowShowsItsPinnedRevisionAndTheApprovalOfEachDraftInWorkbench()
    {
        CancellationToken cancellation = TestContext.Current.CancellationToken;
        using var journey = await ExampleWorkbenchJourney.RunAsync("e3-services.json", "agents", "inspect-review", cancellation);
        string goal = journey.Captures["goal"];
        using ServiceConnection connection = journey.OpenConnection();
        var shell = new ShellViewModel(_ => connection, new FakeRuntimeManager(), new Preferences(journey.Scratch));
        await shell.OpenEstateAsync(journey.Estate, cancellationToken: cancellation);

        GoalsWorkspace workspace = Assert.IsType<GoalsWorkspace>(await shell.OpenGoalsAsync(goal, cancellation));
        Assert.Equal(goal, workspace.Selected?.Id);
        Assert.Equal("Appointment for SR-0001: active", workspace.Heading);
        Assert.Equal(new[] { ("draft", "done"), ("approve", "done") }, workspace.Steps.Select(step => (step.Key, step.Status)));

        await shell.OpenRecordAsync(goal, cancellation);
        using JsonDocument goalRecord = InspectRecord(shell);
        JsonElement pin = goalRecord.RootElement.GetProperty("body").GetProperty("plan").GetProperty("flow").GetProperty("pin");
        Assert.Equal("vericairn.starter/service-appointment", pin.GetProperty("id").GetString());
        Assert.Equal("1.0.0", pin.GetProperty("version").GetString());
        Assert.Equal(journey.Captures["flowHash"], pin.GetProperty("hash").GetString());

        foreach ((string approval, string result, string hash) in new[]
        {
            ("approval", "draftResult", "draftResultHash"),
            ("reapproval", "revision", "revisionHash"),
        })
        {
            await shell.OpenRecordAsync(journey.Captures[approval], cancellation);
            using JsonDocument review = InspectRecord(shell);
            JsonElement subject = review.RootElement.GetProperty("subject");
            Assert.Equal(journey.Captures["operator"], review.RootElement.GetProperty("by").GetString());
            Assert.Equal("pass", review.RootElement.GetProperty("body").GetProperty("outcome").GetString());
            Assert.Equal("result", subject.GetProperty("type").GetString());
            Assert.Equal(journey.Captures[result], subject.GetProperty("id").GetString());
            Assert.Equal(journey.Captures[hash], subject.GetProperty("hash").GetString());
        }
    }

    private static JsonDocument InspectRecord(ShellViewModel shell) =>
        JsonDocument.Parse(Assert.IsType<CompanionDocument>(shell.SelectedDocument).SourceText);
}
