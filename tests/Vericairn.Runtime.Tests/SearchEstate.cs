// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Vericairn.Records.Authoring;
using Vericairn.Records.Canonical;
using Vericairn.Records.Estate;
using Vericairn.Records.Hashing;
using Vericairn.Records.Model;
using Vericairn.Runtime.Service;

namespace Vericairn.Runtime.Tests;

/// <summary>
/// A deterministic estate for search: fixed record ids and a fixed clock, so record hashes, pins, scores and selections are the
/// same on every machine and run; the goldens rest on it. Decisions, work orders, a design document with sections and a table,
/// and knowledge drafts in three audiences.
/// </summary>
public sealed class SearchEstate : IDisposable
{
    public const string Estate = "01M6S000000000000000000001";
    public const string Operator = "01M6S000000000000000000002";
    public const string Builder = "01M6S000000000000000000003";

    private int next = 10;
    private DateTimeOffset clock = new(2026, 11, 1, 12, 0, 0, TimeSpan.Zero);

    public SearchEstate(long format = 3, bool content = true)
    {
        Root = Path.Combine(Path.GetTempPath(), "vericairn-search-" + Records.Identity.Ulid.NewUlid().Value);
        Directory.CreateDirectory(Root);
        Format = format;
        Add("principal", """{"kind":"human","name":"Search Operator","roles":["operator"]}""", id: Operator, by: Operator);
        Add("estate", $$$"""{"name":"Search Estate","slug":"search-estate","purpose":"Exercise keyword search.","operator":"{{{Operator}}}","recordFormat":{{{format}}},"policies":{"signatures":"optional","releaseAcceptance":["operator"]}}""", id: Estate, by: Operator);
        Add("principal", """{"kind":"agent","name":"Search Builder","roles":["builder"],"agent":{"vendor":"Example","product":"Example Agent"}}""", id: Builder, by: Operator);
        string manifest = $$$"""{"estate":"{{{Estate}}}","format":"vericairn.estate-manifest/1","recordFormat":{{{format}}},"vericairn":{"commit":"0123456789abcdef0123456789abcdef01234567","mode":"source","repository":"Vericairn/vericairn"}}""";
        string manifestPath = Path.Combine(Root, EstateLayout.ManifestPath);
        Directory.CreateDirectory(Path.GetDirectoryName(manifestPath)!);
        using (JsonDocument document = JsonDocument.Parse(manifest)) File.WriteAllBytes(manifestPath, CanonicalJson.SerializePrettyUtf8(document.RootElement));
        if (!content) return;

        KeepOriginals = Add("decision", Decision("Keep originals", "Every context package retains the original bytes of its sources; a summary never replaces an original.", "retain originals"));
        Freshness = Add("decision", Decision("Index freshness", "The keyword index compares its basis with the current records before it answers, and updates incrementally.", "update the index before answering"));
        Add("work-order", Order("W-1", "Build the keyword index", "Build the machine-local keyword index for agents.", KeepOriginals.Id));
        Add("work-order", Order("W-2", "Prepare release notes", "Write the release notes for the widget.", null));
        Document("design/search-notes.md", "Search notes", """
            # Search notes

            Searching finds candidates; a candidate is data and never authority.

            ## Freshness

            Each segment records its basis hashes, and the runtime compares them with the current snapshot before answering.

            ## The catalog

            | Entry | Holds |
            | --- | --- |
            | `CAT-001` | A heron catalog entry about wading birds. |
            | `CAT-002` | A kingfisher entry about diving birds. |

            ## Règles

            Une règle écrite en français, with café and naïve words.
            """);
        Document("docs/knowledge/guide/internal-guide.md", "Internal guide", """
            ---
            kind: guide
            id: internal-guide
            title: "Selecting context by search"
            summary: >-
              How an agent adds optional, pinned candidates to its package.
            audience: internal
            sources:
              - {ref: "01M6S000000000000000000099", marked: "historical", note: "Provenance never indexed: zanzibar."}
            ---

            # Selecting context by search

            Searches add optional candidates after the explicit selection.
            """);
        Document("docs/knowledge/article/public-note.md", "Public note", """
            ---
            kind: article
            id: public-note
            title: "Lighthouse keeping"
            summary: >-
              A public note about lighthouse lamps.
            audience: public
            ---

            # Lighthouse keeping

            The lighthouse lamp turns every night.
            """);
    }

    public string Root { get; }

    public long Format { get; }

    public RecordEnvelope KeepOriginals { get; } = null!;

    public RecordEnvelope Freshness { get; } = null!;

    public DateTimeOffset Clock => clock;

    public string NextId() => "01M6S" + (next++).ToString("D21", CultureInfo.InvariantCulture);

    public RecordEnvelope Add(string type, string body, RecordReference? subject = null, string? supersedes = null, string? id = null, string by = Operator, DateTimeOffset? at = null)
    {
        clock = clock.AddSeconds(1);
        using JsonDocument document = JsonDocument.Parse(body);
        AuthoredRecord record = RecordAuthor.Create(new RecordDraft(type, by, document.RootElement, subject, supersedes, at ?? clock, id ?? NextId(), Format));
        RecordAuthor.Write(Root, record);
        return record.Envelope;
    }

    public RecordEnvelope Document(string path, string title, string content)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(content.Replace("\r\n", "\n", StringComparison.Ordinal));
        string full = Path.Combine(Root, path.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllBytes(full, bytes);
        return Add("document-revision", $$"""{"path":"{{path}}","title":"{{title}}","hash":"{{ContentHash.Sha256(bytes)}}","bytes":{{bytes.Length}},"mediaType":"text/markdown","revision":1,"summary":"The original."}""");
    }

    public static string Decision(string title, string summary, string effect) =>
        $$"""{"title":"{{title}}","stake":"medium","statement":"{{summary}}","summary":"{{summary}}","synthesis":"{{summary}}","effects":["{{effect}}"],"notAuthorized":[],"affects":[],"decidedBy":"{{Operator}}","decidedAt":"2026-11-01T12:00:00Z"}""";

    public static string Order(string key, string title, string intent, string? input) =>
        $$"""{"key":"{{key}}","kind":"ordinary","title":"{{title}}","intent":"{{intent}}","scope":["the work"],"exitQuestion":"Is {{key}} done?","inputs":[{{(input is null ? "" : $$"""{"type":"decision","id":"{{input}}"}""")}}],"deliverables":["result"]}""";

    public EstateService Open() => EstateService.Open(Root);

    public EstateModel Load() => EstateLoader.Load(Root);

    public void Dispose()
    {
        try
        {
            var walk = new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = FileAttributes.ReparsePoint };
            foreach (string file in Directory.EnumerateFiles(Root, "*", walk))
            {
                FileAttributes attributes = File.GetAttributes(file);
                if ((attributes & FileAttributes.ReadOnly) != 0) File.SetAttributes(file, attributes & ~FileAttributes.ReadOnly);
            }
            Directory.Delete(Root, recursive: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Best effort.
        }
    }
}
