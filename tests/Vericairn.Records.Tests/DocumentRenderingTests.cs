// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Vericairn.Records.Estate;
using Vericairn.Records.Hashing;
using Vericairn.Records.Model;

namespace Vericairn.Records.Tests;

public class DocumentRenderingTests
{
    [Fact]
    public void CanonicalMarkdownHasOnePortableEncodingAndLiteralHostileValues()
    {
        Assert.Equal("# Title\n\nText\n", Encoding.UTF8.GetString(DocumentMarkdown.Bytes("\uFEFF# Title \t\r\n\rText\t\r\n\r\n")));
        Assert.Equal("\n", Encoding.UTF8.GetString(DocumentMarkdown.Bytes(string.Empty)));
        Assert.Throws<EncoderFallbackException>(() => DocumentMarkdown.Bytes("\ud800"));
        Assert.Equal("&lt;script&gt;&#91;x&#93;&#40;javascript&#58;run&#41;&#96;&#124; &amp; 東京", DocumentMarkdown.Text("<script>[x](javascript:run)`|\n& 東京"));
        Assert.Equal("[A&#124;B](docs/source.md)", DocumentMarkdown.Link("A|B", "docs/source.md"));
        Assert.Throws<ArgumentException>(() => DocumentMarkdown.Link("Open", "javascript:run"));
        Assert.Throws<ArgumentException>(() => DocumentMarkdown.Link("Open", "../private.md"));
    }

    [Fact]
    public void RenderingAndProvenanceIgnoreCultureTimeZoneAndSelectionOrder()
    {
        using var f = new PolicyFixture();
        RecordEnvelope declaration = f.Add("living-document", DocumentGenerationTests.Declaration());
        JsonObject next = f.DecisionBody();
        next["statement"] = "A second exact statement.";
        f.Add("decision", next);
        DocumentSourceSelection selected = DocumentSourceSelection.Records(f.Estate.Load(), DocumentGenerationTests.Generator.Descriptor!);
        DocumentMember first = DocumentGeneration.Members(declaration, selected).Members[0];
        var generator = new LiteralGenerator();
        var context = first.Context with { Generator = generator.Descriptor, Sources = selected.Sources };
        DocumentRendition original = DocumentRendering.Render(generator, context);
        CultureInfo previous = CultureInfo.CurrentCulture;
        try
        {
            var different = (CultureInfo)CultureInfo.InvariantCulture.Clone();
            different.DateTimeFormat.DateSeparator = "!";
            different.NumberFormat.NegativeSign = "minus";
            CultureInfo.CurrentCulture = different;
            DocumentRendition shuffled = DocumentRendering.Render(generator, context with { Sources = [.. selected.Sources.Reverse()] });
            Assert.Equal(DocumentGenerationStatus.Success, shuffled.Status);
            Assert.Equal(original.ContentHash, shuffled.ContentHash);
            Assert.Equal(original.InputDigest, shuffled.InputDigest);
            Assert.Equal("Approve the stated definition only.\nA second exact statement.\n", Encoding.UTF8.GetString(shuffled.Bytes.AsSpan()));
            Assert.Equal("2026-10-13 23:00:00 UTC", DocumentMarkdown.Timestamp(new DateTimeOffset(2026, 10, 14, 1, 0, 0, TimeSpan.FromHours(2))));
            Assert.Equal(DocumentMarkdown.Timestamp(new DateTimeOffset(2026, 10, 13, 23, 0, 0, TimeSpan.Zero)),
                DocumentMarkdown.Timestamp(new DateTimeOffset(2026, 10, 13, 18, 0, 0, TimeSpan.FromHours(-5))));
        }
        finally { CultureInfo.CurrentCulture = previous; }
    }

