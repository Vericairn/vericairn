// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text;
using System.Text.Json;
using Vericairn.Records.Hashing;
using Vericairn.Records.Model;
using Vericairn.Records.Types;

namespace Vericairn.Records.Estate;

/// <summary>
/// From 0.9.4 (OPS-16, decision 01M76DVQX85PAB91GNP4SZQA9J): answering open asks with their recommendations in one signed act. The operator
/// answers a selection of the asks waiting on them, or all of them, each with its recommendation; every ask gets its own decision, which
/// names the act in its <c>act</c> member: the act's identity, the exact ask revision it answers, the SHA-256 of that revision's
/// recommendation and how many asks the act answered, with <c>decidedAt</c> the act's time. Only the operator the ask is put to, a person,
/// records an act's decision, signed by their own device; it takes the recommendation and nothing more.
/// </summary>
public static class OperatorAnswers
{
    /// <summary>The most asks one act answers.</summary>
    public const int MaxAsks = 256;

    /// <summary>The statement every decision of an act records, the operator's words as the act gives them.</summary>
    public const string Statement = "Take the recommendation: answered in one signed act after inspecting the exact ask and its recommendation.";

    /// <summary>What a decision of an act states it does not authorize.</summary>
    public const string NotAuthorized = "Anything the recommendation does not state, or a recommendation that changed after the operator saw it.";

    /// <summary>The members a decision of an act never carries: it takes the recommendation, and changes no document, policy, release review or prompt.</summary>
    internal static readonly string[] Excluded = ["answer", "changeSet", "documents", "obligations", "policies", "releaseReview", "promptUse"];

    /// <summary>The SHA-256 of an ask revision's recommendation as UTF-8, which an act names; null when it states none.</summary>
    public static string? RecommendationHash(RecordEnvelope ask)
    {
        ArgumentNullException.ThrowIfNull(ask);
        return EstateModel.BodyString(ask, "recommendation") is string recommendation ? ContentHash.Sha256(Encoding.UTF8.GetBytes(recommendation)) : null;
    }

    /// <summary>The act a decision names, or null when it names none.</summary>
    /// <param name="Id">The act's identity, which every decision it records names.</param>
    /// <param name="Ask">The exact ask revision the decision answers.</param>
    /// <param name="Hash">That revision's content hash.</param>
    /// <param name="Recommendation">The SHA-256 of that revision's recommendation.</param>
    /// <param name="Count">How many asks the act answered.</param>
    public sealed record Act(string Id, string Ask, string Hash, string Recommendation, long Count);

    /// <summary>The act a decision names, read without validating it; null when it names none or the member is malformed.</summary>
    public static Act? Of(RecordEnvelope decision)
    {
        ArgumentNullException.ThrowIfNull(decision);
        if (decision.Type != "decision" || decision.Body.ValueKind != JsonValueKind.Object || !decision.Body.TryGetProperty("act", out JsonElement act) || act.ValueKind != JsonValueKind.Object)
            return null;
        return Text(act, "id") is string id && Text(act, "ask") is string ask && Text(act, "hash") is string hash && Text(act, "recommendation") is string recommendation
            && act.TryGetProperty("count", out JsonElement count) && count.ValueKind == JsonValueKind.Number && count.TryGetInt64(out long value)
            ? new Act(id, ask, hash, recommendation, value) : null;
    }

    /// <summary>
    /// The asks an act may answer now: every current ask waiting on an operator, or on the one named, as the operator queue lists them,
    /// oldest first by its first revision, each at its current revision. A carried ask waits as its request and is not among them.
    /// </summary>
    public static IReadOnlyList<RecordEnvelope> Answerable(EstateModel model, string? to = null)
    {
        ArgumentNullException.ThrowIfNull(model);
        return [.. Asks.Deliveries(model, to).Where(d => model.HasRole(d.Principal, Roles.Operator)).SelectMany(d => d.Asks)];
    }

    /// <summary>
    /// The body of an act's decision answering one ask with its recommendation, as every surface authors it: the operator's words as the act
    /// gives them, the recommendation as the summary and its effect, the act's time as <c>decidedAt</c>, and the act pinning the exact ask
    /// revision and its recommendation's hash.
    /// </summary>
    public static System.Text.Json.Nodes.JsonObject DecisionBody(RecordEnvelope ask, string by, string act, string decidedAt, int count, string stake)
    {
        ArgumentNullException.ThrowIfNull(ask);
        return DecisionBody(ask.Id, ask.ComputedHash, EstateModel.BodyString(ask, "question") ?? ask.Id, EstateModel.BodyString(ask, "recommendation") ?? string.Empty,
            by, act, decidedAt, count, stake);
    }

