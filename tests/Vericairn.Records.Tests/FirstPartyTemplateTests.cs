// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using System.Text.Json.Nodes;
using Vericairn.Records.Estate;
using Vericairn.Records.Model;
using Vericairn.Records.Templates;
using static Vericairn.Records.Tests.TemplateFixture;

namespace Vericairn.Records.Tests;

public sealed class FirstPartyTemplateTests
{
    [Fact]
    public void AllSixContractsShipWithTheirVersionedGeneratorsAndChecks()
    {
        using var estate = new TestEstate(3);
        string[] names = ["design-document", "design-index", "open-decisions", "readme", "release-notes", "roadmap"];
        Assert.Equal(names.Select(n => "vericairn.estate/" + n), FirstPartyTemplates.Definitions.Select(d => d.Id).Order(StringComparer.Ordinal));
        foreach (TemplateDefinition definition in FirstPartyTemplates.Definitions)
        {
            Assert.Equal("1.0.0", definition.Version);
            Assert.Empty(TemplateDefinition.Validate(definition.Content));
            Assert.Contains(TemplateCatalog.All(estate.Load()), d => d.Hash == definition.Hash);
            RecordEnvelope declared = Declare(estate, definition, "docs/" + definition.Id.Split('/')[1] + ".md");
            string text = TemplateDocuments.NewContent(estate.Load(), declared);
            Assert.NotEmpty(text);
            foreach (JsonElement section in definition.Elements.Where(s => s.GetProperty("synthesis").GetProperty("mode").GetString() == "generated"))
            {
                string generator = section.GetProperty("synthesis").GetProperty("generator").GetString()!;
                Assert.EndsWith("-v1", generator, StringComparison.Ordinal);
                Assert.Equal(1, DocumentGenerators.Find(generator)!.Descriptor!.ContractVersion);
                TemplateSectionState state = TemplateDocuments.Sections(estate.Load(), declared, text: text).Single(s => s.Id == section.GetProperty("id").GetString());
                Assert.All(state.Checks.Concat(state.Cohesion), c => Assert.Equal("passed", c.Outcome));
            }
        }
        AssertValid(estate);
    }

    [Fact]
    public void RoadmapUsesTypedPlacementsAndRejectsMissingExtraDuplicateAndWrongRows()
    {
        using var estate = new TestEstate(3);
        RecordEnvelope product = Product(estate);
        RecordEnvelope carrier = Work(estate, "REL-1", plan: new JsonObject { ["product"] = product.Id, ["version"] = "1.0.0" });
        var placement = new JsonObject { ["kind"] = "release", ["product"] = product.Id, ["version"] = "1.0.0", ["carrier"] = carrier.Id };
        RecordEnvelope one = Work(estate, "WORK-1", placement);
        Work(estate, "WORK-2", placement);
        Work(estate, "WORK-3");
        Work(estate, "WORK-4", new JsonObject { ["kind"] = "backlog", ["key"] = "later" });
        TemplateDefinition definition = FirstPartyTemplates.Definitions.Single(d => d.Id == "vericairn.estate/roadmap");
        RecordEnvelope declaration = Declare(estate, definition, "design/roadmap.md");
        EstateModel model = estate.Load();
        AssertValid(estate);
        string text = TemplateDocuments.NewContent(model, declaration);
        string payload = TemplateSections.Read(text, definition.Elements)["releases"].Content;
        Assert.Contains("WORK&#45;1, WORK&#45;2", payload, StringComparison.Ordinal);
        Assert.DoesNotContain("WORK&#45;3", payload, StringComparison.Ordinal);
        Assert.DoesNotContain("WORK&#45;4", payload, StringComparison.Ordinal);
        Assert.DoesNotContain("REL&#45;1", payload, StringComparison.Ordinal);
        Assert.Equal("passed", PlacementCheck(text));
        foreach (string damaged in new[]
        {
            payload.Replace("WORK&#45;1, ", "", StringComparison.Ordinal),
            payload.Replace("WORK&#45;2", "WORK&#45;3", StringComparison.Ordinal),
            payload.Replace("WORK&#45;2", "WORK&#45;4", StringComparison.Ordinal),
            payload.Replace("WORK&#45;2", "WORK&#45;1, WORK&#45;2", StringComparison.Ordinal),
            payload.Replace("1&#46;0&#46;0", "2&#46;0&#46;0", StringComparison.Ordinal),
            payload.Replace(carrier.Id, one.Id, StringComparison.Ordinal),
        }) Assert.Equal("failed", PlacementCheck(Fill(text, definition, "releases", damaged)));
        JsonObject moved = JsonNode.Parse(one.Body.GetRawText())!.AsObject();
        moved["placement"] = new JsonObject { ["kind"] = "backlog", ["key"] = "later" };
        estate.Add("work-order", estate.OperatorId, moved.ToJsonString(), supersedes: one.Id);
        model = estate.Load();
        Assert.Equal("failed", PlacementCheck(text));
        Assert.DoesNotContain("WORK&#45;1", TemplateDocuments.NewContent(model, declaration), StringComparison.Ordinal);

        string PlacementCheck(string content) => TemplateDocuments.Sections(model, declaration, text: content).Single(s => s.Id == "releases")
            .Checks.Single(c => c.Id == "vericairn.estate/roadmap-placements-v1").Outcome;
    }

