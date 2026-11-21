// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Vericairn.Protocol;
using Vericairn.Records.Authoring;
using Vericairn.Records.Estate;
using Vericairn.Records.Model;
using Vericairn.Records.Signing;
using Vericairn.Records.Types;
using Actions = Vericairn.Records.Estate.OperatorActions;
using Requests = Vericairn.Records.Estate.OperatorRequests;

namespace Vericairn.Runtime.Service;

/// <summary>
/// From 0.9.4 (OPS-24, decision 01M7GYC4BKATR834HV9KBQPBAD): vericairn operator act --ready. Every operator item whose command is ready
/// is listed with what its signature records: an acceptance of a reviewed result (as operator close lists it), an answer with the ask's
/// recommendation (OPS-16), a goal's acceptance, a release candidate's, a milestone's or a milestone result's, and a request's close. The
/// operator unselects any item, to leave it waiting or, for a request, to dismiss it with a reason; the rest are authored completely and
/// signed under one presence session whose announced list names each record by exact hash, at most 16 to a list, a list for each 16, and
/// nothing outside the choices is signed. An item that changed between listing and signing refuses the whole act; only a person holding
/// the operator role performs it, never an agent and never through MCP.
/// </summary>
public sealed partial class EstateService
{
    /// <inheritdoc />
    public OperatorActListResponse OperatorActItems(string? principal = null)
    {
        lock (gate)
        {
            // Inspection neither refreshes the live store nor probes signing custody.
            EstateModel model = session.Read();
            string? by = principal ?? Routing.RootOperator(model);
            if (ActorRefusal(model, by) is Problem refused) return new(by, [], refused);
            return new(by, ReadyItems(model, by!), CloseModelProblem(model));
        }
    }

    /// <inheritdoc />
    public OperatorActResponse OperatorAct(OperatorActRequest request) => OperatorAct(request, CancellationToken.None);

    /// <inheritdoc />
    public Task<OperatorActResponse> OperatorActAsync(OperatorActRequest request, CancellationToken cancellationToken = default) =>
        ClientOperation.RunSynchronousAsync(nameof(OperatorAct), () => OperatorAct(request, cancellationToken), true, this, cancellationToken);