    [Fact]
    public void FiveIndependentRevisionsMergeByUnionWithoutRewritingExistingMembers()
    {
        using var f = new PolicyFixture();
        RecordEnvelope declaration = f.Add("living-document", DocumentGenerationTests.Declaration());
        DocumentMember first = Assert.Single(DocumentGenerationTests.Members(f.Estate.Load(), declaration).Members);
        DocumentRendition original = DocumentRendering.Render(DocumentGenerationTests.Generator, first.Context);
        var union = new Dictionary<string, string>(StringComparer.Ordinal) { [first.Path] = original.ContentHash! };
        for (int i = 0; i < 5; i++)
        {
            JsonObject body = f.DecisionBody();
            body["title"] = "Equal title 東京";
            body["statement"] = "Independent decision " + i.ToString(CultureInfo.InvariantCulture) + ".";
            RecordEnvelope added = f.Add("decision", body);
            DocumentMember member = Assert.Single(DocumentGeneration.Members(declaration, new(DocumentGenerationStatus.Success, [DocumentSourceSnapshot.Record(added)], null, null)).Members);
            union.Add(member.Path, DocumentRendering.Render(DocumentGenerationTests.Generator, member.Context).ContentHash!);
        }
        DocumentMemberSelection merged = DocumentGenerationTests.Members(f.Estate.Load(), declaration);
        Assert.Equal(6, merged.Members.Length);
        Assert.All(merged.Members, m => Assert.Equal(union[m.Path], DocumentRendering.Render(DocumentGenerationTests.Generator, m.Context).ContentHash));
        Assert.Equal(first.Context.InputDigest, merged.Members.Single(m => m.Path == first.Path).Context.InputDigest);
        Assert.Single(f.Estate.Load().OfType("living-document"));
        Assert.Empty(f.Estate.Load().OfType("document-revision"));
    }

    [Fact]
    public void InvalidSourcesExceptionsOversizeAndUnknownVersionsReturnNoBytesOrDigest()
    {
        using var f = new PolicyFixture();
        RecordEnvelope declaration = f.Add("living-document", DocumentGenerationTests.Declaration());
        DocumentGenerationContext basis = Assert.Single(DocumentGenerationTests.Members(f.Estate.Load(), declaration).Members).Context;
        foreach (string behavior in new[] { "throw", "null", "oversize", "unavailable", "invalid-text" })
        {
            var generator = new LiteralGenerator(behavior);
            DocumentRendition result = DocumentRendering.Render(generator, basis with { Generator = generator.Descriptor });
            Assert.NotEqual(DocumentGenerationStatus.Success, result.Status);
            Assert.Empty(result.Bytes);
            Assert.Null(result.InputDigest);
            Assert.Null(result.ContentHash);
            Assert.DoesNotContain("private detail", result.Reason!, StringComparison.Ordinal);
        }
        Assert.Equal(DocumentGenerationStatus.Unavailable, DocumentRendering.Render(DocumentGenerationTests.Generator, basis with { InterfaceVersion = 99 }).Status);
        DocumentSourceSnapshot source = Assert.Single(basis.Sources);
        Assert.Equal(DocumentGenerationStatus.Error, DocumentRendering.Render(DocumentGenerationTests.Generator,
            basis with { Sources = [source with { Body = JsonSerializer.SerializeToElement(new { statement = "tampered" }) }] }).Status);
        Assert.Equal(DocumentGenerationStatus.Error, DocumentRendering.Render(DocumentGenerationTests.Generator,
            basis with { Sources = [source, source] }).Status);
    }

    private sealed class LiteralGenerator(string behavior = "normal") : IDocumentGenerator
    {
        public string Id => "example.documents/literal";
        public DocumentGeneratorDescriptor Descriptor { get; } = DocumentGenerationTests.Generator.Descriptor! with { Id = "example.documents/literal", MaxMemberBytes = 256 };
        public DocumentRenderResult Generate(DocumentGenerationContext context) => behavior switch
        {
            "throw" => throw new NotSupportedException("private detail"),
            "null" => new(DocumentGenerationStatus.Success, null, null),
            "oversize" => DocumentRenderResult.Success(new string('x', 256)),
            "unavailable" => new(DocumentGenerationStatus.Unavailable, "partial", "private detail"),
            "invalid-text" => DocumentRenderResult.Success("\ud800"),
            _ => DocumentRenderResult.Success(string.Join(" \r\n", context.Sources.Select(s => s.Body!.Value.GetProperty("statement").GetString())) + "\r\n\r\n"),
        };
    }
}
