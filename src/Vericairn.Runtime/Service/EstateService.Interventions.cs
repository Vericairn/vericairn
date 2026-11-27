// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using Vericairn.Protocol;
using Vericairn.Records.Authoring;
using Vericairn.Records.Estate;
using Vericairn.Records.Model;

namespace Vericairn.Runtime.Service;

public sealed partial class EstateService
{
    private const string RunStopRecord = "run-stop-request";

    private static bool IsReviewedIntervention(RecordDraft draft) => draft.Type == RunStopRecord || ReviewedPause(draft);

    private static bool ReviewedPause(RecordDraft draft) => draft.Type == "goal-status" && BodyText(draft, "status") == "paused";

    private static string? BodyText(RecordDraft draft, string name) =>
        draft.Body.ValueKind == JsonValueKind.Object &&
        draft.Body.TryGetProperty(name, out JsonElement value) &&
        value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    // The reviewed preconditions are part of the signed record and checked again against live state.
    private Problem? InterventionRefusal(EstateModel model, RecordDraft draft)
    {
        if (!IsReviewedIntervention(draft)) return null;
        if (draft.Format < 3)
            return new("unsupported", "This estate's record format does not support phone-signed interventions.", []);
        if (model.CurrentPrincipal(draft.By) is not { } person || EstateModel.BodyString(person, "kind") != "human")
            return new("denied", "Only a person can send a reviewed intervention.", []);

        if (draft.Type == RunStopRecord)
        {
            if (ReadStopRecord(draft) is not { } stop)
                return new("refused", "A stop request needs its exact run operation and original request identity.", []);
            if (model.OfType(RunStopRecord).Any(record => record.By == draft.By &&
                EstateModel.BodyString(record, "run") == stop.Run && EstateModel.BodyString(record, "operation") == stop.Operation &&
                EstateModel.BodyString(record, "requestId") == stop.RequestId))
                return new("conflict", "This stop request already has a signed record. Reconcile the original request.", []);
            AgentRuns.Run? current = AgentRuns.Find(model, stop.Run);
            if (current is { InFlight: false })
                return new("refused", $"Run {current.Id} ended as {current.Status}; there is nothing to stop.", []);
            if (current is null || current.Latest.Reference != draft.Subject)
                return new("refused", "The reviewed run operation changed. Read it again before requesting a stop.", []);
            return runDispatch.ReviewedStopRefusal(model, stop.Run, stop.Request);
        }

        if (BodyText(draft, "activeStatusHash") is not string expectedActiveStatusHash || draft.Subject is not { Type: "goal", Hash: not null } subject || draft.Supersedes is not null)
            return new("refused", "A reviewed pause needs its exact goal revision and active status.", []);
        if (GoalView.Find(model, subject.Id) is not { } goal || goal.Reference != subject ||
            GoalRules.Status(model, goal) != "active" || ActiveGoalStatusHash(model, goal) != expectedActiveStatusHash)
            return new("refused", "The reviewed active goal status changed. Read it again before requesting a pause.", []);
        if (!GoalRules.MayEnd(model, goal, draft.By))
            return new("denied", $"Only the goal's sponsor or an operator for a goal an agent sponsors pauses goal {goal.Id}.", []);
        return null;
    }

    private static ReviewedRunStop? ReadStopRecord(RecordDraft draft)
    {
        string? operation = BodyText(draft, "operation");
        if (draft.Type != RunStopRecord || draft.Subject is not { Type: "operation", Hash: { } hash } ||
            BodyText(draft, "run") is not string run || operation is not ("cancel" or "contain") ||
            BodyText(draft, "requestId") is not string identity || BodyText(draft, "reason") is not string reason)
            return null;
        return new(run, operation, identity, new(draft.By, reason, operation == "contain" ? 0 : null)
        {
            ExpectedOperationHash = hash,
            ExpectedStopReason = BodyText(draft, "expectedStopReason"),
        });
    }

    private sealed record ReviewedRunStop(string Run, string Operation, string RequestId, RunStopRequest Request);
}
