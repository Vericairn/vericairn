// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text;
using System.Text.Json.Nodes;
using Vericairn.Records.Authoring;
using Vericairn.Records.Canonical;
using Vericairn.Records.Estate;
using Vericairn.Records.Hashing;
using Vericairn.Records.Model;

namespace Vericairn.Records.Tests;

public class CompanionImplementationTests
{
    public static IEnumerable<object[]> Generators => RevisionCompanionGenerator.Registers.Select(r => new object[] { r.Id })
        .Concat(new[] { new object[] { "vericairn.estate/manifest" }, new object[] { "vericairn.records/items" } });

    [Fact]
    public void ImplementationHashesDependOnlyOnIndependentExplicitVersionsAndNoSourceResources()
    {
        Assert.Empty(typeof(DocumentGenerators).Assembly.GetManifestResourceNames());
        DocumentGeneratorDescriptor[] descriptors = Generators.Select(g => DocumentGenerators.Find((string)g[0])!.Descriptor!).ToArray();
        Assert.Equal(descriptors.Length, descriptors.Select(d => d.Implementation).Distinct(StringComparer.Ordinal).Count());
        foreach (DocumentGeneratorDescriptor descriptor in descriptors)
        {
            string prefix = descriptor.Id == "vericairn.records/items" ? "vericairn.records/readers/" : descriptor.Id + "/";
            Assert.StartsWith(prefix, descriptor.Implementation, StringComparison.Ordinal);
            Assert.Matches("^[1-9][0-9]*$", descriptor.Implementation[prefix.Length..]);
            Assert.Equal(ContentHash.Sha256(Encoding.UTF8.GetBytes(descriptor.Implementation)), descriptor.ImplementationHash);
        }
    }

    [Theory]
    [MemberData(nameof(Generators))]
    public void EmittedBytesMatchTheFixturesForThatImplementationVersion(string id) =>
        CompanionGoldenFixtures.Check(DocumentGenerators.Find(id)!.Descriptor!, Cases(id));

    [Fact]
    public void RegenerationCannotReplaceAnExistingImplementationVersionsGoldenBytes()
    {
        DocumentGeneratorDescriptor descriptor = DocumentGenerators.Find("vericairn.records/decisions")!.Descriptor!;
        Assert.Throws<InvalidOperationException>(() => CompanionGoldenFixtures.WriteNewVersion(descriptor, Cases(descriptor.Id)));
    }

    /// <summary>Stable synthetic inputs shared by the comparison and explicit fixture maintenance.</summary>
    public static IReadOnlyDictionary<string, DocumentRendition> Cases(string id)
    {
        string root = Path.Combine(RecordCompanionTests.FindRepository(), "tests", "fixtures", "format-3-policy-estate");
        EstateModel model = EstateLoader.Load(root);
        Assert.False(model.Findings.HasErrors, string.Join('\n', model.Findings));
        IDocumentGenerator generator = DocumentGenerators.Find(id)!;
        var cases = new Dictionary<string, DocumentRendition>(StringComparer.Ordinal);
        if (id == "vericairn.estate/manifest")
        {
            byte[] manifest = File.ReadAllBytes(Path.Combine(root, EstateLayout.ManifestPath));
            cases.Add("manifest.md", DocumentOnDemand.Manifest(model, manifest, DocumentOnDemand.ManifestSnapshot(model, manifest)));
            cases.Add("migrated-release-manifest.md", MigratedManifest());
        }
        else if (id == "vericairn.records/items")
        {
            foreach (RecordEnvelope record in model.Records.Where(r => r.Type is "result" or "review"))
                cases.Add(record.Type + "-" + record.Id + ".md", RecordCompanionTests.Render([DocumentSourceSnapshot.Record(record)]));
            DocumentSourceSnapshot source = DocumentSourceSnapshot.Record(model.OfType("result").First(r => r.Body.TryGetProperty("duties", out _)));
            cases.Add("duties-list.md", RecordCompanionTests.Render([source], "list", "duties"));
            cases.Add("duties-item.md", RecordCompanionTests.Render([source], "item", "duties", "1"));
            cases.Add("list.md", RecordCompanionTests.Render([source], "list"));
        }
        else
        {
            DocumentSourceSelection selected = DocumentSourceSelection.Records(model, generator.Descriptor!);
            DocumentMemberSelection members = DocumentGeneration.Members(RevisionCompanionTests.Carrier(id), selected);
            Assert.Equal(DocumentGenerationStatus.Success, members.Status);
            Assert.NotEmpty(members.Members);
            foreach (DocumentMember member in members.Members)
                cases.Add("revisions/" + Path.GetFileName(member.Path), DocumentRendering.Render(generator, member.Context));
            foreach (string selection in new[] { "current", "all-revisions" })
                cases.Add(selection + ".md", DocumentOnDemand.Records(model, id, DocumentOnDemand.RecordSnapshot(model), selection));

            DocumentSourceSnapshot original = selected.Sources[0];
            JsonObject body = JsonNode.Parse(original.Body!.Value.GetRawText())!.AsObject();
            string field = original.RecordType switch { "decision" => "statement", "work-order" => "intent", "release" => "signing", _ => "reason" };
            body[field] = new string('|', 200000);
            RecordEnvelope large = RecordAuthor.Create(new RecordDraft(original.RecordType!, original.Author!, PolicyFixture.Element(body), original.Subject,
                At: model.ById[original.Identity].AtTime, Id: original.Identity, Format: original.RecordFormat)).Envelope;
            DocumentMember overview = Assert.Single(DocumentGeneration.Members(RevisionCompanionTests.Carrier(id),
                new(DocumentGenerationStatus.Success, [DocumentSourceSnapshot.Record(large)], null, null)).Members);
            cases.Add("partial-overview.md", DocumentRendering.Render(generator, overview.Context));
            Assert.Contains("## Partial overview", RecordCompanionTests.Text(cases["partial-overview.md"]), StringComparison.Ordinal);
        }
        return cases;
    }

