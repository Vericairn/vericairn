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
    public static DocumentMigrationPreview Prepare(EstateModel model, RecordEnvelope proposed)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(proposed);
        RecordEnvelope? previous = proposed.Supersedes is null ? null : model.ById.GetValueOrDefault(proposed.Supersedes);
        byte[] basis = GeneratedDocumentContract.MigrationBasis(previous, proposed.Body);
        if (EstateModel.BodyString(proposed, "repository") is not null || (previous is not null && EstateModel.BodyString(previous, "repository") is not null))
            return new(DocumentGenerationStatus.Unavailable, [.. basis], [], [], [], ["Migration requires a complete repository change plan from the owning source adapter."]);
        GeneratedDocumentPlan plan = GeneratedDocumentVerification.Plan(model, proposed);
        if (plan.Status != DocumentGenerationStatus.Success) return new(plan.Status, [.. basis], [], [], [], plan.Problems);
        try
        {
            DocumentFileInventory prior = previous is null ? new(DocumentGenerationStatus.Success, [], null, null)
                : GeneratedDocumentVerification.ReadInventory(model.Root, previous);
            if (prior.Status != DocumentGenerationStatus.Success)
                return new(prior.Status, [.. basis], [], [], [], [prior.Reason ?? "Former ownership is unavailable."]);
            ImmutableArray<DocumentFile> before = [.. prior.Files.Concat(plan.Actual).DistinctBy(f => f.Path, StringComparer.Ordinal).OrderBy(f => f.Path, StringComparer.Ordinal)];
            RecordReference[] pins = model.OfType("document-revision").Where(p => EstateModel.BodyString(p, "path") is { } path
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
}
