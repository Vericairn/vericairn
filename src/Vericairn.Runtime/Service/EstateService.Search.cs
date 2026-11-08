// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;
using Vericairn.Protocol;
using Vericairn.Records.Canonical;
using Vericairn.Records.Estate;
using Vericairn.Records.Hashing;
using Vericairn.Records.Identity;
using Vericairn.Records.Model;
using Vericairn.Records.Search;
using Vericairn.Records.Types;
using Vericairn.Runtime.Providers;
using Vericairn.Runtime.Search;
using static Vericairn.Runtime.Service.EstateServiceValues;

namespace Vericairn.Runtime.Service;

public sealed partial class EstateService
{
    private const int DefaultSearchLimit = 10;
    private const int SearchSelectLimit = 3;
    // One replay request rebuilds at most this many selections; the rest are named and left for a request of their own.
    private const int MaxReplaySelections = 20;

    /// <summary>The most selections one replay request replays; a test lowers it.</summary>
    internal int ReplaySelectionLimit { get; set; } = MaxReplaySelections;
    private static readonly TimeSpan SearchBudget = TimeSpan.FromMilliseconds(250);
    private static readonly string[] IndexedDirectories = ["records", "design", "docs"];
    private readonly Dictionary<string, string> searchCommits = new(StringComparer.Ordinal);
    private KeywordIndex? keywordIndex;

    /// <summary>
    /// The audiences a principal may read in search, or null for every segment. Today every admitted reader reads the whole estate,
    /// so the rule answers null; Knowledge, read-only participation and network access bring finer audiences. A test narrows it to
    /// exercise the segments. A null principal is the machine's own caller. A rule that needs the estate asks the provider for the
    /// judged estate, loaded only then.
    /// </summary>
    internal Func<Func<EstateModel>, string?, IReadOnlyList<string>?> SearchAudienceRule { get; set; } = static (_, _) => null;

    /// <summary>Where the index lives: beside the live store, or nowhere (in memory) for a read-only runtime that creates no durable state.</summary>
    internal string? SearchIndexPath => session.IsReadOnly ? null : Path.Combine(Path.GetDirectoryName(session.Live.Path)!, KeywordIndex.FileName);

    /// <summary>Test seam: the open index, created when needed.</summary>
    internal KeywordIndex SearchStoreForTests() { lock (gate) return SearchStore(); }

    /// <summary>Test seam: what a failed index operation does, closing the index and deleting it only when no other process has it open.</summary>
    internal void ResetSearchStoreForTests() { lock (gate) ResetSearchStore(); }

    /// <inheritdoc />
    public SearchResponse Search(SearchRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        lock (gate)
        {
            (EstateModel model, IReadOnlyDictionary<string, RecordFile> files) = session.LoadIdentities();
            Func<EstateModel> judged = Judged();
            if (DeviceRefusal(judged) is not null) return SearchRefusal("denied", ContextDenied);
            if (request.Method is not (null or "keyword"))
                return request.Method == "semantic"
                    ? SearchRefusal("search-method-unavailable", "Semantic search needs a semantic search Adapter, and none is installed or enabled on this machine; the keyword method answers without one. Nothing was sent anywhere.")
                    : SearchRefusal("invalid", "A search's method is keyword or semantic.");
            (SearchQuery? query, SearchQuery.Refusal? refusal) = SearchQuery.Parse(request.Query);
            if (refusal is not null) return SearchRefusal(refusal.Code, refusal.Message);
            int limit = request.Limit ?? DefaultSearchLimit;
            if (limit is < 1 or > SearchSelections.MaxCandidates)
                return SearchRefusal("invalid", $"A search requests 1 to {SearchSelections.MaxCandidates} candidates.");
            if (request.Principal is not null && !IsUlid(request.Principal)) return SearchRefusal("invalid", "A search is for a principal named by its record id.");
            for (int attempt = 0; ; attempt++)
            {
                try
                {
                    KeywordIndex index = SearchStore();
                    SearchSnapshot snapshot = Snapshot(model, files);
                    bool current = Freshen(index, snapshot, request.AllowStale ? DateTimeOffset.UtcNow + SearchBudget : null);
                    SearchReader reader = Reader(judged, index, request.Principal);
                    SearchRunner.Found found = SearchRunner.Run(index, snapshot, query!, reader, limit);
                    IReadOnlyList<KeywordIndex.SegmentBasis> bases = index.Bases(reader.Audiences);
                    string commit = SearchCommit(KeywordIndex.Revision(bases), current, reader);
                    JsonObject selection = SearchRunner.Selection(query!, limit, limit, reader, bases, current, commit, index.AsOf(reader.Audiences), found.Candidates, found.More);
                    IReadOnlyList<SearchCandidate> candidates = SearchRunner.Response(found.Candidates, query!);
                    string? completeness = reader.Complete ? null : ContextDenied;
                    return new SearchResponse(RenderSearch(query!, candidates, current, found.More, completeness, selection), Element(selection), candidates, current, found.More, completeness, null);
                }
                catch (SearchQueryRejectedException)
                {
                    return SearchRefusal("search-query-invalid", "The search engine could not read this query; rephrase it as plain words and phrases.");
                }
                catch (Exception exception) when (exception is SqliteException or IOException or UnauthorizedAccessException or InvalidDataException)
                {
                    ResetSearchStore();
                    if (attempt > 0) return SearchRefusal("search-index-unavailable", "The machine-local search index could not be opened or updated. It is derived from the records and documents, so nothing is lost; check the Vericairn home's permissions and space, then search again.");
                }
            }
        }
    }

