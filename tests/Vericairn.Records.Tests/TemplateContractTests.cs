// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Vericairn.Records.Canonical;
using Vericairn.Records.Estate;
using Vericairn.Records.Model;
using Vericairn.Records.Templates;
using Vericairn.Records.Types;
using static Vericairn.Records.Tests.TemplateFixture;

namespace Vericairn.Records.Tests;

public sealed class TemplateContractTests
{
    [Fact]
    public void DamagedRequiredOriginalsMakeTheBriefExplicitlyIncomplete()
    {
        using TestEstate estate = RecordedFixture();
        EstateModel model = estate.Load();
        RecordEnvelope declaration = model.Current("living-document").Single();
        RecordFile[] files = EstateLoader.EnumerateRecordFiles(estate.Root)
            .Select(f => f.Path == declaration.ExpectedPath ? f with { Bytes = Encoding.UTF8.GetBytes("{damaged") } : f).ToArray();
        TemplateSectionBrief brief = TemplateDocuments.Brief(model, declaration, "answer", recordFiles: files);
        Assert.False(brief.Complete);
        TemplateSource original = Assert.Single(brief.Sources, s => s.Id == "record/" + declaration.Id);
        Assert.Equal("unavailable", original.Status); Assert.Null(original.Content);
        Assert.Contains(brief.Sources, s => s.Kind == "template" && s.Content is not null);
        Assert.True(brief.Bytes > 0);
    }

    [Fact]
    public void CanonicalPinsRetainTheCompleteContractAndUnknownPropertiesOrPrimitivesAreRefused()
    {
        JsonObject contract = Contract(Section("answer"));
        TemplateDefinition definition = TemplateDefinition.Read(Element(contract));
        Assert.Equal(definition.Hash, TemplateDefinition.Read(JsonDocument.Parse(contract.ToJsonString(new() { WriteIndented = true })).RootElement).Hash);
        foreach ((string key, JsonNode value) in new (string, JsonNode)[] { ("authority", new JsonArray("publish")), ("plugin", JsonValue.Create("anything")!) })
        {
            JsonObject changed = (JsonObject)contract.DeepClone(); changed[key] = value;
            Assert.Throws<InvalidOperationException>(() => TemplateDefinition.Read(Element(changed)));
        }
        foreach (Action<JsonObject> mutation in new Action<JsonObject>[]
        {
            c => c["sections"]![0]!["role"]!["write"] = "uninstalled-role",
            c => c["sections"]![0]!["synthesis"] = new JsonObject { ["mode"] = "generated", ["generator"] = "missing/generator", ["inputs"] = new JsonArray() },
            c => c["sections"]![0]!["quality"]!["checks"] = new JsonArray(new JsonObject { ["name"] = "missing/check" }),
            c => c["sections"]![0]!["quality"]!["checks"] = new JsonArray(new JsonObject { ["name"] = "length", ["min"] = 30, ["max"] = 2 }),
            c => c["sections"]![0]!["heading"] = "Ambiguous",
            c => c["sections"]!.AsArray().Add(Section("answer")),
            c => c["sections"]![0]!["presence"] = new JsonObject { ["option"] = "missing", ["value"] = "yes" },
            c => c["sections"]![0]!["sources"] = new JsonArray(new JsonObject { ["id"] = "neighbor", ["kind"] = "section", ["section"] = "missing" }),
        })
        {
            JsonObject changed = (JsonObject)contract.DeepClone(); mutation(changed);
            Assert.Throws<InvalidOperationException>(() => TemplateDefinition.Read(Element(changed)));
        }
    }

