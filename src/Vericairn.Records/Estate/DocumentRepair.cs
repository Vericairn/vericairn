// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Collections.Immutable;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Vericairn.Records.Hashing;
using Vericairn.Records.Model;

namespace Vericairn.Records.Estate;

/// <summary>One bounded output mutation, guarded by the exact bytes observed while preparing it; null after-bytes mean removal.</summary>
public sealed record DocumentOutputChange(string Path, string? BeforeHash, ImmutableArray<byte>? After)
{
    /// <summary>The intended final content hash, or null when the verified old member is removed.</summary>
    public string? AfterHash => After is { } bytes ? ContentHash.Sha256(bytes.AsSpan()) : null;
}

/// <summary>A completely prepared repair or repository batch; failure carries no partial output changes.</summary>
public sealed record DocumentChangePlan(DocumentGenerationStatus Status, ImmutableArray<DocumentOutputChange> Changes,
    ImmutableArray<string> Rewritten, ImmutableArray<string> Problems)
{
    /// <summary>Verified former register directories to remove only after all their files are removed and they are empty.</summary>
    public ImmutableArray<string> RetiredDirectories { get; init; } = [];
}

/// <summary>Prepares repairs without writing and preserves every authored byte outside generated markers.</summary>
public static class DocumentRepair
{
    /// <summary>Selects an estate declaration by identity, destination, register directory or proved member.</summary>
    public static DocumentChangePlan Prepare(EstateModel model, string selection)
    {
        ArgumentNullException.ThrowIfNull(model);
        RecordEnvelope[] declarations = model.Current("living-document").Where(d => EstateModel.BodyString(d, "repository") is null
            && (d.Id == selection || DocumentPatterns.Destination(d) == selection
                || (EstateModel.BodyString(d, "pathPattern") is { } p && DocumentPatterns.IsValid(p)
                    && (DocumentPatterns.Prefix(p).TrimEnd('/') == selection || GeneratedDocumentContract.Owns(d, selection))))).ToArray();
        if (declarations.Length != 1) return Failed(DocumentGenerationStatus.Error, "Select one declared estate document or register.");
        return Prepare(model, declarations[0], selection);
    }

    /// <summary>Prepares a supplied declaration, including an explicitly proposed migration, without publishing that declaration.</summary>
    public static DocumentChangePlan Prepare(EstateModel model, RecordEnvelope declaration) => Prepare(model, declaration, declaration.Id);

    private static DocumentChangePlan Prepare(EstateModel model, RecordEnvelope declaration, string selection)
    {
        try
        {
            if (GeneratedDocumentContract.IsStrict(model, declaration))
            {
                GeneratedDocumentPlan generated = GeneratedDocumentVerification.Plan(model, declaration);
                if (generated.Status != DocumentGenerationStatus.Success) return new(generated.Status, [], [], generated.Problems);
                if (GeneratedDocumentContract.IsRetired(model, declaration))
                    return Failed(DocumentGenerationStatus.Error, "Retired ownership is recovered through its original batch; repair cannot infer former members.");
                if (EstateModel.BodyString(declaration, "pathPattern") is not null && GeneratedDocumentContract.Owns(declaration, selection)
                    && !generated.Expected.Any(f => f.Path == selection)) return Failed(DocumentGenerationStatus.Error, "The selected path is not a proved generated member.");
                if (generated.Actual.Any(f => !generated.Expected.Any(e => e.Path == f.Path)))
                    return Failed(DocumentGenerationStatus.Error, "Unexpected files require an explicit ownership migration or retirement; repair cannot prune them.");
                ImmutableArray<DocumentOutputChange> changes = Changes(generated.Actual, generated.Expected);
                return new(DocumentGenerationStatus.Success, changes, [.. changes.Select(c => c.Path)], []);
            }
            string path = EstateModel.BodyString(declaration, "path")!;
            string file = GeneratedDocumentVerification.SafeFile(model.Root, path);
            byte[] original = File.Exists(file) ? File.ReadAllBytes(file) : [];
            string text = new UTF8Encoding(false, true).GetString(original);
            var rewritten = ImmutableArray.CreateBuilder<string>();
            foreach ((string id, string generator, IReadOnlyList<string> inputs) in DocumentChecks.Regions(declaration))
            {
                DocumentRenderResult result = Region(model, declaration, generator, id, inputs);
                if (result.Status != DocumentGenerationStatus.Success || result.Content is null)
                    return Failed(result.Status, "A required region generator is unavailable or failed.");
                string? next = GeneratedRegions.Replace(text, id, result.Content);
                if (next is null) return Failed(DocumentGenerationStatus.Error, "Generated region markers are missing, repeated, crossing or unclosed.");
                if (next != text) { rewritten.Add(id); text = next; }
            }
            if (EstateModel.BodyString(declaration, "kind") == "generated")
            {
                if (DocumentChecks.Generator(declaration) is not (string id, IReadOnlyList<string> inputs)
                    || DocumentGenerators.Find(id)?.Generate(model, null, inputs) is not { } fresh)
                    return Failed(DocumentGenerationStatus.Unavailable, "The whole-document generator is unavailable.");
                // Legacy output bytes retain their established contract until reviewed migration.
                if (fresh != text) { text = fresh; rewritten.Add("the whole document"); }
            }
            byte[] after = new UTF8Encoding(false, true).GetBytes(text);
            if (EstateChecks.Content(path, after).HasErrors) return Failed(DocumentGenerationStatus.Error, "The repaired document fails ordinary repository content checks.");
            return new(DocumentGenerationStatus.Success, original.AsSpan().SequenceEqual(after) ? [] :
                [new(path, File.Exists(file) ? ContentHash.Sha256(original) : null, [.. after])], rewritten.ToImmutable(), []);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            return Failed(DocumentGenerationStatus.Error, "Repair could not obtain a complete safe rendition; nothing was changed.");
        }
    }

