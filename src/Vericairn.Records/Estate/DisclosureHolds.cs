// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text;
using System.Text.Json;
using Vericairn.Records.Model;
using Vericairn.Records.Text;

namespace Vericairn.Records.Estate;

/// <summary>
/// Work orders held from disclosure: while a work order is marked <c>disclosure: held</c> and the release that carries it has not
/// been published, its key and distinctive text must not reach a public repository. Publishing that release, with its
/// documentation set, ends the hold; what the release then discloses stays the operator's choice.
/// </summary>
public static class DisclosureHolds
{
    /// <summary>The marker value of the work order's optional <c>disclosure</c> field.</summary>
    public const string Held = "held";

    /// <summary>Text shorter than this is too common to identify held work, so it is not matched on its own.</summary>
    public const int MinimumPhrase = 24;

    /// <summary>One held work order: its key and the distinctive phrases of its title, intent and scope.</summary>
    public sealed record Hold(string Key, string WorkOrder, IReadOnlyList<string> Phrases);

    /// <summary>The holds in force: current held work orders whose carrying release has no published release record.</summary>
    public static IReadOnlyList<Hold> Active(EstateModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        List<RecordEnvelope> orders = [.. model.Current("work-order")];
        var held = orders.Where(o => EstateModel.BodyString(o, "disclosure") == Held && !Released(model, o)).ToList();
        if (held.Count == 0) return [];
        // A phrase every other work order shares, such as a standard closing step, identifies nothing held.
        var shared = new HashSet<string>(orders.Where(o => !held.Contains(o)).SelectMany(Texts).Select(Phrase), StringComparer.Ordinal);
        return [.. held.Select(o => new Hold(EstateModel.BodyString(o, "key") ?? "", o.Id,
            [.. Texts(o).Select(Phrase).Where(p => p.Length >= MinimumPhrase && !shared.Contains(p)).Distinct(StringComparer.Ordinal)]))];
    }

    /// <summary>The key of the first hold the text would disclose: its key as a whole word, or one of its distinctive phrases.</summary>
    public static string? Disclosed(IReadOnlyList<Hold> holds, string text)
    {
        ArgumentNullException.ThrowIfNull(holds);
        ArgumentNullException.ThrowIfNull(text);
        if (holds.Count == 0) return null;
        string normalized = Normalize(text);
        foreach (Hold hold in holds)
        {
            if (hold.Key.Length > 0 && ContainsWord(normalized, Normalize(hold.Key))) return hold.Key;
            if (hold.Phrases.Any(p => normalized.Contains(p, StringComparison.Ordinal))) return hold.Key;
        }
        return null;
    }

    // The hold ends when the release that carries the work order is published.
    private static bool Released(EstateModel model, RecordEnvelope order)
    {
        if (!order.Body.TryGetProperty("placement", out JsonElement placement) || placement.ValueKind != JsonValueKind.Object
            || !placement.TryGetProperty("kind", out JsonElement kind) || kind.GetString() != "release"
            || !placement.TryGetProperty("version", out JsonElement version) || version.ValueKind != JsonValueKind.String
            || !placement.TryGetProperty("product", out JsonElement product) || product.ValueKind != JsonValueKind.String) return false;
        return model.Current("release").Any(r => EstateModel.BodyString(r, "status") == "published"
            && EstateModel.BodyString(r, "version") == version.GetString() && r.Subject?.Id == product.GetString());
    }

    private static IEnumerable<string> Texts(RecordEnvelope order)
    {
        foreach (string name in new[] { "title", "intent" })
        {
            if (EstateModel.BodyString(order, name) is string text) yield return text;
        }
        if (order.Body.TryGetProperty("scope", out JsonElement scope) && scope.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement item in scope.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.String) yield return item.GetString()!;
            }
        }
    }

    // A sentence's closing punctuation does not decide whether a quotation of it matches.
    private static string Phrase(string text) => Normalize(text).TrimEnd('.', ',', ';', ':', '!', '?');

    // Compatibility forms, case and runs of whitespace (including line breaks within a wrapped sentence) do not hide a match. The
    // folding is the managed one, so it is the same in the product's invariant-globalization build as with ICU.
    private static string Normalize(string text)
    {
        var builder = new StringBuilder(text.Length);
        bool space = false;
        foreach (char c in UnicodeFolding.NfkcCaseFold(text))
        {
            if (char.IsWhiteSpace(c)) { space = builder.Length > 0; continue; }
            if (space) builder.Append(' ');
            space = false;
            builder.Append(c);
        }
        return builder.ToString();
    }

    private static bool ContainsWord(string text, string word)
    {
        for (int at = text.IndexOf(word, StringComparison.Ordinal); at >= 0; at = text.IndexOf(word, at + 1, StringComparison.Ordinal))
        {
            int end = at + word.Length;
            if ((at == 0 || !char.IsLetterOrDigit(text[at - 1])) && (end == text.Length || !char.IsLetterOrDigit(text[end]))) return true;
        }
        return false;
    }
}
