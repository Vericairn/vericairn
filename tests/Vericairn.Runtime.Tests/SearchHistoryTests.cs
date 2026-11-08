// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using System.Text.Json.Nodes;
using Vericairn.Protocol;
using Vericairn.Records.Estate;
using Vericairn.Records.History;
using Vericairn.Records.Model;
using Vericairn.Records.Tests;
using Vericairn.Runtime.Service;

namespace Vericairn.Runtime.Tests;

/// <summary>Search and records the estate does not count (design/signed-history.md § 6).</summary>
public class SearchHistoryTests
{
    [Fact]
    public void AnUnanchoredUnsignedRecordAppearsInPlainSearchAndIsExcludedFromAPackagesSelectionWithItsReason()
    {
        using var records = new PolicyFixture();
        using EstateService api = EstateService.Open(records.Estate.Root);
        foreach (string principal in new[] { records.Estate.OperatorId, records.Estate.BuilderId })
        {
            WriteOutcome device = api.DeviceCreate(new(principal, "Runtime's device", "file"));
            Assert.True(device.Ok, device.Problem?.Message);
            RecordEnvelope held = api.Session.Load().ById[device.Written!.Id];
            records.Estate.SetClock(held.AtTime.AddSeconds(-1));
            records.Add("admission", new() { ["principal"] = principal, ["status"] = "admitted", ["note"] = "The runtime's device." }, subject: held.Reference);
        }

        records.Add("work-order", new JsonObject { ["key"] = "ZEB-1", ["kind"] = "ordinary", ["title"] = "Maintenance", ["intent"] = "Perform the declared checks.", ["scope"] = new JsonArray("test inventory"), ["exitQuestion"] = "Was the inventory checked?", ["inputs"] = new JsonArray(), ["deliverables"] = new JsonArray("report") });
        // Required signatures, and history checkpoints adopted over every unsigned record so far.
        records.Add("estate", new JsonObject
        {
            ["name"] = "Test Estate", ["slug"] = "test-estate", ["purpose"] = "Exercise the record format.", ["operator"] = records.Estate.OperatorId, ["recordFormat"] = 3,
            ["policies"] = new JsonObject { ["signatures"] = "required", ["releaseAcceptance"] = new JsonArray("operator") },
        }, supersedes: records.Estate.Estate.Id);
        CheckpointDraft draft = SignedHistory.Prepare(api.Session.Load(), "adoption", [.. api.Session.Load().Records.Where(r => r.Signatures.Count == 0)]);
        foreach (byte[] bytes in draft.Objects) api.Session.Objects.Put(bytes);
        WriteOutcome adopted = api.NewRecord(new NewRecordRequest("history-checkpoint", records.Estate.OperatorId, draft.Body));
        Assert.True(adopted.Ok, adopted.Problem?.Message);
        Assert.True(SignedHistory.Adopted(api.Session.Load()));

        // An unsigned decision written straight into the tree afterwards: unanchored, so the estate does not count it.
        RecordEnvelope forged = records.Estate.Add("decision", records.Estate.OperatorId, $$"""{"title":"Zebra calibration","stake":"medium","statement":"Calibrate the zebra gauge.","summary":"Calibrate the zebra gauge.","synthesis":"Zebra first.","effects":["calibrate the zebra gauge"],"notAuthorized":[],"affects":[],"decidedBy":"{{records.Estate.OperatorId}}","decidedAt":"2026-11-01T12:00:00Z"}""");
        EstateModel model = api.Session.Load();
        Assert.Empty(forged.Signatures);
        Assert.False(model.Counts(model.ById[forged.Id]));

        // Plain search returns it, as untrusted data whose standing is judged by reading it.
        SearchResponse plain = api.Search(new SearchRequest("zebra calibration"));
        Assert.Null(plain.Problem);
        Assert.Contains(plain.Candidates, c => c.Source.TryGetProperty("id", out JsonElement id) && id.GetString() == forged.Id && c.State == "selected");

        // Preparing a package refuses while such a record stands, as it refuses any invalid record; and the selection a package's
        // searches record never selects it, but excludes it with its reason.
        ContextResponse package = api.Context(new ContextRequest("ZEB-1", null, ["zebra calibration"]));
        Assert.Equal("incomplete-context", package.Problem!.Code);
        JsonNode selection = Assert.Single(api.SearchSelectionsForTests(["zebra calibration"])!)!;
        JsonNode excluded = Assert.Single(selection["candidates"]!.AsArray(), c => c!["source"]!["id"]?.GetValue<string>() == forged.Id)!;
        Assert.Equal("excluded", excluded["state"]!.GetValue<string>());
        Assert.Contains("does not count this record", excluded["reason"]!.GetValue<string>(), StringComparison.Ordinal);
        var findings = new FindingList();
        Records.Search.SearchSelections.Validate(new Records.Types.BodyReader(JsonSerializer.SerializeToElement(selection), "selection", findings, 3));
        Assert.False(findings.HasErrors, string.Join("; ", findings.Select(f => f.Message)));
    }
}
