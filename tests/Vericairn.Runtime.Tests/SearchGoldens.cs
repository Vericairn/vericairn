// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Vericairn.Protocol;
using Vericairn.Records.Canonical;
using Vericairn.Runtime.Search;
using Vericairn.Runtime.Service;

namespace Vericairn.Runtime.Tests;

/// <summary>
/// The search goldens: the analyzer's output over a fixed multilingual corpus, and a selection and a context manifest's
/// searches. Runtime.Tests runs them with ICU loaded and Cli.Tests in the product's invariant-globalization mode; both compare
/// against the same bytes, so the analyzer, ranks and scores are identical with and without ICU.
/// </summary>
public static class SearchGoldens
{
    // Each entry exercises one way text can differ by platform: compatibility forms, case folding beyond ASCII, combining marks,
    // scripts without case, supplementary code points, and characters assigned only in recent Unicode versions.
    private static readonly string[] Corpus =
    [
        "ﬁle Ｆｕｌｌ Café x²",
        "CONTEXT-02 design/search.md 0.9.2 sha256:ABC1 Running ÉCOLE ﬁle end.",
        "Straße STRASSE ẞ",
        "İstanbul ısık",
        "ΟΔΥΣΣΕΥΣ όδυσσευς",
        "한국어 한",
        "日本語のテキスト ｶﾀｶﾅ",
        "العَرَبِيَّة",
        "हिन्दी क़",
        "① ㎡ Ⅳ ½ ™",
        "\U00010400\U00010428 \U0001D400\U0001D7CE",
        "Ᲊᲊ ꟋꟜ",
        "zero​width soft­hyphen non break join‍me",
        "Å Ω Å Å",
        "mañana mañana",
        "\U0001F600 \U0001F44D\U0001F3FD emoji",
        "path/to/RÉSUMÉ.md KEY-ÉTÉ v1.2.3+build",
        "Ǆǅǆ ǰ ͅͺ",
        "ꭰꭱ ᎠᎡ",
        "ക്‍ഷ กิน",
    ];

    /// <summary>The analyzer's normalized text, parts and whole identifiers for the corpus match their golden bytes.</summary>
    public static void Analyzer()
    {
        var entries = new JsonArray();
        foreach (string text in Corpus)
        {
            SearchAnalyzer.Analysis analysis = SearchAnalyzer.Analyze(text);
            entries.Add(new JsonObject
            {
                ["normalized"] = SearchAnalyzer.Normalize(text),
                ["parts"] = new JsonArray(analysis.Parts.Select(p => (JsonNode)p).ToArray()),
                ["text"] = text,
                ["whole"] = new JsonArray(analysis.Whole.Select(w => (JsonNode)w).ToArray()),
            });
        }

        var golden = new JsonObject { ["analyzer"] = SearchAnalyzer.Identity, ["entries"] = entries };
        Golden("analyzer-1.json", CanonicalJson.SerializePrettyUtf8(JsonSerializer.SerializeToElement(golden)));
        SearchAnalyzer.Analysis known = SearchAnalyzer.Analyze("CONTEXT-02 design/search.md 0.9.2 sha256:ABC1 Running ÉCOLE ﬁle end.");
        Assert.Equal(["context", "02", "design", "search", "md", "0", "9", "2", "sha256", "abc1", "running", "école", "file", "end"], known.Parts);
        Assert.Equal(["context-02", "design/search.md", "0.9.2", "sha256:abc1"], known.Whole);
        Assert.Equal("file full café x2", SearchAnalyzer.Normalize("ﬁle Ｆｕｌｌ Café x²"));
        Assert.Equal("\"a\"\"b\"", SearchAnalyzer.Quote("a\"b"));
    }

    /// <summary>A selection and a context manifest's searches over the deterministic estate match their golden bytes.</summary>
    public static void SelectionAndManifest()
    {
        // The deterministic estate gives the same records, hashes and pins everywhere, so ranks, rounded scores, the selection's
        // hash and the manifest's selections must be byte-identical on every platform and in both globalization modes.
        using var estate = new SearchEstate();
        using EstateService service = estate.Open();
        SearchResponse search = service.Search(new SearchRequest("keyword index originals catalog"));
        Golden("selection-1.json", CanonicalJson.SerializePrettyUtf8(search.Selection!.Value));
        ContextResponse package = service.Context(new ContextRequest("W-1", null, ["original bytes", "heron catalog"]));
        Golden("manifest-2.json", CanonicalJson.SerializePrettyUtf8(package.Package!.Payload.GetProperty("manifest").GetProperty("content")));
    }

    private static void Golden(string name, byte[] actual)
    {
        string? directory = AppContext.BaseDirectory;
        while (directory is not null && !File.Exists(Path.Combine(directory, "Vericairn.slnx"))) directory = Path.GetDirectoryName(directory);
        string path = Path.Combine(directory ?? throw new InvalidOperationException("The test repository is unavailable."), "tests", "fixtures", "search", name);
        // Fixture maintenance only: a missing golden is written once for review, never an existing one replaced.
        if (!File.Exists(path) && Environment.GetEnvironmentVariable("VERICAIRN_WRITE_SEARCH_GOLDENS") == "1")
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            using var created = new FileStream(path, FileMode.CreateNew, FileAccess.Write);
            created.Write(actual);
        }

        Assert.True(File.Exists(path), "The golden " + name + " is missing.");
        Assert.True(File.ReadAllBytes(path).AsSpan().SequenceEqual(actual), "The golden " + name + " changed:\n" + Encoding.UTF8.GetString(actual));
    }
}
