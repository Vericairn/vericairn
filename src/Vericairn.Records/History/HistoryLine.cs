// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Globalization;
using System.Text.Json;
using Vericairn.Records.Hashing;

namespace Vericairn.Records.History;

/// <summary>
/// The history line every surface shows (design/signed-history.md § 9): whether the estate has adopted history checkpoints, its heads
/// by short hash, how many records they witness, the unwitnessed tail and the standing problems. Status renders it from the records and
/// Workbench and Remote from the canonical status document, through this one formatter, so the words never differ.
/// </summary>
public static class HistoryLine
{
    /// <summary>The state language's history vocabulary: whether an estate has adopted history checkpoints.</summary>
    public static readonly IReadOnlyList<string> States = ["not adopted", "adopted"];

    /// <summary>How many hexadecimal digits of a head's hash the line shows: enough to name it to <c>estate check --expect</c>.</summary>
    public const int ShortDigits = SignedHistory.MinimumExpectedDigits;

    /// <summary>A checkpoint's hash as the line shows it: its first sixteen hexadecimal digits, which <c>estate check --expect</c> accepts.</summary>
    public static string ShortHash(string? hash)
    {
        if (hash is null) return "?";
        string hex = hash.StartsWith(ContentHash.Prefix, StringComparison.Ordinal) ? hash[ContentHash.Prefix.Length..] : hash;
        return hex.Length > ShortDigits ? hex[..ShortDigits] : hex;
    }

    /// <summary>The line, from what the records establish.</summary>
    public static string Render(bool adopted, IEnumerable<string?> heads, long witnessed, long tail, int problems)
    {
        ArgumentNullException.ThrowIfNull(heads);
        List<string> shown = [.. heads.Select(ShortHash)];
        return "History: " + States[adopted ? 1 : 0]
            + " · " + (shown.Count == 0 ? "no heads" : (shown.Count == 1 ? "head " : "heads ") + string.Join(", ", shown))
            + " · " + witnessed.ToString("N0", CultureInfo.InvariantCulture) + " witnessed"
            + " · tail " + tail.ToString("N0", CultureInfo.InvariantCulture)
            + " · " + (problems == 0 ? "no problems" : problems.ToString(CultureInfo.InvariantCulture) + (problems == 1 ? " problem" : " problems"));
    }

    /// <summary>The line from a canonical status document's <c>history</c> member; null when the document has none.</summary>
    public static string? FromStatus(JsonElement status)
    {
        if (status.ValueKind != JsonValueKind.Object || !status.TryGetProperty("history", out JsonElement history) || history.ValueKind != JsonValueKind.Object) return null;
        bool adopted = history.TryGetProperty("adopted", out JsonElement flag) && flag.ValueKind == JsonValueKind.True;
        List<string?> heads = history.TryGetProperty("heads", out JsonElement list) && list.ValueKind == JsonValueKind.Array
            ? [.. list.EnumerateArray().Select(h => h.ValueKind == JsonValueKind.Object && h.TryGetProperty("hash", out JsonElement hash) && hash.ValueKind == JsonValueKind.String ? hash.GetString() : null)]
            : [];
        long witnessed = history.TryGetProperty("witnessed", out JsonElement w) && w.ValueKind == JsonValueKind.Number && w.TryGetInt64(out long wn) ? wn : 0;
        long tail = history.TryGetProperty("tail", out JsonElement t) && t.ValueKind == JsonValueKind.Number && t.TryGetInt64(out long tn) ? tn : 0;
        int problems = history.TryGetProperty("problems", out JsonElement p) && p.ValueKind == JsonValueKind.Array ? p.GetArrayLength() : 0;
        return Render(adopted, heads, witnessed, tail, problems);
    }
}
