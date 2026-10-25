// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text;
using System.Text.Json.Nodes;
using Vericairn.Protocol;
using Vericairn.Records.Estate;
using Vericairn.Records.Model;
using Vericairn.Records.Templates;
using Vericairn.Records.Tests;
using Vericairn.Runtime.Service;
using static Vericairn.Records.Tests.TemplateFixture;

namespace Vericairn.Runtime.Tests;

public sealed class TemplateJourneyTests
{
    private static readonly DesignIndex Generator = InstallGenerator();

    [Fact]
    [Trait("Journey", "J57")]
    public void J57DesignSectionsAreSynthesizedQuotedCheckedAndReviewedWithoutAcceptance()
    {
        using var estate = new TestEstate(3);
        RecordEnvelope instruction = estate.Add("decision", estate.OperatorId, Decision(estate, "Keep the operator's words exactly.").ToJsonString());
        RecordEnvelope ask = estate.Add("ask", estate.BuilderId, new JsonObject
        {
            ["to"] = estate.OperatorId, ["question"] = "Which format is required?", ["recommendation"] = "Use the current format.",
            ["consequence"] = "The decision remains open.", ["state"] = "open",
        }.ToJsonString());
        Revision(estate, "design/source.md", "# Source\n\nA retained design.\n");
        JsonObject words = Section("words", "quoted"); words["synthesis"]!["source"] = "instruction";
        words["sources"] = new JsonArray(new JsonObject { ["id"] = "instruction", ["kind"] = "record", ["record"] = instruction.Id, ["field"] = "statement" });
        JsonObject vocabulary = Section("vocabulary", "synthesized");
        vocabulary["sources"] = new JsonArray(new JsonObject { ["id"] = "instruction", ["kind"] = "record", ["record"] = instruction.Id, ["field"] = "summary" });
        vocabulary["cohesion"] = new JsonArray(new JsonObject { ["id"] = "meaning", ["kind"] = "judgment", ["summary"] = "Terms preserve the recorded instruction's meaning." });
        JsonObject decisions = Section("decisions");
        decisions["sources"] = new JsonArray(new JsonObject { ["id"] = "asks", ["kind"] = "records", ["type"] = "ask", ["where"] = new JsonObject { ["field"] = "state", ["equals"] = "open" }, ["field"] = "question" });
        decisions["cohesion"] = new JsonArray(new JsonObject { ["id"] = "open-questions", ["kind"] = "agrees-on-values", ["source"] = "asks", ["summary"] = "Decisions agree with the open asks." });
        JsonObject index = Section("index", "generated");
        index["synthesis"]!["generator"] = Generator.Id; index["synthesis"]!["inputs"] = new JsonArray();
        index["sources"] = new JsonArray(new JsonObject { ["id"] = "designs", ["kind"] = "records", ["type"] = "document-revision", ["field"] = "path" });
        index["cohesion"] = new JsonArray(new JsonObject { ["id"] = "fresh-index", ["kind"] = "derived-from", ["summary"] = "The design index comes from the recorded documents." });
        JsonObject contract = Contract(words, vocabulary, decisions, index);
        contract["options"] = new JsonArray(new JsonObject { ["id"] = "stage", ["values"] = new JsonArray("draft", "final"), ["default"] = "draft" });
        foreach (JsonNode? section in contract["sections"]!.AsArray()) section!["fidelity"] = new JsonObject
        { ["default"] = "draft", ["when"] = new JsonArray(new JsonObject { ["option"] = "stage", ["value"] = "final", ["level"] = "final" }) };
        TemplateDefinition definition = Install(estate, contract);
        using EstateService service = EstateService.Open(estate.Root);
        DocumentNewResponse created = service.DocumentNew(new(estate.OperatorId, new(definition.Id, definition.Version, definition.Hash), "design/new-design.md", "New design", Level: "refuse"));
        Assert.True(created.Ok, created.Problem?.Message);
        string id = created.Declaration!.Id;
        RecordEnvelope declaration = service.Session.Load().ById[id];
        string text = File.ReadAllText(Path.Combine(estate.Root, created.Path!));
        Assert.Equal(instruction.Body.GetProperty("statement").GetString(), TemplateSections.Read(text, definition.Elements)["words"].Content);
        Assert.Equal("missing", service.DocumentSections(id).Sections.Single(s => s.Id == "vocabulary").State);
        Assert.Equal(4, service.DocumentSections(id).Sections.Count);
        SectionBriefResponse brief = service.SectionBrief(id, "vocabulary");
        Assert.True(brief.Brief!.Complete, brief.Problem?.Message);
        Assert.Contains(brief.Brief.Sources, s => s.Records.Any(r => r.Id == instruction.Id && r.Hash == instruction.ComputedHash));
        Assert.NotEmpty(brief.Brief.RoleBrief);

        text = Fill(text, definition, "vocabulary", "Record: retained evidence of an attributable act.\n");
        text = Fill(text, definition, "decisions", "- An unrelated question.\n");
        SyncClock(estate, service);
        Revision(estate, created.Path!, text);
        text = Fill(text, definition, "index", Generator.Generate(estate.Load(), "index", [])!);
        Revision(estate, created.Path!, text);
        RecordEnvelope attempt = Attempt(estate);
        WriteOutcome submitted = service.Result(new(attempt.Id, estate.BuilderId, Element(Result(Output(declaration, "vocabulary", TemplateSections.Read(text, definition.Elements)["vocabulary"].Content)))));
        Assert.True(submitted.Ok, submitted.Problem?.Message);
        Assert.Equal("synthesized", service.DocumentSections(id).Sections.Single(s => s.Id == "vocabulary").State);
        Assert.Equal("not-judged", service.DocumentSections(id).Sections.Single(s => s.Id == "vocabulary").Cohesion.Single().Outcome);
        Assert.False(Clear(service, estate, id, "vocabulary", "draft").Ok);
        Assert.False(Clear(service, estate, id, "decisions", "draft").Ok);
        Assert.Equal("failing", service.DocumentSections(id).Sections.Single(s => s.Id == "decisions").State);
        text = Fill(text, definition, "decisions", "- " + ask.Body.GetProperty("question").GetString() + "\n");
        Revision(estate, created.Path!, text);
        foreach (TemplateSectionDto section in service.DocumentSections(id).Sections)
        {
            WriteOutcome reviewed = Clear(service, estate, id, section.Id, "draft", Judgments(section));
            Assert.True(reviewed.Ok, section.Id + ": " + System.Text.Json.JsonSerializer.Serialize(reviewed.Problem));
        }
        Assert.All(service.DocumentSections(id).Sections, section => Assert.Equal("reviewed", section.State));
        Assert.Equal("current", Assert.Single(service.Documents()).State);

        JsonObject promoted = JsonNode.Parse(declaration.Body.GetRawText())!.AsObject(); promoted["template"]!["options"]!["stage"] = "final";
        WriteOutcome promotion = service.NewRecord(new("living-document", estate.OperatorId, Element(promoted), Supersedes: id));
        Assert.True(promotion.Ok, promotion.Problem?.Message);
        id = promotion.Written!.Id;
        Assert.All(service.DocumentSections(id).Sections, section => Assert.Equal("final", section.RequiredFidelity));
        Assert.Equal("due", Assert.Single(service.Documents()).State);
        foreach (TemplateSectionDto section in service.DocumentSections(id).Sections)
        {
            WriteOutcome reviewed = Clear(service, estate, id, section.Id, "final", Judgments(section));
            Assert.True(reviewed.Ok, section.Id + ": " + System.Text.Json.JsonSerializer.Serialize(reviewed.Problem));
        }
        Assert.All(service.DocumentSections(id).Sections, section => Assert.Equal("reviewed", section.State));
        Assert.Equal("current", Assert.Single(service.Documents()).State);
        Assert.Empty(service.Session.Load().OfType("acceptance"));
        Assert.Empty(service.Session.Load().OfType("authority"));
        Assert.Equal(0, service.Check().Errors);
    }

