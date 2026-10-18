// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using System.Text.Json.Nodes;
using Vericairn.Protocol;
using Vericairn.Records.Canonical;
using Vericairn.Records.Model;
using Vericairn.Records.Tests;
using Vericairn.Runtime.Service;

namespace Vericairn.Runtime.Tests;

[Collection("home")]
public class OperatorActionsTests
{
    [Theory]
    [InlineData("short")]
    [InlineData("standard")]
    [InlineData("comprehensive")]
    public void EveryDepthAndQuietFrequencyKeepEveryItemAndReadsLeaveExistingViewsAlone(string depth)
    {
        using var estate = new TestEstate(2);
        List<RecordEnvelope> asks = Enumerable.Range(0, 5).Select(i => Ask(estate, "Question " + i)).ToList();
        using EstateService api = EstateService.Open(estate.Root);
        Assert.True(api.CommunicationSet(new(estate.OperatorId, "person", new(Progress: "none", Status: depth), estate.OperatorId)).Ok);
        string status = CanonicalJson.SerializePretty(api.Status().Json);
        AsksResponse delivery = api.Asks();
        int count = estate.Load().Records.Count;
        OperatorActionsResponse response = api.OperatorActions();
        Assert.False(response.HasErrors);
        Assert.StartsWith("status (" + depth + ")", response.Text, StringComparison.Ordinal);
        Assert.Equal(5, response.Json.GetProperty("items").GetArrayLength());
        foreach (RecordEnvelope ask in asks)
        {
            Assert.Contains(ask.Id, response.Text, StringComparison.Ordinal);
            Assert.Contains(ask.ComputedHash, response.Text, StringComparison.Ordinal);
        }
        Assert.Equal(count, estate.Load().Records.Count);
        Assert.Equal(status, CanonicalJson.SerializePretty(api.Status().Json));
        Assert.Equal(delivery.Text, api.Asks().Text);
        Assert.Equal(delivery.Json.GetRawText(), api.Asks().Json.GetRawText());
        Assert.Empty(estate.Load().Current("device"));
        Assert.Equal(response.Json.GetRawText(), api.OperatorActions(view: new(Status: "comprehensive")).Json.GetRawText());
    }

    [Fact]
    public void UnknownCommunicationRecipientCannotHideTheQueueOrSupplyAuthority()
    {
        using var estate = new TestEstate(2);
        RecordEnvelope ask = Ask(estate, "Question");
        using EstateService api = EstateService.Open(estate.Root);
        OperatorActionsResponse response = api.OperatorActions(estate.BuilderId, view: new(Status: "short"));
        Assert.Contains("Unavailable:", response.Text, StringComparison.Ordinal);
        Assert.Contains(ask.Id, response.Text, StringComparison.Ordinal);
        Assert.Equal(estate.OperatorId, response.Json.GetProperty("items")[0].GetProperty("commands")[0].GetProperty("by").GetString());
    }

    [Fact]
    public void AcceptanceCommandAndBodyAreValidAfterTheOperatorSuppliesTheirOwnStatement()
    {
        using var estate = new TestEstate(2);
        RecordEnvelope milestone = estate.Add("work-order", estate.OperatorId, """{"key":"M-1","kind":"milestone","title":"The milestone","intent":"Finish.","scope":["work"],"exitQuestion":"Done?","inputs":[],"deliverables":["result"]}""");
        estate.Add("work-status", estate.OperatorId, """{"status":"submitted","note":"Ready."}""", milestone.Reference);
        using EstateService api = EstateService.Open(estate.Root);
        JsonElement item = Assert.Single(api.OperatorActions().Json.GetProperty("items").EnumerateArray());
        JsonElement command = Assert.Single(item.GetProperty("commands").EnumerateArray());
        JsonObject body = JsonNode.Parse(command.GetProperty("body").GetRawText())!.AsObject();
        int before = estate.Load().Records.Count;
        Assert.False(Submit(api, command, body).Ok);
        Assert.Equal(before, estate.Load().Records.Count);
        body["statement"] = "I accept this milestone from its recorded evidence.";
        Assert.True(Submit(api, command, body).Ok);
        Assert.Equal(before + 1, estate.Load().Records.Count);
        Assert.Empty(api.OperatorActions().Json.GetProperty("items").EnumerateArray());
    }

    [Fact]
    public void DecisionCommandNamesOnlyTheAskAnsweredAndRequiresTheOperatorsWords()
    {
        using var estate = new TestEstate(2);
        RecordEnvelope ask = Ask(estate, "Question");
        using EstateService api = EstateService.Open(estate.Root);
        JsonElement command = api.OperatorActions().Json.GetProperty("items")[0].GetProperty("commands")[0];
        JsonObject body = JsonNode.Parse(command.GetProperty("body").GetRawText())!.AsObject();
        Assert.False(Submit(api, command, body).Ok);
        body["title"] = "The operator's answer";
        body["statement"] = "Leave it as it is.";
        body["summary"] = "The operator keeps the current behavior.";
        body["synthesis"] = "The question was considered; the operator keeps the current behavior.";
        body["effects"] = new JsonArray("Keep current behavior.");
        body["decidedAt"] = "2026-10-13T12:01:00Z";
        body["stake"] = "low";
        Assert.True(Submit(api, command, body).Ok);
        Assert.Empty(api.OperatorActions().Json.GetProperty("items").EnumerateArray());
        Assert.Equal("answered", Vericairn.Records.Estate.Asks.State(estate.Load(), ask).State);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void InvalidRecordsReturnAnErrorAlongWithTheUnresolvedQueue(bool missingQuestion)
    {
        using var estate = new TestEstate(2);
        RecordEnvelope ask = missingQuestion
            ? estate.Add("ask", estate.BuilderId, $$"""{"to":"{{estate.OperatorId}}","recommendation":"Review.","consequence":"Work waits.","state":"open"}""")
            : Ask(estate, "Question");
        if (!missingQuestion) estate.Add("unknown-record", estate.OperatorId, "{}");
        using EstateService api = EstateService.Open(estate.Root);
        OperatorActionsResponse response = api.OperatorActions();
        Assert.True(response.HasErrors);
        Assert.NotEmpty(response.Json.GetProperty("findings").EnumerateArray());
        Assert.Empty(response.Json.GetProperty("items")[0].GetProperty("commands").EnumerateArray());
        Assert.Contains(ask.Id, response.Text, StringComparison.Ordinal);
        Assert.Contains("Resolve the estate's record errors", response.Text, StringComparison.Ordinal);
    }

    private static RecordEnvelope Ask(TestEstate estate, string question) => estate.Add("ask", estate.BuilderId,
        new JsonObject { ["to"] = estate.OperatorId, ["question"] = question, ["recommendation"] = "Consider the evidence.", ["consequence"] = "Work waits.", ["state"] = "open" }.ToJsonString());

    private static WriteOutcome Submit(EstateService api, JsonElement command, JsonObject body)
    {
        string[] arguments = command.GetProperty("arguments").EnumerateArray().Select(a => a.GetString()!).ToArray();
        int subject = Array.IndexOf(arguments, "--subject");
        return api.NewRecord(new NewRecordRequest(arguments[2], arguments[4], JsonSerializer.SerializeToElement(body), subject < 0 ? null : arguments[subject + 1]));
    }
}
