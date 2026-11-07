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