    /// <inheritdoc />
    public SearchIndexResponse SearchIndex(SearchIndexRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        lock (gate)
        {
            (EstateModel model, IReadOnlyDictionary<string, RecordFile> files) = session.LoadIdentities();
            Func<EstateModel> judged = Judged();
            if (DeviceRefusal(judged) is not null) return IndexRefusal("denied", ContextDenied);
            // A full rebuild holds the estate's gate for as long as indexing everything takes, so it runs from this machine's own
            // command line or for an operator; a paired device can bring the index up to date but not rebuild it.
            if (request.Rebuild && requestDevice.Value is { Closed: false } && !(RequestPrincipal(judged()) is string requester && judged().HasRole(requester, Roles.Operator)))
                return IndexRefusal("denied", "A full rebuild of the search index runs on this machine's own command line or for an operator; a paired device can bring the index up to date with vericairn search index.");
            var watch = Stopwatch.StartNew();
            for (int attempt = 0; ; attempt++)
            {
                try
                {
                    KeywordIndex index = SearchStore();
                    if (request.Rebuild) index.Rebuild();
                    SearchSnapshot snapshot = Snapshot(model, files);
                    string fingerprint = snapshot.Fingerprint();
                    KeywordIndex.UpdateResult? result = index.Matches(fingerprint, model.Root) ? null
                        : index.Update(model, files, snapshot.Pins, fingerprint, snapshot.Bytes, null);
                    long elapsed = watch.ElapsedMilliseconds;
                    SearchReader reader = Reader(judged, index, null);
                    IReadOnlyList<KeywordIndex.SegmentBasis> bases = index.Bases(reader.Audiences);
                    // The index's total size, its source bytes and the update's counts include every segment, so a reader of some
                    // segments only is told none of them.
                    long? sourceBytes = null, size = null;
                    if (reader.Complete)
                    {
                        IReadOnlySet<string> indexed = index.IndexedDocuments();
                        sourceBytes = files.Values.Sum(f => f.Bytes.LongLength) + snapshot.Pins.Where(p => indexed.Contains(p.Path)).Sum(p => snapshot.Bytes(p)?.LongLength ?? 0);
                        size = index.Size();
                    }

                    IReadOnlyList<string> unavailable = reader.Complete ? index.UnavailableDocuments() : [];
                    var segmentsSeen = bases.Select(b => new SearchSegment(b.Audience, b.Passages, b.Records, b.Documents)).ToList();
                    string text = RenderIndex(result?.Current ?? true, KeywordIndex.Revision(bases), segmentsSeen, size, sourceBytes, elapsed, reader.Complete ? result : null, result is null, unavailable);
                    return new SearchIndexResponse(text, SearchSelections.KeywordMethod, KeywordIndex.Revision(bases), result?.Current ?? true, segmentsSeen, size, sourceBytes, elapsed, null)
                    { Unavailable = unavailable };
                }
                catch (Exception exception) when (exception is SqliteException or IOException or UnauthorizedAccessException or InvalidDataException)
                {
                    ResetSearchStore();
                    if (attempt > 0) return IndexRefusal("search-index-unavailable", "The machine-local search index could not be opened or updated; check the Vericairn home's permissions and space.");
                }
            }
        }
    }

    /// <inheritdoc />
    public SearchReplayResponse SearchReplay(SearchReplayRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        lock (gate)
        {
            (EstateModel model, IReadOnlyDictionary<string, RecordFile> files) = session.LoadIdentities();
            Func<EstateModel> judged = Judged();
            if (DeviceRefusal(judged) is not null) return new(ContextDenied + "\n", [], new Problem("denied", ContextDenied, []));
            var selections = new List<JsonElement>();
            if (request.Selection is JsonElement given && given.ValueKind == JsonValueKind.Object)
            {
                selections.Add(given);
            }
            else if (request.Record is string id)
            {
                if (!model.ById.TryGetValue(id, out RecordEnvelope? record) || record.Type != "operation")
                    return new("No operation record has that id.\n", [], new Problem("not-found", "No operation record has that id.", []));
                if (record.Body.TryGetProperty("execution", out JsonElement execution) && execution.ValueKind == JsonValueKind.Object
                    && execution.TryGetProperty("contextSelections", out JsonElement context) && context.ValueKind == JsonValueKind.Array)
                    selections.AddRange(context.EnumerateArray());
                if (record.Body.TryGetProperty("searchesObserved", out JsonElement observed) && observed.ValueKind == JsonValueKind.Object
                    && observed.TryGetProperty("selections", out JsonElement served) && served.ValueKind == JsonValueKind.Array)
                    selections.AddRange(served.EnumerateArray());
                if (request.SelectionHash is string hash)
                    selections = selections.Where(s => s.TryGetProperty("hash", out JsonElement h) && h.ValueKind == JsonValueKind.String && h.GetString() == hash).ToList();
                if (selections.Count == 0)
                    return new("That record holds no such selection.\n", [], new Problem("not-found", "That record holds no such selection.", []));
            }
            else
            {
                return new("Give a selection or an agent-run operation record.\n", [], new Problem("invalid", "Give a selection or an agent-run operation record.", []));
            }

            var outcomes = new List<SearchReplayOutcome>();
            // Each earlier basis is rebuilt once per request, however many of its selections are replayed.
            var rebuilt = new Dictionary<string, (KeywordIndex Index, SearchSnapshot Snapshot)?>(StringComparer.Ordinal);
            try
            {
                foreach ((JsonElement selection, int position) in selections.Select((s, i) => (s, i)))
                {
                    if (position >= ReplaySelectionLimit)
                    {
                        outcomes.Add(new(SelectionHash(selection), false, null, "unavailable", $"Not replayed: one request replays at most {ReplaySelectionLimit.ToString(CultureInfo.InvariantCulture)} selections; replay this one by its hash.", []));
                        continue;
                    }

                    try { outcomes.Add(ReplayOne(model, files, judged, selection, rebuilt)); }
                    catch (Exception exception) when (exception is SqliteException or IOException or UnauthorizedAccessException or InvalidDataException)
                    {
                        ResetSearchStore();
                        outcomes.Add(new(SelectionHash(selection), false, null, "unavailable", "The search index could not be read; nothing was replayed or approximated.", []));
                    }
                    catch (Exception exception) when (exception is CanonicalJsonException or JsonException or InvalidOperationException or KeyNotFoundException or FormatException)
                    {
                        // A selection given whole is untrusted input: one that cannot be read is answered, never thrown.
                        outcomes.Add(new(SelectionHash(selection), false, null, "unavailable", "Not a readable " + SearchSelections.Format + "; nothing was replayed or approximated.", []));
                    }
                }
            }
            finally
            {
                foreach (var built in rebuilt.Values) built?.Index.Dispose();
            }

            return new SearchReplayResponse(RenderReplay(outcomes), outcomes, null);
        }
    }

