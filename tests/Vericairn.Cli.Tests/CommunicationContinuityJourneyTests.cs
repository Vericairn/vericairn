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
public class CommunicationContinuityJourneyTests
{
    [Fact]
    public void D08E_ASuccessorResumesFromOriginalRecordsAfterThePreviousSessionEnds()
    {
        using var estate = new HandoffEstate();
        string order;
        string decision;
        string result;
        string operation;
        using (EstateService previous = EstateService.Open(estate.Root))
        {
            order = Ok(previous.NewRecord(new("work-order", estate.OperatorId, Json("""{"key":"COMMS-J","kind":"ordinary","title":"Interrupted work","intent":"Reconcile the original operation.","scope":["tests"],"exitQuestion":"Reconciled?","inputs":[],"deliverables":["result"]}"""))));
            decision = Ok(previous.NewRecord(new("decision", estate.OperatorId, Json($$"""{"title":"Keep the original identity","statement":"Do not retry until the original effect is reconciled.","summary":"Inspect before retrying.","synthesis":"The interruption did not settle the effect.","effects":["Reconcile the original operation."],"affects":["{{order}}"],"decidedBy":"{{estate.OperatorId}}","decidedAt":"2026-10-13T12:00:00Z","stake":"low","notAuthorized":["No second dispatch."]}"""))));
            string assignment = Ok(previous.Assign(new("COMMS-J", estate.BuilderId, estate.OperatorId, Json("""{"outcome":"Inspect the operation.","sources":[],"permittedChanges":["tests"],"prohibitedEffects":["no retry"],"workspace":["local clone"],"limits":[],"expectedOutputs":["result"],"reviewCriteria":["effect reconciled"],"recovery":"Read the decision and original operation."}"""))));
            string attempt = Ok(previous.Attempt(new(assignment, estate.BuilderId, "local clone", "Inspect the interruption.")));
            JsonObject effect = JsonNode.Parse($$$"""{"provider":"fixture","action":"write-widget","identity":"original-widget-effect","intent":"Write the widget.","target":{"assignment":"{{{assignment}}}"},"status":"not-started","evidence":[]}""" )!.AsObject();
            operation = Ok(previous.NewRecord(new("operation", estate.OperatorId, Json(effect.ToJsonString()))));
            effect["dispatchedAt"] = previous.Record(operation)!.Envelope.GetProperty("at").GetString();
            effect["status"] = "dispatched";
            operation = Ok(previous.NewRecord(new("operation", estate.OperatorId, Json(effect.ToJsonString()), Supersedes: operation)));
            effect["status"] = "uncertain";
            operation = Ok(previous.NewRecord(new("operation", estate.OperatorId, Json(effect.ToJsonString()), Supersedes: operation)));
            result = Ok(previous.Result(new(attempt, estate.BuilderId, Json($$"""{"outcome":"partial","summary":"Interrupted after the operation was dispatched.","outputs":[],"evidence":[],"checks":[{"name":"reply","outcome":"failed","summary":"The reply was lost."},{"name":"external state","outcome":"not-performed","summary":"No external observation was made."}],"limitations":["Conversation is not retained."],"unresolvedEffects":["Inspect operation {{operation}} under original-widget-effect before retrying."],"nextStep":"Reconcile the recorded operation; do not dispatch again."}"""))));
        }

        // The previous service is gone. Only the portable hand-over and the copied estate remain.
        string saved = CliHarness.Ok("--estate", estate.Root, "--json", "handoff").Out;
        HandoffPackage package = JsonSerializer.Deserialize<HandoffPackage>(saved, ProtocolJson.Options)!;
        string copy = CliHarness.Clone(estate.Root, "comms-successor");
        string scratch = CliHarness.TempDirectory("comms-resume");
        try
        {
            string path = CliHarness.WriteJson(scratch, "handoff.json", saved);
            using EstateService successor = EstateService.Open(copy);
            var mcp = new ToolRegistry(successor, null);
            foreach (string id in new[] { decision, result, operation })
            {
                JsonElement retained = package.Payload.GetProperty("originals").EnumerateArray().Single(s => s.GetProperty("id").GetString() == id);
                byte[] bytes = Convert.FromBase64String(retained.GetProperty("content").GetString()!);
                Assert.Equal(File.ReadAllBytes(Path.Combine(copy, retained.GetProperty("path").GetString()!)), bytes);
                Assert.Equal(ContentHash.Sha256(bytes), retained.GetProperty("byteHash").GetString());
                string pin = retained.GetProperty("pin").GetString()!;
                ToolCallResult fetched = mcp.Call("context_read", new JsonObject { ["pin"] = pin });
                Assert.False(fetched.IsError);
                Assert.Equal(bytes, JsonSerializer.Deserialize<ContextReadResponse>(fetched.Text, ProtocolJson.Options)!.Content);
            }

            Assert.Contains("original-widget-effect", successor.Handoff().Text, StringComparison.Ordinal);
            Assert.Contains("The reply was lost.", successor.Handoff().Text, StringComparison.Ordinal);
            Assert.Contains("No external observation was made.", successor.Handoff().Text, StringComparison.Ordinal);
            Assert.Contains("Do not retry until the original effect is reconciled.", successor.Handoff().Text, StringComparison.Ordinal);
            Assert.False(successor.ResumeHandoff(package).Json!.Value.GetProperty("changed").GetBoolean());
            JsonObject changed = JsonNode.Parse(successor.Record(order)!.Envelope.GetProperty("body").GetRawText())!.AsObject();
            changed["intent"] = "Review the changed source before reconciliation.";
            string revised = Ok(successor.NewRecord(new("work-order", estate.OperatorId, Json(changed.ToJsonString()), Supersedes: order)));
            int count = successor.Session.Load().Records.Count;
            JsonElement resume = CliHarness.Ok("--estate", copy, "--json", "resume", path).Json;
            Assert.True(resume.GetProperty("changed").GetBoolean());
            Assert.Contains(resume.GetProperty("added").EnumerateArray(), r => r.GetProperty("id").GetString() == revised);
            Assert.Contains(resume.GetProperty("superseded").EnumerateArray(), r => r.GetProperty("previous").GetString() == order && r.GetProperty("successor").GetString() == revised);
            Assert.Equal(CanonicalJson.SerializePrettyUtf8(successor.ResumeHandoff(package).Json!.Value), CanonicalJson.SerializePrettyUtf8(resume));
            Assert.Contains("Nothing is restarted or accepted", successor.ResumeHandoff(package).Text, StringComparison.Ordinal);
            Assert.Equal(saved, File.ReadAllText(path));
            Assert.Equal(count, successor.Session.Load().Records.Count);
            Assert.Equal(operation, Assert.Single(successor.Records("operation")).Id);
            Assert.Empty(successor.Records("acceptance"));
        }
        finally
        {
            CliHarness.Delete(copy, scratch);
        }
    }

