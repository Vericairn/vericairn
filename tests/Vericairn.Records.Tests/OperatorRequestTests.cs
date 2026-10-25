// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Vericairn.Records.Estate;
using Vericairn.Records.Hashing;
using Vericairn.Records.Model;

namespace Vericairn.Records.Tests;

public sealed class OperatorRequestTests
{
    public static JsonObject Body(TestEstate estate, int number = 1) => new()
    {
        ["to"] = estate.OperatorId, ["askedBy"] = estate.BuilderId, ["action"] = "Perform action " + number,
        ["why"] = "Finish the requested work.", ["recommendation"] = "Recommended action " + number,
        ["consequence"] = "The work waits.", ["kind"] = "action", ["destination"] = "The work's destination",
        ["content"] = new JsonArray(new JsonObject { ["kind"] = "text", ["text"] = "Exact draft.", ["hash"] = ContentHash.Sha256(Encoding.UTF8.GetBytes("Exact draft.")) }),
    };

    public static RecordEnvelope Work(TestEstate estate) => estate.Add("work-order", estate.OperatorId,
        """{"key":"REQUEST-1","kind":"ordinary","title":"Operator actions","intent":"Finish the work.","scope":["operator action"],"exitQuestion":"Done?","inputs":[],"deliverables":["result"]}""");

    public static JsonObject Words(TestEstate estate) => new()
    {
        ["by"] = estate.OperatorId, ["statement"] = "I have done it.", ["source"] = "Operator conversation, message 4",
        ["at"] = RecordParser.FormatTimestamp(estate.Clock),
    };

    public static JsonObject Observation(TestEstate estate) => new()
    {
        ["kind"] = "operator-request", ["asOf"] = RecordParser.FormatTimestamp(estate.Clock), ["summary"] = "The requested effect is present.",
        ["facts"] = new JsonObject { ["outcome"] = "done", ["destination"] = "The work's destination", ["location"] = "https://example.test/announcement", ["evidenceHash"] = ContentHash.Sha256(Encoding.UTF8.GetBytes("Observed effect.")) },
        ["limitations"] = new JsonArray(),
    };

    // Additive coverage of every format-3 request field, leaving the existing signed policy golden bytes intact.
    public static PolicyFixture RecordedFixture()
    {
        var f = new PolicyFixture();
        RecordEnvelope work = Work(f.Estate);
        RecordEnvelope ask = f.Add("ask", new JsonObject
        {
            ["to"] = f.Estate.OperatorId, ["question"] = "Perform the action?", ["recommendation"] = "Perform it.", ["consequence"] = "Work waits.", ["state"] = "open",
        }, builder: true, subject: work.Reference);
        JsonObject body = Body(f.Estate);
        body["sourceAsk"] = ask.Id;
        body["sourceAskHash"] = ask.ComputedHash;
        body["content"]!.AsArray().Add(new JsonObject { ["kind"] = "file", ["name"] = "announcement.md", ["hash"] = ContentHash.Sha256(Encoding.UTF8.GetBytes("File bytes.")) });
        RecordEnvelope request = f.Add("operator-request", body, builder: true, subject: work.Reference);
        f.Add("operator-request-close", new JsonObject
        {
            ["outcome"] = "done", ["reason"] = "The operator reported completion.", ["link"] = "https://example.test/announcement",
            ["evidence"] = new JsonArray(new JsonObject { ["summary"] = "Observed announcement", ["hash"] = ContentHash.Sha256(Encoding.UTF8.GetBytes("Observed effect.")), ["location"] = "https://example.test/announcement" }),
            ["operatorWords"] = Words(f.Estate),
        }, builder: true, subject: request.Reference);
        RecordEnvelope observed = f.Add("operator-request", Body(f.Estate, 2), builder: true, subject: work.Reference);
        f.Add("observation", Observation(f.Estate), builder: true, subject: observed.Reference);
        return f;
    }

