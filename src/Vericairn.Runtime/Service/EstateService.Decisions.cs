// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using System.Text.Json.Nodes;
using Vericairn.Protocol;
using Vericairn.Records.Authoring;
using Vericairn.Records.Estate;
using Vericairn.Records.Hashing;
using Vericairn.Records.Identity;
using Vericairn.Records.Model;
using Vericairn.Records.Types;
using Vericairn.Runtime.Providers;
using AskState = Vericairn.Records.Estate.Asks;

namespace Vericairn.Runtime.Service;

public sealed partial class EstateService
{
    /// <inheritdoc />
    public DecisionBasisResponse DecisionBasis(string? baseCommit = null)
    {
        (DesignChangeSet? change, string? problem) = ObserveDecisionBasis(baseCommit);
        return change is null
            ? new(false, Problem: new Problem("unavailable", problem!, []))
            : new(true, change.BaseCommit, change.Head, change.Documents.OrderBy(d => d.Path, StringComparer.Ordinal).Select(d => new DecisionBasisDocument(d.Path, d.Hash)).ToArray());
    }

    /// <summary>
    /// The design change set from a full local base commit, or from the head observed now when none is given: the basis an answer's
    /// change set states. Reads only the estate's repository, never fetching or changing it; the reason when it is unavailable.
    /// </summary>
    private (DesignChangeSet? Change, string? Problem) ObserveDecisionBasis(string? baseCommit)
    {
        ISourceAdapter? source = Providers.Source("git");
        if (source is null)
            return (null, "The runtime has no repository source to observe the estate's basis. Decide where the estate's clone and its source adapter are.");
        try
        {
            string start = string.IsNullOrEmpty(baseCommit) ? source.Inspect(Root).Commit : baseCommit;
            DesignChangeSet? change = source.InspectDesignChanges(Root, start);
            if (change is null)
                return (null, "The complete design change set is unavailable: the base must be a full local commit of the estate repository, and conflicts, deletions, links or unreadable design files are resolved first.");
            if (string.IsNullOrEmpty(baseCommit) && change.Head != start)
                return (null, "The estate's head moved while its basis was read. Read the basis again.");
            return (change, null);
        }
        catch (Exception exception) when (exception is ProviderRefusedException or IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            return (null, "The estate's repository could not be observed: " + exception.Message);
        }
    }

    /// <summary>
    /// Why a decision prepared for a device's signature states a change set the runtime does not observe now, or null when it states
    /// none or the observation matches: the same head, no pins and no design document changed since its base. Pins are the desktop's.
    /// </summary>
    private string? DecisionBasisRefusal(RecordDraft draft)
    {
        if (draft.Type != "decision" || draft.Body.ValueKind != JsonValueKind.Object || !draft.Body.TryGetProperty("changeSet", out JsonElement changeSet)) return null;
        if (changeSet.ValueKind != JsonValueKind.Object || !changeSet.TryGetProperty("baseCommit", out JsonElement start) || start.ValueKind != JsonValueKind.String
            || !changeSet.TryGetProperty("head", out JsonElement head) || head.ValueKind != JsonValueKind.String
            || !changeSet.TryGetProperty("pins", out JsonElement pins) || pins.ValueKind != JsonValueKind.Array)
            return "A device-signed decision states its change set's base commit, head and pins.";
        (DesignChangeSet? change, string? problem) = ObserveDecisionBasis(start.GetString());
        if (change is null) return problem + " A device-signed decision is prepared only on an observed basis.";
        int changed = change.Documents.Count;
        return pins.GetArrayLength() > 0
            ? "A device-signed decision changes no design documents. An answer that pins documents is recorded on the desktop, which pins them with it."
            : change.Head != head.GetString()
            ? $"The decision's change set names head {head.GetString()}, but the estate's head is {change.Head} now. Read the basis again and review the answer."
            : changed > 0
            ? $"{changed} design documents changed since the decision's base commit, which pins none. Read the basis again; an answer with document changes is recorded on the desktop."
            : null;
    }

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
            if (DecisionRecordTimeProblem(decidedAt, now, "decision") is { } timeProblem)
                return new(null, [], timeProblem);
            if (string.CompareOrdinal(request.AnsweredAt, now) > 0)
                return Failed($"The answer time {request.AnsweredAt} cannot be later than the recording time {now}.");
            if (string.CompareOrdinal(decidedAt, request.AnsweredAt) > 0)
                return Failed("The decision time cannot be later than the operator's answer time.");
            if (string.CompareOrdinal(request.AnsweredAt, model.ById[model.Chain(ask)[0]].At) < 0)
                return Failed("The answer time cannot precede the ask.");
            if (request.Documents is null || request.Documents.Any(string.IsNullOrWhiteSpace)
                || request.Documents.Distinct(StringComparer.Ordinal).Count() != request.Documents.Count)
                return Failed("Supply the complete document list without duplicate or empty paths.");

            ISourceAdapter? source = Providers.Source("git");
            // An empty base commit takes the head observed now, the basis GET /v1/decisions/basis reads.
            string baseCommit = request.BaseCommit;
            if (string.IsNullOrEmpty(baseCommit))
            {
                (DesignChangeSet? basis, string? unavailable) = ObserveDecisionBasis(null);
                if (basis is null) return Failed(unavailable!);
                baseCommit = basis.BaseCommit;
            }

