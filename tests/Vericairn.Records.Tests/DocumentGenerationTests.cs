// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Collections.Immutable;
using System.Text.Json.Nodes;
using Vericairn.Records.Estate;
using Vericairn.Records.Model;

namespace Vericairn.Records.Tests;

public class DocumentGenerationTests
{
    internal const string Pattern = "docs/registers/decisions/{month}/{date}-{slug}-{id}.md";
    internal const string GeneratorId = "example.documents/revisions";
    internal static readonly IDocumentGenerator Generator = Install();

    internal static JsonObject Declaration(string? pattern = Pattern) => new()
    {
        [pattern is null ? "path" : "pathPattern"] = pattern ?? "docs/decisions.md", ["title"] = "Recorded decisions", ["kind"] = "generated",
        ["generator"] = new JsonObject { ["id"] = Generator.Id, ["inputs"] = new JsonArray("format=1", "audience=Internal", "selection=all-revisions") },
        ["source"] = "estate-file", ["triggers"] = new JsonArray(), ["summary"] = "One file for each exact recorded decision.",
    };

    [Fact]
    public void PatternsBelongToFormatThreeAndRequireUnambiguousGeneratedOwnership()
    {
        using var fixture = new PolicyFixture();
        TestEstate estate = fixture.Estate;
        RecordEnvelope valid = fixture.Add("living-document", Declaration());
        Assert.False(estate.Load().Findings.HasErrors, string.Join('\n', estate.Load().Findings));
        using var old = new TestEstate(2);
        old.Add("living-document", old.OperatorId, Declaration().ToJsonString());
        Assert.Contains(old.Load().Findings, f => f.Code == "body.format");

        JsonObject both = Declaration();
        both["path"] = "docs/both.md";
        RecordEnvelope conflict = estate.Add("living-document", estate.OperatorId, both.ToJsonString());
        Assert.Contains(estate.Load().Findings, f => f.Code == "document.pattern" && f.Location.StartsWith(conflict.ExpectedPath, StringComparison.Ordinal));
        estate.Add("living-document", estate.OperatorId, """{"path":"docs/registers/decisions/authored.md","title":"Authored","kind":"authored","source":"estate-file","triggers":[{"on":"decision","level":"warn"}],"summary":"Authored prose."}""");
        Assert.Contains(estate.Load().Findings, f => f.Code == "document.declared-twice");
        Assert.NotNull(valid);
    }

    [Theory]
    [InlineData("docs/{anything}/{id}.md")]
    [InlineData("../docs/{month}/{date}-{slug}-{id}.md")]
    [InlineData("docs/CON/{month}/{date}-{slug}-{id}.md")]
    [InlineData("docs./register/{month}/{date}-{slug}-{id}.md")]
    public void UnsupportedOrNonportablePatternsAreRefused(string pattern) => Assert.False(DocumentPatterns.IsValid(pattern));

    [Theory]
    [InlineData("A  readable TITLE!", "a-readable-title")]
    [InlineData("東京", "item")]
    [InlineData("The K sign", "the-sign")]
    public void SlugsUseOnlyAsciiSourceCharacters(string title, string expected) => Assert.Equal(expected, DocumentPatterns.Slug(title));

    [Fact]
    public void MembersUseImmutableRevisionsAndOnlyTheirOwnInputs()
    {
        using var fixture = new PolicyFixture();
        RecordEnvelope declaration = fixture.Add("living-document", Declaration());
        DocumentMemberSelection first = Members(fixture.Estate.Load(), declaration);
        DocumentMember member = Assert.Single(first.Members);
        Assert.Equal(DocumentGenerationStatus.Success, first.Status);
        Assert.Equal("docs/registers/decisions/2026-10/2026-10-13-test-decision-" + fixture.Decision.Id + ".md", member.Path);
        Assert.Equal(fixture.Decision.ComputedHash, Assert.Single(member.Context.Sources).Hash);

        JsonObject successor = fixture.DecisionBody();
        successor["title"] = "A corrected decision";
        fixture.Add("decision", successor, supersedes: fixture.Decision.Id);
        fixture.Estate.Add("document-revision", fixture.Estate.OperatorId, """{"path":"docs/other.md","title":"Other","hash":"sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa","bytes":1,"mediaType":"text/markdown","revision":1,"summary":"An unrelated pin."}""");
        DocumentMemberSelection next = Members(fixture.Estate.Load(), declaration);
        Assert.Equal(2, next.Members.Length);
        Assert.Equal(member.Context.InputDigest, next.Members.Single(m => m.Path == member.Path).Context.InputDigest);
        Assert.NotEqual(next.Members[0].Path, next.Members[1].Path);
    }

