// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json.Nodes;
using Vericairn.Records.Authoring;
using Vericairn.Records.Estate;
using Vericairn.Records.Model;
using Vericairn.Records.Templates;
using static Vericairn.Records.Tests.TemplateFixture;

namespace Vericairn.Records.Tests;

public sealed class TemplateCompanionTests
{
    [Fact]
    public void EveryTemplateCollectionRetainsExactChoicesOverridesChecksAndEvidence()
    {
        using TestEstate estate = RecordedFixture();
        foreach (RecordEnvelope record in estate.Load().Records.Where(r => r.Type is "template-defaults" or "living-document" or "document-review" or "result"))
        {
            DocumentSourceSnapshot source = DocumentSourceSnapshot.Record(record);
            string text = RecordCompanionTests.Text(RecordCompanionTests.Render([source]));
            Assert.Contains(record.ComputedHash, text, StringComparison.Ordinal);
            foreach (string field in new[] { "template.overrides", "template.options", "sections", "sections[].sources", "sections[].sources[].records", "sections[].checks", "sections[].cohesion", "sections[].cohesion[].evidence", "options" })
            {
                if (!DocumentCoverage.RecordKinds.Any(k => k.Id == "record/" + record.Type + "#" + field)) continue;
                Assert.Equal(DocumentGenerationStatus.Success, RecordCompanionTests.Render([source], "list", field).Status);
            }
        }
        Assert.Contains(DocumentCoverage.All, d => d.Kind == "record/living-document#template.overrides[].definition");
    }

    /// <summary>Stable synthetic records exercise the added fields in versioned golden companion output.</summary>
    public static IReadOnlyDictionary<string, DocumentRendition> GoldenCases()
    {
        const string author = "01ARZ3NDEKTSV4RRFFQ69G5FAV";
        const string id = "01ARZ3NDEKTSV4RRFFQ69G5FAW";
        const string hash = "sha256:1111111111111111111111111111111111111111111111111111111111111111";
        TemplateDefinition template = TemplateDefinition.Read(Element(Contract(Section("answer"))));
        JsonObject pin = Pin(template); pin["owner"] = author; pin["options"] = new JsonObject { ["audience"] = "operator" };
        JsonObject added = Section("notes");
        pin["overrides"] = new JsonArray(new JsonObject { ["section"] = "notes", ["action"] = "add", ["reason"] = "Keep the reader's context.", ["definition"] = added });
        JsonObject declaration = new()
        {
            ["path"] = "docs/example.md", ["title"] = "Example", ["kind"] = "authored", ["source"] = "estate-file", ["summary"] = "Pinned instance.",
            ["triggers"] = new JsonArray(new JsonObject { ["on"] = "decision", ["level"] = "refuse" }), ["template"] = pin,
            ["templateUpgrade"] = new JsonObject { ["offered"] = Pin(template), ["outcome"] = "declined", ["reason"] = "Retain the recorded edition." },
        };
        var at = new DateTimeOffset(2026, 11, 1, 0, 0, 0, TimeSpan.Zero);
        RecordEnvelope instance = RecordAuthor.Create(new RecordDraft("living-document", author, Element(declaration), At: at, Id: id, Format: 3)).Envelope;
        JsonObject review = new()
        {
            ["outcome"] = "current", ["content"] = new JsonObject { ["hash"] = hash, ["bytes"] = 100 }, ["answers"] = new JsonArray(),
            ["independence"] = "operator", ["summary"] = "Each exact criterion was read.", ["checks"] = new JsonArray(), ["findings"] = new JsonArray(),
            ["sections"] = new JsonArray(new JsonObject
            {
                ["id"] = "answer", ["hash"] = hash, ["sourceDigest"] = hash, ["outcome"] = "current", ["fidelity"] = "final",
                ["sources"] = new JsonArray(new JsonObject { ["id"] = "instruction", ["kind"] = "record", ["identity"] = "decision:" + author,
                    ["hash"] = hash, ["bytes"] = 23, ["status"] = "available", ["records"] = new JsonArray(new JsonObject { ["type"] = "decision", ["id"] = author, ["hash"] = hash }) }),
                ["checks"] = new JsonArray(new JsonObject { ["name"] = "field-type", ["outcome"] = "passed", ["summary"] = "Text field." }),
                ["cohesion"] = new JsonArray(new JsonObject { ["id"] = "meaning", ["kind"] = "judgment", ["outcome"] = "passed", ["summary"] = "The exact source and section agree.", ["evidence"] = new JsonArray(hash) }),
            }),
        };
        JsonObject defaults = new() { ["template"] = Pin(template), ["scope"] = new JsonObject { ["kind"] = "project", ["target"] = "example" }, ["options"] = new JsonObject { ["audience"] = "operator" }, ["reason"] = "Use these defaults when creating a document." };
        var cases = new Dictionary<string, DocumentRendition>(StringComparer.Ordinal);
        foreach ((string type, JsonObject body) in new[] { ("living-document", declaration), ("document-review", review), ("template-defaults", defaults), ("result", Result(Output(instance, "answer", "The complete section."))) })
        {
            RecordEnvelope record = RecordAuthor.Create(new RecordDraft(type, author, Element(body), At: at, Id: id, Format: 3)).Envelope;
            DocumentSourceSnapshot source = DocumentSourceSnapshot.Record(record);
            cases.Add("template-" + type + ".md", RecordCompanionTests.Render([source]));
            if (type == "document-review") foreach (string field in new[] { "sections", "sections[].sources", "sections[].checks", "sections[].cohesion", "sections[].cohesion[].evidence" })
                cases.Add("template-" + field.Replace("[]", "", StringComparison.Ordinal) + ".md", RecordCompanionTests.Render([source], "list", field));
            if (type == "living-document") cases.Add("template-overrides.md", RecordCompanionTests.Render([source], "list", "template.overrides"));
        }
        return cases;
    }
}
