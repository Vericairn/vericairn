// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using Vericairn.Records.Estate;
using Vericairn.Records.Model;
using Vericairn.Records.Signing;
using Vericairn.Records.Tests;
using Vericairn.Remote;

namespace Vericairn.Remote.Tests;

/// <summary>
/// OPS-16 (0.9.4): from Remote the operator answers the asks waiting on them with their recommendations in one act, each its own decision
/// naming the act and signed by the phone's presence key; a changed ask refuses the act before any signature and stops it before the next.
/// </summary>
public sealed class RemoteAnswerActTests(HeadlessFixture fixture)
{
    [Fact]
    public async Task TheActAnswersTheSelectedAsksEachWithItsOwnPresenceSignedDecision()
    {
        using TestEstate estate = RemoteFixtures.Seeded(3);
        RecordEnvelope first = Ask(estate, "First?", "Take the first.");
        RecordEnvelope second = Ask(estate, "Second?", "Take the second.");
        RecordEnvelope third = Ask(estate, "Third?", "Take the third.");
        using var connections = new FakeConnections(estate.Root) { EnableSignedWrites = true };
        await fixture.UiAsync(async () =>
        {
            RemoteViewModel model = await RemoteInspectionSeed.Pair(connections, new InspectionClock());
            Assert.False(model.SignAnswersCommand.CanExecute(null));
            await model.ReadAnswersCommand.ExecuteAsync();
            Assert.Equal([first.Id, second.Id, third.Id], model.AnswerChoices.Select(c => c.Item.Ask.Id));
            Assert.StartsWith($"Ask {first.Id} at {first.ComputedHash}\nRecommendation {OperatorAnswers.RecommendationHash(first)}: Take the first.", model.AnswerChoices[0].Label, StringComparison.Ordinal);
            model.AnswerChoices[1].Selected = false;
            Assert.False(model.SignAnswersCommand.CanExecute(null));
            Assert.Contains("One act answers 2 asks", model.AnswerReview, StringComparison.Ordinal);
            Assert.DoesNotContain(second.Id, model.AnswerReview, StringComparison.Ordinal);
            model.AnswersReviewed = true;
            Assert.True(model.SignAnswersCommand.CanExecute(null));

            await model.SignAnswersCommand.ExecuteAsync();

            EstateModel after = estate.Load();
            Assert.Equal(0, after.Findings.Errors);
            List<RecordEnvelope> decisions = [.. after.Current("decision")];
            Assert.Equal(2, decisions.Count);
            Assert.Equal(new[] { first.Id, third.Id }.Order(StringComparer.Ordinal), decisions.Select(d => OperatorAnswers.Of(d)!.Ask).Order(StringComparer.Ordinal));
            Assert.Single(decisions.Select(d => OperatorAnswers.Of(d)!.Id).Distinct());
            Assert.Single(decisions.Select(d => EstateModel.BodyString(d, "decidedAt")).Distinct());
            Assert.All(decisions, d => Assert.Equal(model.Selected!.Connection.PresenceDevice, Assert.Single(d.Signatures).Device));
            Assert.All(decisions, d => Assert.True(RecordSigning.Verify(model.Selected!.Connection.PresenceDevicePublicKey!, RecordSigning.Digest(d.ComputedHash), d.Signatures[0].Signature)));
            Assert.Equal(2, model.AnswerReceipts.Count);
            Assert.StartsWith("Act ", model.AnswerMessage, StringComparison.Ordinal);
            Assert.Equal(second.Id, Assert.Single(model.AnswerChoices).Item.Ask.Id);
            Assert.Equal("open", Asks.State(after, second).State);
            return true;
        });
    }

    [Fact]
    public async Task AnAskChangedSinceItWasShownRefusesTheActBeforeAnySignature()
    {
        using TestEstate estate = RemoteFixtures.Seeded(3);
        Ask(estate, "First?", "Take the first.");
        RecordEnvelope second = Ask(estate, "Second?", "Take the second.");
        using var connections = new FakeConnections(estate.Root) { EnableSignedWrites = true };
        await fixture.UiAsync(async () =>
        {
            RemoteViewModel model = await RemoteInspectionSeed.Pair(connections, new InspectionClock());
            await model.ReadAnswersCommand.ExecuteAsync();
            model.AnswersReviewed = true;
            estate.Add("ask", estate.BuilderId, second.Body.GetRawText().Replace("\"open\"", "\"notified\"", StringComparison.Ordinal), supersedes: second.Id);

            await model.SignAnswersCommand.ExecuteAsync();

            Assert.Equal(0, connections.PreparedCalls);
            Assert.Empty(estate.Load().Current("decision"));
            Assert.Contains("changed since the act showed it", model.AnswerMessage, StringComparison.Ordinal);
            Assert.False(model.AnswersReviewed);
            Assert.False(model.SignAnswersCommand.CanExecute(null));
            return true;
        });
    }

    [Fact]
    public async Task AChangeDuringTheActStopsItBeforeTheNextSignature()
    {
        using TestEstate estate = RemoteFixtures.Seeded(3);
        Ask(estate, "First?", "Take the first.");
        RecordEnvelope second = Ask(estate, "Second?", "Take the second.");
        using var connections = new FakeConnections(estate.Root) { EnableSignedWrites = true };
        await fixture.UiAsync(async () =>
        {
            RemoteViewModel model = await RemoteInspectionSeed.Pair(connections, new InspectionClock());
            await model.ReadAnswersCommand.ExecuteAsync();
            model.AnswersReviewed = true;
            bool changed = false;
            connections.AfterWrite = () =>
            {
                if (changed) return;
                changed = true;
                estate.Add("ask", estate.BuilderId, second.Body.GetRawText().Replace("\"open\"", "\"notified\"", StringComparison.Ordinal), supersedes: second.Id);
            };

            await model.SignAnswersCommand.ExecuteAsync();

            Assert.Equal(1, connections.SignedWrites);
            RecordEnvelope written = Assert.Single(estate.Load().Current("decision"));
            // The count is how many asks the act set out to answer, as its synthesis says; fewer decisions name the act after a stop.
            Assert.Equal(2, OperatorAnswers.Of(written)!.Count);
            Assert.Contains("in one signed act that set out to answer 2 asks", EstateModel.BodyString(written, "synthesis"), StringComparison.Ordinal);
            Assert.Contains("stopped after 1 of 2 decisions", model.AnswerMessage, StringComparison.Ordinal);
            Assert.Single(model.AnswerReceipts);
            return true;
        });
    }

    [Fact]
    public async Task APhoneWithoutItsPresenceKeySignsNoAct()
    {
        using TestEstate estate = RemoteFixtures.Seeded(3);
        Ask(estate, "First?", "Take the first.");
        using var connections = new FakeConnections(estate.Root) { EnableSignedWrites = true, WithoutPresenceKey = true };
        await fixture.UiAsync(async () =>
        {
            RemoteViewModel model = await RemoteInspectionSeed.Pair(connections, new InspectionClock());
            await model.ReadAnswersCommand.ExecuteAsync();
            model.AnswersReviewed = true;
            Assert.False(model.SignAnswersCommand.CanExecute(null));
            await model.SignAnswersCommand.ExecuteAsync();
            Assert.Empty(estate.Load().Current("decision"));
            return true;
        });
    }

    private static RecordEnvelope Ask(TestEstate estate, string question, string recommendation) => estate.Add("ask", estate.BuilderId,
        JsonSerializer.Serialize(new { to = estate.OperatorId, question, recommendation, consequence = "The work waits.", state = "open" }));
}
