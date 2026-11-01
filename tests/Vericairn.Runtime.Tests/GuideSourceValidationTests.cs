// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Net;
using System.Text;
using System.Text.Json;
using Vericairn.Runtime.Guides;

namespace Vericairn.Runtime.Tests;

public sealed class GuideSourceValidationTests
{
    [Theory]
    [InlineData("\n")]
    [InlineData("\r\n")]
    public void UnquotedParentLinksAndContractExamplesLeaveGuideBytesUnchanged(string newline)
    {
        GuideFixture fixture = GuideFixture.Create(newline: newline);
        GuideBundle before = Generate(fixture);
        Append(fixture, "family00", newline + "[Catalog](../catalog.md)" + newline);
        Append(fixture, "contracts", newline + "A comment begins with `<!--` in this unquoted example." + newline);

        GuideBundle after = Generate(fixture);
        Assert.Equal(fixture.PlannedKeys.Length, after.Files.Keys.Count(k => k.StartsWith("guides/", StringComparison.Ordinal) && k.EndsWith(".json", StringComparison.Ordinal)));
        foreach (string name in before.Files.Keys.Where(k => k.StartsWith("guides/", StringComparison.Ordinal)))
            Assert.Equal(before.Files[name], after.Files[name]);
        Assert.Empty(GuideGenerator.CompareInputs(before.Files["source-manifest.json"], after.Files["source-manifest.json"]).Due);
    }

    [Fact]
    public void UnquotedFamilyLinksStillRequireTheirAllowlistedSources()
    {
        GuideFixture fixture = GuideFixture.Create();
        Append(fixture, "catalog", "\n[Additional family](catalog/extra.md)\n");
        GuideBundle bundle = GuideGenerator.Generate(fixture.Index, fixture.Sources);
        AssertFailed(bundle);
        Assert.Contains(bundle.Findings, f => f.Entry == "catalog" && f.Code == "missing-family-source");
    }

    [Theory]
    [InlineData("<script>run()</script>")]
    [InlineData("<iframe src='notes.md'></iframe>")]
    [InlineData("<span onclick='run()'>action</span>")]
    [InlineData("![remote](https://invalid.example/track)")]
    [InlineData("[action](javascript:run())")]
    [InlineData("[action](java&#x73;cript:run())")]
    [InlineData("[action](%256a%2561vascript:run())")]
    [InlineData("[action](vbscript:run())")]
    [InlineData("[action](data:text/plain,example)")]
    [InlineData("[action](file:private.md)")]
    [InlineData("[action](//invalid.example/action)")]
    [InlineData("[action](%2f%2finvalid.example/action)")]
    [InlineData("<!-- hidden obligation -->")]
    public void QuotedBlocksAndMappedCellsRefuseActiveContentAfterRepinning(string markup)
    {
        foreach (bool block in new[] { true, false })
        {
            GuideFixture fixture = GuideFixture.Create();
            fixture.Replace("family00", block ? "Reserved acts: the designated person approves every delivery." : "Builders prepare measured sketches", markup);
            GuideBundle bundle = GuideGenerator.Generate(fixture.Index, fixture.Sources);
            AssertFailed(bundle);
            string entry = block ? "family00.common" : "fixture.workshop00.domain";
            Assert.Contains(bundle.Findings, f => f.Entry == entry && f.Code == "malicious-markup");
        }
    }

