// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using System.Text.Json.Nodes;
using Vericairn.Mcp;
using Vericairn.Protocol;
using Vericairn.Records.Estate;
using Vericairn.Records.Model;
using RequestData = Vericairn.Records.Tests.OperatorRequestTests;

namespace Vericairn.Runtime.Tests;

[Collection("home")]
public sealed class OperatorRequestTests
{
    public static RecordEnvelope Request(OperatorCloseFixture f, int number = 1, bool publication = false)
    {
        RecordEnvelope work = f.Estate.Load().Current("work-order").FirstOrDefault() ?? RequestData.Work(f.Estate);
        JsonObject body = RequestData.Body(f.Estate, number);
        if (publication) body["kind"] = "publication";
        WriteOutcome written = f.Api.OperatorRequestWrite(new(f.Estate.BuilderId, JsonSerializer.SerializeToElement(body), Subject(work)));
        Assert.True(written.Ok, written.Problem?.Message + string.Join('\n', written.Problem?.Findings ?? []));
        return f.Estate.Load().ById[written.Written!.Id];
    }

    public static string Subject(RecordEnvelope record) => record.Type + ":" + record.Id + ":" + record.ComputedHash;

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OneSignedStepClosesOnlyTheExactRequestAndPreservesItsRecord(bool dismiss)
    {
        using var f = new OperatorCloseFixture();
        RecordEnvelope first = Request(f);
        Request(f, 2);
        int before = f.Estate.Load().Records.Count;
        var input = new OperatorRequestCloseRequest(f.Estate.OperatorId, first.ComputedHash, dismiss ? "No longer needed." : null, "https://example.test/effect");
        WriteOutcome closed = dismiss ? f.Api.OperatorRequestDismiss(first.Id, input) : f.Api.OperatorRequestDone(first.Id, input);
        Assert.True(closed.Ok, closed.Problem?.Message);
        EstateModel model = f.Estate.Load();
        Assert.Equal(before + 1, model.Records.Count);
        RecordEnvelope close = model.ById[closed.Written!.Id];
        Assert.True(SignatureChecks.IsAuthentic(model, close));
        Assert.Equal(first.Reference, close.Subject);
        Assert.Equal(dismiss ? "dismissed" : "done", EstateModel.BodyString(close, "outcome"));
        Assert.Equal(first.Body.GetRawText(), model.ById[first.Id].Body.GetRawText());
        Assert.Equal(1, f.Api.OperatorRequests().Json.GetProperty("waiting").GetInt32());
        Assert.False(f.Api.OperatorRequestDone(first.Id, input).Ok);
        Assert.Equal(before + 1, f.Estate.Load().Records.Count);
        Assert.Empty(model.OfType("acceptance"));
        Assert.Empty(model.OfType("operation"));
        Assert.Equal(0, f.Api.Validate().Errors);
    }

    [Fact]
    public void MissingSigningDeviceStaleHashAndMissingDismissalReasonWriteNothing()
    {
        using var unsigned = new OperatorCloseFixture(device: false);
        RecordEnvelope request = Request(unsigned);
        int before = unsigned.Estate.Load().Records.Count;
        Assert.False(unsigned.Api.OperatorRequestDone(request.Id, new(unsigned.Estate.OperatorId)).Ok);
        Assert.Equal(before, unsigned.Estate.Load().Records.Count);
        using var f = new OperatorCloseFixture();
        request = Request(f);
        before = f.Estate.Load().Records.Count;
        Assert.False(f.Api.OperatorRequestDone(request.Id, new(f.Estate.OperatorId, f.Estate.Estate.ComputedHash)).Ok);
        Assert.False(f.Api.OperatorRequestDismiss(request.Id, new(f.Estate.OperatorId)).Ok);
        Assert.False(f.Api.OperatorRequestDone(request.Id, new(f.Estate.BuilderId)).Ok);
        Assert.Equal(before, f.Estate.Load().Records.Count);
    }

