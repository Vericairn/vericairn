// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text;
using System.Text.Json;

namespace Vericairn.Records.Representation;

/// <summary>The foundation's explicit fidelity boundary. Unclassified material is always exact.</summary>
public static class RepresentationContract
{
    /// <summary>The version of the representation contract, independent of its adapters.</summary>
    public const string Format = "vericairn.representation/1";

    /// <summary>The one shipped implementation identity; selection still requires machine admission.</summary>
    public const string LocalLossless = "local-lossless";

    /// <summary>Read an explicitly labelled tool-result envelope. Unknown fields or fidelity refuse the entire envelope.</summary>
    public static IReadOnlyList<RepresentationBlock>? Read(JsonElement envelope)
    {
        if (!Fields(envelope, "format", "blocks") || envelope.GetProperty("format").ValueKind != JsonValueKind.String
            || envelope.GetProperty("format").GetString() != Format || envelope.GetProperty("blocks").ValueKind != JsonValueKind.Array)
            return null;
        var blocks = new List<RepresentationBlock>();
        foreach (JsonElement block in envelope.GetProperty("blocks").EnumerateArray())
        {
            if (!Fields(block, "fidelity", "text", "original") || block.GetProperty("text").ValueKind != JsonValueKind.String
                || block.GetProperty("fidelity").ValueKind != JsonValueKind.String || block.GetProperty("original").ValueKind != JsonValueKind.String)
                return null;
            string? fidelity = block.GetProperty("fidelity").GetString();
            if (fidelity is not ("exact" or "eligible")) return null;
            blocks.Add(new(block.GetProperty("text").GetString()!, fidelity == "eligible", block.GetProperty("original").GetString()!));
            if (blocks.Count > 1024) return null;
        }
        return blocks.AsReadOnly();
    }

    /// <summary>An envelope for a producer that knows the fidelity and retained original of each block.</summary>
    public static string Write(IEnumerable<RepresentationBlock> blocks)
    {
        ArgumentNullException.ThrowIfNull(blocks);
        return JsonSerializer.Serialize(new { format = Format, blocks = blocks.Select(b => new { fidelity = b.Eligible ? "eligible" : "exact", text = b.Text, original = b.Original }) });
    }

    /// <summary>UTF-8 is the only measured unit; no token estimate is implied.</summary>
    public static int Bytes(string text) => Encoding.UTF8.GetByteCount(text);

    private static bool Fields(JsonElement value, params string[] fields) => value.ValueKind == JsonValueKind.Object
        && value.EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal).SequenceEqual(fields.Order(StringComparer.Ordinal));
}

/// <summary>A producer's explicit fidelity declaration; eligibility also requires verification against a retained optional original.</summary>
/// <param name="Text">The original text, never an authentication header.</param>
/// <param name="Eligible">False for instructions, briefs, code, quoted originals, hashes, signatures or anything uncertain.</param>
/// <param name="Original">The existing context-retrieval pin. It grants no access.</param>
public sealed record RepresentationBlock(string Text, bool Eligible, string Original);
