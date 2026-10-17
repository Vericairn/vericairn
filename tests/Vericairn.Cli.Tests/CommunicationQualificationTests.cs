// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Vericairn.Mcp;
using Vericairn.Protocol;
using Vericairn.Records.Canonical;
using Vericairn.Records.Hashing;
using Vericairn.Runtime.Service;

namespace Vericairn.Cli.Tests;

[Collection("cli")]
public class CommunicationQualificationTests
{
    private const string AssignmentBody = """{"outcome":"Qualify the communication journey.","sources":[],"permittedChanges":["tests"],"prohibitedEffects":["no publication"],"workspace":["local clone"],"limits":[],"expectedOutputs":["test results"],"reviewCriteria":["journeys pass"],"recovery":"Read the recorded result before continuing."}""";

    [Fact]
    public void D08A_TwoRecipientsResolveTheSameProfileThroughCliMcpAndExportedBriefs()
    {
        using var estate = new HandoffEstate();
        string other = estate.Add("principal", estate.OperatorId, """{"kind":"human","name":"Reviewer","roles":["contributor"]}""").Id;
        estate.Add("work-order", estate.OperatorId, """{"key":"COMMS-J","kind":"ordinary","title":"Communication journey","intent":"Qualify all surfaces.","scope":["tests"],"exitQuestion":"Consistent?","inputs":[],"deliverables":["results"]}""");
        CliHarness.Ok("--estate", estate.Root, "communication", "set", "--as", estate.OperatorId, "--status", "short", "--decisions", "comprehensive", "--handoffs", "short");
        CliHarness.Ok("--estate", estate.Root, "communication", "set", "--as", other, "--status", "comprehensive", "--decisions", "standard", "--handoffs", "comprehensive");
        using EstateService api = EstateService.Open(estate.Root);
        var mcp = new ToolRegistry(api, null);
        int records = estate.Load().Records.Count;
        foreach ((string recipient, string depth, string decision) in new[] { (estate.OperatorId, "short", "comprehensive"), (other, "comprehensive", "standard") })
        {
            CommunicationProfile expected = api.Communication(recipient);
            CommunicationProfile cli = JsonSerializer.Deserialize<CommunicationProfile>(CliHarness.Ok("--estate", estate.Root, "--recipient", recipient, "--json", "communication", "show").Out, ProtocolJson.Options)!;
            ToolCallResult read = mcp.Call("communication_get", new JsonObject { ["recipient"] = recipient });
            Assert.False(read.IsError);
            CommunicationProfile tool = JsonSerializer.Deserialize<CommunicationProfile>(read.Text, ProtocolJson.Options)!;
            foreach (CommunicationProfile profile in new[] { expected, cli, tool })
            {
                Assert.Equal(depth, profile.Fields["status"].Value);
                Assert.Equal(decision, profile.Fields["decision"].Value);
                Assert.Equal("person", profile.Fields["status"].Source);
                Assert.Equal(expected.RevisionHash, profile.RevisionHash);
                Assert.Equal(expected.Revisions.Single().Hash, profile.Revisions.Single().Hash);
                Assert.Equal(recipient, profile.Revisions.Single().By);
            }

            string brief = CliHarness.Ok("--estate", estate.Root, "--recipient", recipient, "work", "brief", "COMMS-J").Out;
            ToolCallResult exported = mcp.Call("work_brief", new JsonObject { ["workOrder"] = "COMMS-J", ["recipient"] = recipient });
            Assert.False(exported.IsError);
            foreach (string text in new[] { brief, exported.Text, api.BriefContext("COMMS-J", recipient: recipient)!.Markdown })
            {
                Assert.Contains($"handoff ({depth})", text, StringComparison.Ordinal);
                Assert.Contains($"decision: {decision} ← person", text, StringComparison.Ordinal);
                Assert.Contains(expected.Revisions.Single().Hash!, text, StringComparison.Ordinal);
                Assert.Contains("Qualify all surfaces.", text, StringComparison.Ordinal);
            }
        }

        Assert.Equal(records, estate.Load().Records.Count);
        Assert.Equal("short", api.Communication(estate.OperatorId).Fields["status"].Value);
    }

