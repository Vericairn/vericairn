// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Vericairn.Runtime.Guides;

namespace Vericairn.Runtime.Tests;

public sealed class GuideTemplateTests
{
    [Theory]
    [InlineData("\n")]
    [InlineData("\r\n")]
    public void EveryGuideQuotesItsRoleAndRowBoundaryOnceInFull(string newline)
    {
        GuideFixture fixture = GuideFixture.Create(newline: newline);
        GuideBundle bundle = Generate(fixture);
        foreach (string key in fixture.PlannedKeys)
        {
            using JsonDocument document = JsonDocument.Parse(bundle.Files["guides/" + key + ".json"]);
            JsonElement[] sections = document.RootElement.GetProperty("sections").EnumerateArray().ToArray();
            JsonElement[] quotes = sections.SelectMany(s => s.GetProperty("quotes").EnumerateArray()).ToArray();
            Assert.Equal(quotes.Length, quotes.Select(q => q.GetProperty("id").GetString()).Distinct(StringComparer.Ordinal).Count());
            JsonElement boundary = sections.Single(s => s.GetProperty("title").GetString() == "Reserved acts and hard lines");
            JsonElement roleSection = sections.Single(s => s.GetProperty("title").GetString() == "Role and responsibility");
            Assert.Empty(roleSection.GetProperty("quotes").EnumerateArray());
            Assert.Contains("Reserved acts and hard lines", roleSection.GetProperty("text").GetString(), StringComparison.Ordinal);
            string kind = document.RootElement.GetProperty("kind").GetString()!;
            foreach (string id in fixture.Index.Roles.Single(r => r.Kind == kind).Blocks)
            {
                JsonElement role = Assert.Single(boundary.GetProperty("quotes").EnumerateArray(), q => q.GetProperty("id").GetString() == id);
                Assert.Contains(id, roleSection.GetProperty("text").GetString(), StringComparison.Ordinal);
                GuideBlockPin pin = fixture.Index.Blocks.Single(b => b.Id == id);
                string[] lines = Encoding.UTF8.GetString(fixture.Sources[pin.Source]).Split(newline);
                string expected = string.Join(newline, lines[(pin.StartLine - 1)..pin.EndLine]) + newline;
                Assert.Equal(Encoding.UTF8.GetBytes(expected), Encoding.UTF8.GetBytes(role.GetProperty("text").GetString()!));
            }
            foreach (JsonElement quote in boundary.GetProperty("quotes").EnumerateArray())
            {
                string payload = quote.GetProperty("text").GetString()!;
                Assert.Equal(GuideGenerator.Hash(Encoding.UTF8.GetBytes(payload)), quote.GetProperty("sha256").GetString());
                Assert.Single(quotes, q => q.GetProperty("text").GetString() == payload);
                foreach (string extension in new[] { "md", "html" })
                {
                    string rendition = Encoding.UTF8.GetString(bundle.Files["guides/" + key + "." + extension]);
                    string[] readable = Regex.Matches(rendition, "<pre data-source=\"[^\"]*\">(.*?)</pre>", RegexOptions.Singleline | RegexOptions.CultureInvariant)
                        .Select(m => WebUtility.HtmlDecode(m.Groups[1].Value)).ToArray();
                    Assert.Single(readable, text => text == payload);
                }
            }
        }
    }

    [Fact]
    public void BlocksSharedByReadinessContextAndBoundariesHaveOneFullQuotation()
    {
        GuideFixture fixture = GuideFixture.Create();
        const string key = "fixture.shared.adapter";
        fixture.Index = fixture.Index with
        {
            Entries = fixture.Index.Entries.Select(e => e.Key == key ? e with
            {
                Status = ["shared.status", "family00.common"],
                Context = ["family00.common", "shared.status", "role.adapter"],
                Boundaries = [.. e.Boundaries, "family00.common", "role.adapter"],
            } : e).ToArray(),
        };
        GuideBundle bundle = Generate(fixture, [key]);
        using JsonDocument document = JsonDocument.Parse(bundle.Files["guides/" + key + ".json"]);
        JsonElement[] sections = document.RootElement.GetProperty("sections").EnumerateArray().ToArray();
        JsonElement[] quotes = sections.SelectMany(s => s.GetProperty("quotes").EnumerateArray()).ToArray();
        foreach (string id in new[] { "family00.common", "shared.status", "role.adapter", "row-boundary" })
            Assert.Single(quotes, q => q.GetProperty("id").GetString() == id);
        string identity = sections[0].GetProperty("text").GetString()!;
        Assert.Contains("Reserved acts and hard lines\": family00.common", identity, StringComparison.Ordinal);
        string context = sections.Single(s => s.GetProperty("title").GetString() == "Collaborators, prerequisites and edition").GetProperty("text").GetString()!;
        Assert.Contains("Identity, readiness and scope\": shared.status", context, StringComparison.Ordinal);
        Assert.Contains("Reserved acts and hard lines\": family00.common, role.adapter", context, StringComparison.Ordinal);
    }

