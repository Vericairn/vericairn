// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text;
using Vericairn.Records.Estate;
using Vericairn.Records.Model;

namespace Vericairn.Records.Tests;

public class WritingScannerTests
{
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