    [Fact]
    public void LocatorsPreserveExactPayloadsIgnoreFencesAndRefuseAmbiguousNesting()
    {
        JsonObject section = Section("parent"); section.Remove("marker"); section["heading"] = "Parent";
        JsonObject child = Section("child"); child["type"] = "text"; section["fields"] = new JsonArray(child);
        TemplateDefinition definition = TemplateDefinition.Read(Element(Contract(section)));
        const string childText = " café\r\nverbatim ";
        string text = "## Parent\r\n```md\r\n## Parent\r\n```\r\n<!-- vericairn:section child -->" + childText + "<!-- vericairn:section-end child -->\r\n";
        var parts = TemplateSections.Read(text, definition.Elements);
        Assert.Equal(childText, parts["child"].Content);
        Assert.Equal(Encoding.UTF8.GetBytes(childText), Encoding.UTF8.GetBytes(parts["child"].Content));
        Assert.Contains("```md\r\n## Parent", parts["parent"].Content, StringComparison.Ordinal);
        Assert.Throws<InvalidOperationException>(() => TemplateSections.Read(text + "## Parent\n", definition.Elements));
        Assert.Throws<InvalidOperationException>(() => TemplateSections.Read("<!-- vericairn:section a --><!-- vericairn:section b --><!-- vericairn:section-end a -->", definition.Elements));
        Assert.Throws<InvalidOperationException>(() => TemplateSections.Read("<!-- vericairn:section child -->outside<!-- vericairn:section-end child -->\n## Parent\nbody\n", definition.Elements));
    }

    [Fact]
    public void SectionReviewDoesNotClearItsNeighborAndChangesMakeOnlyDependentSectionsDue()
    {
        using var estate = new TestEstate(3);
        JsonObject dependent = Section("test-plan");
        dependent["sources"] = new JsonArray(new JsonObject { ["id"] = "mechanics", ["kind"] = "section", ["section"] = "mechanics" });
        dependent["cohesion"] = new JsonArray(new JsonObject { ["id"] = "coverage", ["kind"] = "covers", ["source"] = "mechanics", ["summary"] = "Every mechanic has a test." });
        TemplateDefinition definition = Install(estate, Contract(Section("mechanics"), dependent, Section("notes")));
        RecordEnvelope declaration = Declare(estate, definition);
        string text = TemplateSections.Skeleton(definition);
        foreach (string id in new[] { "mechanics", "test-plan", "notes" }) text = Fill(text, definition, id, "- jump\n");
        Revision(estate, "docs/example.md", text);
        estate.Add("document-review", estate.OperatorId, Review(estate, declaration, text, "mechanics").ToJsonString(), declaration.Reference);
        Assert.Equal("reviewed", TemplateDocuments.Sections(estate.Load(), declaration).Single(s => s.Id == "mechanics").State);
        Assert.Equal("drafted", TemplateDocuments.Sections(estate.Load(), declaration).Single(s => s.Id == "notes").State);
        foreach (string id in new[] { "test-plan", "notes" }) estate.Add("document-review", estate.OperatorId, Review(estate, declaration, text, id).ToJsonString(), declaration.Reference);
        Assert.All(TemplateDocuments.Sections(estate.Load(), declaration), s => Assert.Equal("reviewed", s.State));
        text = Fill(text, definition, "mechanics", "- jump\n- run\n");
        estate.WriteFile("docs/example.md", text);
        var states = TemplateDocuments.Sections(estate.Load(), declaration);
        Assert.Equal("due", states.Single(s => s.Id == "mechanics").State);
        Assert.Equal("failing", states.Single(s => s.Id == "test-plan").State);
        Assert.Equal("reviewed", states.Single(s => s.Id == "notes").State);
        Assert.Throws<InvalidOperationException>(() => Review(estate, declaration, text, "test-plan"));
        AssertValid(estate);
    }

