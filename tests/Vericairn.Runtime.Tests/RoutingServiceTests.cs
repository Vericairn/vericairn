// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using Vericairn.Packs.Software;
using Vericairn.Protocol;
using Vericairn.Records.Signing;
using Vericairn.Runtime.Providers;

namespace Vericairn.Runtime.Tests;

/// <summary>
/// The next-eligible view routed from this machine (the estate's design/goals.md §§ 6 and 7): the launchers the runtime holds, the
/// device keys its stores hold and the custody guard decide where each eligible step goes from here, and reading the view records and
/// starts nothing.
/// </summary>
[Collection("home")]
public class RoutingServiceTests
{
    [Fact]
    public void TheNextViewRoutesFromWhatThisMachineOffersAndRecordsNothing()
    {
        using var run = new RunEstate(keepOperatorKey: true);
        Routed(run);
        int records = Directory.EnumerateFiles(Path.Combine(run.Estate.Root, "records"), "*.json", SearchOption.AllDirectories).Count();

        // W-1 stays with the builder, whose assignment holds it; W-2 reaches the builder through Claude Code's launcher.
        NextResponse next = run.Service.GoalNext(run.Goal)!;
        JsonElement held = Step(next, "W-1").GetProperty("route");
        Assert.Equal(("agent", run.Estate.BuilderId, "claude-code", run.Assignment), (held.GetProperty("state").GetString(), held.GetProperty("principal").GetString(), held.GetProperty("launcher").GetString(), Step(next, "W-1").GetProperty("assignment").GetProperty("id").GetString()));
        JsonElement fresh = Step(next, "W-2").GetProperty("route");
        Assert.Equal(("agent", "claude-code"), (fresh.GetProperty("state").GetString(), fresh.GetProperty("launcher").GetString()));
        Assert.True(fresh.GetProperty("budget").GetProperty("allows").GetBoolean());
        Assert.Equal("W-1", next.Json.GetProperty("next").GetString());

        // The operator's key is held here without presence: the custody guard says so on the agent's route, which it does not change.
        string custody = Assert.Single(fresh.GetProperty("custody").GetProperty("refusals").EnumerateArray()).GetString()!;
        Assert.StartsWith($"Device {run.OperatorDevice} (Operator's laptop, admitted) of Test Operator ({run.Estate.OperatorId}), an operator", custody, StringComparison.Ordinal);
        Assert.Contains("custody here: an unattended run is refused: Device ", next.Text, StringComparison.Ordinal);
        new FileKeyStore(VericairnHome.Path()).Remove(run.OperatorDevice);
        Assert.Empty(Step(run.Service.GoalNext(run.Goal)!, "W-2").GetProperty("route").GetProperty("custody").GetProperty("refusals").EnumerateArray());

        // Without Claude Code's launcher here the builder is not available, and the sponsor is asked about each step.
        ProviderRegistry launchers = run.Service.Providers;
        var codexOnly = new ProviderRegistry();
        codexOnly.Register(new CodexLauncher());
        run.Service.Providers = codexOnly;
        JsonElement unavailable = Step(run.Service.GoalNext(run.Goal)!, "W-2").GetProperty("route");
        Assert.Equal(("unavailable", run.Estate.OperatorId), (unavailable.GetProperty("state").GetString(), unavailable.GetProperty("principal").GetString()));
        Assert.Contains("no launcher for its harness, Claude Code, is available on this machine (codex for Codex)", unavailable.GetProperty("candidates")[0].GetProperty("reasons")[0].GetString(), StringComparison.Ordinal);
        run.Service.Providers = launchers;

        // Without the builder's key here it is not available either, and the step it holds stays with it: the sponsor is asked.
        new FileKeyStore(VericairnHome.Path()).Remove(run.BuilderDevice);
        NextResponse keyless = run.Service.GoalNext(run.Goal)!;
        Assert.Equal("unavailable", Step(keyless, "W-1").GetProperty("route").GetProperty("state").GetString());
        Assert.Contains("cannot go on from here (no key of its admitted devices is held on this machine", Step(keyless, "W-1").GetProperty("route").GetProperty("reason").GetString(), StringComparison.Ordinal);
        Assert.Equal("unavailable", Step(keyless, "W-2").GetProperty("route").GetProperty("state").GetString());

        Assert.Null(run.Service.GoalNext("01M4TRNE1GYEK4P8MXQAB3C8Q4"));
        Assert.Equal(records, Directory.EnumerateFiles(Path.Combine(run.Estate.Root, "records"), "*.json", SearchOption.AllDirectories).Count());
        Assert.Empty(run.Service.Records("operation"));
    }

    /// <summary>The builder named as Claude Code, a roster naming it for the builder role, and a second step of the goal, W-2.</summary>
    internal static void Routed(RunEstate run)
    {
        string op = run.Estate.OperatorId;
        RunEstate.Ok(run.Service.NewRecord(new NewRecordRequest("principal", op, RunEstate.Json("""{"kind":"agent","name":"Test Builder","roles":["builder"],"agent":{"vendor":"Anthropic","product":"Claude Code"}}"""), Supersedes: run.Estate.BuilderId)));
        RunEstate.Ok(run.Service.NewRecord(new NewRecordRequest("roster", op, RunEstate.Json($$"""{"roles":[{"role":"builder","principals":["{{run.Estate.BuilderId}}"]},{"role":"publisher","principals":["{{op}}"]},{"role":"decider","principals":["{{op}}"]}]}"""))));
        RunEstate.Ok(run.Service.NewRecord(new NewRecordRequest("work-order", op, RunEstate.Json($$"""{"key":"W-2","kind":"ordinary","title":"Document the widget","intent":"Document it.","scope":["widget guide"],"exitQuestion":"Does the guide describe the widget?","inputs":[{"type":"goal","id":"{{run.Goal}}"}],"deliverables":["guide"]}"""))));
    }

    private static JsonElement Step(NextResponse next, string key) =>
        Assert.Single(next.Json.GetProperty("steps").EnumerateArray(), s => s.GetProperty("key").GetString() == key);
}
