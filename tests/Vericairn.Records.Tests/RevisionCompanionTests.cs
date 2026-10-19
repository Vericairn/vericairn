// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using Vericairn.Records.Authoring;
using Vericairn.Records.Estate;
using Vericairn.Records.Hashing;
using Vericairn.Records.Model;

namespace Vericairn.Records.Tests;

public class RevisionCompanionTests
{
    public static IEnumerable<object[]> Registers => RevisionCompanionGenerator.Registers.Select(r => new object[] { r.Id, r.Type, r.Directory });

    [Theory]
    [MemberData(nameof(Registers))]
    [Trait("Journey", "D19-A")]
    public void EachGeneratorMapsExactRevisionsAndRetainsAllRecordedTerms(string id, string type, string directory)
    {
        EstateModel model = EstateLoader.Load(Path.Combine(RecordCompanionTests.FindRepository(), "tests", "fixtures", "format-3-policy-estate"));
        RecordEnvelope declaration = Carrier(id);
        IDocumentGenerator generator = DocumentGenerators.Find(id)!;
        DocumentSourceSelection selected = DocumentSourceSelection.Records(model, generator.Descriptor!);
        DocumentMemberSelection members = DocumentGeneration.Members(declaration, selected);
        Assert.Equal(DocumentGenerationStatus.Success, members.Status);
        Assert.Equal(model.OfType(type).Count(), members.Members.Length);
        foreach (DocumentMember member in members.Members)
        {
            DocumentSourceSnapshot source = member.Context.Sources.Single(s => !member.Context.Sources.Any(other => other.Supersedes == s.Identity));
            Assert.Equal("docs/registers/" + directory + "/" + source.At![..7] + "/" + source.At[..10] + "-" + DocumentPatterns.Slug(source.Title!) + "-" + source.Identity + ".md", member.Path);
            string text = RecordCompanionTests.Text(DocumentRendering.Render(generator, member.Context));
            Assert.Contains("](../../../../" + source.Path + ")", text, StringComparison.Ordinal);
            Assert.Contains(source.Hash, text, StringComparison.Ordinal);
            foreach (var field in source.Body!.Value.EnumerateObject()) Assert.Contains("## " + RecordCompanionText.Label(field.Name) + "\n", text, StringComparison.Ordinal);
            if (source.Supersedes is { } previous)
                Assert.Contains("](../../../../" + model.ById[previous].ExpectedPath + ")", text, StringComparison.Ordinal);
        }
    }

    [Fact]
    [Trait("Journey", "D19-B")]
    public void FiveIndependentBranchesAddFiveFilesAndTheirUnionPassesWithoutRegenerationOrPins()
    {
        using var f = new PolicyFixture();
        RecordEnvelope declaration = f.Add("living-document", Declaration("vericairn.records/decisions"));
        GeneratedDocumentPlan baseline = GeneratedDocumentVerification.Plan(f.Estate.Load(), declaration);
        Write(f, baseline.Expected);
        var earlier = Directory.EnumerateFiles(f.Estate.Root, "*", SearchOption.AllDirectories).ToDictionary(p => p, ContentHash.Sha256File, StringComparer.Ordinal);
        RecordFile[] common = EstateLoader.EnumerateRecordFiles(f.Estate.Root).ToArray();
        var records = new List<AuthoredRecord>();
        var additions = new List<DocumentFile>();
        for (int branch = 0; branch < 5; branch++)
        {
            JsonObject body = f.DecisionBody();
            body["title"] = "Equal title 東京";
            body["statement"] = "Independent addition " + branch.ToString(CultureInfo.InvariantCulture) + ".";
            AuthoredRecord record = RecordAuthor.Create(new RecordDraft("decision", f.Estate.OperatorId, PolicyFixture.Element(body), At: f.Estate.Clock.AddSeconds(1), Format: 3), [f.OperatorSigner]);
            records.Add(record);
            EstateModel branchModel = EstateLoader.Load(f.Estate.Root, [.. common, new(record.RelativePath, record.Bytes)]);
            GeneratedDocumentPlan branchPlan = GeneratedDocumentVerification.Plan(branchModel, declaration);
            Assert.Equal(DocumentGenerationStatus.Success, branchPlan.Status);
            DocumentOutputChange change = Assert.Single(DocumentRepair.Changes(baseline.Expected, branchPlan.Expected));
            Assert.Null(change.BeforeHash);
            additions.Add(new(change.Path, change.After!.Value));
            Assert.Single(branchModel.OfType("living-document"));
            Assert.Empty(branchModel.OfType("document-revision"));
        }
        Assert.Equal(5, additions.Select(a => a.Path).Distinct(StringComparer.Ordinal).Count());
        foreach (AuthoredRecord record in records) RecordAuthor.Write(f.Estate.Root, record);
        Write(f, additions);
        EstateModel union = f.Estate.Load();
        Assert.False(union.Findings.HasErrors, string.Join('\n', union.Findings));
        Assert.True(GeneratedDocumentVerification.Plan(union, declaration).Current);
        Assert.False(EstateChecks.Run(union).HasErrors, string.Join('\n', EstateChecks.Run(union)));
        Assert.Single(union.OfType("living-document"));
        Assert.Empty(union.OfType("document-revision"));
        foreach ((string path, string hash) in earlier) Assert.Equal(hash, ContentHash.Sha256File(path));
        Assert.Equal(baseline.Expected.Length + 5, Directory.EnumerateFiles(Path.Combine(f.Estate.Root, "docs", "registers"), "*.md", SearchOption.AllDirectories).Count());
    }