    /// <inheritdoc />
    public ContextResponse Context(ContextRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return ContextFor(request.WorkOrder, request.MaxBytes, request.Search, request.Principal);
    }

    /// <inheritdoc />
    public BriefResponse? BriefContext(BriefContextRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return ReadBrief(request.WorkOrder, false, request.MaxBytes, request.Recipient, request.Assignment, request.View, request.Search, request.Principal);
    }

    // Search reads identities, which need no judgment; who is asking, and what they may read, is always judged on the full estate,
    // loaded at most once per operation and only when something asks.
    private Func<EstateModel> Judged()
    {
        EstateModel? judged = null;
        return () => judged ??= session.Load();
    }

    // A device-signed request is judged on the full, current estate, never on parsed identities. Other callers carry no device.
    private Problem? DeviceRefusal(Func<EstateModel> judged) => requestDevice.Value is { Closed: false } ? RequestDeviceRefusal(judged()) : null;

    private static bool IsUlid(string value) => Ulid.TryParse(value, out Ulid parsed) && parsed.Value == value;

    private KeywordIndex SearchStore() => keywordIndex ??= KeywordIndex.Open(SearchIndexPath);

    // A failed operation closes the index. Its files are deleted only when no other process (a run's MCP server, a command beside
    // the daemon) has them open; otherwise the next open reuses or rebuilds the index in place.
    private void ResetSearchStore()
    {
        keywordIndex?.Dispose();
        keywordIndex = null;
        if (SearchIndexPath is not string path) return;
        try { KeywordIndex.TryDelete(path); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { /* The next open replaces what it cannot trust. */ }
    }

    private static SearchSnapshot Snapshot(EstateModel model, IReadOnlyDictionary<string, RecordFile> files) =>
        new(model, files, SearchSnapshot.CurrentPins(model), pin => ContextSelection.LocalOriginal(model.Root, pin.Path, pin.ByteHash, null, pin.HashMode));

    // Before answering, the index is compared with the snapshot and updated incrementally; a deadline answers from what is indexed.
    private static bool Freshen(KeywordIndex index, SearchSnapshot snapshot, DateTimeOffset? deadline)
    {
        string fingerprint = snapshot.Fingerprint();
        if (index.Matches(fingerprint, snapshot.Model.Root)) return true;
        return index.Update(snapshot.Model, snapshot.Files, snapshot.Pins, fingerprint, snapshot.Bytes, deadline).Current;
    }

    // The reader's audiences: what the named principal may read, narrowed by what the caller may read, so naming a principal can
    // only narrow a search. A reader of every segment searches all of them and can be told the results are complete for its scope.
    private SearchReader Reader(Func<EstateModel> judged, KeywordIndex index, string? principal)
    {
        string? caller = requestDevice.Value is { Closed: false } ? RequestPrincipal(judged()) : null;
        IReadOnlyList<string>? forPrincipal = principal is null ? null : SearchAudienceRule(judged, principal);
        IReadOnlyList<string>? forCaller = SearchAudienceRule(judged, caller);
        IReadOnlyList<string>? effective = forPrincipal is null ? forCaller : forCaller is null ? forPrincipal : forPrincipal.Intersect(forCaller, StringComparer.Ordinal).ToList();
        IEnumerable<string> audiences = effective ?? index.Audiences().Concat(SearchAudiences.Standard);
        return new SearchReader(principal ?? caller, SearchAudiences.Ordered(audiences).ToList(), effective is null);
    }

    // The commit the indexed records and pins equal, when the clone is clean at it; otherwise uncommitted. A reader that cannot read
    // every segment is told uncommitted, since a clean or changed tree can turn on restricted files. A commit is kept per index
    // revision; an uncommitted answer only until the repository's index or HEAD changes.
    private string SearchCommit(string revision, bool current, SearchReader reader)
    {
        if (!current || !reader.Complete) return "uncommitted";
        string? stamp = GitStamp();
        if (searchCommits.TryGetValue(revision, out string? known) && known != "uncommitted") return known;
        if (stamp is not null && searchCommits.TryGetValue(revision + "|" + stamp, out string? dirty)) return dirty;
        string commit = "uncommitted";
        if (session.Profile != "local-only" && Providers.Source("git") is ISourceAdapter git)
        {
            try
            {
                RepositoryObservation observed = git.Inspect(session.Root);
                if (observed.Clean && Patterns.Commit.IsMatch(observed.Commit)) commit = observed.Commit;
            }
            catch (Exception exception) when (exception is ProviderRefusedException or IOException or InvalidOperationException or UnauthorizedAccessException)
            {
                // No observation: the selection says uncommitted rather than guessing.
            }
        }

        if (searchCommits.Count >= 64) searchCommits.Clear();
        if (commit != "uncommitted") searchCommits[revision] = commit;
        else if (stamp is not null) searchCommits[revision + "|" + stamp] = commit;
        return commit;
    }

    // When the estate's repository index or HEAD was last written, or null when the root holds no ordinary .git directory.
    private string? GitStamp()
    {
        try
        {
            string git = Path.Combine(session.Root, ".git");
            if (!Directory.Exists(git)) return null;
            var index = new FileInfo(Path.Combine(git, "index"));
            var head = new FileInfo(Path.Combine(git, "HEAD"));
            return (index.Exists ? index.LastWriteTimeUtc.Ticks : 0).ToString(CultureInfo.InvariantCulture) + "/" + (head.Exists ? head.LastWriteTimeUtc.Ticks : 0).ToString(CultureInfo.InvariantCulture);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { return null; }
    }

    /// <summary>
    /// Search as a selection method, after the explicit selection: up to five queries over the package's own snapshot; candidates
    /// already selected are duplicates, the top three others of each query become optional sources pinned by exact hash, each
    /// reread and checked against its pin, and every search is recorded. Nothing is made mandatory or reordered.
    /// </summary>
    private (List<ContextAssembly.Source>? Sources, JsonArray? Selections, Problem? Problem) SearchSources(EstateModel model, IReadOnlyList<RecordFile> files,
        IReadOnlyList<ContextAssembly.Source> explicitSources, IReadOnlyList<string> searches, string? principal)
    {
        if (searches.Count > SearchSelections.MaxContextSelections)
            return (null, null, new Problem("invalid-search", $"A context package takes at most {SearchSelections.MaxContextSelections} searches.", []));
        if (principal is not null && !IsUlid(principal)) return (null, null, new Problem("invalid-search", "A package is for a principal named by its record id.", []));
        var queries = new List<SearchQuery>();
        for (int i = 0; i < searches.Count; i++)
        {
            (SearchQuery? query, SearchQuery.Refusal? refusal) = SearchQuery.Parse(searches[i]);
            if (refusal is not null)
                return (null, null, new Problem("invalid-search", $"Search s{(i + 1).ToString(CultureInfo.InvariantCulture)}: {refusal.Message}", []));
            queries.Add(query!);
        }

        Dictionary<string, RecordFile> byPath = files.ToDictionary(f => f.Path, StringComparer.Ordinal);
        KeywordIndex index;
        SearchSnapshot snapshot;
        try
        {
            index = SearchStore();
            snapshot = Snapshot(model, byPath);
            if (!Freshen(index, snapshot, null)) throw new InvalidDataException("The index did not reach the snapshot.");
        }
        catch (Exception exception) when (exception is SqliteException or IOException or UnauthorizedAccessException or InvalidDataException)
        {
            ResetSearchStore();
            return (null, null, new Problem("search-index-stale", "The search index could not be brought up to date before preparation, so nothing was prepared. Run vericairn search index, then prepare again.", []));
        }

        SearchReader reader = Reader(() => model, index, principal);
        var keys = new HashSet<string>(explicitSources.Select(SourceKey).OfType<string>(), StringComparer.Ordinal);
        var sources = new List<ContextAssembly.Source>();
        var selections = new JsonArray();
        for (int q = 0; q < queries.Count; q++)
        {
            string name = "s" + (q + 1).ToString(CultureInfo.InvariantCulture);
            SearchRunner.Found found;
            try { found = SearchRunner.Run(index, snapshot, queries[q], reader, DefaultSearchLimit); }
            catch (SearchQueryRejectedException)
            {
                return (null, null, new Problem("invalid-search", $"Search {name}: the search engine could not read this query; rephrase it as plain words and phrases.", []));
            }
            var classified = new List<SearchRunner.Candidate>();
            int selected = 0;
            foreach (SearchRunner.Candidate candidate in found.Candidates)
            {
                if (candidate.State is "index-fault" or "excluded") classified.Add(candidate);
                else if (keys.Contains(candidate.SourceKey!)) classified.Add(candidate with { State = "duplicate" });
                else if (selected >= SearchSelectLimit) classified.Add(candidate with { State = "below-limit" });
                else
                {
                    selected++;
                    keys.Add(candidate.SourceKey!);
                    classified.Add(candidate);
                    sources.Add(SearchSource(model, byPath, candidate, name, q + 1));
                }
            }

            IReadOnlyList<KeywordIndex.SegmentBasis> bases = index.Bases(reader.Audiences);
            selections.Add(SearchRunner.Selection(queries[q], DefaultSearchLimit, SearchSelectLimit, reader, bases, true,
                SearchCommit(KeywordIndex.Revision(bases), true, reader), index.AsOf(reader.Audiences), classified, found.More));
        }

        return (sources, selections, null);
    }

    private static string? SourceKey(ContextAssembly.Source source) => source.Identity["kind"]?.GetValue<string>() switch
    {
        "record" => "record:" + source.Identity["id"]!.GetValue<string>(),
        "document" => "document:" + source.Identity["path"]!.GetValue<string>() + "@" + source.Identity["revision"]!.GetValue<string>(),
        _ => null,
    };

    private static ContextAssembly.Source SearchSource(EstateModel model, Dictionary<string, RecordFile> files, SearchRunner.Candidate candidate, string name, int origin)
    {
        string dependency = $"candidate from search {name}; optional; grants no authority";
        if (candidate.Hit.Kind == "record")
        {
            RecordEnvelope record = model.ById[candidate.Hit.Record!];
            return ContextAssembly.Record(record, files[record.ExpectedPath], false, dependency) with { Origin = origin, Rank = candidate.Rank, Search = name };
        }

        // A record stays whole; a document section may be a labelled excerpt from any UTF-8 boundary.
        bool whole = candidate.Start == 0 && candidate.Length == candidate.Original!.LongLength;
        return new ContextAssembly.Source(candidate.Source!.DeepClone().AsObject(), candidate.Original, false, dependency,
            ExcerptBytes: whole ? null : (int)candidate.Length, ExcerptStart: whole ? 0 : (int)candidate.Start)
        { Origin = origin, Rank = candidate.Rank, Search = name };
    }

    /// <summary>The selections a package's manifest holds, as a record every estate reader can read them: candidates its readers cannot all read are omitted, saying so without a count.</summary>
    internal static JsonArray? RecordedSelections(JsonElement package)
    {
        if (!package.TryGetProperty("payload", out JsonElement payload) || !payload.TryGetProperty("manifest", out JsonElement manifest)
            || !manifest.GetProperty("content").GetProperty("basis").TryGetProperty("selections", out JsonElement selections) || selections.ValueKind != JsonValueKind.Array)
            return null;
        var recorded = new JsonArray();
        foreach (JsonElement selection in selections.EnumerateArray()) recorded.Add(ForRecord(selection));
        return recorded;
    }

    /// <summary>
    /// One selection as a record keeps it: the same search, with only candidates every reader of the record may read, ranked again
    /// without gaps, and the reader's audiences and the index segments those readers cannot all read replaced by one opaque
    /// Restricted entry, with the index revision recomputed over what the record names, so no restricted audience's label, size,
    /// count or hash is recorded or can be confirmed from the record.
    /// </summary>
    internal static JsonObject ForRecord(JsonElement selection)
    {
        JsonObject copy = JsonNode.Parse(selection.GetRawText())!.AsObject();
        bool changed = false;
        JsonArray candidates = copy["candidates"]!.AsArray();
        var kept = new JsonArray(candidates.Where(c => SearchAudiences.CoversRecordReaders(c!["audience"]!.GetValue<string>())).Select(c => c!.DeepClone()).ToArray());
        if (kept.Count != candidates.Count)
        {
            // Kept candidates are ranked 1, 2, 3 in their order, so no gap counts the candidates omitted between them.
            for (int i = 0; i < kept.Count; i++) kept[i]!["rank"] = i + 1;
            copy["candidates"] = kept;
            copy["limited"] = SearchSelections.Limited;
            changed = true;
        }

        if (copy["reader"]?["audiences"] is JsonArray audiences && audiences.Any(a => !SearchAudiences.CoversRecordReaders(a!.GetValue<string>())))
        {
            copy["reader"]!["audiences"] = new JsonArray(audiences.Select(a => a!.GetValue<string>()).Where(SearchAudiences.CoversRecordReaders)
                .Append(SearchSelections.RestrictedAudience).Select(a => (JsonNode)a).ToArray());
            changed = true;
        }

        if (copy["index"]?["segments"] is JsonArray segments && segments.Any(s => !SearchAudiences.CoversRecordReaders(s!["audience"]!.GetValue<string>())))
        {
            var named = new JsonArray(segments.Where(s => SearchAudiences.CoversRecordReaders(s!["audience"]!.GetValue<string>())).Select(s => s!.DeepClone())
                .Append(new JsonObject { ["audience"] = SearchSelections.RestrictedAudience }).ToArray());
            copy["index"]!["segments"] = named;
            // The live revision covers every segment's label and bases; the record's covers only the segments it names and the
            // opaque entry, so it neither confirms a guessed label nor changes when only restricted content does.
            copy["index"]!["revision"] = ContentHash.Sha256(CanonicalJson.SerializeUtf8(JsonSerializer.SerializeToElement(
                new JsonObject { ["method"] = SearchSelections.KeywordMethod, ["segments"] = named.DeepClone() })));
            changed = true;
        }

        if (!changed) return copy;
        copy.Remove("hash");
        copy["hash"] = SearchSelections.Hash(JsonSerializer.SerializeToElement(copy));
        return copy;
    }

    private SearchReplayOutcome ReplayOne(EstateModel model, IReadOnlyDictionary<string, RecordFile> files, Func<EstateModel> judged, JsonElement selection,
        Dictionary<string, (KeywordIndex Index, SearchSnapshot Snapshot)?> rebuilt)
    {
        string hash = SelectionHash(selection);
        var findings = new FindingList();
        SearchSelections.Validate(new BodyReader(selection, "selection", findings, RecordFormats.Current));
        if (findings.HasErrors)
            return new(hash, false, null, "unavailable", "Not a valid " + SearchSelections.Format + ": " + findings.First(f => f.Severity == Severity.Error).Message, []);
        (SearchQuery? query, SearchQuery.Refusal? refusal) = SearchQuery.Parse(selection.GetProperty("query").GetProperty("text").GetString());
        if (query is null || refusal is not null || CanonicalJson.Serialize(JsonSerializer.SerializeToElement(query.ToJson()))
                != CanonicalJson.Serialize(selection.GetProperty("query")))
            return new(hash, false, null, "unavailable", "The recorded query does not parse to its recorded terms under this method; nothing was approximated.", []);

        KeywordIndex index = SearchStore();
        SearchSnapshot snapshot = Snapshot(model, files);
        Freshen(index, snapshot, null);
        SearchReader caller = Reader(judged, index, null);
        string[] recordedAudiences = selection.GetProperty("reader").GetProperty("audiences").EnumerateArray().Select(a => a.GetString()!).ToArray();
        // A record's selection names only the segments all its readers may read and one opaque entry for the rest: those are
        // searched again as they are now, by a reader of every segment, and only the named segments' bases can be confirmed.
        bool opaque = recordedAudiences.Contains(SearchSelections.RestrictedAudience, StringComparer.Ordinal);
        string[] audiences = recordedAudiences.Where(a => a != SearchSelections.RestrictedAudience).ToArray();
        if (opaque && !caller.Complete)
            return new(hash, false, null, "unavailable", "That selection's record omits restricted segments; only a reader of every segment can replay it.", []);
        if (!caller.Complete && audiences.Any(a => !caller.Audiences.Contains(a, StringComparer.Ordinal)))
            return new(hash, false, null, "unavailable", "This reader cannot replay that selection; completeness cannot be confirmed.", []);
        JsonElement principal = selection.GetProperty("reader").GetProperty("principal");
        SearchReader ReaderOf(KeywordIndex from) => new(principal.ValueKind == JsonValueKind.String ? principal.GetString() : null, opaque
            ? SearchAudiences.Ordered(audiences.Concat(from.Audiences().Concat(SearchAudiences.Standard).Where(a => !SearchAudiences.CoversRecordReaders(a)))).ToList()
            : audiences, true);
        int limit = selection.GetProperty("limit").GetInt32();
        string[] recorded = selection.GetProperty("index").GetProperty("segments").EnumerateArray()
            .Where(s => s.GetProperty("audience").GetString() != SearchSelections.RestrictedAudience)
            .Select(s => s.GetProperty("audience").GetString() + "\t" + s.GetProperty("records").GetString() + "\t" + s.GetProperty("documents").GetString()).ToArray();
        string[] Bases(KeywordIndex from) => from.Bases(audiences).Select(b => b.Audience + "\t" + b.Records + "\t" + b.Documents).ToArray();
        if (Bases(index).SequenceEqual(recorded, StringComparer.Ordinal))
            return Compare(hash, "current", SearchRunner.Run(index, snapshot, query, ReaderOf(index), limit), selection, opaque);

        // This copy may still hold exactly the selection's basis under later records: every record up to the selection's time, with
        // the pins then current, read from the working tree or, by exact hash, from local history. The basis hashes decide.
        if (selection.GetProperty("index").TryGetProperty("asOf", out JsonElement asOfValue) && asOfValue.ValueKind == JsonValueKind.String)
        {
            string asOf = asOfValue.GetString()!;
            HashSet<string> kept = RecordsAsOf(model, asOf, selection.GetProperty("index").GetProperty("segments").EnumerateArray()
                .Where(s => s.GetProperty("audience").GetString() == SearchAudiences.Internal).Select(s => s.GetProperty("records").GetString()).FirstOrDefault());
            var earlier = files.Values.Where(f => kept.Contains(f.Path)).OrderBy(f => f.Path, StringComparer.Ordinal).ToList();
            string key = "copy:" + ContentHash.Sha256(Encoding.UTF8.GetBytes(string.Join('\n', earlier.Select(f => f.Path))));
            if (!rebuilt.TryGetValue(key, out var copyBasis))
            {
                EstateModel earlierModel = EstateLoader.Load(session.Root, earlier);
                ISourceAdapter? history = Providers.Source("git");
                var earlierSnapshot = new SearchSnapshot(earlierModel, earlier.ToDictionary(f => f.Path, StringComparer.Ordinal), SearchSnapshot.CurrentPins(earlierModel),
                    pin => ContextSelection.LocalOriginal(session.Root, pin.Path, pin.ByteHash, history, pin.HashMode));
                KeywordIndex built = KeywordIndex.Open(null);
                rebuilt[key] = copyBasis = (built, earlierSnapshot);
                Freshen(built, earlierSnapshot, null);
            }

            if (copyBasis is { } copy && Bases(copy.Index).SequenceEqual(recorded, StringComparer.Ordinal))
                return Compare(hash, "copy", SearchRunner.Run(copy.Index, copy.Snapshot, query, ReaderOf(copy.Index), limit), selection, opaque);
        }

        string commit = selection.GetProperty("index").GetProperty("commit").GetString()!;
        if (commit == "uncommitted" || Providers.Source("git") is not ISourceAdapter git)
            return new(hash, false, null, "unavailable", "This copy holds neither the selection's basis nor its commit in local history; nothing was approximated.", []);
        if (!rebuilt.TryGetValue("commit:" + commit, out var commitBasis))
        {
            IReadOnlyList<SourceFile>? tree = git.ReadTreeAtCommit(session.Root, commit, IndexedDirectories);
            if (tree is not null)
            {
                var recordFiles = tree.Where(f => f.Path.StartsWith("records/", StringComparison.Ordinal) && f.Path.EndsWith(".json", StringComparison.Ordinal))
                    .Select(f => new RecordFile(f.Path, f.Bytes)).ToList();
                var documents = tree.Where(f => !f.Path.StartsWith("records/", StringComparison.Ordinal)).ToDictionary(f => f.Path, f => f.Bytes, StringComparer.Ordinal);
                EstateModel then = EstateLoader.Load(session.Root, recordFiles);
                var thenSnapshot = new SearchSnapshot(then, recordFiles.ToDictionary(f => f.Path, StringComparer.Ordinal), SearchSnapshot.CurrentPins(then), pin => CommittedOriginal(documents, pin));
                KeywordIndex past = KeywordIndex.Open(null);
                commitBasis = (past, thenSnapshot);
                rebuilt["commit:" + commit] = commitBasis;
                Freshen(past, thenSnapshot, null);
            }
            else rebuilt["commit:" + commit] = null;
        }

        if (commitBasis is not { } atCommit)
            return new(hash, false, null, "unavailable", $"Local history does not hold commit {commit} of this estate, or cannot read it in full; nothing was approximated.", []);
        if (!Bases(atCommit.Index).SequenceEqual(recorded, StringComparer.Ordinal))
            return new(hash, false, null, "unavailable", $"The records and pins at commit {commit} do not reproduce the selection's basis; nothing was approximated.", []);
        return Compare(hash, "commit", SearchRunner.Run(atCommit.Index, atCommit.Snapshot, query, ReaderOf(atCommit.Index), limit), selection, opaque);
    }

    // The record files of a basis as of a time: every record before it, and of the few recorded in that same second, the ones whose
    // record-set hash reproduces the selection's own. Times have whole seconds, so later records of that second are told apart by hash.
    private static HashSet<string> RecordsAsOf(EstateModel model, string asOf, string? recordedSet)
    {
        List<RecordEnvelope> before = model.Records.Where(r => string.CompareOrdinal(r.At, asOf) < 0).ToList();
        List<RecordEnvelope> ties = model.Records.Where(r => r.At == asOf).OrderBy(r => r.Id, StringComparer.Ordinal).ToList();
        IEnumerable<RecordEnvelope> chosen = before.Concat(ties);
        if (recordedSet is not null && ties.Count is > 0 and <= 10)
        {
            for (int mask = (1 << ties.Count) - 1; mask >= 0; mask--)
            {
                List<RecordEnvelope> set = [.. before, .. ties.Where((_, i) => (mask & (1 << i)) != 0)];
                if (KeywordIndex.RecordSetHash(set.Select(r => (r.Id, r.ComputedHash))) != recordedSet) continue;
                chosen = set;
                break;
            }
        }

        return chosen.Select(r => r.ExpectedPath).ToHashSet(StringComparer.Ordinal);
    }

    private static byte[]? CommittedOriginal(Dictionary<string, byte[]> documents, KeywordIndex.Pin pin)
    {
        if (!documents.TryGetValue(pin.Path, out byte[]? bytes)) return null;
        if (pin.HashMode == RevisionLines.HashMode)
        {
            try { bytes = RevisionLines.AuthoredContent(bytes); }
            catch (Exception exception) when (exception is InvalidOperationException or ArgumentException) { return null; }
        }
        else if (pin.HashMode is not null) return null;
        return ContentHash.Sha256(bytes) == pin.ByteHash ? bytes : null;
    }

    private static SearchReplayOutcome Compare(string hash, string basis, SearchRunner.Found found, JsonElement selection, bool opaque)
    {
        bool limited = opaque || selection.TryGetProperty("limited", out _);
        var expected = selection.GetProperty("candidates").EnumerateArray()
            .ToDictionary(c => c.GetProperty("rank").GetInt32(), c => Describe(c.GetProperty("kind").GetString()!, c.GetProperty("source"), c.GetProperty("extent"), c.GetProperty("score").GetInt64()));
        var actual = new Dictionary<int, string>();
        int kept = 0;
        foreach (SearchRunner.Candidate candidate in found.Candidates)
        {
            if (limited && !SearchAudiences.CoversRecordReaders(candidate.Audience)) continue;
            JsonElement source = JsonSerializer.SerializeToElement(candidate.Source);
            JsonElement extent = JsonSerializer.SerializeToElement(new { start = candidate.Start, length = candidate.Length });
            // A record ranks its kept candidates again without gaps; the rerun's are ranked the same way.
            actual[limited ? ++kept : candidate.Rank] = Describe(candidate.Hit.Kind, source, extent, candidate.Hit.Score);
        }

        var differences = new List<string>();
        foreach (int rank in expected.Keys.Concat(actual.Keys).Distinct().Order())
        {
            string? was = expected.GetValueOrDefault(rank);
            string? now = actual.GetValueOrDefault(rank);
            if (was != now) differences.Add($"rank {rank.ToString(CultureInfo.InvariantCulture)}: recorded {was ?? "nothing"}; replayed {now ?? "nothing"}");
        }

        if (selection.GetProperty("more").GetBoolean() != found.More)
            differences.Add($"more: recorded {(selection.GetProperty("more").GetBoolean() ? "true" : "false")}; replayed {(found.More ? "true" : "false")}");
        string where = basis switch
        {
            "current" => "on this copy's identical basis",
            "copy" => "on its exact basis, rebuilt from this copy's records up to its time",
            _ => "on its commit, read from local history",
        };
        string unconfirmed = opaque ? " The record does not name its restricted segments, so their basis was not confirmed." : string.Empty;
        return new(hash, true, differences.Count == 0, basis, (differences.Count == 0
            ? $"Replayed {where}: the same ranked candidates."
            : $"Replayed {where}: {differences.Count.ToString(CultureInfo.InvariantCulture)} difference(s).") + unconfirmed, differences);
    }

    private static string Describe(string kind, JsonElement source, JsonElement extent, long score)
    {
        string identity = source.GetProperty("kind").GetString() == "record"
            ? "record " + source.GetProperty("id").GetString() + " " + source.GetProperty("hash").GetString()
            : kind + " " + source.GetProperty("path").GetString() + "@" + source.GetProperty("revision").GetString() + " bytes "
                + extent.GetProperty("start").GetInt64().ToString(CultureInfo.InvariantCulture) + "+" + extent.GetProperty("length").GetInt64().ToString(CultureInfo.InvariantCulture);
        return identity + " score " + score.ToString(CultureInfo.InvariantCulture);
    }

    private static string SelectionHash(JsonElement selection) =>
        selection.ValueKind == JsonValueKind.Object && selection.TryGetProperty("hash", out JsonElement hash) && hash.ValueKind == JsonValueKind.String && ContentHash.IsValid(hash.GetString())
            ? hash.GetString()! : "unknown";

    private static SearchResponse SearchRefusal(string code, string message) => new(message + "\n", null, [], false, false, null, new Problem(code, message, []));

    private static SearchIndexResponse IndexRefusal(string code, string message) =>
        new(message + "\n", SearchSelections.KeywordMethod, null, false, [], null, null, 0, new Problem(code, message, []));

    private static string RenderSearch(SearchQuery query, IReadOnlyList<SearchCandidate> candidates, bool current, bool more, string? completeness, JsonObject selection)
    {
        var text = new StringBuilder();
        text.Append("Search ").Append(SafeText(query.Text)).Append(": ").Append(candidates.Count.ToString(CultureInfo.InvariantCulture)).Append(candidates.Count == 1 ? " candidate" : " candidates")
            .Append(more ? " (more match)" : string.Empty).Append(" · index ").Append(current ? "current" : "stale; answered with --allow-stale")
            .Append(" · selection ").Append(selection["hash"]!.GetValue<string>()).Append('\n');
        text.Append(SearchRunner.Trust).Append('\n');
        if (completeness is not null) text.Append(completeness).Append('\n');
        foreach (SearchCandidate candidate in candidates)
        {
            text.Append('\n').Append(candidate.Rank.ToString(CultureInfo.InvariantCulture)).Append(". [").Append(candidate.Kind).Append("] ").Append(SafeText(candidate.Title))
                .Append(" · score ").Append(candidate.Score.ToString(CultureInfo.InvariantCulture)).Append(" · ").Append(candidate.State);
            if (candidate.Fields.Count > 0) text.Append(" · fields ").Append(SafeText(string.Join(", ", candidate.Fields)));
            text.Append('\n');
            if (candidate.Reason is not null) text.Append("   ").Append(candidate.Reason).Append('\n');
            if (candidate.State is not ("index-fault" or "excluded")) text.Append("   ").Append(ContextRetrieval.Command(candidate.Pin)).Append('\n');
            if (candidate.Excerpt is { } excerpt)
            {
                text.Append("   ").Append(excerpt.Label).Append('\n');
                foreach (string line in SafeText(excerpt.Text).Split('\n')) text.Append("   > ").Append(line).Append('\n');
            }
        }

        return text.ToString();
    }

    /// <summary>Untrusted text made visible: control, format and direction-changing characters are shown as escapes, never obeyed.</summary>
    internal static string SafeText(string value)
    {
        var text = new StringBuilder(value.Length);
        foreach (Rune rune in value.EnumerateRunes())
        {
            UnicodeCategory category = Rune.GetUnicodeCategory(rune);
            if (rune.Value == '\n') text.Append('\n');
            else if (category is UnicodeCategory.Control or UnicodeCategory.Format or UnicodeCategory.LineSeparator or UnicodeCategory.ParagraphSeparator or UnicodeCategory.Surrogate)
                text.Append("\\u{").Append(rune.Value.ToString("X4", CultureInfo.InvariantCulture)).Append('}');
            else text.Append(rune.ToString());
        }

        return text.ToString();
    }

    private static string RenderIndex(bool current, string revision, IReadOnlyList<SearchSegment> segments, long? size, long? source, long elapsed, KeywordIndex.UpdateResult? result,
        bool alreadyCurrent, IReadOnlyList<string> unavailable)
    {
        var text = new StringBuilder();
        text.Append("Search index ").Append(SearchSelections.KeywordMethod).Append(": ").Append(current ? "current" : "not current").Append(" · revision ").Append(revision).Append('\n');
        if (size is long bytes && source is long sourceBytes)
            text.Append(bytes.ToString(CultureInfo.InvariantCulture)).Append(" index bytes for ").Append(sourceBytes.ToString(CultureInfo.InvariantCulture)).Append(" indexed source bytes · ");
        else text.Append("Sizes and counts cover segments this reader cannot read, so they are omitted · ");
        text.Append(elapsed.ToString(CultureInfo.InvariantCulture)).Append(" ms");
        if (alreadyCurrent) text.Append(" (already current)");
        else if (result is not null) text.Append(" (records added ").Append(result.RecordsAdded.ToString(CultureInfo.InvariantCulture)).Append(", removed ").Append(result.RecordsRemoved.ToString(CultureInfo.InvariantCulture))
            .Append("; documents indexed ").Append(result.DocumentsIndexed.ToString(CultureInfo.InvariantCulture)).Append(", removed ").Append(result.DocumentsRemoved.ToString(CultureInfo.InvariantCulture)).Append(')');
        text.Append('\n');
        foreach (SearchSegment segment in segments)
            text.Append("  ").Append(SafeText(segment.Audience)).Append(": ").Append(segment.Passages.ToString(CultureInfo.InvariantCulture)).Append(" passages · records ").Append(segment.Records)
                .Append(" · documents ").Append(segment.Documents).Append('\n');
        foreach (string path in unavailable) text.Append("  unavailable here: ").Append(SafeText(path)).Append(" (its pinned bytes are not in this copy)\n");
        text.Append("The index is machine-local and derived: never committed, exported or served; deleting it loses nothing.\n");
        return text.ToString();
    }

    private static string RenderReplay(IReadOnlyList<SearchReplayOutcome> outcomes)
    {
        var text = new StringBuilder();
        foreach (SearchReplayOutcome outcome in outcomes)
        {
            text.Append("Selection ").Append(outcome.Selection).Append(": ").Append(outcome.Message).Append('\n');
            foreach (string difference in outcome.Differences) text.Append("  ").Append(difference).Append('\n');
        }

        return text.ToString();
    }
}
