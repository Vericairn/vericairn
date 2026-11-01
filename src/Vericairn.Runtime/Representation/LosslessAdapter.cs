// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text;
using System.Text.Json;
using Vericairn.Records.Representation;

namespace Vericairn.Runtime.Representation;

/// <summary>A pure, local implementation of vericairn.representation/1. It has no transport, credentials, files or telemetry.</summary>
public static class LosslessAdapter
{
    /// <summary>Reduce only explicitly eligible blocks whose exact optional originals remain retrievable.</summary>
    public static RepresentationReduction Reduce(IReadOnlyList<RepresentationBlock> blocks, Func<string, byte[]?> original)
    {
        ArgumentNullException.ThrowIfNull(blocks);
        ArgumentNullException.ThrowIfNull(original);
        var seen = new Dictionary<string, int>(StringComparer.Ordinal);
        var output = new List<string>();
        long before = 0, after = 0;
        for (int i = 0; i < blocks.Count; i++)
        {
            RepresentationBlock block = blocks[i];
            string text = block.Text;
            // A label alone grants nothing: required originals and unknown pins never reach this callback's allowlist.
            if (Eligible(block, original))
            {
                before += RepresentationContract.Bytes(text);
                string reduced = Compact(text);
                if (seen.TryGetValue(text, out int first))
                {
                    string reference = "[vericairn.representation/1: repeat of block " + first.ToString(System.Globalization.CultureInfo.InvariantCulture)
                        + "; original: " + block.Original + "]";
                    if (RepresentationContract.Bytes(reference) < RepresentationContract.Bytes(reduced)) reduced = reference;
                }
                else seen.Add(text, i);
                text = reduced;
                after += RepresentationContract.Bytes(text);
            }
            output.Add(text);
        }
        return new(output.AsReadOnly(), before, after);
    }

    internal static RepresentationReduction Bypass(IReadOnlyList<RepresentationBlock> blocks, Func<string, byte[]?> original)
    {
        long count = blocks.Where(b => Eligible(b, original)).Sum(b => (long)RepresentationContract.Bytes(b.Text));
        return new(blocks.Select(b => b.Text).ToArray(), count, count);
    }

    private static bool Eligible(RepresentationBlock block, Func<string, byte[]?> original) => block.Eligible
        && original(block.Original) is byte[] retained && retained.AsSpan().SequenceEqual(Encoding.UTF8.GetBytes(block.Text)) && !MarkedExact(block.Text);

    private static bool MarkedExact(string text)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(text, new JsonDocumentOptions { MaxDepth = 64, AllowDuplicateProperties = false });
            return Protected(document.RootElement);
        }
        catch (JsonException) { return false; }

        static bool Protected(JsonElement value)
        {
            if (value.ValueKind == JsonValueKind.Array) return value.EnumerateArray().Any(Protected);
            if (value.ValueKind != JsonValueKind.Object) return false;
            foreach (JsonProperty property in value.EnumerateObject())
            {
                string name = property.Name.ToUpperInvariant();
                if (name is "EXACT" or "INSTRUCTIONS" or "SYSTEM" or "BRIEF" or "TOOLS" or "FUNCTIONS" or "CREDENTIALS" or "AUTHORIZATION"
                    or "API_KEY" or "APIKEY" or "PASSWORD" or "SECRET" or "SIGNATURE" or "HASH" or "BYTEHASH") return true;
                if (name == "FIDELITY" && property.Value.ValueKind == JsonValueKind.String && property.Value.GetString() == "exact") return true;
                if (name == "MANDATORY" && property.Value.ValueKind == JsonValueKind.True || Protected(property.Value)) return true;
            }
            return false;
        }
    }

    /// <summary>Restore every block from its existing original route; exact blocks are already present, unchanged.</summary>
    public static IReadOnlyList<string> Restore(IReadOnlyList<RepresentationBlock> blocks, Func<string, byte[]?> original)
    {
        ArgumentNullException.ThrowIfNull(blocks);
        ArgumentNullException.ThrowIfNull(original);
        return blocks.Select(b => b.Eligible ? Encoding.UTF8.GetString(original(b.Original)
            ?? throw new InvalidOperationException("The pinned original is unavailable; no replacement revision is permitted.")) : b.Text).ToArray();
    }

    // Remove only JSON whitespace outside strings. Preserve property order, number spellings and escape sequences exactly.
    private static string Compact(string text)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(text, new JsonDocumentOptions { MaxDepth = 64, AllowDuplicateProperties = false });
            if (document.RootElement.ValueKind is not (JsonValueKind.Object or JsonValueKind.Array)) return text;
        }
        catch (JsonException) { return text; }
        var result = new StringBuilder(text.Length);
        bool quoted = false, escaped = false;
        foreach (char c in text)
        {
            if (quoted)
            {
                result.Append(c);
                if (escaped) escaped = false;
                else if (c == '\\') escaped = true;
                else if (c == '"') quoted = false;
            }
            else if (c == '"') { quoted = true; result.Append(c); }
            else if (c is not (' ' or '\t' or '\r' or '\n')) result.Append(c);
        }
        return result.ToString();
    }
}

/// <summary>Only replacement text and eligible UTF-8 counts; no headers, endpoint, protected copy or token estimate.</summary>
/// <param name="Text">Replacement text in the original block order.</param>
/// <param name="BeforeBytes">Verified eligible bytes before reduction.</param>
/// <param name="AfterBytes">Eligible bytes after reduction, including references.</param>
public sealed record RepresentationReduction(IReadOnlyList<string> Text, long BeforeBytes, long AfterBytes);
