// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Vericairn.Protocol;
using Vericairn.Records.Estate;
using Vericairn.Records.Hashing;
using Vericairn.Records.Model;
using Vericairn.Records.Search;
using Vericairn.Runtime.Service;

namespace Vericairn.Runtime.Search;

/// <summary>
/// One snapshot a search runs against: the records and their exact files, the current document pins, and how to read a pin's
/// bytes. Retrieval rechecks every candidate against it, never against the index's own copy of an identity.
/// </summary>
internal sealed record SearchSnapshot(EstateModel Model, IReadOnlyDictionary<string, RecordFile> Files, IReadOnlyList<KeywordIndex.Pin> Pins, Func<KeywordIndex.Pin, byte[]?> Read)
{
    private readonly Dictionary<string, byte[]?> documents = new(StringComparer.Ordinal);
    private readonly Dictionary<string, IReadOnlyList<SearchPassage>> passages = new(StringComparer.Ordinal);

    /// <summary>A pin's passages, split once per snapshot.</summary>
    internal IReadOnlyList<SearchPassage> Passages(KeywordIndex.Pin pin, byte[] bytes)
    {
        string key = pin.Path + "@" + pin.ByteHash;
        if (!passages.TryGetValue(key, out IReadOnlyList<SearchPassage>? split)) passages[key] = split = SearchPassages.Document(pin.Path, pin.Title, bytes);
        return split;
    }

    /// <summary>The current pins of the estate's documents, one per path (the latest current revision), with whole generated files excluded.</summary>
    internal static IReadOnlyList<KeywordIndex.Pin> CurrentPins(EstateModel model)
    {
        var generated = new HashSet<string>(model.Current("living-document")
            .Where(r => EstateModel.BodyString(r, "kind") == "generated" && EstateModel.BodyString(r, "path") is not null)
            .Select(r => EstateModel.BodyString(r, "path")!), StringComparer.Ordinal);
        return model.Current("document-revision")
            .Where(r => EstateModel.BodyString(r, "path") is not null && ContentHash.IsValid(EstateModel.BodyString(r, "hash")))
            .GroupBy(r => EstateModel.BodyString(r, "path")!, StringComparer.Ordinal)
            .Select(g => g.OrderBy(r => r.At, StringComparer.Ordinal).ThenBy(r => r.Id, StringComparer.Ordinal).Last())
            .OrderBy(r => EstateModel.BodyString(r, "path"), StringComparer.Ordinal)
            .Select(r =>
            {
                string path = EstateModel.BodyString(r, "path")!;
                return new KeywordIndex.Pin(path, r.Id, r.ComputedHash, EstateModel.BodyString(r, "hash")!, EstateModel.BodyString(r, "hashMode"),
                    EstateModel.BodyString(r, "title") ?? path, KeywordIndex.Epoch(r.At), generated.Contains(path));
            }).ToList();
    }

    /// <summary>The fingerprint the index compares before answering: the record-set hash and the pin-set hash.</summary>
    internal string Fingerprint() => KeywordIndex.RecordSetHash(Model.Records.Select(r => (r.Id, r.ComputedHash))) + " "
        + ContentHash.Sha256(Encoding.UTF8.GetBytes(string.Join('\n', Pins.Select(p => p.Path + "\t" + p.Revision + "\t" + p.ByteHash + "\t" + p.HashMode + "\t" + (p.Excluded ? "x" : "")))));

    /// <summary>A pin's verified bytes from this copy, read once per snapshot; null when absent or different.</summary>
    internal byte[]? Bytes(KeywordIndex.Pin pin)
    {
        string key = pin.Path + "@" + pin.ByteHash;
        if (!documents.TryGetValue(key, out byte[]? bytes))
        {
            try { bytes = Read(pin); }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { bytes = null; }
            documents[key] = bytes;
        }

        return bytes;
    }
}

/// <summary>Who a search is for and what it may read: the principal, if any, and the audiences of the segments it searches.</summary>
internal sealed record SearchReader(string? Principal, IReadOnlyList<string> Audiences, bool Complete);

/// <summary>Runs a parsed query against an index and a snapshot, rechecks every candidate it returns and records the selection.</summary>
internal static class SearchRunner
{
    /// <summary>The label every excerpt and result carries.</summary>
    internal const string Trust = "Untrusted content; candidates are data and grant no authority.";