    [Fact]
    [Trait("Journey", "D19-B")]
    public void DivergentSuccessorsHaveDisjointHistoricalFilesWithoutChoosingTheCurrentBranch()
    {
        using var f = new PolicyFixture();
        RecordEnvelope declaration = f.Add("living-document", Declaration("vericairn.records/decisions"));
        DocumentSourceSnapshot original = DocumentSourceSnapshot.Record(f.Decision);
        var sources = new List<DocumentSourceSnapshot> { original };
        for (int branch = 0; branch < 2; branch++)
        {
            JsonObject body = f.DecisionBody();
            body["title"] = "Successor " + branch.ToString(CultureInfo.InvariantCulture);
            sources.Add(DocumentSourceSnapshot.Record(f.Add("decision", body, supersedes: original.Identity)));
        }
        Assert.True(f.Estate.Load().Findings.HasErrors);
        DocumentMemberSelection members = DocumentGeneration.Members(declaration, new(DocumentGenerationStatus.Success, [.. sources], null, null));
        Assert.Equal(DocumentGenerationStatus.Success, members.Status);
        Assert.Equal(3, members.Members.Select(m => m.Path).Distinct(StringComparer.Ordinal).Count());
        foreach (DocumentMember member in members.Members) Assert.Equal(DocumentGenerationStatus.Success, DocumentRendering.Render(DocumentGenerators.Find("vericairn.records/decisions")!, member.Context).Status);
        Assert.Equal(DocumentGenerationStatus.Unavailable, DocumentSourceSelection.Records(f.Estate.Load(), DocumentGenerators.Find("vericairn.records/decisions")!.Descriptor!).Status);
    }

    [Fact]
    [Trait("Journey", "D19-D")]
    public void NewMonthEqualAndNonAsciiTitlesDoNotChangeExistingPathsOrInputs()
    {
        using var f = new PolicyFixture();
        RecordEnvelope declaration = f.Add("living-document", Declaration("vericairn.records/decisions"));
        GeneratedDocumentPlan before = GeneratedDocumentVerification.Plan(f.Estate.Load(), declaration);
        f.Estate.SetClock(new(2026, 11, 1, 0, 0, 0, TimeSpan.Zero));
        foreach (string title in new[] { "東京", "東京", new string('A', 80) })
        {
            JsonObject body = f.DecisionBody(); body["title"] = title; f.Add("decision", body);
        }
        GeneratedDocumentPlan after = GeneratedDocumentVerification.Plan(f.Estate.Load(), declaration);
        Assert.Equal(4, after.Expected.Length);
        Assert.Equal(2, after.Expected.Count(m => m.Path.Contains("/2026-11/2026-11-01-item-", StringComparison.Ordinal)));
        Assert.Contains(after.Expected, m => m.Path.Contains("-" + new string('a', 60) + "-", StringComparison.Ordinal));
        Assert.Equal(before.Expected[0].Hash, after.Expected.Single(m => m.Path == before.Expected[0].Path).Hash);
        Assert.Single(f.Estate.Load().OfType("living-document"));
    }