    [Theory]
    [InlineData("format=2", DocumentGenerationStatus.Unavailable)]
    [InlineData("unknown=x", DocumentGenerationStatus.Error)]
    [InlineData("audience=Public", DocumentGenerationStatus.Error)]
    [InlineData("audience=Internal", DocumentGenerationStatus.Error)]
    public void UnknownVersionsAndInvalidOrDuplicateInputsNeverProduceMembers(string input, DocumentGenerationStatus expected)
    {
        using var estate = new TestEstate(3);
        JsonObject body = Declaration();
        JsonArray inputs = body["generator"]!["inputs"]!.AsArray();
        if (input.StartsWith("format=", StringComparison.Ordinal)) inputs.RemoveAt(0);
        inputs.Add(input);
        RecordEnvelope declaration = estate.Add("living-document", estate.OperatorId, body.ToJsonString());
        DocumentMemberSelection result = DocumentGeneration.Members(declaration, new(DocumentGenerationStatus.Success, [], null, null));
        Assert.Equal(expected, result.Status);
        Assert.Empty(result.Members);
    }

    [Fact]
    public void MissingSourcesAndDuplicateRegistrationsAreExplicit()
    {
        using var estate = new TestEstate(3);
        RecordEnvelope declaration = estate.Add("living-document", estate.OperatorId, Declaration().ToJsonString());
        var unavailable = new DocumentSourceSelection(DocumentGenerationStatus.Unavailable, [], null, "The exact commit is unavailable.");
        Assert.Equal(DocumentGenerationStatus.Unavailable, DocumentGeneration.Members(declaration, unavailable).Status);
        Assert.Throws<InvalidOperationException>(() => DocumentGenerators.Register(Generator));
    }

    [Fact]
    public void NamedGrammarAdmitsExplicitIdentitiesAndInstantsWithoutAClockOrEstateSpecificRegistration()
    {
        Assert.True(DocumentInputs.Accepts(["all", "@ulid"], "all"));
        Assert.True(DocumentInputs.Accepts(["all", "@ulid"], "01ARZ3NDEKTSV4RRFFQ69G5FAV"));
        Assert.False(DocumentInputs.Accepts(["all", "@ulid"], "latest"));
        Assert.True(DocumentInputs.Accepts(["@timestamp"], "2026-10-13T12:00:00Z"));
        Assert.False(DocumentInputs.Accepts(["@timestamp"], "now"));
        Assert.False(DocumentInputs.Accepts(["@path"], "../outside.md"));
        Assert.False(DocumentInputs.Supported("@unknown"));
    }

    [Fact]
    public void AValidPatternCannotPublishAnOverlongMappedMember()
    {
        using var f = new PolicyFixture();
        string prefix = string.Join('/', Enumerable.Repeat(new string('x', 85), 5));
        JsonObject body = Declaration(prefix + "/{month}/{date}-{slug}-{id}.md");
        JsonObject decision = f.DecisionBody();
        decision["title"] = new string('t', 100);
        f.Add("decision", decision);
        RecordEnvelope declared = f.Add("living-document", body);
        DocumentMemberSelection result = Members(f.Estate.Load(), declared);
        Assert.Equal(DocumentGenerationStatus.Error, result.Status);
        Assert.Empty(result.Members);
    }

    internal static DocumentMemberSelection Members(EstateModel model, RecordEnvelope declaration) =>
        DocumentGeneration.Members(declaration, DocumentSourceSelection.Records(model, Generator.Descriptor!));

    private static RevisionGenerator Install()
    {
        var generator = new RevisionGenerator();
        DocumentGenerators.Register(generator);
        return generator;
    }

    private sealed class RevisionGenerator : IDocumentGenerator
    {
        public string Id => GeneratorId;
        public DocumentGeneratorDescriptor Descriptor { get; } = new(GeneratorId, 1, "example.documents/1",
            "sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
            new Dictionary<string, ImmutableArray<string>>(StringComparer.Ordinal) { ["audience"] = ["Internal"], ["selection"] = ["all-revisions"] }.ToImmutableDictionary(StringComparer.Ordinal),
            ["decision"], ["record"], ["item", "all-revisions"], "text/markdown", "UTC time then ordinal identity", "Internal", "record-revision");
        public DocumentRenderResult Generate(DocumentGenerationContext context) => DocumentRenderResult.Success(
            string.Join('\n', context.Sources.Select(s => s.Body!.Value.GetProperty("statement").GetString())) + "\n");
    }
}
