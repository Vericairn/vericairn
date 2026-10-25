// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using System.Text.Json.Nodes;
using Vericairn.Protocol;
using Vericairn.Records.Estate;
using Vericairn.Records.Model;
using Vericairn.Records.Templates;
using Vericairn.Records.Tests;
using Vericairn.Runtime.Service;
using Vericairn.Runtime.Store;
using static Vericairn.Records.Tests.TemplateFixture;

namespace Vericairn.Runtime.Tests;

public sealed class TemplateRefusalTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void InterruptedCreationRecoversItsOriginalBytesAndPreservesConflictingEdits(bool conflict)
    {
        using var estate = new TestEstate(3);
        TemplateDefinition definition = Install(estate, Contract(Section("answer")));
        using EstateService service = EstateService.Open(estate.Root);
        service.BeforePublishingDocuments = _ => throw new IOException("Injected output interruption.");
        DocumentNewResponse response = service.DocumentNew(new(estate.OperatorId, new(definition.Id, definition.Version, definition.Hash), "docs/example.md", "Example"));
        Assert.False(response.Ok); Assert.NotNull(response.Declaration);
        Assert.Equal("reconcile-required", response.Problem!.Code);
        RecordWritten receipt = response.Declaration;
        byte[] declarationBytes = File.ReadAllBytes(Path.Combine(estate.Root, receipt.Path));
        BatchJournal journal = Assert.Single(BatchJournal.All(service.Session).Journals);
        DocumentOutputChange output = journal.Outputs.Single(o => o.Path == "docs/example.md");
        if (conflict) estate.WriteFile(output.Path, "Keep the operator's intervening edit.\n");
        service.Dispose();
        using EstateService recovered = EstateService.Open(estate.Root);
        Assert.Equal(declarationBytes, File.ReadAllBytes(Path.Combine(estate.Root, receipt.Path)));
        Assert.Single(recovered.Session.Load().Current("living-document"));
        if (conflict)
        {
            Assert.Equal("Keep the operator's intervening edit.\n", File.ReadAllText(Path.Combine(estate.Root, output.Path)));
            Assert.Single(BatchJournal.All(recovered.Session).Journals);
            DocumentNewResponse blocked = recovered.DocumentNew(new(estate.OperatorId, new(definition.Id, definition.Version, definition.Hash), "docs/later.md", "Later"));
            Assert.Null(blocked.Declaration); Assert.Equal("reconcile-required", blocked.Problem!.Code);
        }
        else
        {
            Assert.Empty(BatchJournal.All(recovered.Session).Journals);
            Assert.Equal(response.ContentHash, Vericairn.Records.Hashing.ContentHash.Sha256File(Path.Combine(estate.Root, output.Path)));
            Assert.Equal("missing", Assert.Single(recovered.DocumentSections(receipt.Id).Sections).State);
        }
    }

    [Theory]
    [InlineData("[null]")]
    [InlineData("[{\"id\":\"answer\",\"outcome\":\"current\",\"fidelity\":\"draft\",\"judgments\":[null]}]")]
    [InlineData("[{\"id\":\"answer\",\"outcome\":\"current\",\"fidelity\":\"draft\",\"judgments\":[{\"rule\":\"meaning\",\"verdict\":\"passed\",\"summary\":\"Read.\",\"evidence\":null}]}]")]
    public void MalformedSectionReviewPayloadsAreDefiniteRefusals(string json)
    {
        using TestEstate estate = RecordedFixture();
        using EstateService service = EstateService.Open(estate.Root);
        RecordEnvelope declaration = service.Session.Load().Current("living-document").Single();
        var request = new DocumentReviewRequest(estate.OperatorId, "current", "Malformed selection.")
        { Sections = JsonSerializer.Deserialize<DocumentSectionReview[]>(json, ProtocolJson.Options) };
        int count = service.Session.Load().Records.Count;
        WriteOutcome result = service.DocumentReview(declaration.Id, request);
        Assert.False(result.Ok); Assert.Null(result.Written);
        Assert.Equal(count, service.Session.Load().Records.Count);
    }

    [Fact]
    public void RawWritesCannotForgeASectionByteCountOrAReviewAndHistoryRemainsIntact()
    {
        using var estate = new TestEstate(3);
        TemplateDefinition definition = Install(estate, Contract(Section("answer", "synthesized")));
        RecordEnvelope declaration = Declare(estate, definition);
        const string content = "Café, exactly.\r\n";
        string text = Fill(TemplateSections.Skeleton(definition), definition, "answer", content);
        Revision(estate, "docs/example.md", text);
        RecordEnvelope attempt = Attempt(estate);
        using EstateService service = EstateService.Open(estate.Root);
        JsonObject output = Output(declaration, "answer", content); output["bytes"] = content.Length;
        int count = service.Session.Load().Records.Count;
        WriteOutcome wrong = service.Result(new(attempt.Id, estate.BuilderId, Element(Result(output))));
        Assert.False(wrong.Ok); Assert.Null(wrong.Written); Assert.Contains("byte count", wrong.Problem!.Message, StringComparison.Ordinal);
        JsonObject forged = Review(estate, declaration, text, "answer"); forged["sections"]![0]!["hash"] = "sha256:" + new string('1', 64);
        WriteOutcome review = service.NewRecord(new("document-review", estate.OperatorId, Element(forged), $"living-document:{declaration.Id}:{declaration.ComputedHash}"));
        Assert.False(review.Ok); Assert.Null(review.Written); Assert.Equal(count, service.Session.Load().Records.Count);
        WriteOutcome correct = service.Result(new(attempt.Id, estate.BuilderId, Element(Result(Output(declaration, "answer", content)))));
        Assert.True(correct.Ok, correct.Problem?.Message);
        estate.WriteFile("docs/example.md", Fill(text, definition, "answer", "Later edited bytes.\n"));
        Assert.False(service.Session.Load().Findings.HasErrors);
        Assert.Contains(service.Session.Load().Findings, f => f.Code == "result.output-changed");
        Assert.Empty(service.Session.Load().OfType("acceptance"));
    }

    [Fact]
    public void RetirementKeepsExistingPinsReadableAndRefusesNewInstancesThroughBothAuthoringPaths()
    {
        using var estate = new TestEstate(3);
        JsonObject contract = Contract(Section("answer")); contract["id"] = "example/retired-fixture";
        TemplateDefinition definition = Install(estate, contract);
        RecordEnvelope declaration = Declare(estate, definition);
        Revision(estate, "docs/example.md", Fill(TemplateSections.Skeleton(definition), definition, "answer", "An existing edition.\n"));
        TemplateCatalog.Register(definition, retired: true);
        using EstateService service = EstateService.Open(estate.Root);
        Assert.DoesNotContain(service.Templates().Templates, t => t.Pin.Id == definition.Id);
        Assert.True(service.SectionBrief(declaration.Id, "answer").Brief!.Complete);
        Assert.False(service.DocumentNew(new(estate.OperatorId, new(definition.Id, definition.Version, definition.Hash), "docs/new.md", "New")).Ok);
        Assert.False(service.NewRecord(new("living-document", estate.OperatorId, Element(Declaration(estate, definition, "docs/raw.md")))).Ok);
        JsonObject detached = JsonNode.Parse(declaration.Body.GetRawText())!.AsObject(); detached.Remove("template");
        WriteOutcome removed = service.NewRecord(new("living-document", estate.OperatorId, Element(detached), Supersedes: declaration.Id, Expected: declaration.Id));
        Assert.False(removed.Ok); Assert.Null(removed.Written);
        WriteOutcome retained = service.NewRecord(new("living-document", estate.OperatorId, declaration.Body, Supersedes: declaration.Id, Expected: declaration.Id));
        Assert.True(retained.Ok, retained.Problem?.Message);
        Assert.Equal(definition.Hash, TemplateCatalog.Pin(service.Session.Load().ById[retained.Written!.Id])!.Hash);
    }

    [Fact]
    public void QuotedTextCannotHideTheNextSectionWhenInstantiatingHeadings()
    {
        using var estate = new TestEstate(3);
        RecordEnvelope source = estate.Add("decision", estate.OperatorId, Decision(estate, "No trailing newline.").ToJsonString());
        JsonObject quote = Section("quote", "quoted"); quote.Remove("marker"); quote["heading"] = "Quote";
        quote["synthesis"]!["source"] = "words";
        quote["sources"] = new JsonArray(new JsonObject { ["id"] = "words", ["kind"] = "record", ["record"] = source.Id, ["field"] = "statement" });
        JsonObject next = Section("next"); next.Remove("marker"); next["heading"] = "Next";
        TemplateDefinition definition = Install(estate, Contract(quote, next));
        using EstateService service = EstateService.Open(estate.Root);
        DocumentNewResponse response = service.DocumentNew(new(estate.OperatorId, new(definition.Id, definition.Version, definition.Hash), "docs/example.md", "Example"));
        Assert.False(response.Ok); Assert.Null(response.Declaration);
        Assert.Empty(service.Session.Load().OfType("living-document"));
        Assert.False(File.Exists(Path.Combine(estate.Root, "docs/example.md")));
    }
}
