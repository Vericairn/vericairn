// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Vericairn.Records.Estate;
using Vericairn.Records.Hashing;
using Vericairn.Records.Model;
using Vericairn.Records.Templates;

namespace Vericairn.Records.Tests;

/// <summary>Declarative contracts and exact section evidence, only in disposable estates.</summary>
public static class TemplateFixture
{
    public static JsonElement Element(JsonNode node) => JsonSerializer.SerializeToElement(node);

    public static JsonObject Section(string id, string mode = "authored", string fidelity = "draft") => new()
    {
        ["id"] = id, ["marker"] = true, ["purpose"] = "Explain " + id + " with complete evidence.", ["presence"] = "required",
        ["role"] = new JsonObject { ["write"] = "builder", ["review"] = "operator", ["clear"] = "operator" },
        ["synthesis"] = new JsonObject { ["mode"] = mode }, ["sources"] = new JsonArray(), ["cohesion"] = new JsonArray(),
        ["fidelity"] = fidelity, ["quality"] = new JsonObject { ["review"] = "independent", ["checks"] = new JsonArray() },
    };

    public static JsonObject Contract(params JsonObject[] sections) => new()
    {
        ["format"] = "vericairn.template/1", ["id"] = "example/design", ["version"] = "1.0.0",
        ["purpose"] = "A document whose sections retain their evidence.", ["artifact"] = "document", ["sections"] = new JsonArray(sections.Select(s => (JsonNode)s).ToArray()),
    };

    public static TemplateDefinition Install(TestEstate estate, JsonObject contract)
    {
        TemplateDefinition definition = TemplateDefinition.Read(Element(contract));
        estate.WriteFile("docs/templates/" + definition.Id.Replace('/', '-') + "-" + definition.Version + ".json", contract.ToJsonString());
        return definition;
    }

    public static JsonObject Pin(TemplateDefinition definition) => new() { ["id"] = definition.Id, ["version"] = definition.Version, ["hash"] = definition.Hash };

    public static JsonObject Declaration(TestEstate estate, TemplateDefinition definition, string path = "docs/example.md")
    {
        JsonObject pin = Pin(definition);
        pin["owner"] = estate.OperatorId;
        pin["options"] = JsonSerializer.SerializeToNode(TemplateInstances.Options(estate.Load(), definition, default, default));
        pin["overrides"] = new JsonArray();
        return new JsonObject
        {
            ["path"] = path, ["title"] = "Example", ["kind"] = "authored", ["source"] = "estate-file",
            ["triggers"] = new JsonArray(new JsonObject { ["on"] = "decision", ["level"] = "refuse" }), ["clearedBy"] = "any",
            ["summary"] = "An exact template instance.", ["template"] = pin,
        };
    }

    public static RecordEnvelope Declare(TestEstate estate, TemplateDefinition definition, string path = "docs/example.md") =>
        estate.Add("living-document", estate.OperatorId, Declaration(estate, definition, path).ToJsonString());

    public static JsonObject Reference(RecordEnvelope record) => new() { ["type"] = record.Type, ["id"] = record.Id, ["hash"] = record.ComputedHash };

    public static string Fill(string text, TemplateDefinition definition, string id, string content)
    {
        TemplateSectionContent section = TemplateSections.Read(text, definition.Elements)[id];
        return string.Concat(text.AsSpan(0, section.Start), content, text.AsSpan(section.Start + section.Length));
    }

    public static RecordEnvelope Revision(TestEstate estate, string path, string content, string? by = null)
    {
        estate.WriteFile(path, content);
        RecordEnvelope? previous = estate.Load().Current("document-revision").SingleOrDefault(r => EstateModel.BodyString(r, "path") == path);
        return estate.Add("document-revision", by ?? estate.BuilderId, new JsonObject
        {
            ["path"] = path, ["title"] = "Example", ["hash"] = ContentHash.Sha256(Encoding.UTF8.GetBytes(content)),
            ["bytes"] = Encoding.UTF8.GetByteCount(content), ["mediaType"] = "text/markdown", ["revision"] = previous is null ? 1 : previous.Body.GetProperty("revision").GetInt64() + 1,
            ["summary"] = "Record the exact authored bytes.",
        }.ToJsonString(), supersedes: previous?.Id);
    }

    public static JsonObject Review(TestEstate estate, RecordEnvelope declaration, string text, string id, string fidelity = "draft",
        IReadOnlyList<TemplateJudgment>? judgments = null, string? by = null, string outcome = "current")
    {
        string reviewer = by ?? estate.OperatorId;
        return new JsonObject
        {
            ["outcome"] = outcome, ["content"] = new JsonObject { ["hash"] = ContentHash.Sha256(Encoding.UTF8.GetBytes(text)), ["bytes"] = Encoding.UTF8.GetByteCount(text) },
            ["answers"] = new JsonArray(), ["independence"] = reviewer == estate.OperatorId ? "operator" : "self", ["summary"] = "Read the section and every source.",
            ["findings"] = new JsonArray(), ["checks"] = new JsonArray(),
            ["sections"] = TemplateDocuments.ReviewSections(estate.Load(), declaration, text, null, reviewer, [new(id, outcome, fidelity, judgments)]),
        };
    }