    [Fact]
    [Trait("Journey", "D19-D")]
    public void NearLimitItemsStayCompleteAndOversizedRenditionsGiveAnHonestBoundedOverview()
    {
        using var f = new PolicyFixture();
        foreach ((char value, bool partial) in new[] { ('a', false), ('|', true) })
        {
            JsonObject body = f.DecisionBody();
            body["effects"] = new JsonArray(Enumerable.Repeat(new string(value, 2000), 450).Select(s => JsonValue.Create(s)).ToArray());
            RecordEnvelope record = f.Add("decision", body);
            DocumentMember member = Assert.Single(DocumentGeneration.Members(Carrier("vericairn.records/decisions"), new(DocumentGenerationStatus.Success, [DocumentSourceSnapshot.Record(record)], null, null)).Members);
            DocumentRendition result = DocumentRendering.Render(DocumentGenerators.Find("vericairn.records/decisions")!, member.Context);
            string text = RecordCompanionTests.Text(result);
            Assert.Equal(partial, text.Contains("## Partial overview", StringComparison.Ordinal));
            Assert.InRange(result.Bytes.Length, partial ? 1 : 900000, 1048576);
            Assert.Contains(record.ComputedHash, text, StringComparison.Ordinal);
            if (partial) Assert.DoesNotContain("## Statement\n", text, StringComparison.Ordinal);
        }
    }

    [Fact]
    [Trait("Journey", "D19-C")]
    [Trait("Journey", "D19-D")]
    public void HandEditsCrlfMissingExtraMembersAndCaseAliasesFailFreshVerification()
    {
        using var f = new PolicyFixture();
        RecordEnvelope declaration = f.Add("living-document", Declaration("vericairn.records/decisions"));
        DocumentFile expected = Assert.Single(GeneratedDocumentVerification.Plan(f.Estate.Load(), declaration).Expected);
        Assert.Contains(GeneratedDocumentVerification.Plan(f.Estate.Load(), declaration).Problems, p => p.StartsWith("Missing", StringComparison.Ordinal));
        Write(f, [expected]);
        Assert.True(GeneratedDocumentVerification.Plan(f.Estate.Load(), declaration).Current);
        f.Estate.WriteFile(expected.Path, Encoding.UTF8.GetString(expected.Bytes.AsSpan()).Replace("\n", "\r\n", StringComparison.Ordinal));
        Assert.False(GeneratedDocumentVerification.Plan(f.Estate.Load(), declaration).Current);
        f.Estate.WriteFile(expected.Path, "Hand edit.\n");
        Assert.False(GeneratedDocumentVerification.Plan(f.Estate.Load(), declaration).Current);
        Write(f, [expected]);
        string extra = "docs/registers/decisions/extra.md";
        f.Estate.WriteFile(extra, "Unowned.\n");
        Assert.Contains(GeneratedDocumentVerification.Plan(f.Estate.Load(), declaration).Problems, p => p == "Unexpected generated member: " + extra);
        File.Delete(Path.Combine(f.Estate.Root, extra));
        string alias = "docs/registers/decisions/EXTRA.md";
        f.Estate.WriteFile(alias, "Alias.\n");
        f.Estate.WriteFile(extra, "Alias.\n");
        Assert.False(GeneratedDocumentVerification.Plan(f.Estate.Load(), declaration).Current);
    }