    /// <summary>Authors a record for each item chosen to sign or dismiss, signs them under the act's presence check, rechecks every item, then stores them together.</summary>
    public OperatorActResponse OperatorAct(OperatorActRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        lock (gate)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (request.Choices is null || request.Choices.Any(c => c is null || string.IsNullOrWhiteSpace(c.Item) || c.Subject is null || c.Basis is null || c.Basis.Any(b => b is null)
                || !OperatorActs.Choices.Contains(c.Choice, StringComparer.Ordinal)))
                return ActRefused("Supply the exact items from operator act's listing, each chosen to sign, wait or dismiss.");
            // The caller may own mutable lists. Copy them before any presence check can yield control.
            OperatorActChoice[] choices = [.. request.Choices.Select(c => c with { Basis = [.. c.Basis] })];
            if (choices.Select(c => c.Item).Distinct(StringComparer.Ordinal).Count() != choices.Length)
                return ActRefused("Choose each listed item once.");
            if (choices.Any(c => c.Choice == "dismiss" && string.IsNullOrWhiteSpace(c.Reason)) || choices.Any(c => c.Choice != "dismiss" && c.Reason is not null))
                return ActRefused("A dismissal carries your reason, and only a dismissal does.");
            if (!DecisionType.Stakes.Contains(request.Stake, StringComparer.Ordinal))
                return ActRefused("The stake is one of low, medium, high or critical.");
            OperatorActChoice[] acting = [.. choices.Where(c => c.Choice != "wait")];
            if (!request.All && acting.Length == 0)
                return ActRefused("Choose at least one item to sign or dismiss, or all of the listed items.");

            EstateModel model = session.Load();
            if (Check(model) is Problem refused) return new(null, [], refused);
            if (acting.Length == 0) return new(null, []);
            string? refusal = ResolveSigner(model, new RecordDraft("acceptance", request.By, default), out IRecordSigner? signer);
            if (refusal is not null || signer is null)
                return ActRefused("The operator needs their own admitted signing device on this machine. No record of an act is unsigned.");

            // Every record is authored completely, its id and time fixed, before the first signature, so the presence session lists each by
            // exact hash (ID-16). The act's time is every answer's decidedAt.
            Dictionary<string, OperatorActItem> listed = ReadyItems(model, request.By).ToDictionary(i => i.Id, StringComparer.Ordinal);
            DateTimeOffset at = SessionNow();
            string act = global::Vericairn.Records.Identity.Ulid.NewUlid(at).Value;
            string decidedAt = RecordParser.FormatTimestamp(DateTimeOffset.FromUnixTimeSeconds(at.ToUnixTimeSeconds()));
            int answers = acting.Count(c => listed[c.Item].Kind == "answer");
            var drafts = new List<(OperatorActChoice Choice, RecordDraft Draft)>();
            foreach (OperatorActChoice choice in acting)
            {
                OperatorActItem item = listed[choice.Item];
                RecordDraft draft = item.Kind switch
                {
                    "answer" => new RecordDraft("decision", request.By, JsonSerializer.SerializeToElement(OperatorAnswers.DecisionBody(model.ById[item.Subject.Id], request.By, act, decidedAt, answers, request.Stake))),
                    "request" => new RecordDraft("operator-request-close", request.By, JsonSerializer.SerializeToElement(OperatorActs.RequestClose(choice.Choice == "dismiss" ? choice.Reason : null)), model.ById[item.Subject.Id].Reference),
                    _ => new RecordDraft("acceptance", request.By, JsonSerializer.SerializeToElement(OperatorActs.Acceptance(item.Kind, item.Basis.Select(b => b.Id), item.Self)), model.ById[item.Subject.Id].Reference),
                };
                draft = Fixed(draft, at);
                if (item.Kind == "answer" && DecisionContent.BlockedProseField(model, draft.Body) is string field)
                    return ActRefused($"The recommendation of ask {item.Subject.Id} carries, in its decision's {field}, a model identity from the estate's run or qualification records; leave it waiting and answer it alone.");
                drafts.Add((choice, draft));
            }

            // Every record is announced before the first check, and each signature says which it is (FIX-124); one presence session covers up
            // to 16 by exact hash with one check, a list for each 16, each announced before its own check (ID-16). From decision
            // 01M8001M0T9XRGF7SKHW3B45KF the vocabulary's operator act names the session, as the dialog and the ledger show it.
            using IDisposable counted = SessionAction("operator act",
                NeedsPresence(signer, drafts[0].Draft, model) ? drafts.Select(d => DescribeRecord(d.Draft, model)) : null,
                ExactSession("operator act", "operator act:" + act + ":" + string.Join(",", acting.Select(c => c.Item + "@" + c.Subject.Hash + "=" + c.Choice)),
                    [.. drafts.Select(d => d.Draft)], redatable: true));
            List<AuthoredRecord> records = [];
            foreach ((_, RecordDraft planned) in drafts)
            {
                cancellationToken.ThrowIfCancellationRequested();
                (AuthoredRecord? record, refusal) = AuthorLocked(SessionDraft(planned), model, [signer]);
                if (record is null) return new(null, [], new("refused", refusal!, []));
                records.Add(record);
            }

            cancellationToken.ThrowIfCancellationRequested();
            var stored = StoreTogetherLocked(records, "Sign the operator's chosen ready items in one reviewed act.", null, Check);
            Dictionary<string, OperatorActChoice> byRecord = drafts.ToDictionary(d => d.Draft.Id!, d => d.Choice, StringComparer.Ordinal);
            OperatorActReceipt[] signed = [.. stored.Written.Select(w => new OperatorActReceipt(byRecord[w.Id].Item, byRecord[w.Id].Choice, w))];
            return new(signed.Length > 0 ? act : null, signed, signed.Length > 0 && stored.Problem is not null
                ? stored.Problem with { Code = "reconcile-required", Message = "Some or all of the act's records were stored, but the batch did not finish. Inspect the returned receipts and original batch journal before retrying; do not sign those items again." }
                : stored.Problem);

            Problem? Check(EstateModel current)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (RequestDeviceRefusal(current) is Problem denied) return denied;
                if (ActorRefusal(current, request.By) is Problem actor) return actor;
                if (requestDevice.Value is { Closed: false } device && Standing(current, device.Device)?.Principal != request.By)
                    return new("denied", "An operator command can act only for the requesting device's own principal.", []);
                if (ActingRefusal.BeforeActing(current, "signing the operator act") is Problem invalid) return invalid;
                Dictionary<string, OperatorActItem> ready = ReadyItems(current, request.By).ToDictionary(i => i.Id, StringComparer.Ordinal);
                if (request.All && !ready.Keys.ToHashSet(StringComparer.Ordinal).SetEquals(choices.Select(c => c.Item))) return ActConflict();
                foreach (OperatorActChoice choice in request.All ? choices : acting)
                {
                    if (!ready.TryGetValue(choice.Item, out OperatorActItem? actual) || actual.Subject != choice.Subject || actual.RecommendationHash != choice.RecommendationHash
                        || !actual.Basis.OrderBy(b => b.Id, StringComparer.Ordinal).SequenceEqual(choice.Basis.OrderBy(b => b.Id, StringComparer.Ordinal)))
                        return ActConflict();
                    if (choice.Choice == "dismiss" && !actual.Dismissable)
                        return new("refused", $"Item {choice.Item} is not a request; only a request is dismissed, and any other item is left waiting.", []);
                }

                return null;
            }
        }
    }

    private static OperatorActResponse ActRefused(string message) => new(null, [], new("refused", message, []));

    private static Problem ActConflict() => new("conflict",
        "A chosen item changed or is no longer ready: its subject, basis or recommendation moved, it was signed or closed elsewhere, or, with all, another item became ready. Read operator act --ready again and review the current list; nothing was written.", []);

    /// <summary>
    /// Every operator item whose command is ready for one operator, in the order of <see cref="OperatorActs.Kinds"/>, each with what its
    /// signature records and the reviews it rests on. A run's reserved step and a publication's close are not ready here: their own commands
    /// complete them. A release candidate's, a milestone's or a milestone result's acceptance is ready only as operator close would accept a
    /// step: its exact revision has at least one current review and every current review of it passes, and a result is completed; one with
    /// a failed review or findings, none, or a result that is not completed, waits for the operator's own acceptance command.
    /// </summary>
    private List<OperatorActItem> ReadyItems(EstateModel model, string by)
    {
        var items = new List<OperatorActItem>();
        if (model.Findings.HasErrors) return items;
        var closing = new HashSet<string>(StringComparer.Ordinal);
        foreach (OperatorCloseItem close in CloseCandidates(model))
        {
            RecordEnvelope result = model.ById[close.Result.Id];
            closing.Add(result.Id);
            items.Add(new("close:" + close.Key, "close", by, close.Key + " result", $"an acceptance of result {close.Result.Id} of {close.Key} at its exact hash, on the basis of {Count(close.Reviews.Count, "passing review")}",
                "acceptance", "result", close.Result, [.. close.Reviews.Select(r => new OperatorClosePin(r.Id, r.Hash)).OrderBy(r => r.Id, StringComparer.Ordinal)], result.By == by, false,
                Reviews: [.. close.Reviews.Select(r => ReviewOf(model.ById[r.Id]))]));
        }

        foreach (OperatorAnswerItem ask in AnswerCandidates(model, by))
            items.Add(new("answer:" + ask.Ask.Id, "answer", by, ask.Question, $"a decision answering ask {ask.Ask.Id} at its exact hash with its recommendation {ask.RecommendationHash}",
                "decision", "ask", ask.Ask, [], false, false, ask.Question, ask.Recommendation, ask.RecommendationHash));

        JsonObject queue = Actions.Derive(model, FlowClock.GetUtcNow());
        foreach (string kind in new[] { "goal-acceptance", "release-acceptance", "milestone-acceptance", "milestone-result-acceptance" })
        {
            foreach (JsonObject item in queue["items"]!.AsArray().OfType<JsonObject>().Where(i => i["kind"]?.GetValue<string>() == kind))
            {
                JsonObject? command = item["commands"]!.AsArray().OfType<JsonObject>().FirstOrDefault(c => c["by"]?.GetValue<string>() == by);
                string id = item["source"]!["id"]!.GetValue<string>();
                if (command is null || closing.Contains(id) || !model.ById.TryGetValue(id, out RecordEnvelope? subject)) continue;
                OperatorClosePin[] basis = [.. command["body"]!["basis"]!.AsArray().Select(b => b!.GetValue<string>()).Where(model.ById.ContainsKey)
                    .Select(b => new OperatorClosePin(b, model.ById[b].ComputedHash)).OrderBy(b => b.Id, StringComparer.Ordinal)];
                RecordEnvelope[] reviews = [];
                if (kind != "goal-acceptance")
                {
                    // As operator close admits a step: its exact revision reviewed, every current review of it passing, and a result completed.
                    reviews = [.. model.About(subject.Id, "review").Where(r => model.IsCurrent(r) && r.Subject == subject.Reference).OrderBy(r => r.Id, StringComparer.Ordinal)];
                    if (reviews.Length == 0 || reviews.Any(r => EstateModel.BodyString(r, "outcome") != "pass")
                        || subject.Type == "result" && EstateModel.BodyString(subject, "outcome") != "completed") continue;
                    basis = [.. reviews.Select(r => new OperatorClosePin(r.Id, r.ComputedHash))];
                }

                string title = item["title"]!.GetValue<string>();
                string records = kind switch
                {
                    "goal-acceptance" => $"an acceptance of goal {subject.Id} at its exact hash, relying on {(basis.Length == 0 ? "no evaluation" : "its current evaluation " + basis[0].Id)}",
                    "release-acceptance" => $"an acceptance of release candidate {subject.Id} at its exact hash ({title}), on the basis of {Count(reviews.Length, "passing review")}; it publishes nothing",
                    "milestone-acceptance" => $"an acceptance of milestone {subject.Id} at its exact hash ({title}), on the basis of {Count(reviews.Length, "passing review")}",
                    _ => $"an acceptance of result {subject.Id} toward a milestone at its exact hash ({title}), on the basis of {Count(reviews.Length, "passing review")}",
                };
                items.Add(new(kind + ":" + subject.Id, kind, by, title, records, "acceptance", subject.Type, new(subject.Id, subject.ComputedHash), basis,
                    command["body"]!["self"]?.GetValue<bool>() ?? subject.By == by, false, Reviews: [.. reviews.Select(ReviewOf)]));
            }
        }

        foreach (RecordEnvelope request in Requests.WaitingOf(model, by, FlowClock.GetUtcNow()).Open
            .Where(r => FlowStops.StepOf(r) is null && EstateModel.BodyString(r, "kind") == "action"))
            items.Add(new("request:" + request.Id, "request", by, EstateModel.BodyString(request, "action") ?? "Request",
                $"a close of request {request.Id} at its exact hash as done, recording that you did what it asks; dismissed instead, it records your reason",
                "operator-request-close", "operator-request", new(request.Id, request.ComputedHash), [], false, true));
        return items;

        static string Count(int count, string what) => count.ToString(CultureInfo.InvariantCulture) + " " + what + (count == 1 ? string.Empty : "s");
        static OperatorActReview ReviewOf(RecordEnvelope review) => new(review.Id, review.ComputedHash, review.By,
            EstateModel.BodyString(review, "outcome") ?? "unknown", EstateModel.BodyString(review, "independence") ?? "unknown", review.Subject?.Hash ?? string.Empty);
    }
}
