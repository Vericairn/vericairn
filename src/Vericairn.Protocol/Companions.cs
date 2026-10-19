// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

namespace Vericairn.Protocol;

/// <summary>An explicit reader selection. Audience is a requested projection, never a grant of access.</summary>
public sealed record CompanionRequest(string Generator, string Selection, string? Revision = null, string? Snapshot = null,
    int Page = 1, string Field = "body", string Index = "all", string Audience = "Internal", int Contract = 1,
    string? Declaration = null, string? Commit = null);

/// <summary>A compare-only request for a declared document or register. It writes neither output nor records.</summary>
public sealed record CompanionCompareRequest(string Selection, string? Commit = null, string Audience = "Internal");

/// <summary>Installed reader coverage and its placement; a destination does not mean an estate adopted it.</summary>
public sealed record CompanionCoverageRow(string Kind, string ReaderTask, string? Item, string? List,
    string InputSchema, string Audience, string Destination, string? NoReaderReason, string? ReaderOperation = null);

/// <summary>An installed output contract and its supported selections, without local machine configuration.</summary>
public sealed record CompanionGeneratorRow(string Id, int Contract, string Implementation, string ImplementationHash,
    IReadOnlyList<string> Selections, IReadOnlyDictionary<string, IReadOnlyList<string>> Inputs,
    string Audience, string? Pattern, int MaximumBytes, bool LocalOnly);

/// <summary>Current declared placement, independently of shipped coverage.</summary>
public sealed record CompanionDestination(string Declaration, string Hash, string Generator, string Path, string? Repository);

/// <summary>Coverage metadata with an exact authorized snapshot for subsequent collection requests.</summary>
public sealed record CompanionsResponse(int Version, string Status, string? Snapshot, IReadOnlyList<CompanionCoverageRow> Coverage,
    IReadOnlyList<CompanionGeneratorRow> Generators, IReadOnlyList<CompanionDestination> Destinations, string? Reason, string? ManifestSnapshot = null);

/// <summary>An exact permitted source. Current describes record lineage at the returned snapshot, never an acceptance.</summary>
public sealed record CompanionSource(string Kind, string Identity, string Hash, string? Path, string? Repository,
    string? RecordType, string? At, bool? Current, long? Bytes);

/// <summary>A generated view or inspected source basis, with explicit scope and freshness. No response is a record.</summary>
public sealed record CompanionResponse(int Version, string Status, string Currentness, string Completeness,
    string? Audience, string? Snapshot, string? Commit, CompanionGeneratorRow? Generator,
    IReadOnlyList<CompanionSource> Sources, string? InputDigest, string? ContentHash, string? Text, string? Reason)
{
    /// <summary>A safe failure with no selected source metadata.</summary>
    public static CompanionResponse Unavailable(string reason) => new(1, "unavailable", "unavailable", "unavailable", null, null, null, null, [], null, null, null, reason);
}

/// <summary>One freshly observed output difference, with no output bytes or write authority.</summary>
public sealed record CompanionDifference(string Path, string Change, string? ActualHash, string? ExpectedHash);

/// <summary>Fresh byte and membership comparison, distinct from authored review and clearance.</summary>
public sealed record CompanionComparison(int Version, string Status, string Currentness, string? Snapshot, string? Commit,
    IReadOnlyList<CompanionDifference> Differences, IReadOnlyList<string> Problems);