    [Fact]
    public void D08A_AssignmentAndViewOriginsAreVisibleWithoutChangingCanonicalPayloads()
    {
        using var estate = new HandoffEstate();
        estate.Add("work-order", estate.OperatorId, """{"key":"COMMS-J","kind":"ordinary","title":"Communication journey","intent":"Keep exact originals.","scope":["tests"],"exitQuestion":"Consistent?","inputs":[],"deliverables":["results"]}""");
        using EstateService api = EstateService.Open(estate.Root);
        string assignment = Ok(api.Assign(new("COMMS-J", estate.BuilderId, estate.OperatorId, Json(AssignmentBody))));
        CliHarness.Ok("--estate", estate.Root, "communication", "set", "--as", estate.OperatorId, "--status", "short", "--handoffs", "comprehensive");
        CliHarness.Ok("--estate", estate.Root, "--for-assignment", assignment, "communication", "set", "--as", estate.OperatorId, "--status", "standard", "--handoffs", "short");
        CommunicationProfile scoped = api.Communication(estate.OperatorId, assignment);
        Assert.Equal("assignment", scoped.Fields["status"].Source);
        Assert.Equal("standard", scoped.Fields["status"].Value);
        var mcp = new ToolRegistry(api, null);
        string read = mcp.Call("communication_get", new JsonObject { ["recipient"] = estate.OperatorId, ["assignment"] = assignment }).Text;
        Assert.Contains(scoped.Revisions.Single(r => r.Scope == "assignment").Hash!, read, StringComparison.Ordinal);
        string[] prefix = ["--estate", estate.Root, "--recipient", estate.OperatorId, "--for-assignment", assignment];
        string status = CliHarness.Ok([.. prefix, "--json", "status"]).Out;
        string brief = CliHarness.Ok([.. prefix, "--json", "work", "brief", "COMMS-J"]).Out;
        string handoff = CliHarness.Ok([.. prefix, "--json", "handoff"]).Out;
        int records = estate.Load().Records.Count;
        foreach (string depth in CommunicationResolution.Depths)
        {
            string description = CliHarness.Ok([.. prefix, "--depth", depth, "communication", "show"]).Out;
            Assert.Contains($"status: {depth} ← view", description, StringComparison.Ordinal);
            Assert.Contains("assignment record " + scoped.Revisions.Single(r => r.Scope == "assignment").Record, description, StringComparison.Ordinal);
            Assert.Equal(status, CliHarness.Ok([.. prefix, "--depth", depth, "--json", "status"]).Out);
            Assert.Equal(brief, CliHarness.Ok([.. prefix, "--depth", depth, "--json", "work", "brief", "COMMS-J"]).Out);
            Assert.Equal(handoff, CliHarness.Ok([.. prefix, "--depth", depth, "--json", "handoff"]).Out);
            Assert.Equal(api.Brief("COMMS-J")!.Json.GetRawText(), api.BriefFor("COMMS-J", estate.OperatorId, assignment, new(Handoff: depth))!.Json.GetRawText());
        }

        Assert.Equal(status, CanonicalJson.SerializePretty(api.Status().Json));
        Assert.Equal(records, estate.Load().Records.Count);
        Assert.Equal("standard", api.Communication(estate.OperatorId, assignment).Fields["status"].Value);
        Assert.Equal("short", api.Communication(estate.OperatorId).Fields["status"].Value);
    }

    private static JsonElement Json(string text) => JsonSerializer.Deserialize<JsonElement>(text);