    [Fact]
    public void AgentClosesWithItsOwnSignatureAndTheOperatorsCitedWords()
    {
        using var f = new OperatorCloseFixture();
        string device = f.Api.DeviceCreate(new(f.Estate.BuilderId, "Agent test device", "file")).Written!.Id;
        Assert.True(f.Api.DeviceAdmit(new(device, f.Estate.OperatorId, "Admit the test agent.")).Ok);
        RecordEnvelope request = Request(f);
        var words = new OperatorRequestWords(f.Estate.OperatorId, "I have done it.", "Operator conversation, message 4", request.At);
        int before = f.Estate.Load().Records.Count;
        Assert.False(f.Api.OperatorRequestDone(request.Id, new(f.Estate.BuilderId)).Ok);
        Assert.False(f.Api.OperatorRequestDone(request.Id, new(f.Estate.BuilderId, OperatorWords: words with { Source = "" })).Ok);
        Assert.Equal(before, f.Estate.Load().Records.Count);
        WriteOutcome close = f.Api.OperatorRequestDone(request.Id, new(f.Estate.BuilderId, OperatorWords: words));
        Assert.True(close.Ok, close.Problem?.Message);
        RecordEnvelope recorded = f.Estate.Load().ById[close.Written!.Id];
        Assert.Equal(f.Estate.BuilderId, recorded.By);
        Assert.Equal("I have done it.", recorded.Body.GetProperty("operatorWords").GetProperty("statement").GetString());
        Assert.True(SignatureChecks.IsAuthentic(f.Estate.Load(), recorded));
    }

    [Fact]
    public void RevisedDraftRefusesAnOldCloseAndAnObservationClosesTheNewPublicationWithoutDispatch()
    {
        using var f = new OperatorCloseFixture();
        RecordEnvelope original = Request(f, publication: true);
        JsonObject body = JsonNode.Parse(original.Body.GetRawText())!.AsObject();
        body["why"] = "The announcement was clarified.";
        Assert.False(f.Api.OperatorRequestWrite(new(original.By, JsonSerializer.SerializeToElement(body), Subject(f.Estate.Load().ById[original.Subject!.Id]), original.Id)).Ok);
        WriteOutcome revised = f.Api.OperatorRequestWrite(new(original.By, JsonSerializer.SerializeToElement(body), Subject(f.Estate.Load().ById[original.Subject!.Id]), original.Id, original.ComputedHash));
        Assert.True(revised.Ok, revised.Problem?.Message);
        Assert.False(f.Api.OperatorRequestDone(original.Id, new(f.Estate.OperatorId, Link: "https://example.test/effect")).Ok);
        RecordEnvelope current = f.Estate.Load().ById[revised.Written!.Id];
        Assert.False(f.Api.OperatorRequestDone(current.Id, new(f.Estate.OperatorId)).Ok);
        JsonObject observation = RequestData.Observation(f.Estate);
        observation["asOf"] = current.At;
        int before = f.Estate.Load().Records.Count;
        WriteOutcome observed = f.Api.NewRecord(new("observation", f.Estate.OperatorId, JsonSerializer.SerializeToElement(observation), Subject(current)));
        Assert.True(observed.Ok, observed.Problem?.Message);
        Assert.Equal(before + 1, f.Estate.Load().Records.Count);
        Assert.Equal(0, f.Api.OperatorRequests().Json.GetProperty("waiting").GetInt32());
        Assert.Empty(f.Estate.Load().OfType("operation"));
        Assert.Equal(0, f.Api.Validate().Errors);
    }