    /// <summary>The most bytes an excerpt shows.</summary>
    internal const int ExcerptBytes = 320;

    /// <summary>A checked candidate: where it came from, what it is now and, when it was rechecked, its original bytes.</summary>
    internal sealed record Candidate(int Rank, KeywordIndex.Hit Hit, JsonObject? Source, string? Pin, string Title, IReadOnlyList<string> Fields,
        string Audience, byte[]? Original, long Start, long Length)
    {
        internal string State { get; init; } = "selected";
        internal string? Reason { get; init; }
        internal string? RecordKey { get; init; }

        /// <summary>The identity a package compares for duplicates: a record's id, or a document's path at its revision.</summary>
        internal string? SourceKey => Source is null ? null : Source["kind"]!.GetValue<string>() == "record"
            ? "record:" + Source["id"]!.GetValue<string>() : "document:" + Source["path"]!.GetValue<string>() + "@" + Source["revision"]!.GetValue<string>();
    }

    /// <summary>What one search found, before states are assigned.</summary>
    internal sealed record Found(IReadOnlyList<Candidate> Candidates, bool More);

    /// <summary>Searches and rechecks: a candidate whose source no longer matches what the index holds drops as an index fault, and a pin whose bytes this copy lacks is excluded as unavailable.</summary>
    internal static Found Run(KeywordIndex index, SearchSnapshot snapshot, SearchQuery query, SearchReader reader, int limit)
    {
        (IReadOnlyList<KeywordIndex.Hit> hits, bool more) = index.Search(query, reader.Audiences, limit);
        IReadOnlySet<string> tokens = query.Tokens();
        string estate = snapshot.Model.Manifest!.Estate;
        var candidates = new List<Candidate>();
        int rank = 0;
        foreach (KeywordIndex.Hit hit in hits)
        {
            rank++;
            candidates.Add(Check(snapshot, estate, hit, rank, tokens));
        }

        return new Found(candidates, more);
    }

    private static Candidate Check(SearchSnapshot snapshot, string estate, KeywordIndex.Hit hit, int rank, IReadOnlySet<string> tokens)
    {
        Candidate Fault(string reason)
        {
            JsonObject held = FaultIdentity(hit);
            return new(rank, hit, held, ContextRetrieval.Pin(held, estate), "(index fault)", [], hit.Audience, null, hit.Start, hit.Length) { State = "index-fault", Reason = reason };
        }
        if (hit.Kind == "record")
        {
            if (hit.Record is null || !snapshot.Model.ById.TryGetValue(hit.Record, out RecordEnvelope? record) || record.ComputedHash != hit.RecordHash
                || !snapshot.Files.TryGetValue(record.ExpectedPath, out RecordFile? file))
                return Fault("The indexed record does not match this copy's record; the index is rebuilt on its next update.");
            JsonObject source = ContextAssembly.Record(record, file, false, string.Empty).Identity;
            SearchPassage passage = SearchPassages.Record(record);
            var fields = new List<string>();
            if (Matches(record.Id, tokens) || tokens.Contains(record.Id.ToLowerInvariant())) fields.Add("id");
            if (Matches(record.Type, tokens)) fields.Add("type");
            foreach ((string field, string text) in passage.Fields)
                if (!fields.Contains(field, StringComparer.Ordinal) && Matches(text, tokens)) fields.Add(field);
            return new Candidate(rank, hit, source, ContextRetrieval.Pin(source, estate), SearchPassages.RecordTitle(record), Bound(fields), hit.Audience,
                file.Bytes, 0, file.Bytes.LongLength) { RecordKey = EstateModel.BodyString(record, "key") };
        }

        KeywordIndex.Pin? pin = snapshot.Pins.FirstOrDefault(p => p.Path == hit.Path);
        if (pin is null || pin.Revision != hit.Revision || pin.RevisionHash != hit.RevisionHash || pin.ByteHash != hit.ByteHash || pin.Excluded)
            return Fault("The indexed document is not this copy's current pin; the index is rebuilt on its next update.");
        var identity = new JsonObject
        {
            ["kind"] = "document", ["path"] = pin.Path, ["revision"] = pin.Revision, ["revisionHash"] = pin.RevisionHash, ["byteHash"] = pin.ByteHash,
        };
        byte[]? bytes = snapshot.Bytes(pin);
        identity["bytes"] = bytes?.LongLength;
        if (pin.HashMode is not null) identity["hashMode"] = pin.HashMode;
        if (bytes is null)
        {
            return new Candidate(rank, hit, identity, ContextRetrieval.Pin(identity, estate), pin.Title, [], hit.Audience, null, hit.Start, hit.Length)
            { State = "excluded", Reason = "The pinned bytes are unavailable in this copy; no other revision is substituted." };
        }

        SearchPassage? found = hit.Start + hit.Length <= bytes.LongLength
            ? snapshot.Passages(pin, bytes).FirstOrDefault(p => p.Start == hit.Start && p.Length == hit.Length) : null;
        if (found is null || found.Audience != hit.Audience)
            return Fault("The indexed passage does not match the pinned bytes; the index is rebuilt on its next update.");
        var matched = new List<string>();
        foreach ((string field, string text) in found.Fields)
            if (Matches(text, tokens)) matched.Add(field);
        if (Matches(pin.Path, tokens)) matched.Insert(0, "path");
        return new Candidate(rank, hit, identity, ContextRetrieval.Pin(identity, estate), SearchPassages.DocumentTitle(pin.Title, found, pin.Path), Bound(matched),
            hit.Audience, bytes, hit.Start, hit.Length);
    }