    [Fact]
    [Trait("Journey", "J58")]
    public void J58GameDesignMovesFromPitchSketchToProductionFinalWithEveryMechanicTested()
    {
        using var estate = new TestEstate(3);
        JsonObject mechanics = Section("mechanics", fidelity: "sketch");
        JsonObject tests = Section("test-plan", fidelity: "sketch");
        tests["sources"] = new JsonArray(new JsonObject { ["id"] = "mechanics", ["kind"] = "section", ["section"] = "mechanics" });
        tests["cohesion"] = new JsonArray(new JsonObject { ["id"] = "coverage", ["kind"] = "covers", ["source"] = "mechanics", ["summary"] = "Every mechanic has a test." });
        JsonObject contract = Contract(mechanics, tests); contract["id"] = "example/game-design";
        contract["options"] = new JsonArray(new JsonObject { ["id"] = "stage", ["values"] = new JsonArray("pitch", "production"), ["default"] = "pitch" });
        foreach (JsonNode? section in contract["sections"]!.AsArray()) section!["fidelity"] = new JsonObject
        { ["default"] = "sketch", ["when"] = new JsonArray(new JsonObject { ["option"] = "stage", ["value"] = "production", ["level"] = "final" }) };
        TemplateDefinition definition = Install(estate, contract);
        using EstateService service = EstateService.Open(estate.Root);
        DocumentNewResponse created = service.DocumentNew(new(estate.OperatorId, new(definition.Id, definition.Version, definition.Hash), "docs/game.md", "Game design", Level: "refuse"));
        Assert.True(created.Ok, created.Problem?.Message);
        string id = created.Declaration!.Id;
        string text = Fill(Fill(File.ReadAllText(Path.Combine(estate.Root, created.Path!)), definition, "mechanics", "- jump\n- dash\n"), definition, "test-plan", "- jump\n");
        SyncClock(estate, service); Revision(estate, created.Path!, text);
        Assert.False(Clear(service, estate, id, "test-plan", "sketch").Ok);
        text = Fill(text, definition, "test-plan", "- jump\n- dash\n");
        Revision(estate, created.Path!, text);
        Assert.True(Clear(service, estate, id, "mechanics", "sketch").Ok);
        Assert.True(Clear(service, estate, id, "test-plan", "sketch").Ok);
        Assert.All(service.DocumentSections(id).Sections, s => Assert.Equal("reviewed", s.State));

        JsonObject body = JsonNode.Parse(service.Session.Load().ById[id].Body.GetRawText())!.AsObject(); body["template"]!["options"]!["stage"] = "production";
        WriteOutcome production = service.NewRecord(new("living-document", estate.OperatorId, Element(body), Supersedes: id));
        Assert.True(production.Ok, production.Problem?.Message); id = production.Written!.Id;
        Assert.True(Clear(service, estate, id, "mechanics", "sketch").Ok);
        Assert.Equal("drafted", service.DocumentSections(id).Sections.Single(s => s.Id == "mechanics").State);
        Assert.Equal("due", Assert.Single(service.Documents()).State);
        Assert.True(Clear(service, estate, id, "mechanics", "final").Ok);
        Assert.True(Clear(service, estate, id, "test-plan", "final").Ok);
        Assert.Equal("current", Assert.Single(service.Documents()).State);
        Assert.Equal(0, service.Check().Errors);
    }

