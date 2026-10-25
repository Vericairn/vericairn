// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using System.Text.Json.Nodes;
using Vericairn.Records.Estate;
using Vericairn.Records.Templates;
using static Vericairn.Records.Tests.TemplateFixture;

namespace Vericairn.Records.Tests;

public sealed class TemplateFieldTests
{
    [Theory]
    [InlineData("number", "-12.50", true)]
    [InlineData("number", "1,2,3", false)]
    [InlineData("date", "2028-02-29", true)]
    [InlineData("date", "2027-02-29", false)]
    [InlineData("list", "- one\n- two\n", true)]
    [InlineData("list", "one\ntwo\n", false)]
    [InlineData("table", "| Name | Value |\n| :--- | ---: |\n| One | Two |\n", true)]
    [InlineData("table", "| Name | Value |\n| A line | Not a delimiter |\n", false)]
    [InlineData("table", "| Name | Value |\n| --- | --- |\n| Missing |\n", false)]
    public void TypedFieldsCheckTheRepresentationTheyDeclare(string type, string content, bool passes)
    {
        using var estate = new TestEstate(3);
        JsonObject parent = Section("parent"); JsonObject field = Section("value"); field["type"] = type;
        parent["fields"] = new JsonArray(field);
        TemplateDefinition definition = Install(estate, Contract(parent));
        var declaration = Declare(estate, definition);
        string text = Fill(TemplateSections.Skeleton(definition), definition, "value", content);
        Revision(estate, "docs/example.md", text);
        TemplateSectionState state = TemplateDocuments.Sections(estate.Load(), declaration).Single(s => s.Id == "value");
        Assert.Equal(passes ? "passed" : "failed", Assert.Single(state.Checks, c => c.Id == "field-type").Outcome);
        Assert.Equal(passes ? "drafted" : "failing", state.State);
    }

    [Fact]
    public void ReferenceCohesionChecksActualTargetsAndMissingTargetsCannotBeClearedByJudgment()
    {
        using var estate = new TestEstate(3);
        JsonObject section = Section("references");
        section["cohesion"] = new JsonArray(new JsonObject { ["id"] = "targets", ["kind"] = "references-exist", ["summary"] = "Every listed path exists." });
        TemplateDefinition definition = Install(estate, Contract(section));
        var declaration = Declare(estate, definition);
        estate.WriteFile("docs/target.md", "A target.\n");
        string text = Fill(TemplateSections.Skeleton(definition), definition, "references", "- docs/target.md\n- docs/missing.md\n");
        Revision(estate, "docs/example.md", text);
        Assert.Equal("failed", Assert.Single(Assert.Single(TemplateDocuments.Sections(estate.Load(), declaration)).Cohesion).Outcome);
        Assert.Throws<InvalidOperationException>(() => Review(estate, declaration, text, "references", judgments: [new("targets", "passed", "Claim a pass.", [])]));
        estate.WriteFile("docs/missing.md", "The other target.\n");
        Assert.Equal("passed", Assert.Single(Assert.Single(TemplateDocuments.Sections(estate.Load(), declaration)).Cohesion).Outcome);
        estate.Add("document-review", estate.OperatorId, Review(estate, declaration, text, "references").ToJsonString(), declaration.Reference);
        Assert.Equal("reviewed", Assert.Single(TemplateDocuments.Sections(estate.Load(), declaration)).State);
        File.Delete(Path.Combine(estate.Root, "docs", "target.md"));
        Assert.Equal("failing", Assert.Single(TemplateDocuments.Sections(estate.Load(), declaration)).State);
    }

    [Fact]
    public void ClosedAtxHeadingsAndAbsentGeneratedSectionsAreDistinctFromMalformedLocators()
    {
        JsonObject section = Section("answer"); section.Remove("marker"); section["heading"] = "Answer";
        TemplateDefinition definition = TemplateDefinition.Read(Element(Contract(section)));
        Assert.Equal("Exact.\r\n", TemplateSections.Read("## Answer ##\r\nExact.\r\n", definition.Elements)["answer"].Content);
        JsonElement generated = JsonDocument.Parse("""{"id":"index","synthesis":{"mode":"generated"}}""").RootElement.Clone();
        Assert.Empty(TemplateSections.Read("An absent optional section.\n", [generated]));
        Assert.Throws<InvalidOperationException>(() => TemplateSections.Read(GeneratedRegions.Start("index") + "\n", [generated]));
    }
}
