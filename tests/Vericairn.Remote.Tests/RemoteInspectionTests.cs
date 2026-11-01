// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using Vericairn.Protocol;
using Vericairn.Records.Model;
using Vericairn.Records.Tests;
using Vericairn.Remote;
using Vericairn.Sdk;
using Vericairn.Runtime.Workers;

namespace Vericairn.Remote.Tests;

public sealed class RemoteAttentionTests(HeadlessFixture fixture)
{
    [Fact]
    public async Task ALostAcknowledgmentResponseIsUnconfirmedAndNeverRetriedByThePhone()
    {
        using TestEstate estate = RemoteFixtures.Seeded(3);
        RecordEnvelope assignment = RemoteInspectionSeed.Assign(estate);
        _ = RemoteInspectionSeed.Result(estate, assignment);
        using var connections = new FakeConnections(estate.Root);
        await fixture.UiAsync(async () =>
        {
            var clock = new InspectionClock();
            RemoteViewModel model = await RemoteInspectionSeed.Pair(connections, clock);
            model.SelectedAttention = model.Attention.First(a => a.Notice is { SubjectType: "result" });
            string key = model.SelectedAttention.Notice!.Key;
            int records = estate.Load().Records.Count;
            connections.LoseAcknowledgmentResponse = true;
            await model.AcknowledgeCommand.ExecuteAsync();
            Assert.Equal(ConnectionStates.Cached, model.ConnectionState);
            Assert.Contains("not confirmed", model.DetailMessage, StringComparison.Ordinal);
            Assert.Equal("unacknowledged", Assert.Single(model.Attention, a => a.Notice!.Key == key).AttentionState);
            int calls = connections.ApiCalls;
            clock.Advance(TimeSpan.FromSeconds(5));
            model.RefreshFreshness();
            await model.AcknowledgeCommand.ExecuteAsync();
            Assert.Equal(calls, connections.ApiCalls);
            Assert.Equal("5 seconds old", model.ObservationAge);
            await model.RefreshCommand.ExecuteAsync();
            Assert.Equal("acknowledged", Assert.Single(model.Attention, a => a.Notice!.Key == key).AttentionState);
            Assert.Equal(records, estate.Load().Records.Count);
            return true;
        });
    }

    [Fact]
    public async Task AcknowledgmentStaysVisibleAcrossDevicesAndOfflineTapsSendNothing()
    {
        using TestEstate estate = RemoteFixtures.Seeded(3);
        RecordEnvelope assignment = RemoteInspectionSeed.Assign(estate);
        _ = RemoteInspectionSeed.Result(estate, assignment);
        using var connections = new FakeConnections(estate.Root);
        await fixture.UiAsync(async () =>
        {
            var clock = new InspectionClock();
            RemoteViewModel model = await RemoteInspectionSeed.Pair(connections, clock);
            model.SelectedAttention = model.Attention.First(a => a.Notice is { SubjectType: "result" });
            string key = model.SelectedAttention.Notice!.Key;
            string[] records = estate.Load().Records.Select(r => r.ComputedHash).ToArray();
            Assert.True(model.AcknowledgeCommand.CanExecute(null));
            await model.AcknowledgeCommand.ExecuteAsync();
            Assert.Equal("acknowledged", Assert.Single(model.Attention, a => a.Notice!.Key == key).AttentionState);
            Assert.False(model.AcknowledgeCommand.CanExecute(null));
            Assert.Equal(records, estate.Load().Records.Select(r => r.ComputedHash));
            using var secondDevice = new FakeConnections(estate.Root);
            RemoteViewModel second = await RemoteInspectionSeed.Pair(secondDevice, clock);
            Assert.Equal("acknowledged", Assert.Single(second.Attention, a => a.Notice!.Key == key).AttentionState);

            connections.Checks.Enqueue((ConnectionStates.Cached, "Offline."));
            clock.Advance(TimeSpan.FromMinutes(3));
            await model.RefreshCommand.ExecuteAsync();
            int calls = connections.ApiCalls;
            model.SelectedAttention = model.Attention.First(a => a.AttentionState == "unacknowledged");
            await model.AcknowledgeCommand.ExecuteAsync();
            Assert.Equal(calls, connections.ApiCalls);
            Assert.False(model.AcknowledgeCommand.CanExecute(null));
            Assert.Equal("180 seconds old", model.ObservationAge);
            Assert.All(model.Attention, a => Assert.StartsWith("cached ", a.AsOf, StringComparison.Ordinal));
            var restored = new RemoteViewModel(connections, clock);
            Assert.Equal(ConnectionStates.Cached, restored.ConnectionState);
            Assert.Equal("180 seconds old", restored.ObservationAge);
            Assert.Equal(model.Attention.Count, restored.Attention.Count);
            Assert.Equal(calls, connections.ApiCalls);
            return true;
        });
    }
}

