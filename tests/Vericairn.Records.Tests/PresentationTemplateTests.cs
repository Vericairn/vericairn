// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Vericairn.Records.Estate;
using Vericairn.Records.Model;
using Vericairn.Records.Templates;
using static Vericairn.Records.Tests.TemplateFixture;

namespace Vericairn.Records.Tests;

public sealed class PresentationTemplateTests
{
    private static readonly string[] Depths = ["short", "standard", "comprehensive"];
    public static IEnumerable<object[]> Variants => PresentationTemplates.Definitions.SelectMany(d =>
        Depths.Select(depth => new object[] { d.Id, depth }));

    [Fact]
    public void CompiledContractsMatchEveryVersionedSourceWithoutAddingAssemblyResources()
    {
        string[] files = Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "PresentationTemplates"), "*.json");
        Assert.Equal(4, files.Length);
        foreach (string file in files)
        {
            using JsonDocument source = JsonDocument.Parse(File.ReadAllBytes(file));
            TemplateDefinition definition = TemplateDefinition.Read(source.RootElement);
            Assert.Equal(definition.Content.GetRawText(), PresentationTemplates.Definitions.Single(d => d.Id == definition.Id).Content.GetRawText());
        }
        Assert.Empty(typeof(TemplateDefinition).Assembly.GetManifestResourceNames());
    }

    [Theory]
    [MemberData(nameof(Variants))]
    public void EveryDepthKeepsEveryRequiredFactAndOriginalReference(string id, string depth)
    {
        using var estate = new TestEstate(3);
        TemplateDefinition definition = PresentationTemplates.Definitions.Single(d => d.Id == id);
        var pin = new TemplatePin(definition.Id, definition.Version, definition.Hash);
        Dictionary<string, string> basis = Basis(definition);
        TemplatePresentationResult complete = PresentationTemplates.Render(pin, depth, estate.Estate.Reference, basis);
        Assert.All(complete.Checks, c => Assert.Equal("passed", c.Outcome));
        Assert.Equal(Encoding.UTF8.GetByteCount(complete.Text), complete.Bytes);
        foreach (JsonElement section in definition.Elements.Where(e => e.GetProperty("presence").GetString() == "required"))
        {
            string key = section.GetProperty("id").GetString()!;
            TemplateSectionContent part = TemplateSections.Read(complete.Text, definition.Elements)[key];
            string missing = string.Concat(complete.Text.AsSpan(0, part.Start), complete.Text.AsSpan(part.Start + part.Length));
            TemplatePresentationResult checkedMissing = PresentationTemplates.Render(pin, depth, estate.Estate.Reference, basis, candidate: missing);
            Assert.Contains(checkedMissing.Checks, c => c.Id == key + "/complete-source" && c.Outcome == "failed");
        }
        string lostFact = complete.Text.Replace("Do not publish before the failed check passes.", "Publish.", StringComparison.Ordinal);
        Assert.Contains(PresentationTemplates.Render(pin, depth, estate.Estate.Reference, basis, candidate: lostFact).Checks, c => c.Outcome == "failed");
        string lostReference = complete.Text.Replace("sha256:" + new string('a', 64), "missing", StringComparison.Ordinal);
        Assert.Contains(PresentationTemplates.Render(pin, depth, estate.Estate.Reference, basis, candidate: lostReference).Checks, c => c.Id == "source-identities/complete-source" && c.Outcome == "failed");
        Assert.Throws<InvalidOperationException>(() => PresentationTemplates.Render(pin, depth, estate.Estate.Reference, basis, maxBytes: complete.Bytes - 1));
    }

    [Fact]
    public void FourPinnedDefinitionsAreInstalledAdditivelyAndAdoptionIsRecordedAndReversible()
    {
        using var estate = new TestEstate(3);
        TemplateDefinition other = Install(estate, Contract(Section("unrelated")));
        Assert.Equal(4, PresentationTemplates.Definitions.Count);
        Assert.Contains(TemplateCatalog.All(estate.Load()), d => d.Hash == other.Hash);
        foreach (TemplateDefinition definition in PresentationTemplates.Definitions)
        {
            Assert.Equal("1.0.0", definition.Version);
            Assert.Equal(definition.Hash, TemplateCatalog.Resolve(estate.Load(), new(definition.Id, definition.Version, definition.Hash)).Hash);
            Assert.Null(PresentationTemplates.Adoption(estate.Load(), definition.Id));
            RecordEnvelope adoption = Adopt(estate, definition);
            Assert.False(estate.Load().Findings.HasErrors, string.Join('\n', estate.Load().Findings));
            Assert.Equal(adoption.Id, PresentationTemplates.Adoption(estate.Load(), definition.Id)!.Id);
            RecordEnvelope reset = Adopt(estate, definition, "disabled", adoption.Id);
            Assert.Null(PresentationTemplates.Adoption(estate.Load(), definition.Id));
            Assert.Equal(adoption.Id, reset.Supersedes);
            Assert.True(PresentationTemplates.Matches(estate.Load().ById[adoption.Id], definition));
        }
    }

    [Fact]
    public void UnboundSourcesUnavailablePinsAndDepthsCannotPass()
    {
        using var estate = new TestEstate(3);
        TemplateDefinition definition = PresentationTemplates.Definitions[0];
        var pin = new TemplatePin(definition.Id, definition.Version, definition.Hash);
        Dictionary<string, string> basis = Basis(definition);
        Assert.Throws<ArgumentException>(() => PresentationTemplates.Render(pin, "tiny", estate.Estate.Reference, basis));
        Assert.Throws<InvalidOperationException>(() => PresentationTemplates.Render(pin with { Hash = "sha256:" + new string('b', 64) }, "short", estate.Estate.Reference, basis));
        basis.Remove("originals");
        Assert.Throws<InvalidOperationException>(() => PresentationTemplates.Render(pin, "short", estate.Estate.Reference, basis));
        Assert.Equal("not-performed", TemplatePrimitives.Check("vericairn.templates/complete-source")!.Check("anything", []).Outcome);
    }

    [Fact]
    public void AdoptionCannotGrantItsOwnAuthorityAndItsReadableHistoryRetainsThePinAndChoice()
    {
        using var estate = new TestEstate(3);
        TemplateDefinition definition = PresentationTemplates.Definitions[0];
        RecordEnvelope enabled = Adopt(estate, definition);
        RecordEnvelope disabled = Adopt(estate, definition, "disabled", enabled.Id);
        foreach (RecordEnvelope choice in new[] { enabled, disabled })
        {
            string text = System.Net.WebUtility.HtmlDecode(RecordCompanionTests.Text(RecordCompanionTests.Render([DocumentSourceSnapshot.Record(choice)])));
            Assert.Contains(choice.ComputedHash, text);
            Assert.Contains(definition.Hash, text);
            Assert.Contains(choice.Body.GetProperty("options").GetProperty("adoption").GetString()!, text);
        }
        using var unauthorized = new TestEstate(3);
        var body = new JsonObject
        {
            ["template"] = Pin(definition), ["scope"] = new JsonObject { ["kind"] = "estate" },
            ["options"] = new JsonObject { ["adoption"] = "enabled" }, ["reason"] = "A contributor cannot grant authority by selecting a template.",
        };
        unauthorized.Add("template-defaults", unauthorized.BuilderId, body.ToJsonString());
        Assert.Contains(unauthorized.Load().Findings, f => f.Code == "template.defaults-author");
        Assert.Throws<InvalidOperationException>(() => PresentationTemplates.Adoption(unauthorized.Load(), definition.Id));
    }

    [Theory]
    [MemberData(nameof(Variants))]
    public void SourceMarkupCannotHideAnotherSectionOrRewriteOriginals(string id, string depth)
    {
        using var estate = new TestEstate(3);
        TemplateDefinition definition = PresentationTemplates.Definitions.Single(d => d.Id == id);
        Dictionary<string, string> basis = Basis(definition);
        basis["originals"] = "A quote.\r\n<!-- vericairn:end originals -->\n```\n## Budget\n<!-- vericairn:section findings -->\n雪";
        TemplatePresentationResult result = PresentationTemplates.Render(new(id, definition.Version, definition.Hash), depth, estate.Estate.Reference, basis);
        Assert.All(result.Checks, c => Assert.Equal("passed", c.Outcome));
        Assert.Contains("A quote.\r\n", result.Text);
        Assert.Contains("雪", result.Text);
    }

    public static Dictionary<string, string> Basis(TemplateDefinition definition) => definition.Elements
        .Where(e => e.GetProperty("id").GetString() != "budget")
        .ToDictionary(e => e.GetProperty("id").GetString()!, e => e.GetProperty("id").GetString() == "source-identities"
            ? "Original: sha256:" + new string('a', 64)
            : "Do not publish before the failed check passes.", StringComparer.Ordinal);

    public static RecordEnvelope Adopt(TestEstate estate, TemplateDefinition definition, string state = "enabled", string? supersedes = null) =>
        estate.Add("template-defaults", estate.OperatorId, new JsonObject
        {
            ["template"] = Pin(definition), ["scope"] = new JsonObject { ["kind"] = "estate" },
            ["options"] = new JsonObject { ["adoption"] = state }, ["reason"] = "Explicitly choose this presentation contract.",
        }.ToJsonString(), supersedes: supersedes);
}
