// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Vericairn.Protocol;
using Vericairn.Records.Estate;
using Vericairn.Records.Hashing;
using Vericairn.Records.Model;
using Vericairn.Records.Signing;
using Vericairn.Runtime.Service;

namespace Vericairn.Runtime.Tests;

/// <summary>
/// OPS-16 (0.9.4): the operator answers open asks with their recommendations in one act signed by their own device, each ask its own
/// decision naming the act, decidedAt the act's time; the act refuses whole when any ask or recommendation changed since it was shown, and
/// no agent performs it.
/// </summary>
public sealed class OperatorAnswerTests
{
    [Fact]
    public void TheListingShowsEachAskWaitingOnAnOperatorByItsRevisionWithItsRecommendationHash()
    {
        using var f = new OperatorCloseFixture();
        RecordEnvelope first = Ask(f, "First question?", "Take the first.");
        RecordEnvelope second = Ask(f, "Second question?", "Take the second.");
        // An ask to an agent is not the operator's.
        f.Estate.Add("ask", f.Estate.OperatorId, $$"""{"to":"{{f.Estate.BuilderId}}","question":"Builder?","recommendation":"Yes.","consequence":"Waits.","state":"open"}""");

        OperatorAnswerCandidatesResponse listed = f.Api.OperatorAnswerCandidates();

        Assert.True(listed.Ok);
        Assert.Equal([first.Id, second.Id], listed.Items.Select(i => i.Ask.Id));
        OperatorAnswerItem item = listed.Items[0];
        Assert.Equal((first.ComputedHash, f.Estate.OperatorId, "Take the first.", "First question?", "open"), (item.Ask.Hash, item.To, item.Recommendation, item.Question, item.State));
        Assert.Equal(ContentHash.Sha256(Encoding.UTF8.GetBytes("Take the first.")), item.RecommendationHash);
        Assert.Equal(item.Ask, item.ToSelection().Ask);
        Assert.Empty(f.Api.OperatorAnswerCandidates(f.Estate.BuilderId).Items);
    }

    [Fact]
    public void EachSelectedAskGetsItsOwnSignedDecisionNamingTheActAtTheActsTime()
    {
        using var f = new OperatorCloseFixture();
        RecordEnvelope first = Ask(f, "First question?", "Take the first.");
        RecordEnvelope second = Ask(f, "Second question?", "Take the second.");
        RecordEnvelope third = Ask(f, "Third question?", "Take the third.");
        OperatorAnswerCandidatesResponse listed = f.Api.OperatorAnswerCandidates(f.Estate.OperatorId);

        OperatorAnswerResponse answered = f.Api.OperatorAnswer(new(f.Estate.OperatorId, [listed.Items[0].ToSelection(), listed.Items[2].ToSelection()], Stake: "medium"),
            TestContext.Current.CancellationToken);

        Assert.True(answered.Ok, answered.Problem?.Message);
        Assert.NotNull(answered.Act);
        Assert.Equal(2, answered.Written.Count);
        EstateModel model = f.Estate.Load();
        Assert.False(model.Findings.HasErrors, string.Join("\n", model.Findings.Select(x => x.Message)));
        List<RecordEnvelope> decisions = [.. answered.Written.Select(w => model.ById[w.Id])];
        Assert.Equal([first.Id, third.Id], decisions.Select(d => d.Body.GetProperty("answers")[0].GetString()));
        foreach ((RecordEnvelope decision, RecordEnvelope ask) in decisions.Zip([first, third]))
        {
            OperatorAnswers.Act act = OperatorAnswers.Of(decision)!;
            Assert.Equal((answered.Act, ask.Id, ask.ComputedHash, OperatorAnswers.RecommendationHash(ask), 2L), (act.Id, act.Ask, act.Hash, act.Recommendation, act.Count));
            Assert.Equal(f.Estate.OperatorId, decision.By);
            Assert.Equal(f.Estate.OperatorId, EstateModel.BodyString(decision, "decidedBy"));
            Assert.Equal("medium", EstateModel.BodyString(decision, "stake"));
            Assert.Equal(OperatorAnswers.Statement, EstateModel.BodyString(decision, "statement"));
            Assert.Contains(EstateModel.BodyString(ask, "recommendation")!, EstateModel.BodyString(decision, "summary"), StringComparison.Ordinal);
            Assert.Equal(f.Device, Assert.Single(decision.Signatures).Device);
            Assert.Equal("answered", Asks.State(model, ask).State);
        }

        // One act, one time: every decision's decidedAt is the act's.
        Assert.Single(decisions.Select(d => EstateModel.BodyString(d, "decidedAt")).Distinct());
        Assert.Equal(second.Id, Assert.Single(f.Api.OperatorAnswerCandidates().Items).Ask.Id);
        Assert.Equal(0, f.Api.Validate().Errors);
    }

