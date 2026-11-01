// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using System.Text.Json.Nodes;
using Vericairn.Mcp;
using Vericairn.Protocol;
using Vericairn.Records.Estate;
using Vericairn.Records.Model;
using Vericairn.Records.Tests;
using Vericairn.Runtime.Service;

namespace Vericairn.Runtime.Tests;

[Collection("home")]
public sealed class AnsweredAskViewsTests
{
    [Fact]
    public void CurrentDecisionAnswersReachEveryRuntimeAndMcpQueueWithoutAnAskRevision()
    {
        using var fixture = new AnsweredAskFixture();
        var mcp = new ToolRegistry(fixture.Service, null);
        foreach (bool answered in new[] { false, true, false, true })
        {
            fixture.Decide(answered);
            Dictionary<string, string> before = fixture.Records();
            AnsweredAskFixture.AssertOpenViews(fixture.Service, fixture, answered);
            var asks = mcp.Call("asks_list", new() { ["to"] = fixture.Estate.OperatorId, ["form"] = "json" });
            Assert.False(asks.IsError, asks.Text);
            Assert.True(JsonElement.DeepEquals(fixture.Service.Asks(fixture.Estate.OperatorId).Json, JsonSerializer.Deserialize<JsonElement>(asks.Text)));
            string text = mcp.Call("asks_list", new() { ["to"] = fixture.Estate.OperatorId }).Text;
            Assert.Contains("Still pending?", text, StringComparison.Ordinal);
            Assert.Equal(!answered, text.Contains("Continue this work?", StringComparison.Ordinal));
            foreach (var (tool, json) in new[] { ("estate_status", fixture.Service.Status().Json), ("estate_progress", fixture.Service.Progress().Json) })
            {
                var result = mcp.Call(tool, new() { ["form"] = "json" });
                Assert.False(result.IsError, result.Text);
                Assert.True(JsonElement.DeepEquals(json, JsonSerializer.Deserialize<JsonElement>(result.Text)));
            }
            Assert.Equal(before.OrderBy(p => p.Key), fixture.Records().OrderBy(p => p.Key));
            fixture.AssertAskUnchanged(answered);
        }
    }
}

public sealed class AnsweredAskFixture : IDisposable
{
    public static void AssertOpenViews(IEstateApi api, AnsweredAskFixture fixture, bool answered)
    {
        string[] expected = (answered ? new[] { fixture.Pending.Id } : new[] { fixture.Notified.Id, fixture.Pending.Id }).Order(StringComparer.Ordinal).ToArray();
        foreach (string? recipient in new[] { null, fixture.Estate.OperatorId })
        {
            AsksResponse asks = api.Asks(recipient);
            Assert.Equal(expected, asks.Json.GetProperty("principals").EnumerateArray().SelectMany(p => p.GetProperty("delivered").EnumerateArray())
                .Select(a => a.GetProperty("revision").GetString()).Order(StringComparer.Ordinal));
            Assert.Equal(!answered, asks.Text.Contains("Continue this work?", StringComparison.Ordinal));
            Assert.Contains("Still pending?", asks.Text, StringComparison.Ordinal);
        }
        OperatorActionsResponse actions = api.OperatorActions();
        Assert.False(actions.HasErrors);
        Assert.Equal(expected, actions.Json.GetProperty("items").EnumerateArray().Where(a => a.GetProperty("kind").GetString() == "ask")
            .Select(a => a.GetProperty("source").GetProperty("id").GetString()).Order(StringComparer.Ordinal));
        ProgressResponse progress = api.Progress();
        Assert.False(progress.HasErrors);
        Assert.Equal(expected, progress.Json.GetProperty("operator").GetProperty("asks").EnumerateArray().Select(a => a.GetProperty("id").GetString()).Order(StringComparer.Ordinal));
        foreach (JsonElement view in new[] { api.Status().Json, progress.Json })
            Assert.Equal(expected, view.GetProperty("attention").EnumerateArray().Where(a => a.GetProperty("kind").GetString() == "required-decision")
                .Select(a => a.GetProperty("record").GetString()).Order(StringComparer.Ordinal));
    }

    public TestEstate Estate { get; } = new(3);
    public EstateService Service { get; }
    public RecordEnvelope Original { get; }
    public RecordEnvelope Notified { get; }
    public RecordEnvelope Pending { get; }
    private RecordEnvelope? decision;

    public AnsweredAskFixture()
    {
        Original = Ask("Continue this work?");
        JsonObject body = JsonNode.Parse(Original.Body.GetRawText())!.AsObject();
        body["state"] = "notified";
        Notified = Estate.Add("ask", Original.By, body.ToJsonString(), supersedes: Original.Id);
        Pending = Ask("Still pending?");
        RecordEnvelope withdrawn = Ask("Withdrawn question?");
        body = JsonNode.Parse(withdrawn.Body.GetRawText())!.AsObject();
        body["state"] = "withdrawn";
        Estate.Add("ask", withdrawn.By, body.ToJsonString(), supersedes: withdrawn.Id);
        Service = EstateService.Open(Estate.Root);
    }

    public void Decide(bool answers)
    {
        JsonObject body = new()
        {
            ["title"] = "Continue the work", ["statement"] = "Continue.", ["summary"] = "The operator reviewed the question.",
            ["synthesis"] = "Continue the work.", ["effects"] = new JsonArray("Continue."), ["affects"] = new JsonArray(),
            ["decidedBy"] = Estate.OperatorId, ["decidedAt"] = Notified.At, ["stake"] = "low", ["notAuthorized"] = new JsonArray(),
        };
        if (answers) body["answers"] = new JsonArray(Original.Id);
        decision = Estate.Add("decision", Estate.OperatorId, body.ToJsonString(), supersedes: decision?.Id);
    }

    public void AssertAskUnchanged(bool answered)
    {
        EstateModel model = Estate.Load();
        Assert.False(model.Findings.HasErrors, string.Join('\n', model.Findings));
        Assert.Equal(5, model.OfType("ask").Count());
        Assert.Equal(Notified.Id, model.Latest(Original).Id);
        Assert.Equal(Notified.ComputedHash, model.Latest(Original).ComputedHash);
        Assert.Equal("notified", EstateModel.BodyString(model.Latest(Original), "state"));
        Assert.Equal(answered ? "answered" : "notified", Asks.State(model, Original).State);
    }

    public Dictionary<string, string> Records() => Estate.Load().Records.ToDictionary(r => r.Id, r => r.ComputedHash);

    private RecordEnvelope Ask(string question) => Estate.Add("ask", Estate.BuilderId,
        JsonSerializer.Serialize(new { to = Estate.OperatorId, question, recommendation = "Review the question.", consequence = "Work waits.", state = "open" }));

    public void Dispose()
    {
        Service.Dispose();
        Estate.Dispose();
    }
}
