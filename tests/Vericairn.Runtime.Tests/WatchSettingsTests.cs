// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using System.Text.Json.Nodes;
using Vericairn.Protocol;
using Vericairn.Records.Estate;
using Vericairn.Records.Model;
using Vericairn.Runtime.Workers;

namespace Vericairn.Runtime.Tests;

[Collection("home")]
public class WatchSettingsTests
{
    [Fact]
    public void GoalFieldsOverrideRosterFieldsAndBothKeepTheLaunchersRemainingDefaults()
    {
        using var run = new RunEstate();
        string roster = Roster(run, """{"stallSeconds":30,"watch":{"normalTokensPerMinute":40000,"loopWarning":3,"loopStop":8}}""");
        Goal(run, """{"stallSeconds":45,"watch":{"loopStop":4,"loopWindow":12}}""", roster);
        RunResponse started = run.Start("codex", "watch-codex-loop", run.Report("settings"), settings: new Dictionary<string, string>(StringComparer.Ordinal) { ["stall-seconds"] = "5" });
        Assert.True(started.Ok, started.Message);
        RunRow settled = run.Settled(started.Run);
        Assert.Equal([3, 4], settled.Signals!.Where(s => s.Code == "loop").Select(s => s.Count));
        AgentRuns.Run recorded = AgentRuns.Find(run.Estate.Load(), started.Run)!;
        JsonElement watch = RunEstate.Json(recorded.Target("watch")!);
        Assert.Equal(40000, watch.GetProperty("normalTokensPerMinute").GetInt64());
        Assert.Equal(4, watch.GetProperty("burnMultiplier").GetInt32());
        Assert.Equal(12, watch.GetProperty("loopWindow").GetInt32());
        Assert.Equal("45", recorded.Target("stallSeconds"));
        Assert.Equal(roster, recorded.Target("watchRoster"));
        string pinned = recorded.Target("watch")!;
        Roster(run, """{"watch":{"normalTokensPerMinute":50000}}""", roster);
        Goal(run, """{"watch":{"loopWarning":6}}""");
        Assert.Equal(pinned, AgentRuns.Find(run.Estate.Load(), started.Run)!.Target("watch"));
        Assert.Equal(0, run.Service.Check().Errors);
    }

    [Fact]
    public void TheEstatesCurrentRosterAppliesEvenWhenTheGoalOverridesItsRoles()
    {
        var clock = new WorkerClock();
        using var run = new RunEstate(options: new WorkerOptions { Clock = clock, Heartbeat = TimeSpan.FromMilliseconds(20), Grace = TimeSpan.FromSeconds(1) });
        string first = Roster(run, """{"stallSeconds":10,"watch":{"normalTokensPerMinute":1000}}""");
        string current = Roster(run, """{"stallSeconds":20,"watch":{"normalTokensPerMinute":2000}}""", first);
        Goal(run, "{}", roles: true);
        RunResponse started = run.Start("generic", "silent-ignore", run.Report("roster-stall"), settings: new Dictionary<string, string>(StringComparer.Ordinal) { ["stall-seconds"] = "2" });
        Assert.True(started.Ok, started.Message);
        WaitUntil(() => File.Exists(run.Report("roster-stall")));
        AgentRuns.Run recorded = AgentRuns.Find(run.Estate.Load(), started.Run)!;
        Assert.Equal("20", recorded.Target("stallSeconds"));
        Assert.Equal(current, recorded.Target("watchRoster"));
        clock.Advance(TimeSpan.FromSeconds(40));
        WaitUntil(() => run.Row(started.Run).Stop?.TransmittedAt is not null);
        clock.Advance(TimeSpan.FromSeconds(1));
        Assert.NotNull(run.Settled(started.Run).Stop!.ContainedAt);
        Assert.Equal(0, run.Service.Check().Errors);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void IncompatibleMergedLoopLimitsRefuseBeforeAnIntentOrHarnessIsStarted(bool duringPreparation)
    {
        using var run = new RunEstate();
        Roster(run, """{"watch":{"loopWarning":8}}""");
        if (duringPreparation)
        {
            HookedLauncher.Install(run, verify: () => { Goal(run, """{"watch":{"loopStop":4}}"""); return null; });
        }
        else
        {
            Goal(run, """{"watch":{"loopStop":4}}""");
        }

        RunResponse refused = run.Start("claude-code", "claude", run.Report("invalid"));
        Assert.False(refused.Ok);
        Assert.Contains("loopWarning < loopStop <= loopWindow", refused.Message, StringComparison.Ordinal);
        Assert.Empty(run.Service.Runs().Runs);
        Assert.False(File.Exists(run.Report("invalid")));
        Assert.Equal(0, run.Service.Check().Errors);
    }

    [Fact]
    public void TheIntentUsesTheCurrentRosterAndGoalAfterPreparation()
    {
        using var run = new RunEstate();
        string first = Roster(run, """{"watch":{"normalTokensPerMinute":1000}}""");
        string? revision = null;
        HookedLauncher.Install(run, verify: () =>
        {
            revision = Roster(run, """{"watch":{"normalTokensPerMinute":2000,"burnMultiplier":6}}""", first);
            Goal(run, """{"watch":{"burnMultiplier":7}}""", first);
            return null;
        });
        RunResponse started = run.Start("claude-code", "claude", run.Report("latest"));
        Assert.True(started.Ok, started.Message);
        run.Settled(started.Run);
        AgentRuns.Run recorded = AgentRuns.Find(run.Estate.Load(), started.Run)!;
        Assert.Equal(revision, recorded.Target("watchRoster"));
        JsonElement watch = RunEstate.Json(recorded.Target("watch")!);
        Assert.Equal(2000, watch.GetProperty("normalTokensPerMinute").GetInt64());
        Assert.Equal(7, watch.GetProperty("burnMultiplier").GetInt32());
        Assert.Equal(0, run.Service.Check().Errors);
    }

    private static string Roster(RunEstate run, string limits, string? supersedes = null) =>
        RunEstate.Ok(run.Service.NewRecord(new NewRecordRequest("roster", run.Estate.OperatorId,
            RunEstate.Json($$"""{"roles":[{"role":"builder","principals":["{{run.Estate.BuilderId}}"]}],"limits":{{limits}}}"""), Supersedes: supersedes))).Id;

    private static void Goal(RunEstate run, string limits, string? roster = null, bool roles = false)
    {
        EstateModel model = run.Estate.Load();
        RecordEnvelope current = model.Latest(model.ById[run.Goal]);
        var body = JsonNode.Parse(current.Body.GetRawText())!.AsObject();
        body["limits"] = JsonNode.Parse(limits);
        if (roster is not null)
        {
            body["roster"] = roster;
        }

        if (roles)
        {
            body["roles"] = JsonNode.Parse($$"""[{"role":"builder","principals":["{{run.Estate.BuilderId}}"]}]""");
        }

        string revised = RunEstate.Ok(run.Service.GoalPropose(new GoalProposeRequest(run.Estate.OperatorId, RunEstate.Json(body.ToJsonString()), current.Id))).Id;
        RunEstate.Ok(run.Service.GoalActivate(revised, new GoalActionRequest(run.Estate.OperatorId)));
    }

    private static void WaitUntil(Func<bool> condition) => Assert.True(SpinWait.SpinUntil(condition, TimeSpan.FromSeconds(20)), "The worker observation did not arrive.");
}
