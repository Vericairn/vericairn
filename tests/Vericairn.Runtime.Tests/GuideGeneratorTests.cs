// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Vericairn.Runtime.Guides;

namespace Vericairn.Runtime.Tests;

public sealed class GuideGeneratorTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(27)]
    [InlineData(31)]
    public void D17_A_EverySelectedPlannedOwningRowHasExactlyOneGuide(int familyCount)
    {
        GuideFixture fixture = GuideFixture.Create(familyCount);
        GuideBundle bundle = Generate(fixture);
        Assert.Equal(fixture.PlannedKeys, GuideKeys(bundle));
        Assert.Equal(fixture.Index.Entries.Length, bundle.Coverage.Count);
        Assert.Single(bundle.Coverage, c => c.State == "planned-remainder-guide");
        Assert.Single(bundle.Coverage, c => c.State == "reference" && c.Reason.Contains("fixture.shared.adapter", StringComparison.Ordinal));
        Assert.Single(bundle.Coverage, c => c.State == "fully-available-documentation");
        Assert.Equal(familyCount, bundle.Coverage.Count(c => c.Name == "Route Amber / Route Indigo"));
        Assert.Contains("added by the builder's draft", Text(bundle, "guides/fixture.workshop00.domain.md"), StringComparison.Ordinal);
        Assert.Contains("candidate only", Text(bundle, "guides/provider.workshop00.group.md"), StringComparison.Ordinal);
        foreach (string key in fixture.PlannedKeys)
        {
            using JsonDocument document = JsonDocument.Parse(bundle.Files["guides/" + key + ".json"]);
            Assert.Equal(13, document.RootElement.GetProperty("sections").GetArrayLength());
            Assert.Equal("private-preview", document.RootElement.GetProperty("publicationState").GetString());
            Assert.Contains("this guide does not make the entry available", Text(bundle, "guides/" + key + ".md"), StringComparison.Ordinal);
        }
        string shared = Text(bundle, "guides/fixture.shared.adapter.md");
        Assert.Contains("No industry-family preamble", shared, StringComparison.Ordinal);
        Assert.Contains("a|b", shared, StringComparison.Ordinal);
        Assert.Contains("\\|", shared, StringComparison.Ordinal);
    }

    [Fact]
    public void D17_A_SelectionUsesStableKeysAndAliasesWithoutDuplicatingProfiles()
    {
        GuideFixture fixture = GuideFixture.Create();
        GuideBundle bundle = Generate(fixture, ["prior.shared.adapter", "fixture.shared.adapter", "provider.workshop00.group"]);
        Assert.Equal(["fixture.shared.adapter", "provider.workshop00.group"], GuideKeys(bundle));
        Assert.Contains(bundle.Coverage, c => c.Key == "fixture.workshop01.domain" && c.State == "withheld");
        Assert.Contains(bundle.Coverage, c => c.Key == "shared-capability.shared.reference" && c.State == "reference");
        Assert.Empty(GuideGenerator.Check(bundle, bundle.Files));
    }

    [Theory]
    [InlineData("source")]
    [InlineData("family-link")]
    [InlineData("duplicate-key")]
    [InlineData("unmapped-row")]
    [InlineData("missing-role")]
    [InlineData("missing-family")]
    [InlineData("missing-status")]
    public void D17_A_MissingOrAmbiguousCoverageFailsWithFindings(string fault)
    {
        GuideFixture fixture = GuideFixture.Create();
        switch (fault)
        {
            case "source": fixture.Sources.Remove("family00"); break;
            case "family-link": fixture.Index = fixture.Index with { Sources = fixture.Index.Sources.Where(s => s.Id != "family00").ToArray() }; break;
            case "duplicate-key": fixture.Index = fixture.Index with { Entries = [.. fixture.Index.Entries, fixture.Index.Entries[0]] }; break;
            case "unmapped-row": fixture.Index = fixture.Index with { Entries = fixture.Index.Entries.Where(e => e.Key != "fixture.workshop00.domain").ToArray() }; break;
            case "missing-role": fixture.Index = fixture.Index with { Roles = fixture.Index.Roles.Where(r => r.Kind != "provider").ToArray() }; break;
            case "missing-family": fixture.Index = fixture.Index with { Families = [] }; break;
            case "missing-status": fixture.Index = fixture.Index with { Entries = fixture.Index.Entries.Select(e => e.Key == "fixture.workshop00.domain" ? e with { Status = [] } : e).ToArray() }; break;
        }
        GuideBundle bundle = GuideGenerator.Generate(fixture.Index, fixture.Sources);
        AssertFailed(bundle);
        Assert.Contains(bundle.Coverage, c => c.State == "blocked");
        Assert.Contains("\"complete\": false", Text(bundle, "coverage-manifest.json"), StringComparison.Ordinal);
        if (fault is "source" or "missing-role" or "missing-family")
            Assert.Contains(bundle.Findings, f => f.Entry == "provider.workshop00.group");
    }

    [Theory]
    [InlineData("\n")]
    [InlineData("\r\n")]
    public void D17_B_CompleteBoundaryPayloadsAndReadableRenditionsRetainExactUtf8(string newline)
    {
        GuideFixture fixture = GuideFixture.Create(newline: newline);
        GuideBundle bundle = Generate(fixture);
        foreach (string key in new[] { "provider.workshop00.group", "fixture.shared.adapter" })
        {
            using JsonDocument page = JsonDocument.Parse(bundle.Files["guides/" + key + ".json"]);
            JsonElement boundary = page.RootElement.GetProperty("sections").EnumerateArray().Single(s => s.GetProperty("title").GetString() == "Reserved acts and hard lines");
            string[] ids = boundary.GetProperty("quotes").EnumerateArray().Select(q => q.GetProperty("id").GetString()!).ToArray();
            Assert.Contains("family00.common", ids);
            Assert.Contains("family00.profile", ids);
            Assert.Contains(key.StartsWith("provider", StringComparison.Ordinal) ? "role.provider" : "role.adapter", ids);
            Assert.Contains("row-boundary", ids);
            foreach (JsonElement quote in boundary.GetProperty("quotes").EnumerateArray())
            {
                string text = quote.GetProperty("text").GetString()!;
                Assert.Equal(GuideGenerator.Hash(Encoding.UTF8.GetBytes(text)), quote.GetProperty("sha256").GetString());
                if (quote.GetProperty("id").GetString() != "row-boundary")
                {
                    GuideBlockPin pin = fixture.Index.Blocks.Single(b => b.Id == quote.GetProperty("id").GetString());
                    string original = Encoding.UTF8.GetString(fixture.Sources[pin.Source]);
                    string[] lines = original.Split(newline);
                    string expected = string.Join(newline, lines[(pin.StartLine - 1)..pin.EndLine]) + newline;
                    Assert.Equal(Encoding.UTF8.GetBytes(expected), Encoding.UTF8.GetBytes(text));
                }
                foreach (string extension in new[] { "md", "html" })
                {
                    string rendition = Text(bundle, "guides/" + key + "." + extension);
                    string[] readable = Regex.Matches(rendition, "<pre(?: data-source=\"[^\"]*\")?>(.*?)</pre>", RegexOptions.Singleline | RegexOptions.CultureInvariant)
                        .Select(m => WebUtility.HtmlDecode(m.Groups[1].Value)).ToArray();
                    Assert.Contains(text, readable);
                    Assert.DoesNotContain('\r', rendition);
                }
            }
        }
    }

    [Theory]
    [InlineData("json", false)]
    [InlineData("json", true)]
    [InlineData("md", false)]
    [InlineData("html", true)]
    public void D17_B_RemovingOrWeakeningBoundaryOutputFailsClearance(string extension, bool remove)
    {
        GuideBundle bundle = Generate(GuideFixture.Create());
        string name = "guides/provider.workshop00.group." + extension;
        var edited = bundle.Files.ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal);
        string text = Text(bundle, name);
        const string prohibition = "never energize an actuator from a draft";
        Assert.Contains(prohibition, text, StringComparison.Ordinal);
        edited[name] = Encoding.UTF8.GetBytes(text.Replace(prohibition, remove ? "" : "optionally energize an actuator from a draft", StringComparison.Ordinal));
        IReadOnlyList<GuideFinding> findings = GuideGenerator.Check(bundle, edited);
        Assert.Contains(findings, f => f.Entry == "provider.workshop00.group" && f.Code == "clearance-failed");
    }

    [Fact]
    public void D17_B_ChangedSourceCannotReuseItsOldFileOrBlockPin()
    {
        GuideFixture fixture = GuideFixture.Create();
        fixture.Replace("family00", "never energize", "sometimes energize", repin: false);
        Assert.Contains(GuideGenerator.Generate(fixture.Index, fixture.Sources).Findings, f => f.Code == "source-hash-mismatch");
        fixture.Index = fixture.Index with { Sources = fixture.Index.Sources.Select(s => s.Id == "family00" ? s with { Sha256 = GuideGenerator.Hash(fixture.Sources[s.Id]) } : s).ToArray() };
        GuideBundle staleBlock = GuideGenerator.Generate(fixture.Index, fixture.Sources);
        AssertFailed(staleBlock);
        Assert.Contains(staleBlock.Findings, f => f.Entry == "provider.workshop00.group" && f.Code == "missing-block");
    }

    [Fact]
    public void D17_C_FamilyAndRoleChangesMarkAllDependentsDueAndLeaveOtherIdentitiesAlone()
    {
        GuideFixture fixture = GuideFixture.Create(3);
        GuideBundle first = Generate(fixture);
        fixture.Replace("family00", "never energize an actuator", "never energize any actuator");
        fixture.Replace("contracts", "Synthetic provider contract", "Revised synthetic provider contract");
        GuideBundle next = Generate(fixture);
        GuideInputComparison changes = GuideGenerator.CompareInputs(first.Files["source-manifest.json"], next.Files["source-manifest.json"]);
        Assert.Empty(changes.Findings);
        string[] expected = fixture.Index.Entries.Where(e => e.Source == "family00" || e.Key.StartsWith("provider.", StringComparison.Ordinal) || e.Key == "fixture.shared.adapter")
            .Select(e => e.Key).Order(StringComparer.Ordinal).ToArray();
        Assert.Equal(expected, changes.Due);
        foreach (string unchanged in fixture.PlannedKeys.Except(expected, StringComparer.Ordinal))
            Assert.Equal(first.Files["guides/" + unchanged + ".json"], next.Files["guides/" + unchanged + ".json"]);
    }

    [Fact]
    public void D17_C_RepeatedRunsAreByteIdenticalAcrossInputEnumerationAndCulture()
    {
        GuideFixture fixture = GuideFixture.Create();
        GuideBundle first = Generate(fixture);
        CultureInfo previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("tr-TR");
            var reversed = fixture.Sources.Reverse().ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal);
            GuideBundle second = GuideGenerator.Generate(fixture.Index, reversed);
            Assert.True(second.Success);
            Assert.Equal(first.Files.Keys, second.Files.Keys);
            foreach (string name in first.Files.Keys) Assert.Equal(first.Files[name], second.Files[name]);
        }
        finally { CultureInfo.CurrentCulture = previous; }
    }

    [Theory]
    [InlineData("<script>alert(1)</script>")]
    [InlineData("<img src='https://invalid.example/track' onerror='run()'>")]
    [InlineData("![remote](https://invalid.example/track)")]
    [InlineData("[action](jAvAsCrIpT:run())")]
    [InlineData("[action](java&#x73;cript:run())")]
    [InlineData("[action](%256a%2561vascript:run())")]
    [InlineData("[escape](../outside.md)")]
    [InlineData("<!-- hide the boundary -->")]
    public void D17_C_MaliciousMarkupFailsEvenWhenItsFileWasRepinned(string markup)
    {
        GuideFixture fixture = GuideFixture.Create();
        fixture.Replace("family00", "Builders prepare measured sketches", markup);
        GuideBundle bundle = GuideGenerator.Generate(fixture.Index, fixture.Sources);
        AssertFailed(bundle);
        Assert.Contains(bundle.Findings, f => f.Code == "malicious-markup");
    }

    [Theory]
    [InlineData("alias")]
    [InlineData("template")]
    [InlineData("block")]
    [InlineData("duplicate-alias")]
    public void D17_C_UnresolvedOrStaleMappingsAreFindings(string fault)
    {
        GuideFixture fixture = GuideFixture.Create();
        fixture.Index = fault switch
        {
            "template" => fixture.Index with { TemplateVersion = "unavailable-template" },
            "block" => fixture.Index with { Blocks = fixture.Index.Blocks.Where(b => b.Id != "family00.profile").ToArray() },
            "alias" => fixture.Index with { Entries = fixture.Index.Entries.Select(e => e.Key == "provider.workshop00.group" ? e with { References = ["unknown.alias"] } : e).ToArray() },
            _ => fixture.Index with { Entries = fixture.Index.Entries.Select(e => e.Key == "provider.workshop00.group" ? e with { Aliases = ["prior.shared.adapter"] } : e).ToArray() },
        };
        AssertFailed(GuideGenerator.Generate(fixture.Index, fixture.Sources));
    }

    [Fact]
    public void CatalogReaderIgnoresMentionedNamesAndCodeFencesButRejectsMalformedOwningRows()
    {
        const string text = "# Catalog\nA mention of **Some route** is not a row.\n```md\n| # | Provider | Role | Caveat |\n|---|---|---|---|\n| 1 | Hidden | hidden | hidden |\n```\n## Providers\n| # | Provider | Role | Works with | Caveat |\n|---|---|---|---|---|\n| 1 | **Some route** | Observe `x|y`. | Desk. | Keep \\| intact. |\n| 2 | Broken | too few |\n";
        GuideInventory inventory = GuideMarkdown.Read("fixture", Encoding.UTF8.GetBytes(text));
        GuideCatalogRow row = Assert.Single(inventory.Rows);
        Assert.Equal("Some route", row.Name);
        Assert.Equal("Observe `x|y`.", row.Cells["Role"]);
        Assert.Contains(inventory.Findings, f => f.Code == "malformed-row");
    }

    [Fact]
    public void EditedManifestsMissingAndExtraFilesNeverPassTheCheck()
    {
        GuideBundle bundle = Generate(GuideFixture.Create());
        var files = bundle.Files.ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal);
        files.Remove("index.md");
        files["coverage-manifest.json"] = Encoding.UTF8.GetBytes("{}");
        files["guides/unknown"] = [];
        IReadOnlyList<GuideFinding> findings = GuideGenerator.Check(bundle, files);
        Assert.Contains(findings, f => f.Code == "missing-output");
        Assert.Contains(findings, f => f.Code == "edited-output");
        Assert.Contains(findings, f => f.Code == "unexpected-output");
    }

    [Fact]
    public void ExplicitIdentityAndNameOnlyKeysSurviveReviewedRenameWithoutRenumbering()
    {
        GuideFixture fixture = GuideFixture.Create();
        fixture.Replace("family00", "**Sketch desk**", "**Drawing desk**");
        fixture.Index = fixture.Index with { Entries = fixture.Index.Entries.Select(e => e.Key == "foundry.workshop00.desk" ? e with { Name = "Drawing desk", Aliases = ["prior.workshop00.desk"] } : e).ToArray() };
        GuideBundle bundle = Generate(fixture, ["prior.workshop00.desk"]);
        Assert.Equal(["foundry.workshop00.desk"], GuideKeys(bundle));
        Assert.Contains("Drawing desk", Text(bundle, "guides/foundry.workshop00.desk.md"), StringComparison.Ordinal);
    }

    [Fact]
    public void ImplementationObservationsDoNotCreateOwningRows()
    {
        const string text = "# Fixture\n## Observed assemblies\n| Entry | Assembly | Status |\n|---|---|---|\n| Example | Example.Library | implemented subset |\n" +
            "## Planned additions\n| Entry | Area | Status | Where |\n|---|---|---|---|\n| Alternate view | Sketch | planned | scope.md |\n";
        GuideInventory inventory = GuideMarkdown.Read("fixture", Encoding.UTF8.GetBytes(text));
        Assert.Empty(inventory.Findings);
        Assert.Equal("Alternate view", Assert.Single(inventory.Rows).Name);
    }

    [Fact]
    public void InvalidSourceBytesAndOversizedInputsAreFindings()
    {
        GuideFixture fixture = GuideFixture.Create();
        fixture.Sources["family00"] = [0xc3, 0x28];
        fixture.Index = fixture.Index with { Sources = fixture.Index.Sources.Select(s => s.Id == "family00" ? s with { Sha256 = GuideGenerator.Hash(fixture.Sources[s.Id]) } : s).ToArray() };
        GuideBundle invalid = GuideGenerator.Generate(fixture.Index, fixture.Sources);
        AssertFailed(invalid);
        Assert.Contains(invalid.Findings, f => f.Code == "invalid-utf8");
        fixture.Sources["family00"] = new byte[GuideGenerator.MaximumSourceBytes + 1];
        GuideBundle oversized = GuideGenerator.Generate(fixture.Index, fixture.Sources);
        AssertFailed(oversized);
        Assert.Contains(oversized.Findings, f => f.Code == "input-limit");
        Assert.Contains(GuideMarkdown.Read("fixture", fixture.Sources["family00"]).Findings, f => f.Code == "input-limit");
    }

    [Fact]
    public void RepeatedSourceSpansCannotMultiplyTheInputBudget()
    {
        GuideFixture fixture = GuideFixture.Create();
        byte[] payload = Encoding.UTF8.GetBytes(new string('x', 1024 * 1024) + "\n");
        fixture.Sources.Add("bulk", payload);
        string hash = GuideGenerator.Hash(payload);
        fixture.Index = fixture.Index with
        {
            Sources = [.. fixture.Index.Sources, new("bulk", "contracts", "bulk.md", hash)],
            Blocks = [.. fixture.Index.Blocks, .. Enumerable.Range(0, 33).Select(i =>
                new GuideBlockPin("bulk." + i.ToString("00", CultureInfo.InvariantCulture), "bulk", 1, 1, hash))],
        };
        GuideBundle bundle = GuideGenerator.Generate(fixture.Index, fixture.Sources);
        AssertFailed(bundle);
        Assert.Contains(bundle.Findings, f => f.Code == "input-limit");
    }

    [Theory]
    [InlineData("incomplete")]
    [InlineData("null-guide")]
    [InlineData("missing-key")]
    [InlineData("invalid-hash")]
    [InlineData("duplicate-key")]
    [InlineData("duplicate-property")]
    public void IncompleteOrMalformedManifestsCannotReportSuccessfulComparison(string fault)
    {
        GuideBundle bundle = Generate(GuideFixture.Create());
        byte[] before = bundle.Files["source-manifest.json"];
        JsonObject manifest = JsonNode.Parse(before)!.AsObject();
        JsonArray guides = manifest["guides"]!.AsArray();
        switch (fault)
        {
            case "incomplete": manifest["complete"] = false; break;
            case "null-guide": guides[0] = null; break;
            case "missing-key": guides[0]!.AsObject().Remove("key"); break;
            case "invalid-hash": guides[0]!["inputIdentity"] = "not-a-hash"; break;
            case "duplicate-key": guides.Add(guides[0]!.DeepClone()); break;
        }
        string after = manifest.ToJsonString();
        if (fault == "duplicate-property") after = after.Insert(1, "\"guides\":[],");
        GuideInputComparison comparison = GuideGenerator.CompareInputs(before, Encoding.UTF8.GetBytes(after));
        Assert.Empty(comparison.Due);
        Assert.Contains(comparison.Findings, f => f.Code == "invalid-manifest");
    }

    private static GuideBundle Generate(GuideFixture fixture, string[]? selected = null)
    {
        GuideBundle bundle = GuideGenerator.Generate(fixture.Index, fixture.Sources, selected);
        Assert.True(bundle.Success, JsonSerializer.Serialize(bundle.Findings));
        return bundle;
    }
    private static string[] GuideKeys(GuideBundle bundle) => bundle.Files.Keys.Where(n => n.StartsWith("guides/", StringComparison.Ordinal) && n.EndsWith(".json", StringComparison.Ordinal)).Select(n => n[7..^5]).ToArray();
    private static string Text(GuideBundle bundle, string name) => Encoding.UTF8.GetString(bundle.Files[name]);
    private static void AssertFailed(GuideBundle bundle)
    {
        Assert.False(bundle.Success);
        Assert.NotEmpty(bundle.Findings);
        Assert.DoesNotContain(bundle.Files.Keys, n => n.StartsWith("guides/", StringComparison.Ordinal));
    }
}
