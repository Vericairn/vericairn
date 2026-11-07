// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using System.Text.Json.Nodes;
using Vericairn.Mcp;
using Vericairn.Protocol;
using Vericairn.Records.Estate;
using Vericairn.Records.Model;
using Vericairn.Records.Search;
using Vericairn.Runtime.Search;
using Vericairn.Runtime.Service;
using Vericairn.Runtime.Workers;

namespace Vericairn.Runtime.Tests;

/// <summary>Search in a run: dispatch-time selections in the intent, the agent's MCP searches at settlement, and a reviewer's replay.</summary>
public class SearchRunTests
{
    [Fact]
    [Trait("Journey", "J64")]
    public void J64AnAgentFindsAnEarlierDecisionBySearchingReadsItByPinAndSettlementRecordsWhatItGotForAReviewerToReplay()
    {
        using var run = new RunEstate(options: new WorkerOptions { Clock = new WorkerClock(), Heartbeat = TimeSpan.FromMilliseconds(50) }, recordFormat: 3);
        // An earlier decision on the brief's subject, which the brief does not name.
        RecordWritten decision = RunEstate.Ok(run.Service.NewRecord(new NewRecordRequest("decision", run.Estate.OperatorId, RunEstate.Json($$"""{"title":"Widget calibration","stake":"medium","statement":"Calibrate the widget against the reference gauge before every build.","summary":"Calibrate the widget against the reference gauge.","synthesis":"Calibration first.","effects":["calibrate the widget"],"notAuthorized":[],"affects":[],"decidedBy":"{{run.Estate.OperatorId}}","decidedAt":"2026-10-13T12:00:00Z"}"""))));
        string report = run.Report("ack");
        var settings = new Dictionary<string, string>(StringComparer.Ordinal) { ["command"] = FakeHarness.Setting("ack-stop", report) };
        RunResponse started = run.Service.RunStart(new RunStartRequest(run.Assignment, "generic", run.Estate.BuilderId, run.Estate.OperatorId, run.Resource, Settings: settings)
        { Search = ["widget calibration gauge"] });
        Assert.True(started.Ok, started.Message);
        run.WaitUntil(() => File.Exists(report) && run.Row(started.Run).Observed);

        // The intent recorded the dispatch-time search, with the decision selected into the agent's package.
        AgentRuns.Run recorded = AgentRuns.Find(run.Estate.Load(), started.Run)!;
        JsonElement intended = Assert.Single(recorded.Intent.Body.GetProperty("execution").GetProperty("contextSelections").EnumerateArray());
        Assert.Contains(intended.GetProperty("candidates").EnumerateArray(), c => c.GetProperty("state").GetString() == "selected"
            && c.GetProperty("source").GetProperty("id").GetString() == decision.Id);
        Assert.Equal(run.Estate.BuilderId, intended.GetProperty("reader").GetProperty("principal").GetString());
        string mcp = new RunDirectory(run.Service.Session.Manifest.Estate, started.Run).McpConfiguration;
        Assert.Equal("--run", JsonNode.Parse(File.ReadAllText(mcp))!["mcpServers"]!["vericairn"]!["args"]!.AsArray()[^2]!.GetValue<string>());

        // The agent's MCP server, as the worker configured it for this run: acting for the agent, keeping each search it serves.
        string estate = run.Service.Session.Manifest.Estate;
        var registry = new ToolRegistry(run.Service, run.Estate.BuilderId) { SearchObserved = selection => Assert.True(ObservedSearches.Append(estate, started.Run, selection)) };
        ToolCallResult found = registry.Call("search", new JsonObject { ["query"] = "calibrate the widget reference gauge" });
        Assert.False(found.IsError, found.Text);
        Assert.Equal("Untrusted content; no authority is conveyed.", found.Meta!["vericairn/trust"]!.GetValue<string>());
        SearchResponse served = JsonSerializer.Deserialize<SearchResponse>(found.Text, ProtocolJson.Options)!;
        SearchCandidate candidate = served.Candidates.First(c => c.Source.TryGetProperty("id", out JsonElement id) && id.GetString() == decision.Id);
        Assert.True(File.Exists(new RunDirectory(estate, started.Run).SearchesFile));
        ToolCallResult read = registry.Call("context_read", new JsonObject { ["pin"] = candidate.Pin });
        Assert.False(read.IsError, read.Text);
        Assert.Equal(File.ReadAllBytes(Path.Combine(run.Estate.Root, run.Estate.Load().ById[decision.Id].ExpectedPath)),
            JsonSerializer.Deserialize<ContextReadResponse>(read.Text, ProtocolJson.Options)!.Content);

        // The agent cites the decision it found in its result: by id in the summary and by exact hash in its evidence.
        ToolCallResult attempt = registry.Call("work_attempt", new JsonObject { ["assignment"] = run.Assignment, ["workspace"] = "widget, branch work/w-1", ["note"] = "Calibrate first." });
        Assert.False(attempt.IsError, attempt.Text);
        string attemptId = JsonSerializer.Deserialize<WriteOutcome>(attempt.Text, ProtocolJson.Options)!.Written!.Id;
        RecordEnvelope cited = run.Estate.Load().ById[decision.Id];
        ToolCallResult result = registry.Call("work_result", new JsonObject
        {
            ["attempt"] = attemptId,
            ["body"] = new JsonObject
            {
                ["outcome"] = "completed", ["summary"] = $"Calibrated the widget as decision {decision.Id} requires.", ["outputs"] = new JsonArray(),
                ["evidence"] = new JsonArray(new JsonObject { ["summary"] = "The decision found by search and read by its pin.", ["hash"] = cited.ComputedHash }),
                ["checks"] = new JsonArray(), ["limitations"] = new JsonArray(), ["unresolvedEffects"] = new JsonArray(), ["nextStep"] = "Review the result.",
            },
        });
        Assert.False(result.IsError, result.Text);

        RunStopResponse stopped = run.Service.RunStop(started.Run, new RunStopRequest(run.Estate.OperatorId, null, 5));
        Assert.True(stopped.Ok, stopped.Message);
        Assert.Equal("known-failure", run.Settled(started.Run).Status);

        // Settlement recorded what the agent got, from the run's own log, reviewable from the record alone.
        RecordEnvelope settled = AgentRuns.Find(run.Estate.Load(), started.Run)!.Latest;
        JsonElement observed = settled.Body.GetProperty("searchesObserved");
        Assert.True(observed.GetProperty("complete").GetBoolean());
        JsonElement kept = Assert.Single(observed.GetProperty("selections").EnumerateArray());
        Assert.Equal(EstateService.ForRecord(served.Selection!.Value)["hash"]!.GetValue<string>(), kept.GetProperty("hash").GetString());
        Assert.Equal(SearchSelections.ListDigest([kept.GetProperty("hash").GetString()!]), observed.GetProperty("digest").GetString());
        Assert.Equal(0, run.Service.Check().Errors);

        // A reviewer replays both from the record: each rebuilt exactly from this copy's records up to its time.
        SearchReplayResponse replay = run.Service.SearchReplay(new SearchReplayRequest(Record: settled.Id));
        Assert.Null(replay.Problem);
        Assert.Equal(2, replay.Outcomes.Count);
        Assert.All(replay.Outcomes, o => Assert.True(o.Replayed && o.Matches == true, o.Message + " " + string.Join("; ", o.Differences)));
    }