public sealed class RemoteWorkDetailTests(HeadlessFixture fixture)
{
    [Fact]
    public async Task AHistoricalResultKeepsItsOwnAdmittedScopeAfterTheAssignmentChanges()
    {
        using TestEstate estate = RemoteFixtures.Seeded(3);
        RecordEnvelope assignment = RemoteInspectionSeed.Assign(estate);
        RecordEnvelope attempt = RemoteInspectionSeed.Result(estate, assignment);
        estate.Add("assignment", estate.OperatorId, assignment.Body.GetRawText().Replace("path:src/**", "path:docs/**", StringComparison.Ordinal), assignment.Subject, assignment.Id);
        using var connections = new FakeConnections(estate.Root);
        await fixture.UiAsync(async () =>
        {
            RemoteViewModel model = await RemoteInspectionSeed.Pair(connections, new InspectionClock());
            model.SelectedAttention = model.Attention.First(a => a.Notice is { SubjectType: "result" });
            await model.OpenAttentionCommand.ExecuteAsync();
            Assert.Equal(assignment.Id, model.WorkDetail!.Assignment);
            Assert.Contains("Historical assignment", model.WorkDetail.Scope, StringComparison.Ordinal);
            Assert.Contains("path:src/**", model.WorkDetail.Scope, StringComparison.Ordinal);
            Assert.DoesNotContain("path:docs/**", model.WorkDetail.Scope, StringComparison.Ordinal);
            Assert.Contains(assignment.Subject!.Hash!, model.WorkDetail.Scope, StringComparison.Ordinal);
            Assert.Contains(attempt.Id, model.WorkDetail.History, StringComparison.Ordinal);
            Assert.Contains("partial", model.WorkDetail.LatestResult, StringComparison.Ordinal);
            Assert.Contains("current acceptance not observed", model.WorkDetail.LatestResult, StringComparison.Ordinal);
            connections.Checks.Enqueue((ConnectionStates.Cached, "Offline."));
            await model.RefreshCommand.ExecuteAsync();
            int calls = connections.ApiCalls;
            await model.OpenAttentionCommand.ExecuteAsync();
            Assert.Equal(calls, connections.ApiCalls);
            Assert.Contains("Historical assignment", model.WorkDetail.Scope, StringComparison.Ordinal);
            return true;
        });
    }

    [Fact]
    public async Task OpeningAResultFollowsItsWorkAndRetainsTheLatestResultWhileAnotherAttemptRuns()
    {
        using TestEstate estate = RemoteFixtures.Seeded(3);
        RecordEnvelope assignment = RemoteInspectionSeed.Assign(estate);
        _ = RemoteInspectionSeed.Result(estate, assignment);
        RecordEnvelope next = estate.Add("attempt", estate.BuilderId, """{"startedAt":"2026-11-01T11:00:00Z","workspace":"widget branch work/phone","note":"Continue the work."}""", assignment.Reference);
        using var connections = new FakeConnections(estate.Root);
        await fixture.UiAsync(async () =>
        {
            var clock = new InspectionClock();
            RemoteViewModel model = await RemoteInspectionSeed.Pair(connections, clock);
            model.SelectedAttention = model.Attention.First(a => a.Notice is { SubjectType: "result" });
            await model.OpenAttentionCommand.ExecuteAsync();
            Assert.Equal(assignment.Id, model.WorkDetail!.Assignment);
            Assert.Contains("partial", model.WorkDetail.LatestResult, StringComparison.Ordinal);
            Assert.Contains(next.Id, model.WorkDetail.History, StringComparison.Ordinal);
            Assert.Contains("No result observed.", model.WorkDetail.History, StringComparison.Ordinal);
            clock.Advance(TimeSpan.FromMinutes(1));
            await model.RefreshCommand.ExecuteAsync();
            Assert.Equal("0 seconds old", model.ObservationAge);
            Assert.StartsWith("cached ", model.DetailFreshness, StringComparison.Ordinal);
            Assert.Contains("60 seconds old", model.DetailFreshness, StringComparison.Ordinal);
            Assert.False(model.ShowWorkList);
            int calls = connections.ApiCalls;
            await model.BackToWorkCommand.ExecuteAsync();
            Assert.True(model.ShowWorkList);
            Assert.Null(model.WorkDetail);
            Assert.Equal(calls, connections.ApiCalls);
            return true;
        });
    }

