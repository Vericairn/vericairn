// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using System.Text.Json.Nodes;
using Vericairn.Protocol;
using Vericairn.Records.Estate;
using Vericairn.Records.Model;
using static Vericairn.Runtime.Service.EstateServiceValues;

namespace Vericairn.Runtime.Service;

public sealed partial class EstateService
{
    /// <inheritdoc />
    public RequestedProgress? WorkProgressGet(string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        lock (gate)
        {
            EstateModel model = session.Load();
            if (RequestDeviceRefusal(model) is not null) throw new UnauthorizedAccessException(ContextDenied);
            IReadOnlyList<RunRow> runs = runDispatch.Runs(model).Runs;
            if (Latest(model, id, "attempt") is { } attempt && WorkView.AssignmentOfAttempt(model, attempt) is { } assignment)
                return RequestedProgressOf(model, assignment, attempt, RunOfAttempt(model, runs, attempt));
            RunRow? run = runs.FirstOrDefault(r => r.Run == id);
            return run?.Assignment is string assigned && model.ById.TryGetValue(assigned, out assignment)
                ? RequestedProgressOf(model, assignment, AttemptOfRun(model, run), run) : null;
        }
    }

    private List<RequestedProgress> CurrentRequestedProgress(EstateModel model, RecordEnvelope? order = null)
    {
        if (model.CurrentEstate?.Body.TryGetProperty("progressRequest", out _) != true
            && !model.Current("assignment").Any(a => a.Body.TryGetProperty("brief", out JsonElement brief)
                && brief.ValueKind == JsonValueKind.Object && brief.TryGetProperty("progressRequest", out _))
            && !model.Current("goal").Any(g => g.Body.TryGetProperty("progressRequest", out _))
            && !model.Current("roster").Any(r => r.Body.TryGetProperty("roles", out JsonElement roles)
                && roles.ValueKind == JsonValueKind.Array && roles.EnumerateArray().Any(role => role.ValueKind == JsonValueKind.Object && role.TryGetProperty("progressRequest", out _)))) return [];
        var selected = new List<(RecordEnvelope Assignment, ProgressRequestSelection Request)>();
        foreach (RecordEnvelope assignment in model.Current("assignment"))
        {
            if (order is not null && (WorkView.OrderOfAssignment(model, assignment) is not { } assignedOrder
                || !model.Chain(order).Contains(assignedOrder.Id, StringComparer.Ordinal))) continue;
            ProgressRequestSelection request = ProgressRequests.Resolve(model, assignment);
            if (request.Request is not null || request.Problem is not null) selected.Add((assignment, request));
        }
        if (selected.Count == 0) return [];
        IReadOnlyList<RunRow> runs = runDispatch.Runs(model).Runs;
        var result = new List<RequestedProgress>();
        HashSet<string> inFlight = AgentRuns.All(model).Where(r => r.InFlight).Select(r => r.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var (assignment, _) in selected.OrderBy(s => s.Assignment.Id, StringComparer.Ordinal))
        {
            RunRow[] active = runs.Where(r => r.Assignment == assignment.Id && inFlight.Contains(r.Run)).ToArray();
            if (active.Length > 0)
            {
                result.AddRange(active.Select(run => RequestedProgressOf(model, assignment, AttemptOfRun(model, run), run)));
                continue;
            }
            RecordEnvelope? attempt = model.Current("attempt").Where(a => a.Subject?.Id == assignment.Id)
                .OrderBy(a => a.At, StringComparer.Ordinal).ThenBy(a => a.Id, StringComparer.Ordinal).LastOrDefault();
            if (attempt is null) continue; // An assignment alone does not start a reporting clock.
            RequestedProgress status = RequestedProgressOf(model, assignment, attempt, RunOfAttempt(model, runs, attempt));
            if (order is not null || !status.Closed) result.Add(status);
        }
        return result;
    }

    private static RecordEnvelope? AttemptOfRun(EstateModel model, RunRow run)
    {
        var window = ProgressRunWindow(model, run);
        return model.Current("attempt").Where(a => a.Subject?.Id == run.Assignment && a.By == run.Agent && InProgressRun(a, window))
            .OrderBy(a => a.At, StringComparer.Ordinal).ThenBy(a => a.Id, StringComparer.Ordinal).LastOrDefault();
    }

    private static RunRow? RunOfAttempt(EstateModel model, IReadOnlyList<RunRow> runs, RecordEnvelope attempt) =>
        runs.Where(r => r.Assignment == attempt.Subject?.Id && r.Agent == attempt.By && InProgressRun(attempt, ProgressRunWindow(model, r)))
            .OrderBy(r => model.ById[r.Run].At, StringComparer.Ordinal).ThenBy(r => r.Run, StringComparer.Ordinal).LastOrDefault();

    private static (RecordEnvelope Start, RecordEnvelope? Next, RecordEnvelope? End) ProgressRunWindow(EstateModel model, RunRow run)
    {
        AgentRuns.Run ledger = AgentRuns.Find(model, run.Run)!;
        RecordEnvelope? next = AgentRuns.All(model).Where(r => r.Assignment == run.Assignment && r.Principal == run.Agent
                && ProgressRecordOrder(r.Intent, ledger.Intent) > 0)
            .OrderBy(r => r.Intent.At, StringComparer.Ordinal).ThenBy(r => r.Id, StringComparer.Ordinal).Select(r => r.Intent).FirstOrDefault();
        return (ledger.Intent, next, ledger.InFlight ? null : ledger.Latest);
    }

    private static bool InProgressRun(RecordEnvelope attempt, (RecordEnvelope Start, RecordEnvelope? Next, RecordEnvelope? End) window) =>
        ProgressRecordOrder(attempt, window.Start) >= 0 && (window.Next is null || ProgressRecordOrder(attempt, window.Next) < 0)
        && (window.End is null || ProgressRecordOrder(attempt, window.End) <= 0);

