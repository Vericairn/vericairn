// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using Vericairn.Records.Canonical;
using Vericairn.Records.Model;

namespace Vericairn.Records.Estate;

/// <summary>An effective request and its exact source, or an unresolved choice of goal.</summary>
/// <param name="Request">The complete winning request; null means nothing is requested.</param>
/// <param name="Scope">estate, roster, goal or brief.</param>
/// <param name="Record">The winning source record.</param>
/// <param name="Hash">Its exact hash.</param>
/// <param name="Problem">Why a request cannot be resolved without choosing a goal or setting a brief override.</param>
public sealed record ProgressRequestSelection(JsonElement? Request = null, string? Scope = null, string? Record = null, string? Hash = null, string? Problem = null);

/// <summary>Requested reporting, independent of communication preferences, execution choices and worker liveness.</summary>
public static class ProgressRequests
{
    /// <summary>Resolve complete requests, most specific wins: brief, goal, role's roster entry, estate.</summary>
    public static ProgressRequestSelection Resolve(EstateModel model, RecordEnvelope assignment, RecordEnvelope? goal = null)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(assignment);
        if (assignment.Body.TryGetProperty("brief", out JsonElement brief) && Select(assignment, brief, "brief") is { } explicitRequest)
            return explicitRequest;

        return ResolveInherited(model, WorkView.OrderOfAssignment(model, assignment), assignment.Id, goal);
    }

    /// <summary>Resolve inherited reporting for a work order before its assignment is prepared. An ambiguous goal remains an explicit problem.</summary>
    public static ProgressRequestSelection ResolveForWork(EstateModel model, RecordEnvelope order)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(order);
        return ResolveInherited(model, order, null, null);
    }

    private static ProgressRequestSelection ResolveInherited(EstateModel model, RecordEnvelope? order, string? assignment, RecordEnvelope? goal)
    {
        // Preserve the unconfigured path without traversing goals, routing or the run ledger.
        if (model.CurrentEstate?.Body.TryGetProperty("progressRequest", out _) != true
            && !model.Current("goal").Any(g => g.Body.TryGetProperty("progressRequest", out _))
            && !model.Current("roster").Any(r => r.Body.TryGetProperty("roles", out JsonElement roles)
                && roles.ValueKind == JsonValueKind.Array && roles.EnumerateArray().Any(entry => entry.ValueKind == JsonValueKind.Object && entry.TryGetProperty("progressRequest", out _))))
            return new();

        string role = order is null ? Routing.DefaultRole : Routing.RoleOf(model.Latest(order));
        if (goal is not null) return ForGoal(model, model.Latest(goal), role);
        IReadOnlyList<RecordEnvelope> goals = order is null ? [] : Routing.GoalsOf(model, order);
        // A running assignment uses the goal its worker selected, including when work belongs to several goals.
        string[] runningGoals = AgentRuns.All(model).Where(r => r.InFlight && assignment is not null && r.Assignment == assignment && r.Goal is not null)
            .Select(r => r.Goal!).Distinct(StringComparer.Ordinal).ToArray();
        if (runningGoals.Length == 1 && GoalView.Find(model, runningGoals[0]) is { } runningGoal)
            return ForGoal(model, runningGoal, role);
        RecordEnvelope[] active = goals.Where(g => GoalRules.Status(model, g) == "active").ToArray();
        if (active.Length == 1) return ForGoal(model, active[0], role);
        if (goals.Count <= 1) return ForGoal(model, goals.Count == 0 ? null : goals[0], role);

        // No arbitrary goal wins. Identical requirements remain usable; an explicit brief can settle a conflict.
        ProgressRequestSelection[] choices = (active.Length > 1 ? active : goals).Select(g => ForGoal(model, g, role)).ToArray();
        string?[] requests = choices.Select(c => c.Request is { } request ? CanonicalJson.Serialize(request) : null).Distinct(StringComparer.Ordinal).ToArray();
        return requests.Length == 1 ? choices[0]
            : new(Problem: "Several goals request different progress for this assignment. Select its run goal or set progressRequest in its brief.");
    }

    private static ProgressRequestSelection ForGoal(EstateModel model, RecordEnvelope? goal, string role)
    {
        if (goal is not null && Select(goal, goal.Body, "goal") is { } goalRequest) return goalRequest;
        RecordEnvelope? roster = goal is not null && EstateModel.BodyString(goal, "roster") is string named
            && model.ById.TryGetValue(named, out RecordEnvelope? record) && record.Type == "roster"
                ? model.Latest(record) : model.Current("roster").OrderBy(r => r.Id, StringComparer.Ordinal).FirstOrDefault();
        if (roster is not null && roster.Body.TryGetProperty("roles", out JsonElement roles))
        {
            foreach (JsonElement entry in roles.EnumerateArray())
                if (entry.GetProperty("role").GetString() == role && Select(roster, entry, "roster") is { } roleRequest)
                    return roleRequest;
        }
        return model.CurrentEstate is { } estate && Select(estate, estate.Body, "estate") is { } estateRequest ? estateRequest : new();
    }

    private static ProgressRequestSelection? Select(RecordEnvelope record, JsonElement body, string scope) =>
        body.TryGetProperty("progressRequest", out JsonElement request) ? new(request, scope, record.Id, record.ComputedHash) : null;
}
