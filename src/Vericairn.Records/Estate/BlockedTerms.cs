// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Vericairn.Records.Model;

namespace Vericairn.Records.Estate;

/// <summary>
/// The estate's blocked terms: words an estate policy forbids in authored records and in documents generated from records, such
/// as the names of the models agents run on. A term matches whole, ignoring case and Unicode compatibility forms, so it is never
/// found inside a longer word. Text that already held a term is never refused for keeping it; only an introduced occurrence is.
/// </summary>
public static class BlockedTerms
{
    /// <summary>The estate-policy rule kind that declares the list.</summary>
    public const string Kind = "vericairn.estate/blocked-terms";

    /// <summary>The most terms one rule may declare.</summary>
    public const int MaximumTerms = 256;

    // Structured model selectors the worker must record, such as a run's requested and observed model, are facts rather than
    // prose; the list governs prose. A record's own list is exempt from itself.
    private const string ModelProperty = "model";

    /// <summary>The terms declared by the estate's current policies, in a stable order, without duplicates.</summary>
    public static ImmutableArray<string> In(EstateModel model, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(model);
        var terms = new SortedSet<string>(StringComparer.Ordinal);
        foreach (RecordEnvelope policy in model.Current("estate-policy"))
        {
            if (EstateModel.BodyString(policy, "startsAt") is string starts
                && DateTimeOffset.TryParse(starts, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out DateTimeOffset start) && start > now) continue;
            foreach (JsonElement parameters in Rules(policy.Body))
            {
                if (!parameters.TryGetProperty("terms", out JsonElement list) || list.ValueKind != JsonValueKind.Array) continue;
                foreach (JsonElement term in list.EnumerateArray())
                {
                    if (term.ValueKind == JsonValueKind.String && Valid(term.GetString()) is null) terms.Add(Normalize(term.GetString()!.Trim()));
                }
            }
        }
        return [.. terms];
    }

    /// <summary>Why a declared list is malformed, or null when it is well formed.</summary>
    public static string? Validate(JsonElement parameters)
    {
        if (parameters.ValueKind != JsonValueKind.Object) return "Rule parameters must be an object.";
        foreach (JsonProperty property in parameters.EnumerateObject())
        {
            if (property.Name != "terms") return $"Unknown blocked-terms parameter: {property.Name}.";
        }
        if (!parameters.TryGetProperty("terms", out JsonElement list) || list.ValueKind != JsonValueKind.Array || list.GetArrayLength() == 0)
            return "A blocked-terms rule declares a non-empty terms array.";
        if (list.GetArrayLength() > MaximumTerms) return $"A blocked-terms rule declares at most {MaximumTerms} terms.";
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (JsonElement term in list.EnumerateArray())
        {
            if (term.ValueKind != JsonValueKind.String) return "Every blocked term is a string.";
            if (Valid(term.GetString()) is string problem) return problem;
            if (!seen.Add(Normalize(term.GetString()!.Trim()))) return "Blocked terms are unique, ignoring case.";
        }
        return null;
    }

