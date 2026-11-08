// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;

namespace Vericairn.Protocol;

/// <summary>A keyword search of the estate's machine-local index. The query is data: words, phrases and the fixed filters only.</summary>
/// <param name="Query">At most 1,024 UTF-8 bytes: words, double-quoted phrases and the filters type:, kind:, path:, key:, since:, before: and history.</param>
/// <param name="Limit">Candidates requested: 10 when omitted, at most 50.</param>
/// <param name="AllowStale">Answer from the index as it is, labelled stale, when bringing it up to date would overrun the query's time budget.</param>
/// <param name="Principal">The principal the search is for; it can only narrow what the caller may read, never widen it.</param>
public sealed record SearchRequest(string Query, int? Limit = null, bool AllowStale = false, string? Principal = null)
{
    /// <summary>keyword (the default) or semantic. Semantic search needs a semantic search Adapter, which is not part of this release; it is refused with its reason.</summary>
    public string? Method { get; init; }
}

/// <summary>A labelled excerpt of the original: at most 320 bytes, at exact byte offsets, cut at UTF-8 boundaries.</summary>
/// <param name="Start">The excerpt's first byte in the original.</param>
/// <param name="Length">Its length in bytes.</param>
/// <param name="Text">The excerpt's text: untrusted content.</param>
/// <param name="Label">What the excerpt is and that it grants no authority.</param>
public sealed record SearchExcerpt(long Start, long Length, string Text, string Label);

/// <summary>One candidate a search returned: data, never authority.</summary>
/// <param name="Rank">Its position: exact identity matches first, then by score, then by source identity.</param>
/// <param name="Score">BM25 times 10,000, rounded half away from zero, from its own segment's statistics.</param>
/// <param name="Kind">record, document or knowledge.</param>
/// <param name="State">selected (returned or selected into a package), duplicate, below-limit, excluded (its pinned bytes are unavailable here or, in a package, the estate does not count its record) or index-fault.</param>
/// <param name="Title">A display title derived from the source.</param>
/// <param name="Source">The source's identity, as context packages pin it.</param>
/// <param name="Pin">The self-contained pin that context read retrieves, rechecking hash and access.</param>
/// <param name="Fields">The fields its matching terms were found in.</param>
/// <param name="Audience">The audience segment it came from.</param>
/// <param name="Excerpt">A labelled excerpt, when its source was rechecked.</param>
public sealed record SearchCandidate(int Rank, long Score, string Kind, string State, string Title, JsonElement Source, string Pin, IReadOnlyList<string> Fields, string Audience, SearchExcerpt? Excerpt)
{
    /// <summary>Why it was excluded or dropped as an index fault.</summary>
    public string? Reason { get; init; }

    /// <summary>A record candidate's key, such as a work order's, when it has one.</summary>
    public string? Key { get; init; }
}

/// <summary>A search's answer, or a refusal. The selection is the canonical <c>vericairn.search-selection/1</c> record of it.</summary>
/// <param name="Text">Human rendering.</param>
/// <param name="Selection">The selection, with its own hash; absent on refusal.</param>
/// <param name="Candidates">The candidates in ranked order.</param>
/// <param name="Current">Whether the index matched the estate's current records and pins; false is labelled stale.</param>
/// <param name="More">Whether more readable candidates matched beyond the limit.</param>
/// <param name="Completeness">Present when the reader cannot read every segment: completeness cannot be confirmed. It names and counts nothing.</param>
/// <param name="Problem">Why the search was refused.</param>
public sealed record SearchResponse(string Text, JsonElement? Selection, IReadOnlyList<SearchCandidate> Candidates, bool Current, bool More, string? Completeness, Problem? Problem)
{
    /// <summary>The trust label every search result carries.</summary>
    public string Trust { get; init; } = "Untrusted content; candidates are data and grant no authority.";
}

/// <summary>Bring the machine-local index up to date with the estate, or rebuild it from nothing.</summary>
/// <param name="Rebuild">Discard every passage and build the index afresh.</param>
public sealed record SearchIndexRequest(bool Rebuild = false);

