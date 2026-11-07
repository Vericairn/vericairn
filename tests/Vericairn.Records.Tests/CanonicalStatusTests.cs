// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using System.Text.Json.Nodes;
using Vericairn.Records.Estate;
using Vericairn.Records.Model;

namespace Vericairn.Records.Tests;

/// <summary>
/// Canonical status reads no clock (the estate's design/cohesion-contract.md § 4): an estate without runs keeps the exact bytes main
/// rendered before runs were shown, and so the statusHash a person's observation pins; a run's position, its stops and the delivery of
/// its operator requests are derived at the records' own time, the newest record's, whenever the document is read.
/// </summary>
public class CanonicalStatusTests
{
    [Theory]
    [InlineData("format-1-estate")]
    [InlineData("format-3-policy-estate")]
    public void AnEstateWithoutRunsKeepsTheCanonicalStatusBytesItHadBeforeRuns(string estate)
    {
        string repository = RecordCompanionTests.FindRepository();
        EstateModel model = EstateLoader.Load(Path.Combine(repository, "tests", "fixtures", estate));
        string expected = File.ReadAllText(Path.Combine(repository, "tests", "fixtures", "canonical-status", estate + ".json"));
        string rendered = EstateStatus.RenderJson(model, new FindingList());
        if (estate == "format-1-estate")
        {
            Assert.Equal(expected, rendered);
        }
        else
        {
            // This estate's one living document names a generator only tests install, process-wide, so its state depends on which
            // tests ran first; every other member keeps its exact value.
            JsonObject before = JsonNode.Parse(expected)!.AsObject(), now = JsonNode.Parse(rendered)!.AsObject();
            Assert.Equal(before.Select(p => p.Key), now.Select(p => p.Key));
            before.Remove("documents");
            now.Remove("documents");
            Assert.True(JsonNode.DeepEquals(before, now), rendered);
        }

        using JsonDocument status = JsonDocument.Parse(rendered);
        Assert.False(status.RootElement.TryGetProperty("runs", out _));
        Assert.False(status.RootElement.GetProperty("operatorRequests").TryGetProperty("withdrawn", out _));
    }

    [Fact]
    public void ARunsCanonicalStatusIsDerivedAtTheRecordsOwnTimeNotWhenItIsRead()
    {
        // Publication falls due a day after acceptance: a live read before then shows it blocked, and one after it stopped for the operator.
        JsonObject song = FlowFixture.Song();
        song["steps"]!.AsArray().Select(s => s!.AsObject()).Single(s => s["key"]!.GetValue<string>() == "publish")["when"] =
            new JsonObject { ["due"] = new JsonObject { ["after"] = "P1D", ["from"] = "accept" } };
        using var run = new FlowFixture(song, new JsonObject { ["step.review-lyrics"] = "off", ["step.shorts"] = "off" });
        RecordEnvelope lyrics = run.Result(run.Assign("draft", run.Estate.BuilderId), "completed", ("lyrics", FlowFixture.Hash('a'), "text/markdown"));
        run.Review(run.Result(run.Assign("master", run.Estate.BuilderId, inputs: new JsonArray(FlowFixture.PinOf(lyrics, "lyrics"))), "completed", ("master", FlowFixture.Hash('b'), null)));
        RecordEnvelope accepted = run.Marked("ACCEPTED");
        EstateModel model = run.Estate.Load();
        Assert.Equal(accepted.AtTime, FlowStops.RecordedTime(model));
        Assert.Equal("blocked", Publish(FlowPosition.Derive(model, run.Goal, accepted.AtTime.AddHours(1))).State);
        Assert.Equal("stopped", Publish(FlowPosition.Derive(model, run.Goal, accepted.AtTime.AddDays(2))).State);

        // Canonical status gives the run as of its newest record, the same document at every reading.
        string first = EstateStatus.RenderJson(model, new FindingList());
        Assert.Equal(first, EstateStatus.RenderJson(run.Estate.Load(), new FindingList()));
        using JsonDocument status = JsonDocument.Parse(first);
        JsonElement shown = Assert.Single(status.RootElement.GetProperty("runs").EnumerateArray());
        Assert.Equal(RecordParser.FormatTimestamp(accepted.AtTime), shown.GetProperty("asOf").GetString());
        Assert.Empty(shown.GetProperty("stops").EnumerateArray());

        // A record written once publication is due moves the records' own time, and canonical status then shows the stop.
        run.Estate.SetClock(accepted.AtTime.AddDays(2));
        run.Marked("LATER");
        using JsonDocument later = JsonDocument.Parse(EstateStatus.RenderJson(run.Estate.Load(), new FindingList()));
        JsonElement stop = Assert.Single(Assert.Single(later.RootElement.GetProperty("runs").EnumerateArray()).GetProperty("stops").EnumerateArray());
        Assert.Equal(("reserved", "publish", "due"), (stop.GetProperty("kind").GetString(), stop.GetProperty("name").GetString(), stop.GetProperty("delivery").GetString()));
    }

    private static FlowPosition.StepInstance Publish(FlowPosition.Position position) => Assert.Single(position.Steps, s => s.Name == "publish");
}