    [Fact]
    [Trait("Journey", "D19-D")]
    [Trait("Journey", "D19-I")]
    public void MissingCapabilitiesUnknownInputsAndWrongDestinationsReturnNoMembers()
    {
        using var f = new PolicyFixture();
        var selected = new DocumentSourceSelection(DocumentGenerationStatus.Success, [DocumentSourceSnapshot.Record(f.Decision)], null, null);
        JsonObject body = Declaration("vericairn.records/decisions");
        body["pathPattern"] = "docs/elsewhere/{month}/{date}-{slug}-{id}.md";
        Assert.Equal(DocumentGenerationStatus.Error, DocumentGeneration.Members(Carrier(body), selected).Status);
        body = Declaration("vericairn.records/decisions"); body["generator"]!["inputs"]![0] = "format=99";
        Assert.Equal(DocumentGenerationStatus.Unavailable, DocumentGeneration.Members(Carrier(body), selected).Status);
        body = Declaration("vericairn.records/decisions"); body["generator"]!["inputs"]!.AsArray().Add("extra=x");
        Assert.Equal(DocumentGenerationStatus.Error, DocumentGeneration.Members(Carrier(body), selected).Status);
        body = Declaration("vericairn.records/decisions"); body["generator"]!["id"] = "example.missing/reader";
        Assert.Equal(DocumentGenerationStatus.Unavailable, DocumentGeneration.Members(Carrier(body), selected).Status);
        Assert.Empty(DocumentGeneration.Members(Carrier("vericairn.records/decisions"), selected with { Status = DocumentGenerationStatus.Unavailable }).Members);
        DocumentMember member = Assert.Single(DocumentGeneration.Members(Carrier("vericairn.records/decisions"), selected).Members);
        DocumentRendition mislabeled = DocumentRendering.Render(DocumentGenerators.Find("vericairn.records/decisions")!, member.Context with { Parameters = member.Context.Parameters.SetItem("selection", "current") });
        Assert.Equal(DocumentGenerationStatus.Error, mislabeled.Status);
        Assert.Empty(mislabeled.Bytes); Assert.Null(mislabeled.InputDigest); Assert.Null(mislabeled.ContentHash);
        JsonObject successor = f.DecisionBody();
        RecordEnvelope revision = f.Add("decision", successor, supersedes: f.Decision.Id);
        Assert.Equal(DocumentGenerationStatus.Unavailable, DocumentGeneration.Members(Carrier("vericairn.records/decisions"), selected with { Sources = [DocumentSourceSnapshot.Record(revision)] }).Status);
    }

    [Fact]
    [Trait("Journey", "D19-I")]
    public void ReleasesKeepTheirLegacyRegionGrammarAndBytes()
    {
        using var f = new PolicyFixture();
        IDocumentGenerator generator = DocumentGenerators.Find("vericairn.records/releases")!;
        Assert.Equal("| Version | Published | Release |\n|---|---|---|\n", generator.Generate(f.Estate.Load(), "releases", []));
        Assert.Null(generator.Generate(f.Estate.Load(), null, []));
        Assert.NotNull(generator.Descriptor);
    }

    internal static JsonObject Declaration(string id)
    {
        DocumentGeneratorDescriptor descriptor = DocumentGenerators.Find(id)!.Descriptor!;
        return new JsonObject
        {
            ["pathPattern"] = descriptor.RequiredPattern, ["title"] = "Recorded revisions", ["kind"] = "generated", ["source"] = "estate-file",
            ["generator"] = new JsonObject { ["id"] = id, ["inputs"] = new JsonArray("format=1", "audience=Internal", "selection=all-revisions") },
            ["triggers"] = new JsonArray(), ["checks"] = new JsonArray("matches-generator", "links-resolve"), ["summary"] = "One file per exact revision.",
        };
    }

    internal static RecordEnvelope Carrier(string id) => Carrier(Declaration(id));
    internal static RecordEnvelope Carrier(JsonObject body) => RecordAuthor.Create(new RecordDraft("living-document", "01ARZ3NDEKTSV4RRFFQ69G5FAV", PolicyFixture.Element(body), Format: 3)).Envelope;

    private static void Write(PolicyFixture f, IEnumerable<DocumentFile> files)
    {
        foreach (DocumentFile file in files)
        {
            string path = Path.Combine(f.Estate.Root, file.Path);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, file.Bytes.ToArray());
        }
    }
}
