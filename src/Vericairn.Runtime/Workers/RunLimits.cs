// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Globalization;
using System.Text.Json;
using Vericairn.Records.Estate;

namespace Vericairn.Runtime.Workers;

/// <summary>Operational stop thresholds below the reserved allowance, leaving headroom for usage and shutdown in flight.</summary>
/// <param name="WallTime">Time until the worker asks for a stop, after reserving grace and one heartbeat.</param>
/// <param name="Tokens">Token threshold after reserving ten percent (rounded up).</param>
/// <param name="Currency">Currency threshold in minor units after reserving ten percent (rounded up).</param>
/// <param name="CurrencyUnit">The currency of that threshold.</param>
public sealed record RunLimits(TimeSpan WallTime, long? Tokens, long? Currency, string? CurrencyUnit)
{
    /// <summary>Builds thresholds without reducing the cap held by the ledger.</summary>
    public static RunLimits For(JsonElement cap, WorkerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        TimeSpan total = options.Minute * (AgentRuns.Amount(cap, "wallMinutes") ?? 1);
        TimeSpan wall = total - options.Grace - options.Heartbeat;
        return new RunLimits(wall > TimeSpan.Zero ? wall : TimeSpan.Zero, Below(AgentRuns.Amount(cap, "tokens")), Below(AgentRuns.Amount(cap, "currency")), AgentRuns.CurrencyUnit(cap));
    }

    /// <summary>The reservation and its softer operational thresholds, safe to record without local configuration.</summary>
    public string Describe(JsonElement cap) => "Soft accounting limits: wallMinutes, tokens and currency are not guaranteed hard spending caps. The full cap remains reserved. "
        + $"Stop after {WallTime.TotalSeconds.ToString(CultureInfo.InvariantCulture)} seconds, leaving shutdown grace and heartbeat headroom; "
        + $"token threshold {Text(Tokens)} of {Text(AgentRuns.Amount(cap, "tokens"))}; currency threshold {Text(Currency)} of {Text(AgentRuns.Amount(cap, "currency"))} minor units ({CurrencyUnit ?? "none"}). Tokens and currency reserve ten percent rounded up for usage in flight; a single turn or shutdown may exceed that headroom.";

    private static long? Below(long? amount) => amount is long value ? value - (value / 10 + (value % 10 == 0 ? 0 : 1)) : null;

    private static string Text(long? value) => value?.ToString(CultureInfo.InvariantCulture) ?? "not granted";
}
