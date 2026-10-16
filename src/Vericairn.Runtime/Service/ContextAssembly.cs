// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Vericairn.Protocol;
using Vericairn.Records.Canonical;
using Vericairn.Records.Estate;
using Vericairn.Records.Hashing;
using Vericairn.Records.Model;

namespace Vericairn.Runtime.Service;

/// <summary>The shared content contract; presentation depth and authoring authority are outside it.</summary>
internal static class ContextAssembly
{
    internal const string Format = "vericairn.context/1";
    internal const string ManifestFormat = "vericairn.context-manifest/1";

    internal sealed record Source(JsonObject Identity, byte[]? Original, bool Mandatory, string Dependency,
        bool Inline = true, int? ExcerptBytes = null, string State = "missing");

    internal static Source Record(RecordEnvelope record, RecordFile file, bool mandatory, string dependency) => new(new JsonObject
    {
        ["kind"] = "record",
        ["id"] = record.Id,
        ["type"] = record.Type,
        ["hash"] = record.ComputedHash,
        ["path"] = record.ExpectedPath,
        ["byteHash"] = ContentHash.Sha256(file.Bytes),
        ["bytes"] = file.Bytes.LongLength,
    }, file.Bytes, mandatory, dependency);

    internal static ContextPackage Build(JsonObject basis, IEnumerable<Source> selected, string presentation)
    {
        List<Source> sources = selected.OrderByDescending(s => s.Mandatory)
            .ThenBy(s => s.Identity["kind"]!.GetValue<string>(), StringComparer.Ordinal)
            .ThenBy(s => s.Identity["path"]?.GetValue<string>() ?? s.Identity["byteHash"]!.GetValue<string>(), StringComparer.Ordinal)
            .ThenBy(s => s.Identity["hash"]?.GetValue<string>(), StringComparer.Ordinal)
            .ThenBy(s => s.Identity["byteHash"]?.GetValue<string>(), StringComparer.Ordinal)
            .ThenBy(s => Encoding.UTF8.GetString(Bytes(s.Identity)), StringComparer.Ordinal)
            .ThenBy(s => s.Dependency, StringComparer.Ordinal).ToList();
        var entries = new JsonArray();
        var originals = new JsonArray();
        var withheld = new JsonArray();
        var excerpts = new JsonArray();
        for (int i = 0; i < sources.Count; i++)
        {
            Source source = sources[i];
            if (source.Mandatory && (source.Original is null || !source.Inline || source.ExcerptBytes is not null))
            {
                throw new InvalidOperationException("Mandatory originals must be available and fully inlined before the dependent step.");
            }

            if (source.Original is byte[] bytes && (source.Identity["byteHash"]?.GetValue<string>() != ContentHash.Sha256(bytes) ||
                source.Identity["bytes"]?.GetValue<long>() != bytes.LongLength))
            {
                throw new InvalidOperationException("An original must match its pinned byte hash and size.");
            }

            if (source.ExcerptBytes is int extent && (extent <= 0 || source.Original is null || extent >= source.Original.Length || source.Identity["kind"]!.GetValue<string>() == "record"))
            {
                throw new InvalidOperationException("Excerpts require a proper byte extent of an available non-record source.");
            }

            string key = "s" + i.ToString("D6", CultureInfo.InvariantCulture);
            JsonObject identity = source.Identity.DeepClone().AsObject();
            identity["key"] = key;
            identity["mandatory"] = source.Mandatory;
            identity["dependency"] = source.Dependency;
            identity["state"] = source.Original is null ? source.State : "retained";
            entries.Add(identity);
            bool full = source.Original is not null && source.Inline && source.ExcerptBytes is null;
            if (full)
            {
                JsonObject original = identity.DeepClone().AsObject();
                original["encoding"] = "base64";
                original["content"] = Convert.ToBase64String(source.Original!);
                originals.Add(original);
            }
            else
            {
                long included = source.ExcerptBytes ?? 0;
                long? size = source.Original?.LongLength ?? source.Identity["bytes"]?.GetValue<long>();
                JsonObject entry = identity.DeepClone().AsObject();
                entry["reason"] = source.Original is null ? "unavailable" : source.ExcerptBytes is null ? "budget" : "excerpt";
                entry["state"] = source.Original is null ? source.State : "withheld-but-retrievable";
                entry["originalBytes"] = size;
                entry["includedBytes"] = included;
                entry["withheldBytes"] = size - included;
                entry["tokens"] = null;
                entry["tokenizer"] = "unknown";
                entry["reduction"] = new JsonObject
                {
                    ["method"] = source.ExcerptBytes is null ? "whole-source selection" : "byte excerpt",
                    ["originalRetained"] = source.Original is not null,
                    ["loss"] = source.Original is null ? "irreversible reduction; original unavailable" : source.ExcerptBytes is not null
                        ? "irreversible reduction of this representation; original retained" : "selection only; original retained",
                    ["cause"] = source.Original is null ? "The pinned original is not available in this copy; no retention disposition is inferred." : "Deterministic package selection; no original was deleted.",
                };
                if (source.ExcerptBytes is int length)
                {
                    var extentNode = new JsonObject { ["unit"] = "bytes", ["start"] = 0, ["length"] = length, ["endExclusive"] = length };
                    entry["extent"] = extentNode.DeepClone();
                    excerpts.Add(new JsonObject
                    {
                        ["source"] = key, ["originalByteHash"] = source.Identity["byteHash"]!.DeepClone(),
                        ["extent"] = extentNode, ["encoding"] = "base64", ["content"] = Convert.ToBase64String(source.Original!, 0, length),
                        ["method"] = "byte excerpt", ["review"] = "derived, not accepted",
                        ["loss"] = "irreversible reduction of this representation; original retained",
                    });
                }

                withheld.Add(entry);
            }
        }

        var manifest = new JsonObject
        {
            ["format"] = ManifestFormat,
            ["basis"] = basis.DeepClone(),
            ["sources"] = entries,
        };
        string manifestHash = ContentHash.Sha256(Bytes(manifest));
        foreach (JsonObject entry in withheld.Cast<JsonObject>())
        {
            string key = entry["key"]!.GetValue<string>();
            entry["retrieve"] = Retrieval(manifestHash, key);
            entry["api"] = "/v1/context/" + manifestHash + "/sources/" + key;
        }

        var derivative = new JsonObject
        {
            ["generator"] = Format,
            ["method"] = "deterministic record views",
            ["synthesis"] = "off",
            ["review"] = "derived, not accepted",
            ["basis"] = entries.DeepClone(),
            ["text"] = presentation,
        };
        string derivativeHash = ContentHash.Sha256(Bytes(derivative));
        var payload = new JsonObject
        {
            ["format"] = Format,
            ["manifest"] = new JsonObject { ["hash"] = manifestHash, ["content"] = manifest },
            ["originals"] = originals,
            ["presentation"] = derivative,
            ["derivatives"] = new JsonArray(new JsonObject { ["hash"] = derivativeHash, ["role"] = "presentation" }),
            ["excerpts"] = excerpts,
            ["withheld"] = withheld,
            ["completeness"] = withheld.Count == 0 ? "Complete for the declared selection; no claim about sources outside it."
                : "Mandatory originals are inlined; inspect every withheld entry before depending on optional material. No claim about sources outside this selection.",
            ["limitations"] = new JsonArray("This derived package grants no authority; check current authority before acting.", "Remote is not built."),
        };
        return Measure(payload);
    }

    internal static ContextPackage Measure(JsonObject payload)
    {
        JsonElement element = JsonSerializer.SerializeToElement(payload, ProtocolJson.Options);
        byte[] bytes = CanonicalJson.SerializePrettyUtf8(element);
        return new ContextPackage(ContentHash.Sha256(bytes), bytes.LongLength, element);
    }

    internal static byte[] Bytes(JsonNode node) => CanonicalJson.SerializePrettyUtf8(JsonSerializer.SerializeToElement(node, ProtocolJson.Options));

    internal static string Serialize(ContextPackage package) => Encoding.UTF8.GetString(CanonicalJson.SerializePrettyUtf8(package.Payload));

    internal static string Retrieval(string manifest, string key) => "vericairn context read " + manifest + " --source " + key + " --original";
}
