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
            return SettleRetained(found, request.By);
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
                RunSettleResponse response = SettleRetained(run, run.Intent.By);
                if (!response.Ok && response.Problem?.Code != "in-flight") settlementProblems[run.Id] = response.Message;
            }
        }
    }

    // Caller holds the service gate through the observation, latest-revision check and settlement. An active
    // supervisor owns its final audits until it returns, even after publishing the boundary's exit.
    private RunSettleResponse SettleRetained(AgentRuns.Run run, string by)
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
        if (observed is null) return Unresolved("No retained run observation is available on this machine; settlement remains unresolved.");
        if (observed.Run != run.Id) return Unresolved("The retained observation names a different run; settlement remains unresolved.");
        if (observed.Settled is not null) return Unresolved("The local observation claims settlement but the records do not; reconcile the records before retrying.");
        if (observed.ExitedAt is null)
            return SettlementProblem(run.Id, "in-flight", "The run's boundary has not been observed exited; no settlement can be established.", run.Status);
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

        WriteOutcome settled;
        string? status = null;
        try
        {
            if (run.Status == "not-started")
            {
                WriteOutcome dispatch = RecordDispatch(run.Id, by, outcome!.LaunchedAt);
                if (!dispatch.Ok) return Unresolved("The observed dispatch could not be recorded: " + dispatch.Problem?.Message, dispatch.Problem?.Findings);
            }
            settled = Revise(run.Id, by, previous =>
            {
                JsonObject body = Settle(previous, outcome!, scope,
                    TimeSpan.FromTicks(observed.MinuteTicks ?? TimeSpan.TicksPerMinute), observed.HeartbeatSeconds);
                if (recoveryNote is not null) body["evidence"]!.AsArray().Add(new JsonObject { ["summary"] = recoveryNote });
                status = body["status"]!.GetValue<string>();
            });
            if (!settled.Ok) return Unresolved("The observed settlement could not be recorded: " + settled.Problem?.Message, settled.Problem?.Findings);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException or JsonException)
        {
            return Unresolved("The observed settlement could not be written; the retained observation remains available for another request.");
        }

        settlementProblems.Remove(run.Id);
        observed.Settled = status;
        observed.Problem = null;
        string message = $"Run {run.Id} settled as {status} from its retained observation; its branch is released and its observed usage is charged.";
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

        RunSettleResponse Unresolved(string reason, IReadOnlyList<FindingDto>? findings = null)
        {
            settlementProblems[run.Id] = reason;
            return new(run.Id, run.Status, reason, false, Problem: new Problem("unresolved", reason, findings ?? []));
        }
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