    private static DocumentRendition MigratedManifest()
    {
        EstateModel original = EstateLoader.Load(Path.Combine(RecordCompanionTests.FindRepository(), "tests", "fixtures", "format-1-estate"));
        Assert.False(original.Findings.HasErrors, string.Join('\n', original.Findings));
        RecordEnvelope charter = original.CurrentEstate!;
        JsonObject body = PolicyFixture.Body(charter);
        body["purpose"] = "A revised purpose, retained at this exact revision.";
        var at = new DateTimeOffset(2026, 11, 1, 0, 0, 0, TimeSpan.Zero);
        RecordEnvelope current = RecordAuthor.Create(new RecordDraft("estate", charter.By, PolicyFixture.Element(body), Supersedes: charter.Id,
            At: at, Id: "01ARZ3NDEKTSV4RRFFQ69G5FAX", Format: 3)).Envelope;
        RecordEnvelope migration = RecordAuthor.Create(new RecordDraft("migration", charter.By, PolicyFixture.Element(new JsonObject
        {
            ["fromPin"] = "release 1.2.2", ["toPin"] = "release 1.2.3", ["fromFormat"] = 1, ["toFormat"] = 3,
            ["migrations"] = new JsonArray("record-format-2", "record-format-3"), ["changes"] = new JsonArray("Preserved original record envelopes."),
        }), At: at, Id: "01ARZ3NDEKTSV4RRFFQ69G5FAY", Format: 3)).Envelope;
        RecordEnvelope[] records = [.. original.Records, current, migration];
        var successors = new Dictionary<string, string>(original.SupersededBy, StringComparer.Ordinal) { [charter.Id] = current.Id };
        byte[] manifest = CanonicalJson.SerializePrettyUtf8(PolicyFixture.Element(new JsonObject
        {
            ["estate"] = charter.Id, ["format"] = EstateManifest.Format1, ["recordFormat"] = 3, ["profile"] = "local-only",
            ["vericairn"] = new JsonObject
            {
                ["mode"] = "release", ["version"] = "1.2.3", ["repository"] = "Example/runtime",
                ["artifacts"] = new JsonObject { ["win-x64"] = "sha256:" + new string('2', 64), ["linux-x64"] = "sha256:" + new string('1', 64) },
            },
        }));
        var findings = new FindingList();
        var migrated = new EstateModel(original.Root, records, records.ToDictionary(r => r.Id, StringComparer.Ordinal), successors, findings)
        { Manifest = EstateManifest.Parse(manifest, findings) };
        Assert.False(findings.HasErrors, string.Join('\n', findings));
        return DocumentOnDemand.Manifest(migrated, manifest, DocumentOnDemand.ManifestSnapshot(migrated, manifest));
    }
}