    [Theory]
    [InlineData("notes.md")]
    [InlineData("./notes.md#scope")]
    [InlineData("notes/detail.md")]
    public void QuotedRelativeLinksAreInertText(string target)
    {
        GuideFixture fixture = GuideFixture.Create();
        string text = "Read [the notes](" + target + ") without loading them.";
        fixture.Replace("family00", "Reserved acts: the designated person approves every delivery.", text);
        GuideBundle bundle = Generate(fixture);
        foreach (string extension in new[] { "md", "html" })
        {
            string rendition = Encoding.UTF8.GetString(bundle.Files["guides/provider.workshop00.group." + extension]);
            Assert.Contains(text, WebUtility.HtmlDecode(rendition), StringComparison.Ordinal);
            Assert.DoesNotContain("<a ", rendition, StringComparison.Ordinal);
        }
        using JsonDocument guide = JsonDocument.Parse(bundle.Files["guides/provider.workshop00.group.json"]);
        JsonElement quote = Assert.Single(guide.RootElement.GetProperty("sections").EnumerateArray()
            .SelectMany(s => s.GetProperty("quotes").EnumerateArray()), q => q.GetProperty("id").GetString() == "family00.common");
        Assert.StartsWith(text + "\n", quote.GetProperty("text").GetString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("|---|---|---|")]
    [InlineData("|---|---|---|---|---|")]
    [InlineData("|---|--|---|---|")]
    [InlineData("|---||---|---|")]
    [InlineData("|---|text|---|---|")]
    public void MalformedOwningDelimitersNameTheirSourceLineAndHeading(string delimiter)
    {
        string text = "# Synthetic catalog\n## Providers\n### Routes\n| Provider | Role | Works with | Caveat |\n" + delimiter +
            "\n| Route | Observe. | Sketch. | No delivery. |\n\n### Later routes\n| Provider | Role | Works with | Caveat |\n|---|---|---|---|\n| Later route | Observe. | Sketch. | No delivery. |\n";
        GuideInventory inventory = GuideMarkdown.Read("synthetic", Encoding.UTF8.GetBytes(text));
        Assert.Equal("Later route", Assert.Single(inventory.Rows).Name);
        GuideFinding finding = Assert.Single(inventory.Findings);
        Assert.Equal("synthetic", finding.Entry);
        Assert.Equal("malformed-table", finding.Code);
        Assert.Contains("Synthetic catalog / Providers / Routes", finding.Message, StringComparison.Ordinal);
        Assert.Contains("at line 4", finding.Message, StringComparison.Ordinal);
        Assert.Contains("delimiter at line 5", finding.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Identifier | Who needs it and why | Caveat")]
    [InlineData("Foundry | Initializes or adopts | Caveat")]
    [InlineData("Provider | Role | Caveat")]
    [InlineData("Capability | Role | Caveat")]
    [InlineData("Area | Owns | Boundary")]
    [InlineData("Entry | Area | Status | Where")]
    public void EveryOwningColumnContractReportsMalformedDelimiters(string columns)
    {
        GuideInventory inventory = GuideMarkdown.Read("synthetic", Encoding.UTF8.GetBytes("# Synthetic\n| " + columns + " |\n|---|---|\n"));
        Assert.Empty(inventory.Rows);
        Assert.Equal("malformed-table", Assert.Single(inventory.Findings).Code);
    }

    [Fact]
    public void AnUnmappedMalformedOwningTableCannotDisappearFromCoverage()
    {
        GuideFixture fixture = GuideFixture.Create();
        Append(fixture, "family00", "\n## Additional routes\n| Provider | Role | Works with | Caveat |\n|---|---|---|\n| Extra route | Observe. | Sketch. | No delivery. |\n");
        GuideBundle bundle = GuideGenerator.Generate(fixture.Index, fixture.Sources);
        AssertFailed(bundle);
        Assert.Contains(bundle.Findings, f => f.Entry == "family00" && f.Code == "malformed-table" &&
            f.Message.Contains("Workshop 00 / Additional routes", StringComparison.Ordinal));
        Assert.DoesNotContain(bundle.Coverage, c => c.State is "active-guide" or "planned-remainder-guide");
        using JsonDocument coverage = JsonDocument.Parse(bundle.Files["coverage-manifest.json"]);
        Assert.False(coverage.RootElement.GetProperty("complete").GetBoolean());
    }

    [Fact]
    public void NonowningAndFencedMalformedTablesRemainOutsideTheInventory()
    {
        const string text = "# Synthetic\n| Entry | Assembly | Status |\n|---|---|\n| Observed | Example | available |\n" +
            "```md\n| Provider | Role | Caveat |\n|---|---|\n| Example | Observe. | No delivery. |\n```\n";
        GuideInventory inventory = GuideMarkdown.Read("synthetic", Encoding.UTF8.GetBytes(text));
        Assert.Empty(inventory.Rows);
        Assert.Empty(inventory.Findings);
    }

    private static void Append(GuideFixture fixture, string source, string text)
    {
        fixture.Sources[source] = [.. fixture.Sources[source], .. Encoding.UTF8.GetBytes(text)];
        fixture.Index = fixture.Index with
        {
            Sources = fixture.Index.Sources.Select(s => s.Id == source ? s with { Sha256 = GuideGenerator.Hash(fixture.Sources[source]) } : s).ToArray(),
        };
    }

    private static GuideBundle Generate(GuideFixture fixture)
    {
        GuideBundle bundle = GuideGenerator.Generate(fixture.Index, fixture.Sources);
        Assert.True(bundle.Success, JsonSerializer.Serialize(bundle.Findings));
        return bundle;
    }

    private static void AssertFailed(GuideBundle bundle)
    {
        Assert.False(bundle.Success);
        Assert.DoesNotContain(bundle.Files.Keys, name => name.StartsWith("guides/", StringComparison.Ordinal));
    }
}