    [Fact]
    public void AKindQualifiedFoundryShowsItsExplicitIdentifierAliasInIdentity()
    {
        GuideFixture fixture = GuideFixture.Create();
        const string key = "foundry.workshop00.desk";
        const string alias = "fixture.workshop00.desk";
        fixture.Replace("family00", "**Sketch desk**", "`" + alias + "`");
        fixture.Index = fixture.Index with
        {
            Entries = fixture.Index.Entries.Select(e => e.Key == key ? e with
            {
                Name = alias,
                Aliases = ["prior.workshop00.desk", alias],
            } : e).ToArray(),
        };
        GuideBundle bundle = Generate(fixture, [alias]);
        using JsonDocument document = JsonDocument.Parse(bundle.Files["guides/" + key + ".json"]);
        Assert.Equal(key, document.RootElement.GetProperty("key").GetString());
        string identity = document.RootElement.GetProperty("sections")[0].GetProperty("text").GetString()!;
        Assert.Contains("Catalog target: " + key, identity, StringComparison.Ordinal);
        Assert.Contains("Catalog aliases: " + alias + ", prior.workshop00.desk.", identity, StringComparison.Ordinal);
        foreach (string extension in new[] { "md", "html" })
            Assert.Contains("Catalog aliases: " + alias, Encoding.UTF8.GetString(bundle.Files["guides/" + key + "." + extension]), StringComparison.Ordinal);
        fixture.Index = fixture.Index with { Entries = fixture.Index.Entries.Select(e => e with { Aliases = e.Aliases.Reverse().ToArray() }).ToArray() };
        GuideBundle reordered = Generate(fixture, [key]);
        Assert.Equal(bundle.Files["guides/" + key + ".json"], reordered.Files["guides/" + key + ".json"]);
    }

    [Fact]
    public void OversizedSelectionsRefuseWithoutPartialGuidesAndAnExactBatchFits()
    {
        GuideFixture fixture = GuideFixture.Create(15);
        fixture.Replace("contracts", "Synthetic provider contract:", "Synthetic provider contract: " + new string('x', 3 * 1024 * 1024));
        GuideBundle full = GuideGenerator.Generate(fixture.Index, fixture.Sources);
        Assert.False(full.Success);
        Assert.Contains(full.Findings, f => f.Code == "output-limit");
        Assert.DoesNotContain(full.Files.Keys, name => name.StartsWith("guides/", StringComparison.Ordinal));
        const string key = "provider.workshop00.group";
        GuideBundle batch = Generate(fixture, [key]);
        Assert.Equal(fixture.Index.Entries.Length, batch.Coverage.Count);
        Assert.Single(batch.Coverage, c => c.State == "active-guide" && c.Key == key);
        Assert.Contains(batch.Coverage, c => c.State == "withheld" && c.Key == "provider.workshop01.group");
        Assert.True(batch.Files.Where(f => f.Key.StartsWith("guides/", StringComparison.Ordinal)).Sum(f => f.Value.LongLength) <= GuideGenerator.MaximumOutputBytes);
        Assert.Empty(GuideGenerator.Check(batch, batch.Files));
    }

    private static GuideBundle Generate(GuideFixture fixture, string[]? selection = null)
    {
        GuideBundle bundle = GuideGenerator.Generate(fixture.Index, fixture.Sources, selection);
        Assert.True(bundle.Success, JsonSerializer.Serialize(bundle.Findings));
        return bundle;
    }
}