    [Fact]
    public void AllAnswersEveryAskOfTheOperatorAndRefusesWhenTheMembershipChanged()
    {
        using var f = new OperatorCloseFixture();
        Ask(f, "First question?", "Take the first.");
        Ask(f, "Second question?", "Take the second.");
        OperatorAnswerCandidatesResponse listed = f.Api.OperatorAnswerCandidates(f.Estate.OperatorId);
        Ask(f, "A third arrives?", "Take the third.");
        int before = f.Estate.Load().Records.Count;

        OperatorAnswerResponse refused = f.Api.OperatorAnswer(new(f.Estate.OperatorId, [.. listed.Items.Select(i => i.ToSelection())], All: true), TestContext.Current.CancellationToken);

        Assert.Equal("conflict", refused.Problem?.Code);
        Assert.Empty(refused.Written);
        Assert.Null(refused.Act);
        Assert.Equal(before, f.Estate.Load().Records.Count);
        OperatorAnswerResponse all = f.Api.OperatorAnswer(new(f.Estate.OperatorId, [.. f.Api.OperatorAnswerCandidates(f.Estate.OperatorId).Items.Select(i => i.ToSelection())], All: true),
            TestContext.Current.CancellationToken);
        Assert.True(all.Ok, all.Problem?.Message);
        Assert.Equal(3, all.Written.Count);
        Assert.Empty(f.Api.OperatorAnswerCandidates().Items);
    }

    [Theory]
    [InlineData("revised")]
    [InlineData("recommendation")]
    [InlineData("answered")]
    [InlineData("withdrawn")]
    public void AnAskOrRecommendationChangedSinceItWasShownRefusesTheWholeAct(string change)
    {
        using var f = new OperatorCloseFixture();
        RecordEnvelope first = Ask(f, "First question?", "Take the first.");
        RecordEnvelope second = Ask(f, "Second question?", "Take the second.");
        OperatorAnswerSelection[] selection = [.. f.Api.OperatorAnswerCandidates(f.Estate.OperatorId).Items.Select(i => i.ToSelection())];
        switch (change)
        {
            case "revised":
                f.Estate.Add("ask", second.By, Body(second, "state", "notified"), supersedes: second.Id);
                break;
            case "recommendation":
                selection[1] = selection[1] with { Recommendation = ContentHash.Sha256(Encoding.UTF8.GetBytes("Another recommendation.")) };
                break;
            case "answered":
                f.Estate.Add("decision", f.Estate.OperatorId, $$"""{"title":"Answer","statement":"No.","summary":"No.","synthesis":"No.","effects":["No."],"affects":[],"decidedBy":"{{f.Estate.OperatorId}}","decidedAt":"{{RecordParser.FormatTimestamp(f.Estate.Clock)}}","stake":"low","notAuthorized":[],"answers":["{{second.Id}}"]}""");
                break;
            default:
                f.Estate.Add("ask", second.By, Body(second, "state", "withdrawn"), supersedes: second.Id);
                break;
        }

        int before = f.Estate.Load().Records.Count;

        OperatorAnswerResponse refused = f.Api.OperatorAnswer(new(f.Estate.OperatorId, selection), TestContext.Current.CancellationToken);

        Assert.Equal("conflict", refused.Problem?.Code);
        Assert.Empty(refused.Written);
        Assert.Equal(before, f.Estate.Load().Records.Count);
        Assert.Equal("open", Asks.State(f.Estate.Load(), first).State);
    }

