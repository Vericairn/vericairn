// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using Vericairn.Records.Estate;
using Vericairn.Records.Model;
using Vericairn.Records.Tests;
using Vericairn.Remote;

namespace Vericairn.Remote.Tests;

/// <summary>
/// OPS-24 (0.9.4): Remote offers the same reviewed list as vericairn operator act --ready: each ready item with what its signature records;
/// the person leaves items waiting or dismisses a request, and the phone's presence key signs exactly the chosen records.
/// </summary>
public sealed class RemoteOperatorActTests(HeadlessFixture fixture)
{
    [Fact]
    public async Task TheReviewedListSignsTheChosenRecordsWithThePresenceKeyAndNothingElse()
    {
        using TestEstate estate = RemoteFixtures.Seeded(3);
        RecordEnvelope first = Ask(estate, "First?", "Take the first.");
        RecordEnvelope second = Ask(estate, "Second?", "Take the second.");
        RecordEnvelope request = Request(estate);
        using var connections = new FakeConnections(estate.Root) { EnableSignedWrites = true };
        await fixture.UiAsync(async () =>
        {
            RemoteViewModel model = await RemoteInspectionSeed.Pair(connections, new InspectionClock());
            await model.ReadActCommand.ExecuteAsync();
            Assert.Equal(["answer:" + first.Id, "answer:" + second.Id, "request:" + request.Id], model.ActChoices.Select(c => c.Item.Id));
            Assert.Equal(["sign", "wait", "dismiss"], model.ActChoices[2].Choices);
            Assert.Equal(["sign", "wait"], model.ActChoices[0].Choices);
            model.ActChoices[1].Choice = "wait";
            model.ActChoices[2].Choice = "dismiss";
            model.ActReviewed = true;
            Assert.False(model.SignActCommand.CanExecute(null));
            model.ActChoices[2].Reason = "No longer needed.";
            model.ActReviewed = true;
            Assert.True(model.SignActCommand.CanExecute(null));
            Assert.Contains("signs 1 and dismisses 1 of 3 ready items", model.ActReview, StringComparison.Ordinal);

            await model.SignActCommand.ExecuteAsync();

            EstateModel after = estate.Load();
            Assert.Equal(0, after.Findings.Errors);
            Assert.Equal(2, connections.SignedWrites);
            RecordEnvelope decision = Assert.Single(after.Current("decision"));
            Assert.Equal(first.Id, OperatorAnswers.Of(decision)!.Ask);
            RecordEnvelope close = Assert.Single(after.OfType("operator-request-close"));
            Assert.Equal(("dismissed", "No longer needed."), (EstateModel.BodyString(close, "outcome"), EstateModel.BodyString(close, "reason")));
            Assert.All(new[] { decision, close }, r => Assert.Equal(model.Selected!.Connection.PresenceDevice, Assert.Single(r.Signatures).Device));
            Assert.Equal("open", Asks.State(after, second).State);
            Assert.Equal(["answer:" + second.Id], model.ActChoices.Select(c => c.Item.Id));
            Assert.StartsWith("Act ", model.ActMessage, StringComparison.Ordinal);
            return true;
        });
    }

    [Fact]
    public async Task AnItemChangedSinceItWasShownRefusesTheActBeforeAnySignature()
    {
        using TestEstate estate = RemoteFixtures.Seeded(3);
        Ask(estate, "First?", "Take the first.");
        RecordEnvelope second = Ask(estate, "Second?", "Take the second.");
        using var connections = new FakeConnections(estate.Root) { EnableSignedWrites = true };
        await fixture.UiAsync(async () =>
        {
            RemoteViewModel model = await RemoteInspectionSeed.Pair(connections, new InspectionClock());
            await model.ReadActCommand.ExecuteAsync();
            model.ActChoices[1].Choice = "wait";
            model.ActReviewed = true;
            estate.Add("ask", estate.BuilderId, second.Body.GetRawText().Replace("\"open\"", "\"notified\"", StringComparison.Ordinal), supersedes: second.Id);

            await model.SignActCommand.ExecuteAsync();

            Assert.Equal(0, connections.PreparedCalls);
            Assert.Empty(estate.Load().Current("decision"));
            Assert.Contains("changed since the act showed it", model.ActMessage, StringComparison.Ordinal);
            return true;
        });
    }

    [Fact]
    public void AnAcceptanceStartsWaitingUntilThePersonSelectsItAndShowsItsReviews()
    {
        var review = new Protocol.OperatorActReview("01M7ZZZZZZZZZZZZZZZZZZZZZ1", "sha256:" + new string('b', 64), "01M7ZZZZZZZZZZZZZZZZZZZZZ2", "pass", "independent-agent", "sha256:" + new string('a', 64));
        var acceptance = new Protocol.OperatorActItem("close:STEP-1", "close", "op", "STEP-1 result", "an acceptance", "acceptance", "result",
            new("01M7ZZZZZZZZZZZZZZZZZZZZZ3", "sha256:" + new string('a', 64)), [new(review.Id, review.Hash)], false, false, Reviews: [review]);
        var choice = new RemoteActChoice(acceptance);

        Assert.Equal("wait", choice.Choice);
        Assert.Equal("sign", new RemoteActChoice(acceptance with { Id = "answer:x", Kind = "answer", RecordType = "decision" }).Choice);
        Assert.Contains($"Review {review.Id} at {review.Hash} by {review.By}: pass, independent-agent, of {review.SubjectHash}", choice.Label, StringComparison.Ordinal);
    }

    private static RecordEnvelope Ask(TestEstate estate, string question, string recommendation) => estate.Add("ask", estate.BuilderId,
        JsonSerializer.Serialize(new { to = estate.OperatorId, question, recommendation, consequence = "The work waits.", state = "open" }));

    private static RecordEnvelope Request(TestEstate estate) =>
        estate.Add("operator-request", estate.BuilderId, RequestBody(estate, 1).ToJsonString(), estate.Load().Current("work-order").Single().Reference);

    /// <summary>An action request put to the operator, as the record library's request fixtures write it.</summary>
    internal static System.Text.Json.Nodes.JsonObject RequestBody(TestEstate estate, int number) => new()
    {
        ["to"] = estate.OperatorId, ["askedBy"] = estate.BuilderId, ["action"] = "Perform action " + number,
        ["why"] = "Finish the requested work.", ["recommendation"] = "Recommended action " + number,
        ["consequence"] = "The work waits.", ["kind"] = "action", ["destination"] = "The work's destination",
        ["content"] = new System.Text.Json.Nodes.JsonArray(new System.Text.Json.Nodes.JsonObject
        {
            ["kind"] = "text", ["text"] = "Exact draft.", ["hash"] = Vericairn.Records.Hashing.ContentHash.Sha256(System.Text.Encoding.UTF8.GetBytes("Exact draft.")),
        }),
    };
}
