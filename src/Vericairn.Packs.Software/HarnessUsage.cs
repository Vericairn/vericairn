// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;

namespace Vericairn.Packs.Software;

/// <summary>Usage is complete only when every required count is a nonnegative integer and the sum fits.</summary>
internal static class HarnessUsage
{
    public static long? Message(JsonElement message)
    {
        long? total = Total(message, "input_tokens", "output_tokens");
        if (total is null)
        {
            return null;
        }

        JsonElement usage = message.GetProperty("usage");
        try
        {
            foreach (string name in new[] { "cache_creation_input_tokens", "cache_read_input_tokens" })
            {
                if (usage.TryGetProperty(name, out JsonElement count))
                {
                    if (!count.TryGetInt64(out long value) || value < 0)
                    {
                        return null;
                    }

                    total = checked(total + value);
                }
            }
        }
        catch (OverflowException)
        {
            return null;
        }

        return total;
    }

    public static long? Total(JsonElement root, params string[] fields)
    {
        if (!root.TryGetProperty("usage", out JsonElement usage) || usage.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        long total = 0;
        try
        {
            foreach (string field in fields)
            {
                if (!usage.TryGetProperty(field, out JsonElement count) || count.ValueKind != JsonValueKind.Number || !count.TryGetInt64(out long value) || value < 0)
                {
                    return null;
                }

                total = checked(total + value);
            }

            if (usage.TryGetProperty("cached_input_tokens", out JsonElement cached) && (cached.ValueKind != JsonValueKind.Number || !cached.TryGetInt64(out long cachedValue) || cachedValue < 0))
            {
                return null;
            }
        }
        catch (OverflowException)
        {
            return null;
        }

        return total;
    }
}
