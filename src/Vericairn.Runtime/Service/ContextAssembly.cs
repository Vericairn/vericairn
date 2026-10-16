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

    internal sealed record Source(JsonObject Identity, byte[]? Original, bool Mandatory, string Dependency);

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
            .ThenBy(s => s.Identity["hash"]?.GetValue<string>(), StringComparer.Ordinal).ToList();
        var entries = new JsonArray();
        var originals = new JsonArray();
        for (int i = 0; i < sources.Count; i++)
        {
            Source source = sources[i];
            string key = "s" + i.ToString("D6", CultureInfo.InvariantCulture);
            JsonObject identity = source.Identity.DeepClone().AsObject();
            identity["key"] = key;
            identity["mandatory"] = source.Mandatory;
            identity["dependency"] = source.Dependency;
            entries.Add(identity);
            JsonObject original = identity.DeepClone().AsObject();
            original["encoding"] = "base64";
            original["content"] = source.Original is null ? null : Convert.ToBase64String(source.Original);
            originals.Add(original);
        }

        var manifest = new JsonObject
        {
            ["format"] = ManifestFormat,
            ["basis"] = basis.DeepClone(),
            ["sources"] = entries,
        };
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
            ["manifest"] = new JsonObject { ["hash"] = ContentHash.Sha256(Bytes(manifest)), ["content"] = manifest },
            ["originals"] = originals,
            ["presentation"] = derivative,
            ["derivatives"] = new JsonArray(new JsonObject { ["hash"] = derivativeHash, ["role"] = "presentation" }),
            ["withheld"] = new JsonArray(),
            ["completeness"] = "Complete for the declared selection; no claim about sources outside it.",
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
}
