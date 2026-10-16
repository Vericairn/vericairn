// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Globalization;
using System.Text;
using System.Text.Json;
using Vericairn.Protocol;
using Vericairn.Records.Canonical;
using Vericairn.Records.Hashing;

namespace Vericairn.Runtime.Service;

/// <summary>Adds the complete context content to a caller's lead without selecting its communication depth.</summary>
internal static class ContextRendering
{
    internal static (string Text, ContextTextMeasurement Measurement) Render(string lead, ContextPackage package)
    {
        JsonElement payload = package.Payload;
        var text = new StringBuilder(lead);
        text.Append("\n\n## Context package\n\n").Append(package.Hash).Append(" · ").Append(package.Bytes.ToString(CultureInfo.InvariantCulture))
            .Append(" serialized payload bytes · ").Append(payload.GetProperty("accounting").GetProperty("maxBytes").ValueKind == JsonValueKind.Null ? "Limit not set" : "explicit context allocation")
            .Append(" · tokens unknown; model fit not claimed\n")
            .Append("This is derived context, not a second assignment or authority. Remote is not built.\n")
            .Append(payload.GetProperty("completeness").GetString()).Append('\n');
        Section(text, "Context manifest (distinct from the estate release manifest)", payload.GetProperty("manifest"));
        Section(text, "Withheld originals", payload.GetProperty("withheld"));
        Section(text, "Derivative identities (synthesis off; not accepted)", payload.GetProperty("derivatives"));
        if (payload.GetProperty("excerpts").GetArrayLength() > 0)
        {
            Section(text, "Excerpts (exact byte extents; originals withheld)", payload.GetProperty("excerpts"));
        }
        text.Append("\nThe lead is a deterministic view: irreversible reduction of this representation; original retained. Required originals follow in full.\n");
        text.Append("\n## Originals (complete)\n");
        foreach (JsonElement source in payload.GetProperty("originals").EnumerateArray())
        {
            text.Append('\n').Append(source.GetProperty("kind").GetString()).Append(' ')
                .Append(source.TryGetProperty("id", out JsonElement id) ? id.GetString() : source.GetProperty("path").GetString())
                .Append(" at ").Append(source.GetProperty("byteHash").GetString()).Append(" (byte hash)\n")
                .Append(ContextAssembly.Retrieval(payload.GetProperty("manifest").GetProperty("hash").GetString()!, source.GetProperty("key").GetString()!)).Append('\n');
            byte[] original = Convert.FromBase64String(source.GetProperty("content").GetString()!);
            string content = System.Text.Unicode.Utf8.IsValid(original) ? Encoding.UTF8.GetString(original) : Convert.ToBase64String(original);
            string fence = "```";
            while (content.Contains(fence, StringComparison.Ordinal))
            {
                fence += "`";
            }

            text.Append(fence).Append(System.Text.Unicode.Utf8.IsValid(original) ? "text\n" : "base64\n")
                .Append(content).Append(content.EndsWith('\n') ? string.Empty : "\n").Append(fence).Append('\n');
        }

        Section(text, "Accounting for the serialized context payload", payload.GetProperty("accounting"));
        return MeasureText(text.ToString());
    }

    internal static (string Text, ContextTextMeasurement Measurement) MeasureText(string body)
    {
        long count = 0;
        while (true)
        {
            string text = body + "\nHuman rendering measurement: " + count.ToString(CultureInfo.InvariantCulture) +
                " UTF-8 bytes including this line; tokens unknown; limit not set for this rendering; model fit not claimed.\n";
            byte[] bytes = Encoding.UTF8.GetBytes(text);
            if (count == bytes.LongLength)
            {
                return (text, new ContextTextMeasurement(ContentHash.Sha256(bytes), count, "exact UTF-8 bytes of the complete human rendering, including its accounting line; identity is an excluded sidecar"));
            }

            count = bytes.LongLength;
        }
    }

    private static void Section(StringBuilder text, string title, JsonElement value) =>
        text.Append("\n## ").Append(title).Append("\n\n```json\n").Append(CanonicalJson.SerializePretty(value)).Append("```\n");
}
