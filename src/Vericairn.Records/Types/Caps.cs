// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

namespace Vericairn.Records.Types;

/// <summary>
/// Caps on what work may spend, by unit, from record format 2: a goal's budget and an agent run's cap share them, and an agent
/// run's observed usage uses the same units. An absent unit is not an unlimited one, and usage a harness does not report is
/// charged at its cap, never counted as zero.
/// </summary>
public static class Caps
{
    /// <summary>The units, in plain words, for field documentation.</summary>
    public const string Units = "wallMinutes, attemptsPerStep, tokens and concurrency (positive integers) and currency ({amount in minor units, unit: three upper-case letters}); an absent unit is not an unlimited one";

    /// <summary>The units by name, in the order a budget is shown.</summary>
    public static readonly string[] UnitNames = ["wallMinutes", "attemptsPerStep", "tokens", "concurrency", "currency"];

    /// <summary>
    /// What a caps object grants, unit by unit in the order of <see cref="UnitNames"/>: the cap in words, or null for a unit it does
    /// not state, which is not granted at all, never granted without limit.
    /// </summary>
    public static IReadOnlyList<(string Unit, string? Cap)> Granted(System.Text.Json.JsonElement caps)
    {
        var granted = new List<(string Unit, string? Cap)>();
        foreach (string unit in UnitNames)
        {
            string? cap = null;
            if (caps.ValueKind == System.Text.Json.JsonValueKind.Object && caps.TryGetProperty(unit, out System.Text.Json.JsonElement value))
            {
                cap = value.ValueKind == System.Text.Json.JsonValueKind.Object
                    ? $"{(value.TryGetProperty("amount", out System.Text.Json.JsonElement amount) ? amount.GetRawText() : "?")} minor units of {(value.TryGetProperty("unit", out System.Text.Json.JsonElement currency) ? currency.GetString() : "?")}"
                    : value.GetRawText();
            }

            granted.Add((unit, cap));
        }

        return granted;
    }

    /// <summary>Reads a caps object; the named units are required.</summary>
    public static void Read(BodyReader caps, params string[] required)
    {
        ArgumentNullException.ThrowIfNull(caps);
        ArgumentNullException.ThrowIfNull(required);
        caps.Number("wallMinutes", required.Contains("wallMinutes"), 1, 1_000_000);
        caps.Number("attemptsPerStep", required.Contains("attemptsPerStep"), 1, 1_000);
        caps.Number("tokens", required.Contains("tokens"), 1, 1_000_000_000_000);
        caps.Number("concurrency", required.Contains("concurrency"), 1, 1_000);
        Currency(caps, required.Contains("currency"));
        caps.NoOtherKeys();
    }

    /// <summary>Reads an agent run's observed usage: what the harness reported, and the units it did not report.</summary>
    public static void ReadUsage(BodyReader usage)
    {
        ArgumentNullException.ThrowIfNull(usage);
        usage.Number("wallMinutes", false, 0, 1_000_000);
        usage.Number("tokens", false, 0, 1_000_000_000_000);
        Currency(usage, false);
        usage.StringArray("unreported", true, allowEmpty: true, maxItemLength: 40);
        usage.NoOtherKeys();
    }

    private static void Currency(BodyReader parent, bool required)
    {
        BodyReader? currency = parent.Nested("currency", required);
        if (currency is null)
        {
            return;
        }

        currency.Number("amount", true, 0, 1_000_000_000_000);
        currency.Text("unit", true, 3, Patterns.CurrencyUnit, "three upper-case letters such as USD");
        currency.NoOtherKeys();
    }
}
