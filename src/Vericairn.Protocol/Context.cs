// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Vericairn.Protocol;

/// <summary>A derived context artifact, never an assignment, authority or an editable record.</summary>
/// <param name="Hash">SHA-256 of the exact canonical pretty UTF-8 payload, distinct from record content hashes.</param>
/// <param name="Bytes">The serialized payload's byte count, including its manifest, originals, derivatives and accounting.</param>
/// <param name="Payload">The versioned context contract. The hash and byte count are an excluded sidecar.</param>
public sealed record ContextPackage(string Hash, long Bytes, JsonElement Payload);

/// <summary>A context package or a refusal. Human text is a separately identified rendering, not a model-fit claim.</summary>
/// <param name="Text">Human rendering.</param>
/// <param name="Package">The package, absent on refusal.</param>
/// <param name="Problem">Why assembly could not complete.</param>
public sealed record ContextResponse(string Text, ContextPackage? Package, Problem? Problem);

/// <summary>The exact original read using its self-contained pinned identity, with permission checked again.</summary>
/// <param name="Identity">Pinned source identity; absent when completeness cannot be confirmed.</param>
/// <param name="Content">Original bytes (base64 in JSON), never the latest revision or a derivative.</param>
/// <param name="Problem">Why bytes cannot be returned.</param>
public sealed record ContextReadResponse(JsonElement? Identity, byte[]? Content, Problem? Problem);

/// <summary>Identity and exact byte measurement of a complete human rendering, including its accounting line.</summary>
/// <param name="Hash">SHA-256 of the UTF-8 text, independent of the serialized package identity.</param>
/// <param name="Bytes">Actual UTF-8 bytes of the final text.</param>
/// <param name="Method">Counting method and scope.</param>
public sealed record ContextTextMeasurement(string Hash, long Bytes, string Method)
{
    /// <summary>Measure a client presentation, including its new accounting line, without changing its protected original.</summary>
    public static (string Text, ContextTextMeasurement Measurement) Presentation(string body)
    {
        long count = 0;
        while (true)
        {
            string text = "Presentation measurement: " + count.ToString(CultureInfo.InvariantCulture) +
                " UTF-8 bytes for this complete rendering including this line; tokens unknown; limit not set for this rendering; model fit not claimed.\n\n" + body;
            byte[] bytes = Encoding.UTF8.GetBytes(text);
            if (count == bytes.LongLength)
            {
                return (text, new ContextTextMeasurement("sha256:" + Convert.ToHexStringLower(SHA256.HashData(bytes)), count,
                    "exact UTF-8 bytes of the complete presentation, including its accounting line; identity is an excluded sidecar"));
            }

            count = bytes.LongLength;
        }
    }
}
