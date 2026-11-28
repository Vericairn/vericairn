// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using Vericairn.Protocol;
using Vericairn.Records.Authoring;
using Vericairn.Records.Estate;
using Vericairn.Records.Identity;
using Vericairn.Records.Model;
using static Vericairn.Runtime.Service.EstateServiceValues;

namespace Vericairn.Runtime.Service;

/// <summary>
/// A run's decision step decided by the principal its role reached (from 0.9.4, PROC-19; the estate's design/flow.md § 5): a person, or an
/// agent, holding the step's assignment records the path its transition takes, with the reasons and the evidence by hash, as the result of
/// that assignment, signed by its own device; a new attempt is recorded with it when the decider has none open. A rule's decision is
/// never recorded here: goal step records it as the position derives it. Choosing a path performs nothing reserved and dispatches
/// nothing; goal step takes the steps the path makes eligible.
/// </summary>
public sealed partial class EstateService
{
    /// <inheritdoc />
    public WriteOutcome GoalDecide(string goal, GoalDecideRequest request)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(goal);
        ArgumentNullException.ThrowIfNull(request);
        if (!Ulid.IsValid(request.By))
        {
            return Refuse("The deciding principal must be a ULID.");
        }

        if (string.IsNullOrWhiteSpace(request.Step))
        {
            return Refuse("A decision names the step it decides: its key, or key[instance].");
        }

        lock (gate)
        {
            EstateModel model = session.Load();
            RecordEnvelope? current = GoalView.Find(model, goal);
            if (current is null)
            {
                return NotFound($"No goal has id {goal}.");
            }

            DateTimeOffset now = FlowClock.GetUtcNow();
            FlowDecisions.Plan plan = FlowDecisions.Prepare(model, current, request.By, request.Step.Trim(), request.Paths ?? [], request.Reasons, request.Evidence ?? [], now);
            if (plan.Refusals.Count > 0)
            {
                return Refuse("Not decided: " + string.Join(" ", plan.Refusals));
            }

            // The decider's open attempt is answered by the decision; otherwise an attempt and the decision are stored together, both
            // signed by the decider's own device and announced before the first presence prompt. Under a rule then the role, a round whose
            // kind is not on record yet is recorded first, in the same batch: no rule held when it began, so its role decides.
            RecordEnvelope assignment = plan.Assignment!;
            if (plan.Attempt is RecordEnvelope open && plan.Kind is null)
            {
                return WriteLocked(new RecordDraft("result", request.By, JsonSerializer.SerializeToElement(plan.ResultBody()), new RecordReference(open.Type, open.Id, open.ComputedHash)), model);
            }

            RecordDraft? kinding = plan.Kind is null ? null : new RecordDraft("observation", request.By, JsonSerializer.SerializeToElement(plan.Kind), new RecordReference(current.Type, current.Id, current.ComputedHash));
            RecordDraft? attempting = plan.Attempt is null
                ? new RecordDraft("attempt", request.By, JsonSerializer.SerializeToElement(plan.AttemptBody(now)), new RecordReference(assignment.Type, assignment.Id, assignment.ComputedHash))
                : null;
            List<PresenceSignature> announced = [];
            if (kinding is not null)
            {
                announced.Add(DescribeRecord(kinding, model));
            }

            if (attempting is not null)
            {
                announced.Add(DescribeRecord(attempting, model));
            }

            announced.Add(new("result", "the decision of step " + plan.Step, "records the decision of a run's step"));
            using IDisposable counted = presence.Action("goal decide " + current.Id, SignsWithPresence(model, request.By) ? announced : null);
            var authored = new List<AuthoredRecord>();
            foreach (RecordDraft draft in new[] { kinding, attempting }.OfType<RecordDraft>())
            {
                (AuthoredRecord? record, Problem? reason) = AuthorLocked(draft, model);
                if (record is null)
                {
                    return WriteOutcome.Fail(reason!);
                }

                authored.Add(record);
            }

            RecordEnvelope answered = attempting is null ? plan.Attempt! : authored[^1].Envelope;
            var deciding = new RecordDraft("result", request.By, JsonSerializer.SerializeToElement(plan.ResultBody()), new RecordReference("attempt", answered.Id, answered.ComputedHash));
            (AuthoredRecord? result, Problem? refusal) = AuthorLocked(deciding, model);
            if (result is null)
            {
                return WriteOutcome.Fail(refusal!);
            }

            authored.Add(result);
            (IReadOnlyList<RecordWritten> written, Problem? problem) = StoreTogetherLocked(authored, $"Record the decision of step {plan.Step} of goal {current.Id}.", null);
            RecordWritten? decided = written.FirstOrDefault(w => w.Id == result.Envelope.Id);
            return problem is null ? WriteOutcome.Success(decided!) : new WriteOutcome(written.Count == 0 ? null : written[^1], problem);
        }
    }
}