    [Fact]
    public void QuotedRecordsAreComparedByteForByteAndRetainTheirRevisionEvidence()
    {
        using var estate = new TestEstate(3);
        const string words = "Keep café.\r\n  Keep spaces. ";
        RecordEnvelope decision = estate.Add("decision", estate.OperatorId, Decision(estate, words).ToJsonString());
        JsonObject quote = Section("words", "quoted"); quote["synthesis"]!["source"] = "instruction";
        quote["sources"] = new JsonArray(new JsonObject { ["id"] = "instruction", ["kind"] = "record", ["record"] = decision.Id, ["field"] = "statement" });
        quote["cohesion"] = new JsonArray(new JsonObject { ["id"] = "exact", ["kind"] = "matches-record", ["source"] = "instruction", ["summary"] = "Preserve the operator's words." });
        TemplateDefinition definition = Install(estate, Contract(quote));
        RecordEnvelope declaration = Declare(estate, definition);
        string text = TemplateDocuments.NewContent(estate.Load(), declaration);
        Assert.Equal(words, TemplateSections.Read(text, definition.Elements)["words"].Content);
        Revision(estate, "docs/example.md", text);
        JsonObject review = Review(estate, declaration, text, "words");
        Assert.Equal(decision.ComputedHash, review["sections"]![0]!["sources"]![0]!["records"]![0]!["hash"]!.GetValue<string>());
        estate.Add("document-review", estate.OperatorId, review.ToJsonString(), declaration.Reference);
        Assert.Equal("reviewed", Assert.Single(TemplateDocuments.Sections(estate.Load(), declaration)).State);
        estate.WriteFile("docs/example.md", text.Replace("\r\n", "\n", StringComparison.Ordinal));
        Assert.Equal("failing", Assert.Single(TemplateDocuments.Sections(estate.Load(), declaration)).State);
        AssertValid(estate);
    }

    [Fact]
    public void EvaluatorJudgmentsNeedIndependenceExactEvidenceAndOneVerdictPerRule()
    {
        using var estate = new TestEstate(3);
        JsonObject section = Section("answer", "synthesized");
        section["cohesion"] = new JsonArray(new JsonObject { ["id"] = "consistency", ["kind"] = "judgment", ["summary"] = "The explanation is coherent." });
        TemplateDefinition definition = Install(estate, Contract(section));
        RecordEnvelope declaration = Declare(estate, definition);
        string text = Fill(TemplateSections.Skeleton(definition), definition, "answer", "A complete explanation.\n");
        Revision(estate, "docs/example.md", text);
        TemplateSectionState state = Assert.Single(TemplateDocuments.Sections(estate.Load(), declaration));
        Assert.Equal("not-judged", Assert.Single(state.Cohesion).Outcome);
        Assert.Throws<InvalidOperationException>(() => Review(estate, declaration, text, "answer"));
        Assert.Throws<InvalidOperationException>(() => Review(estate, declaration, text, "answer", judgments: [new("consistency", "passed", "Read it.", [])]));
        Assert.Throws<InvalidOperationException>(() => Review(estate, declaration, text, "answer", judgments: [new("consistency", "unknown", "Insufficient evidence.", [state.ContentHash!])]));
        estate.Add("roster", estate.OperatorId, new JsonObject { ["roles"] = new JsonArray(new JsonObject { ["role"] = "evaluator", ["principals"] = new JsonArray(estate.BuilderId) }, new JsonObject { ["role"] = "operator", ["principals"] = new JsonArray(estate.BuilderId) }) }.ToJsonString());
        Assert.Throws<InvalidOperationException>(() => Review(estate, declaration, text, "answer", judgments: [new("consistency", "passed", "Read it.", [state.ContentHash!])], by: estate.BuilderId));
        JsonObject review = Review(estate, declaration, text, "answer", judgments: [new("consistency", "passed", "The explanation is consistent with its complete evidence.", [state.ContentHash!])]);
        estate.Add("document-review", estate.OperatorId, review.ToJsonString(), declaration.Reference);
        Assert.Equal("reviewed", Assert.Single(TemplateDocuments.Sections(estate.Load(), declaration)).State);
        Assert.Empty(estate.Load().OfType("acceptance"));
        AssertValid(estate);
    }