    [Fact]
    public void D08B_QuietShortStatusKeepsCompactAttentionAndOpensTheWholeAsk()
    {
        using var estate = new HandoffEstate();
        estate.Add("work-order", estate.OperatorId, """{"key":"COMMS-J","kind":"ordinary","title":"Interrupted work","intent":"Inspect before retrying.","scope":["tests"],"exitQuestion":"Reconciled?","inputs":[],"deliverables":["results"]}""");
        using EstateService api = EstateService.Open(estate.Root);
        string assignment = Ok(api.Assign(new("COMMS-J", estate.BuilderId, estate.OperatorId, Json(AssignmentBody))));
        string attempt = Ok(api.Attempt(new(assignment, estate.BuilderId, "local clone", "Check before continuing.")));
        string result = Ok(api.Result(new(attempt, estate.BuilderId, Json("""{"outcome":"partial","summary":"Validation failed.","outputs":[],"evidence":[],"checks":[{"name":"journey","outcome":"failed","summary":"The response was incomplete."}],"limitations":[],"unresolvedEffects":[],"nextStep":"Inspect the failed check."}"""))));
        const string intent = """{"provider":"fixture","action":"write-widget","identity":"widget-original-operation","intent":"Write the widget.","target":{"repository":"Example/widget"},"status":"not-started","evidence":[]}""";
        string operation = Ok(api.NewRecord(new("operation", estate.OperatorId, Json(intent))));
        JsonObject dispatched = JsonNode.Parse(intent)!.AsObject();
        dispatched["status"] = "dispatched";
        dispatched["dispatchedAt"] = api.Record(operation)!.Envelope.GetProperty("at").GetString();
        operation = Ok(api.NewRecord(new("operation", estate.OperatorId, Json(dispatched.ToJsonString()), Supersedes: operation)));
        dispatched["status"] = "uncertain";
        operation = Ok(api.NewRecord(new("operation", estate.OperatorId, Json(dispatched.ToJsonString()), Supersedes: operation)));
        string ask = Ok(api.NewRecord(new("ask", estate.BuilderId, Json($$"""{"to":"{{estate.OperatorId}}","question":"How should the uncertain effect be reconciled?","recommendation":"Inspect the original operation before retrying.","options":["Inspect its existing identity.","Abandon the retry."],"consequence":"The work stays blocked and no retry is dispatched.","state":"open"}"""))));
        CliHarness.Ok("--estate", estate.Root, "communication", "set", "--as", estate.OperatorId, "--progress", "none", "--status", "short", "--decisions", "short", "--results", "short", "--handoffs", "short");
        int count = estate.Load().Records.Count;
        string status = CliHarness.Ok("--estate", estate.Root, "--recipient", estate.OperatorId, "status").Out;
        Assert.Contains("status (short)", status, StringComparison.Ordinal);
        Assert.Contains("uncertain-effect", status, StringComparison.Ordinal);
        Assert.Contains("failed-check", status, StringComparison.Ordinal);
        Assert.Contains("widget-original-operation", status, StringComparison.Ordinal);
        Assert.Contains("1 failed checks", status, StringComparison.Ordinal);
        Assert.Contains("vericairn records show " + ask, status, StringComparison.Ordinal);
        Assert.DoesNotContain(api.Record(result)!.Envelope.GetRawText(), status, StringComparison.Ordinal);
        var mcp = new ToolRegistry(api, null);
        string askText = mcp.Call("asks_list", new JsonObject { ["to"] = estate.OperatorId }).Text;
        foreach (string text in new[] { askText, api.Asks(estate.OperatorId).Text })
        {
            foreach (string required in new[] { ask, "Inspect the original operation before retrying.", "Inspect its existing identity.", "Abandon the retry.", "The work stays blocked and no retry is dispatched." })
            {
                Assert.Contains(required, text, StringComparison.Ordinal);
            }

            Assert.True(text.IndexOf("Recommendation:", StringComparison.Ordinal) < text.IndexOf("Asked:", StringComparison.Ordinal));
        }

        foreach (string id in new[] { result, operation })
        {
            RecordResponse original = api.Record(id)!;
            ToolCallResult read = mcp.Call("record_get", new JsonObject { ["id"] = id });
            Assert.False(read.IsError);
            Assert.True(JsonElement.DeepEquals(original.Envelope, Json(read.Text).GetProperty("envelope")));
            Assert.Contains(original.Hash, status, StringComparison.Ordinal);
        }

        Assert.True(CommunicationPresentation.ShouldDeliver("none", "progress", CommunicationPresentation.Attention(api, estate.OperatorId)));
        Assert.Equal(count, estate.Load().Records.Count);
        Assert.Equal("open", api.Record(ask)!.Envelope.GetProperty("body").GetProperty("state").GetString());
        Assert.Empty(api.Records("acceptance"));
    }

