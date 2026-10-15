// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using Vericairn.Records.Estate;
using Vericairn.Records.Model;

namespace Vericairn.Records.Tests;

/// <summary>Generated regions, the foundation's releases generator, and the checks run at a clearance (the estate's design/living-documents.md §§ 4 and 8).</summary>
public class LivingDocumentContentTests
{
    [Fact]
    public void ARegionIsTheContentBetweenItsMarkersAndOnlyThatIsRewritten()
    {
        const string Text = "# Title\n\nAuthored.\n<!-- vericairn:generated releases -->\nold\n<!-- vericairn:end releases -->\nMore authored.\n";
        Assert.Equal("old\n", GeneratedRegions.Content(Text, "releases"));
        Assert.Equal("# Title\n\nAuthored.\n<!-- vericairn:generated releases -->\nnew\n<!-- vericairn:end releases -->\nMore authored.\n", GeneratedRegions.Replace(Text, "releases", "new"));
        Assert.Null(GeneratedRegions.Content(Text, "other"));
        Assert.Null(GeneratedRegions.Content("<!-- vericairn:end x -->\n<!-- vericairn:generated x -->\n", "x"));
        Assert.Null(GeneratedRegions.Content("<!-- vericairn:generated x -->\na\n<!-- vericairn:end x -->\n<!-- vericairn:generated x -->\n", "x"));
        Assert.Null(GeneratedRegions.Content("Inline <!-- vericairn:generated x --> marker\n<!-- vericairn:end x -->\n", "x"));
        Assert.Equal("a\n", GeneratedRegions.Content("<!-- vericairn:generated x -->\r\na\n<!-- vericairn:end x -->\r\n", "x"));
    }

    [Fact]
    public void TheReleasesGeneratorTabulatesPublishedReleasesAndTheEstateCheckRefusesAHandEdit()
    {
        using var estate = new TestEstate(recordFormat: 2);
        RecordEnvelope product = Product(estate);
        estate.Add("release", estate.BuilderId, Release("0.1.0", "candidate"), new RecordReference("product", product.Id, null));
        estate.Add("release", estate.BuilderId, Release("0.2.0", "published"), new RecordReference("product", product.Id, null));
        estate.Add("living-document", estate.OperatorId, $$"""{"path":"docs/guide.md","title":"Guide","kind":"authored","source":"estate-file","triggers":[{"on":"release","level":"warn","product":"{{product.Id}}"}],"regions":[{"id":"releases","generator":"vericairn.records/releases","inputs":["{{product.Id}}"]}],"checks":["matches-generator"],"summary":"Lists the releases."}""");
        string table = DocumentGenerators.Find("vericairn.records/releases")!.Generate(estate.Load(), "releases", [product.Id])!;
        Assert.Equal("| Version | Published | Release |\n|---|---|---|\n| 0.2.0 | 2026-10-13 | [v0.2.0](https://example.invalid/releases/0.2.0) |\n", table);

        // In place it passes; a hand edit inside the markers is refused, and so are lost markers; an absent file is reported.
        estate.WriteFile("docs/guide.md", "# Guide\n<!-- vericairn:generated releases -->\n" + table + "<!-- vericairn:end releases -->\n");
        Assert.DoesNotContain(EstateChecks.Run(estate.Load()), f => f.Code.StartsWith("document.generated", StringComparison.Ordinal));
        estate.WriteFile("docs/guide.md", "# Guide\n<!-- vericairn:generated releases -->\n| 9.9.9 | soon | |\n<!-- vericairn:end releases -->\n");
        Assert.Contains(EstateChecks.Run(estate.Load()), f => f.Code == "document.generated-edited" && f.Message.Contains("region releases differs", StringComparison.Ordinal));
        estate.WriteFile("docs/guide.md", "# Guide\n");
        Assert.Contains(EstateChecks.Run(estate.Load()), f => f.Code == "document.generated-edited" && f.Message.Contains("lost its markers", StringComparison.Ordinal));
        File.Delete(Path.Combine(estate.Root, "docs", "guide.md"));
        Assert.Contains(EstateChecks.Run(estate.Load()), f => f.Code == "document.absent");
    }

    [Fact]
    public void TheClearanceChecksNameTheVersionResolveLinksAndCompareGeneratedContent()
    {
        using var estate = new TestEstate(recordFormat: 2);
        RecordEnvelope product = Product(estate);
        RecordEnvelope release = estate.Add("release", estate.BuilderId, Release("0.2.0", "published"), new RecordReference("product", product.Id, null));
        RecordEnvelope declaration = estate.Add("living-document", estate.OperatorId, $$"""{"path":"docs/guide.md","title":"Guide","kind":"authored","source":"estate-file","triggers":[{"on":"release","level":"warn","product":"{{product.Id}}"}],"regions":[{"id":"releases","generator":"vericairn.records/releases"}],"checks":["names-version","links-resolve","matches-generator","vericairn.example/not-installed"],"summary":"x"}""");
        estate.WriteFile("docs/other.md", "# Other\n");
        EstateModel model = estate.Load();
        string table = DocumentGenerators.Find("vericairn.records/releases")!.Generate(model, "releases", [])!;
        string good = $"# Guide for 0.2.0\n\nSee [the other](other.md#top), [the site](https://example.invalid) and [here](#anchor).\n<!-- vericairn:generated releases -->\n{table}<!-- vericairn:end releases -->\n";
        Dictionary<string, string> outcomes = DocumentChecks.Run(model, declaration, good, "docs/guide.md", estate.Root, [release]).ToDictionary(o => o.Name, o => o.Result);
        Assert.Equal("passed", outcomes["names-version"]);
        Assert.Equal("passed", outcomes["links-resolve"]);
        Assert.Equal("passed", outcomes["matches-generator"]);
        Assert.Equal("not-performed", outcomes["vericairn.example/not-installed"]);

        string bad = "# Guide\n\nSee [missing](gone.md) and [outside](../../../etc/hosts).\n<!-- vericairn:generated releases -->\nedited\n<!-- vericairn:end releases -->\n";
        IReadOnlyList<DocumentChecks.Outcome> failed = DocumentChecks.Run(model, declaration, bad, "docs/guide.md", estate.Root, [release]);
        Assert.All(failed.Where(o => o.Name != "vericairn.example/not-installed"), o => Assert.Equal("failed", o.Result));
        Assert.Contains("gone.md", failed.Single(o => o.Name == "links-resolve").Summary, StringComparison.Ordinal);
        Assert.Contains("../../../etc/hosts", failed.Single(o => o.Name == "links-resolve").Summary, StringComparison.Ordinal);
        Assert.Equal("skipped", DocumentChecks.Run(model, declaration, bad, "docs/guide.md", estate.Root, []).Single(o => o.Name == "names-version").Result);
    }

    private static RecordEnvelope Product(TestEstate estate) =>
        estate.Add("product", estate.OperatorId, """{"name":"Example","slug":"example","summary":"A product.","editions":[{"name":"Community","slug":"community","summary":"Free."}],"surfaces":[{"name":"CLI","slug":"cli","summary":"Command line."}]}""");

    private static string Release(string version, string status) =>
        $$"""{"version":"{{version}}","repository":"Example/example","commit":"0123456789abcdef0123456789abcdef01234567","status":"{{status}}","publishedAt":"2026-10-13T20:00:00Z","url":"https://example.invalid/releases/{{version}}","artifacts":[],"recordFormat":{"reads":[1,2],"writes":2},"evidence":[],"qualified":[],"signing":"unsigned developer build","limitations":[]}""";
}
