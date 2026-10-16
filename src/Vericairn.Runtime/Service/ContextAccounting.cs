// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using System.Text.Json.Nodes;
using Vericairn.Protocol;
using Vericairn.Records.Canonical;
using Vericairn.Records.Hashing;

namespace Vericairn.Runtime.Service;

/// <summary>Measures the final representation, including the accounting it carries, without a provider or tokenizer call.</summary>
internal static class ContextAccounting
{
    internal static ContextPackage Measure(JsonObject payload, long? maxBytes)
    {
        var accounting = new JsonObject
        {
            ["method"] = "exact canonical pretty UTF-8 serialization, including this accounting",
            ["units"] = "bytes",
            ["bytes"] = 0L,
            ["maxBytes"] = maxBytes,
            ["capSource"] = maxBytes is null ? "limit not set" : "explicit context allocation",
            ["allocationKind"] = "context input allocation; separate from the run spending cap",
            ["remainingBytes"] = maxBytes,
            ["overageBytes"] = 0L,
            ["tokens"] = null,
            ["tokenizer"] = "unknown; no qualified local tokenizer is available",
            ["model"] = null,
            ["modelFit"] = "not claimed",
            ["dispatch"] = "not qualified by a standalone byte measurement; the launcher must establish the model input bound and overhead",
            ["asOf"] = payload["manifest"]?["content"]?["basis"]?["asOf"]?.DeepClone(),
            ["scope"] = "the final serialized payload: instructions, originals, derivatives, wrappers, context manifest, withheld list and accounting",
            ["exclusions"] = new JsonArray("The hash/bytes sidecar and transport envelope are not part of this payload.", "Human renderings have their own byte measurement; they are not this serialization."),
            ["unknowns"] = new JsonArray("model-specific tokens", "existing conversation and mandatory harness/tool framing", "output and safety reserves", "provider hidden framing and billed usage"),
        };
        payload["accounting"] = accounting;
        var seen = new HashSet<long>();
        for (int iteration = 0; iteration < 64; iteration++)
        {
            ContextPackage measured = ContextAssembly.Measure(payload);
            if (accounting["bytes"]!.GetValue<long>() == measured.Bytes)
            {
                return measured;
            }

            // Near decimal boundaries the headroom's digit count can oscillate. A visible, counted space
            // breaks the cycle; no payload bytes are excluded and the result remains deterministic.
            if (!seen.Add(measured.Bytes))
            {
                accounting["serializationPadding"] = (accounting["serializationPadding"]?.GetValue<string>() ?? string.Empty) + " ";
                seen.Clear();
            }

            accounting["bytes"] = measured.Bytes;
            accounting["remainingBytes"] = maxBytes is long cap ? Math.Max(0, cap - measured.Bytes) : null;
            accounting["overageBytes"] = maxBytes is long limit ? Math.Max(0, measured.Bytes - limit) : 0;
        }

        throw new InvalidOperationException("Context accounting did not reach an exact serialized size.");
    }

    internal static bool Verify(ContextPackage package)
    {
        byte[] bytes = CanonicalJson.SerializePrettyUtf8(package.Payload);
        return package.Hash == ContentHash.Sha256(bytes) && package.Bytes == bytes.LongLength &&
            package.Payload.TryGetProperty("accounting", out JsonElement accounting) &&
            accounting.TryGetProperty("bytes", out JsonElement reported) && reported.TryGetInt64(out long size) && size == bytes.LongLength;
    }
}
