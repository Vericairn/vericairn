// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using Vericairn.Protocol;
using Vericairn.Records.Estate;
using Vericairn.Records.Signing;
using Vericairn.Runtime.Service;

namespace Vericairn.Runtime.Tests;

/// <summary>
/// From 0.9.4 (FIX-149): progress counts each release carrier's placed work from one index of the work orders by carrier, built once for
/// a judged model, never a scan of every work order for each carrier, and adds no judgment: counted, as <see cref="StatusPassCountTests"/>
/// counts status, on generated estates of 40 work orders placed in 4 carriers and 200 placed in 20, a progress read by a one-shot runtime
/// judges the estate once and reads the whole work orders as often on both, where a scan for each carrier would read them five times as
/// often on the larger.
/// </summary>
[Collection("status-performance")]
public sealed class ProgressPassCountTests
{
    [Fact]
    public void ProgressCountsEachCarriersPlacementsWithoutAJudgmentOrAReadThatGrowsWithTheEstate()
    {
        using var small = new GeneratedStatusEstate(40, goals: 4, carriers: 4);
        using var large = new GeneratedStatusEstate(200, goals: 4, carriers: 20);
        (long fewerJudgments, long fewerReads, JsonElement fewer) = Read(small.Root);
        (long moreJudgments, long moreReads, JsonElement more) = Read(large.Root);
        Assert.Equal((1L, 1L), (fewerJudgments, moreJudgments));
        Assert.Equal(fewerReads, moreReads);

        // Every carrier carries the ten work orders placed in it.
        foreach ((JsonElement report, int carriers) in new[] { (fewer, 4), (more, 20) })
        {
            JsonElement[] releases = [.. report.GetProperty("next").GetProperty("releases").EnumerateArray()];
            Assert.Equal(carriers, releases.Length);
            Assert.All(releases, r => Assert.Equal(10, r.GetProperty("carries").GetArrayLength()));
            Assert.Equal(report.GetProperty("workOrders").GetProperty("states").EnumerateArray().Where(s => s.GetProperty("state").GetString() == "accepted")
                .Sum(s => s.GetProperty("workOrders").EnumerateArray().Count(o => o.GetProperty("key").GetString()!.StartsWith("TASK-", StringComparison.Ordinal))),
                releases.Sum(r => r.GetProperty("accepted").GetInt32()));
        }
    }

    // A one-shot runtime's opening and progress, as `vericairn progress` runs without a daemon, counted in a context of its own.
    private static (long Judgments, long WorkOrderReads, JsonElement Report) Read(string root)
    {
        var judgments = new JudgmentContext(new SignatureTables());
        using EstateService service = EstateService.Open(root, null, null, oneShot: true, judgments: judgments);
        EstateModel? read = null;
        service.Session.Loaded = model => read = model;
        ProgressResponse progress = service.Progress();
        Assert.False(progress.HasErrors);
        Assert.NotNull(read);
        return (judgments.Judgments, read.TypeReads.GetValueOrDefault("work-order"), progress.Json.Clone());
    }
}
