// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Collections.Immutable;
using Vericairn.Records.Model;

namespace Vericairn.Records.Estate;

/// <summary>Prepares the generated side of proposed records before a repository writer publishes any of them.</summary>
public static class DocumentRecordChanges
{
    /// <summary>Renders complete affected inventories, verifies fresh migration reviews and refuses unowned overwrites or pruning.</summary>
    public static DocumentChangePlan Prepare(EstateModel current, EstateModel proposed, IReadOnlyList<RecordEnvelope> added)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(proposed);
        ArgumentNullException.ThrowIfNull(added);
        var changes = ImmutableArray.CreateBuilder<DocumentOutputChange>();
        var directories = ImmutableArray.CreateBuilder<string>();
        try
        {
            foreach (RecordEnvelope declaration in proposed.Current("living-document").Where(d => EstateModel.BodyString(d, "repository") is null))
            {
                bool declarationAdded = added.Any(r => r.Id == declaration.Id);
                if (declarationAdded && GeneratedDocumentContract.RequiresMigration(proposed, declaration))
                {
                    DocumentMigrationPreview preview = DocumentMigration.Prepare(proposed, declaration, requireAuthoredPin: true,
                        introducedPins: added.Where(r => r.Type == "document-revision").Select(r => r.Id).ToHashSet(StringComparer.Ordinal));
                    if (!DocumentMigration.IsReviewed(proposed, declaration, preview))
                        return Failed(DocumentGenerationStatus.Error, "The complete fresh ownership preview and retained pins must match the independently reviewed migration result.");
                    changes.AddRange(DocumentRepair.Changes(preview.Before, preview.After));
                    if (declaration.Supersedes is { } prior && proposed.ById.TryGetValue(prior, out RecordEnvelope? former)
                        && EstateModel.BodyString(former, "pathPattern") is not null
                        && GeneratedDocumentContract.RetirementPath(former) is { } directory
                        && !preview.After.Any(f => f.Path.StartsWith(directory + "/", StringComparison.OrdinalIgnoreCase))) directories.Add(directory);
                    continue;
                }
                if (!GeneratedDocumentContract.IsStrict(proposed, declaration)) continue;
                bool retirement = added.Any(r => r.Type == "retirement" && EstateModel.BodyString(r, "path") == GeneratedDocumentContract.RetirementPath(declaration)
                    && EstateModel.BodyString(r, "retiredAt") is not null);
                bool affected = declarationAdded || retirement || (DocumentChecks.Generator(declaration) is (string id, _)
                    && DocumentGenerators.Find(id)?.Descriptor is { } descriptor && added.Any(r => descriptor.RecordTypes.Contains(r.Type)));
                if (!affected) continue;
                if (proposed.Manifest?.Profile == "local-only")
                    return Failed(DocumentGenerationStatus.Unavailable, "Committed estate companions require repository custody; local-only records use on-demand renditions.");
                RecordEnvelope? old = current.Current("living-document").FirstOrDefault(d => proposed.Chain(declaration).Contains(d.Id));
                if (retirement && old is not null)
                {
                    GeneratedDocumentPlan former = GeneratedDocumentVerification.Plan(current, old);
                    if (!former.Current) return Failed(former.Status == DocumentGenerationStatus.Success ? DocumentGenerationStatus.Error : former.Status,
                        "Retirement requires a complete current generated inventory; unavailable or unexpected files cannot be pruned.");
                    if (!GeneratedDocumentContract.IsRetired(proposed, declaration)) return Failed(DocumentGenerationStatus.Error, "Generated retirement is not authorized.");
                    changes.AddRange(DocumentRepair.Changes(former.Actual, []));
                    if (EstateModel.BodyString(declaration, "pathPattern") is not null) directories.Add(GeneratedDocumentContract.RetirementPath(declaration)!);
                    continue;
                }
                if (GeneratedDocumentContract.IsRetired(proposed, declaration)) continue;
                GeneratedDocumentPlan next = GeneratedDocumentVerification.Plan(proposed, declaration);
                if (next.Status != DocumentGenerationStatus.Success) return new(next.Status, [], [], next.Problems);
                if (next.Actual.Any(f => !next.Expected.Any(e => e.Path == f.Path)))
                    return Failed(DocumentGenerationStatus.Error, "Unexpected files require explicit ownership migration; a record write cannot prune them.");
                if (old is null)
                {
                    if (next.Actual.Any(f => next.Expected.Single(e => e.Path == f.Path).Hash != f.Hash))
                        return Failed(DocumentGenerationStatus.Error, "A new declaration cannot overwrite existing unowned content.");
                }
                else
                {
                    GeneratedDocumentPlan before = GeneratedDocumentVerification.Plan(current, old);
                    if (!before.Current) return Failed(before.Status == DocumentGenerationStatus.Success ? DocumentGenerationStatus.Error : before.Status,
                        "Repair the existing generated members before changing their sources.");
                }
                changes.AddRange(DocumentRepair.Changes(next.Actual, next.Expected));
            }
            if (changes.Select(c => c.Path).Distinct(StringComparer.OrdinalIgnoreCase).Count() != changes.Count)
                return Failed(DocumentGenerationStatus.Error, "Prepared generated changes collide on a portable filesystem.");
            return new(DocumentGenerationStatus.Success, [.. changes.OrderBy(c => c.Path, StringComparer.Ordinal)], [.. changes.Select(c => c.Path)], [])
                { RetiredDirectories = [.. directories.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)] };
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            return Failed(DocumentGenerationStatus.Error, "The complete generated change could not be prepared; no record or output was written.");
        }
    }

    private static DocumentChangePlan Failed(DocumentGenerationStatus status, string reason) => new(status, [], [], [reason]);
}