    [Fact]
    [Trait("Journey", "J59")]
    public void J59OwnersPreviewThenTwoAdoptAndOneDeclinesWithoutChangingHistory()
    {
        using var estate = new TestEstate(3);
        JsonObject original = Contract(Section("answer"));
        TemplateDefinition first = Install(estate, original);
        using EstateService service = EstateService.Open(estate.Root);
        var instances = new List<RecordEnvelope>();
        foreach (string name in new[] { "one", "two", "three" })
        {
            DocumentNewResponse made = service.DocumentNew(new(estate.OperatorId, new(first.Id, first.Version, first.Hash), "docs/" + name + ".md", name));
            Assert.True(made.Ok, made.Problem?.Message); instances.Add(service.Session.Load().ById[made.Declaration!.Id]);
        }
        Dictionary<string, byte[]> originals = instances.ToDictionary(d => d.ExpectedPath, d => File.ReadAllBytes(Path.Combine(estate.Root, d.ExpectedPath)), StringComparer.Ordinal);
        Dictionary<string, byte[]> documents = instances.ToDictionary(d => EstateModel.BodyString(d, "path")!, d => File.ReadAllBytes(Path.Combine(estate.Root, EstateModel.BodyString(d, "path")!)), StringComparer.Ordinal);
        JsonObject next = (JsonObject)original.DeepClone(); next["version"] = "1.1.0";
        JsonObject notes = Section("notes"); notes["presence"] = "optional"; next["sections"]!.AsArray().Add(notes);
        TemplateDefinition second = Install(estate, next);
        int count = service.Session.Load().Records.Count;
        Assert.Contains("Added section or field: notes", TemplateInstances.Changes(first, second));
        Assert.Equal(count, service.Session.Load().Records.Count);
        Assert.All(instances, d => Assert.Equal(first.Hash, TemplateCatalog.Pin(service.Session.Load().Latest(d))!.Hash));
        for (int i = 0; i < instances.Count; i++)
        {
            RecordEnvelope before = instances[i];
            JsonObject body = JsonNode.Parse(before.Body.GetRawText())!.AsObject();
            bool accepted = i < 2;
            if (accepted) { body["template"]!["version"] = second.Version; body["template"]!["hash"] = second.Hash; }
            body["templateUpgrade"] = new JsonObject { ["offered"] = Pin(second), ["outcome"] = accepted ? "accepted" : "declined", ["reason"] = accepted ? "Use the optional notes in future revisions." : "This document's retained edition is sufficient." };
            WriteOutcome choice = service.NewRecord(new("living-document", estate.OperatorId, Element(body), Supersedes: before.Id));
            Assert.True(choice.Ok, choice.Problem?.Message);
            RecordEnvelope after = service.Session.Load().ById[choice.Written!.Id];
            Assert.Equal(accepted ? second.Hash : first.Hash, TemplateCatalog.Pin(after)!.Hash);
            Assert.Equal(accepted ? "accepted" : "declined", after.Body.GetProperty("templateUpgrade").GetProperty("outcome").GetString());
        }
        Assert.Equal(2, service.Session.Load().Current("living-document").Count(d => TemplateCatalog.Pin(d)!.Hash == second.Hash));
        Assert.Single(service.Session.Load().Current("living-document"), d => TemplateCatalog.Pin(d)!.Hash == first.Hash);
        foreach (var originalFile in originals.Concat(documents)) Assert.Equal(originalFile.Value, File.ReadAllBytes(Path.Combine(estate.Root, originalFile.Key)));
        AssertValid(estate);
    }

