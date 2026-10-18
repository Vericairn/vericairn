// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Collections.Immutable;
using System.Text.Json;
using Vericairn.Records.Canonical;
using Vericairn.Records.Hashing;
using Vericairn.Records.Model;

namespace Vericairn.Records.Estate;

/// <summary>The complete proposed adoption: exact prior declaration, old bytes and pins, and freshly generated new members.</summary>
public sealed record DocumentMigrationPreview(DocumentGenerationStatus Status, ImmutableArray<byte> Basis,
    ImmutableArray<byte> Preview, ImmutableArray<DocumentFile> Before, ImmutableArray<DocumentFile> After, ImmutableArray<string> Problems)
{
    /// <summary>Exact historical pins that the migration result must retain in its ordinary evidence list.</summary>
    public ImmutableArray<RecordReference> Pins { get; init; } = [];
    /// <summary>The declaration basis submitted as an existing result output for independent review.</summary>
    public string BasisHash => ContentHash.Sha256(Basis.AsSpan());
    /// <summary>The exact diff and provenance reviewed before application.</summary>
    public string PreviewHash => ContentHash.Sha256(Preview.AsSpan());
}

/// <summary>Explicit migration preparation; it writes nothing and never invents replacement output pins.</summary>
public static class DocumentMigration
{
    /// <summary>Previews the complete change before a result and its operator review authorize the successor.</summary>
    public static DocumentMigrationPreview Prepare(EstateModel model, RecordEnvelope proposed, bool requireAuthoredPin = false, IReadOnlySet<string>? introducedPins = null)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(proposed);
        RecordEnvelope? previous = proposed.Supersedes is null ? null : model.ById.GetValueOrDefault(proposed.Supersedes);
        byte[] basis = GeneratedDocumentContract.MigrationBasis(previous, proposed.Body);
        if (EstateModel.BodyString(proposed, "repository") is not null || (previous is not null && EstateModel.BodyString(previous, "repository") is not null))
            return new(DocumentGenerationStatus.Unavailable, [.. basis], [], [], [], ["Migration requires a complete repository change plan from the owning source adapter."]);
        GeneratedDocumentPlan plan = EstateModel.BodyString(proposed, "kind") == "generated"
            ? GeneratedDocumentVerification.Plan(model, proposed) : Authored(model, proposed, requireAuthoredPin);
        if (plan.Status != DocumentGenerationStatus.Success) return new(plan.Status, [.. basis], [], [], [], plan.Problems);
        try
        {
            if (previous is not null && GeneratedDocumentContract.IsStrict(model, previous))
            {
                GeneratedDocumentPlan owned = GeneratedDocumentVerification.Plan(model, previous);
                if (owned.Status != DocumentGenerationStatus.Success || owned.Actual.Any(a => !owned.Expected.Any(e => e.Path == a.Path)))
                    return new(DocumentGenerationStatus.Error, [.. basis], [], [], [], ["The complete former generated membership must be proved before ownership changes."]);
            }
            DocumentFileInventory prior = previous is null ? new(DocumentGenerationStatus.Success, [], null, null)
                : GeneratedDocumentVerification.ReadInventory(model.Root, previous);
            if (prior.Status != DocumentGenerationStatus.Success)
                return new(prior.Status, [.. basis], [], [], [], [prior.Reason ?? "Former ownership is unavailable."]);
            ImmutableArray<DocumentFile> before = [.. prior.Files.Concat(plan.Actual).DistinctBy(f => f.Path, StringComparer.Ordinal).OrderBy(f => f.Path, StringComparer.Ordinal)];
            RecordReference[] pins = model.OfType("document-revision").Where(p => introducedPins?.Contains(p.Id) != true && EstateModel.BodyString(p, "path") is { } path
                && (GeneratedDocumentContract.Owns(proposed, path) || (previous is not null && GeneratedDocumentContract.Owns(previous, path))))
                .OrderBy(p => p.Id, StringComparer.Ordinal).Select(p => p.Reference).ToArray();
            byte[] preview = CanonicalJson.SerializeUtf8(JsonSerializer.SerializeToElement(new
            {
                format = "vericairn.document-migration-preview/1", basis = ContentHash.Sha256(basis), pins,
                before = before.Select(f => new { path = f.Path, hash = f.Hash }),
                after = plan.Expected.Select(f => new { path = f.Path, hash = f.Hash, inputs = f.InputDigest }), commit = plan.Commit,
            }));
            return new(DocumentGenerationStatus.Success, [.. basis], [.. preview], before, plan.Expected, []) { Pins = [.. pins] };
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            return new(DocumentGenerationStatus.Unavailable, [.. basis], [], [], [], ["The complete former ownership could not be read safely."]);
        }
    }

    /// <summary>Rechecks the exact fresh preview and every retained pin against the independently reviewed result.</summary>
    public static bool IsReviewed(EstateModel model, RecordEnvelope proposed, DocumentMigrationPreview preview) =>
        preview.Status == DocumentGenerationStatus.Success && GeneratedDocumentContract.ReviewedResults(model, proposed).Any(r =>
            r.Body.GetProperty("outputs").EnumerateArray().Count(o => o.GetProperty("name").GetString() == GeneratedDocumentContract.MigrationPreviewOutput
                && o.GetProperty("hash").GetString() == preview.PreviewHash) == 1
            && preview.Pins.All(pin => r.Body.TryGetProperty("evidence", out JsonElement evidence) && evidence.EnumerateArray().Any(e =>
                e.TryGetProperty("hash", out JsonElement hash) && hash.GetString() == pin.Hash
                && e.TryGetProperty("location", out JsonElement location) && location.GetString() == model.ById[pin.Id].ExpectedPath)));

    private static GeneratedDocumentPlan Authored(EstateModel model, RecordEnvelope proposed, bool requirePin)
    {
        try
        {
            DocumentFileInventory inventory = GeneratedDocumentVerification.ReadInventory(model.Root, proposed);
            if (inventory.Status != DocumentGenerationStatus.Success || inventory.Files.Length != 1)
                return new(DocumentGenerationStatus.Error, [], [], null, ["Restored authored ownership requires its exact file and pin."]);
            DocumentChangePlan repair = DocumentRepair.Prepare(model, proposed);
            if (repair.Status != DocumentGenerationStatus.Success) return new(repair.Status, [], [], null, repair.Problems);
            DocumentFile file = inventory.Files[0];
            if (repair.Changes.SingleOrDefault() is { After: { } after }) file = file with { Bytes = after };
            if (requirePin && !model.Current("document-revision").Any(p => EstateModel.BodyString(p, "path") == file.Path
                && EstateModel.BodyString(p, "hash") == file.Hash && p.Body.TryGetProperty("bytes", out JsonElement size) && size.GetInt64() == file.Bytes.Length))
                return new(DocumentGenerationStatus.Error, [], [], null, ["Restored authored ownership requires an exact current document-revision pin."]);
            if (DocumentChecks.Generated(model, proposed, System.Text.Encoding.UTF8.GetString(file.Bytes.AsSpan())).Count != 0)
                return new(DocumentGenerationStatus.Error, [], [], null, ["The restored authored regions must pass their generation checks."]);
            return new(DocumentGenerationStatus.Success, [file], inventory.Files, null, []);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            return new(DocumentGenerationStatus.Unavailable, [], [], null, ["The restored authored file could not be verified."]);
        }
    }
}
