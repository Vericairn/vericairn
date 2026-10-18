// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Collections.Immutable;
using System.Text;

namespace Vericairn.Records.Estate;

/// <summary>First-party immutable revision files. Adoption belongs to an estate, never to registration.</summary>
internal sealed class RevisionCompanionGenerator : IDocumentGenerator
{
    internal static readonly ImmutableArray<(string Id, string Type, string Directory)> Registers =
    [
        ("vericairn.records/decisions", "decision", "decisions"),
        ("vericairn.records/work-orders", "work-order", "work"),
        ("vericairn.records/releases", "release", "releases"),
        ("vericairn.estate/policies", "estate-policy", "policies"),
        ("vericairn.estate/policy-exceptions", "policy-exception", "policy-exceptions"),
        ("vericairn.estate/writing-exceptions", "writing-exception", "writing-exceptions"),
    ];

    internal RevisionCompanionGenerator(string id)
    {
        var register = Registers.Single(r => r.Id == id);
        Id = id;
        Descriptor = new(id, 1, "vericairn.records/companions/1", CompanionImplementation.Hash,
            new Dictionary<string, ImmutableArray<string>> { ["audience"] = ["Internal"], ["selection"] = ["all-revisions"] }.ToImmutableDictionary(StringComparer.Ordinal),
            [register.Type], ["record"], ["item", "all-revisions"], "text/markdown", "Source UTC time then ordinal revision identity; nested arrays retain recorded order", "Internal", "record-revision")
        {
            RequiredPattern = "docs/registers/" + register.Directory + "/{month}/{date}-{slug}-{id}.md",
            PartialOverviewFields = ["title", "type", "id", "hash", "at", "by", "format", "subject", "supersedes"],
        };
    }

    public string Id { get; }
    public DocumentGeneratorDescriptor Descriptor { get; }

    public DocumentRenderResult Generate(DocumentGenerationContext context)
    {
        if (context.Region is not null || context.Selection != "item") return DocumentRenderResult.Error("A revision file renders exactly one member; collections require an on-demand selection.");
        DocumentSourceSnapshot[] primary = context.Sources.Where(s => !context.Sources.Any(other => other.Supersedes == s.Identity)).ToArray();
        if (primary.Length != 1) return DocumentRenderResult.Error("A revision item requires one exact source and only its predecessor.");
        DocumentSourceSnapshot source = primary[0];
        if (!RecordCompanionText.KnownFields(source)) return DocumentRenderResult.Unavailable("The source schema is unavailable.");
        if (context.Sources.Length != (source.Supersedes is null ? 1 : 2)) return DocumentRenderResult.Unavailable("The exact predecessor basis is unavailable.");
        string header = RecordCompanionText.Header(source, context, "../../../../");
        if (source.Supersedes is { } previous)
        {
            DocumentSourceSnapshot? predecessor = context.Sources.SingleOrDefault(s => s.Identity == previous);
            if (predecessor is null) return DocumentRenderResult.Unavailable("The exact predecessor revision is unavailable.");
            header += "Exact predecessor: [" + previous + "](../../../../" + predecessor.Path + ") at " + predecessor.Hash + ".\n\n";
        }
        var content = new StringBuilder(header);
        int bytes = Encoding.UTF8.GetByteCount(header);
        foreach (string block in RecordCompanionText.Body(source))
        {
            bytes += Encoding.UTF8.GetByteCount(block);
            if (bytes > Descriptor.MaxMemberBytes)
                return DocumentRenderResult.Success(header + "## Partial overview\n\nThe full recorded fields exceed this file's 1 MiB bound. This is an explicitly partial overview, not the full statement or definition. Only the exact title, identity, hash, author, time, format, subject and predecessor metadata are shown.\n\nRead the exact source linked above or every page of the registered item reader at this revision for full detail. No field has been silently shortened and no current verdict is inferred.\n");
            content.Append(block);
        }
        return DocumentRenderResult.Success(content.ToString());
    }
}