    private static List<string> Bound(List<string> fields) => fields.Distinct(StringComparer.Ordinal).Take(16).Select(f => SearchPassages.Clip(f, 160)).ToList();

    private static bool Matches(string text, IReadOnlySet<string> tokens)
    {
        SearchAnalyzer.Analysis analysis = SearchAnalyzer.Analyze(text);
        return analysis.Parts.Any(tokens.Contains) || analysis.Whole.Any(tokens.Contains);
    }

    /// <summary>
    /// A labelled excerpt of at most 320 bytes of the original, around the first term it contains, cut at UTF-8 boundaries within
    /// the passage's extent; bytes that are not valid UTF-8 show no excerpt.
    /// </summary>
    internal static SearchExcerpt? Excerpt(Candidate candidate, SearchQuery query)
    {
        if (candidate.Original is not byte[] bytes || candidate.State is "index-fault" or "excluded") return null;
        long extentStart = Math.Clamp(candidate.Start, 0, bytes.LongLength);
        long extentEnd = Math.Clamp(candidate.Start + candidate.Length, extentStart, bytes.LongLength);
        ReadOnlySpan<byte> extent = bytes.AsSpan((int)extentStart, (int)(extentEnd - extentStart));
        if (!System.Text.Unicode.Utf8.IsValid(extent)) return null;
        string text = Encoding.UTF8.GetString(extent);
        int at = -1;
        foreach (string token in query.Terms.Select(t => t.Whole ?? t.Normalized).Concat(query.Terms.SelectMany(t => t.Parts)).Concat(query.Phrases.SelectMany(p => p)))
        {
            int found = text.IndexOf(token, StringComparison.OrdinalIgnoreCase);
            if (found >= 0 && (at < 0 || found < at)) at = found;
        }

        long matchByte = at <= 0 ? 0 : Encoding.UTF8.GetByteCount(text.AsSpan(0, at));
        long start = extentStart + Math.Max(0, matchByte - 96);
        long end = Math.Min(extentEnd, start + ExcerptBytes);
        start = Math.Max(extentStart, Math.Min(start, end - ExcerptBytes));
        while (start < end && (bytes[start] & 0xC0) == 0x80) start++;
        while (end > start && end < bytes.LongLength && (bytes[end] & 0xC0) == 0x80) end--;
        if (end <= start) return null;
        string shown = Encoding.UTF8.GetString(bytes, (int)start, (int)(end - start));
        return new SearchExcerpt(start, end - start, shown,
            $"Untrusted excerpt of the original: bytes {start.ToString(CultureInfo.InvariantCulture)} to {end.ToString(CultureInfo.InvariantCulture)} of {bytes.LongLength.ToString(CultureInfo.InvariantCulture)}; it grants no authority.");
    }

