// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Vericairn.Protocol;
using Vericairn.Records.Estate;
using Vericairn.Records.Identity;
using Vericairn.Records.Model;
using Vericairn.Runtime.Workers;
using static Vericairn.Runtime.Service.EstateServiceValues;

namespace Vericairn.Runtime.Service;

internal sealed partial class RunDispatchService
{
    // Startup failures remain visible even when the observation itself cannot be updated.
    private readonly Dictionary<string, string> settlementProblems = new(StringComparer.Ordinal);

    internal RunSettleResponse RunSettle(string run, RunSettleRequest request)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(run);
        ArgumentNullException.ThrowIfNull(request);
        lock (gate)
        {
            EstateModel model = session.Load();
            if (!Ulid.IsValid(request.By) || model.CurrentPrincipal(request.By) is null)
                return SettlementProblem(run, "refused", "The principal asking for settlement must name a principal in this estate.");
            AgentRuns.Run? found = AgentRuns.Find(model, run);
            if (found is null) return SettlementProblem(run, "not-found", $"No agent run has id or identity {run}.");
            if (StopRefusal(model, found, request.By, "settle") is string refused)
                return SettlementProblem(found.Id, "refused", refused);
            if (recordAccess.RequestDeviceRefusal(model) is Problem denied)
                return SettlementProblem(found.Id, denied.Code, denied.Message);
            return SettleRetained(found, request.By, request.RecordsChecked);
        }
    }

    // The host calls this after interrupted batches have been judged, before serving requests. It uses the
    // original author's current standing and the ordinary record writer; recovery grants no new authority.
    internal void RecoverSettlements()
    {
        lock (gate)
        {
            foreach (AgentRuns.Run run in AgentRuns.All(session.Load()).Where(r => r.InFlight))
            {
                RunSettleResponse response = SettleRetained(run, run.Intent.By, recordsChecked: false);
                if (!response.Ok && response.Problem?.Code is not ("in-flight" or "no-observation")) settlementProblems[run.Id] = response.Message;
            }
        }
    }

    // Caller holds the service gate through the observation, latest-revision check and settlement. An active
    // supervisor owns its final audits until it returns, even after publishing the boundary's exit.
    private RunSettleResponse SettleRetained(AgentRuns.Run run, string by, bool recordsChecked, bool unobservedOnly = false)
    {
        if (!run.InFlight)
            return SettlementProblem(run.Id, "already-settled", $"Run {run.Id} ended as {run.Status}; there is nothing to settle.", run.Status);

        RunDirectory directory = WorkerOptions.OpenRunDirectory(session.Manifest.Estate, run.Id);
        RunObservedState? observed;
        try { observed = directory.ReadForSettlement(); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException or ArgumentException or InvalidOperationException)
        {
            return Unresolved("The worker's retained run observation could not be read; settlement remains unresolved.");
        }
        // A run another machine supervised has no run directory here, and that absence is not a local concern; a lost
        // observation in this machine's own run directory stays unresolved.
        if (observed is null)
            return Directory.Exists(directory.Path)
                ? Unresolved("No retained run observation is available on this machine; settlement remains unresolved.")
                : SettlementProblem(run.Id, "no-observation", "This machine did not supervise the run and retains no observation of it; settle it where it ran.", run.Status);
        if (observed.Run != run.Id) return Unresolved("The retained observation names a different run; settlement remains unresolved.");
        // A marker without a settlement record can mean the record sits on a branch or copy not yet merged, so settling
        // here could record the run twice. Only a requester who has checked every branch and copy settles it, never startup.
        string? unrecordedMarker = observed.Settled;
        if (unrecordedMarker is not null && !recordsChecked)
            return Unresolved($"The local observation is marked settled as {unrecordedMarker}, but these records hold no settlement for run {run.Id}. "
                + "Merge any branch or copy of the estate that holds it; once none does, retry with --records-checked to record the settlement from the retained observation.");
        if (observed.ExitedAt is null)
        {
            const string notExited = "The run's boundary has not been observed exited; no settlement can be established.";
            if (unrecordedMarker is not null) return SettlementProblem(run.Id, "in-flight", notExited, run.Status);
            if (LostObservation(run, observed, out string why) is not UnobservedExit lost)
                return SettlementProblem(run.Id, "in-flight", notExited + " " + why, run.Status);
            return SettleUnobserved(run, by, directory, observed, lost);
        }
        if (unobservedOnly)
            return SettlementProblem(run.Id, "exited", "The run's exit was observed; worker settle settles it from the retained observation.", run.Status);
        if (supervisions.TryGetValue(run.Id, out var supervision) && !supervision.Task.IsCompleted && observed.Problem is null)
            return SettlementProblem(run.Id, "in-flight", "The worker is still supervising this run or completing its final audits; retry after supervision finishes.", run.Status);
        if (!TryOutcome(observed, out RunOutcome? outcome, out string? problem)) return Unresolved(problem!);
        string? dispatchedAt = EstateModel.BodyString(run.Latest, "dispatchedAt");
        if (dispatchedAt is not null && dispatchedAt != observed.LaunchedAt)
            return Unresolved("The retained launch time differs from the recorded dispatch; settlement remains unresolved.");

        RunScope scope = observed.Scope ?? new RunScope(0, 0, [], "The final workspace scope audit was not retained; scope is unverified.");
        string? recoveryNote = null;
        if (observed.Outcome is null || observed.Scope is null || !observed.SettlementPrepared)
        {
            // Older snapshots contain exit and usage observations, but no final scope audit, price or monotonic
            // duration. Preserve their facts, leave unreported units charged at the cap, and disclose the gap.
            string summary = observed.Outcome is null
                ? "Settlement recovered from retained exit facts. Elapsed time uses the retained UTC launch and exit timestamps; monotonic duration and currency usage were not retained."
                : "Settlement recovered before all final audits were retained.";
            if (observed.Scope is null) summary += " The final scope audit is unverified.";
            if (!observed.SettlementPrepared) summary += " The final attempt audit is unverified.";
            recoveryNote = summary;
            List<RunSignal> signals = [.. outcome!.Signals ?? []];
            // Keep an existing observation whole, including a summary already at its record limit.
            // The evidence below always discloses recovery's gaps without rewriting that signal.
            if (!signals.Any(s => s.Code == "audit-unverified" && s.Severity == "warning"))
                signals.Add(new RunSignal("audit-unverified", "warning", observed.ExitedAt, summary));
            outcome = outcome with { Signals = signals };
        }

        return RecordSettlement(run, by, directory, observed, outcome!.LaunchedAt, previous =>
        {
            JsonObject body = Settle(previous, outcome!, scope,
                TimeSpan.FromTicks(observed.MinuteTicks ?? TimeSpan.TicksPerMinute), observed.HeartbeatSeconds);
            if (recoveryNote is not null) body["evidence"]!.AsArray().Add(new JsonObject { ["summary"] = recoveryNote });
            if (unrecordedMarker is not null)
                body["evidence"]!.AsArray().Add(new JsonObject { ["summary"] = $"The retained observation was marked settled as {unrecordedMarker}, but the records held no settlement; the requester confirmed that no branch or copy of the estate holds one, and the settlement is recorded from the retained facts." });
        }, status => $"Run {run.Id} settled as {status} from its retained observation; its branch is released and its observed usage is charged.");

        RunSettleResponse Unresolved(string reason, IReadOnlyList<FindingDto>? findings = null) => UnresolvedSettlement(run, reason, findings);
    }

    // Caller holds the service gate. Records the settlement exactly once, through the ordinary writer under the run's latest
    // revision, then marks the retained observation settled and the goal budget-limited when a cap is crossed.
    private RunSettleResponse RecordSettlement(AgentRuns.Run run, string by, RunDirectory directory, RunObservedState observed, DateTimeOffset launchedAt,
        Action<JsonObject> settle, Func<string, string> settledMessage)
    {
        WriteOutcome settled;
        string? status = null;
        try
        {
            if (run.Status == "not-started")
            {
                WriteOutcome dispatch = RecordDispatch(run.Id, by, launchedAt);
                if (!dispatch.Ok) return UnresolvedSettlement(run, "The observed dispatch could not be recorded: " + dispatch.Problem?.Message, dispatch.Problem?.Findings);
            }
            settled = Revise(run.Id, by, previous =>
            {
                settle(previous);
                status = previous["status"]!.GetValue<string>();
            });
            if (!settled.Ok) return UnresolvedSettlement(run, "The observed settlement could not be recorded: " + settled.Problem?.Message, settled.Problem?.Findings);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException or JsonException)
        {
            return UnresolvedSettlement(run, "The observed settlement could not be written; the retained observation remains available for another request.");
        }

        settlementProblems.Remove(run.Id);
        observed.Settled = status;
        observed.Problem = null;
        string message = settledMessage(status!);
        try { directory.Write(observed); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            message += " The settlement is recorded, but the local settlement marker could not be updated.";
        }
        try
        {
            EstateModel model = session.Load();
            if (run.Goal is string goalId && AgentRuns.GoalAt(model, goalId) is RecordEnvelope goal
                && RunAdmissionService.Crossed(AgentRuns.Of(model, goal)) is { Count: > 0 } crossed)
            {
                AgentRuns.Ledger ledger = AgentRuns.Of(model, goal);
                runAdmission.MarkBudgetLimited(model, goal, by, string.Join("; ", crossed.Select(ledger.Describe)) + ".");
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            message += " The goal's budget status could not be updated; its ledger still includes the recorded charge.";
        }
        return new RunSettleResponse(run.Id, status!, message, true, settled.Written);
    }

    private RunSettleResponse UnresolvedSettlement(AgentRuns.Run run, string reason, IReadOnlyList<FindingDto>? findings = null)
    {
        settlementProblems[run.Id] = reason;
        return new(run.Id, run.Status, reason, false, Problem: new Problem("unresolved", reason, findings ?? []));
    }

    /// <summary>A run whose supervising worker died: when it was launched and last observed running, when its loss was established, and its stall limit.</summary>
    private sealed record UnobservedExit(DateTimeOffset LaunchedAt, DateTimeOffset LastHeartbeat, DateTimeOffset CheckedAt, int StallSeconds);

    // A run whose supervising worker died is never observed exiting. It is lost only when this service does not supervise it,
    // no heartbeat, output or event came for longer than its stall limit (and the freshness window), the recorded worker
    // process is gone and the harness's whole boundary is gone. Anything less may still be observed and stays in flight;
    // the reason says what is missing. The process checks come last, after the retained facts.
    private UnobservedExit? LostObservation(AgentRuns.Run run, RunObservedState observed, out string reason)
    {
        if (supervisions.TryGetValue(run.Id, out var supervision) && !supervision.Task.IsCompleted)
        {
            reason = "The worker is still supervising this run on this machine.";
            return null;
        }
        if (!Timestamp(observed.LaunchedAt, out DateTimeOffset launched) || !Timestamp(observed.Heartbeat, out DateTimeOffset heartbeat) || heartbeat < launched)
        {
            reason = "The retained observation has no valid launch and heartbeat times, so its supervising worker cannot be established lost.";
            return null;
        }
        int stall = int.TryParse(run.Target("stallSeconds"), NumberStyles.None, CultureInfo.InvariantCulture, out int recorded) && recorded > 0
            ? recorded : observed.Liveness?.StallSeconds ?? 0;
        if (stall <= 0)
        {
            reason = "The run's stall limit is unknown, so its supervising worker cannot be established lost.";
            return null;
        }
        DateTimeOffset last = heartbeat;
        foreach (string? at in new[] { observed.Output, observed.Liveness?.LastEventAt })
            if (Timestamp(at, out DateTimeOffset seen) && seen > last) last = seen;
        double beat = double.IsFinite(observed.HeartbeatSeconds) && observed.HeartbeatSeconds > 0 ? Math.Min(observed.HeartbeatSeconds, 86400) : 0;
        TimeSpan silence = TimeSpan.FromSeconds(Math.Max(stall, Math.Max(3 * beat, 2) + 2));
        DateTimeOffset now = WorkerOptions.Clock.GetUtcNow();
        if (now - last <= silence)
        {
            reason = $"It was last observed at {RecordParser.FormatTimestamp(last)}, within its stall limit of {stall.ToString(CultureInfo.InvariantCulture)} seconds.";
            return null;
        }
        if (!RunProcesses.Gone(observed.Supervisor, launched))
        {
            reason = "The worker process that supervised it is still running on this machine, or cannot be inspected.";
            return null;
        }
        if (observed.ProcessId is not int harness || !RunProcesses.BoundaryGone(harness, launched))
        {
            reason = "Its harness is still running on this machine, or cannot be inspected.";
            return null;
        }
        reason = string.Empty;
        return new UnobservedExit(launched, heartbeat, now, stall);
    }

    // Caller holds the service gate. The run exited without observation: settled as a known failure, charged the usage observed
    // up to its last heartbeat, at most its cap, and its cap in each unit not observed. The workspace is not touched, so it keeps
    // its commits and uncommitted files for the next attempt; settlement releases the branch.
    private RunSettleResponse SettleUnobserved(AgentRuns.Run run, string by, RunDirectory directory, RunObservedState observed, UnobservedExit lost)
    {
        if (observed.Heartbeats < 0 || observed.Tokens < 0 || observed.MinuteTicks <= 0 || observed.Signals is null || observed.Signals.Any(s => s is null))
            return UnresolvedSettlement(run, "The retained observation lacks valid heartbeat, usage or signal facts; settlement remains unresolved.");
        string? dispatchedAt = EstateModel.BodyString(run.Latest, "dispatchedAt");
        if (dispatchedAt is not null && dispatchedAt != observed.LaunchedAt)
            return UnresolvedSettlement(run, "The retained launch time differs from the recorded dispatch; settlement remains unresolved.");
        RunStop? stop;
        try { stop = observed.Stop ?? Requested(directory.ReadStop()); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return UnresolvedSettlement(run, "The local stop request could not be read, so the stop's facts cannot be recorded; settlement remains unresolved.");
        }

        string heartbeat = RecordParser.FormatTimestamp(lost.LastHeartbeat);
        string stall = lost.StallSeconds.ToString(CultureInfo.InvariantCulture);
        return RecordSettlement(run, by, directory, observed, lost.LaunchedAt, previous => UnobservedSettlement(previous, observed, lost, stop),
            status => $"Run {run.Id} settled as {status}, exited without observation: the worker that supervised it and its harness are gone, and it was last observed at its heartbeat at {heartbeat}, longer ago than its stall limit of {stall} seconds. "
                + "Its branch is released; its workspace, with its commits and uncommitted files, is kept for the next attempt; it is charged the usage observed up to that heartbeat, and its cap in each unit not observed.");
    }

    private static void UnobservedSettlement(JsonObject body, RunObservedState observed, UnobservedExit lost, RunStop? stop)
    {
        JsonElement cap = body["cap"] is JsonNode node ? Element(node.AsObject()) : default;
        string launched = RecordParser.FormatTimestamp(lost.LaunchedAt);
        string heartbeat = RecordParser.FormatTimestamp(lost.LastHeartbeat);
        string checkedAt = RecordParser.FormatTimestamp(lost.CheckedAt);
        string stall = lost.StallSeconds.ToString(CultureInfo.InvariantCulture);
        body["status"] = "known-failure";
        string summary = $"Exited without observation. The worker that supervised the run last observed it running at {heartbeat}; at {checkedAt} neither that worker's process nor the harness's process boundary was running on the machine that ran it, and nothing had been observed for longer than its stall limit of {stall} seconds. "
            + "Its exit code, its final audits and any usage after that heartbeat are unknown: it is charged the usage observed up to that heartbeat, at most its cap, and its cap in each unit not observed.";
        if (stop is not null)
        {
            body["stop"] = Stop(stop);
            summary += $" The stop requested at {stop.RequestedAt} was {(stop.TransmittedAt is null ? "never transmitted" : "transmitted at " + stop.TransmittedAt)}, and its containment was not observed.";
        }

        List<RunSignal> signals = [.. observed.Signals];
        // Keep an existing observation whole, as recovery does; the evidence below always discloses what was not observed.
        if (!signals.Any(s => s.Code == "audit-unverified" && s.Severity == "warning"))
            signals.Add(new RunSignal("audit-unverified", "warning", checkedAt, "The run exited without observation: its exit, final workspace scope audit and attempt audit were not observed, so they are unverified."));
        if (body["execution"] is JsonObject execution)
            body["executionObserved"] = observed.Execution is JsonElement facts ? JsonNode.Parse(facts.GetRawText()) : new RunExecutionObservation(Element(execution)).Snapshot();
        body["scope"] = JsonSerializer.SerializeToNode(new RunScope(0, 0, [], "The run exited without observation, so no final workspace scope audit was made; scope is unverified."), RunRecordJson);
        body["signals"] = JsonSerializer.SerializeToNode(signals, RunRecordJson);
        body["observed"] = new JsonObject { ["summary"] = summary, ["at"] = checkedAt };
        body["dispatchedAt"] ??= launched;
        body["usage"] = Usage(cap, lost.LastHeartbeat - lost.LaunchedAt, TimeSpan.FromTicks(observed.MinuteTicks ?? TimeSpan.TicksPerMinute), observed.Tokens, null, null, atMostCap: true);
        if (observed.Liveness is not null) body["liveness"] = JsonSerializer.SerializeToNode(observed.Liveness, RunRecordJson);
        JsonArray evidence = body["evidence"] as JsonArray ?? [];
        evidence.Add(new JsonObject { ["summary"] = $"Observed by the worker on the machine that ran it at {observed.Heartbeats.ToString(CultureInfo.InvariantCulture)} heartbeats every {observed.HeartbeatSeconds.ToString(CultureInfo.InvariantCulture)} seconds, from {launched} to its last heartbeat at {heartbeat}, when that worker stopped observing it. "
            + $"At {checkedAt} the worker settling it found that worker's process and the harness's process boundary gone, with nothing observed for longer than the stall limit of {stall} seconds. Its exit was not observed; its output, if any, is kept on that machine." });
        body["evidence"] = evidence;
    }

    private static bool TryOutcome(RunObservedState state, out RunOutcome? outcome, out string? problem)
    {
        outcome = null;
        problem = "The retained observation lacks valid launch, exit, usage, heartbeat or signal facts; settlement remains unresolved.";
        if (state.ExitCode is not int exitCode || !Timestamp(state.LaunchedAt, out DateTimeOffset launched)
            || !Timestamp(state.ExitedAt, out DateTimeOffset exited) || state.Heartbeats < 0 || state.Tokens < 0
            || !double.IsFinite(state.HeartbeatSeconds) || state.HeartbeatSeconds <= 0 || state.MinuteTicks <= 0
            || state.Signals is null || state.Signals.Any(s => s is null))
            return false;
        if (state.Outcome is RunOutcome retained)
        {
            if (RecordParser.FormatTimestamp(retained.LaunchedAt) != state.LaunchedAt
                || RecordParser.FormatTimestamp(retained.ExitedAt) != state.ExitedAt || retained.ExitCode != exitCode
                || retained.Heartbeats != state.Heartbeats || retained.Tokens != state.Tokens || retained.Stop != state.Stop
                || retained.Price < 0 || retained.Elapsed < TimeSpan.Zero || retained.Signals is null || retained.Signals.Any(s => s is null)
                || (retained.Elapsed is null && retained.ExitedAt < retained.LaunchedAt)) return false;
            outcome = retained;
        }
        else
        {
            if (exited < launched) return false;
            outcome = new RunOutcome(exitCode, launched, exited, state.Heartbeats, state.Tokens, null, null,
                state.Stop, state.EscalationFailure, state.Liveness, Signals: state.Signals, Execution: state.Execution);
        }
        problem = null;
        return true;
    }

    private static bool Timestamp(string? value, out DateTimeOffset time) => DateTimeOffset.TryParseExact(value,
        "yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out time);

    private static RunSettleResponse SettlementProblem(string run, string code, string message, string status = "none") =>
        new(run, status, message, false, Problem: new Problem(code, message, []));
}
