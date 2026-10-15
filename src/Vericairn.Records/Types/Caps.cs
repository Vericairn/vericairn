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
