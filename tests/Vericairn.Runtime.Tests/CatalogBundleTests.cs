// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using Vericairn.Runtime.Guides;

namespace Vericairn.Runtime.Tests;

public sealed class CatalogBundleTests
{
    [Fact]
    public void RegenerationPreservesEveryByteAcrossEnumerationAndCulture()
    {
        var fixture = new CatalogFixture();
        CatalogBundle first = fixture.Generate(new CatalogTrackingIssue("fixture.shared.adapter", 17));
        Assert.True(first.Success, string.Join("; ", first.Findings));
        CultureInfo previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("tr-TR");
            CatalogBundle second = CatalogExporter.Generate(fixture.Guides.Index,
                fixture.Guides.Sources.Reverse().ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal),
                fixture.Plan with { Entries = fixture.Plan.Entries.Reverse().ToArray(), Allowlist = fixture.Plan.Allowlist.Reverse().ToArray() },
                [new("fixture.shared.adapter", 17)]);
            Assert.True(second.Success, string.Join("; ", second.Findings));
            Assert.Equal(first.Files.Keys, second.Files.Keys);
            foreach ((string path, byte[] bytes) in first.Files)
            {
                Assert.Equal(bytes, second.Files[path]);
                Assert.False(bytes.AsSpan().StartsWith(Encoding.UTF8.Preamble));
                Assert.Equal((byte)'\n', bytes[^1]);
            }
        }
        finally { CultureInfo.CurrentCulture = previous; }
    }

    [Fact]
    public void CleanLayoutContainsOnlyClearedRenditionsAndVerifiedPublicIdentities()
    {
        var fixture = new CatalogFixture();
        fixture.Plan = fixture.Plan with { Entries = ["provider.workshop00.group", "fixture.shared.adapter"] };
        CatalogBundle bundle = fixture.Generate();
        Assert.True(bundle.Success, string.Join("; ", bundle.Findings));
        Assert.Equal(fixture.Plan.Entries.Order(StringComparer.Ordinal), bundle.Inventory.Entries.Select(e => e.Key));
        foreach (string file in new[] { "README.md", "LICENSE.md", "NOTICE.md", "CONTRIBUTING.md", "templates/README.md", "templates/role-recipes.json",
            "sources/catalog/entries.json", "sources/catalog/blocks.json", "sources/contracts/blocks.json", "sources/catalog/retirements.json",
            "indexes/entries.json", "indexes/entries.md", "indexes/families.md", "indexes/roles.md", "indexes/tracking-issues.json",
            "manifests/inputs.json", "manifests/outputs.json", ".github/ISSUE_TEMPLATE/request-implementation.yml" })
            Assert.Contains(file, bundle.Files.Keys);
        Assert.Equal(20, bundle.Files.Count);
        Assert.Empty(JsonSerializer.Deserialize<CatalogTrackingIssue[]>(bundle.Files["indexes/tracking-issues.json"], GuideJson.Options)!);
        string all = string.Join('\n', bundle.Files.Select(f => f.Key + "\n" + Encoding.UTF8.GetString(f.Value)));
        Assert.DoesNotContain("Workshop 01", all, StringComparison.Ordinal);
        Assert.DoesNotContain("fixture.shared.available", all, StringComparison.Ordinal);
        Assert.DoesNotContain("coverage-manifest", all, StringComparison.Ordinal);
        foreach (string identity in fixture.Guides.Index.Sources.Select(s => s.Sha256).Append(fixture.Plan.IndexIdentity))
            Assert.DoesNotContain(identity, all, StringComparison.Ordinal);
        Assert.Contains("CC BY 4.0", all, StringComparison.Ordinal);
        Assert.Contains("Apache-2.0", all, StringComparison.Ordinal);
        Assert.Contains("unavailable until", all, StringComparison.Ordinal);
        Assert.Empty(CatalogDisclosure.Check(bundle.Files));
        using JsonDocument outputs = JsonDocument.Parse(bundle.Files["manifests/outputs.json"]);
        JsonElement[] identities = outputs.RootElement.GetProperty("files").EnumerateArray().ToArray();
        Assert.Equal(bundle.Files.Keys.Where(k => k != "manifests/outputs.json"), identities.Select(i => i.GetProperty("path").GetString()));
        foreach (JsonElement identity in identities)
            Assert.Equal(GuideGenerator.Hash(bundle.Files[identity.GetProperty("path").GetString()!]), identity.GetProperty("sha256").GetString());
    }

    [Theory]
    [InlineData("extra-source")]
    [InlineData("missing-allowlist")]
    [InlineData("changed-pin")]
    [InlineData("changed-index")]
    [InlineData("withheld")]
    [InlineData("unknown")]
    [InlineData("alias")]
    [InlineData("missing-dependency")]
    public void UnreviewedOrUnresolvedSelectionsEmitNoPublicFiles(string fault)
    {
        var fixture = new CatalogFixture();
        switch (fault)
        {
            case "extra-source": fixture.Guides.Sources.Add("unlisted", Encoding.UTF8.GetBytes("Not cleared.")); break;
            case "missing-allowlist": fixture.Plan = fixture.Plan with { Allowlist = fixture.Plan.Allowlist.Skip(1).ToArray() }; break;
            case "changed-pin": fixture.Guides.Replace("family00", "read-only", "observe-only", repin: false); break;
            case "changed-index": fixture.Guides.Index = fixture.Guides.Index with { TemplateVersion = "different" }; break;
            case "withheld":
                fixture.Guides.Index = fixture.Guides.Index with { Entries = fixture.Guides.Index.Entries.Select(e => e.Key == "fixture.shared.adapter" ? e with { State = "withheld", Explanation = "Uncleared." } : e).ToArray() };
                fixture.Plan = fixture.Plan with { IndexIdentity = GuideGenerator.Hash(CatalogFixture.Json(fixture.Guides.Index)), Entries = ["fixture.shared.adapter"] }; break;
            case "unknown": fixture.Plan = fixture.Plan with { Entries = ["fixture.unknown"] }; break;
            case "alias": fixture.Plan = fixture.Plan with { Entries = ["prior.shared.adapter"] }; break;
            case "missing-dependency": fixture.Plan = fixture.Plan with { Entries = ["provider.workshop00.group"] }; break;
        }
        CatalogBundle bundle = fixture.Generate();
        Assert.False(bundle.Success);
        Assert.NotEmpty(bundle.Findings);
        Assert.Empty(bundle.Files);
        Assert.Empty(bundle.Inventory.Entries);
    }

    [Fact]
    public void AllGuideSectionsAndCompleteBoundaryQuotationsSurvivePublicRendition()
    {
        var fixture = new CatalogFixture();
        CatalogBundle bundle = fixture.Generate();
        Assert.True(bundle.Success, string.Join("; ", bundle.Findings));
        GuideBundle original = GuideGenerator.Generate(fixture.Guides.Index, fixture.Guides.Sources);
        foreach (CatalogEntry entry in bundle.Inventory.Entries)
        {
            string guide = WebUtility.HtmlDecode(CatalogFixture.Text(bundle, entry.Guide));
            using JsonDocument page = JsonDocument.Parse(original.Files["guides/" + entry.Key + ".json"]);
            JsonElement[] sections = page.RootElement.GetProperty("sections").EnumerateArray().ToArray();
            Assert.Equal(13, sections.Length);
            foreach (JsonElement section in sections)
            {
                Assert.Contains("## " + section.GetProperty("title").GetString(), guide, StringComparison.Ordinal);
                foreach (JsonElement quote in section.GetProperty("quotes").EnumerateArray())
                    Assert.Contains(quote.GetProperty("text").GetString()!, guide, StringComparison.Ordinal);
            }
            Assert.DoesNotContain("Input identity:", guide, StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData("retired")]
    [InlineData("available")]
    public void RetirementRetainsTheSameAddressAndClosesOnlyCoveredScope(string state)
    {
        var fixture = new CatalogFixture();
        CatalogBundle before = fixture.Generate();
        fixture.Guides.Index = fixture.Guides.Index with { Entries = fixture.Guides.Index.Entries.Select(e =>
            e.Key == "fixture.shared.adapter" ? e with { State = state, Explanation = "Reviewed replacement; migration unassessed." } : e).ToArray() };
        fixture.Plan = fixture.Plan with
        {
            IndexIdentity = GuideGenerator.Hash(CatalogFixture.Json(fixture.Guides.Index)),
            Retirements = [new("fixture.shared.adapter", "2026-01-02", "public-2", "https://example.org/replacement", "No automatic migration is supported. Unknown builds are unassessed.")],
        };
        CatalogBundle after = fixture.Generate();
        Assert.True(after.Success, string.Join("; ", after.Findings));
        string address = Assert.Single(before.Inventory.Entries, e => e.Key == "fixture.shared.adapter").Guide;
        CatalogEntry retired = Assert.Single(after.Inventory.Entries, e => e.Key == "fixture.shared.adapter");
        Assert.Equal(address, retired.Guide);
        Assert.False(retired.Requestable);
        string text = CatalogFixture.Text(after, address);
        foreach (string required in new[] { "retired guide", "2026-01-02", "public-2", "https://example.org/replacement", "No automatic migration", "History and community" })
            Assert.Contains(required, text, StringComparison.Ordinal);
        Assert.True(Assert.Single(after.Inventory.Entries, e => e.State == "remainder").Requestable);
        Assert.False(CatalogRequests.Validate(after.Inventory, new(retired.Key, true)).Accepted);
        Assert.Equal(address, CatalogRequests.Validate(after.Inventory, new(retired.Key, true)).Reference);
        fixture.Plan = fixture.Plan with { Retirements = [] };
        Assert.False(fixture.Generate().Success);
    }
}