    /// <summary>The normalized terms one well-formed rule declares.</summary>
    public static ImmutableArray<string> TermsOf(JsonElement parameters) => Validate(parameters) is not null ? []
        : [.. parameters.GetProperty("terms").EnumerateArray().Select(t => Normalize(t.GetString()!.Trim())).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)];

    /// <summary>Whether a record declares a blocked-terms rule, which exempts that record from the list it declares.</summary>
    public static bool Declares(string type, JsonElement body) => type == "estate-policy" && Rules(body).Any();

    /// <summary>The first term a record body introduces: any occurrence in a new record, or more occurrences than its predecessor held.</summary>
    public static string? IntroducedInRecord(ImmutableArray<string> terms, string type, JsonElement body, JsonElement? predecessor)
    {
        if (terms.IsEmpty || Declares(type, body)) return null;
        string text = Prose(body);
        string before = predecessor is JsonElement prior ? Prose(prior) : "";
        return Introduced(terms, before, text);
    }

    /// <summary>The first term the new text introduces relative to the old text, or null.</summary>
    public static string? Introduced(ImmutableArray<string> terms, string before, string after)
    {
        if (terms.IsEmpty) return null;
        string old = Normalize(before), next = Normalize(after);
        return terms.FirstOrDefault(term => Count(next, term) > Count(old, term));
    }

    /// <summary>The first term generated files introduce, comparing each path's new bytes with its old bytes, with the path.</summary>
    public static (string Path, string Term)? IntroducedInFiles(ImmutableArray<string> terms, ImmutableArray<DocumentFile> before, ImmutableArray<DocumentFile> after)
    {
        if (terms.IsEmpty) return null;
        foreach (DocumentFile file in after)
        {
            DocumentFile? old = before.FirstOrDefault(b => string.Equals(b.Path, file.Path, StringComparison.Ordinal));
            if (Introduced(terms, old is null ? "" : Decode(old.Bytes), Decode(file.Bytes)) is string term) return (file.Path, term);
        }
        return null;
    }

    /// <summary>The refusal a write states when it would introduce a blocked term.</summary>
    public static string Refusal(string what, string term) =>
        $"{what} would introduce the blocked term \"{term}\", which an estate policy forbids. Remove it; blocked terms such as the names of the models agents run on stay out of records and generated documents.";

    /// <summary>Counts whole-term occurrences in already normalized text.</summary>
    internal static int Count(string text, string term)
    {
        int count = 0;
        for (int at = text.IndexOf(term, StringComparison.Ordinal); at >= 0; at = text.IndexOf(term, at + 1, StringComparison.Ordinal))
        {
            bool startsWhole = at == 0 || !char.IsLetterOrDigit(text[at - 1]);
            int end = at + term.Length;
            bool endsWhole = end == text.Length || !char.IsLetterOrDigit(text[end]);
            if (startsWhole && endsWhole) count++;
        }
        return count;
    }

    internal static string Normalize(string text) => text.Normalize(NormalizationForm.FormKC).ToLowerInvariant();

    private static string? Valid(string? term)
    {
        string trimmed = term?.Trim() ?? "";
        if (trimmed.Length < 2 || trimmed.Length > 100) return "A blocked term has 2 to 100 characters.";
        if (trimmed.Any(char.IsControl)) return "A blocked term has no control characters.";
        if (!trimmed.Any(char.IsLetterOrDigit)) return "A blocked term contains a letter or digit.";
        return null;
    }

    private static IEnumerable<JsonElement> Rules(JsonElement body)
    {
        if (body.ValueKind != JsonValueKind.Object || !body.TryGetProperty("rules", out JsonElement rules) || rules.ValueKind != JsonValueKind.Array) yield break;
        foreach (JsonElement rule in rules.EnumerateArray())
        {
            if (rule.ValueKind == JsonValueKind.Object && rule.TryGetProperty("kind", out JsonElement kind) && kind.ValueKind == JsonValueKind.String
                && kind.GetString() == Kind && rule.TryGetProperty("parameters", out JsonElement parameters)) yield return parameters;
        }
    }

    // Every string value and property name, one per line, except values of a structured model selector.
    private static string Prose(JsonElement body)
    {
        var text = new StringBuilder();
        Walk(body, null);
        return text.ToString();

        void Walk(JsonElement element, string? property)
        {
            switch (element.ValueKind)
            {
                case JsonValueKind.Object:
                    foreach (JsonProperty child in element.EnumerateObject())
                    {
                        text.Append(child.Name).Append('\n');
                        Walk(child.Value, child.Name);
                    }
                    break;
                case JsonValueKind.Array:
                    foreach (JsonElement item in element.EnumerateArray()) Walk(item, property);
                    break;
                case JsonValueKind.String when property != ModelProperty:
                    text.Append(element.GetString()).Append('\n');
                    break;
            }
        }
    }

    private static string Decode(ImmutableArray<byte> bytes) => Encoding.UTF8.GetString(bytes.AsSpan());
}