    [Fact]
    public void AnAgentNeverPerformsTheActAndNoOneAnswersAnotherOperatorsAsks()
    {
        using var f = new OperatorCloseFixture();
        Ask(f, "First question?", "Take the first.");
        OperatorAnswerSelection[] selection = [.. f.Api.OperatorAnswerCandidates().Items.Select(i => i.ToSelection())];

        OperatorAnswerResponse agent = f.Api.OperatorAnswer(new(f.Estate.BuilderId, selection), TestContext.Current.CancellationToken);

        Assert.Equal("denied", agent.Problem?.Code);
        Assert.Empty(f.Estate.Load().OfType("decision"));
        // Another person holding the operator role cannot answer what is put to the first.
        RecordEnvelope other = f.Estate.Add("principal", f.Estate.OperatorId, """{"kind":"human","name":"Second Operator","roles":["operator"]}""");
        OperatorAnswerResponse another = f.Api.OperatorAnswer(new(other.Id, selection), TestContext.Current.CancellationToken);
        Assert.False(another.Ok);
        Assert.Empty(f.Estate.Load().OfType("decision"));
    }

    [Fact]
    public void AHandWrittenDecisionOfAnActIsHeldToTheCurrentAskAndItsRecommendation()
    {
        using var f = new OperatorCloseFixture();
        RecordEnvelope ask = Ask(f, "First question?", "Take the first.");
        JsonObject body = OperatorAnswers.DecisionBody(ask, f.Estate.OperatorId, Records.Identity.Ulid.NewUlid().Value, RecordParser.FormatTimestamp(DateTimeOffset.UtcNow.AddSeconds(-5)), 1, "low");
        RecordEnvelope revised = f.Estate.Add("ask", ask.By, Body(ask, "state", "notified"), supersedes: ask.Id);

        WriteOutcome stale = f.Api.NewRecord(new("decision", f.Estate.OperatorId, JsonSerializer.SerializeToElement(body)));

        Assert.False(stale.Ok);
        Assert.Contains("changed since the act showed it", stale.Problem!.Message, StringComparison.Ordinal);
        JsonObject current = OperatorAnswers.DecisionBody(revised, f.Estate.OperatorId, Records.Identity.Ulid.NewUlid().Value, RecordParser.FormatTimestamp(DateTimeOffset.UtcNow.AddSeconds(-5)), 1, "low");
        WriteOutcome agent = f.Api.NewRecord(new("decision", f.Estate.BuilderId, JsonSerializer.SerializeToElement(current)));
        Assert.False(agent.Ok);
        Assert.True(f.Api.NewRecord(new("decision", f.Estate.OperatorId, JsonSerializer.SerializeToElement(current))).Ok);
    }

    [Fact]
    public void WithPresenceTheActsDecisionsAreListedByExactHashAndSignedUnderOneCheck()
    {
        using var f = new OperatorCloseFixture();
        for (int i = 0; i < 3; i++) Ask(f, $"Question {i}?", $"Take {i}.");
        var custody = new RecordingCustody(VericairnHome.Path());
        var helpers = new List<PresenceHelperStandIn>();
        f.Api.KeyStores = [custody];
        f.Api.PresenceRequired = device => device == f.Device;
        f.Api.HostsEmbedded = true;
        f.Api.PresenceHost = StandInSessions.Host(helpers);

        OperatorAnswerResponse answered = f.Api.OperatorAnswer(new(f.Estate.OperatorId, [.. f.Api.OperatorAnswerCandidates().Items.Select(i => i.ToSelection())], All: true),
            TestContext.Current.CancellationToken);

        Assert.True(answered.Ok, answered.Problem?.Message);
        PresenceHelperStandIn helper = Assert.Single(helpers);
        Assert.Empty(custody.Reasons);
        PresenceSessionRules terms = PresenceSessionRules.ReadTerms(Assert.Single(helper.Terms));
        Assert.Equal("decision from ask", terms.Action);
        Assert.Equal(3, terms.ExactCount);
        EstateModel model = f.Estate.Load();
        // The announced list is exactly what was signed and stored.
        Assert.Equal(terms.Entries.Select(e => e.Hash).Order(StringComparer.Ordinal), answered.Written.Select(w => model.ById[w.Id].ComputedHash).Order(StringComparer.Ordinal));
        Assert.All(terms.Entries, e => Assert.Equal("decision", e.Type));
    }

    public static RecordEnvelope Ask(OperatorCloseFixture f, string question, string recommendation) => f.Estate.Add("ask", f.Estate.BuilderId,
        JsonSerializer.Serialize(new { to = f.Estate.OperatorId, question, recommendation, consequence = "The work waits.", state = "open" }));

    private static string Body(RecordEnvelope record, string field, string value)
    {
        JsonObject body = JsonNode.Parse(record.Body.GetRawText())!.AsObject();
        body[field] = value;
        return body.ToJsonString();
    }
}