    [Fact]
    public void ImportedReviewsCannotOmitChecksSourcesOrCohesionRules()
    {
        using var estate = new TestEstate(3);
        JsonObject section = Section("answer");
        section["quality"]!["checks"] = new JsonArray(new JsonObject { ["name"] = "length", ["min"] = 3 });
        section["sources"] = new JsonArray(new JsonObject { ["id"] = "input", ["kind"] = "file", ["path"] = "docs/input.txt" });
        section["cohesion"] = new JsonArray(new JsonObject { ["id"] = "same", ["kind"] = "agrees-on-values", ["source"] = "input", ["summary"] = "Values agree." });
        estate.WriteFile("docs/input.txt", "one\ntwo\n");
        TemplateDefinition definition = Install(estate, Contract(section));
        RecordEnvelope declaration = Declare(estate, definition);
        string text = Fill(TemplateSections.Skeleton(definition), definition, "answer", "- two\n- one\n");
        Revision(estate, "docs/example.md", text);
        foreach ((string member, string code) in new[] { ("checks", "template.check"), ("sources", "template.source"), ("cohesion", "template.cohesion") })
        {
            JsonObject review = Review(estate, declaration, text, "answer"); review["sections"]![0]![member] = new JsonArray();
            RecordEnvelope forged = estate.Add("document-review", estate.OperatorId, review.ToJsonString(), declaration.Reference);
            Assert.Contains(estate.Load().Findings, f => f.Code == code && f.Location.StartsWith(forged.ExpectedPath, StringComparison.Ordinal));
        }
    }

    [Fact]
    public void AuthoredOnlyRefusesAnAgentResultAndTemplatesGrantNoStanding()
    {
        using var estate = new TestEstate(3);
        JsonObject section = Section("dedication"); section["synthesis"]!["personOnly"] = true;
        TemplateDefinition definition = Install(estate, Contract(section));
        RecordEnvelope declaration = Declare(estate, definition);
        const string content = "An authored dedication.";
        estate.WriteFile("docs/example.md", Fill(TemplateSections.Skeleton(definition), definition, "dedication", content));
        RecordEnvelope attempt = Attempt(estate);
        RecordEnvelope result = estate.Add("result", estate.BuilderId, Result(Output(declaration, "dedication", content)).ToJsonString(), attempt.Reference);
        Assert.Contains(estate.Load().Findings, f => f.Code == "template.authored-only" && f.Location.StartsWith(result.ExpectedPath, StringComparison.Ordinal));
        Assert.False(TemplateInstances.HasRole(estate.Load(), estate.BuilderId, "operator"));
        Assert.False(estate.Load().HasScope(estate.BuilderId, Scopes.Administer));
        Assert.Empty(estate.Load().OfType("authority"));
        Assert.Empty(estate.Load().OfType("acceptance"));
    }

    [Fact]
    public void WholeDocumentChecksUseTheWholeDocumentAndBriefAccountingIncludesFullOriginals()
    {
        using var estate = new TestEstate(3);
        RecordEnvelope decision = estate.Add("decision", estate.OperatorId, Decision(estate, "Read the whole instruction.").ToJsonString());
        JsonObject answer = Section("answer");
        answer["sources"] = new JsonArray(new JsonObject { ["id"] = "instruction", ["kind"] = "record", ["record"] = decision.Id, ["field"] = "statement" });
        JsonObject contract = Contract(answer, Section("notes"));
        contract["checks"] = new JsonArray(new JsonObject { ["name"] = "required-terms", ["terms"] = new JsonArray("reserved-term") });
        TemplateDefinition definition = Install(estate, contract);
        RecordEnvelope declaration = Declare(estate, definition);
        string text = Fill(Fill(TemplateSections.Skeleton(definition), definition, "answer", "The answer.\n"), definition, "notes", "reserved-term\n");
        Revision(estate, "docs/example.md", text);
        Assert.All(TemplateDocuments.Sections(estate.Load(), declaration), s => Assert.Equal("passed", s.Checks.Single(c => c.Id == "document/required-terms").Outcome));
        TemplateSectionBrief brief = TemplateDocuments.Brief(estate.Load(), declaration, "answer");
        Assert.True(brief.Complete);
        Assert.Contains("recorded", brief.RoleBrief, StringComparison.Ordinal);
        Assert.Contains(brief.Sources, s => s.Kind == "original-record" && s.Content == File.ReadAllText(Path.Combine(estate.Root, decision.ExpectedPath)));
        Assert.Contains(brief.Sources, s => s.Id == "template/contract" && s.Content!.Contains("reserved-term", StringComparison.Ordinal));
        var json = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        Assert.Equal(CanonicalJson.SerializeUtf8(JsonSerializer.SerializeToElement(brief, json)).LongLength, brief.Bytes);
        TemplateSectionBrief limited = TemplateDocuments.Brief(estate.Load(), declaration, "answer", 20);
        Assert.False(limited.Complete);
        Assert.Equal(JsonSerializer.Serialize(brief.Sources), JsonSerializer.Serialize(limited.Sources));
        Assert.Equal(CanonicalJson.SerializeUtf8(JsonSerializer.SerializeToElement(limited, json)).LongLength, limited.Bytes);
    }

