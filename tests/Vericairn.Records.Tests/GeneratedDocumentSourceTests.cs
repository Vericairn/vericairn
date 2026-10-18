// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text;
using System.Text.Json.Nodes;
using Vericairn.Records.Estate;
using Vericairn.Records.Model;

namespace Vericairn.Records.Tests;

public class GeneratedDocumentSourceTests
{
    private const string Commit = "0123456789abcdef0123456789abcdef01234567";
    private static readonly DependencyRenderer DependencyGenerator = InstallDependencyGenerator();

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void SourceDependenciesMustBeCurrentAndAcyclicAtTheSameCommit(bool cycle, bool region)
    {
        var source = new DependencySource(cycle, region);
        Assert.True(DocumentSources.Register(source));
        using var f = new PolicyFixture();
        RecordEnvelope repository = f.Estate.Add("resource", f.Estate.OperatorId, """{"kind":"repository","name":"Example","role":"source","location":{"github":"Example/example"},"visibility":"private"}""");
        RecordEnvelope Add(string path)
        {
            JsonObject body = DocumentGenerationTests.Declaration(null);
            body["path"] = path;
            body["repository"] = repository.Id;
            body["source"] = source.Id;
            body["generator"]!["id"] = DependencyGenerator.Id;
            if (region && path == "docs/second.md")
            {
                body["kind"] = "authored";
                body.Remove("generator");
                body["triggers"] = new JsonArray(new JsonObject { ["on"] = "decision", ["level"] = "warn" });
                body["regions"] = new JsonArray(new JsonObject { ["id"] = "section", ["generator"] = DependencyGenerator.Id,
                    ["inputs"] = new JsonArray("format=1", "audience=Internal", "selection=all-revisions") });
            }
            return f.Add("living-document", body);
        }
        RecordEnvelope first = Add("docs/first.md");
        Add("docs/second.md");
        f.AssertValid();
        GeneratedDocumentPlan plan = GeneratedDocumentVerification.Plan(f.Estate.Load(), first, Commit);
        Assert.Equal(!cycle, plan.Current);
        if (cycle)
        {
            Assert.Equal(DocumentGenerationStatus.Error, plan.Status);
            Assert.Empty(plan.Expected);
            Assert.Contains(plan.Problems, p => p.Contains("cycle", StringComparison.Ordinal));
        }
    }

    private static DependencyRenderer InstallDependencyGenerator()
    {
        var generator = new DependencyRenderer();
        DocumentGenerators.Register(generator);
        return generator;
    }

    private sealed class DependencyRenderer : IDocumentGenerator
    {
        public string Id => "example.documents/dependency";
        public DocumentGeneratorDescriptor Descriptor { get; } = DocumentGenerationTests.Generator.Descriptor! with
        { Id = "example.documents/dependency", RecordTypes = [], SourceKinds = ["file"], PathMapping = "single" };
        public DocumentRenderResult Generate(DocumentGenerationContext context) => DocumentRenderResult.Success("Body\n");
    }