    [Fact]
    public void CarryRetainsTheAskAndReplacesItsQueueEntryWithoutAnsweringItsDecisionQuestion()
    {
        using var f = new OperatorCloseFixture();
        RecordEnvelope work = RequestData.Work(f.Estate);
        RecordEnvelope ask = f.Estate.Add("ask", f.Estate.BuilderId, new JsonObject
        {
            ["to"] = f.Estate.OperatorId, ["question"] = "Publish the announcement?", ["recommendation"] = "Publish the supplied draft.", ["consequence"] = "The announcement waits.", ["state"] = "open",
        }.ToJsonString(), work.Reference);
        JsonObject body = RequestData.Body(f.Estate);
        var input = new OperatorRequestCarryRequest(f.Estate.OperatorId, ask.ComputedHash, JsonSerializer.SerializeToElement(body), Subject(work));
        WriteOutcome carried = f.Api.OperatorRequestCarry(ask.Id, input);
        Assert.True(carried.Ok, carried.Problem?.Message);
        EstateModel model = f.Estate.Load();
        RecordEnvelope request = model.ById[carried.Written!.Id];
        Assert.Equal(f.Estate.OperatorId, request.By);
        Assert.Equal(f.Estate.BuilderId, EstateModel.BodyString(request, "askedBy"));
        Assert.Equal(ask.ComputedHash, EstateModel.BodyString(request, "sourceAskHash"));
        Assert.Equal(ask.Body.GetRawText(), model.ById[ask.Id].Body.GetRawText());
        Assert.Equal("open", Asks.State(model, ask).State);
        Assert.Single(f.Api.OperatorActions().Json.GetProperty("items").EnumerateArray());
        Assert.Empty(f.Api.Asks().Json.GetProperty("principals").EnumerateArray());
        Assert.False(f.Api.OperatorRequestCarry(ask.Id, input).Ok);
        Assert.True(f.Api.OperatorRequestDismiss(request.Id, new(f.Estate.OperatorId, Reason: "Skip this announcement.")).Ok);
        Assert.Empty(f.Api.OperatorActions().Json.GetProperty("items").EnumerateArray());
        Assert.NotNull(f.Api.Record(ask.Id));
        Assert.Equal("open", Asks.State(f.Estate.Load(), ask).State);
    }

    [Fact]
    public void QueueStatusAttentionAndMcpShareAllOpenIdentitiesAndReadsWriteNothing()
    {
        using var f = new OperatorCloseFixture();
        List<RecordEnvelope> requests = Enumerable.Range(1, 5).Select(i => Request(f, i)).ToList();
        int before = f.Estate.Load().Records.Count;
        Assert.Equal(5, f.Api.OperatorRequests().Json.GetProperty("waiting").GetInt32());
        Assert.Equal(3, f.Api.OperatorRequests().Json.GetProperty("delivered").GetArrayLength());
        Assert.Equal(2, f.Api.OperatorRequests().Json.GetProperty("queued").GetArrayLength());
        Assert.All(requests, r => Assert.Contains(r.Id, f.Api.OperatorActions().Text, StringComparison.Ordinal));
        Assert.All(requests, r => Assert.Contains(r.Id, f.Api.Status().Text, StringComparison.Ordinal));
        Assert.DoesNotContain("Recommended action 4", f.Api.Status().Text, StringComparison.Ordinal);
        IReadOnlyList<CommunicationAttention> attention = CommunicationPresentation.Attention(f.Api, f.Estate.OperatorId);
        Assert.Equal(3, attention.Count(a => a.RecordType == "operator-request" && a.State == "open"));
        Assert.StartsWith("Recommendation:", attention.First(a => a.RecordType == "operator-request").Text, StringComparison.Ordinal);
        Assert.Contains(requests[4].Id, Assert.Single(attention, a => a.State == "queued").Text, StringComparison.Ordinal);
        var registry = new ToolRegistry(f.Api, null);
        Assert.False(Assert.Single(registry.All, t => t.Name == "requests_list").Writes);
        ToolCallResult result = registry.Call("requests_list", new JsonObject { ["form"] = "json" });
        Assert.False(result.IsError);
        Assert.All(requests, r => Assert.Contains(r.Id, result.Text, StringComparison.Ordinal));
        Assert.DoesNotContain(registry.All, t => t.Writes && t.Name.Contains("request", StringComparison.Ordinal));
        Assert.Equal(before, f.Estate.Load().Records.Count);
    }
}