    /// <summary>The same body from an ask revision's identity, hash, question and recommendation, as a client that holds no record model authors it.</summary>
    public static System.Text.Json.Nodes.JsonObject DecisionBody(string ask, string hash, string question, string recommendation, string by, string act, string decidedAt, int count, string stake)
    {
        ArgumentNullException.ThrowIfNull(question);
        ArgumentNullException.ThrowIfNull(recommendation);
        string countText = count.ToString(System.Globalization.CultureInfo.InvariantCulture);
        return new System.Text.Json.Nodes.JsonObject
        {
            ["title"] = Cut("Recommendation taken: " + question, 200),
            ["statement"] = Statement,
            ["summary"] = Cut("The operator took the recommendation: " + recommendation, 4000),
            ["synthesis"] = Cut($"Context. Ask {ask} asked the operator: {question} Decision. The operator took its recommendation, in one signed act answering {countText} " +
                $"{(count == 1 ? "ask" : "asks")} with their recommendations: {recommendation} Consequences and limits. The answer is the recommendation as ask revision {hash} " +
                "states it, and authorizes nothing it does not state.", 40000),
            ["effects"] = new System.Text.Json.Nodes.JsonArray($"The recommendation of ask {ask} is taken as its answer."),
            ["affects"] = new System.Text.Json.Nodes.JsonArray(),
            ["decidedBy"] = by,
            ["decidedAt"] = decidedAt,
            ["stake"] = stake,
            ["notAuthorized"] = new System.Text.Json.Nodes.JsonArray(NotAuthorized),
            ["answers"] = new System.Text.Json.Nodes.JsonArray(ask),
            ["act"] = new System.Text.Json.Nodes.JsonObject
            {
                ["id"] = act, ["ask"] = ask, ["hash"] = hash, ["recommendation"] = ContentHash.Sha256(Encoding.UTF8.GetBytes(recommendation)), ["count"] = count,
            },
        };
    }

    // Cut to fit a field, never inside a surrogate pair.
    private static string Cut(string text, int max)
    {
        if (text.Length <= max) return text;
        int end = max - 1;
        if (char.IsHighSurrogate(text[end - 1])) end--;
        return text[..end] + "…";
    }

    /// <summary>
    /// Validates a decision's <c>act</c> member (record format 3): it pins exactly the ask revision its answers name and that revision's
    /// recommendation, is recorded by the operator who decides it, a person, with their device's signature, carries none of
    /// <see cref="Excluded"/>, and agrees with every other decision of the same act on who decided, when and how many asks it answered.
    /// </summary>
    internal static void ValidateDecision(ValidationContext context, BodyReader act, IReadOnlyList<string>? answers, string? decidedBy, string? decidedAt)
    {
        string? id = act.Ulid("id", true);
        string? ask = act.Ulid("ask", true);
        string? hash = act.Hash("hash", true);
        string? recommendation = act.Hash("recommendation", true);
        long? count = act.Number("count", true, 1, MaxAsks);
        act.NoOtherKeys();
        if (answers is not { Count: 1 } || ask is null || answers[0] != ask)
            context.Error("decision.act", "A decision of an act answers exactly the ask revision its act names.");
        RecordEnvelope? source = context.RequireReference("act.ask", ask, "ask");
        if (source is not null && hash is not null && source.ComputedHash != hash)
            context.Error("decision.act", "The act pins the exact ask revision it answers, by its hash.");
        if (source is not null && recommendation is not null && RecommendationHash(source) != recommendation)
            context.Error("decision.act", "The act names the SHA-256 of that ask revision's recommendation, the answer it takes.");
        if (decidedBy is null || context.Record.By != decidedBy)
            context.Error("decision.act", "A decision of an act is recorded by the operator who decides it, never on their behalf.");
        else if (context.Principal(decidedBy) is not { } person || EstateModel.BodyString(person, "kind") != "human")
            context.Error("decision.act", "An act answering asks with their recommendations is a person's.");
        if (context.Record.Signatures.Count == 0)
            context.Error("signature.required", "A decision of an act requires its operator's admitted-device signature.");
        foreach (string member in Excluded.Where(context.Body.Has))
            context.Error("decision.act", $"A decision of an act takes the recommendation and nothing more; it carries no {member}.");
        if (id is null) return;

        // Every decision of one act agrees on who decided, when and how many asks the act answered, and answers its own ask.
        var chains = new HashSet<string>(StringComparer.Ordinal) { context.Model.Chain(context.Record)[0] };
        var asks = new HashSet<string>(StringComparer.Ordinal);
        if (source is not null) asks.Add(context.Model.Chain(source)[0]);
        foreach (RecordEnvelope other in context.Model.Current("decision"))
        {
            if (other.Id == context.Record.Id || context.Model.Chain(other)[0] == context.Model.Chain(context.Record)[0] || Of(other) is not { } named || named.Id != id) continue;
            if (EstateModel.BodyString(other, "decidedBy") != decidedBy || EstateModel.BodyString(other, "decidedAt") != decidedAt || named.Count != count)
                context.Error("decision.act", $"Decision {other.Id} names the same act with another operator, time or count; one act is one operator's, at one time.");
            chains.Add(context.Model.Chain(other)[0]);
            if (context.Find(named.Ask) is { } answered && !asks.Add(context.Model.Chain(answered)[0]))
                context.Error("decision.act", $"Decision {other.Id} answers the same ask in the same act; an act answers each ask once.");
        }

        if (count is not null && chains.Count > count)
            context.Error("decision.act", $"{chains.Count} decisions name an act that answered {count} asks.");
    }

    private static string? Text(JsonElement element, string name) => element.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}
