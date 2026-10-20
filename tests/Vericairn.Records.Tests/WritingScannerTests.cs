// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text;
using System.Text.Json.Nodes;
using Vericairn.Records.Estate;
using Vericairn.Records.Model;

namespace Vericairn.Records.Tests;

public class WritingScannerTests
{
    [Fact]
    public void EstateScanKeepsAuthoredAndRecordProseWithoutScanningGeneratedCopies()
    {
        using var fixture = new GeneratedWritingFixture();
        FindingList findings = EstateChecks.Run(fixture.Estate.Load());
        Assert.False(findings.HasErrors, string.Join('\n', findings));
        Assert.Equal(fixture.ExpectedLocations, findings.Where(f => f.Code == "writing.spacing").Select(f => f.Location).Order(StringComparer.Ordinal));
        Assert.All(findings.Where(f => f.Code == "writing.spacing"), f => Assert.Contains("October13", f.Message, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("\n")]
    [InlineData("\r\n")]
    public void GeneratedRegionsKeepOriginalOffsetsAndCannotHideAuthoredLinesWithFences(string newline)
    {
        using var fixture = new PolicyFixture();
        JsonObject body = RegionDeclaration();
        body["regions"]!.AsArray().Add(new JsonObject { ["id"] = "second", ["generator"] = "vericairn.records/releases" });
        fixture.Add("living-document", body);
        fixture.AssertValid();
        string text = string.Join(newline, "é Before123", "<!-- vericairn:generated first -->", "😀 Open311", "~~~", "<!-- vericairn:end first -->",
            "é After123", "<!-- vericairn:generated second -->", "Rapid7", "<!-- vericairn:end second -->", "Outside123");
        IReadOnlyList<SpacingOccurrence> occurrences = WritingScanner.ScanFile(fixture.Estate.Load(), fixture.Estate.Estate.Id, text, "docs/guide.md");
        Assert.Equal(["Before123", "After123", "Outside123"], occurrences.Select(o => o.Token));
        Assert.Equal([1, 6, 10], occurrences.Select(o => o.Line));
        byte[] bytes = Encoding.UTF8.GetBytes(text);
        foreach (SpacingOccurrence occurrence in occurrences)
            Assert.Equal(occurrence.Token, Encoding.UTF8.GetString(bytes.AsSpan(occurrence.ByteOffset, occurrence.ByteLength)));
    }

    [Theory]
    [InlineData("<!-- vericairn:generated other -->\nOpen311\n<!-- vericairn:end other -->")]
    [InlineData("<!-- vericairn:generated first -->\nOpen311")]
    [InlineData("<!-- vericairn:end first -->\nOpen311\n<!-- vericairn:generated first -->")]
    [InlineData("<!-- vericairn:generated first -->\nOpen311\n<!-- vericairn:end first -->\n<!-- vericairn:generated first -->\nRapid7\n<!-- vericairn:end first -->")]
    [InlineData("<!-- vericairn:generated first -->\nOpen311\n<!-- vericairn:generated other -->\nRapid7\n<!-- vericairn:end first -->\n<!-- vericairn:end other -->")]
    [InlineData("Inline <!-- vericairn:generated first -->\nOpen311\n<!-- vericairn:end first -->")]
    public void UndeclaredOrMalformedRegionsLeaveAllAuthoredProseInTheScan(string text)
    {
        using var fixture = new PolicyFixture();
        fixture.Add("living-document", RegionDeclaration());
        Assert.Equal(WritingScanner.Scan(text, "docs/guide.md"), WritingScanner.ScanFile(fixture.Estate.Load(), fixture.Estate.Estate.Id, text, "docs/guide.md"));
    }

    [Fact]
    public void MarkersWithoutADeclarationNeverExemptText()
    {
        using var fixture = new PolicyFixture();
        const string Text = "Before123\n<!-- vericairn:generated first -->\nOpen311\n<!-- vericairn:end first -->\nAfter123";
        Assert.Equal(WritingScanner.Scan(Text, "docs/guide.md"), WritingScanner.ScanFile(fixture.Estate.Load(), fixture.Estate.Estate.Id, Text, "docs/guide.md"));
    }

    [Theory]
    [InlineData("authored")]
    [InlineData("unknown")]
    public void AGeneratorPropertyDoesNotExemptOtherDeclarationKinds(string kind)
    {
        using var fixture = new PolicyFixture();
        JsonObject body = GeneratedWritingFixture.Declaration("docs/guide.md", kind);
        body["generator"] = GeneratedWritingFixture.Generator();
        fixture.Add("living-document", body);
        EstateModel model = fixture.Estate.Load();
        Assert.True(model.Findings.HasErrors);
        Assert.Single(WritingScanner.ScanFile(model, fixture.Estate.Estate.Id, "Open311", "docs/guide.md"));
    }

    [Fact]
    public void RetiredGeneratedOwnershipDoesNotExemptRemainingText()
    {
        using var fixture = new GeneratedWritingFixture();
        fixture.Records.Add("retirement", new JsonObject
        {
            ["path"] = "docs/decisions.md", ["reason"] = "End generated ownership.", ["condition"] = "The register replaces the whole document.",
            ["retiredAt"] = RecordParser.FormatTimestamp(fixture.Estate.Clock),
        });
        fixture.Records.AssertValid();
        Assert.Single(WritingScanner.ScanFile(fixture.Estate.Load(), fixture.Estate.Estate.Id, "Open311", "docs/decisions.md"));
    }

    [Fact]
    public void OnlyTheCurrentDeclarationCanExemptText()
    {
        using var fixture = new PolicyFixture(format: 2);
        JsonObject body = GeneratedWritingFixture.Declaration("docs/guide.md", "generated");
        body["generator"] = GeneratedWritingFixture.Generator();
        body["triggers"] = new JsonArray(new JsonObject { ["on"] = "decision", ["level"] = "warn" });
        RecordEnvelope previous = fixture.Add("living-document", body);
        Assert.Empty(WritingScanner.ScanFile(fixture.Estate.Load(), fixture.Estate.Estate.Id, "Open311", "docs/guide.md"));
        body["kind"] = "authored";
        body.Remove("generator");
        body.Remove("checks");
        fixture.Add("living-document", body, supersedes: previous.Id);
        fixture.AssertValid();
        Assert.Single(WritingScanner.ScanFile(fixture.Estate.Load(), fixture.Estate.Estate.Id, "Open311", "docs/guide.md"));
    }

    [Fact]
    public void SkippingGeneratedProsePreservesOtherContentAndGeneratorChecks()
    {
        using var fixture = new GeneratedWritingFixture();
        fixture.Estate.WriteFile("docs/decisions.md", "October13\r\n");
        FindingList findings = EstateChecks.Run(fixture.Estate.Load());
        Assert.DoesNotContain(findings, f => f.Code == "writing.spacing" && f.Location.StartsWith("docs/decisions.md:", StringComparison.Ordinal));
        Assert.Contains(findings, f => f.Code == "layout.line-endings" && f.Location == "docs/decisions.md");
        Assert.Contains(findings, f => f.Severity == Severity.Error && f.Code.StartsWith("document.generated", StringComparison.Ordinal) && f.Location == "docs/decisions.md");
    }

    private static JsonObject RegionDeclaration()
    {
        JsonObject body = GeneratedWritingFixture.Declaration("docs/guide.md", "authored");
        body["regions"] = new JsonArray(new JsonObject { ["id"] = "first", ["generator"] = "vericairn.records/releases" });
        return body;
    }

    [Theory]
    [InlineData("```info\nOpen311\n```\nRapid7", "Rapid7")]
    [InlineData("~~~info`\nOpen311\n~~~~\nRapid7", "Rapid7")]
    [InlineData("   ````info\nOpen311\n```\nRapid7\n```` \t\nproject44", "project44")]
    [InlineData("`````\nOpen311\n~~~~~\nRapid7", "")]
    [InlineData("```\nOpen311\n```text\nRapid7\n```\nproject44", "project44")]
    [InlineData("```bad`info\nOpen311", "Open311")]
    [InlineData("    ```\nOpen311", "Open311")]
    [InlineData("\t```\nOpen311", "Open311")]
    [InlineData("> ```\nOpen311", "Open311")]
    [InlineData("- ```\nOpen311", "Open311")]
    [InlineData("``\nOpen311", "Open311")]
    [InlineData("~~~\n```\nOpen311\n~~~\nRapid7", "Rapid7")]
    [InlineData("Before123\n```\n```\nAfter123", "Before123,After123")]
    [InlineData("`Open311` Rapid7 `project44`", "Rapid7")]
    [InlineData("decade2026 `decade2026` efea482", "decade2026,efea482")]
    public void FiniteFenceGrammarAndExistingInlineBlanking(string source, string expected)
    {
        Assert.Equal(expected, string.Join(',', WritingScanner.Scan(source, "docs/test.md").Select(o => o.Token)));
        Assert.Equal(expected, string.Join(',', WritingScanner.Scan(source.Replace("\n", "\r\n", StringComparison.Ordinal), "docs/test.md").Select(o => o.Token)));
        Assert.Single(WritingScanner.Scan("Open311", "docs/next.md"));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void EveryPermittedIndentationOpensAndCloses(int indentation)
    {
        string spaces = new(' ', indentation);
        Assert.Empty(WritingScanner.Scan(spaces + "~~~\nOpen311\n" + spaces + "~~~", "field"));
    }

    [Theory]
    [InlineData("\n")]
    [InlineData("\r\n")]
    public void LocationsAreUtf8OffsetsInOriginalBytesIncludingCodeAndLineEndings(string newline)
    {
        string text = "é `ignored123` 😀 Open311" + newline + "~~~" + newline + "Rapid7" + newline + "~~~" + newline + "project44";
        IReadOnlyList<SpacingOccurrence> occurrences = WritingScanner.Scan(text, "docs/test.md");
        Assert.Equal(2, occurrences.Count);
        Assert.Equal([1, 5], occurrences.Select(o => o.Line));
        byte[] bytes = Encoding.UTF8.GetBytes(text);
        foreach (SpacingOccurrence occurrence in occurrences)
        {
            Assert.Equal(occurrence.Token, Encoding.UTF8.GetString(bytes.AsSpan(occurrence.ByteOffset, occurrence.ByteLength)));
            Assert.Equal(Severity.Warning, occurrence.Diagnostic.Severity);
            Assert.Equal("writing.spacing", occurrence.Diagnostic.Code);
            Assert.EndsWith("put a space between the word and the number, or write an identifier as code.", occurrence.Diagnostic.Message, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void MatchingAndTheBuiltinAllowlistStayUnchanged()
    {
        Assert.Equal(["October13", "checkpoint2693", "Open311"], WritingScanner.Scan("October13 checkpoint2693 Open311 python3 sqlite3 SHA256 sha256 words12/path part99.md camelCase12 ID123 _word123", "field").Select(o => o.Token));
        FindingList check = EstateChecks.Content("docs/test.md", Encoding.UTF8.GetBytes("```\nOpen311\n```\nRapid7"));
        Finding spacing = Assert.Single(check, f => f.Code == "writing.spacing");
        Assert.Contains("Rapid7", spacing.Message, StringComparison.Ordinal);
    }
}
