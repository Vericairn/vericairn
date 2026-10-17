// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text;
using System.Text.Json;
using Vericairn.Mcp;
using Vericairn.Protocol;
using Vericairn.Records.Canonical;
using Vericairn.Records.Model;
using Vericairn.Records.Signing;
using Vericairn.Runtime.Service;
using Vericairn.Runtime.Workers;

namespace Vericairn.Cli.Tests;

[Collection("cli")]
public class AttentionJourneyTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TwoRuntimesWithTheSameRecordsHaveByteIdenticalStatusWhileProgressKeepsLocalWorkerConcerns(bool settled)
    {
        _ = CliHarness.Run("--help");
        string? originalHome = Environment.GetEnvironmentVariable(VericairnHome.EnvironmentVariable);
        string withoutObservations = CliHarness.TempDirectory("attention-records-only");
        string withObservations = CliHarness.TempDirectory("attention-local-worker");
        using var estate = new HandoffEstate();
        RecordEnvelope goal = estate.Add("goal", estate.OperatorId, $$"""{"title":"Check attention","objective":"Keep status portable.","completion":[{"evidence":"Regression tests","check":"Run them."}],"constraints":[],"kind":"finite","sponsor":"{{estate.OperatorId}}","budget":{"wallMinutes":60,"attemptsPerStep":3},"plan":{"policy":"proposed-by-planner","approval":"sponsor-approves"},"inputs":[]}""");
        estate.Add("goal-status", estate.OperatorId, """{"status":"active","note":"Exercise the worker view."}""", subject: goal.Reference);
        RecordEnvelope order = estate.Add("work-order", estate.OperatorId, $$"""{"key":"LOCAL","kind":"ordinary","title":"Local observations","intent":"Keep status portable.","scope":["tests"],"exitQuestion":"Portable?","inputs":[{"type":"goal","id":"{{goal.Id}}"}],"deliverables":["evidence"]}""");
        RecordEnvelope assignment = estate.Add("assignment", estate.OperatorId, $$$"""{"contributor":"{{{estate.BuilderId}}}","brief":{"outcome":"Check attention.","sources":[],"permittedChanges":["tests"],"prohibitedEffects":[],"workspace":["local clone"],"limits":[],"expectedOutputs":["result"],"reviewCriteria":["portable"],"recovery":"Inspect the records."}}""", subject: order.Reference);
        string Body(string status, string extra = "") => $$"""{"provider":"generic","action":"agent-run","identity":"attention-test","intent":"Check attention.","target":{"assignment":"{{assignment.Id}}","principal":"{{estate.BuilderId}}","workspace":"widget, branch work/local","goal":"{{goal.Id}}"},"status":"{{status}}","cap":{"wallMinutes":5,"attemptsPerStep":3},"evidence":[]{{extra}}}""";
        RecordEnvelope intent = estate.Add("operation", estate.OperatorId, Body("not-started"));
        string dispatched = $$""","dispatchedAt":"{{intent.At}}" """;
        RecordEnvelope latest = estate.Add("operation", estate.OperatorId, Body("dispatched", dispatched), supersedes: intent.Id);
        if (settled)
        {
            latest = estate.Add("operation", estate.OperatorId, Body("observed-success", dispatched + $$""","observed":{"summary":"The stand-in exited.","at":"{{latest.At}}"},"usage":{"wallMinutes":1,"unreported":[]}"""), supersedes: latest.Id);
        }
        int recordCount = estate.Load().Records.Count;
        try
        {
            Environment.SetEnvironmentVariable(VericairnHome.EnvironmentVariable, withoutObservations);
            using EstateService remote = EstateService.Open(estate.Root);
            Assert.Equal(0, remote.Validate().Errors);
            byte[] portable = CanonicalJson.SerializePrettyUtf8(remote.Status().Json);
            string remoteCli = CliHarness.Ok("--estate", estate.Root, "--json", "status").Out;
            Assert.Empty(remote.Progress().Json.GetProperty("runSignals").EnumerateArray());
            Assert.DoesNotContain(remote.Progress().Json.GetProperty("attention").EnumerateArray(), a => a.GetProperty("kind").GetString() == "incident");

            Environment.SetEnvironmentVariable(VericairnHome.EnvironmentVariable, withObservations);
            using EstateService local = EstateService.Open(estate.Root);
            var directory = new RunDirectory(local.Session.Manifest.Estate, intent.Id);
            directory.Create();
            const string concern = "The worker observed a local concern awaiting reconciliation.";
            directory.Write(new RunObservedState
            {
                Run = intent.Id,
                Signals = settled ? [] : [new("scope-read", "warning", latest.At, concern)],
                Settled = settled ? "observed-success" : null,
                Problem = settled ? concern : null,
                ExitedAt = settled ? latest.At : null,
            });
            Assert.Equal(portable, CanonicalJson.SerializePrettyUtf8(local.Status().Json));
            Assert.Equal(remoteCli, CliHarness.Ok("--estate", estate.Root, "--json", "status").Out);
            Assert.Equal(portable, CanonicalJson.SerializePrettyUtf8(JsonSerializer.Deserialize<JsonElement>(new ToolRegistry(local, null).Call("estate_status", new()).Text)));
            ProgressResponse progress = local.Progress();
            Assert.Contains(concern, progress.Text, StringComparison.Ordinal);
            JsonElement incident = Assert.Single(progress.Json.GetProperty("attention").EnumerateArray(), a => a.GetProperty("kind").GetString() == "incident");
            Assert.Equal(latest.Id, incident.GetProperty("record").GetString());
            Assert.Equal(latest.ComputedHash, incident.GetProperty("hash").GetString());
            Assert.Contains("Local worker observations:", incident.GetProperty("text").GetString()!, StringComparison.Ordinal);
            foreach (string command in new[] { "status", "progress" })
            {
                string human = CliHarness.Ok("--estate", estate.Root, "--depth", "short", command).Out;
                Assert.Contains("Local worker observations:", human, StringComparison.Ordinal);
                Assert.Contains("vericairn worker runs", human, StringComparison.Ordinal);
            }
            Assert.Equal(recordCount, estate.Load().Records.Count);
        }
        finally
        {
            Environment.SetEnvironmentVariable(VericairnHome.EnvironmentVariable, originalHome);
            CliHarness.Delete(withoutObservations, withObservations);
        }
    }

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