    public static JsonObject Decision(TestEstate estate, string statement) => new()
    {
        ["title"] = "Recorded instruction", ["statement"] = statement, ["summary"] = "Retain this instruction.", ["synthesis"] = "The recorded instruction supplies the document's source.",
        ["effects"] = new JsonArray("Use the instruction as evidence."), ["affects"] = new JsonArray(), ["decidedBy"] = estate.OperatorId,
        ["decidedAt"] = "2026-10-13T12:00:00Z", ["stake"] = "low", ["notAuthorized"] = new JsonArray(),
    };

    public static JsonObject Output(RecordEnvelope declaration, string id, string content) => new()
    {
        ["name"] = id, ["hash"] = ContentHash.Sha256(Encoding.UTF8.GetBytes(content)), ["bytes"] = Encoding.UTF8.GetByteCount(content),
        ["mediaType"] = "text/markdown", ["location"] = EstateModel.BodyString(declaration, "path"),
        ["section"] = new JsonObject { ["id"] = id, ["document"] = Reference(declaration) },
    };

    public static RecordEnvelope Attempt(TestEstate estate, string? by = null)
    {
        by ??= estate.BuilderId;
        int number = estate.Load().OfType("work-order").Count() + 1;
        RecordEnvelope order = estate.Add("work-order", estate.OperatorId, new JsonObject
        {
            ["key"] = "TPL-" + number, ["kind"] = "ordinary", ["title"] = "Write a section", ["intent"] = "Produce exact section bytes.",
            ["scope"] = new JsonArray("The fixture document."), ["exitQuestion"] = "Is the section ready for review?", ["inputs"] = new JsonArray(), ["deliverables"] = new JsonArray("Section bytes."),
        }.ToJsonString());
        RecordEnvelope assignment = estate.Add("assignment", estate.OperatorId, new JsonObject
        {
            ["contributor"] = by, ["brief"] = new JsonObject
            {
                ["outcome"] = "A section ready for review.", ["sources"] = new JsonArray(), ["permittedChanges"] = new JsonArray("The fixture section."),
                ["prohibitedEffects"] = new JsonArray("No publication."), ["workspace"] = new JsonArray("The disposable estate."), ["limits"] = new JsonArray(),
                ["expectedOutputs"] = new JsonArray("Exact section bytes."), ["reviewCriteria"] = new JsonArray("Read every source."), ["recovery"] = "Continue from the recorded result.",
            },
        }.ToJsonString(), order.Reference);
        return estate.Add("attempt", by, """{"startedAt":"2026-10-13T12:00:00Z","workspace":"The disposable estate.","note":"Write a section."}""", assignment.Reference);
    }

    public static JsonObject Result(params JsonObject[] outputs) => new()
    {
        ["outcome"] = "completed", ["summary"] = "Section bytes are ready to inspect.", ["outputs"] = new JsonArray(outputs.Select(o => (JsonNode)o).ToArray()),
        ["evidence"] = new JsonArray(), ["checks"] = new JsonArray(), ["limitations"] = new JsonArray(), ["unresolvedEffects"] = new JsonArray(), ["nextStep"] = "Review the section.",
    };

    public static void AssertValid(TestEstate estate) => Assert.False(estate.Load().Findings.HasErrors, string.Join('\n', estate.Load().Findings));

    public static TestEstate RecordedFixture()
    {
        var estate = new TestEstate(3);
        JsonObject contract = Contract(Section("answer", "synthesized"));
        contract["options"] = new JsonArray(new JsonObject { ["id"] = "audience", ["values"] = new JsonArray("operator", "public"), ["default"] = "operator" });
        TemplateDefinition definition = Install(estate, contract);
        estate.Add("template-defaults", estate.OperatorId, new JsonObject
        {
            ["template"] = Pin(definition), ["scope"] = new JsonObject { ["kind"] = "estate" },
            ["options"] = new JsonObject { ["audience"] = "operator" }, ["reason"] = "Use the accountable reader's context.",
        }.ToJsonString());
        RecordEnvelope declaration = Declare(estate, definition);
        const string content = "The complete section.\n";
        string text = Fill(TemplateSections.Skeleton(definition), definition, "answer", content);
        Revision(estate, "docs/example.md", text);
        RecordEnvelope attempt = Attempt(estate);
        estate.Add("result", estate.BuilderId, Result(Output(declaration, "answer", content)).ToJsonString(), attempt.Reference);
        estate.Add("document-review", estate.OperatorId, Review(estate, declaration, text, "answer").ToJsonString(), declaration.Reference);
        contract["version"] = "1.1.0";
        TemplateDefinition offered = Install(estate, contract);
        JsonObject revised = JsonNode.Parse(declaration.Body.GetRawText())!.AsObject();
        revised["templateUpgrade"] = new JsonObject { ["offered"] = Pin(offered), ["outcome"] = "declined", ["reason"] = "Keep this edition for its current readers." };
        estate.Add("living-document", estate.OperatorId, revised.ToJsonString(), supersedes: declaration.Id);
        AssertValid(estate);
        return estate;
    }
}
