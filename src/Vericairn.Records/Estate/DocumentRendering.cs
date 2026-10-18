// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Collections.Immutable;
using System.Globalization;
using Vericairn.Records.Hashing;

namespace Vericairn.Records.Estate;

/// <summary>Versioned derived response data, shared by stored and on-demand renditions. It never asserts acceptance.</summary>
public sealed record DocumentRendition(int Version, DocumentGenerationStatus Status, ImmutableArray<byte> Bytes,
    string? InputDigest, string? ContentHash, string? Reason);

/// <summary>The single pure rendering boundary; selection and authorization happen before it, writes after it.</summary>
public static class DocumentRendering
{
    /// <summary>The rendition response version, independent of the generator, record and descriptor versions.</summary>
    public const int Version = 1;

    /// <summary>Produces canonical bytes from the same authorized snapshot used for either a file or an on-demand view.</summary>
    public static DocumentRendition Render(IDocumentGenerator generator, DocumentGenerationContext context)
    {
        ArgumentNullException.ThrowIfNull(generator);
        ArgumentNullException.ThrowIfNull(context);
        if (context.InterfaceVersion != DocumentGeneration.InterfaceVersion || generator.Descriptor is not { } descriptor
            || descriptor != context.Generator)
            return Failed(DocumentGenerationStatus.Unavailable, "The requested generator interface or output contract is unavailable.");
        try
        {
            descriptor.Validate(generator.Id);
            if (context.Parameters.GetValueOrDefault("format") != descriptor.ContractVersion.ToString(CultureInfo.InvariantCulture))
                return Failed(DocumentGenerationStatus.Unavailable, "The requested output contract version is unavailable.");
            if (context.Parameters.Any(p => p.Key != "format" && (!descriptor.Inputs.TryGetValue(p.Key, out ImmutableArray<string> values) || !values.Contains(p.Value)))
                || descriptor.Inputs.Keys.Any(k => !context.Parameters.ContainsKey(k)))
                return Failed(DocumentGenerationStatus.Error, "The named inputs violate the generator contract.");
            if (DocumentGeneration.ValidateSources(descriptor, context.Sources) is { } error)
                return Failed(DocumentGenerationStatus.Error, error);
            if (!descriptor.Selections.Contains(context.Selection) || context.Parameters.GetValueOrDefault("audience") != descriptor.Audience)
                return Failed(DocumentGenerationStatus.Unavailable, "The requested selection or audience is unavailable.");
            DocumentRenderResult result = generator.Generate(context with { Sources = DocumentGeneration.Order(context.Sources) });
            if (result.Status != DocumentGenerationStatus.Success)
                return Failed(result.Status is DocumentGenerationStatus.Unavailable ? result.Status : DocumentGenerationStatus.Error,
                    result.Status == DocumentGenerationStatus.Unavailable ? "A required generator capability or source is unavailable." : "The generator could not produce a complete rendition.");
            if (result.Content is null) return Failed(DocumentGenerationStatus.Error, "The generator returned no complete rendition.");
            byte[] bytes = DocumentMarkdown.Bytes(result.Content);
            if (bytes.Length > descriptor.MaxMemberBytes) return Failed(DocumentGenerationStatus.Error, "A generated member exceeds its declared size bound.");
            if (EstateChecks.Content("generated.md", bytes).HasErrors) return Failed(DocumentGenerationStatus.Error, "Generated content fails the ordinary repository checks.");
            return new(Version, DocumentGenerationStatus.Success, [.. bytes], context.InputDigest, ContentHash.Sha256(bytes), null);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            // A renderer's exception can contain source data or local details; it is never response content.
            return Failed(DocumentGenerationStatus.Error, "The generator violated its declared contract.");
        }
    }

    private static DocumentRendition Failed(DocumentGenerationStatus status, string reason) => new(Version, status, [], null, null, reason);
}
