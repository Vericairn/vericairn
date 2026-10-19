// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Collections.Immutable;
using System.Text.Json;
using Vericairn.Records.Canonical;
using Vericairn.Records.Hashing;
using Vericairn.Records.Model;

namespace Vericairn.Records.Estate;

/// <summary>Pure read models for authorized Internal snapshots; no declaration, filesystem access or transport operation.</summary>
public static class DocumentOnDemand
{
    /// <summary>Identifies the exact authorized record set from which the owning model derives lineage selection.</summary>
    public static string RecordSnapshot(EstateModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        return Digest(model.Records.OrderBy(r => r.Type, StringComparer.Ordinal).ThenBy(r => r.Id, StringComparer.Ordinal)
            .Select(r => new { r.Type, r.Id, Hash = r.ComputedHash }));
    }

    /// <summary>Renders a complete history or current lineage collection at the supplied, verified snapshot identity.</summary>
    public static DocumentRendition Records(EstateModel model, string generatorId, string snapshot, string selection, int page = 1)
    {
        ArgumentNullException.ThrowIfNull(model);
        if (selection is not ("all-revisions" or "current") || page < 1) return Failed(DocumentGenerationStatus.Error, "The collection selection or page is invalid.");
        if (DocumentGenerators.Find(generatorId) is not { Descriptor.RequiredPattern: not null } generator)
            return Failed(DocumentGenerationStatus.Unavailable, "The revision collection generator is unavailable.");
        if (snapshot != RecordSnapshot(model)) return Failed(DocumentGenerationStatus.Unavailable, "The requested record snapshot is unavailable.");
        DocumentSourceSelection sources = DocumentSourceSelection.Records(model, generator.Descriptor!);
        if (sources.Status != DocumentGenerationStatus.Success) return Failed(sources.Status, "The complete record selection is unavailable.");
        if (selection == "current") sources = sources with { Sources = [.. sources.Sources.Where(s => model.IsCurrent(model.ById[s.Identity]))] };
        var parameters = new Dictionary<string, string> { ["format"] = "1", ["audience"] = "Internal", ["selection"] = selection }.ToImmutableDictionary(StringComparer.Ordinal);
        return DocumentRendering.Render(generator, new(1, generator.Descriptor!, parameters, "list", null, sources.Sources) { SnapshotIdentity = snapshot, Page = page });
    }

    /// <summary>Identifies an exact manifest together with its verified record basis, without an enclosing output commit.</summary>
    public static string ManifestSnapshot(EstateModel model, byte[] manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        return Digest(new { records = RecordSnapshot(model), manifest = ContentHash.Sha256(manifest) });
    }

    /// <summary>Renders the supplied manifest, original and selected current charter, and exact migration history on demand.</summary>
    public static DocumentRendition Manifest(EstateModel model, byte[] manifest, string snapshot, int page = 1)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(manifest);
        if (page < 1) return Failed(DocumentGenerationStatus.Error, "The page is invalid.");
        if (model.Findings.HasErrors || model.Manifest is null || model.CurrentEstate is null || snapshot != ManifestSnapshot(model, manifest))
            return Failed(DocumentGenerationStatus.Unavailable, "The exact manifest and record basis is unavailable.");
        var findings = new FindingList();
        EstateManifest? parsed = EstateManifest.Parse(manifest, findings);
        if (parsed is null || findings.HasErrors) return Failed(DocumentGenerationStatus.Error, "The selected manifest is invalid or unsupported.");
        if (Digest(parsed) != Digest(model.Manifest) || !model.ById.TryGetValue(parsed.Estate, out RecordEnvelope? original))
            return Failed(DocumentGenerationStatus.Unavailable, "The manifest does not match the verified estate basis.");
        var sources = model.OfType("migration").Append(original).Append(model.CurrentEstate).DistinctBy(r => r.Id, StringComparer.Ordinal).Select(DocumentSourceSnapshot.Record).ToList();
        sources.Add(DocumentSourceSnapshot.Content("estate-manifest", parsed.Estate, parsed.Estate, EstateLayout.ManifestPath, ContentHash.Sha256(manifest), manifest));
        IDocumentGenerator generator = DocumentGenerators.Find(ManifestCompanionGenerator.Name)!;
        var parameters = new Dictionary<string, string> { ["format"] = "1", ["audience"] = "Internal" }.ToImmutableDictionary(StringComparer.Ordinal);
        return DocumentRendering.Render(generator, new(1, generator.Descriptor!, parameters, "item", null, [.. sources]) { SnapshotIdentity = snapshot, Page = page });
    }

    private static string Digest<T>(T value) => ContentHash.Sha256(CanonicalJson.SerializeUtf8(JsonSerializer.SerializeToElement(value)));
    private static DocumentRendition Failed(DocumentGenerationStatus status, string reason) => new(DocumentRendering.Version, status, [], null, null, reason);
}
