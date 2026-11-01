// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using System.Text.Json.Serialization;

namespace Vericairn.Runtime.Guides;

/// <summary>A reviewed mapping of exact Markdown sources to catalog identities and applicable contracts.</summary>
public sealed record GuideSourceIndex
{
    /// <summary>The supported input contract.</summary>
    public string Format { get; init; } = "vericairn.guide-inputs/1";
    /// <summary>The explicitly selected built-in template version.</summary>
    public string TemplateVersion { get; init; } = GuideGenerator.TemplateVersion;
    /// <summary>Exact files; paths are relative to the caller's catalog or contracts root.</summary>
    public GuideSourcePin[] Sources { get; init; } = [];
    /// <summary>Reviewed, inclusive line spans, including their original line endings.</summary>
    public GuideBlockPin[] Blocks { get; init; } = [];
    /// <summary>Role contracts, shared by every entry of that role.</summary>
    public GuideRoleBinding[] Roles { get; init; } = [];
    /// <summary>Common industry boundaries, including those located under another role's heading.</summary>
    public GuideFamilyBinding[] Families { get; init; } = [];
    /// <summary>Owning table contexts. Every discovered owning table must have one mapping.</summary>
    public GuideTableBinding[] Tables { get; init; } = [];
    /// <summary>Stable identities and explicit readiness for every owning row.</summary>
    public GuideEntryBinding[] Entries { get; init; } = [];
}

/// <summary>An exact source; Area is catalog or contracts. Neither is an ambient estate lookup.</summary>
public sealed record GuideSourcePin(string Id, string Area, string Path, string Sha256);

/// <summary>An exact source span. Hashes include original UTF-8 line endings.</summary>
public sealed record GuideBlockPin(string Id, string Source, int StartLine, int EndLine, string Sha256);

/// <summary>A supported role and its mandatory contract blocks.</summary>
public sealed record GuideRoleBinding(string Kind, string[] Blocks);

/// <summary>A named industry's common reserved acts and hard lines.</summary>
public sealed record GuideFamilyBinding(string Name, string[] Boundaries);

/// <summary>An owning table, identified by source and its full heading trail joined with " / ".</summary>
public sealed record GuideTableBinding
{
    /// <summary>The source id.</summary>
    public required string Source { get; init; }
    /// <summary>The exact heading trail, without Markdown heading markers.</summary>
    public required string Heading { get; init; }
    /// <summary>domain, foundry, adapter, provider or shared-capability.</summary>
    public required string Kind { get; init; }
    /// <summary>A family binding, or null for a shared entry outside industry families.</summary>
    public string? Family { get; init; }
    /// <summary>Kind/group preambles required in addition to the common family blocks.</summary>
    public string[] Boundaries { get; init; } = [];
    /// <summary>Introductions, shared profiles, edition allocation and other contextual blocks.</summary>
    public string[] Context { get; init; } = [];
}

/// <summary>A reviewed identity mapping. Names locate rows; keys survive renames and are never inferred from row numbers.</summary>
public sealed record GuideEntryBinding
{
    /// <summary>A stable, portable key. Existing explicit identifiers must be retained.</summary>
    public required string Key { get; init; }
    /// <summary>The source id.</summary>
    public required string Source { get; init; }
    /// <summary>The table's exact heading trail.</summary>
    public required string Heading { get; init; }
    /// <summary>The row's display name without its outer bold or code delimiters.</summary>
    public required string Name { get; init; }
    /// <summary>planned, remainder, available, retired, reference or withheld.</summary>
    public required string State { get; init; }
    /// <summary>Reviewed status/subset disclosures; mandatory even when the row has no status column.</summary>
    public string[] Status { get; init; } = [];
    /// <summary>Additional shared-profile boundaries that cannot replace inherited boundaries.</summary>
    public string[] Boundaries { get; init; } = [];
    /// <summary>Explicitly referenced implementation specifications and other context.</summary>
    public string[] Context { get; init; } = [];
    /// <summary>Dependencies resolved against stable keys or aliases, never guessed from prose.</summary>
    public string[] References { get; init; } = [];
    /// <summary>Prior names/keys retained after a reviewed rename.</summary>
    public string[] Aliases { get; init; } = [];
    /// <summary>The owning key for a reference-only row; it never creates an extra guide.</summary>
    public string? Owner { get; init; }
    /// <summary>A withheld reason, or the retired guide's replacement and migration explanation.</summary>
    public string? Explanation { get; init; }
}

/// <summary>A deterministic diagnostic tied to an entry, or to the inventory when no entry can be identified.</summary>
public sealed record GuideFinding(string Entry, string Code, string Message);

/// <summary>One parsed owning row, preserving its exact cells and source row hash.</summary>
public sealed record GuideCatalogRow(string Source, string Heading, string Name, string NameColumn,
    IReadOnlyDictionary<string, string> Cells, int Line, string Sha256);

/// <summary>A complete inventory parse, including malformed or ambiguous table findings.</summary>
public sealed record GuideInventory(IReadOnlyList<GuideCatalogRow> Rows, IReadOnlyList<GuideFinding> Findings);

/// <summary>Every owning row's disposition, including selected entries that could not generate.</summary>
public sealed record GuideCoverage(string Key, string Source, string Heading, string Name, string State, string Reason);

/// <summary>A private deterministic bundle. Failed bundles contain findings and manifests, never partial guides.</summary>
public sealed record GuideBundle(IReadOnlyDictionary<string, byte[]> Files, IReadOnlyList<GuideCoverage> Coverage,
    IReadOnlyList<GuideFinding> Findings)
{
    /// <summary>Whether the complete selected set passed all generation checks.</summary>
    public bool Success => Findings.Count == 0;
}

/// <summary>The JSON representation shared by the CLI, index and manifests.</summary>
public static class GuideJson
{
    /// <summary>Strict camel-case JSON; unknown properties are findings rather than ignored options.</summary>
    public static JsonSerializerOptions Options { get; } = CreateOptions();

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
            AllowDuplicateProperties = false,
        };
        options.MakeReadOnly(populateMissingResolver: true);
        return options;
    }
}
