// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

namespace Vericairn.Runtime.Guides;

/// <summary>An explicit, reviewed public selection. This input stays private and grants no publication authority.</summary>
public sealed record CatalogExportPlan
{
    /// <summary>The supported preparation contract.</summary>
    public string Format { get; init; } = "vericairn.catalog-export/1";
    /// <summary>Exact bytes of the source index, serialized using GuideJson, including its final LF.</summary>
    public required string IndexIdentity { get; init; }
    /// <summary>Exact files admitted for reading; must equal the index's complete source set.</summary>
    public GuideSourcePin[] Allowlist { get; init; } = [];
    /// <summary>Cleared stable entry keys. Withheld entries and aliases cannot be selected.</summary>
    public string[] Entries { get; init; } = [];
    /// <summary>Reviewed retained-page information for each retired or fully available selected entry.</summary>
    public CatalogRetirement[] Retirements { get; init; } = [];
    /// <summary>Additional private strings that must never occur in public bytes; never exported or echoed.</summary>
    public string[] RestrictedText { get; init; } = [];
}

/// <summary>Public disposition metadata, without private decision identities. Unknown migration compatibility stays explicit.</summary>
public sealed record CatalogRetirement(string Key, string Date, string Revision, string Replacement, string Migration);

/// <summary>An observed public issue number in the proposed catalog repository. No account information belongs here.</summary>
public sealed record CatalogTrackingIssue(string Key, long Number);

/// <summary>A public inventory item and its stable repository-relative pointer.</summary>
public sealed record CatalogEntry(string Key, string Name, string Kind, string? Family, string State, string Guide,
    bool Requestable, string? Replacement);

/// <summary>The public inventory used by the form, request validator and reconciliation.</summary>
public sealed record CatalogInventory(string Format, CatalogEntry[] Entries);

/// <summary>A complete proposed repository. Failure emits no files and no partial public inventory.</summary>
public sealed record CatalogBundle(IReadOnlyDictionary<string, byte[]> Files, CatalogInventory Inventory,
    IReadOnlyList<GuideFinding> Findings)
{
    /// <summary>Whether local preparation succeeded; this never means authorized or published.</summary>
    public bool Success => Findings.Count == 0;
}

/// <summary>A relative destination change; contains neither destination paths nor destination content.</summary>
public sealed record CatalogChange(string Path, string Action);

/// <summary>A read-only publication comparison. A clean comparison grants no publication authority.</summary>
public sealed record CatalogComparison(IReadOnlyList<CatalogChange> Changes, IReadOnlyList<GuideFinding> Findings);
