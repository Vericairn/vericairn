// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Collections.Immutable;
using System.Globalization;
using System.Text.Json.Nodes;
using Vericairn.Records.Estate;
using Vericairn.Records.Hashing;
using Vericairn.Records.Model;

namespace Vericairn.Records.Tests;

public class OnDemandCompanionTests
{
    [Theory]
    [MemberData(nameof(RevisionCompanionTests.Registers), MemberType = typeof(RevisionCompanionTests))]
    public void EachCollectionUsesTheOwningModelsExplicitSnapshotAndSelection(string id, string type, string directory)
    {
        Assert.NotEmpty(directory);
        EstateModel model = EstateLoader.Load(Path.Combine(RecordCompanionTests.FindRepository(), "tests", "fixtures", "format-3-policy-estate"));
        string snapshot = DocumentOnDemand.RecordSnapshot(model);
        foreach (string selection in new[] { "current", "all-revisions" })
        {
            RecordEnvelope[] expected = (selection == "current" ? model.Current(type) : model.OfType(type)).ToArray();
            string text = RecordCompanionTests.Text(DocumentOnDemand.Records(model, id, snapshot, selection));
            Assert.Contains(snapshot, text, StringComparison.Ordinal);
            Assert.Contains("Selection: " + selection, text, StringComparison.Ordinal);
            Assert.Contains("Selected revisions: " + expected.Length.ToString(CultureInfo.InvariantCulture), text, StringComparison.Ordinal);
            foreach (RecordEnvelope record in expected) Assert.Contains(record.ComputedHash, text, StringComparison.Ordinal);
            foreach (RecordEnvelope excluded in model.OfType(type).Except(expected)) Assert.DoesNotContain(excluded.ComputedHash, text, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void EmptySelectionIsDistinctFromUnavailableSnapshotAndConflict()
    {
        using var f = new PolicyFixture();
        EstateModel before = f.Estate.Load();
        string snapshot = DocumentOnDemand.RecordSnapshot(before);
        Assert.Contains("None (complete authorized selection)", RecordCompanionTests.Text(DocumentOnDemand.Records(before, "vericairn.records/releases", snapshot, "current")), StringComparison.Ordinal);
        f.Add("decision", f.DecisionBody());
        DocumentRendition stale = DocumentOnDemand.Records(f.Estate.Load(), "vericairn.records/releases", snapshot, "current");
        Assert.Equal(DocumentGenerationStatus.Unavailable, stale.Status);
        Assert.Empty(stale.Bytes); Assert.Null(stale.InputDigest); Assert.Null(stale.ContentHash);
        Assert.Equal(DocumentGenerationStatus.Error, DocumentOnDemand.Records(before, "vericairn.records/releases", snapshot, "guessed").Status);
        Assert.Equal(DocumentGenerationStatus.Unavailable, DocumentOnDemand.Records(before, "example.missing/reader", snapshot, "current").Status);
        f.Add("decision", f.DecisionBody(), supersedes: f.Decision.Id);
        f.Add("decision", f.DecisionBody(), supersedes: f.Decision.Id);
        EstateModel conflict = f.Estate.Load();
        Assert.Equal(DocumentGenerationStatus.Unavailable, DocumentOnDemand.Records(conflict, "vericairn.records/decisions", DocumentOnDemand.RecordSnapshot(conflict), "current").Status);
    }

    [Fact]
    public void ACollectionCannotRenderWithoutItsSnapshotOrBecomeACommittedIndex()
    {
        using var f = new PolicyFixture();
        IDocumentGenerator generator = DocumentGenerators.Find("vericairn.records/decisions")!;
        var parameters = new Dictionary<string, string> { ["format"] = "1", ["audience"] = "Internal", ["selection"] = "all-revisions" }.ToImmutableDictionary(StringComparer.Ordinal);
        DocumentRendition missing = DocumentRendering.Render(generator, new(1, generator.Descriptor!, parameters, "list", null, [DocumentSourceSnapshot.Record(f.Decision)]));
        Assert.Equal(DocumentGenerationStatus.Unavailable, missing.Status);
        JsonObject declaration = RevisionCompanionTests.Declaration(generator.Id);
        declaration.Remove("pathPattern"); declaration["path"] = "docs/index.md";
        var sources = DocumentSourceSelection.Records(f.Estate.Load(), generator.Descriptor!);
        Assert.Equal(DocumentGenerationStatus.Error, DocumentGeneration.Members(RevisionCompanionTests.Carrier(declaration), sources).Status);
        declaration = RevisionCompanionTests.Declaration(generator.Id);
        declaration["generator"]!["inputs"]![2] = "selection=current";
        Assert.Equal(DocumentGenerationStatus.Error, DocumentGeneration.Members(RevisionCompanionTests.Carrier(declaration), sources).Status);
    }

    [Fact]
    public void ManifestExplainsOriginalCharterAndCurrentFormatAndWritesNothing()
    {
        using var estate = new TestEstate();
        estate.MoveTo(3);
        JsonObject charter = PolicyFixture.Body(estate.Estate);
        charter["purpose"] = "A revised, exact purpose.";
        RecordEnvelope revised = estate.Add("estate", estate.OperatorId, charter.ToJsonString(), supersedes: estate.Estate.Id);
        EstateModel model = estate.Load();
        Assert.False(model.Findings.HasErrors, string.Join('\n', model.Findings));
        byte[] manifest = File.ReadAllBytes(Path.Combine(estate.Root, EstateLayout.ManifestPath));
        var before = Directory.EnumerateFiles(estate.Root, "*", SearchOption.AllDirectories).ToDictionary(p => p, ContentHash.Sha256File, StringComparer.Ordinal);
        string snapshot = DocumentOnDemand.ManifestSnapshot(model, manifest);
        string text = System.Net.WebUtility.HtmlDecode(RecordCompanionTests.Text(DocumentOnDemand.Manifest(model, manifest, snapshot)));
        Assert.Contains("Manifest record format: 3", text, StringComparison.Ordinal);
        Assert.Contains("envelope format 1", text, StringComparison.Ordinal);
        Assert.Contains("Original charter", text, StringComparison.Ordinal);
        Assert.Contains("Selected current charter", text, StringComparison.Ordinal);
        Assert.Contains(revised.ComputedHash, text, StringComparison.Ordinal);
        Assert.Contains("A revised, exact purpose.", text, StringComparison.Ordinal);
        Assert.Contains("manifest format default", text, StringComparison.Ordinal);
        Assert.Equal(before.Keys.Order(StringComparer.Ordinal), Directory.EnumerateFiles(estate.Root, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal));
        foreach ((string path, string hash) in before) Assert.Equal(hash, ContentHash.Sha256File(path));
        Assert.Empty(model.OfType("living-document"));
        Assert.Empty(model.OfType("document-revision"));
        Assert.False(Directory.Exists(Path.Combine(estate.Root, "docs", "registers")));
    }

    [Fact]
    public void ManifestRejectsMismatchedBytesAndCannotAcquireRepositoryOwnership()
    {
        using var estate = new TestEstate();
        EstateModel model = estate.Load();
        byte[] bytes = File.ReadAllBytes(Path.Combine(estate.Root, EstateLayout.ManifestPath));
        Assert.Equal(DocumentGenerationStatus.Unavailable, DocumentOnDemand.Manifest(model, bytes, "sha256:" + new string('a', 64)).Status);
        byte[] invalid = "{}\n"u8.ToArray();
        Assert.Equal(DocumentGenerationStatus.Error, DocumentOnDemand.Manifest(model, invalid, DocumentOnDemand.ManifestSnapshot(model, invalid)).Status);
        JsonObject body = new() { ["path"] = "docs/estate.md", ["generator"] = new JsonObject { ["id"] = "vericairn.estate/manifest", ["inputs"] = new JsonArray("format=1", "audience=Internal") } };
        DocumentMemberSelection refused = DocumentGeneration.Members(RevisionCompanionTests.Carrier(body), new(DocumentGenerationStatus.Success, [], null, null));
        Assert.Equal(DocumentGenerationStatus.Error, refused.Status);
        Assert.Empty(refused.Members);
        Assert.Empty(DocumentCoverage.Check(ManifestCompanionGenerator.Inventory, DocumentCoverage.All.Where(d => d.Kind.StartsWith("estate/manifest", StringComparison.Ordinal))));
    }
}