    [Fact]
    public void CompleteSignedRequestsAndTheirCompanionsStayInFormatThree()
    {
        using PolicyFixture f = RecordedFixture();
        EstateModel model = f.Estate.Load();
        Assert.False(model.Findings.HasErrors, string.Join('\n', model.Findings));
        Assert.Empty(OperatorRequests.Open(model));
        Assert.Equal("open", Asks.State(model, Assert.Single(model.Current("ask"))).State);
        Assert.Empty(Asks.Deliveries(model));
        Assert.All(model.OfType("operator-request-close"), r => Assert.True(SignatureChecks.IsAuthentic(model, r)));
        Assert.Contains("operator-request", RecordCompanions.Types);
        Assert.Contains("operator-request-close", RecordCompanions.Types);
        Assert.All(model.OfType("operator-request"), r => Assert.Equal(3, r.Format));
    }

    [Fact]
    public void EveryRequestIsListedInOriginalOrderWithThreeDeliveredAndTheRecommendationFirst()
    {
        using var f = new PolicyFixture();
        RecordEnvelope work = Work(f.Estate);
        List<RecordEnvelope> requests = Enumerable.Range(1, 5).Select(i => f.Add("operator-request", Body(f.Estate, i), builder: true, subject: work.Reference)).ToList();
        JsonObject revision = Body(f.Estate);
        revision["why"] = "The draft was clarified.";
        RecordEnvelope revised = f.Add("operator-request", revision, builder: true, subject: work.Reference, supersedes: requests[0].Id);
        EstateModel model = f.Estate.Load();
        Assert.False(model.Findings.HasErrors, string.Join('\n', model.Findings));
        JsonObject queue = OperatorRequests.Derive(model);
        Assert.Equal(3, queue["delivered"]!.AsArray().Count);
        Assert.Equal(2, queue["queued"]!.AsArray().Count);
        Assert.Equal(revised.Id, queue["delivered"]![0]!["revision"]!.GetValue<string>());
        string text = OperatorRequests.RenderText(model);
        Assert.True(text.IndexOf("Recommendation:", StringComparison.Ordinal) < text.IndexOf("Action:", StringComparison.Ordinal));
        Assert.DoesNotContain("Recommended action 4", text, StringComparison.Ordinal);
        Assert.All(requests.Skip(1), r => Assert.Contains(r.Id, text, StringComparison.Ordinal));
        JsonObject actions = OperatorActions.Derive(model);
        Assert.Equal(5, actions["items"]!.AsArray().Count);
        Assert.DoesNotContain("Recommended action 4", OperatorActions.RenderText(actions), StringComparison.Ordinal);
        f.Add("operator-request-close", new JsonObject { ["outcome"] = "dismissed", ["reason"] = "No longer needed." }, subject: revised.Reference);
        model = f.Estate.Load();
        Assert.False(model.Findings.HasErrors, string.Join('\n', model.Findings));
        Assert.Contains("Recommended action 4", OperatorRequests.RenderText(model), StringComparison.Ordinal);
        Assert.Equal(4, OperatorRequests.Open(model).Count);
        Assert.Equal(requests[0].Body.GetRawText(), model.ById[requests[0].Id].Body.GetRawText());
    }

    [Theory]
    [InlineData("content", "operator-request.content")]
    [InlineData("to", "operator-request.to")]
    [InlineData("askedBy", "operator-request.requester")]
    [InlineData("unknown", "body.unknown-key")]
    public void InvalidDraftsAreRefused(string change, string code)
    {
        using var f = new PolicyFixture();
        JsonObject body = Body(f.Estate);
        if (change == "content") body["content"]![0]!["text"] = "Changed without rehashing.";
        else if (change == "to") body["to"] = f.Estate.BuilderId;
        else if (change == "askedBy") body["askedBy"] = f.Estate.OperatorId;
        else body["unknown"] = "Unrecognized";
        f.Add("operator-request", body, builder: true, subject: Work(f.Estate).Reference);
        Assert.Contains(f.Estate.Load().Findings, finding => finding.Code == code);
    }

