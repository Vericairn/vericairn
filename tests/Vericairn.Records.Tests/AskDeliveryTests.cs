// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using Vericairn.Records.Estate;
using Vericairn.Records.Model;

namespace Vericairn.Records.Tests;

/// <summary>
/// What waits on a principal (the estate's design/goals.md § 8, design/cohesion-contract.md § 5): the asks still open or notified,
/// oldest first, at most three in one delivery, each with its recommendation first and the consequence of leaving it unanswered, and
/// the rest waiting in order behind them; an answered or withdrawn ask waits on no one, and reading what waits answers nothing.
/// </summary>
public class AskDeliveryTests
{
    [Fact]
    public void AtMostThreeAsksGoOutPerDeliveryWithTheRecommendationFirstAndTheRestWaitInOrder()
    {
        using var estate = new TestEstate(recordFormat: 2);
        string op = estate.OperatorId;
        RecordEnvelope first = estate.Add("ask", estate.BuilderId, Ask(op, "First?", "open", ""","options":["yes","no"]"""));
        RecordEnvelope second = estate.Add("ask", estate.BuilderId, Ask(op, "Second?", "open"));
        RecordEnvelope third = estate.Add("ask", estate.BuilderId, Ask(op, "Third?", "open"));
        RecordEnvelope answered = estate.Add("ask", estate.BuilderId, Ask(op, "Answered?", "open"));
        estate.Add("decision", estate.BuilderId, Answer(op, answered.Id));
        RecordEnvelope withdrawn = estate.Add("ask", estate.BuilderId, Ask(op, "Withdrawn?", "open"));
        estate.Add("ask", estate.BuilderId, Ask(op, "Withdrawn?", "withdrawn"), supersedes: withdrawn.Id);
        RecordEnvelope fourth = estate.Add("ask", estate.BuilderId, Ask(op, "Fourth?", "open"));
        RecordEnvelope fifth = estate.Add("ask", estate.BuilderId, Ask(op, "Fifth?", "open"));
        estate.Add("ask", estate.BuilderId, Ask(op, "Fifth?", "notified"), supersedes: fifth.Id);
        RecordEnvelope toBuilder = estate.Add("ask", op, Ask(estate.BuilderId, "For the builder?", "open"));
        EstateModel model = estate.Load();
        Assert.False(model.Findings.HasErrors, string.Join("\n", model.Findings));

        Asks.Delivery delivery = Assert.Single(Asks.Deliveries(model, op));
        Assert.Equal([first.Id, second.Id, third.Id], delivery.Delivered.Select(a => model.Chain(a)[0]));
        Assert.Equal([fourth.Id, fifth.Id], delivery.Queued.Select(a => model.Chain(a)[0]));
        Assert.Equal(2, Asks.Deliveries(model).Count);

        string text = Asks.RenderText(model, op);
        Assert.StartsWith($"What waits on Test Operator [principal {op}]: 5 asks, 3 in this delivery and the rest in order behind them\n  1. Recommendation: Answer First? yes.\n     Asked: First? [ask {first.Id}], by Test Builder, open\n     If unanswered: First? waits.\n     Options: yes; no\n  2. Recommendation:", text, StringComparison.Ordinal);
        Assert.Contains($"  Waiting in order: 2 more asks [ask {fourth.Id}, ask {fifth.Id}]; each follows as those before it are answered or withdrawn\n", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Fourth?", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Answered?", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Withdrawn?", text, StringComparison.Ordinal);
        Assert.EndsWith("An ask is answered by a decision of the principal it is put to that names it in its answers; reading it, waiting and dismissing it answer nothing.\n", text, StringComparison.Ordinal);
        Assert.Contains($"What waits on Test Builder [principal {estate.BuilderId}]: 1 ask\n  1. Recommendation: Answer For the builder? yes.\n     Asked: For the builder? [ask {toBuilder.Id}]", Asks.RenderText(model), StringComparison.Ordinal);

        using (JsonDocument json = JsonDocument.Parse(Asks.RenderJson(model, op)))
        {
            JsonElement principal = Assert.Single(json.RootElement.GetProperty("principals").EnumerateArray());
            Assert.Equal((3, 5, true), (json.RootElement.GetProperty("perDelivery").GetInt32(), principal.GetProperty("waiting").GetInt32(), principal.GetProperty("operator").GetBoolean()));
            Assert.Equal([1, 2, 3], principal.GetProperty("delivered").EnumerateArray().Select(a => a.GetProperty("position").GetInt32()));
            Assert.Equal("Answer First? yes.", principal.GetProperty("delivered")[0].GetProperty("recommendation").GetString());
            Assert.Equal([(fourth.Id, 4), (fifth.Id, 5)], principal.GetProperty("queued").EnumerateArray().Select(q => (q.GetProperty("id").GetString(), q.GetProperty("position").GetInt32())));
        }

        // Answering the first brings the fourth into the delivery; the fifth, notified, still waits behind it.
        estate.Add("decision", estate.BuilderId, Answer(op, first.Id));
        model = estate.Load();
        Asks.Delivery next = Assert.Single(Asks.Deliveries(model, op));
        Assert.Equal([second.Id, third.Id, fourth.Id], next.Delivered.Select(a => model.Chain(a)[0]));
        Assert.Equal([fifth.Id], next.Queued.Select(a => model.Chain(a)[0]));

        // Withdrawn by its asker, the builder's ask waits on no one.
        estate.Add("ask", op, Ask(estate.BuilderId, "For the builder?", "withdrawn"), supersedes: toBuilder.Id);
        Assert.Equal($"Nothing waits on Test Builder [principal {estate.BuilderId}]: no ask put to it is open or notified.\n", Asks.RenderText(estate.Load(), estate.BuilderId));
    }

    private static string Ask(string to, string question, string state, string extra = "") =>
        $$"""{"to":"{{to}}","question":"{{question}}","recommendation":"Answer {{question}} yes.","consequence":"{{question}} waits.","state":"{{state}}"{{extra}}}""";

    private static string Answer(string op, string ask) =>
        $$"""{"title":"An answer","statement":"Yes.","summary":"Yes.","synthesis":"The operator answers.","effects":["the work goes on"],"affects":[],"decidedBy":"{{op}}","decidedAt":"2026-10-13T12:00:00Z","stake":"low","notAuthorized":[],"answers":["{{ask}}"]}""";
}
