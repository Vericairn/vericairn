// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Collections.Immutable;
using Vericairn.Records.Model;
using Vericairn.Records.Types;

namespace Vericairn.Records.Estate;

/// <summary>A generated member an estate upgrade rewrites: it held exactly what an earlier implementation of its generator wrote for the same sources.</summary>
/// <param name="Path">The member's repository-relative path.</param>
/// <param name="Destination">The declared file or register directory that owns it.</param>
/// <param name="Earlier">The earlier implementation whose exact bytes the member held.</param>
/// <param name="Current">The installed implementation whose bytes replace them.</param>
/// <param name="Change">The change from the observed bytes to the current rendition, guarded by the observed hash.</param>
public sealed record GeneratedMemberUpgrade(string Path, string Destination, string Earlier, string Current, DocumentOutputChange Change);

/// <summary>
/// The generated members an estate upgrade rewrites, prepared without writing. A member whose bytes differ from its current generator's
/// is rewritten only when they equal, byte for byte, what a retained earlier implementation of that generator renders for the same exact
/// sources, its recorded input digest included. Any other difference cannot be attributed to the generator change: the member is left as it
/// is, and the estate check keeps reporting it as document.generated-edited.
/// </summary>
public static class GeneratedDocumentUpgrade
{
    /// <summary>Every member of the estate's strict generated declarations that an earlier implementation wrote, ordered by path.</summary>
    public static ImmutableArray<GeneratedMemberUpgrade> Prepare(EstateModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        var upgrades = ImmutableArray.CreateBuilder<GeneratedMemberUpgrade>();
        foreach (RecordEnvelope declaration in model.Current("living-document").Where(d => EstateModel.BodyString(d, "repository") is null
            && EstateModel.BodyString(d, "source") == LivingDocumentType.EstateFile && GeneratedDocumentContract.IsStrict(model, d)
            && !GeneratedDocumentContract.IsRetired(model, d)))
        {
            if (DocumentChecks.Generator(declaration) is not (string id, _) || DocumentGenerators.Find(id) is not { Descriptor: { } descriptor } generator) continue;
            var earlier = new List<(IDocumentGenerator Generator, DocumentGeneratorDescriptor Descriptor)>();
            foreach (IDocumentGenerator prior in generator.EarlierImplementations)
            {
                if (prior.Id == id && prior.Descriptor is { } priorDescriptor && priorDescriptor.ContractVersion == descriptor.ContractVersion
                    && priorDescriptor.Implementation != descriptor.Implementation) earlier.Add((prior, priorDescriptor));
            }

            if (earlier.Count == 0) continue;
            GeneratedDocumentPlan plan = GeneratedDocumentVerification.Plan(model, declaration);
            if (plan.Status != DocumentGenerationStatus.Success || plan.Current) continue;
            Dictionary<string, DocumentFile> actual = plan.Actual.ToDictionary(f => f.Path, StringComparer.Ordinal);
            DocumentFile[] differing = [.. plan.Expected.Where(e => actual.TryGetValue(e.Path, out DocumentFile? present) && !present.Bytes.AsSpan().SequenceEqual(e.Bytes.AsSpan()))];
            if (differing.Length == 0) continue;
            DocumentMemberSelection members = DocumentGeneration.Members(declaration, DocumentSourceSelection.Records(model, descriptor));
            if (members.Status != DocumentGenerationStatus.Success) continue;
            Dictionary<string, DocumentGenerationContext> contexts = members.Members.ToDictionary(m => m.Path, m => m.Context, StringComparer.Ordinal);
            string destination = GeneratedDocumentContract.RetirementPath(declaration)!;
            foreach (DocumentFile expected in differing)
            {
                if (!contexts.TryGetValue(expected.Path, out DocumentGenerationContext? context)) continue;
                DocumentFile present = actual[expected.Path];
                foreach ((IDocumentGenerator prior, DocumentGeneratorDescriptor priorDescriptor) in earlier)
                {
                    DocumentRendition rendition = DocumentRendering.Render(prior, context with { Generator = priorDescriptor });
                    if (rendition.Status != DocumentGenerationStatus.Success || !rendition.Bytes.AsSpan().SequenceEqual(present.Bytes.AsSpan())) continue;
                    upgrades.Add(new(expected.Path, destination, priorDescriptor.Implementation, descriptor.Implementation, new(expected.Path, present.Hash, expected.Bytes)));
                    break;
                }
            }
        }

        return [.. upgrades.OrderBy(u => u.Path, StringComparer.Ordinal)];
    }
}
