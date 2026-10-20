// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Collections.Immutable;
using System.Text;
using System.Text.Json.Nodes;
using Vericairn.Records.Estate;
using Vericairn.Records.Hashing;
using Vericairn.Records.Model;

namespace Vericairn.Records.Tests;

/// <summary>Real generated copies of record prose, beside authored copies of the same spelling.</summary>
public sealed class GeneratedWritingFixture : IDisposable
{
    static GeneratedWritingFixture() => DocumentGenerators.Register(new SummaryGenerator());

    public PolicyFixture Records { get; } = new();
    public TestEstate Estate => Records.Estate;
    public RecordEnvelope Source { get; }
    public IReadOnlyList<string> GeneratedPaths { get; }
    public IReadOnlyList<string> ExpectedLocations { get; }

    public GeneratedWritingFixture()
    {
        JsonObject decision = Records.DecisionBody();
        decision["summary"] = "October13";
        Source = Records.Add("decision", decision);

        JsonObject whole = Declaration("docs/decisions.md", "generated");
        whole["generator"] = new JsonObject { ["id"] = SummaryGenerator.Name, ["inputs"] = Inputs() };
        RecordEnvelope document = Records.Add("living-document", whole);
        JsonObject register = Declaration("docs/registers/decisions/{month}/{date}-{slug}-{id}.md", "generated");
        register["pathPattern"] = register["path"]!.DeepClone();
        register.Remove("path");
        register["generator"] = Generator();
        RecordEnvelope members = Records.Add("living-document", register);

        JsonObject region = Declaration("docs/guide.md", "authored");
        region["regions"] = new JsonArray(new JsonObject
        {
            ["id"] = "decisions", ["generator"] = SummaryGenerator.Name, ["inputs"] = Inputs(),
        });
        RecordEnvelope guide = Records.Add("living-document", region);
        Estate.WriteFile("docs/guide.md", "October13\n<!-- vericairn:generated decisions -->\n<!-- vericairn:end decisions -->\nOctober13\n");
        JsonObject authored = Declaration("docs/authored.md", "authored");
        authored.Remove("checks");
        Records.Add("living-document", authored);
        Estate.WriteFile("docs/authored.md", "October13\n");
        Records.AssertValid();

        List<string> generated = [];
        foreach (RecordEnvelope declaration in new[] { document, members, guide })
        {
            DocumentChangePlan plan = DocumentRepair.Prepare(Estate.Load(), declaration);
            Assert.Empty(plan.Problems);
            Assert.Equal(DocumentGenerationStatus.Success, plan.Status);
            Assert.NotEmpty(plan.Changes);
            foreach (DocumentOutputChange change in plan.Changes)
            {
                string text = Encoding.UTF8.GetString(change.After!.Value.AsSpan());
                Estate.WriteFile(change.Path, text);
                if (declaration != guide) generated.Add(change.Path);
            }
        }
        GeneratedPaths = generated;
        string guideText = File.ReadAllText(Path.Combine(Estate.Root, "docs", "guide.md"));
        int lastLine = guideText.TrimEnd('\n').Split('\n').Length;
        ExpectedLocations = new[] { "docs/authored.md:1", "docs/guide.md:1", "docs/guide.md:" + lastLine.ToString(System.Globalization.CultureInfo.InvariantCulture),
            Source.ExpectedPath + "#body.summary:1" }.Order(StringComparer.Ordinal).ToArray();
        string member = Assert.Single(generated, path => path.Contains(Source.Id, StringComparison.Ordinal));
        foreach (string path in new[] { "docs/decisions.md", member })
            Assert.Contains(WritingScanner.Scan(File.ReadAllText(Path.Combine(Estate.Root, path)), path), o => o.Token == "October13");
        Assert.Contains("October13", GeneratedRegions.Content(guideText, "decisions"), StringComparison.Ordinal);
    }

    public static JsonObject Declaration(string path, string kind) => new()
    {
        ["path"] = path, ["title"] = "Writing scan fixture", ["kind"] = kind, ["source"] = "estate-file",
        ["triggers"] = kind == "generated" ? new JsonArray() : new JsonArray(new JsonObject { ["on"] = "decision", ["level"] = "warn" }),
        ["checks"] = new JsonArray("matches-generator"), ["summary"] = "Check generated ownership and authored prose.",
    };

    public static JsonObject Generator() => new() { ["id"] = "vericairn.records/decisions", ["inputs"] = Inputs() };
    private static JsonArray Inputs() => new("format=1", "audience=Internal", "selection=all-revisions");

    public void Dispose() => Records.Dispose();

    private sealed class SummaryGenerator : IDocumentGenerator
    {
        internal const string Name = "example.writing/summaries";
        public string Id => Name;
        public DocumentGeneratorDescriptor Descriptor { get; } = new(Name, 1, "example.writing/summaries/1",
            ContentHash.Sha256("Writing fixture summaries"u8), new Dictionary<string, ImmutableArray<string>>
            {
                ["audience"] = ["Internal"], ["selection"] = ["all-revisions"],
            }.ToImmutableDictionary(StringComparer.Ordinal), ["decision"], ["record"], ["all-revisions"], "text/markdown", "UTC time, id", "Internal", "single");
        public DocumentRenderResult Generate(DocumentGenerationContext context) => DocumentRenderResult.Success(
            string.Join('\n', context.Sources.Select(s => s.Body!.Value.GetProperty("summary").GetString())) + "\n");
    }
}
