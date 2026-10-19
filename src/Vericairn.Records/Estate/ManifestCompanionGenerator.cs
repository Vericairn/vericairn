// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Collections.Immutable;
using System.Globalization;
using System.Text.Json;
using Vericairn.Records.Hashing;
using Vericairn.Records.Model;

namespace Vericairn.Records.Estate;

/// <summary>On-demand manifest and charter explanation over exact selected bytes and verified record revisions.</summary>
internal sealed class ManifestCompanionGenerator : IDocumentGenerator
{
    internal const string Name = "vericairn.estate/manifest";
    private const string Implementation = "vericairn.estate/manifest/1";
    internal static readonly ImmutableArray<DocumentManagedKind> Inventory =
    [new("estate/manifest", "vericairn.estate-manifest/1"), new("estate/manifest#vericairn.artifacts", "vericairn.estate-manifest/1#vericairn.artifacts")];
    internal static IEnumerable<DocumentCoverageDescriptor> Coverage => Inventory.Select(k => new DocumentCoverageDescriptor(1, k.Id,
        "Read the exact runtime pin, storage profile, charter and artifact hashes, retaining original and current record formats.", new(Name, 1), new(Name, 1), k.InputSchema, "Internal", "on-demand"));

    public string Id => Name;
    public DocumentGeneratorDescriptor Descriptor { get; } = new(Name, 1, Implementation, CompanionImplementation.Hash(Implementation),
        new Dictionary<string, ImmutableArray<string>> { ["audience"] = ["Internal"] }.ToImmutableDictionary(StringComparer.Ordinal),
        ["estate", "migration"], ["estate-manifest", "record"], ["item", "list"], "text/markdown",
        "Manifest fields then charter basis and UTC migration history; artifact platforms ordinal", "Internal", "single") { AllowsRepositoryOutput = false };

    public DocumentRenderResult Generate(DocumentGenerationContext context)
    {
        if (!ContentHash.IsValid(context.SnapshotIdentity)) return DocumentRenderResult.Unavailable("The manifest requires an explicit on-demand snapshot.");
        if (context.Region is not null || context.Page < 1) return DocumentRenderResult.Error("The manifest requires an on-demand page.");
        DocumentSourceSnapshot[] files = context.Sources.Where(s => s.Kind == "estate-manifest").ToArray();
        if (files.Length != 1 || files[0].Path != EstateLayout.ManifestPath) return DocumentRenderResult.Unavailable("The exact manifest source is unavailable.");
        var findings = new FindingList();
        EstateManifest? manifest = EstateManifest.Parse(files[0].Bytes.ToArray(), findings);
        if (manifest is null || findings.HasErrors) return DocumentRenderResult.Error("The manifest snapshot is invalid.");
        if (context.Sources.Any(s => s.Kind == "record" && !RecordCompanionText.KnownFields(s))) return DocumentRenderResult.Unavailable("A selected record schema is unavailable.");
        DocumentSourceSnapshot[] charters = context.Sources.Where(s => s.RecordType == "estate").ToArray();
        if (files[0].Identity != manifest.Estate || files[0].Repository != manifest.Estate || charters.Length is < 1 or > 2
            || !charters.Any(s => s.Identity == manifest.Estate)) return DocumentRenderResult.Unavailable("The exact original charter is unavailable.");
        string header = "# Estate manifest and charter\n\nInternal; on demand only. Snapshot: " + context.SnapshotIdentity
            + ".\n\nGenerator: " + DocumentMarkdown.Text(Id) + "; contract 1. Input digest: " + context.InputDigest
            + ".\n\nManifest: " + DocumentMarkdown.Link(EstateLayout.ManifestPath, EstateLayout.ManifestPath) + " at " + files[0].Hash + ".\n\n";
        using JsonDocument document = JsonDocument.Parse(files[0].Bytes.ToArray());
        bool profileRecorded = document.RootElement.TryGetProperty("profile", out _);
        return RecordCompanionGenerator.Pages(header, Blocks(), context.Page);

        IEnumerable<string> Blocks()
        {
            yield return "## Runtime and storage\n\nEstate identity: " + manifest.Estate + ".\n\nStorage profile: " + DocumentMarkdown.Text(manifest.Profile)
                + (profileRecorded ? " (recorded)" : " (manifest format default; no profile field recorded)")
                + ". A local-only record store remains authoritative; this view changes no custody.\n\nManifest record format: " + manifest.RecordFormat.ToString(CultureInfo.InvariantCulture)
                + ". Each charter retains its original envelope and declared format below.\n\nRuntime mode: " + DocumentMarkdown.Text(manifest.Mode)
                + ". Repository: " + DocumentMarkdown.Text(manifest.Repository!) + ".\n\nPinned version: " + DocumentMarkdown.Text(manifest.Version ?? "Not recorded")
                + ". Pinned source commit: " + (manifest.Commit ?? "Not recorded") + ".\n\n";
            yield return "## Runtime artifact pins\n\nHashes identify exact bytes; they do not prove publication, availability or acceptance.\n\n";
            if (manifest.Artifacts is not { Count: > 0 }) yield return "No artifact pins recorded.\n\n";
            else foreach ((string platform, string hash) in manifest.Artifacts.OrderBy(p => p.Key, StringComparer.Ordinal)) yield return "- " + DocumentMarkdown.Text(platform) + ": " + hash + "\n\n";
            foreach (DocumentSourceSnapshot charter in charters.OrderBy(s => s.Identity == manifest.Estate ? 0 : 1))
            {
                yield return "## " + (charter.Identity == manifest.Estate ? "Original charter" : "Selected current charter") + "\n\n"
                    + DocumentMarkdown.Link(charter.Identity, charter.Path!) + " at " + charter.Hash + "; envelope format " + charter.RecordFormat!.Value.ToString(CultureInfo.InvariantCulture) + ".\n\n";
                foreach (string block in RecordCompanionText.Body(charter)) yield return block;
            }
            yield return "## Recorded migrations\n\n";
            DocumentSourceSnapshot[] migrations = context.Sources.Where(s => s.RecordType == "migration").ToArray();
            if (migrations.Length == 0) yield return "None (complete selected history).\n\n";
            foreach (DocumentSourceSnapshot migration in migrations)
            {
                yield return DocumentMarkdown.Link(migration.Identity, migration.Path!) + " at " + migration.Hash + ".\n\n";
                foreach (string block in RecordCompanionText.Body(migration)) yield return block;
            }
        }
    }
}
