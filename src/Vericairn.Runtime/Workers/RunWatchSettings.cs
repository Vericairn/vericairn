// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using Vericairn.Records.Estate;
using Vericairn.Records.Model;

namespace Vericairn.Runtime.Workers;

/// <summary>Watching thresholds fixed before launch; configured reference rates are not claims of measured harness performance.</summary>
/// <param name="NormalTokensPerMinute">The launcher's configured reference rate.</param>
/// <param name="BurnMultiplier">How far above the greater of the reference and historical median a rate must rise.</param>
/// <param name="BurnMinimumTokens">The minimum observed spend in a rate sample.</param>
/// <param name="BurnWindowSeconds">The minimum rate sample duration and sliding window.</param>
/// <param name="BurnCapPercent">The fraction of the token reservation consumed before an anomalous rate is critical.</param>
/// <param name="LoopWarning">Repeated identical invocations that need attention.</param>
/// <param name="LoopStop">Repeated identical invocations that require a stop.</param>
/// <param name="LoopWindow">How many recent invocations are compared.</param>
public sealed record RunWatchSettings(long NormalTokensPerMinute = 20000, int BurnMultiplier = 4, long BurnMinimumTokens = 10000,
    int BurnWindowSeconds = 60, int BurnCapPercent = 25, int LoopWarning = 5, int LoopStop = 10, int LoopWindow = 20)
{
    internal string? Refusal => LoopWarning >= LoopStop || LoopStop > LoopWindow
        ? "The inherited watching limits must satisfy loopWarning < loopStop <= loopWindow. Revise the goal or roster limits before launching."
        : null;

    internal static RunWatchSettings Resolve(EstateModel model, RecordEnvelope goal, IAgentLauncher launcher) =>
        Overlay(Overlay(launcher.WatchDefaults, Roster(model, goal)), goal);

    // Role overrides do not discard the roster's limits. A named roster follows its current revision, just as routing does.
    internal static RecordEnvelope? Roster(EstateModel model, RecordEnvelope goal) =>
        EstateModel.BodyString(goal, "roster") is string named && model.ById.TryGetValue(named, out RecordEnvelope? record) && record.Type == "roster"
            ? model.Latest(record)
            : model.Current("roster").OrderBy(r => r.Id, StringComparer.Ordinal).FirstOrDefault();

    private static RunWatchSettings Overlay(RunWatchSettings settings, RecordEnvelope? record)
    {
        if (record is null || !record.Body.TryGetProperty("limits", out JsonElement limits) || !limits.TryGetProperty("watch", out JsonElement watch))
        {
            return settings;
        }

        return settings with
        {
            NormalTokensPerMinute = AgentRuns.Amount(watch, "normalTokensPerMinute") ?? settings.NormalTokensPerMinute,
            BurnMultiplier = (int)(AgentRuns.Amount(watch, "burnMultiplier") ?? settings.BurnMultiplier),
            BurnMinimumTokens = AgentRuns.Amount(watch, "burnMinimumTokens") ?? settings.BurnMinimumTokens,
            BurnWindowSeconds = (int)(AgentRuns.Amount(watch, "burnWindowSeconds") ?? settings.BurnWindowSeconds),
            BurnCapPercent = (int)(AgentRuns.Amount(watch, "burnCapPercent") ?? settings.BurnCapPercent),
            LoopWarning = (int)(AgentRuns.Amount(watch, "loopWarning") ?? settings.LoopWarning),
            LoopStop = (int)(AgentRuns.Amount(watch, "loopStop") ?? settings.LoopStop),
            LoopWindow = (int)(AgentRuns.Amount(watch, "loopWindow") ?? settings.LoopWindow),
        };
    }
}