    private static DocumentSectionJudgment[] Judgments(TemplateSectionDto section) => section.Definition.GetProperty("cohesion").EnumerateArray()
        .Where(r => r.GetProperty("kind").GetString() == "judgment").Select(r => new DocumentSectionJudgment(r.GetProperty("id").GetString()!, "passed",
            "The complete section is coherent with every exact source.", new[] { section.ContentHash! }.Concat(section.Sources.Select(s => s.Hash)).Concat(section.Sources.SelectMany(s => s.Records).Select(r => r.Hash)).Distinct(StringComparer.Ordinal).ToArray())).ToArray();

    private static WriteOutcome Clear(EstateService service, TestEstate estate, string declaration, string section, string fidelity, IReadOnlyList<DocumentSectionJudgment>? judgments = null) =>
        service.DocumentReview(declaration, new(estate.OperatorId, "current", "Review the selected section against its exact evidence.") { Sections = [new(section, "current", fidelity, judgments)] });

    private static void SyncClock(TestEstate estate, EstateService service) => estate.SetClock(service.Session.Load().Records.Max(r => r.AtTime).AddMinutes(-1));

    private static DesignIndex InstallGenerator()
    {
        var generator = new DesignIndex(); DocumentGenerators.Register(generator); return generator;
    }

    private sealed class DesignIndex : IDocumentGenerator
    {
        public string Id => "example.template-journeys/design-index";
        public string? Generate(EstateModel model, string? region, IReadOnlyList<string> inputs) => string.Join("\n", model.Current("document-revision")
            .Select(r => "- " + EstateModel.BodyString(r, "path")).Order(StringComparer.Ordinal)) + "\n";
    }
}
