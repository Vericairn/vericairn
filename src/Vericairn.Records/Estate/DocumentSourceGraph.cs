// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Collections.Immutable;
using Vericairn.Records.Model;
using Vericairn.Records.Types;

namespace Vericairn.Records.Estate;

/// <summary>Checks declared file dependencies, including generated regions, before invoking any renderer.</summary>
public static class DocumentSourceGraph
{
    /// <summary>Success has no reason; unavailable and error expose no source content or partial rendition.</summary>
    public static (DocumentGenerationStatus Status, string? Reason) Check(EstateModel model, RecordEnvelope declaration,
        ImmutableArray<DocumentSourceSnapshot> sources, string? commit)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(declaration);
        var visiting = new HashSet<string>(StringComparer.Ordinal) { declaration.Id };
        var complete = new HashSet<string>(StringComparer.Ordinal);
        try { return Visit(sources); }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        { return (DocumentGenerationStatus.Unavailable, "The complete source dependency graph is unavailable."); }

        (DocumentGenerationStatus Status, string? Reason) Visit(ImmutableArray<DocumentSourceSnapshot> inputs)
        {
            foreach (DocumentSourceSnapshot input in inputs.Where(s => s.Kind != "record" && s.Path is not null))
            {
                foreach (RecordEnvelope owner in model.Current("living-document").Where(d => Repository(model, d) == input.Repository
                    && GeneratedDocumentContract.Owns(d, input.Path!)))
                {
                    string[] generators = [.. DocumentChecks.Regions(owner).Select(r => r.Generator), .. DocumentChecks.Generator(owner) is (string whole, _) ? new[] { whole } : []];
                    if (generators.Length == 0) continue;
                    if (visiting.Contains(owner.Id)) return (DocumentGenerationStatus.Error, "The declared source graph contains a generation cycle.");
                    if (complete.Contains(owner.Id)) continue;
                    visiting.Add(owner.Id);
                    foreach (string id in generators)
                    {
                        if (DocumentGenerators.Find(id)?.Descriptor is not { } descriptor)
                            return (DocumentGenerationStatus.Unavailable, "A generated source dependency has no versioned source contract.");
                        DocumentSourceSelection selected;
                        if (EstateModel.BodyString(owner, "source") == LivingDocumentType.EstateFile)
                            selected = DocumentSourceSelection.Records(model, descriptor, commit);
                        else
                        {
                            IDocumentSource? adapter = DocumentSources.Find(EstateModel.BodyString(owner, "source"));
                            if (adapter?.SnapshotVersion != DocumentGeneration.InterfaceVersion || (EstateModel.BodyString(owner, "repository") is not null && commit is null))
                                return (DocumentGenerationStatus.Unavailable, "A generated source dependency has no exact source snapshot.");
                            selected = adapter.ReadSelection(model, owner, commit ?? string.Empty, descriptor);
                            if (commit is not null && selected.Commit != commit)
                                return (DocumentGenerationStatus.Unavailable, "A source dependency did not read the requested exact commit.");
                        }
                        if (selected.Status != DocumentGenerationStatus.Success || DocumentGeneration.ValidateSources(descriptor, selected.Sources) is not null)
                            return (DocumentGenerationStatus.Unavailable, "The complete source dependency selection is unavailable.");
                        (DocumentGenerationStatus status, string? reason) = Visit(selected.Sources);
                        if (status != DocumentGenerationStatus.Success) return (status, reason);
                    }
                    visiting.Remove(owner.Id);
                    complete.Add(owner.Id);
                }
            }
            return (DocumentGenerationStatus.Success, null);
        }
    }

    /// <summary>The repository identity for matching source paths; estate-owned files retain the estate's identity.</summary>
    public static string? Repository(EstateModel model, RecordEnvelope declaration) => EstateModel.BodyString(declaration, "repository") ?? model.Manifest?.Estate;
}
