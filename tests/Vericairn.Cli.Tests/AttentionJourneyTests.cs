// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text;
using System.Text.Json;
using Vericairn.Mcp;
using Vericairn.Protocol;
using Vericairn.Records.Model;
using Vericairn.Runtime.Service;

namespace Vericairn.Cli.Tests;

[Collection("cli")]
public class AttentionJourneyTests
{
    [Fact]
    public void LongResolvedHistoryKeepsStatusSmallAndEverySurfaceKeepsExactOpenReferences()
    {
        using var estate = new HandoffEstate();
        RecordEnvelope order = estate.Add("work-order", estate.OperatorId, """{"key":"HISTORY","kind":"ordinary","title":"Resolved history","intent":"Keep attention current.","scope":["tests"],"exitQuestion":"Current?","inputs":[],"deliverables":["evidence"]}""");
        RecordEnvelope assignment = estate.Add("assignment", estate.OperatorId, $$$"""{"contributor":"{{{estate.BuilderId}}}","brief":{"outcome":"Check the history.","sources":[],"permittedChanges":["tests"],"prohibitedEffects":["no publication"],"workspace":["local clone"],"limits":[],"expectedOutputs":["result"],"reviewCriteria":["current attention"],"recovery":"Inspect the records."}}""", subject: order.Reference);
        RecordEnvelope? original = null;
        for (int i = 0; i < 200; i++)
        {
            RecordEnvelope attempt = estate.Add("attempt", estate.BuilderId, """{"startedAt":"2026-10-13T12:00:00Z","workspace":"local clone","note":"Check."}""", subject: assignment.Reference);
            RecordEnvelope result = estate.Add("result", estate.BuilderId, $$"""{"outcome":"partial","summary":"{{new string('x', 7000)}}","outputs":[],"evidence":[],"checks":[{"name":"validation","outcome":"{{(i % 2 == 0 ? "failed" : "not-performed")}}","summary":"Retained original check."}],"limitations":[],"unresolvedEffects":["Retained original effect."],"nextStep":"Inspect."}""", subject: attempt.Reference);
            estate.Add("review", estate.OperatorId, """{"independence":"operator","outcome":"findings","summary":"Inspect the result.","findings":[{"severity":"error","summary":"Retained original finding."}]}""", subject: result.Reference);
            original ??= result;
        }
        estate.Add("acceptance", estate.OperatorId, """{"statement":"Work accepted.","basis":[],"self":true}""", subject: order.Reference);
        RecordEnvelope repository = estate.Add("resource", estate.OperatorId, """{"kind":"repository","name":"Example","role":"source","location":{"github":"Example/example"},"visibility":"private"}""");
        RecordEnvelope open = estate.Add("verification", estate.BuilderId, """{"method":"github-actions","commit":"0123456789abcdef0123456789abcdef01234567","environment":{"workflow":"ci.yml"},"role":"automation","outcome":"unknown","summary":"The check has not answered.","evidence":[],"limitations":[]}""", subject: repository.Reference);
        using EstateService api = EstateService.Open(estate.Root);
        FindingsResponse validation = api.Validate();
        Assert.True(validation.Errors == 0, string.Join("; ", validation.Findings.Select(f => f.Message)));
        int records = estate.Load().Records.Count;
        var mcp = new ToolRegistry(api, null);
        foreach (string command in new[] { "status", "progress" })
        {
            JsonElement json = CliHarness.Ok("--estate", estate.Root, "--json", command).Json;
            JsonElement attention = Assert.Single(json.GetProperty("attention").EnumerateArray());
            Assert.Equal(open.Id, attention.GetProperty("record").GetString());
            Assert.Equal(open.ComputedHash, attention.GetProperty("hash").GetString());
            Assert.Equal(repository.ComputedHash, attention.GetProperty("subject").GetProperty("hash").GetString());
            JsonElement canonical = command == "status" ? api.Status().Json : api.Progress().Json;
            Assert.True(JsonElement.DeepEquals(canonical, json));
            string tool = command == "status" ? mcp.Call("estate_status", new()).Text : mcp.Call("estate_progress", new() { ["form"] = "json" }).Text;
            Assert.True(JsonElement.DeepEquals(canonical, JsonSerializer.Deserialize<JsonElement>(tool)));
            foreach (string depth in CommunicationResolution.Depths)
            {
                string text = CliHarness.Ok("--estate", estate.Root, "--depth", depth, command).Out;
                Assert.Contains("unknown-check: verification " + open.Id, text, StringComparison.Ordinal);
                Assert.Contains(open.ComputedHash, text, StringComparison.Ordinal);
                Assert.DoesNotContain("Retained original", text, StringComparison.Ordinal);
                Assert.DoesNotContain(new string('x', 100), text, StringComparison.Ordinal);
                if (command == "status") { Assert.True(Encoding.UTF8.GetByteCount(text) < 6000, $"Status grew to {Encoding.UTF8.GetByteCount(text)} bytes."); }
                Assert.True(JsonElement.DeepEquals(json, CliHarness.Ok("--estate", estate.Root, "--depth", depth, "--json", command).Json));
            }
        }
        Assert.Equal(original!.Body.GetProperty("summary").GetString(), api.Record(original.Id)!.Envelope.GetProperty("body").GetProperty("summary").GetString());
        Assert.Contains("Retained original effect.", CliHarness.Ok("--estate", estate.Root, "records", "show", original.Id).Out, StringComparison.Ordinal);
        Assert.Equal(records, estate.Load().Records.Count);
    }
}