    [Fact]
    public async Task DetailRetainsScopeAttemptsResultsAndSeparateClaimsThenRestoresOffline()
    {
        using TestEstate estate = RemoteFixtures.Seeded(3);
        RecordEnvelope assignment = RemoteInspectionSeed.Assign(estate);
        RecordEnvelope run = RemoteInspectionSeed.Run(estate, assignment);
        RecordEnvelope attempt = RemoteInspectionSeed.Result(estate, assignment);
        using var connections = new FakeConnections(estate.Root);
        await fixture.UiAsync(async () =>
        {
            var clock = new InspectionClock();
            RemoteViewModel model = await RemoteInspectionSeed.Pair(connections, clock);
            var worker = new RunDirectory(connections.Service.Session.Manifest.Estate, run.Id);
            worker.Create();
            worker.Write(new RunObservedState { Run = run.Id, Heartbeat = "2026-11-01T11:59:50Z", Liveness = new(120, "2026-11-01T11:59:40Z") });
            connections.Service.WorkerOptions = WorkerOptions.Default with { Clock = clock };
            model.SelectedAssignment = Assert.Single(model.Assignments);
            await model.OpenAssignmentCommand.ExecuteAsync();
            Assert.NotNull(model.WorkDetail);
            Assert.Equal(assignment.Id, model.WorkDetail.Assignment);
            Assert.Contains("Exercise Remote's home.", model.WorkDetail.Goal, StringComparison.Ordinal);
            Assert.Contains("path:src/**", model.WorkDetail.Scope, StringComparison.Ordinal);
            Assert.Contains("no publication", model.WorkDetail.Scope, StringComparison.Ordinal);
            Assert.Contains("Keep the complete evidence.", model.WorkDetail.Scope, StringComparison.Ordinal);
            Assert.Contains(attempt.Id, model.WorkDetail.History, StringComparison.Ordinal);
            Assert.Contains("partial", model.WorkDetail.LatestResult, StringComparison.Ordinal);
            Assert.Contains("A remaining limitation.", model.WorkDetail.History, StringComparison.Ordinal);
            Assert.Contains("Inspect the result.", model.WorkDetail.History, StringComparison.Ordinal);
            Assert.Contains("Agent's progress claim", model.ProgressClaims, StringComparison.Ordinal);
            Assert.Contains("accurate: 4 of 10 files", model.ProgressClaims, StringComparison.Ordinal);
            Assert.Contains("Read four files.", model.ProgressClaims, StringComparison.Ordinal);
            Assert.Contains("Worker liveness: last event 2026-11-01T11:59:40Z", model.ProgressClaims, StringComparison.Ordinal);
            Assert.Contains("20 seconds old", model.ProgressClaims, StringComparison.Ordinal);
            Assert.Contains(run.Id, model.RunObservations, StringComparison.Ordinal);
            Assert.Contains("dispatched, outcome pending", model.RunObservations, StringComparison.Ordinal);
            Assert.Contains("heartbeat 2026-11-01T11:59:50Z; 10 seconds old", model.RunObservations, StringComparison.Ordinal);
            Assert.Contains("as of", model.ProgressClaims, StringComparison.Ordinal);
            Assert.Equal(1, model.SelectedArea);

            clock.Advance(TimeSpan.FromMinutes(2));
            connections.Checks.Enqueue((ConnectionStates.Cached, "Offline."));
            await model.RefreshCommand.ExecuteAsync();
            Assert.StartsWith("cached ", model.DetailFreshness, StringComparison.Ordinal);
            Assert.Contains("120 seconds old", model.DetailFreshness, StringComparison.Ordinal);
            var restored = new RemoteViewModel(connections, clock);
            restored.SelectedAssignment = Assert.Single(restored.Assignments);
            int calls = connections.ApiCalls;
            await restored.OpenAssignmentCommand.ExecuteAsync();
            Assert.NotNull(restored.WorkDetail);
            Assert.Equal(model.WorkDetail.History, restored.WorkDetail.History);
            Assert.Contains("Cached reporting status", restored.ProgressClaims, StringComparison.Ordinal);
            Assert.Equal(calls, connections.ApiCalls);
            restored.SelectedWork = Assert.Single(restored.Work);
            await restored.OpenWorkCommand.ExecuteAsync();
            Assert.Contains("was not cached", restored.DetailMessage, StringComparison.Ordinal);
            Assert.Equal(calls, connections.ApiCalls);
            return true;
        });
    }
}

