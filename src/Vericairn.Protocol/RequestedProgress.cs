// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Globalization;
using System.Text.Json;

namespace Vericairn.Protocol;

/// <summary>A current agent claim and its reporting deadline, beside an independent worker observation. Reading never stops a run or establishes completion.</summary>
public sealed record RequestedProgress(string Assignment, string? Attempt, string By, string Agent, string? Run,
    WorkProgressClaim? Claim, JsonElement? Request, AttentionReference? Source, string Freshness, string AsOf,
    long QuietSeconds, long? CadenceSeconds, bool Overdue, bool Closed, RunLiveness? Liveness, bool WorkerObserved,
    string? Problem = null);

/// <summary>Shared wording for requested status. Optional narration preferences never hide this status or worker liveness.</summary>
public static class ProgressClaimPresentation
{
    /// <summary>Render a typed value without turning a count into an estimate or an estimate into evidence.</summary>
    public static string Value(JsonElement? progress)
    {
        if (progress is not { ValueKind: JsonValueKind.Object } value) return "no typed value";
        string? type = value.GetProperty("type").GetString();
        return type switch
        {
            "accurate" => "accurate: " + value.GetProperty("done").GetRawText()
                + (value.TryGetProperty("total", out JsonElement total) ? " of " + total.GetRawText() : "")
                + " " + value.GetProperty("unit").GetString()
                + (value.TryGetProperty("estimatePercent", out JsonElement estimate) ? "; best-effort estimate " + estimate.GetRawText() + "%" : ""),
            "best-effort" => "best-effort estimate: " + value.GetProperty("percent").GetRawText() + "%",
            "not-measurable" => "not-measurable: in progress",
            _ => "unavailable typed value",
        };
    }

    /// <summary>The claim's status and freshness, always attributed and explicitly separate from completion evidence.</summary>
    public static string Claim(RequestedProgress progress)
    {
        ArgumentNullException.ThrowIfNull(progress);
        return $"Agent's progress claim by {progress.Agent} ({progress.By}); assignment {progress.Assignment}"
            + (progress.Attempt is null ? "; no attempt yet" : "; attempt " + progress.Attempt)
            + (progress.Run is null ? "" : "; run " + progress.Run)
            + $": {progress.Freshness}; "
            + (progress.Claim is { } claim ? Value(claim.Progress) + "; " + claim.StatusText + "; claimed at " + claim.At : "no claim")
            + "; quiet " + progress.QuietSeconds.ToString(CultureInfo.InvariantCulture) + " seconds"
            + (progress.CadenceSeconds is long cadence ? "; cadence " + cadence.ToString(CultureInfo.InvariantCulture) + " seconds" : "")
            + (progress.Closed ? "; context only, never evidence of completion" : "; not a worker observation or evidence of completion")
            + (progress.Problem is null ? "" : "; unavailable: " + progress.Problem);
    }

    /// <summary>The worker's independent event-stream observation, with no liveness inferred from a claim.</summary>
    public static string Liveness(RequestedProgress progress)
    {
        ArgumentNullException.ThrowIfNull(progress);
        return "Worker liveness: " + (progress.Liveness is not { } live ? "unavailable; no event-stream observation"
            : "last event " + live.LastEventAt + "; stall limit " + live.StallSeconds.ToString(CultureInfo.InvariantCulture) + " seconds"
                + (live.AttentionAt is null ? "" : "; stall attention at " + live.AttentionAt)
                + (live.StoppedAt is null ? "" : "; stall stop at " + live.StoppedAt))
            + "; worker heartbeat " + (progress.WorkerObserved ? "current" : "not observed current");
    }

    /// <summary>Render the claim beside liveness on status, progress and work views.</summary>
    public static string Render(IEnumerable<RequestedProgress>? progress) => progress is null ? "" :
        string.Concat(progress.Select(p => "\n" + Claim(p) + "\n  " + Liveness(p) + "\n"));
}
