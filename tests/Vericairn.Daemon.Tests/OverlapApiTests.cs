// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using Vericairn.Protocol;
using Vericairn.Records.Canonical;
using Vericairn.Records.Tests;
using Vericairn.Sdk;

namespace Vericairn.Daemon.Tests;

/// <summary>From 0.9.4 (CODE-73): the overlap check's view and a draft brief's preview through the SDK against a running daemon, as the embedded runtime gives them.</summary>
[Collection("daemon")]
public class OverlapApiTests
{
    private const string Brief = """{"outcome":"The guide.","sources":[],"permittedChanges":["path:docs/**"],"prohibitedEffects":["no publication"],"workspace":["the estate, a work branch"],"limits":[],"expectedOutputs":["guide"],"reviewCriteria":["it reads"],"recovery":"Record a result."}""";

    public OverlapApiTests()
    {
        DaemonTestHome.Ensure();
    }

    [Fact]
    public async Task TheViewAndThePreviewReadTheSameOverlapsAsTheRuntimeAndRecordNothing()
    {
        using var estate = new TestEstate(recordFormat: 3);
        await using DaemonHost host = await DaemonTestHome.StartAsync(estate.Root, cancellationToken: TestContext.Current.CancellationToken);
        using var api = new VericairnClient(DaemonHost.Discover(estate.Root)!);
        string op = estate.OperatorId;
        foreach (string key in new[] { "W-1", "W-2" })
        {
            Ok(api.NewRecord(new NewRecordRequest("work-order", op, Json($$"""{"key":"{{key}}","kind":"ordinary","title":"Guide {{key}}","intent":"Write it.","scope":["the guide"],"exitQuestion":"Written?","inputs":[],"deliverables":["guide"]}"""))));
        }

        Assert.Null(api.Assign(new AssignRequest("W-1", estate.BuilderId, op, Json(Brief))).Notices);
        WriteOutcome second = api.Assign(new AssignRequest("W-2", estate.BuilderId, op, Json(Brief)) { Repository = "estate" });
        Assert.StartsWith("Overlap: W-1's assignment", Assert.Single(second.Notices!), StringComparison.Ordinal);
        int records = estate.Load().Records.Count;

        OverlapResponse view = api.WorkOverlap();
        Assert.Equal(CanonicalJson.SerializePretty(host.Service.WorkOverlap().Json), CanonicalJson.SerializePretty(view.Json));
        Assert.Single(view.Json.GetProperty("overlaps").EnumerateArray());
        Assert.Single((await api.WorkOverlapAsync("estate", TestContext.Current.CancellationToken)).Json.GetProperty("overlaps").EnumerateArray());
        Assert.True(api.WorkOverlap("01M4TRNE1GYEK4P8MXQAB3C8Q4").Json.TryGetProperty("problem", out _));
        OverlapResponse preview = api.WorkOverlapPreview(new OverlapPreviewRequest(Json(Brief), "estate"));
        Assert.Equal(2, preview.Json.GetProperty("overlaps").GetArrayLength());
        Assert.Equal(records, estate.Load().Records.Count);
    }

    private static RecordWritten Ok(WriteOutcome outcome)
    {
        Assert.True(outcome.Ok, outcome.Problem?.Message);
        return outcome.Written!;
    }

    private static JsonElement Json(string text)
    {
        using JsonDocument document = JsonDocument.Parse(text);
        return document.RootElement.Clone();
    }
}
