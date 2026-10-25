// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Vericairn.Records.Estate;

/// <summary>Complete paginated record and nested-list readers over an authorized immutable selection.</summary>
internal sealed class RecordCompanionGenerator : IDocumentGenerator
{
    internal const string Name = "vericairn.records/items";
    private const string Implementation = "vericairn.records/readers/3";
    public string Id => Name;
    public DocumentGeneratorDescriptor Descriptor { get; } = new(Name, 1, Implementation,
        CompanionImplementation.Hash(Implementation),
        new Dictionary<string, ImmutableArray<string>>
        {
            ["audience"] = ["Internal"], ["selection"] = ["item", "list"], ["field"] = ["@text"], ["index"] = ["all", "@text"], ["page"] = ["@text"],
        }.ToImmutableDictionary(StringComparer.Ordinal), RecordCompanions.Types, ["record"], ["item", "list"], "text/markdown",
        "Record type, UTC time, ordinal id; fields ordinal within schema sections; array members in recorded order", "Internal", "single") { AllowsRepositoryOutput = false };

    public DocumentRenderResult Generate(DocumentGenerationContext context)
    {
        if (context.Selection != context.Parameters["selection"]) return DocumentRenderResult.Error("The selected reader mode does not match its named input.");
        string field = context.Parameters["field"];
        if (!int.TryParse(context.Parameters["page"], NumberStyles.None, CultureInfo.InvariantCulture, out int page) || page < 1)
            return DocumentRenderResult.Error("A page is a positive integer.");
        if (context.Sources.Any(s => !RecordCompanionText.KnownFields(s))) return DocumentRenderResult.Unavailable("The record schema is not covered by this reader.");
        if (field == "body" && context.Selection == "list")
        {
            if (context.Parameters["index"] != "all") return DocumentRenderResult.Error("A record list has no item index.");
            return Pages("# Recorded revisions\n\nComplete authorized snapshot; historical facts, not current lineage selection. Input digest: " + context.InputDigest + ".\n\n",
                context.Sources.IsEmpty ? ["None (complete selected list).\n\n"] : context.Sources.Select(s =>
                    "- " + DocumentMarkdown.Link(s.RecordType + " " + s.Identity, s.Path!) + ": " + DocumentMarkdown.Text(s.Title!) + "; " + s.At + "; " + s.Hash
                    + ". " + RecordCompanionText.Notice(s.RecordType) + " Inspect the exact revision for full fields and nested lists.\n\n"), page);
        }
        if (context.Sources.Length != 1) return DocumentRenderResult.Error("Item and nested collection readers require one exact parent revision.");
        DocumentSourceSnapshot source = context.Sources[0];
        string header = RecordCompanionText.Header(source, context);
        if (field == "body")
        {
            if (context.Parameters["index"] != "all") return DocumentRenderResult.Error("A record item has no nested index.");
            return Pages(header, RecordCompanionText.Body(source), page);
        }
        string kind = "record/" + source.RecordType + "#" + field;
        if (!DocumentCoverage.RecordKinds.Any(k => k.Id == kind)) return DocumentRenderResult.Unavailable("The selected collection is not registered.");
        var collections = Select(source.Body!.Value, field.Split('.'), 0, "").ToArray();
        bool openTree = RecordCollections.IsOpenTree(source.RecordType!, field);
        if (collections.Any(c => c.Value.ValueKind != (openTree ? JsonValueKind.Object : JsonValueKind.Array))) return DocumentRenderResult.Error("The selected field is not a collection.");
        if (openTree) header += "Recorded contribution fields and nested lists. Their names and values are retained; only the owning evaluator can interpret their effect.\n\n";
        var items = collections.SelectMany(c => openTree
            ? c.Value.EnumerateObject().OrderBy(p => p.Name, StringComparer.Ordinal).Select(p => (Value: p.Value, Path: c.Path + "." + p.Name))
            : c.Value.EnumerateArray().Select((v, i) => (Value: v, Path: c.Path + "[" + (i + 1).ToString(CultureInfo.InvariantCulture) + "]"))).ToArray();
        if (context.Selection == "item")
        {
            if (!int.TryParse(context.Parameters["index"], NumberStyles.None, CultureInfo.InvariantCulture, out int index) || index < 1 || index > items.Length)
                return DocumentRenderResult.Unavailable("The selected collection member is unavailable.");
            return Pages(header, RecordCompanionText.Value(items[index - 1].Value, items[index - 1].Path), page);
        }
        if (context.Parameters["index"] != "all") return DocumentRenderResult.Error("A list must select all members.");
        return Pages(header, collections.Length == 0 ? ["Collection not recorded in this revision.\n\n"]
            : items.Length == 0 ? ["None (complete selected collection).\n\n"]
            : items.SelectMany(i => RecordCompanionText.Value(i.Value, i.Path)), page);
    }

    internal static DocumentRenderResult Pages(string header, IEnumerable<string> blocks, int page)
    {
        const int pageBytes = 262144;
        var pages = new List<string>();
        var body = new StringBuilder();
        int bytes = 0;
        foreach (string block in blocks)
        {
            int size = Encoding.UTF8.GetByteCount(block);
            if (size > pageBytes) return DocumentRenderResult.Error("A reader block exceeds the pagination bound.");
            if (bytes + size > pageBytes) { pages.Add(body.ToString()); body.Clear(); bytes = 0; }
            body.Append(block); bytes += size;
        }
        pages.Add(body.ToString());
        if (page > pages.Count) return DocumentRenderResult.Unavailable("The selected page is outside this exact rendition.");
        return DocumentRenderResult.Success(header + "Page " + page.ToString(CultureInfo.InvariantCulture) + " of " + pages.Count.ToString(CultureInfo.InvariantCulture)
            + (pages.Count > 1 ? ". Partial page; read every page at this same source basis for full detail.\n\n" : ". Complete selected detail.\n\n") + pages[page - 1]);
    }

    internal static IEnumerable<(JsonElement Value, string Path)> Select(JsonElement value, string[] segments, int at, string path)
    {
        if (at == segments.Length) { yield return (value, path); yield break; }
        string segment = segments[at];
        bool array = segment.EndsWith("[]", StringComparison.Ordinal);
        string name = array ? segment[..^2] : segment;
        if (value.ValueKind != JsonValueKind.Object || !value.TryGetProperty(name, out JsonElement child)) yield break;
        string next = path.Length == 0 ? name : path + "." + name;
        if (array)
        {
            if (child.ValueKind != JsonValueKind.Array) yield break;
            int index = 0;
            foreach (JsonElement item in child.EnumerateArray())
                foreach (var selected in Select(item, segments, at + 1, next + "[" + (++index).ToString(CultureInfo.InvariantCulture) + "]")) yield return selected;
        }
        else foreach (var selected in Select(child, segments, at + 1, next)) yield return selected;
    }
}
