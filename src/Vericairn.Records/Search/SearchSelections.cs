// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using System.Text.RegularExpressions;
using Vericairn.Records.Canonical;
using Vericairn.Records.Hashing;
using Vericairn.Records.Types;

namespace Vericairn.Records.Search;

/// <summary>
/// The search selection contract, <c>vericairn.search-selection/1</c>: the canonical account of one search used for a context
/// package or served to a run's agent. It is data with its own content hash, never authority; a candidate is a passage the search
/// returned, with its rank, score, source identity and pin, and what became of it.
/// </summary>
public static partial class SearchSelections
{
    /// <summary>The selection format.</summary>
    public const string Format = "vericairn.search-selection/1";

    /// <summary>The keyword method's version: its analyzer, scoring and ordering together.</summary>
    public const string KeywordMethod = "vericairn.search.keyword/1";

    /// <summary>The most candidates one search returns, and one selection holds.</summary>
    public const int MaxCandidates = 50;

    /// <summary>The most searches a context package or a run's intent carries.</summary>
    public const int MaxContextSelections = 5;

    /// <summary>The most selections a settled run keeps in full; the rest are named only by the digest of the retained list.</summary>
    public const int MaxObservedSelections = 100;

    /// <summary>What became of a candidate.</summary>
    public static readonly IReadOnlyList<string> States = ["selected", "duplicate", "below-limit", "excluded", "index-fault"];

    /// <summary>The kinds of passage a candidate can be.</summary>
    public static readonly IReadOnlyList<string> Kinds = ["record", "document", "knowledge"];

    /// <summary>The statement a record carries when it omits candidates its readers could not all read; it gives no count.</summary>
    public const string Limited = "Candidates whose audience does not cover every reader of this record are omitted.";

    private static readonly IReadOnlySet<string> HashKey = new HashSet<string>(StringComparer.Ordinal) { "hash" };

    /// <summary>The selection's own content hash: SHA-256 of its canonical JSON without the <c>hash</c> member.</summary>
    public static string Hash(JsonElement selection) => ContentHash.Sha256(CanonicalJson.SerializeUtf8(selection, HashKey));

    /// <summary>The digest of a list of selections: SHA-256 of the canonical JSON array of their hashes, in order.</summary>
    public static string ListDigest(IEnumerable<string> hashes)
    {
        ArgumentNullException.ThrowIfNull(hashes);
        return ContentHash.Sha256(CanonicalJson.SerializeUtf8(JsonSerializer.SerializeToElement(hashes.ToArray())));
    }

    /// <summary>
    /// Reads a settled run's observed searches: at most <see cref="MaxObservedSelections"/> selections, whether the list is complete,
    /// and the digest of every observed selection's hash, which a complete list must equal.
    /// </summary>
    public static void ValidateObserved(BodyReader observed)
    {
        ArgumentNullException.ThrowIfNull(observed);
        IReadOnlyList<BodyReader> selections = observed.NestedArray("selections", true, allowEmpty: true) ?? [];
        if (selections.Count > MaxObservedSelections)
            observed.Error("search.selections", "selections", $"A run keeps at most {MaxObservedSelections} observed selections in full.");
        foreach (BodyReader selection in selections) Validate(selection, recordReaders: true);
        bool? complete = observed.Flag("complete", true);
        string? digest = observed.Hash("digest", true);
        observed.NoOtherKeys();
        if (complete == true && digest is not null && selections.All(s => s.Element.TryGetProperty("hash", out JsonElement h) && h.ValueKind == JsonValueKind.String)
            && digest != ListDigest(selections.Select(s => s.Element.GetProperty("hash").GetString()!)))
            observed.Error("search.selections-digest", "digest", "A complete list's digest is the digest of its selections' hashes.");
    }