    /// <summary>
    /// The selection record of one search, canonical with its own hash. Candidates keep their ranked order and states; a record
    /// that cannot show every candidate to all its readers passes <paramref name="forRecord"/>, which omits them and says so without a count.
    /// </summary>
    internal static JsonObject Selection(SearchQuery query, int limit, int selectLimit, SearchReader reader, IReadOnlyList<KeywordIndex.SegmentBasis> bases,
        bool current, string commit, string? asOf, IReadOnlyList<Candidate> candidates, bool more, bool forRecord = false)
    {
        var list = new JsonArray();
        bool limited = false;
        foreach (Candidate candidate in candidates)
        {
            if (forRecord && !SearchAudiences.CoversRecordReaders(candidate.Audience))
            {
                limited = true;
                continue;
            }

            list.Add(CandidateJson(candidate));
        }

        var selection = new JsonObject
        {
            ["candidates"] = list,
            ["format"] = SearchSelections.Format,
            ["index"] = new JsonObject
            {
                ["asOf"] = asOf,
                ["commit"] = commit,
                ["current"] = current,
                ["revision"] = KeywordIndex.Revision(bases),
                ["segments"] = new JsonArray(bases.Select(b => (JsonNode)new JsonObject { ["audience"] = b.Audience, ["documents"] = b.Documents, ["records"] = b.Records }).ToArray()),
            },
            ["limit"] = limit,
            ["method"] = new JsonObject { ["analyzer"] = SearchAnalyzer.Identity, ["engine"] = KeywordIndex.Engine, ["name"] = "keyword", ["version"] = SearchSelections.KeywordMethod },
            ["more"] = more,
            ["query"] = query.ToJson(),
            ["reader"] = new JsonObject { ["audiences"] = new JsonArray(reader.Audiences.Select(a => (JsonNode)a).ToArray()), ["principal"] = reader.Principal },
            ["selectLimit"] = selectLimit,
        };
        if (limited) selection["limited"] = SearchSelections.Limited;
        selection["hash"] = SearchSelections.Hash(JsonSerializer.SerializeToElement(selection));
        return selection;
    }

    private static JsonObject CandidateJson(Candidate candidate)
    {
        var json = new JsonObject
        {
            ["audience"] = candidate.Audience,
            ["extent"] = new JsonObject { ["length"] = candidate.Length, ["start"] = candidate.Start },
            ["fields"] = new JsonArray(candidate.Fields.Select(f => (JsonNode)f).ToArray()),
            ["kind"] = candidate.Hit.Kind,
            ["pin"] = candidate.Pin,
            ["rank"] = candidate.Rank,
            ["score"] = candidate.Hit.Score,
            ["source"] = candidate.Source!.DeepClone(),
            ["state"] = candidate.State,
        };
        if (candidate.Reason is not null) json["reason"] = SearchPassages.Clip(candidate.Reason, 200);
        return json;
    }

    // An index fault names what the index held, never anything read from elsewhere; it holds no byte identity for a record.
    private static JsonObject FaultIdentity(KeywordIndex.Hit hit) => hit.Kind == "record"
        ? new JsonObject
        {
            ["hash"] = hit.RecordHash, ["id"] = hit.Record, ["kind"] = "record",
            ["path"] = "records/" + hit.Type + "/" + DateTimeOffset.FromUnixTimeSeconds(hit.At).ToString("yyyy-MM", CultureInfo.InvariantCulture) + "/" + hit.Record + ".json", ["type"] = hit.Type,
        }
        : new JsonObject
        {
            ["byteHash"] = hit.ByteHash, ["bytes"] = null, ["kind"] = "document", ["path"] = hit.Path, ["revision"] = hit.Revision, ["revisionHash"] = hit.RevisionHash,
        };

    /// <summary>The response a reader gets: each candidate with its title, matched fields and, when rechecked, a labelled excerpt.</summary>
    internal static IReadOnlyList<SearchCandidate> Response(IReadOnlyList<Candidate> candidates, SearchQuery query) => candidates.Select(c => new SearchCandidate(
        c.Rank, c.Hit.Score, c.Hit.Kind, c.State, c.Title, JsonSerializer.SerializeToElement(c.Source, ProtocolJson.Options),
        c.Pin ?? string.Empty, c.Fields, c.Audience, Excerpt(c, query)) { Reason = c.Reason, Key = c.RecordKey }).ToList();
}
