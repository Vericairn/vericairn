// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text;
using Vericairn.Records.Estate;
using Vericairn.Records.Model;

namespace Vericairn.Records.Tests;

public class GeneratedDocumentSourceTests
{
    private const string Commit = "0123456789abcdef0123456789abcdef01234567";

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