    [Fact]
    public void D08E_UnavailableOriginalsKeepUnknownSizesTokensAndIrreversibleLossVisible()
    {
        using var estate = new HandoffEstate();
        byte[] lost = Encoding.UTF8.GetBytes("An original not retained in this copy.\n");
        estate.Add("document-revision", estate.OperatorId, $$"""{"path":"docs/lost.md","title":"Unavailable original","hash":"{{ContentHash.Sha256(lost)}}","bytes":{{lost.Length}},"mediaType":"text/markdown","revision":1,"summary":"This summary cannot restore the original."}""");
        estate.Add("work-order", estate.OperatorId, """{"key":"COMMS-J","kind":"ordinary","title":"Lossy continuation","intent":"Inspect docs/lost.md if available.","scope":["tests"],"exitQuestion":"Honest limits?","inputs":[],"deliverables":["result"]}""");
        using EstateService api = EstateService.Open(estate.Root);
        var mcp = new ToolRegistry(api, null);
        string cli = CliHarness.Ok("--estate", estate.Root, "context", "prepare", "COMMS-J").Out;
        Assert.Equal(cli, mcp.Call("context_prepare", new JsonObject { ["workOrder"] = "COMMS-J" }).Text);
        JsonElement payload = Json(cli);
        JsonElement lostSource = payload.GetProperty("withheld").EnumerateArray().Single(s => s.GetProperty("kind").GetString() == "document");
        foreach (string field in new[] { "bytes", "originalBytes", "withheldBytes", "tokens" })
        {
            Assert.Equal(JsonValueKind.Null, lostSource.GetProperty(field).ValueKind);
        }

        Assert.Equal("unavailable", lostSource.GetProperty("reason").GetString());
        Assert.Equal("irreversible reduction; original unavailable", lostSource.GetProperty("reduction").GetProperty("loss").GetString());
        Assert.Equal(JsonValueKind.Null, payload.GetProperty("accounting").GetProperty("tokens").ValueKind);
        Assert.Contains("unknown", payload.GetProperty("accounting").GetProperty("tokenizer").GetString(), StringComparison.Ordinal);
        Assert.Equal("not claimed", payload.GetProperty("accounting").GetProperty("modelFit").GetString());
        string pin = lostSource.GetProperty("pin").GetString()!;
        ContextReadResponse unavailable = api.ContextRead(pin);
        Assert.Null(unavailable.Content);
        Assert.Equal("original-unavailable", unavailable.Problem!.Code);
        Assert.Equal(ContentHash.Sha256(lost), unavailable.Identity!.Value.GetProperty("byteHash").GetString());
        ToolCallResult fetched = mcp.Call("context_read", new JsonObject { ["pin"] = pin });
        Assert.True(fetched.IsError);
        Assert.Equal(fetched.Text, CliHarness.Run("--estate", estate.Root, "--json", "context", "read", pin, "--original").Out);
        string brief = CliHarness.Ok("--estate", estate.Root, "--depth", "short", "work", "brief", "COMMS-J").Out;
        Assert.Contains("irreversible reduction; original unavailable", brief, StringComparison.Ordinal);
        Assert.Contains(pin, brief, StringComparison.Ordinal);
    }

