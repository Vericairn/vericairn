// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text;
using System.Text.Json.Nodes;
using Vericairn.Protocol;
using Vericairn.Records.Estate;
using Vericairn.Records.Hashing;
using Vericairn.Records.Model;
using Vericairn.Records.Templates;
using Vericairn.Records.Tests;
using Vericairn.Runtime.Service;
using Vericairn.Runtime.Store;
using static Vericairn.Records.Tests.TemplateFixture;

namespace Vericairn.Runtime.Tests;

public sealed class TemplatePinTests
{
    [Fact]
    public void ExplicitRenderingPinsTheNewRevisionLineWithoutAHashLoopOrChangingAuthoredBytes()
    {
        using var estate = new TestEstate(3);
        var definition = FirstPartyTemplates.Definitions.Single(d => d.Id == "vericairn.estate/design-document");
        using EstateService service = EstateService.Open(estate.Root);
        DocumentNewResponse created = service.DocumentNew(new(estate.OperatorId, new(definition.Id, definition.Version, definition.Hash), "design/example.md", "Example"));
        Assert.True(created.Ok, created.Problem?.Message);
        string file = Path.Combine(estate.Root, "design", "example.md");
        string text = "\uFEFF# Example\r\n\r\nRevision 7: the authored history stays here.\r\n\r\n" + File.ReadAllText(file);
        text = Fill(text, definition, "answer", "\r\nKeep every café sentence, and this trailing space. \r\n");
        File.WriteAllText(file, text);
        string authored = TemplateSections.Read(text, definition.Elements)["answer"].Content;
        DocumentPinResponse first = service.DocumentPin(new("design/example.md", estate.OperatorId, "Adopt the generated revision line.") { RenderTemplate = true });
        Assert.True(first.Ok, first.Problem?.Message);
        string rendered = File.ReadAllText(file);
        Assert.StartsWith("\uFEFF# Example\r\n\r\nRevision 7: the authored history stays here.", new UTF8Encoding(false, true).GetString(File.ReadAllBytes(file)), StringComparison.Ordinal);
        Assert.Equal(authored, TemplateSections.Read(rendered, definition.Elements)["answer"].Content);
        Assert.StartsWith("Revision 1, ", TemplateSections.Read(rendered, definition.Elements)["revision"].Content, StringComparison.Ordinal);
        Assert.Equal(ContentHash.Sha256File(file), service.Session.Load().ById[first.Written!.Id].Body.GetProperty("hash").GetString());
        Assert.All(service.DocumentSections(created.Declaration!.Id).Sections.Single(s => s.Id == "revision").Checks, c => Assert.Equal("passed", c.Outcome));
        byte[] firstRecord = File.ReadAllBytes(Path.Combine(estate.Root, first.Written.Path));
        byte[] before = File.ReadAllBytes(file);
        DocumentPinResponse second = service.DocumentPin(new("design/example.md", estate.OperatorId, "Refresh from the next recorded revision.", Expected: first.Written.Id) { RenderTemplate = true });
        Assert.True(second.Ok, second.Problem?.Message);
        Assert.Equal(2, second.Revision);
        Assert.Equal(firstRecord, File.ReadAllBytes(Path.Combine(estate.Root, first.Written.Path)));
        Assert.Equal(authored, TemplateSections.Read(File.ReadAllText(file), definition.Elements)["answer"].Content);
        Assert.DoesNotContain(service.Check().Findings, f => f.Code == "document.drift");
        Assert.False(service.DocumentPin(new("design/example.md", estate.OperatorId, "Stale pin.", Expected: first.Written.Id) { RenderTemplate = true }).Ok);
        Assert.Equal(2, service.Session.Load().OfType("document-revision").Count());
        Assert.NotEqual(ContentHash.Sha256(before), ContentHash.Sha256File(file));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void InterruptedTemplatePinsRecoverOrRetainInterveningEdits(bool conflict)
    {
        using var estate = new TestEstate(3);
        var definition = FirstPartyTemplates.Definitions.Single(d => d.Id == "vericairn.estate/design-document");
        using EstateService service = EstateService.Open(estate.Root);
        DocumentNewResponse created = service.DocumentNew(new(estate.OperatorId, new(definition.Id, definition.Version, definition.Hash), "design/example.md", "Example"));
        Assert.True(created.Ok, created.Problem?.Message);
        service.BeforePublishingDocuments = _ => throw new IOException("Injected interruption after the pin.");
        DocumentPinResponse pinned = service.DocumentPin(new("design/example.md", estate.OperatorId, "First recorded revision.") { RenderTemplate = true });
        Assert.NotNull(pinned.Written); Assert.Equal("reconcile-required", pinned.Problem!.Code);
        BatchJournal pending = Assert.Single(BatchJournal.All(service.Session).Journals);
        DocumentOutputChange output = Assert.Single(pending.Outputs);
        if (conflict) estate.WriteFile("design/example.md", "Keep the intervening authored edit.\n");
        service.Dispose();
        using EstateService reopened = EstateService.Open(estate.Root);
        Assert.Single(reopened.Session.Load().OfType("document-revision"));
        if (conflict)
        {
            Assert.Equal("Keep the intervening authored edit.\n", File.ReadAllText(Path.Combine(estate.Root, "design", "example.md")));
            Assert.Single(BatchJournal.All(reopened.Session).Journals);
        }
        else
        {
            Assert.Equal(output.AfterHash, ContentHash.Sha256File(Path.Combine(estate.Root, "design", "example.md")));
            Assert.Empty(BatchJournal.All(reopened.Session).Journals);
        }
    }

    [Fact]
    public void MissingMarkersAndAnOutputHashCycleAreRefusedWithoutWriting()
    {
        using var estate = new TestEstate(3);
        var generator = new HashCycle(); DocumentGenerators.Register(generator);
        JsonObject section = Section("revision", "generated");
        section["synthesis"]!["generator"] = generator.Id; section["synthesis"]!["inputs"] = new JsonArray();
        TemplateDefinition definition = Install(estate, Contract(section));
        JsonObject declared = Declaration(estate, definition);
        declared["regions"] = new JsonArray(new JsonObject { ["id"] = "revision", ["generator"] = generator.Id });
        RecordEnvelope declaration = estate.Add("living-document", estate.OperatorId, declared.ToJsonString());
        string text = TemplateDocuments.NewContent(estate.Load(), declaration);
        estate.WriteFile("docs/example.md", text);
        using EstateService service = EstateService.Open(estate.Root);
        var request = new DocumentPinRequest("docs/example.md", estate.OperatorId, "Refuse the cycle.") { RenderTemplate = true };
        DocumentPinResponse refused = service.DocumentPin(request);
        Assert.False(refused.Ok);
        Assert.Contains("unstable source", refused.Problem!.Message, StringComparison.Ordinal);
        Assert.Equal(text, File.ReadAllText(Path.Combine(estate.Root, "docs", "example.md")));
        Assert.Empty(service.Session.Load().OfType("document-revision"));
        estate.WriteFile("docs/example.md", "Keep authored bytes with no generated markers.\n");
        Assert.False(service.DocumentPin(request).Ok);
        Assert.Empty(service.Session.Load().OfType("document-revision"));
    }

    private sealed class HashCycle : IDocumentGenerator
    {
        public string Id => "example.template-pin/hash-cycle";
        public string? Generate(EstateModel model, string? region, IReadOnlyList<string> inputs) =>
            model.Current("document-revision").SingleOrDefault()?.Body.GetProperty("hash").GetString() ?? "No pin.";
    }
}
