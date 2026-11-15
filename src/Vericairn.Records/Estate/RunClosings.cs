// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using System.Text.Json.Nodes;
using Vericairn.Records.Model;
using Vericairn.Records.Templates;

namespace Vericairn.Records.Estate;

/// <summary>
/// Closing a run that will never complete (from 0.9.4, FIX-143; the estate's design/flow.md § 8, decision 01M7GAX5DD9BMV5QDXM7RJNAZ4):
/// who decides for its goal, a person, records each step the run has not settled, neither done nor off, with a reason, in a goal-status of
/// closed, alone or with the goal's acceptance. The run then reads closed, each of those steps unsettled with its reason, never done; a
/// required reserved step such as publication reads not done with its reason. Closing is neither acceptance nor stopping, which abandons
/// the goal, and an agent never closes a run. The runtime plans a closing here and its record type keeps the same rules.
/// </summary>
public static class RunClosings
{
    /// <summary>The longest reason a closing records for one step.</summary>
    public const int MaximumReason = 2000;

    /// <summary>One step a closing names as unsettled, with the closer's reason.</summary>
    /// <param name="Key">The step's key.</param>
    /// <param name="Instance">Its instance, for a step that runs over a list.</param>
    /// <param name="Reason">Why it is left unsettled.</param>
    public sealed record Unsettled(string Key, string? Instance, string Reason)
    {
        /// <summary>The step instance.</summary>
        public FlowRuns.StepRef Ref => new(Key, Instance);

        /// <summary>The step instance in words: key, or key[instance].</summary>
        public string Name => Ref.ToString();
    }

    /// <summary>A closing the runtime may write: the unsettled steps with their reasons, and the note its record carries; or why not.</summary>
    /// <param name="Steps">Each unsettled step with its reason, in the flow's order; empty when refused.</param>
    /// <param name="Note">The closing's note.</param>
    /// <param name="Refusals">Every reason it is refused, all at once; empty when it may be written.</param>
    public sealed record Plan(IReadOnlyList<Unsettled> Steps, string Note, IReadOnlyList<string> Refusals)
    {
        /// <summary>The goal-status body that records it.</summary>
        public JsonObject Body() => new()
        {
            ["status"] = "closed",
            ["note"] = Note,
            ["unsettled"] = new JsonArray(Steps.Select(s =>
            {
                var entry = new JsonObject { ["key"] = s.Key };
                if (s.Instance is not null)
                {
                    entry["instance"] = s.Instance;
                }

                entry["reason"] = s.Reason;
                return (JsonNode)entry;
            }).ToArray()),
        };
    }

    /// <summary>The steps a closing names as unsettled, as it recorded them.</summary>
    public static IReadOnlyList<Unsettled> Of(RecordEnvelope closing)
    {
        ArgumentNullException.ThrowIfNull(closing);
        return TJson.Array(closing.Body, "unsettled").Where(e => TJson.Text(e, "key") is not null)
            .Select(e => new Unsettled(TJson.Text(e, "key")!, TJson.Text(e, "instance"), TJson.Text(e, "reason") ?? string.Empty)).ToList();
    }

    /// <summary>
    /// Plans the closing of a goal's run by a principal now, with one reason for every unsettled step a step's own reason does not cover:
    /// refused, with every reason at once, unless the goal is a run whose flow resolves here, it was activated and has neither ended nor
    /// been closed, the principal is who decides for it and a person, the run has a step it has not settled, each step reason names one of
    /// those steps once, and every one of them has a reason. An achieved goal's run may still be closed, which records why it did not
    /// complete.
    /// </summary>
    public static Plan Prepare(EstateModel model, RecordEnvelope goal, string by, string? reason, IReadOnlyList<(string Step, string Reason)> steps, DateTimeOffset at)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(goal);
        ArgumentNullException.ThrowIfNull(steps);
        var refusals = new List<string>();
        refusals.AddRange(GoalRules.CloseRefusals(model, goal, by).Select(r => r.Message));
        string status = GoalRules.Status(model, goal);
        IReadOnlyList<RecordEnvelope> statuses = GoalRules.Statuses(model, goal);
        if (GoalRules.Closing(model, goal) is RecordEnvelope closed)
        {
            refusals.Add($"Goal {goal.Id}'s run was already closed by goal-status {closed.Id}.");
        }
        else if (statuses.Count == 0)
        {
            refusals.Add($"Goal {goal.Id} was never activated; a goal that never ran is stopped, not closed.");
        }
        else if (EstateModel.BodyString(statuses[^1], "status") is string last && GoalRules.Ended.IndexOf(last) >= 0)
        {
            refusals.Add($"Goal {goal.Id} ended as {last}; its run is not closed.");
        }

        if (!FlowRuns.IsRun(goal))
        {
            refusals.Add($"Goal {goal.Id} is not a run of a flow; a goal without a run is accepted or stopped.");
            return new Plan([], string.Empty, refusals);
        }

        FlowPosition.Position position = FlowPosition.Derive(model, goal, at);
        if (position.Flow is null)
        {
            refusals.Add($"The run's {position.Unavailable}; a run is closed where its flow resolves.");
            return new Plan([], string.Empty, refusals);
        }

        List<FlowPosition.StepInstance> open = position.Steps.Where(s => s.State is not ("done" or "off")).ToList();
        if (open.Count == 0)
        {
            refusals.Add(status == "achieved"
                ? $"Goal {goal.Id}'s run has no unsettled step: every step is done or off, so there is nothing to close."
                : $"Goal {goal.Id}'s run has no unsettled step: every step is done or off, so the run is complete and its goal is accepted, not closed.");
        }

        var given = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach ((string step, string why) in steps)
        {
            if (string.IsNullOrWhiteSpace(why))
            {
                refusals.Add($"The reason for step {step} is blank.");
            }
            else if (why.Trim().Length > MaximumReason)
            {
                refusals.Add($"The reason for step {step} is longer than {MaximumReason} characters.");
            }
            else if (open.All(s => s.Name != step))
            {
                refusals.Add(position.Steps.FirstOrDefault(s => s.Name == step) is FlowPosition.StepInstance settled
                    ? $"Step {step} is {settled.State}, not unsettled; only a step that is neither done nor off is closed with a reason."
                    : $"The run has no step {step}; name a step instance as key, or key[instance] for a step that runs over a list.");
            }
            else if (!given.TryAdd(step, why.Trim()))
            {
                refusals.Add($"Step {step} is given more than one reason.");
            }
        }

        string? general = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
        if (reason is not null && general is null)
        {
            refusals.Add("The reason is blank.");
        }
        else if (general is { Length: > MaximumReason })
        {
            refusals.Add($"The reason is longer than {MaximumReason} characters.");
        }

        List<string> missing = open.Where(s => !given.ContainsKey(s.Name) && general is null).Select(s => $"{s.Name} ({s.State})").ToList();
        if (missing.Count > 0)
        {
            refusals.Add($"Every unsettled step needs a reason: give one for all, or one for each of {string.Join(", ", missing)}.");
        }

        if (refusals.Count > 0)
        {
            return new Plan([], string.Empty, refusals);
        }

        List<Unsettled> unsettled = open.Select(s => new Unsettled(s.Step.Key, s.Instance, given.TryGetValue(s.Name, out string? own) ? own : general!)).ToList();
        string note = general ?? $"Closed by who decides for the goal with {unsettled.Count} unsettled step{(unsettled.Count == 1 ? string.Empty : "s")}, each with its reason.";
        return new Plan(unsettled, note, []);
    }
}