    private sealed class DependencySource(bool cycle, bool region) : IDocumentSource
    {
        public string Id => "example.documents/" + (cycle ? "cyclic" : "acyclic") + (region ? "-region-source" : "-source");
        public int SnapshotVersion => 1;
        public string? Head(EstateModel model, RecordEnvelope declaration) => null;
        public string? Resolve(EstateModel model, RecordEnvelope declaration, string reference) => reference;
        public byte[]? Read(EstateModel model, RecordEnvelope declaration, string commit) => null;
        public string? Covered(EstateModel model, RecordEnvelope declaration, IReadOnlyList<string> paths, string commit) => null;
        public bool Exists(EstateModel model, RecordEnvelope declaration, string commit, string path) => true;
        public DocumentSourceSelection ReadSelection(EstateModel model, RecordEnvelope declaration, string commit, DocumentGeneratorDescriptor generator)
        {
            string path = EstateModel.BodyString(declaration, "path") == "docs/first.md" ? "docs/second.md" : cycle ? "docs/first.md" : "sources/base.md";
            byte[] bytes = region && path == "docs/second.md" ? "<!-- vericairn:generated section -->\nBody\n<!-- vericairn:end section -->\n"u8.ToArray() : "Body\n"u8.ToArray();
            return new(DocumentGenerationStatus.Success, [DocumentSourceSnapshot.Content("file", path, EstateModel.BodyString(declaration, "repository")!, path,
                Vericairn.Records.Hashing.ContentHash.Sha256(bytes), bytes)], commit, null);
        }
        public DocumentFileInventory ReadInventory(EstateModel model, RecordEnvelope declaration, string commit) =>
            new(DocumentGenerationStatus.Success, [new(EstateModel.BodyString(declaration, "path")!, [.. "Body\n"u8.ToArray()])], commit, null);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void StrictRepositoryMembersUseOnlyTheRequestedCommitAndMissingCommitsCloseTheGate(bool matches)
    {
        var source = new SnapshotSource(matches);
        Assert.True(DocumentSources.Register(source));
        using var f = new PolicyFixture();
        RecordEnvelope repository = f.Estate.Add("resource", f.Estate.OperatorId, """{"kind":"repository","name":"Example","role":"source","location":{"github":"Example/example"},"visibility":"private"}""");
        RecordEnvelope product = f.Estate.Add("product", f.Estate.OperatorId, """{"name":"Example","slug":"example","summary":"A product.","editions":[{"name":"Community","slug":"community","summary":"Free."}],"surfaces":[{"name":"CLI","slug":"cli","summary":"Command line."}]}""");
        var body = DocumentGenerationTests.Declaration(null);
        body["source"] = source.Id;
        body["repository"] = repository.Id;
        RecordEnvelope declaration = f.Add("living-document", body);
        f.AssertValid();
        Assert.Equal(DocumentGenerationStatus.Unavailable, GeneratedDocumentVerification.Plan(f.Estate.Load(), declaration).Status);
        LivingDocuments.State state = Assert.Single(LivingDocuments.AtReleaseCommit(f.Estate.Load(), product.Id, Commit, new HashSet<string>(StringComparer.Ordinal) { repository.Id }));
        Assert.Equal(matches, state.Generation!.Current);
        Assert.Equal(!matches, state.Closes);
        Assert.Equal(Commit, state.ComparisonCommit);
        Assert.Null(state.Clearance);
    }

    private sealed class SnapshotSource(bool matches) : IDocumentSource
    {
        public string Id => matches ? "example.documents/exact-source" : "example.documents/wrong-source";
        public int SnapshotVersion => 1;
        public string? Head(EstateModel model, RecordEnvelope declaration) => throw new InvalidOperationException("An exact comparison cannot ask for clone HEAD.");
        public string? Resolve(EstateModel model, RecordEnvelope declaration, string reference) => reference;
        public byte[]? Read(EstateModel model, RecordEnvelope declaration, string commit) => throw new InvalidOperationException("The complete snapshot is required.");
        public string? Covered(EstateModel model, RecordEnvelope declaration, IReadOnlyList<string> paths, string commit) => null;
        public bool Exists(EstateModel model, RecordEnvelope declaration, string commit, string path) => true;
        public DocumentSourceSelection ReadSelection(EstateModel model, RecordEnvelope declaration, string commit, DocumentGeneratorDescriptor generator) =>
            DocumentSourceSelection.Records(model, generator, matches ? commit : "1111111111111111111111111111111111111111");
        public DocumentFileInventory ReadInventory(EstateModel model, RecordEnvelope declaration, string commit) =>
            new(DocumentGenerationStatus.Success, [new("docs/decisions.md", [.. Encoding.UTF8.GetBytes("Approve the stated definition only.\n")])], commit, null);
    }
}
