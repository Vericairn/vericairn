// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text;
using System.Text.Json;
using Vericairn.Records.Model;
using Vericairn.Records.Text;
using Vericairn.Records.Types;

namespace Vericairn.Records.Estate;

/// <summary>
/// Work orders held from disclosure: while a work order is marked <c>disclosure: held</c> and the release that carries it has not
/// been published, its key and distinctive text must not reach a public repository. Publishing that release ends the hold; what the
/// release then discloses stays the operator's choice. Before that, one push may carry them (FIX-137): the carrying release's
/// documentation set, with the held fixes it carries, once the operator has approved that release's Security section, as the release
/// procedure merges it just before the candidate's submission; every other push stays held until publication.
/// </summary>
public static class DisclosureHolds
{
    /// <summary>The marker value of the work order's optional <c>disclosure</c> field.</summary>
    public const string Held = "held";

    /// <summary>Text shorter than this is too common to identify held work, so it is not matched on its own.</summary>
    public const int MinimumPhrase = 24;

    /// <summary>One held work order: its key and the distinctive phrases of its title, intent and scope.</summary>
    public sealed record Hold(string Key, string WorkOrder, IReadOnlyList<string> Phrases);

    /// <summary>
    /// The holds in force: current held work orders whose carrying release has no published release record. A checked push may still
    /// carry one with the documentation set of <see cref="DocumentationSetRelease"/>'s release.
    /// </summary>
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

    /// <summary>
    /// The version of the release whose documentation set a checked push of <paramref name="product"/>'s repository may carry with this
    /// hold's text (FIX-137), or null when no push may before publication. It is the hold's carrying release, a release of that product,
    /// once the operator has approved its Security section for this held work order (<see cref="SecuritySectionApproved"/>). The caller
    /// then lets the push pass this hold only if the push carries that version's documentation set and nothing else but the release's
    /// held fixes.
    /// </summary>
    internal static string? DocumentationSetRelease(EstateModel model, Hold hold, string product)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(hold);
        ArgumentNullException.ThrowIfNull(product);
        if (!model.ById.TryGetValue(hold.WorkOrder, out RecordEnvelope? order) || order.Type != "work-order") return null;
        order = model.Latest(order);
        if (Carrying(order) is not (string carrier, string placed, string version) || !Patterns.SemanticVersion.IsMatch(version)
            || LivingDocuments.ProductIdentity(model, placed) != LivingDocuments.ProductIdentity(model, product)) return null;
        return SecuritySectionApproved(model, carrier, order) ? version : null;
    }

    /// <summary>
    /// Whether the operator has approved, for <paramref name="order"/>, the Security section of the release that
    /// <paramref name="carrier"/> carries: an ask about the carrier whose question names the release's Security section
    /// (<see cref="SecuritySection"/>) is answered by the operator's decision, that decision names the held work order among the work
    /// orders it affects, and it was decided and recorded after the work order's current revision, so that a section approved before
    /// a fix joined the hold never covers it; and, as the release flow's <c>security-section</c> trap reads it, no ask about the carrier
    /// still waits, open or notified. An answer to any other question about the carrier, such as when to submit, approves nothing.
    /// </summary>
    internal static bool SecuritySectionApproved(EstateModel model, string carrier, RecordEnvelope order)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(order);
        if (!model.ById.TryGetValue(carrier, out RecordEnvelope? record) || record.Type != "work-order") return false;
        HashSet<string> carriers = [.. model.Chain(model.Latest(record))];
        HashSet<string> held = [.. model.Chain(order)];
        DateTimeOffset since = order.AtTime;
        List<(RecordEnvelope Ask, string State, IReadOnlyList<RecordEnvelope> Answers)> asks = [.. model.Current("ask")
            .Where(a => a.Subject is { Type: "work-order" } subject && carriers.Contains(subject.Id))
            .Select(a =>
            {
                (string state, IReadOnlyList<RecordEnvelope> answers) = Asks.State(model, a);
                return (a, state, answers);
            })];
        if (asks.Any(a => Asks.IsWaiting(a.State))) return false;
        return asks.Any(a => a.State == "answered" && ContainsWord(Normalize(EstateModel.BodyString(a.Ask, "question") ?? ""), SecuritySection)
            && a.Answers.Any(d => model.HasRole(EstateModel.BodyString(d, "decidedBy"), Roles.Operator) && d.AtTime > since && Decided(d) > since
                && d.Body.TryGetProperty("affects", out JsonElement affects) && affects.ValueKind == JsonValueKind.Array
                && affects.EnumerateArray().Any(id => id.ValueKind == JsonValueKind.String && held.Contains(id.GetString()!))));
    }

    /// <summary>The words an ask's question uses to name the release's Security section, matched whole, ignoring case and compatibility forms.</summary>
    internal const string SecuritySection = "security section";

    /// <summary>Whether the text names the hold's key as a whole word, as a held fix's commit message does (FIX-137).</summary>
    internal static bool Names(Hold hold, string text)
    {
        ArgumentNullException.ThrowIfNull(hold);
        ArgumentNullException.ThrowIfNull(text);
        return hold.Key.Length > 0 && ContainsWord(Normalize(text), Normalize(hold.Key));
    }

    // The hold ends when the release that carries the work order is published.
    private static bool Released(EstateModel model, RecordEnvelope order) => Carrying(order) is (_, string product, string version)
        && model.Current("release").Any(r => EstateModel.BodyString(r, "status") == "published"
            && EstateModel.BodyString(r, "version") == version && r.Subject?.Id == product);

    // The release a work order is placed in: its carrier, product and version.
    private static (string Carrier, string Product, string Version)? Carrying(RecordEnvelope order)
    {
        if (!order.Body.TryGetProperty("placement", out JsonElement placement) || placement.ValueKind != JsonValueKind.Object
            || !placement.TryGetProperty("kind", out JsonElement kind) || kind.GetString() != "release"
            || !placement.TryGetProperty("version", out JsonElement version) || version.ValueKind != JsonValueKind.String
            || !placement.TryGetProperty("product", out JsonElement product) || product.ValueKind != JsonValueKind.String) return null;
        string carrier = placement.TryGetProperty("carrier", out JsonElement named) && named.ValueKind == JsonValueKind.String ? named.GetString()! : "";
        return (carrier, product.GetString()!, version.GetString()!);
    }

    // When the operator decided: the decision's stated time, or its record's time when that cannot be read.
    private static DateTimeOffset Decided(RecordEnvelope decision) =>
        EstateModel.BodyString(decision, "decidedAt") is string at && DateTimeOffset.TryParse(at, System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal, out DateTimeOffset decided)
            ? decided : decision.AtTime;

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
