// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using System.Text.Json.Nodes;
using Vericairn.Protocol;
using Vericairn.Records.Authoring;
using Vericairn.Records.Estate;
using Vericairn.Records.Identity;
using Vericairn.Records.Model;
using Vericairn.Records.Types;
using Vericairn.Runtime.Providers;
using AskState = Vericairn.Records.Estate.Asks;

namespace Vericairn.Runtime.Service;

public sealed partial class EstateService
{
    /// <inheritdoc />
    public DecisionNewResponse DecisionFromAsk(DecisionNewRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        lock (gate)
        {
            EstateModel model = session.Load();
            if (RequestDeviceRefusal(model) is Problem denied) return new(null, [], denied);
            if (!Ulid.IsValid(request.Ask) || !model.ById.TryGetValue(request.Ask, out RecordEnvelope? original) || original.Type != "ask")
                return Failed("Name an existing ask whose operator has supplied an answer.");
            RecordEnvelope ask = model.Latest(original);
            if (request.ExpectedAsk is not null && request.ExpectedAsk != ask.ComputedHash)
                return Failed("The expected ask hash is not current; read the ask again before recording its answer.");
            if (!AskState.IsWaiting(AskState.State(model, ask).State))
                return Failed("The ask is withdrawn or already answered; inspect its existing records before continuing.");
            string? decider = EstateModel.BodyString(ask, "to");
            if (!model.HasRole(decider, Roles.Operator)) return Failed("The ask must be addressed to an operator.");
            string now = RecordParser.FormatTimestamp(DateTimeOffset.UtcNow);
            string decidedAt = request.DecidedAt ?? request.AnsweredAt;
            if (!RecordParser.IsTimestamp(request.AnsweredAt) || !RecordParser.IsTimestamp(decidedAt))
                return Failed("Answer and decision times must be UTC record timestamps with second precision.");
            if (string.CompareOrdinal(request.AnsweredAt, now) > 0 || string.CompareOrdinal(decidedAt, now) > 0)
                return Failed("The answer and decision times cannot be later than now.");
            if (string.CompareOrdinal(decidedAt, request.AnsweredAt) > 0)
                return Failed("The decision time cannot be later than the operator's answer time.");
            if (string.CompareOrdinal(request.AnsweredAt, model.ById[model.Chain(ask)[0]].At) < 0)
                return Failed("The answer time cannot precede the ask.");
            if (request.Documents is null || request.Documents.Any(string.IsNullOrWhiteSpace)
                || request.Documents.Distinct(StringComparer.Ordinal).Count() != request.Documents.Count)
                return Failed("Supply the complete document list without duplicate or empty paths.");

            ISourceAdapter? source = Providers.Source("git");
            DesignChangeSet? change;
            try { change = source?.InspectDesignChanges(Root, request.BaseCommit); }
            catch (ProviderRefusedException) { change = null; }
            if (change is null)
                return Failed("The complete design change set is unavailable. Supply a full local base commit in the estate repository; resolve conflicts, deletions or unavailable files first.");
            if (!request.Documents.Order(StringComparer.Ordinal).SequenceEqual(change.Documents.Select(d => d.Path).Order(StringComparer.Ordinal)))
                return Failed("The decision's documents must equal exactly the design files changed from the base commit, including untracked files.");

            var body = new JsonObject
            {
                ["title"] = request.Title, ["statement"] = request.Statement, ["summary"] = request.Summary,
                ["synthesis"] = request.Synthesis, ["effects"] = JsonSerializer.SerializeToNode(request.Effects),
                ["affects"] = JsonSerializer.SerializeToNode(request.Affects ?? []), ["decidedBy"] = decider,
                ["decidedAt"] = decidedAt, ["stake"] = request.Stake,
                ["notAuthorized"] = JsonSerializer.SerializeToNode(request.NotAuthorized ?? []),
                ["answers"] = new JsonArray(ask.Id),
                ["answer"] = new JsonObject { ["ask"] = ask.Id, ["hash"] = ask.ComputedHash, ["at"] = request.AnsweredAt },
            };
            if (request.Documents.Count > 0) body["documents"] = JsonSerializer.SerializeToNode(request.Documents);
            if (DecisionContent.BlockedProseField(model, JsonSerializer.SerializeToElement(body)) is string field)
                return Failed("Decision prose in " + field + " contains a model identity from the estate's run or qualification records.");

            var records = new List<AuthoredRecord>();
            var pins = new JsonArray();
            foreach (DesignFileChange document in change.Documents.OrderBy(d => d.Path, StringComparer.Ordinal))
            {
                Problem? problem = PrepareDocumentPin(new(document.Path, request.By, request.Summary), model, out RecordDraft? draft, out _);
                if (problem is not null) return new(null, [], problem);
                if (draft!.Body.GetProperty("hash").GetString() != document.Hash)
                    return Failed("A design file changed during preparation; read the change set again.");
                (AuthoredRecord? pin, string? refusal) = AuthorLocked(draft, model);
                if (pin is null) return Failed(refusal!);
                records.Add(pin);
                pins.Add(new JsonObject { ["path"] = document.Path, ["id"] = pin.Envelope.Id, ["hash"] = pin.Envelope.ComputedHash });
            }
            body["changeSet"] = new JsonObject { ["baseCommit"] = change.BaseCommit, ["head"] = change.Head, ["pins"] = pins };
            (AuthoredRecord? decision, string? reason) = AuthorLocked(new("decision", request.By, JsonSerializer.SerializeToElement(body)), model);
            if (decision is null) return Failed(reason!);
            records.Add(decision);

            // Signing can require operator presence. Re-observe membership, bytes and the ask after that wait.
            DesignChangeSet? current;
            try { current = source!.InspectDesignChanges(Root, request.BaseCommit); }
            catch (ProviderRefusedException) { current = null; }
            EstateModel refreshed = session.Load();
            RecordEnvelope currentAsk = refreshed.Latest(ask);
            if (current is null || current.Head != change.Head || current.BaseCommit != change.BaseCommit
                || !change.Documents.OrderBy(d => d.Path, StringComparer.Ordinal).SequenceEqual(current.Documents.OrderBy(d => d.Path, StringComparer.Ordinal))
                || currentAsk.ComputedHash != ask.ComputedHash || !AskState.IsWaiting(AskState.State(refreshed, currentAsk).State))
                return Failed("The ask or design change set changed during preparation; inspect it before trying again.");
            var stored = StoreTogetherLocked(records, "Record the operator's supplied answer and its exact design document pins.", null);
            RecordWritten? written = stored.Written.SingleOrDefault(r => r.Id == decision.Envelope.Id);
            Problem? incomplete = stored.Problem;
            if (incomplete is not null && stored.Written.Count > 0)
                incomplete = new("reconcile-required", "Part of the decision and pin batch was stored. Inspect its receipts and reconcile the retained batch before retrying.", incomplete.Findings);
            return new(written, stored.Written.Where(r => r.Id != decision.Envelope.Id).ToArray(), incomplete);
        }

        static DecisionNewResponse Failed(string message) => new(null, [], new Problem("refused", message, []));
    }
}
