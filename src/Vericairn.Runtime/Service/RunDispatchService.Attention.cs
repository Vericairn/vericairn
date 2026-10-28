// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Vericairn.Protocol;
using Vericairn.Records.Authoring;
using Vericairn.Records.Estate;
using Vericairn.Records.Model;
using Vericairn.Runtime.Store;
using static Vericairn.Runtime.Service.EstateServiceValues;

namespace Vericairn.Runtime.Service;

internal sealed partial class RunDispatchService
{
    // Older settlements can lack attention ids, and recovery can be requested by another principal. The generated question,
    // original author and subject identify process warnings without mistaking a decision or settlement review for one.
    private static List<RecordEnvelope> ProcessAttention(EstateModel model, AgentRuns.Run run) => model.Current("ask")
        .Where(a => a.By == run.Intent.By && a.Subject?.Type == "operation" && a.Subject.Id == run.Id
            && EstateModel.BodyString(a, "question") is string question && question.StartsWith($"Run {run.Id} ", StringComparison.Ordinal)
            && ProcessAttentionQuestion().IsMatch(question) && Asks.IsWaiting(Asks.State(model, a).State)
            && !WasAnswered(model, a))
        .OrderBy(a => a.Id, StringComparer.Ordinal).ToList();

    [GeneratedRegex(@"\ARun [0-9A-HJKMNP-TV-Z]{26} (?:"
        + @"\[(?!attempt-missing/|scope-settlement/)[a-z][a-z0-9-]*/(?:warning|critical)\] needs attention at "
        + @"[0-9]{4}-[0-9]{2}-[0-9]{2}T[0-9]{2}:[0-9]{2}:[0-9]{2}Z: .+ Inspect the run\?|"
        + @"needs attention: at [0-9]{4}-[0-9]{2}-[0-9]{2}T[0-9]{2}:[0-9]{2}:[0-9]{2}Z "
        + @"the worker observed no stdout event for [0-9]+(?:\.[0-9]+)? seconds\. Inspect the run\?)\z",
        RegexOptions.CultureInvariant | RegexOptions.Singleline)]
    private static partial Regex ProcessAttentionQuestion();

    // Automatic cleanup must not revise an ask a person answered, including a historical answer. Its queue state still follows
    // current decisions through Asks.State; the decision validator also preserves the validity of historical answers.
    private static bool WasAnswered(EstateModel model, RecordEnvelope ask)
    {
        IReadOnlyList<string> chain = model.Chain(ask);
        return model.OfType("decision").Any(d => d.Body.TryGetProperty("answers", out JsonElement answers)
            && answers.ValueKind == JsonValueKind.Array
            && answers.EnumerateArray().Any(a => a.ValueKind == JsonValueKind.String && chain.Contains(a.GetString()!)));
    }

    private static RecordDraft AttentionWithdrawal(AgentRuns.Run run, RecordEnvelope settlement, RecordEnvelope ask)
    {
        JsonObject body = JsonNode.Parse(ask.Body.GetRawText())!.AsObject();
        body["state"] = "withdrawn";
        body["recommendation"] = $"No longer needed: run {run.Id} settled as {EstateModel.BodyString(settlement, "status")} in operation {settlement.Id}. Its worker observations remain available for review.";
        return new RecordDraft("ask", ask.By, Element(body), settlement.Reference, ask.Id);
    }

    // No migration marker is needed: the withdrawals themselves make the next open a no-op. Use the same authoring,
    // validation and journal as live settlement, in one batch for all existing terminal runs. No local observation is evidence
    // of settlement here, and missing signing authority leaves the asks open with a visible local reconciliation concern.
    internal void ReconcileSettledAttention()
    {
        lock (gate)
        {
            EstateModel model = session.Load();
            var pending = AgentRuns.All(model).Where(r => r.Status is "observed-success" or "known-failure" or "canceled")
                .SelectMany(r => ProcessAttention(model, r).Select(a => (Run: r, Ask: a))).ToList();
            if (pending.Count == 0) return;
            var records = new List<AuthoredRecord>();
            try
            {
                // A partial settlement or cleanup already reserved its withdrawal identities. Its journal does not identify
                // their predecessors, so do not create competing revisions while any journal still has an unwritten ask.
                BatchJournal.ReadResult journals = BatchJournal.All(session);
                if (journals.Unreadable.Count > 0 || journals.Journals.Any(j => j.Records.Any(r => r.Type == "ask" && !model.ById.ContainsKey(r.Id))))
                {
                    Unresolved();
                    return;
                }
                foreach (var (run, ask) in pending)
                {
                    (AuthoredRecord? withdrawal, _) = recordAccess.AuthorLocked(AttentionWithdrawal(run, run.Latest, ask), model);
                    if (withdrawal is null)
                    {
                        Unresolved();
                        return;
                    }
                    records.Add(withdrawal);
                }
                (_, Problem? problem) = recordAccess.StoreTogetherLocked(records, "Withdraw outstanding process attention asks of settled runs.", null);
                if (problem is not null) Unresolved();
                else foreach (string run in pending.Select(p => p.Run.Id).Distinct(StringComparer.Ordinal)) settlementProblems.Remove(run);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
            {
                Unresolved();
            }

            void Unresolved()
            {
                foreach (string run in pending.Select(p => p.Run.Id).Distinct(StringComparer.Ordinal))
                    settlementProblems[run] = "The run is settled, but its outstanding process attention asks could not be withdrawn. Reconcile the records and the original asker's signing authority before reopening the estate.";
            }
        }
    }
}