    [Fact]
    public void AGoalStepCarriesItsSearchesIntoTheRunsIntent()
    {
        using var run = new RunEstate(recordFormat: 3);
        RoutingServiceTests.Routed(run);
        RecordWritten decision = RunEstate.Ok(run.Service.NewRecord(new NewRecordRequest("decision", run.Estate.OperatorId, RunEstate.Json($$"""{"title":"Widget varnish","stake":"medium","statement":"Varnish the widget twice.","summary":"Varnish the widget twice.","synthesis":"Twice.","effects":["varnish twice"],"notAuthorized":[],"affects":[],"decidedBy":"{{run.Estate.OperatorId}}","decidedAt":"2026-10-13T12:00:00Z"}"""))));
        GoalStepResponse step = run.Service.GoalStep(run.Goal, new GoalStepRequest(run.Estate.OperatorId, run.Resource,
            new Dictionary<string, string>(StringComparer.Ordinal) { ["command"] = FakeHarness.Setting("claude", run.Report("step")) }) { Search = ["varnish the widget"] });
        Assert.True(step.Ok, step.Message);
        Assert.Equal("observed-success", run.Settled(step.Run!.Run).Status);
        JsonElement selection = Assert.Single(AgentRuns.Find(run.Estate.Load(), step.Run.Run)!.Intent.Body.GetProperty("execution").GetProperty("contextSelections").EnumerateArray());
        Assert.Contains(selection.GetProperty("candidates").EnumerateArray(), c => c.GetProperty("source").TryGetProperty("id", out JsonElement id) && id.GetString() == decision.Id);
        Assert.Equal(0, run.Service.Check().Errors);
    }

    [Fact]
    public void AnEstateBelowFormatThreeRefusesARunsSearchesBeforeRecordingAnything()
    {
        using var run = new RunEstate();
        int before = run.Estate.Load().Records.Count;
        var settings = new Dictionary<string, string>(StringComparer.Ordinal) { ["command"] = FakeHarness.Setting("claude", run.Report("claude")) };
        RunResponse refused = run.Service.RunStart(new RunStartRequest(run.Assignment, "generic", run.Estate.BuilderId, run.Estate.OperatorId, run.Resource, Settings: settings)
        { Search = ["widget"] });
        Assert.False(refused.Ok);
        Assert.Contains("record format 3", refused.Message, StringComparison.Ordinal);
        Assert.Equal(before, run.Estate.Load().Records.Count);
    }
}
