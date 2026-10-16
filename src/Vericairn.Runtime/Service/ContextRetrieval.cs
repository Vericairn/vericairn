// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Buffers;
using System.Text.Json;
using System.Text.Json.Nodes;
using Vericairn.Records.Canonical;
using Vericairn.Records.Hashing;

namespace Vericairn.Runtime.Service;

/// <summary>A shell-safe, self-contained identity, with no retained manifest lookup or authority.</summary>
internal static class ContextRetrieval
{
    private static readonly SearchValues<char> PinCharacters = SearchValues.Create("ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789-_");

    internal static string Pin(JsonObject identity, string? estate)
    {
        var value = new JsonObject { ["estate"] = estate, ["source"] = identity.DeepClone() };
        byte[] bytes = CanonicalJson.SerializeUtf8(JsonSerializer.SerializeToElement(value));
        return "vc1_" + Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    internal static JsonElement Decode(string pin, string estate)
    {
        if (pin is null || pin.Length > 16_384 || !pin.StartsWith("vc1_", StringComparison.Ordinal) ||
            pin.AsSpan(4).ContainsAnyExcept(PinCharacters))
        {
            throw new ArgumentException("Invalid context pin.");
        }

        string encoded = pin[4..].Replace('-', '+').Replace('_', '/');
        using JsonDocument document = JsonDocument.Parse(Convert.FromBase64String(encoded.PadRight((encoded.Length + 3) / 4 * 4, '=')));
        JsonElement root = document.RootElement;
        if (root.GetProperty("estate").GetString() != estate || !ContentHash.IsValid(root.GetProperty("source").GetProperty("byteHash").GetString()))
        {
            throw new ArgumentException("Invalid context identity.");
        }

        return root.GetProperty("source").Clone();
    }

    internal static string Command(string pin) => "vericairn context read " + pin + " --original";
}