    private static int ProgressRecordOrder(RecordEnvelope left, RecordEnvelope right) =>
        left.At == right.At ? string.CompareOrdinal(left.Id, right.Id) : string.CompareOrdinal(left.At, right.At);

    private RequestedProgress RequestedProgressOf(EstateModel model, RecordEnvelope assignment, RecordEnvelope? attempt, RunRow? run)
    {
        RecordEnvelope? goal = run?.Goal is string goalId ? GoalView.Find(model, goalId) : null;
        ProgressRequestSelection selected = ProgressRequests.Resolve(model, assignment, goal);
        WorkProgressClaim? claim = attempt is null ? null : LatestProgressClaim(model, attempt);
        bool closed = attempt is not null && WorkView.ResultOf(model, attempt) is not null
            || run is not null && !AgentRuns.All(model).Any(r => r.Id == run.Run && r.InFlight);
        DateTimeOffset now = WorkerOptions.Clock.GetUtcNow();
        DateTimeOffset start = attempt?.AtTime ?? (run is not null ? model.ById[run.Run].AtTime : assignment.AtTime);
        if (selected.Record is string source && model.ById[source].AtTime > start) start = model.ById[source].AtTime;
        DateTimeOffset? claimed = ParseAt(claim?.At);
        long quiet = (long)Math.Max(0, (now - (claimed ?? start)).TotalSeconds);
        long? cadence = selected.Request is { } request && request.GetProperty("cadence").TryGetProperty("minutes", out JsonElement minutes)
            ? minutes.GetInt64() * 60 : null;
        bool matches = claim?.Progress is { } value && selected.Request is { } requirement
            && (!requirement.TryGetProperty("types", out JsonElement types)
                || types.EnumerateArray().Any(t => t.GetString() == value.GetProperty("type").GetString())
                    && (!value.TryGetProperty("estimatePercent", out _) || types.EnumerateArray().Any(t => t.GetString() == "best-effort")))
            && (!requirement.TryGetProperty("statusText", out JsonElement status) || !status.GetBoolean() || !string.IsNullOrWhiteSpace(claim.StatusText))
            && claimed is not null && claimed <= now;
        bool due = cadence is null || (now - (matches ? claimed!.Value : start)).TotalSeconds >= cadence;
        string freshness = selected.Problem is not null ? "unavailable" : selected.Request is null ? "not-requested"
            : matches && !due ? "fresh" : matches && cadence is null ? "fresh"
            : claim is not null ? "stale" : due ? "overdue" : "pending";
        bool overdue = !closed && selected.Request is not null && selected.Problem is null && due && freshness != "fresh";
        string by = attempt?.By ?? run?.Agent ?? EstateModel.BodyString(assignment, "contributor")!;
        string name = model.CurrentPrincipal(by) is { } principal ? EstateModel.BodyString(principal, "name") ?? by : by;
        return new RequestedProgress(assignment.Id, attempt?.Id, by, name, run?.Run, claim, selected.Request,
            selected.Record is null ? null : new AttentionReference(selected.Scope == "brief" ? "assignment" : selected.Scope!, selected.Record, selected.Hash),
            freshness, RecordParser.FormatTimestamp(now), quiet, cadence, overdue, closed, run?.Liveness, run?.Observed == true, selected.Problem);
    }

    private WorkProgressClaim? LatestProgressClaim(EstateModel model, RecordEnvelope attempt)
    {
        // Once submitted, the signed result is the context source even on another machine with no live store.
        if (WorkView.ResultOf(model, attempt) is { } result && result.Body.TryGetProperty("progressClaim", out JsonElement retained))
            return new WorkProgressClaim(attempt.Id, attempt.By, retained.GetProperty("phase").GetString()!, retained.GetProperty("note").GetString()!,
                retained.GetProperty("at").GetString()!, retained.TryGetProperty("completed", out JsonElement completed) && completed.GetBoolean(),
                retained.TryGetProperty("record", out JsonElement record) ? record.GetString() : null)
            {
                Progress = retained.TryGetProperty("progress", out JsonElement value) ? value.Clone() : null,
                StatusText = retained.TryGetProperty("statusText", out JsonElement status) ? status.GetString() : null,
            };
        RecordEnvelope? recorded = model.Current("work-progress").Where(r => r.Subject?.Id == attempt.Id)
            .OrderBy(r => r.At, StringComparer.Ordinal).ThenBy(r => r.Id, StringComparer.Ordinal).LastOrDefault();
        WorkProgressClaim? claim = recorded is null ? null : Claim(recorded);
        if (session.Live.Meta("work-progress:" + attempt.Id) is string live)
        {
            try
            {
                WorkProgressClaim? latest = JsonSerializer.Deserialize<WorkProgressClaim>(live, ProtocolJson.Options);
                if (latest is not null && latest.Attempt == attempt.Id && latest.By == attempt.By && ParseAt(latest.At) is not null
                    && (claim is null || string.CompareOrdinal(latest.At, claim.At) >= 0)) claim = latest;
            }
            catch (JsonException) { } // Unreadable local state cannot count as a fresh claim.
        }
        return claim;
    }

    private static JsonObject ProgressContext(WorkProgressClaim claim)
    {
        var context = new JsonObject { ["phase"] = claim.Phase, ["note"] = claim.Note, ["at"] = claim.At, ["completed"] = claim.Completed };
        if (claim.Record is not null) context["record"] = claim.Record;
        if (claim.Progress is { } value) context["progress"] = JsonNode.Parse(value.GetRawText());
        if (claim.StatusText is not null) context["statusText"] = claim.StatusText;
        return context;
    }
}