    [Theory]
    [InlineData("unsigned", "signature.required")]
    [InlineData("missing-words", "body.missing")]
    [InlineData("blank-words", "operator-request.words")]
    [InlineData("wrong-operator", "operator-request.words")]
    [InlineData("future-words", "operator-request.words")]
    [InlineData("dismissal-reason", "body.missing")]
    [InlineData("publication-link", "body.missing")]
    [InlineData("duplicate", "operator-request.closed")]
    [InlineData("revision-after-close", "operator-request.closed")]
    public void InvalidClosesCannotEstablishTheRequiredOutcome(string change, string code)
    {
        using var f = new PolicyFixture();
        RecordEnvelope work = Work(f.Estate);
        JsonObject draft = Body(f.Estate);
        if (change == "publication-link") draft["kind"] = "publication";
        RecordEnvelope request = f.Add("operator-request", draft, builder: true, subject: work.Reference);
        if (change == "publication-link")
        {
            EstateModel model = f.Estate.Load();
            Assert.Contains("--link <observed-destination>", OperatorRequests.RenderText(model), StringComparison.Ordinal);
            Assert.Contains("--link <observed-destination>", OperatorActions.RenderText(OperatorActions.Derive(model)), StringComparison.Ordinal);
        }
        JsonObject close = new() { ["outcome"] = change == "dismissal-reason" ? "dismissed" : "done" };
        bool agent = change is "missing-words" or "blank-words" or "wrong-operator" or "future-words";
        if (agent && change != "missing-words")
        {
            close["operatorWords"] = Words(f.Estate);
            if (change == "blank-words") close["operatorWords"]!["statement"] = "   ";
            else close["operatorWords"]![change == "wrong-operator" ? "by" : "at"] = change == "wrong-operator" ? f.Estate.BuilderId : "2099-01-01T00:00:00Z";
        }
        f.Add("operator-request-close", close, builder: agent, subject: request.Reference, sign: change != "unsigned");
        if (change == "duplicate") f.Add("operator-request-close", close, subject: request.Reference);
        if (change == "revision-after-close") f.Add("operator-request", draft, builder: true, subject: work.Reference, supersedes: request.Id);
        Assert.Contains(f.Estate.Load().Findings, finding => finding.Code == code);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void CarryPinsTheAskThatWasCurrentWhenTheRequestWasRecorded(bool revisedBeforeCarry)
    {
        using var f = new PolicyFixture();
        RecordEnvelope work = Work(f.Estate);
        JsonObject askBody = new()
        {
            ["to"] = f.Estate.OperatorId, ["question"] = "Perform the action?", ["recommendation"] = "Perform it.",
            ["consequence"] = "Work waits.", ["state"] = "open",
        };
        RecordEnvelope ask = f.Add("ask", askBody, builder: true, subject: work.Reference);
        if (revisedBeforeCarry) f.Add("ask", askBody, builder: true, subject: work.Reference, supersedes: ask.Id);
        JsonObject body = Body(f.Estate);
        body["sourceAsk"] = ask.Id;
        body["sourceAskHash"] = ask.ComputedHash;
        f.Add("operator-request", body, builder: true, subject: work.Reference);
        if (!revisedBeforeCarry) f.Add("ask", askBody, builder: true, subject: work.Reference, supersedes: ask.Id);
        EstateModel model = f.Estate.Load();
        if (revisedBeforeCarry) Assert.Contains(model.Findings, finding => finding.Code == "operator-request.source");
        else Assert.False(model.Findings.HasErrors, string.Join('\n', model.Findings));
    }

    [Theory]
    [InlineData("destination", "operator-request.observation")]
    [InlineData("before-intent", "operator-request.observation")]
    [InlineData("future", "observation.future")]
    [InlineData("missing-proof", "body.missing")]
    [InlineData("stale-draft", "operator-request.target")]
    public void AnObservationMustProveTheExactRequestedEffectAfterItsIntent(string change, string code)
    {
        using var f = new PolicyFixture();
        RecordEnvelope work = Work(f.Estate);
        RecordEnvelope request = f.Add("operator-request", Body(f.Estate), builder: true, subject: work.Reference);
        JsonObject observation = Observation(f.Estate);
        if (change == "destination") observation["facts"]!["destination"] = "Another destination";
        if (change == "before-intent") observation["asOf"] = "2020-01-01T00:00:00Z";
        if (change == "future") observation["asOf"] = "2099-01-01T00:00:00Z";
        if (change == "missing-proof") observation["facts"]!.AsObject().Remove("evidenceHash");
        if (change == "stale-draft") f.Add("operator-request", Body(f.Estate), builder: true, subject: work.Reference, supersedes: request.Id);
        f.Add("observation", observation, builder: true, subject: request.Reference);
        Assert.Contains(f.Estate.Load().Findings, finding => finding.Code == code);
    }
}
