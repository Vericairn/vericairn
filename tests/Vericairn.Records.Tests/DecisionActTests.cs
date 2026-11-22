// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json.Nodes;
using Vericairn.Records.Estate;
using Vericairn.Records.Identity;
using Vericairn.Records.Model;

namespace Vericairn.Records.Tests;

/// <summary>
/// OPS-16 (0.9.4): a decision's act member at record format 3. It pins the exact ask revision its answers name and that revision's
/// recommendation; only the operator who decides, a person, records it, signed; it takes the recommendation and nothing more; and the
/// decisions of one act agree on who decided, when and how many asks it answered.
/// </summary>
public sealed class DecisionActTests
{
    [Fact]
    public void AnActsDecisionsPinTheirAsksAndRecommendationsAndValidate()
    {
        using var f = new PolicyFixture();
        RecordEnvelope first = Ask(f, "First?", "Take the first.");
        RecordEnvelope second = Ask(f, "Second?", "Take the second.");
        string act = Ulid.NewUlid().Value;
        string at = RecordParser.FormatTimestamp(f.Estate.Clock);
        f.Add("decision", OperatorAnswers.DecisionBody(first, f.Estate.OperatorId, act, at, 2, "low"));
        f.Add("decision", OperatorAnswers.DecisionBody(second, f.Estate.OperatorId, act, at, 2, "low"));

        EstateModel model = f.Estate.Load();

        Assert.False(model.Findings.HasErrors, string.Join("\n", model.Findings.Select(x => x.Code + ": " + x.Message)));
        Assert.Equal("answered", Asks.State(model, first).State);
        Assert.Equal("answered", Asks.State(model, second).State);
    }

    [Theory]
    [InlineData("ask hash")]
    [InlineData("recommendation")]
    [InlineData("answers")]
    [InlineData("agent")]
    [InlineData("unsigned")]
    [InlineData("documents")]
    [InlineData("obligations")]
    [InlineData("count")]
    [InlineData("time")]
    [InlineData("twice")]
    public void AnActsDecisionThatBreaksItsRulesIsRefused(string broken)
    {
        using var f = new PolicyFixture();
        RecordEnvelope first = Ask(f, "First?", "Take the first.");
        RecordEnvelope second = Ask(f, "Second?", "Take the second.");
        string act = Ulid.NewUlid().Value;
        string at = RecordParser.FormatTimestamp(f.Estate.Clock);
        JsonObject body = OperatorAnswers.DecisionBody(first, f.Estate.OperatorId, act, at, 1, "low");
        bool builder = false, sign = true;
        switch (broken)
        {
            case "ask hash": body["act"]!["hash"] = second.ComputedHash; break;
            case "recommendation": body["act"]!["recommendation"] = OperatorAnswers.RecommendationHash(second); break;
            case "answers": body["answers"] = new JsonArray(second.Id); break;
            case "agent": builder = true; break;
            case "unsigned": sign = false; break;
            case "documents": body["documents"] = new JsonArray("design/plan.md"); break;
            case "obligations": body["obligations"] = new JsonArray(new JsonObject { ["key"] = "owed", ["summary"] = "Owed.", ["owner"] = f.Estate.OperatorId, ["resolution"] = "action", ["discharge"] = "Done." }); break;
            case "count":
                f.Add("decision", OperatorAnswers.DecisionBody(second, f.Estate.OperatorId, act, at, 1, "low"));
                break;
            case "time":
                f.Add("decision", OperatorAnswers.DecisionBody(second, f.Estate.OperatorId, act, RecordParser.FormatTimestamp(f.Estate.Clock.AddSeconds(-30)), 2, "low"));
                body = OperatorAnswers.DecisionBody(first, f.Estate.OperatorId, act, at, 2, "low");
                break;
            default:
                f.Add("decision", OperatorAnswers.DecisionBody(first, f.Estate.OperatorId, act, at, 2, "low"));
                body = OperatorAnswers.DecisionBody(first, f.Estate.OperatorId, act, at, 2, "low");
                break;
        }

        if (builder) body["decidedBy"] = f.Estate.OperatorId;
        RecordEnvelope decision = f.Add("decision", body, builder: builder, sign: sign);

        EstateModel model = f.Estate.Load();

        Assert.Contains(model.Findings, x => x.Severity == Severity.Error && x.Location.StartsWith(decision.ExpectedPath, StringComparison.Ordinal)
            && x.Code is "decision.act" or "signature.required");
    }

    [Fact]
    public void AFormatTwoDecisionCannotNameAnAct()
    {
        using var f = new PolicyFixture(2);
        RecordEnvelope ask = Ask(f, "First?", "Take the first.");
        RecordEnvelope decision = f.Add("decision", OperatorAnswers.DecisionBody(ask, f.Estate.OperatorId, Ulid.NewUlid().Value, RecordParser.FormatTimestamp(f.Estate.Clock), 1, "low"));

        Assert.Contains(f.Estate.Load().Findings, x => x.Code == "body.format" && x.Location.StartsWith(decision.ExpectedPath, StringComparison.Ordinal));
    }

    /// <summary>Adds an ask and the decision of an act answering it with its recommendation, for the format-3 round trip of every field.</summary>
    public static RecordEnvelope AddAct(PolicyFixture f)
    {
        RecordEnvelope ask = Ask(f, "Answer in one act?", "Take the recommendation.");
        return f.Add("decision", OperatorAnswers.DecisionBody(ask, f.Estate.OperatorId, Ulid.NewUlid(f.Estate.Clock).Value, RecordParser.FormatTimestamp(f.Estate.Clock), 1, "low"));
    }

    private static RecordEnvelope Ask(PolicyFixture f, string question, string recommendation) => f.Add("ask", new JsonObject
    {
        ["to"] = f.Estate.OperatorId, ["question"] = question, ["recommendation"] = recommendation, ["consequence"] = "Work waits.", ["state"] = "open",
    }, builder: true);
}