public sealed class RemoteCommunicationTests(HeadlessFixture fixture)
{
    [Fact]
    public async Task CommunicationShowsInheritancePreviewAndFreshnessWithoutWriting()
    {
        using TestEstate estate = RemoteFixtures.Seeded(3);
        using var connections = new FakeConnections(estate.Root);
        await fixture.UiAsync(async () =>
        {
            int before = estate.Load().Records.Count;
            RemoteViewModel model = await RemoteInspectionSeed.Pair(connections, new InspectionClock());
            Assert.Contains("Communication for " + estate.OperatorId, model.Communication, StringComparison.Ordinal);
            Assert.Contains("shipped", model.Communication, StringComparison.Ordinal);
            Assert.Contains("Freshness:", model.Communication, StringComparison.Ordinal);
            Assert.Contains("Preview", model.Communication, StringComparison.Ordinal);
            Assert.Contains("read-only", RemoteViewModel.WorkLater, StringComparison.Ordinal);
            Assert.Contains("PROMPT-02 in 0.9.1", RemoteViewModel.WorkLater, StringComparison.Ordinal);
            connections.Checks.Enqueue((ConnectionStates.Cached, "Offline."));
            await model.RefreshCommand.ExecuteAsync();
            Assert.StartsWith("Cached ", model.Communication, StringComparison.Ordinal);
            Assert.Equal(before, estate.Load().Records.Count);
            return true;
        });
    }
}

internal sealed class InspectionClock : TimeProvider
{
    private DateTimeOffset now = new(2026, 11, 1, 12, 0, 0, TimeSpan.Zero);
    public override DateTimeOffset GetUtcNow() => now;
    public void Advance(TimeSpan amount) => now += amount;
}

internal static class RemoteInspectionSeed
{
    internal static async Task<RemoteViewModel> Pair(FakeConnections connections, TimeProvider clock)
    {
        var model = new RemoteViewModel(connections, clock) { Address = "192.168.1.20:7443", Code = "ABCD-EFGH" };
        await model.PairCommand.ExecuteAsync();
        Assert.Equal(ConnectionStates.Current, model.ConnectionState);
        return model;
    }

    internal static RecordEnvelope Assign(TestEstate estate) => estate.Add("assignment", estate.OperatorId, $$$$$"""
        {"contributor":"{{{{{estate.BuilderId}}}}}","brief":{"outcome":"Inspect phone work.","sources":[],"permittedChanges":["path:src/**"],"prohibitedEffects":["no publication"],"workspace":["widget branch work/phone"],"limits":[],"expectedOutputs":["result"],"reviewCriteria":["Keep the complete evidence."],"recovery":"Read the result.","progressRequest":{"types":["accurate"],"statusText":true,"cadence":{"minutes":10}}}}
        """, estate.Load().Current("work-order").Single().Reference);

    internal static RecordEnvelope Run(TestEstate estate, RecordEnvelope assignment)
    {
        var goal = estate.Add("goal", estate.OperatorId, JsonSerializer.Serialize(new
        {
            title = "Inspect phone work", objective = "Read the work from a phone.", completion = new[] { new { evidence = "Tests", check = "Run them." } },
            constraints = Array.Empty<string>(), kind = "finite", sponsor = estate.OperatorId, budget = new { wallMinutes = 60, attemptsPerStep = 3 },
            plan = new { policy = "proposed-by-planner", approval = "sponsor-approves" }, inputs = Array.Empty<string>(),
        }));
        var body = new
        {
            provider = "generic", action = "agent-run", identity = "phone-observation", intent = "Inspect the phone view.",
            target = new { assignment = assignment.Id, principal = estate.BuilderId, workspace = "widget branch work/phone", goal = goal.Id },
            status = "not-started", cap = new { wallMinutes = 5, attemptsPerStep = 3 }, evidence = Array.Empty<string>(),
        };
        RecordEnvelope intent = estate.Add("operation", estate.OperatorId, JsonSerializer.Serialize(body));
        estate.Add("operation", estate.OperatorId, JsonSerializer.Serialize(body).Replace("not-started", "dispatched", StringComparison.Ordinal), supersedes: intent.Id);
        return intent;
    }

    internal static RecordEnvelope Result(TestEstate estate, RecordEnvelope assignment)
    {
        RecordEnvelope attempt = estate.Add("attempt", estate.BuilderId, """{"startedAt":"2026-10-13T12:00:00Z","workspace":"widget branch work/phone","note":"Inspect the phone view."}""", assignment.Reference);
        estate.Add("work-progress", estate.BuilderId, JsonSerializer.Serialize(new
        {
            phase = "inspection", note = "Inspecting the phone view.", completedAt = RecordParser.FormatTimestamp(estate.Clock.AddSeconds(1)),
            progress = new { type = "accurate", done = 4, total = 10, unit = "files" }, statusText = "Read four files.",
        }), attempt.Reference);
        estate.Add("result", estate.BuilderId, """{"outcome":"partial","summary":"Work observed.","outputs":[],"evidence":[],"checks":[{"name":"view","outcome":"not-performed","summary":"Still inspecting."}],"limitations":["A remaining limitation."],"unresolvedEffects":[],"nextStep":"Inspect the result."}""", attempt.Reference);
        return attempt;
    }
}