    [Fact]
    public void DefaultsAreMaterializedNarrowestFirstAndRequiredOmissionNeedsAnExactDecision()
    {
        using var estate = new TestEstate(3);
        JsonObject contract = Contract(Section("answer"), Section("notes"));
        contract["options"] = new JsonArray(new JsonObject { ["id"] = "audience", ["values"] = new JsonArray("public", "engineering", "operator"), ["default"] = "public" });
        TemplateDefinition definition = Install(estate, contract);
        foreach ((string kind, string? target, string value) in new[] { ("estate", (string?)null, "engineering"), ("domain", "design", "public"), ("project", "demo", "operator") })
        {
            var scope = new JsonObject { ["kind"] = kind }; if (target is not null) scope["target"] = target;
            estate.Add("template-defaults", estate.OperatorId, new JsonObject { ["template"] = Pin(definition), ["scope"] = scope, ["options"] = new JsonObject { ["audience"] = value }, ["reason"] = "Fit this scope." }.ToJsonString());
        }
        Assert.Equal("operator", TemplateInstances.Options(estate.Load(), definition, Element(new JsonObject { ["domain"] = "design", ["project"] = "demo" }), default)["audience"]);
        Assert.Equal("public", TemplateInstances.Options(estate.Load(), definition, Element(new JsonObject { ["project"] = "demo" }), Element(new JsonObject { ["audience"] = "public" }))["audience"]);
        JsonObject body = Declaration(estate, definition);
        body["template"]!["overrides"] = new JsonArray(new JsonObject { ["section"] = "notes", ["action"] = "omit", ["reason"] = "Not applicable." });
        RecordEnvelope refused = estate.Add("living-document", estate.OperatorId, body.ToJsonString());
        Assert.Contains(estate.Load().Findings, f => f.Code == "template.omission" && f.Location.StartsWith(refused.ExpectedPath, StringComparison.Ordinal));
        JsonObject decisionBody = Decision(estate, "Omit the notes section."); decisionBody["documents"] = new JsonArray("docs/example.md"); decisionBody["effects"] = new JsonArray("omit-section docs/example.md#notes");
        RecordEnvelope decision = estate.Add("decision", estate.OperatorId, decisionBody.ToJsonString());
        body["template"]!["overrides"]![0]!["decision"] = Reference(decision);
        RecordEnvelope permitted = estate.Add("living-document", estate.OperatorId, body.ToJsonString(), supersedes: refused.Id);
        Assert.DoesNotContain(estate.Load().Findings, f => f.Severity == Severity.Error && f.Location.StartsWith(permitted.ExpectedPath, StringComparison.Ordinal));
        Assert.Single(TemplateInstances.Effective(definition, permitted).Elements);
    }

    [Fact]
    public void AdditiveFieldsStayInFormatThreeWithCompanionAndDescribeCoverage()
    {
        Assert.Equal(3, RecordTypeRegistry.Find("template-defaults")!.SinceFormat);
        Assert.Contains("template-defaults", RecordCompanions.Types);
        Assert.Contains(RecordCompanions.Coverage, c => c.Kind == "record/template-defaults");
        Assert.Contains(RecordTypeRegistry.Find("living-document")!.Fields, f => f.Name == "template" && f.SinceFormat == 3);
        Assert.Contains(RecordTypeRegistry.Find("document-review")!.Fields, f => f.Name == "sections" && f.SinceFormat == 3);
        using var early = new TestEstate(2);
        TemplateDefinition definition = Install(early, Contract(Section("answer")));
        RecordEnvelope declaration = Declare(early, definition);
        Assert.Contains(early.Load().Findings, f => f.Code == "body.format" && f.Location.StartsWith(declaration.ExpectedPath, StringComparison.Ordinal));
    }
}