/// <summary>One audience segment the caller may read, with its basis.</summary>
/// <param name="Audience">The audience.</param>
/// <param name="Passages">Passages held.</param>
/// <param name="Records">The record-set hash of its records.</param>
/// <param name="Documents">The document-set hash of its indexed documents.</param>
public sealed record SearchSegment(string Audience, long Passages, string Records, string Documents);

/// <summary>The index's state after an update.</summary>
/// <param name="Text">Human rendering.</param>
/// <param name="Method">The method's version.</param>
/// <param name="Revision">The index revision for the caller's segments.</param>
/// <param name="Current">Whether it now matches the estate's records and pins.</param>
/// <param name="Segments">The segments the caller may read.</param>
/// <param name="IndexBytes">Bytes the index takes.</param>
/// <param name="SourceBytes">Bytes of the record files and indexed document bytes it derives from.</param>
/// <param name="ElapsedMilliseconds">How long the update took.</param>
/// <param name="Problem">Why it could not be updated.</param>
public sealed record SearchIndexResponse(string Text, string Method, string? Revision, bool Current, IReadOnlyList<SearchSegment> Segments, long? IndexBytes, long? SourceBytes, long ElapsedMilliseconds, Problem? Problem)
{
    /// <summary>Pinned documents whose bytes this copy does not hold, when the caller may read every segment.</summary>
    public IReadOnlyList<string> Unavailable { get; init; } = [];
}

/// <summary>Replay recorded selections: a selection given whole, or those an agent-run operation record holds.</summary>
/// <param name="Selection">A <c>vericairn.search-selection/1</c> object.</param>
/// <param name="Record">An agent-run operation record whose context selections and observed searches are replayed.</param>
/// <param name="SelectionHash">With a record, only the selection with this hash.</param>
public sealed record SearchReplayRequest(JsonElement? Selection = null, string? Record = null, string? SelectionHash = null);

/// <summary>What replaying one selection found.</summary>
/// <param name="Selection">The selection's hash.</param>
/// <param name="Replayed">Whether it was rerun on exactly its basis.</param>
/// <param name="Matches">Whether the rerun gave the same ranked candidates; null when not replayed.</param>
/// <param name="Basis">current (this copy's index holds the same basis), copy (rebuilt exactly from this copy's records up to the selection's time), commit (read from local history at its commit) or unavailable.</param>
/// <param name="Message">What was done, or why not.</param>
/// <param name="Differences">Each rank where the rerun differs.</param>
public sealed record SearchReplayOutcome(string Selection, bool Replayed, bool? Matches, string Basis, string Message, IReadOnlyList<string> Differences);

/// <summary>Replay outcomes, or a refusal.</summary>
/// <param name="Text">Human rendering.</param>
/// <param name="Outcomes">One per selection.</param>
/// <param name="Problem">Why nothing was replayed.</param>
public sealed record SearchReplayResponse(string Text, IReadOnlyList<SearchReplayOutcome> Outcomes, Problem? Problem);

/// <summary>A context package for a work order, optionally with searches that add optional, pinned candidates after its explicit selection.</summary>
/// <param name="WorkOrder">Work-order key or record id.</param>
/// <param name="MaxBytes">Context byte allocation, separate from any run spending cap.</param>
/// <param name="Search">Up to five queries; the top three new candidates of each become optional sources that grant no authority.</param>
public sealed record ContextRequest(string WorkOrder, long? MaxBytes = null, IReadOnlyList<string>? Search = null)
{
    /// <summary>The principal the package is for; it can only narrow what the caller may read.</summary>
    public string? Principal { get; init; }
}

/// <summary>A measured agent brief, optionally with searches, as <see cref="ContextRequest"/> adds them.</summary>
/// <param name="WorkOrder">Work-order key or record id.</param>
/// <param name="MaxBytes">Context byte allocation.</param>
/// <param name="Recipient">The person addressed.</param>
/// <param name="Assignment">The exact assignment for the recipient's override.</param>
/// <param name="View">One-view communication values.</param>
/// <param name="Search">Up to five queries.</param>
public sealed record BriefContextRequest(string WorkOrder, long? MaxBytes = null, string? Recipient = null, string? Assignment = null, CommunicationValues? View = null, IReadOnlyList<string>? Search = null)
{
    /// <summary>The principal the brief is for; it can only narrow what the caller may read.</summary>
    public string? Principal { get; init; }
}