    /// <summary>Adapts retained region grammars and the versioned renderer without silently treating one as the other.</summary>
    public static DocumentRenderResult Region(EstateModel model, RecordEnvelope declaration, string id, string region, IReadOnlyList<string> inputs, string? commit = null)
    {
        try
        {
            IDocumentGenerator? generator = DocumentGenerators.Find(id);
            if (generator is null) return DocumentRenderResult.Unavailable("The region generator is unavailable.");
            if (!inputs.Any(i => i.StartsWith("format=", StringComparison.Ordinal)))
            {
                string? legacy = generator.Generate(model, region, inputs);
                if (legacy is null) return DocumentRenderResult.Unavailable("The legacy region contract is unavailable.");
                return DocumentRenderResult.Success(GeneratedDocumentContract.UsesStrictRegions(model, declaration)
                    ? Encoding.UTF8.GetString(DocumentMarkdown.Bytes(legacy)) : GeneratedRegions.Normalize(legacy));
            }
            if (generator.Descriptor is not { } descriptor) return DocumentRenderResult.Unavailable("The requested region contract is unavailable.");
            var body = new JsonObject
            {
                ["path"] = EstateModel.BodyString(declaration, "path"),
                ["generator"] = new JsonObject { ["id"] = id, ["inputs"] = JsonSerializer.SerializeToNode(inputs) },
            };
            var carrier = new RecordEnvelope(declaration.Type, declaration.Id, declaration.At, declaration.By, declaration.Subject, declaration.Supersedes,
                JsonSerializer.SerializeToElement(body), declaration.DeclaredHash, declaration.ComputedHash, declaration.Signatures, declaration.Format);
            DocumentSourceSelection selected;
            if (EstateModel.BodyString(declaration, "source") == Types.LivingDocumentType.EstateFile)
                selected = DocumentSourceSelection.Records(model, descriptor);
            else
            {
                IDocumentSource? source = DocumentSources.Find(EstateModel.BodyString(declaration, "source"));
                if (source?.SnapshotVersion != DocumentGeneration.InterfaceVersion || (EstateModel.BodyString(declaration, "repository") is not null && commit is null))
                    return DocumentRenderResult.Unavailable("The exact region source snapshot is unavailable.");
                selected = source.ReadSelection(model, declaration, commit ?? string.Empty, descriptor);
                if (commit is not null && selected.Commit != commit) return DocumentRenderResult.Unavailable("The region source did not read the requested exact commit.");
            }
            DocumentMemberSelection members = DocumentGeneration.Members(carrier, selected);
            if (members.Status != DocumentGenerationStatus.Success) return new(members.Status, null, members.Reason);
            (DocumentGenerationStatus graphStatus, string? graphReason) = DocumentSourceGraph.Check(model, declaration, selected.Sources, commit);
            if (graphStatus != DocumentGenerationStatus.Success) return new(graphStatus, null, graphReason);
            DocumentRendition result = DocumentRendering.Render(generator, members.Members.Single().Context with { Region = region });
            return new(result.Status, result.Status == DocumentGenerationStatus.Success ? Encoding.UTF8.GetString(result.Bytes.AsSpan()) : null, result.Reason);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            return DocumentRenderResult.Error("The region generator failed its contract.");
        }
    }

    /// <summary>A complete deterministic before/after change list. Callers establish ownership before allowing removals.</summary>
    public static ImmutableArray<DocumentOutputChange> Changes(ImmutableArray<DocumentFile> before, ImmutableArray<DocumentFile> after)
    {
        var old = before.ToDictionary(f => f.Path, StringComparer.Ordinal);
        var changes = ImmutableArray.CreateBuilder<DocumentOutputChange>();
        foreach (DocumentFile file in after)
        {
            old.Remove(file.Path, out DocumentFile? previous);
            if (previous?.Hash != file.Hash) changes.Add(new(file.Path, previous?.Hash, file.Bytes));
        }
        foreach (DocumentFile removed in old.Values) changes.Add(new(removed.Path, removed.Hash, null));
        return [.. changes.OrderBy(c => c.Path, StringComparer.Ordinal)];
    }

    private static DocumentChangePlan Failed(DocumentGenerationStatus status, string reason) => new(status, [], [], [reason]);
}
