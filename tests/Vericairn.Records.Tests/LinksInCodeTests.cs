// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using Vericairn.Records.Estate;
using Vericairn.Records.Model;

namespace Vericairn.Records.Tests;

/// <summary>
/// FIX-138 (decision 01M7BMNRGSG44FXFZMRD9SY44R): links-resolve passes over a link written inside a fenced or indented code block or an
/// inline code span, which shows Markdown rather than linking, while a broken relative link outside code is still refused at the same
/// line and column it always had.
/// </summary>
public sealed class LinksInCodeTests
{
    // Each kind of code holds a link that resolves to nothing from docs/; a broken link stands beside each, outside code.
    private static readonly string[] Document =
    [
        "# Guide",
        "",
        "The owner's file at an estate's root, for example:",
        "",
        "```markdown",
        "Read [the orientation](docs/vericairn-orientation/AGENTS.md) first.",
        "```",
        "",
        "~~~",
        "[A tilde fence](missing-tilde.md)",
        "~~~",
        "",
        "    [An indented block](missing-indented.md)",
        "",
        "Write `[a link](missing-span.md)` to link, or ``[two](missing-double.md) ` ticks``, and see [the broken one](missing-prose.md).",
        "",
        "- An item with [a broken link](missing-item.md).",
        "",
        "  A continuation, not code, with [another](missing-continuation.md).",
        "",
        "      [Code in the item](missing-item-code.md)",
        "",
        "> ```",
        "> [A quoted fence](missing-quoted.md)",
        "> ```",
        "",
        "See [the other guide](other.md) and [a gone one](gone.md).",
        "",
        "A lone ` backtick leaves [this](missing-lone.md) a link.",
        "",
        "An escaped \\`[mark](missing-escaped.md)\\` stays prose, as does a paragraph's",
        "    [indented continuation](missing-lazy.md).",
        "",
    ];

    private static readonly string[] OutsideCode =
        ["missing-prose.md", "missing-item.md", "missing-continuation.md", "gone.md", "missing-lone.md", "missing-escaped.md", "missing-lazy.md"];

    [Fact]
    public void ALinkInsideEachKindOfCodeIsSkippedAndABrokenLinkBesideItIsStillRefusedAtItsLineAndColumn()
    {
        using var estate = new TestEstate(recordFormat: 2);
        (EstateModel model, RecordEnvelope declaration) = Declare(estate);
        string text = string.Join("\n", Document);

        DocumentChecks.Outcome outcome = Assert.Single(DocumentChecks.Run(model, declaration, text, "docs/guide.md", estate.Root, []), o => o.Name == "links-resolve");

        Assert.Equal("failed", outcome.Result);
        string[] expected = [.. OutsideCode.Select(target => Where(text, target))];
        Assert.Equal(expected, outcome.Details.Select(d => d.Location));
        Assert.Equal(OutsideCode.Select(target => $"links-resolve: {target} does not resolve, which names docs/{target}, not in the repository."), outcome.Details.Select(d => d.Message));
        Assert.Contains($"({OutsideCode.Length}): missing-prose.md at line 15, column ", outcome.Summary, StringComparison.Ordinal);
        Assert.DoesNotContain("orientation", outcome.Summary, StringComparison.Ordinal);
    }

    [Fact]
    public void ADocumentWhoseOnlyUnresolvedLinksAreInCodePasses()
    {
        using var estate = new TestEstate(recordFormat: 2);
        (EstateModel model, RecordEnvelope declaration) = Declare(estate);
        // Every link outside code now resolves; those inside code still name nothing.
        string text = string.Join("\n", Document.Select(line => OutsideCode.Aggregate(line, (l, target) => l.Replace($"({target})", "(other.md)", StringComparison.Ordinal))));

        DocumentChecks.Outcome outcome = Assert.Single(DocumentChecks.Run(model, declaration, text, "docs/guide.md", estate.Root, []), o => o.Name == "links-resolve");

        Assert.Equal("passed", outcome.Result);
        Assert.Empty(outcome.Details);
    }

    [Theory]
    [InlineData("```\n[a](x)\n", true)]
    [InlineData("````\n```\n[a](x)\n````\n[b](y)\n", false)]
    [InlineData("``` info `with` backticks\n[a](x)\n", false)]
    [InlineData("Text\n    [a](x)\n", false)]
    [InlineData("\t[a](x)\n", true)]
    [InlineData("1. Item\n\n       [a](x)\n", true)]
    [InlineData("1. Item\n\n   [a](x)\n", false)]
    [InlineData("A `span\nacross lines [a](x)` here.\n", true)]
    [InlineData("A `span\n\nbroken by a blank line [a](x)` here.\n", false)]
    public void TheCodeMaskFollowsTheMarkdownBlocksAndSpans(string text, bool coded)
    {
        int index = text.LastIndexOf("](", StringComparison.Ordinal);
        Assert.Equal(coded, MarkdownCode.Mask(text)[index]);
    }

    private static (EstateModel Model, RecordEnvelope Declaration) Declare(TestEstate estate)
    {
        RecordEnvelope product = estate.Add("product", estate.OperatorId, """{"name":"Example","slug":"example","summary":"A product.","editions":[{"name":"Community","slug":"community","summary":"Free."}],"surfaces":[{"name":"CLI","slug":"cli","summary":"Command line."}]}""");
        RecordEnvelope declaration = estate.Add("living-document", estate.OperatorId, $$"""{"path":"docs/guide.md","title":"Guide","kind":"authored","source":"estate-file","triggers":[{"on":"release","level":"warn","product":"{{product.Id}}"}],"checks":["links-resolve"],"summary":"x"}""");
        estate.WriteFile("docs/other.md", "# Other\n");
        return (estate.Load(), declaration);
    }

    // The link's location as the check has always named it: the document, the line, and the column of its target, from 1.
    private static string Where(string text, string target)
    {
        string[] lines = text.Split('\n');
        int line = Array.FindIndex(lines, l => l.Contains($"({target})", StringComparison.Ordinal));
        return $"docs/guide.md:{line + 1}:{lines[line].IndexOf($"({target})", StringComparison.Ordinal) + 2}";
    }
}