    /// <summary>
    /// Reads one selection held by a record, reporting every problem as a finding: the format, the method, the query, the reader,
    /// the index revision and segments, at most <see cref="MaxCandidates"/> candidates with their states, and a hash that matches.
    /// </summary>
    public static void Validate(BodyReader selection, bool recordReaders = false)
    {
        ArgumentNullException.ThrowIfNull(selection);
        selection.Enum("format", true, Format);
        string? declared = selection.Hash("hash", true);
        if (selection.Nested("method", true) is BodyReader method)
        {
            method.Enum("name", true, "keyword");
            method.Enum("version", true, KeywordMethod);
            method.Text("analyzer", true, 200);
            method.Text("engine", true, 200);
            method.NoOtherKeys();
        }

        if (selection.Nested("query", true) is BodyReader query)
        {
            query.Text("text", true, 1024);
            query.StringArray("terms", true, allowEmpty: true, maxItemLength: 1024);
            if (query.Element.TryGetProperty("phrases", out JsonElement phrases))
            {
                query.Allow("phrases");
                if (phrases.ValueKind != JsonValueKind.Array || phrases.EnumerateArray().Any(p => p.ValueKind != JsonValueKind.Array
                    || p.GetArrayLength() == 0 || p.EnumerateArray().Any(t => t.ValueKind != JsonValueKind.String || t.GetString()!.Length is 0 or > 1024)))
                    query.Error("search.selection", "phrases", "Phrases are non-empty arrays of terms.");
            }
            else query.Error("body.missing", "phrases", "A parsed query states its phrases, which may be none.");
            if (query.Nested("filters", true) is BodyReader filters)
            {
                foreach (string name in new[] { "type", "kind", "path", "key" })
                    filters.StringArray(name, false, allowEmpty: false, maxItemLength: 512);
                filters.Text("since", false, 10, DatePattern(), "yyyy-MM-dd");
                filters.Text("before", false, 10, DatePattern(), "yyyy-MM-dd");
                filters.Flag("history", true);
                filters.NoOtherKeys();
            }
            query.NoOtherKeys();
        }

        long? limit = selection.Number("limit", true, 1, MaxCandidates);
        long? selectLimit = selection.Number("selectLimit", true, 0, MaxCandidates);
        if (limit is not null && selectLimit > limit) selection.Error("search.selection", "selectLimit", "A selection selects no more candidates than it requested.");
        if (selection.Nested("reader", true) is BodyReader reader)
        {
            if (reader.Element.TryGetProperty("principal", out JsonElement principal) && principal.ValueKind == JsonValueKind.Null) reader.Allow("principal");
            else reader.Ulid("principal", true);
            reader.StringArray("audiences", true, allowEmpty: true, maxItemLength: 80);
            reader.NoOtherKeys();
        }

        if (selection.Nested("index", true) is BodyReader index)
        {
            index.Hash("revision", true);
            index.Flag("current", true);
            index.Text("commit", true, 40, CommitPattern(), "a 40-character lower-case hex commit id or uncommitted");
            // The latest recorded time among the indexed records: with it a copy holding later records can rebuild exactly this basis.
            if (index.Element.TryGetProperty("asOf", out JsonElement asOf) && asOf.ValueKind == JsonValueKind.Null) index.Allow("asOf");
            else index.Timestamp("asOf", true);
            foreach (BodyReader segment in index.NestedArray("segments", true, allowEmpty: true) ?? [])
            {
                segment.Text("audience", true, 80);
                segment.Hash("records", true);
                segment.Hash("documents", true);
                segment.NoOtherKeys();
            }
            index.NoOtherKeys();
        }

        IReadOnlyList<BodyReader> candidates = selection.NestedArray("candidates", true, allowEmpty: true) ?? [];
        if (candidates.Count > MaxCandidates) selection.Error("search.selection", "candidates", $"A selection holds at most {MaxCandidates} candidates.");
        long previous = 0;
        foreach (BodyReader candidate in candidates)
        {
            long? rank = candidate.Number("rank", true, 1, MaxCandidates);
            if (rank is not null && rank <= previous) candidate.Error("search.selection", "rank", "Candidates keep their ranked order.");
            previous = rank ?? previous;
            candidate.Number("score", true, 0, CanonicalJson.MaxSafeInteger);
            string? kind = candidate.Enum("kind", true, Kinds);
            string? audience = candidate.Text("audience", true, 80);
            // A record carries only candidates every reader of the estate's records may read: Internal or Public passages.
            if (recordReaders && audience is not null and not ("Internal" or "Public"))
                candidate.Error("search.selection-audience", "audience", "A record carries only candidates whose audience covers all its readers.");
            bool fault = candidate.Enum("state", true, States) == "index-fault";
            candidate.Text("reason", false, 200);
            candidate.Text("pin", true, 16_384, PinPattern(), "a vc1_ pin");
            candidate.StringArray("fields", true, allowEmpty: true, maxItemLength: 160);
            if (candidate.Nested("extent", true) is BodyReader extent)
            {
                extent.Number("start", true, 0, CanonicalJson.MaxSafeInteger);
                extent.Number("length", true, 0, CanonicalJson.MaxSafeInteger);
                extent.NoOtherKeys();
            }

            if (candidate.Nested("source", true) is BodyReader source)
            {
                string? sourceKind = source.Enum("kind", true, "record", "document");
                if (sourceKind == "record")
                {
                    source.Ulid("id", true);
                    source.Text("type", true, 80);
                    source.Hash("hash", true);
                    source.Text("path", true, 200);
                    // An index fault names only what the index held, which has no byte identity for a record.
                    source.Number("bytes", !fault, 0, CanonicalJson.MaxSafeInteger);
                    source.Hash("byteHash", !fault);
                }
                else if (sourceKind == "document")
                {
                    source.Text("path", true, 512);
                    source.Ulid("revision", true);
                    source.Hash("revisionHash", true);
                    if (source.Element.TryGetProperty("bytes", out JsonElement bytes) && bytes.ValueKind == JsonValueKind.Null) source.Allow("bytes");
                    else source.Number("bytes", true, 0, CanonicalJson.MaxSafeInteger);
                    source.Text("hashMode", false, 80);
                    source.Hash("byteHash", true);
                }
                source.NoOtherKeys();
                if (kind == "record" != (sourceKind == "record"))
                    candidate.Error("search.selection", "source", "A record candidate's source is a record, and a document or knowledge candidate's a document.");
            }
            candidate.NoOtherKeys();
        }

        selection.Flag("more", true);
        selection.Text("limited", false, 200);
        selection.NoOtherKeys();
        if (declared is not null && declared != Hash(selection.Element))
            selection.Error("search.selection-hash", "hash", "A selection's hash identifies its exact canonical content.");
    }

    [GeneratedRegex("^[0-9]{4}-[0-9]{2}-[0-9]{2}$", RegexOptions.CultureInvariant)]
    private static partial Regex DatePattern();

    [GeneratedRegex("^(?:[0-9a-f]{40}|uncommitted)$", RegexOptions.CultureInvariant)]
    private static partial Regex CommitPattern();

    [GeneratedRegex("^vc1_[A-Za-z0-9_-]+$", RegexOptions.CultureInvariant)]
    private static partial Regex PinPattern();
}