    [Fact]
    public void D08E_ARevokedMcpReaderGetsNoOmissionMetadataEvenWithAPreviouslyValidPin()
    {
        using var estate = new HandoffEstate();
        byte[] original = Encoding.UTF8.GetBytes("Private optional original.\n");
        estate.Add("document-revision", estate.OperatorId, $$"""{"path":"docs/private.md","title":"Private original","hash":"{{ContentHash.Sha256(original)}}","bytes":{{original.Length}},"mediaType":"text/markdown","revision":1,"summary":"Not retained."}""");
        estate.Add("work-order", estate.OperatorId, """{"key":"COMMS-J","kind":"ordinary","title":"Private continuation","intent":"Inspect docs/private.md if available.","scope":["tests"],"exitQuestion":"Authorized?","inputs":[],"deliverables":["result"]}""");
        using EstateService api = EstateService.Open(estate.Root);
        string device = Ok(api.DeviceCreate(new(estate.OperatorId, "Context reader", "file")));
        Ok(api.DeviceAdmit(new(device, estate.OperatorId, "Read the context.")));
        DeviceStanding standing = api.Standing(device)!;
        ContextPackage package = api.Context("COMMS-J").Package!;
        string pin = package.Payload.GetProperty("withheld").EnumerateArray().Single(s => s.GetProperty("kind").GetString() == "document").GetProperty("pin").GetString()!;
        Ok(api.DeviceRevoke(new(device, estate.OperatorId, "Withdraw this reader.")));
        var mcp = new ToolRegistry(api, null);
        using (api.ForDeviceRequest(standing.Identity, standing.PublicKey))
        {
            ToolCallResult denied = mcp.Call("context_read", new JsonObject { ["pin"] = pin });
            Assert.True(denied.IsError);
            Assert.Equal(denied, mcp.Call("context_read", new JsonObject { ["pin"] = "unknown" }));
            ContextReadResponse read = JsonSerializer.Deserialize<ContextReadResponse>(denied.Text, ProtocolJson.Options)!;
            Assert.Null(read.Identity);
            Assert.Null(read.Content);
            Assert.Empty(read.Problem!.Findings);
            foreach (string tool in new[] { "context_prepare", "work_brief" })
            {
                ToolCallResult refused = mcp.Call(tool, new JsonObject { ["workOrder"] = "COMMS-J" });
                Assert.True(refused.IsError);
                Assert.Equal("Completeness cannot be confirmed.\n", refused.Text);
            }

            foreach (string metadata in new[] { pin, "docs/private.md", ContentHash.Sha256(original), "originalBytes", "withheld" })
            {
                Assert.DoesNotContain(metadata, denied.Text, StringComparison.Ordinal);
            }
        }
    }

    private static JsonElement Json(string text) => JsonSerializer.Deserialize<JsonElement>(text);

    private static string Ok(WriteOutcome outcome)
    {
        Assert.True(outcome.Ok, JsonSerializer.Serialize(outcome.Problem, ProtocolJson.Options));
        return outcome.Written!.Id;
    }
}
