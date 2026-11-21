// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using Vericairn.Protocol;
using Vericairn.Records.Authoring;
using Vericairn.Records.Estate;
using Vericairn.Records.Model;
using Vericairn.Records.Signing;
using Vericairn.Records.Types;
using AskState = Vericairn.Records.Estate.Asks;

namespace Vericairn.Runtime.Service;

/// <summary>
/// From 0.9.4 (OPS-16, decision 01M76DVQX85PAB91GNP4SZQA9J): the operator answers a selection of the asks waiting on them, or all of them,
/// each with its recommendation, in one act signed by their own device. The act shows each ask and recommendation by hash first and refuses
/// whole if any changed since; each ask gets its own decision naming the act, decidedAt the act's time, all stored together. Only the
/// operator the asks are put to, a person, performs it, never an agent and never through MCP; any ask can still be answered alone.
/// </summary>
public sealed partial class EstateService
{
    /// <inheritdoc />
    public OperatorAnswerCandidatesResponse OperatorAnswerCandidates(string? principal = null)
    {
        lock (gate)
        {
            // Inspection neither refreshes the live store nor probes signing custody.
            EstateModel model = session.Read();
            return new(AnswerCandidates(model, principal), CloseModelProblem(model));
        }
    }

    /// <inheritdoc />
    public OperatorAnswerResponse OperatorAnswer(OperatorAnswerRequest request) => OperatorAnswer(request, CancellationToken.None);

    /// <inheritdoc />
    public Task<OperatorAnswerResponse> OperatorAnswerAsync(OperatorAnswerRequest request, CancellationToken cancellationToken = default) =>
        ClientOperation.RunSynchronousAsync(nameof(OperatorAnswer), () => OperatorAnswer(request, cancellationToken), true, this, cancellationToken);

