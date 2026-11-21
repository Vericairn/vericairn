// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json.Nodes;
using Vericairn.Protocol;
using Vericairn.Records.Authoring;
using Vericairn.Records.Estate;
using Vericairn.Records.Model;
using Vericairn.Runtime.Release;
using static Vericairn.Runtime.Service.EstateServiceValues;

namespace Vericairn.Runtime.Service;

public sealed partial class EstateService
{
    /// <inheritdoc />
    public ReleaseResponse ReleaseWithdraw(ReleaseWithdrawRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        lock (gate)
        {
            EstateModel model = session.Load();
            RecordEnvelope? candidate = Latest(model, request.Release, "release");
            if (candidate is null) return Failed("not-found", "No release has that id.");
            if (request.Expected != candidate.ComputedHash)
                return Failed("conflict", "The expected release hash is stale; read the current candidate and prepare again.");
            if (EstateModel.BodyString(candidate, "status") != "candidate")
                return Failed("refused", "Only a current candidate can be withdrawn by this command.");
            if (string.IsNullOrWhiteSpace(request.Reason)) return Failed("refused", "Give the reason for withdrawing the candidate.");

            string repository = EstateModel.BodyString(candidate, "repository")!;
            string version = EstateModel.BodyString(candidate, "version")!;
            string identity = ReleaseFlow.Identity(request.Provider, repository, version) + ":delete-draft";
            var target = new JsonObject();
            foreach (var (name, value) in request.Settings ?? new Dictionary<string, string>(StringComparer.Ordinal)) target[name] = value;
            foreach (var (name, value) in new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["repository"] = repository, ["version"] = version, ["product"] = candidate.Subject!.Id,
            })
            {
                if (target.ContainsKey(name) && target[name]?.GetValue<string>() != value)
                    return Failed("refused", "The deletion settings must keep the candidate's repository, version and product.");
                target[name] = value;
            }

            DateTimeOffset at = PolicyClock.GetUtcNow();
            if (at < candidate.AtTime) at = candidate.AtTime;
            JsonObject body = JsonNode.Parse(candidate.Body.GetRawText())!.AsObject();
            body["status"] = "withdrawn";
            body["evidence"]!.AsArray().Add(new JsonObject { ["summary"] = request.Reason, ["hash"] = candidate.ComputedHash });
            var withdrawing = Fixed(new RecordDraft("release", request.By, Element(body), candidate.Subject, candidate.Id, at), at);
            // The withdrawal is authored completely, unsigned, before the first signature, so the draft-deletion intent that names it at its
            // exact hash is too, and a presence session lists both by exact hash (ID-16).
            (AuthoredRecord? unsignedWithdrawal, string? refusal) = AuthorUnsignedLocked(withdrawing, model);
            if (unsignedWithdrawal is null) return Failed("refused", refusal!);
            var intent = new JsonObject
            {
                ["provider"] = request.Provider, ["action"] = ReleaseFlow.DeleteDraftAction, ["identity"] = identity,
                ["intent"] = $"Delete the draft of release {version} at {repository}; deletion stays the operator's manual step.",
                ["target"] = target, ["status"] = "not-started",
                ["evidence"] = new JsonArray(new JsonObject { ["summary"] = request.Reason, ["hash"] = candidate.ComputedHash }),
            };
            RecordDraft deleting = Fixed(new("operation", request.By, Element(intent), unsignedWithdrawal.Envelope.Reference, At: at), at);
            // Both records are announced before the first presence prompt (FIX-124).
            using IDisposable counted = SessionAction("release withdraw " + version, SignsWithPresence(model, request.By)
                ? [DescribeRecord(withdrawing, model), OperationSignature(ReleaseFlow.DeleteDraftAction, "not-started", "release " + version)] : null,
                ExactSession("release withdraw", "release withdraw:" + candidate.Id, [withdrawing, deleting], redatable: false));
            (AuthoredRecord? withdrawn, refusal) = AuthorLocked(SessionDraft(withdrawing), model);
            if (withdrawn is null) return Failed("refused", refusal!);
            if (withdrawn.Envelope.ComputedHash != unsignedWithdrawal.Envelope.ComputedHash) return Failed("refused", "The withdrawal changed while it was signed; nothing was written.");
            (AuthoredRecord? operation, refusal) = AuthorLocked(SessionDraft(deleting), model);
            if (operation is null) return Failed("refused", refusal!);
            var stored = StoreTogetherLocked([withdrawn, operation], "Withdraw a candidate and record its draft-deletion intent.", null);
            RecordWritten? release = stored.Written.FirstOrDefault(r => r.Id == withdrawn.Envelope.Id);
            RecordWritten? deletion = stored.Written.FirstOrDefault(r => r.Id == operation.Envelope.Id);
            return new(identity, release is null ? "candidate" : "withdrawn",
                stored.Problem?.Message ?? $"Release {version} withdrawn; draft-deletion intent {deletion!.Id} is not started. The operator deletes the draft and records dispatch, then observes this intent with release observe --withdrawal {deletion.Id}.",
                stored.Problem is null, Operation: deletion, Release: release, ReleaseId: release?.Id, Problem: stored.Problem);
        }

        ReleaseResponse Failed(string code, string message) => new(request.Release, "not-started", message, false, Problem: new(code, message, []));
    }
}