            DesignChangeSet? change;
            try { change = source?.InspectDesignChanges(Root, baseCommit); }
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
            var outputs = new List<DocumentOutputChange>();
            var pins = new JsonArray();
            string decisionId = Ulid.NewUlid().Value;
            // Every document pin and the decision are announced before the first presence prompt (FIX-124).
            using IDisposable counted = presence.Action("decision from ask " + ask.Id, SignsWithPresence(model, request.By)
                ? [.. change.Documents.OrderBy(d => d.Path, StringComparer.Ordinal).Select(d => PinSignature(d.Path)),
                    DescribeRecord(new RecordDraft("decision", request.By, JsonSerializer.SerializeToElement(body)), model)] : null);
            foreach (DesignFileChange document in change.Documents.OrderBy(d => d.Path, StringComparer.Ordinal))
            {
                Problem? problem = PrepareDocumentPin(new(document.Path, request.By, request.Summary), model, out RecordDraft? draft, out _);
                if (problem is not null) return new(null, [], problem);
                byte[] documentBytes;
                try
                {
                    documentBytes = File.ReadAllBytes(GeneratedDocumentVerification.SafeFile(Root, document.Path));
                    byte[] pinned = draft!.Body.TryGetProperty("hashMode", out _) ? RevisionLines.AuthoredContent(documentBytes) : documentBytes;
                    if (ContentHash.Sha256(documentBytes) != document.Hash || ContentHash.Sha256(pinned) != draft.Body.GetProperty("hash").GetString())
                        return Failed("A design file changed during preparation; read the change set again.");
                }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException)
                {
                    return Failed("A design file became unavailable during preparation; read the change set again.");
                }
                if (draft!.Body.TryGetProperty("hashMode", out _))
                {
                    JsonObject pinBody = JsonNode.Parse(draft.Body.GetRawText())!.AsObject();
                    pinBody["decision"] = decisionId;
                    draft = draft with { Body = JsonSerializer.SerializeToElement(pinBody) };
                    draft = StampRevision(draft, RevisionLines.History(model, document.Path));
                }
                (AuthoredRecord? pin, string? refusal) = AuthorLocked(draft, model);
                if (pin is null) return Failed(refusal!);
                records.Add(pin);
                if (RevisionLines.IsAuthored(pin.Envelope))
                {
                    byte[] after = RevisionLines.Refresh(ProjectRevision(model, pin.Envelope), document.Path, documentBytes);
                    outputs.Add(new(document.Path, document.Hash, [.. after]));
                }
                pins.Add(new JsonObject { ["path"] = document.Path, ["id"] = pin.Envelope.Id, ["hash"] = pin.Envelope.ComputedHash });
            }
            body["changeSet"] = new JsonObject { ["baseCommit"] = change.BaseCommit, ["head"] = change.Head, ["pins"] = pins };
            (AuthoredRecord? decision, string? reason) = AuthorLocked(new("decision", request.By, JsonSerializer.SerializeToElement(body), Id: decisionId), model);
            if (decision is null) return Failed(reason!);
            records.Add(decision);

            // Signing can require operator presence. Re-observe membership, bytes and the ask after that wait.
            DesignChangeSet? current;
            try { current = source!.InspectDesignChanges(Root, baseCommit); }
            catch (ProviderRefusedException) { current = null; }
            EstateModel refreshed = session.Load();
            RecordEnvelope currentAsk = refreshed.Latest(ask);
            if (current is null || current.Head != change.Head || current.BaseCommit != change.BaseCommit
                || !change.Documents.OrderBy(d => d.Path, StringComparer.Ordinal).SequenceEqual(current.Documents.OrderBy(d => d.Path, StringComparer.Ordinal))
                || currentAsk.ComputedHash != ask.ComputedHash || !AskState.IsWaiting(AskState.State(refreshed, currentAsk).State))
                return Failed("The ask or design change set changed during preparation; inspect it before trying again.");
            var stored = StoreTogetherLocked(records, "Record the operator's supplied answer and its exact design document pins.", null,
                current => change.Documents.Any(d => !RevisionLines.History(current, d.Path).Select(r => r.ComputedHash)
                    .SequenceEqual(RevisionLines.History(model, d.Path).Select(r => r.ComputedHash), StringComparer.Ordinal))
                    ? new("conflict", "A document's revision history changed during preparation.", []) : null, [.. outputs]);
            RecordWritten? written = stored.Written.SingleOrDefault(r => r.Id == decision.Envelope.Id);
            Problem? incomplete = stored.Problem;
            if (incomplete is not null && stored.Written.Count > 0)
                incomplete = new("reconcile-required", "Part of the decision and pin batch was stored. Inspect its receipts and reconcile the retained batch before retrying.", incomplete.Findings);
            return new(written, stored.Written.Where(r => r.Id != decision.Envelope.Id).ToArray(), incomplete);
        }

        static DecisionNewResponse Failed(string message) => new(null, [], new Problem("refused", message, []));
    }

    private static Problem? DecisionRecordTimeProblem(string? decidedAt, string at, string location) =>
        DecisionType.RecordTimeFinding(decidedAt, at, location) is { } finding
            ? new("rejected", finding.Message, [new FindingDto("error", finding.Code, finding.Location, finding.Message)])
            : null;
}