    /// <summary>Authors one decision for each selected ask, signs them under the act's presence check, rechecks every pin, then stores them together.</summary>
    public OperatorAnswerResponse OperatorAnswer(OperatorAnswerRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        lock (gate)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (request.Selection is null || request.Selection.Any(s => s?.Ask is null || string.IsNullOrWhiteSpace(s.Ask.Id) || string.IsNullOrWhiteSpace(s.Ask.Hash)
                || string.IsNullOrWhiteSpace(s.Recommendation)))
                return AnswerRefused("Supply the exact asks and recommendation hashes from operator answer's listing.");
            // The caller may own mutable lists. Copy them before any presence check can yield control.
            OperatorAnswerSelection[] selection = [.. request.Selection];
            if (selection.Select(s => s.Ask.Id).Distinct(StringComparer.Ordinal).Count() != selection.Length || (!request.All && selection.Length == 0))
                return AnswerRefused("Select distinct asks, or all of the displayed asks.");
            if (selection.Length > OperatorAnswers.MaxAsks)
                return AnswerRefused($"One act answers at most {OperatorAnswers.MaxAsks} asks; select fewer and answer the rest in another act.");
            if (!DecisionType.Stakes.Contains(request.Stake, StringComparer.Ordinal))
                return AnswerRefused("The stake is one of low, medium, high or critical.");

            EstateModel model = session.Load();
            if (Check(model) is Problem refused) return new(null, [], refused);
            if (selection.Length == 0) return new(null, []);
            string? refusal = ResolveSigner(model, new RecordDraft("decision", request.By, default), out IRecordSigner? signer);
            if (refusal is not null || signer is null)
                return AnswerRefused("The operator needs their own admitted signing device on this machine. No unsigned decision is allowed.");

            // Every decision is authored completely, its id and time fixed, before the first signature, so a presence session lists each by
            // exact hash, at most 16 to a check (ID-16). The act's time is every decision's decidedAt.
            DateTimeOffset at = SessionNow();
            string act = global::Vericairn.Records.Identity.Ulid.NewUlid(at).Value;
            string decidedAt = RecordParser.FormatTimestamp(DateTimeOffset.FromUnixTimeSeconds(at.ToUnixTimeSeconds()));
            RecordDraft[] drafts = [.. selection.Select(item =>
            {
                RecordEnvelope ask = model.ById[item.Ask.Id];
                JsonElement body = JsonSerializer.SerializeToElement(OperatorAnswers.DecisionBody(ask, request.By, act, decidedAt, selection.Length, request.Stake));
                return Fixed(new RecordDraft("decision", request.By, body), at);
            })];
            foreach (RecordDraft draft in drafts)
            {
                if (DecisionContent.BlockedProseField(model, draft.Body) is string field)
                    return AnswerRefused($"The recommendation of ask {draft.Body.GetProperty("answers")[0].GetString()} carries, in its decision's {field}, a model identity from the estate's run or qualification records; answer it alone.");
            }

            // Every decision is announced before the first check, and each signature says which it is (FIX-124); a presence session covers up to
            // 16 by exact hash with one check, a list for each 16, each announced before its own check (ID-16).
            using IDisposable counted = SessionAction("decision from ask",
                NeedsPresence(signer, drafts[0], model) ? drafts.Select(d => DescribeRecord(d, model)) : null,
                ExactSession("decision from ask", "operator answer:" + act + ":" + string.Join(",", selection.Select(s => s.Ask.Id + "@" + s.Ask.Hash)), drafts, redatable: true));
            List<AuthoredRecord> records = [];
            foreach (RecordDraft planned in drafts)
            {
                cancellationToken.ThrowIfCancellationRequested();
                (AuthoredRecord? record, refusal) = AuthorLocked(SessionDraft(planned), model, [signer]);
                if (record is null) return new(null, [], new("refused", refusal!, []));
                records.Add(record);
            }

            cancellationToken.ThrowIfCancellationRequested();
            var stored = StoreTogetherLocked(records, "Answer the operator's selected asks with their recommendations in one act.", null, Check);
            return new(stored.Written.Count > 0 ? act : null, stored.Written, stored.Written.Count > 0 && stored.Problem is not null
                ? stored.Problem with { Code = "reconcile-required", Message = "Some or all of the act's decisions were stored, but the batch did not finish. Inspect the returned receipts and original batch journal before retrying; do not answer those asks again." }
                : stored.Problem);

            Problem? Check(EstateModel current)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (RequestDeviceRefusal(current) is Problem denied) return denied;
                if (ActorRefusal(current, request.By) is Problem actor) return actor;
                if (requestDevice.Value is { Closed: false } device && Standing(current, device.Device)?.Principal != request.By)
                    return new("denied", "An operator command can act only for the requesting device's own principal.", []);
                if (CloseModelProblem(current) is Problem invalid) return invalid;
                List<OperatorAnswerItem> candidates = AnswerCandidates(current, request.By);
                if (request.All && candidates.Count != selection.Length) return AnswerConflict();
                foreach (OperatorAnswerSelection expected in selection)
                {
                    OperatorAnswerItem? actual = candidates.SingleOrDefault(c => c.Ask.Id == expected.Ask.Id);
                    if (actual is null || actual.Ask != expected.Ask || actual.RecommendationHash != expected.Recommendation) return AnswerConflict();
                }

                return null;
            }
        }
    }

    /// <summary>Why a principal may not perform an operator's act, or null: only a person holding the operator role does, never an agent.</summary>
    private static Problem? ActorRefusal(EstateModel model, string? by) =>
        string.IsNullOrWhiteSpace(by) || !model.HasRole(by, Roles.Operator) ? new("denied", "Only an operator performs an operator's act.", [])
        : model.CurrentPrincipal(by) is not { } person || EstateModel.BodyString(person, "kind") != "human"
            ? new("denied", "An operator's act is a person's; an agent never performs it, whatever role it holds.", [])
        : null;

    /// <summary>
    /// Why a decision naming an act may not be written now, or null (OPS-16): the ask revision it pins is the current one, still waits on the
    /// operator who decides and is not carried, and the recommendation it names is that revision's. The record's own rules hold the rest.
    /// Applies to every write of such a decision, the act's own, a device-signed one from a phone and a hand-written one alike.
    /// </summary>
    private static string? ActWriteRefusal(RecordDraft draft, EstateModel model)
    {
        if (draft.Type != "decision" || draft.Body.ValueKind != JsonValueKind.Object || !draft.Body.TryGetProperty("act", out JsonElement act)) return null;
        if (act.ValueKind != JsonValueKind.Object || !act.TryGetProperty("ask", out JsonElement id) || id.ValueKind != JsonValueKind.String
            || !model.ById.TryGetValue(id.GetString()!, out RecordEnvelope? ask) || ask.Type != "ask")
            return "A decision of an act names the exact ask revision it answers.";
        RecordEnvelope current = model.Latest(ask);
        if (current.Id != ask.Id || !act.TryGetProperty("hash", out JsonElement hash) || hash.ValueKind != JsonValueKind.String || hash.GetString() != current.ComputedHash)
            return $"Ask {ask.Id} changed since the act showed it; nothing of the act was written. Read the asks again and review the current revision.";
        if (!act.TryGetProperty("recommendation", out JsonElement recommendation) || recommendation.ValueKind != JsonValueKind.String
            || recommendation.GetString() != OperatorAnswers.RecommendationHash(current))
            return $"The recommendation of ask {ask.Id} is not the one the act showed; nothing of the act was written.";
        if (!AskState.IsWaiting(AskState.State(model, current).State) || global::Vericairn.Records.Estate.OperatorRequests.Carried(model, current) is not null)
            return $"Ask {ask.Id} no longer waits on the operator: it was answered, withdrawn or carried into a request.";
        if (ActorRefusal(model, draft.By) is Problem actor) return actor.Message;
        return EstateModel.BodyString(current, "to") != draft.By ? $"Ask {ask.Id} is put to another principal; only the operator it is put to answers it in an act." : null;
    }

    private static OperatorAnswerResponse AnswerRefused(string message) => new(null, [], new("refused", message, []));

    private static Problem AnswerConflict() => new("conflict",
        "A selected ask or its recommendation changed, or the asks waiting on the operator changed: it was revised, answered, withdrawn or carried, or another arrived. Read operator answer again and review the current asks; nothing was written.", []);

    private static List<OperatorAnswerItem> AnswerCandidates(EstateModel model, string? to) => [.. OperatorAnswers.Answerable(model, to).Select(ask => new OperatorAnswerItem(
        new(ask.Id, ask.ComputedHash), EstateModel.BodyString(ask, "to")!, EstateModel.BodyString(ask, "recommendation") ?? string.Empty,
        OperatorAnswers.RecommendationHash(ask) ?? string.Empty, EstateModel.BodyString(ask, "question") ?? string.Empty,
        EstateModel.BodyString(ask, "consequence") ?? string.Empty,
        ask.Body.TryGetProperty("options", out JsonElement options) && options.ValueKind == JsonValueKind.Array
            ? [.. options.EnumerateArray().Where(o => o.ValueKind == JsonValueKind.String).Select(o => o.GetString()!)] : [],
        AskState.State(model, ask).State))];
}