    [Fact]
    public void IndexIncludesEveryCurrentDesignAndAsksKeepTheirRecordedWordsAndAnswers()
    {
        using var estate = new TestEstate(3);
        Revision(estate, "design/alpha.md", "# Alpha\n\nFirst revision.\n");
        Revision(estate, "design/alpha.md", "# Alpha\n\nSecond revision.\n");
        Revision(estate, "design/beta.md", "# Beta\n\nDesign body.\n");
        Revision(estate, "docs/other.md", "# Other\n");
        var index = FirstPartyTemplates.Definitions.Single(d => d.Id == "vericairn.estate/design-index");
        string text = TemplateDocuments.NewContent(estate.Load(), Declare(estate, index, "design/master-design.md"));
        Assert.Contains("design&#47;alpha&#46;md", text, StringComparison.Ordinal);
        Assert.Contains("design&#47;beta&#46;md", text, StringComparison.Ordinal);
        Assert.DoesNotContain("other", text, StringComparison.Ordinal);
        Assert.Equal(1, text.Split("design&#47;alpha&#46;md", StringSplitOptions.None).Length - 1);

        const string question = "Keep `exact` words?\n\nEven this second paragraph.";
        RecordEnvelope ask = estate.Add("ask", estate.BuilderId, new JsonObject
        {
            ["to"] = estate.OperatorId, ["question"] = question, ["recommendation"] = "Retain them.",
            ["consequence"] = "The choice stays open.", ["state"] = "open",
        }.ToJsonString());
        JsonObject answer = Decision(estate, "Yes. Keep every word, including ``` fences.");
        answer["answers"] = new JsonArray(ask.Id);
        RecordEnvelope decision = estate.Add("decision", estate.OperatorId, answer.ToJsonString());
        var register = FirstPartyTemplates.Definitions.Single(d => d.Id == "vericairn.estate/open-decisions");
        text = TemplateDocuments.NewContent(estate.Load(), Declare(estate, register, "design/open-decisions.md"));
        Assert.Contains(question, text, StringComparison.Ordinal);
        Assert.Contains("Yes. Keep every word, including ``` fences.", text, StringComparison.Ordinal);
        Assert.Contains("State: answered", text, StringComparison.Ordinal);
        Assert.Contains("Answered by: " + decision.Id, text, StringComparison.Ordinal);
        Assert.True(text.IndexOf("Retain them.", StringComparison.Ordinal) < text.IndexOf(question, StringComparison.Ordinal));
        AssertValid(estate);
    }

    public static RecordEnvelope Product(TestEstate estate) => estate.Add("product", estate.OperatorId,
        """{"name":"Example","slug":"example","summary":"A fixture product.","editions":[{"name":"Community","slug":"community","summary":"The foundation."}],"surfaces":[{"name":"CLI","slug":"cli","summary":"Commands."}]}""");

    public static RecordEnvelope Work(TestEstate estate, string key, JsonObject? placement = null, JsonObject? plan = null)
    {
        var body = new JsonObject { ["key"] = key, ["kind"] = "ordinary", ["title"] = key, ["intent"] = "A bounded change.",
            ["scope"] = new JsonArray("The fixture."), ["exitQuestion"] = "Is the change ready?", ["inputs"] = new JsonArray(), ["deliverables"] = new JsonArray("A reviewed change.") };
        if (placement is not null) body["placement"] = placement.DeepClone();
        if (plan is not null) body["releasePlan"] = plan.DeepClone();
        return estate.Add("work-order", estate.OperatorId, body.ToJsonString());
    }
}
