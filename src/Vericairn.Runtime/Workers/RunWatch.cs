// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Globalization;
using Vericairn.Protocol;
using Vericairn.Records.Estate;
using Vericairn.Records.Model;

namespace Vericairn.Runtime.Workers;

/// <summary>Bounded rate samples and repeated invocation fingerprints, driven only by worker-observed events and monotonic time.</summary>
internal sealed class RunWatch(RunWatchSettings settings, long? tokenCap, decimal? historyRate)
{
    private readonly Queue<string> recent = new();
    private readonly Dictionary<string, int> repeats = new(StringComparer.Ordinal);
    private readonly Queue<(TimeSpan At, long Tokens)> samples = new([(TimeSpan.Zero, 0)]);
    private long lastTokens;

    public IReadOnlyList<RunSignal> Action(HarnessAction action)
    {
        string fingerprint = CommandAudit.Fingerprint(action);
        recent.Enqueue(fingerprint);
        repeats[fingerprint] = repeats.GetValueOrDefault(fingerprint) + 1;
        if (recent.Count > settings.LoopWindow)
        {
            string removed = recent.Dequeue();
            if (--repeats[removed] == 0)
            {
                repeats.Remove(removed);
            }
        }

        int count = repeats[fingerprint];
        if (count < settings.LoopWarning)
        {
            return [];
        }

        return [new RunSignal("loop", count >= settings.LoopStop ? "critical" : "warning", Now(),
            $"The worker observed the same tool and canonical input {count.ToString(CultureInfo.InvariantCulture)} times among the last {settings.LoopWindow.ToString(CultureInfo.InvariantCulture)} invocations.", fingerprint, count)];
    }

    public IReadOnlyList<RunSignal> Usage(long? tokens, TimeSpan elapsed)
    {
        if (tokens is not long total || total < lastTokens || elapsed < TimeSpan.Zero)
        {
            return [];
        }

        TimeSpan window = TimeSpan.FromSeconds(settings.BurnWindowSeconds);
        while (samples.Count > 1 && samples.ElementAt(1).At <= elapsed - window)
        {
            samples.Dequeue();
        }

        (TimeSpan at, long before) = samples.Peek();
        long spent = total - before;
        TimeSpan duration = elapsed - at;
        if (total != lastTokens)
        {
            samples.Enqueue((elapsed, total));
            lastTokens = total;
            // Keep the launch sample until a full window exists, then bound event volume without shortening the sample.
            if (samples.Count > 256)
            {
                (TimeSpan At, long Tokens) first = samples.Dequeue();
                _ = samples.Dequeue();
                var remaining = samples.ToArray();
                samples.Clear();
                samples.Enqueue(first);
                foreach ((TimeSpan At, long Tokens) sample in remaining)
                {
                    samples.Enqueue(sample);
                }
            }
        }

        if (duration < window || spent < settings.BurnMinimumTokens)
        {
            return [];
        }

        decimal rate = spent * 60m / (decimal)duration.TotalSeconds;
        decimal baseline = Math.Max(settings.NormalTokensPerMinute, historyRate ?? 0);
        if (rate <= baseline * settings.BurnMultiplier)
        {
            return [];
        }

        bool critical = tokenCap is > 0 ? total * 100m >= tokenCap.Value * (decimal)settings.BurnCapPercent
            : rate >= baseline * settings.BurnMultiplier * 2;
        string history = historyRate is decimal past ? past.ToString("0.##", CultureInfo.InvariantCulture) : "unavailable";
        string cap = tokenCap?.ToString(CultureInfo.InvariantCulture) ?? "not stated";
        return [new RunSignal("burn-rate", critical ? "critical" : "warning", Now(),
            $"The worker observed {rate.ToString("0.##", CultureInfo.InvariantCulture)} tokens per minute over {duration.TotalSeconds.ToString("0.##", CultureInfo.InvariantCulture)} seconds; launcher reference {settings.NormalTokensPerMinute.ToString(CultureInfo.InvariantCulture)}, goal history median {history}, multiplier {settings.BurnMultiplier.ToString(CultureInfo.InvariantCulture)}; reported tokens {total.ToString(CultureInfo.InvariantCulture)} against token cap {cap}.")];
    }

    /// <summary>Only successful, fully reported runs of the same goal and launcher inform its recent median.</summary>
    public static decimal? History(IEnumerable<AgentRuns.Run> runs, string launcher)
    {
        decimal[] rates = runs.Where(r => r.Launcher == launcher && r.Status == "observed-success"
                && AgentRuns.Amount(r.Usage, "tokens") is >= 0 && AgentRuns.Amount(r.Usage, "wallMinutes") is > 0
                && (!r.Usage.TryGetProperty("unreported", out System.Text.Json.JsonElement missing) || !missing.EnumerateArray().Any(v => v.GetString() == "tokens")))
            .TakeLast(20).Select(r => (decimal)AgentRuns.Amount(r.Usage, "tokens")!.Value / AgentRuns.Amount(r.Usage, "wallMinutes")!.Value)
            .Order().ToArray();
        return rates.Length == 0 ? null : rates.Length % 2 == 1 ? rates[rates.Length / 2] : (rates[rates.Length / 2 - 1] + rates[rates.Length / 2]) / 2;
    }

    private static string Now() => RecordParser.FormatTimestamp(DateTimeOffset.UtcNow);
}