    private static string Ok(WriteOutcome outcome)
    {
        Assert.True(outcome.Ok, outcome.Problem?.Message + JsonSerializer.Serialize(outcome.Problem, ProtocolJson.Options));
        return outcome.Written!.Id;
    }

    [Fact]
    public void D08C_AMeasuredOmissionCanBeRetrievedUntilItsExactOriginalChanges()
    {
        using var estate = new HandoffEstate();
        byte[] original = Encoding.UTF8.GetBytes(string.Concat(Enumerable.Repeat("café 漢字\n", 20_000)));
        Directory.CreateDirectory(Path.Combine(estate.Root, "docs"));
        string path = Path.Combine(estate.Root, "docs", "large.md");
        File.WriteAllBytes(path, original);
        var revision = estate.Add("document-revision", estate.OperatorId, $$"""{"path":"docs/large.md","title":"Optional source","hash":"{{ContentHash.Sha256(original)}}","bytes":{{original.Length}},"mediaType":"text/markdown","revision":1,"summary":"A large optional original."}""");
        var order = estate.Add("work-order", estate.OperatorId, """{"key":"COMMS-J","kind":"ordinary","title":"Measured omission","intent":"Read docs/large.md if useful.","scope":["tests"],"exitQuestion":"Measured?","inputs":[],"deliverables":["results"]}""");
        using EstateService api = EstateService.Open(estate.Root);
        var mcp = new ToolRegistry(api, null);
        const long cap = 100_000;
        ContextResponse response = api.Context("COMMS-J", cap);
        Assert.Null(response.Problem);
        ContextPackage package = response.Package!;
        string cli = CliHarness.Ok("--estate", estate.Root, "context", "prepare", "COMMS-J", "--max-bytes", "100000").Out;
        ToolCallResult prepared = mcp.Call("context_prepare", new JsonObject { ["workOrder"] = "COMMS-J", ["maxBytes"] = "100000" });
        Assert.False(prepared.IsError);
        Assert.Equal(response.Text, cli);
        Assert.Equal(cli, prepared.Text);
        Assert.Equal(package.Bytes, Encoding.UTF8.GetByteCount(cli));
        Assert.Equal(ContentHash.Sha256(Encoding.UTF8.GetBytes(cli)), package.Hash);
        JsonElement payload = package.Payload;
        JsonElement withheld = Assert.Single(payload.GetProperty("withheld").EnumerateArray());
        Assert.Equal(original.Length, withheld.GetProperty("originalBytes").GetInt64());
        Assert.Equal(original.Length, withheld.GetProperty("withheldBytes").GetInt64());
        Assert.Equal(0, withheld.GetProperty("includedBytes").GetInt64());
        Assert.Equal("budget", withheld.GetProperty("reason").GetString());
        Assert.Equal("withheld-but-retrievable", withheld.GetProperty("state").GetString());
        Assert.Equal(package.Bytes, payload.GetProperty("accounting").GetProperty("bytes").GetInt64());
        Assert.Equal(cap - package.Bytes, payload.GetProperty("accounting").GetProperty("remainingBytes").GetInt64());
        long originals = payload.GetProperty("originals").EnumerateArray().Sum(s => s.GetProperty("bytes").GetInt64());
        long manifest = CanonicalJson.SerializePrettyUtf8(payload.GetProperty("manifest")).LongLength;
        Assert.True(package.Bytes > originals + manifest);
        Assert.Equal(ContentHash.Sha256(CanonicalJson.SerializePrettyUtf8(payload.GetProperty("presentation"))), payload.GetProperty("derivatives")[0].GetProperty("hash").GetString());
        Assert.NotEqual(withheld.GetProperty("byteHash").GetString(), payload.GetProperty("derivatives")[0].GetProperty("hash").GetString());
        Assert.Equal("off", payload.GetProperty("presentation").GetProperty("synthesis").GetString());
        string pin = withheld.GetProperty("pin").GetString()!;
        string command = withheld.GetProperty("retrieve").GetString()!;
        Assert.Matches("^vericairn context read vc1_[A-Za-z0-9_-]+ --original$", command);
        string scratch = CliHarness.TempDirectory("comms-original");
        try
        {
            string fetched = Path.Combine(scratch, "original.md");
            CliHarness.Ok(["--estate", estate.Root, .. command.Split(' ').Skip(1), "--out", fetched]);
            Assert.Equal(original, File.ReadAllBytes(fetched));
            Assert.Equal(withheld.GetProperty("byteHash").GetString(), ContentHash.Sha256File(fetched));
            ToolCallResult read = mcp.Call("context_read", new JsonObject { ["pin"] = pin });
            Assert.False(read.IsError);
            Assert.Equal(original, JsonSerializer.Deserialize<ContextReadResponse>(read.Text, ProtocolJson.Options)!.Content);
            Assert.Equal(original, api.ContextRead(pin).Content);

            // A changed working file cannot satisfy an old pin or silently reuse its measured size.
            File.WriteAllText(path, "Edited original: café.\n");
            Assert.Equal("original-unavailable", api.ContextRead(pin).Problem!.Code);
            Assert.True(mcp.Call("context_read", new JsonObject { ["pin"] = pin }).IsError);
            Assert.Equal(2, CliHarness.Run(["--estate", estate.Root, "--json", .. command.Split(' ').Skip(1)]).Code);
            ContextPackage changed = api.Context("COMMS-J", cap).Package!;
            Assert.NotEqual(package.Hash, changed.Hash);
            JsonElement missing = changed.Payload.GetProperty("withheld").EnumerateArray().Single(s => s.GetProperty("kind").GetString() == "document");
            Assert.Equal(JsonValueKind.Null, missing.GetProperty("originalBytes").ValueKind);
            Assert.Equal("missing", missing.GetProperty("state").GetString());
            Assert.Equal(original, File.ReadAllBytes(fetched));
            File.WriteAllBytes(path, original);
            Assert.Equal(package.Hash, api.Context("COMMS-J", cap).Package!.Hash);
        }
        finally
        {
            CliHarness.Delete(scratch);
        }

        // Even the mandatory raw bytes alone cannot pay for their manifest and wrappers.
        ContextResponse overhead = api.Context("COMMS-J", originals);
        Assert.Null(overhead.Package);
        Assert.Equal("context-over-budget", overhead.Problem!.Code);
        JsonObject required = JsonNode.Parse(order.Body.GetRawText())!.AsObject();
        required["inputs"] = new JsonArray(new JsonObject { ["type"] = "document-revision", ["id"] = revision.Id });
        Ok(api.NewRecord(new("work-order", estate.OperatorId, Json(required.ToJsonString()), Supersedes: order.Id)));
        Assert.Null(api.Context("COMMS-J", cap).Package);
        Assert.Equal("context-over-budget", api.Context("COMMS-J", cap).Problem!.Code);
        CliHarness.Outcome refused = CliHarness.Run("--estate", estate.Root, "context", "prepare", "COMMS-J", "--max-bytes", "100000");
        Assert.Equal(2, refused.Code);
        Assert.Contains("Nothing was truncated or dispatched", refused.Out, StringComparison.Ordinal);
        Assert.True(mcp.Call("context_prepare", new JsonObject { ["workOrder"] = "COMMS-J", ["maxBytes"] = "100000" }).IsError);
        Assert.Empty(api.Records("operation"));
    }
}
