// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Collections.Immutable;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Vericairn.Records.Canonical;
using Vericairn.Records.Estate;
using Vericairn.Records.Model;

namespace Vericairn.Runtime.Reading;

/// <summary>The shared local metadata renderer accepts only an already projected, canonical snapshot.</summary>
internal sealed class LocalCompanionGenerator : IDocumentGenerator
{
    private const string Implementation = "vericairn.runtime/local-readers/2";
    public string Id => "vericairn.local/inspectors";
    public DocumentGeneratorDescriptor Descriptor { get; } = new("vericairn.local/inspectors", 1, Implementation, CompanionImplementation.Hash(Implementation),
        new Dictionary<string, ImmutableArray<string>> { ["audience"] = ["Internal"], ["selection"] = ["item", "list"], ["field"] = ["@text"], ["index"] = ["all", "@text"], ["page"] = ["@text"] }.ToImmutableDictionary(StringComparer.Ordinal),
        [], ["local-inspection"], ["item", "list"], "text/markdown", "Canonical redacted metadata order; nested observations retain source order", "Internal", "single") { AllowsRepositoryOutput = false };

    public DocumentRenderResult Generate(DocumentGenerationContext context)
    {
        if (context.Selection != context.Parameters["selection"]) return DocumentRenderResult.Error("The selected inspector mode does not match its named input.");
        if (context.Region is not null || context.Sources.Length != 1 || !int.TryParse(context.Parameters["page"], NumberStyles.None, CultureInfo.InvariantCulture, out int page) || page < 1)
            return DocumentRenderResult.Error("The local inspector request is invalid.");
        DocumentSourceSnapshot source = context.Sources[0];
        using JsonDocument document = CanonicalJson.Parse(source.Bytes.ToArray());
        JsonElement root = document.RootElement;
        string? kind = root.GetProperty("kind").GetString();
        string? asOf = root.GetProperty("asOf").GetString();
        if (kind is null || kind != source.Identity || !LocalCompanionSchemas.All.TryGetValue(kind, out ImmutableArray<LocalCompanionSchemas.Field> fields) || !RecordParser.IsTimestamp(asOf))
            return DocumentRenderResult.Unavailable("The selected local metadata schema is unavailable.");
        JsonElement[] items = root.GetProperty("items").EnumerateArray().ToArray();
        JsonObject[] projected = items.Select(item => LocalCompanionSchemas.Project(item, fields)).OrderBy(v => LocalCompanions.Canonical(v), StringComparer.Ordinal).ToArray();
        var expected = new JsonObject { ["kind"] = kind, ["asOf"] = asOf, ["items"] = new JsonArray(projected.Select(p => (JsonNode)p).ToArray()) };
        if (!source.Bytes.AsSpan().SequenceEqual(CanonicalJson.SerializeUtf8(JsonSerializer.SerializeToElement(expected))))
            return DocumentRenderResult.Error("Only canonical allowlisted metadata can cross this rendering boundary.");
        string field = context.Parameters["field"];
        if (field != "body" && !LocalCompanionSchemas.Collections(fields).Contains(field, StringComparer.Ordinal))
            return DocumentRenderResult.Unavailable("The selected local metadata collection is unavailable.");
        bool unknown = false;
        if (field != "body")
        {
            var collections = items.SelectMany(item => RecordCompanionGenerator.Select(item, field.Split('.'), 0, "")).ToArray();
            unknown = collections.Length == 0 || collections.Any(c => c.Value.ValueKind != JsonValueKind.Array);
            items = collections.Where(c => c.Value.ValueKind == JsonValueKind.Array).SelectMany(c => c.Value.EnumerateArray()).ToArray();
        }
        if (context.Selection == "item")
        {
            if (!int.TryParse(context.Parameters["index"], NumberStyles.None, CultureInfo.InvariantCulture, out int index) || index < 1 || index > items.Length)
                return DocumentRenderResult.Unavailable("The selected local metadata item is unavailable.");
            items = [items[index - 1]];
        }
        else if (context.Parameters["index"] != "all") return DocumentRenderResult.Error("A local list must select all items.");
        string header = "# Local " + RecordCompanionText.Label(kind) + " metadata\n\nInternal; on demand only. Observed snapshot: " + asOf
            + ". No live liveness or eligibility is inferred.\n\nGenerator: " + DocumentMarkdown.Text(Id) + "; contract 1. Metadata input digest: " + context.InputDigest
            + ".\n\nOnly selected metadata is shown. Unavailable, missing or withheld values remain unknown. Byte counts are bytes; token counts are reported tokens, never an estimate from bytes.\n\n"
            + "Machine paths, keys, recovery secrets, pairing codes, bearer values, replay entries, raw logs and context bodies are excluded. This metadata rendition may be shared without machine bindings.\n\n"
            + "Device admission is separate from possession of a key; a discovered endpoint grants no admission. Certificate dates alone establish no trust. Run observations are separate from contributor claims and recorded acceptance.\n\n";
        header += "Selection: " + context.Selection + "; field: " + DocumentMarkdown.Text(field) + "; index: " + context.Parameters["index"] + ".\n\n";
        if (unknown) header += "Some selected collection metadata is unknown or withheld; this is not a complete collection claim.\n\n";
        return RecordCompanionGenerator.Pages(header, items.Length == 0 ? [unknown ? "Collection unknown or withheld.\n\n" : "None (complete selected metadata list).\n\n"]
            : items.SelectMany((item, index) => RecordCompanionText.Value(item, "Item " + (index + 1).ToString(CultureInfo.InvariantCulture))), page);
    }
}
