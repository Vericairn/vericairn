// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Collections.Immutable;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Vericairn.Protocol;
using Vericairn.Records.Authoring;
using Vericairn.Records.Canonical;
using Vericairn.Records.Estate;
using Vericairn.Records.Hashing;
using Vericairn.Records.Model;
using Vericairn.Records.Tests;
using Vericairn.Runtime.Service;
using Vericairn.Runtime.Store;

namespace Vericairn.Runtime.Tests;

public class GeneratedDocumentWriteTests
{
    private static readonly DecisionRenderer Renderer = Install();

    [Fact]
    public void LocalOnlyRecordsStayAuthoritativeAndRenderOnDemandWithoutRepositoryCompanions()
    {
        using var f = new Fixture(localOnly: true);
        f.Decision("Local decision", "Recorded locally.");
        EstateModel model = f.Service.Session.Load();
        DocumentSourceSelection sources = DocumentSourceSelection.Records(model, Renderer.Descriptor);
        var parameters = new Dictionary<string, string> { ["format"] = "1", ["audience"] = "Internal", ["selection"] = "all-revisions" }.ToImmutableDictionary(StringComparer.Ordinal);
        DocumentRendition rendition = DocumentRendering.Render(Renderer, new(1, Renderer.Descriptor, parameters, "item", null, sources.Sources));
        Assert.Equal(DocumentGenerationStatus.Success, rendition.Status);
        Assert.Contains("Recorded locally&#46;", Encoding.UTF8.GetString(rendition.Bytes.AsSpan()), StringComparison.Ordinal);
        WriteOutcome refused = f.Service.NewRecord(new("living-document", f.Estate.OperatorId, Fixture.Element(Fixture.Declaration())));
        Assert.False(refused.Ok);
        Assert.Null(refused.Written);
        Assert.Equal("local-only", f.Service.Session.Profile);
        Assert.Single(f.Service.Session.Load().OfType("decision"));
        Assert.Empty(f.Service.Session.Load().OfType("living-document"));
        Assert.False(Directory.Exists(Path.Combine(f.Estate.Root, "records")));
        Assert.Empty(Outputs(f.Estate.Root));
    }

    [Fact]
    public void SourceWritesAddTheirOwnMembersWithoutPinsOrRewritingEarlierRevisions()
    {
        using var f = new Fixture();
        f.Decision("First", "First statement.");
        RecordWritten declaration = f.Declare();
        string first = Assert.Single(Outputs(f.Estate.Root).Keys);
        byte[] original = File.ReadAllBytes(Path.Combine(f.Estate.Root, first));
        f.Decision("Second", "Second statement.");
        Assert.Equal(2, Outputs(f.Estate.Root).Count);
        Assert.Equal(original, File.ReadAllBytes(Path.Combine(f.Estate.Root, first)));
        Assert.Contains("First statement&#46;", Encoding.UTF8.GetString(original), StringComparison.Ordinal);
        EstateModel model = f.Service.Session.Load();
        Assert.Empty(model.OfType("document-revision"));
        Assert.Empty(model.OfType("document-review"));
        Assert.True(GeneratedDocumentVerification.Plan(model, model.ById[declaration.Id]).Current);
        Assert.Empty(BatchJournal.All(f.Service.Session).Journals);
    }

    [Theory]
    [InlineData("Refuse generation")]
    [InlineData("Unavailable generation")]
    [InlineData("Throw during generation")]
    public void AFailedLaterMemberWritesNoDeclarationRecordOrPartialOutput(string title)
    {
        using var f = new Fixture();
        f.Decision("A valid first", "A valid statement.");
        f.Decision(title, "This member cannot be rendered.");
        Dictionary<string, string> before = Records(f.Estate.Root);
        WriteOutcome refused = f.Service.NewRecord(new("living-document", f.Estate.OperatorId, Fixture.Element(Fixture.Declaration())));
        Assert.False(refused.Ok);
        Assert.Null(refused.Written);
        Assert.Equal("generated-refused", refused.Problem!.Code);
        Assert.Equal(before, Records(f.Estate.Root));
        Assert.Empty(Outputs(f.Estate.Root));
        Assert.Empty(BatchJournal.All(f.Service.Session).Journals);
    }

    [Fact]
    public void ASourceThatFailsGenerationIsNotStoredAndExistingOutputBytesSurvive()
    {
        using var f = new Fixture();
        f.Decision("First", "First statement.");
        f.Declare();
        Dictionary<string, string> records = Records(f.Estate.Root);
        Dictionary<string, string> outputs = Outputs(f.Estate.Root);
        WriteOutcome refused = f.Service.NewRecord(new("decision", f.Estate.OperatorId, Fixture.Element(f.DecisionBody("Throw during generation", "Never written."))));
        Assert.False(refused.Ok);
        Assert.Null(refused.Written);
        Assert.Equal(records, Records(f.Estate.Root));
        Assert.Equal(outputs, Outputs(f.Estate.Root));
    }

    [Fact]
    public void InterruptedPublicationReportsTheStoredRecordAndRecoversTheExactRemainingOutputs()
    {
        using var f = new Fixture();
        f.Decision("First", "First statement.");
        f.Decision("Second", "Second statement.");
        int published = 0;
        f.Service.BeforePublishingDocuments = _ => { if (++published == 2) throw new IOException("Injected publication interruption."); };
        WriteOutcome interrupted = f.Service.NewRecord(new("living-document", f.Estate.OperatorId, Fixture.Element(Fixture.Declaration())));
        Assert.False(interrupted.Ok);
        Assert.NotNull(interrupted.Written);
        Assert.Equal("reconcile-required", interrupted.Problem!.Code);
        Assert.Contains(interrupted.Written.Id, interrupted.Problem.Message, StringComparison.Ordinal);
        Assert.Single(Outputs(f.Estate.Root));
        BatchJournal journal = Assert.Single(BatchJournal.All(f.Service.Session).Journals);
        Assert.Equal(2, journal.Outputs.Count);
        byte[] signed = File.ReadAllBytes(Path.Combine(f.Estate.Root, interrupted.Written.Path));
        f.Reopen();
        Assert.Equal(2, Outputs(f.Estate.Root).Count);
        Assert.Equal(signed, File.ReadAllBytes(Path.Combine(f.Estate.Root, interrupted.Written.Path)));
        Assert.Empty(BatchJournal.All(f.Service.Session).Journals);
        Assert.True(GeneratedDocumentVerification.Plan(f.Service.Session.Load(), f.Service.Session.Load().ById[interrupted.Written.Id]).Current);
        Assert.Empty(f.Service.Records("document-revision"));
    }

    [Fact]
    public void RecoveryPreservesAnOutputChangedAfterTheInterruptionAndHoldsFurtherWrites()
    {
        using var f = new Fixture();
        f.Decision("First", "First statement.");
        f.Service.BeforePublishingDocuments = _ => throw new IOException("Injected interruption.");
        WriteOutcome interrupted = f.Service.NewRecord(new("living-document", f.Estate.OperatorId, Fixture.Element(Fixture.Declaration())));
        Assert.NotNull(interrupted.Written);
        DocumentOutputChange change = Assert.Single(Assert.Single(BatchJournal.All(f.Service.Session).Journals).Outputs);
        string full = Path.Combine(f.Estate.Root, change.Path);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, "Preserve this unexpected content.\n");
        f.Reopen();
        Assert.Equal("Preserve this unexpected content.\n", File.ReadAllText(full));
        WriteOutcome held = f.Service.NewRecord(new("decision", f.Estate.OperatorId, Fixture.Element(f.DecisionBody("Later", "Held."))));
        Assert.Equal("reconcile-required", held.Problem!.Code);
        Assert.Null(held.Written);
        Assert.Single(BatchJournal.All(f.Service.Session).Journals);
    }

    [Fact]
    public void RetirementRemovesOnlyVerifiedMembersAndKeepsTheSourceHistory()
    {
        using var f = new Fixture();
        f.Decision("First", "First statement.");
        RecordWritten declaration = f.Declare();
        JsonObject retirement = new() { ["path"] = "docs/generated", ["reason"] = "End this register.", ["condition"] = "The operator retires its generated files.", ["retiredAt"] = "2026-10-18T12:00:00Z" };
        WriteOutcome retired = f.Service.NewRecord(new("retirement", f.Estate.OperatorId, Fixture.Element(retirement)));
        Assert.True(retired.Ok, JsonSerializer.Serialize(retired.Problem));
        Assert.False(Directory.Exists(Path.Combine(f.Estate.Root, "docs/generated")));
        EstateModel model = f.Service.Session.Load();
        Assert.True(GeneratedDocumentVerification.Plan(model, model.ById[declaration.Id]).Current);
        Assert.Single(model.OfType("decision"));
        Assert.Single(model.OfType("living-document"));
        Assert.DoesNotContain(EstateChecks.Run(model), finding => finding.Code.StartsWith("document.generated", StringComparison.Ordinal) || finding.Code == "retirement.present");
    }

    [Fact]
    public void UnexpectedMembersPreventRetirementAndAreNeverPrunedByRepair()
    {
        using var f = new Fixture();
        f.Decision("First", "First statement.");
        RecordWritten declaration = f.Declare();
        File.WriteAllText(Path.Combine(f.Estate.Root, "docs/generated/unowned.md"), "Unowned content.\n");
        Assert.Equal(DocumentGenerationStatus.Error, DocumentRepairWriter.Repair(f.Service.Session, declaration.Id).Status);
        var retirement = new JsonObject { ["path"] = "docs/generated", ["reason"] = "End the register.", ["condition"] = "Verified retirement.", ["retiredAt"] = "2026-10-18T12:00:00Z" };
        WriteOutcome refused = f.Service.NewRecord(new("retirement", f.Estate.OperatorId, Fixture.Element(retirement)));
        Assert.False(refused.Ok);
        Assert.Null(refused.Written);
        Assert.Empty(f.Service.Records("retirement"));
        Assert.Equal("Unowned content.\n", File.ReadAllText(Path.Combine(f.Estate.Root, "docs/generated/unowned.md")));
    }

    [Fact]
    public void InterruptedRetirementCannotPruneWhenItsAuthoritativeSourceBecomesUnavailable()
    {
        using var f = new Fixture();
        RecordWritten source = f.Decision("First", "First statement.");
        f.Declare();
        Dictionary<string, string> before = Outputs(f.Estate.Root);
        byte[] original = File.ReadAllBytes(Path.Combine(f.Estate.Root, source.Path));
        f.Service.BeforePublishingDocuments = _ => throw new IOException("Injected retirement interruption.");
        var retirement = new JsonObject { ["path"] = "docs/generated", ["reason"] = "End the register.", ["condition"] = "Verified retirement.", ["retiredAt"] = "2026-10-18T12:00:00Z" };
        WriteOutcome interrupted = f.Service.NewRecord(new("retirement", f.Estate.OperatorId, Fixture.Element(retirement)));
        Assert.NotNull(interrupted.Written);
        Assert.NotNull(interrupted.Problem);
        File.WriteAllText(Path.Combine(f.Estate.Root, source.Path), "{broken");
        f.Reopen();
        Assert.Equal(before, Outputs(f.Estate.Root));
        Assert.Single(BatchJournal.All(f.Service.Session).Journals);
        File.WriteAllBytes(Path.Combine(f.Estate.Root, source.Path), original);
        f.Reopen();
        Assert.Empty(Outputs(f.Estate.Root));
        Assert.False(Directory.Exists(Path.Combine(f.Estate.Root, "docs/generated")));
        Assert.Empty(BatchJournal.All(f.Service.Session).Journals);
    }

    [Theory]
    [InlineData("bytes")]
    [InlineData("before")]
    [InlineData("after")]
    [InlineData("path")]
    public void CorruptOutputJournalsAreRetainedAndRefuseFurtherWrites(string field)
    {
        using var f = new Fixture();
        f.Decision("First", "First statement.");
        f.Service.BeforePublishingDocuments = _ => throw new IOException("Injected interruption.");
        f.Service.NewRecord(new("living-document", f.Estate.OperatorId, Fixture.Element(Fixture.Declaration())));
        BatchJournal journal = Assert.Single(BatchJournal.All(f.Service.Session).Journals);
        string file = Path.Combine(BatchJournal.Directory(f.Service.Session), journal.Id + ".json");
        JsonObject body = JsonNode.Parse(File.ReadAllText(file))!.AsObject();
        body["outputs"]![0]![field] = 42;
        File.WriteAllText(file, body.ToJsonString());
        Assert.Single(BatchJournal.All(f.Service.Session).Unreadable);
        WriteOutcome refused = f.Service.NewRecord(new("decision", f.Estate.OperatorId, Fixture.Element(f.DecisionBody("Held", "Nothing written."))));
        Assert.Equal("reconcile-required", refused.Problem!.Code);
        Assert.Null(refused.Written);
        Assert.True(File.Exists(file));
        Assert.Empty(Outputs(f.Estate.Root));
    }

    private static Dictionary<string, string> Records(string root) => Directory.EnumerateFiles(Path.Combine(root, "records"), "*.json", SearchOption.AllDirectories)
        .Order(StringComparer.Ordinal).ToDictionary(p => Path.GetRelativePath(root, p), ContentHash.Sha256File, StringComparer.Ordinal);
    private static Dictionary<string, string> Outputs(string root) => Directory.Exists(Path.Combine(root, "docs"))
        ? Directory.EnumerateFiles(Path.Combine(root, "docs"), "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal).ToDictionary(p => Path.GetRelativePath(root, p), ContentHash.Sha256File, StringComparer.Ordinal) : [];

    private static DecisionRenderer Install()
    {
        var renderer = new DecisionRenderer();
        DocumentGenerators.Register(renderer);
        return renderer;
    }

    private sealed class DecisionRenderer : IDocumentGenerator
    {
        public string Id => "example.runtime/decisions";
        public DocumentGeneratorDescriptor Descriptor { get; } = new("example.runtime/decisions", 1, "example.runtime/1", "sha256:" + new string('c', 64),
            new Dictionary<string, ImmutableArray<string>> { ["audience"] = ["Internal"], ["selection"] = ["all-revisions"] }.ToImmutableDictionary(StringComparer.Ordinal),
            ["decision"], ["record"], ["item", "all-revisions"], "text/markdown", "UTC time then ordinal identity", "Internal", "record-revision");
        public DocumentRenderResult Generate(DocumentGenerationContext context)
        {
            DocumentSourceSnapshot record = Assert.Single(context.Sources);
            return record.Title switch
            {
                "Refuse generation" => DocumentRenderResult.Error("Injected renderer refusal."),
                "Unavailable generation" => DocumentRenderResult.Unavailable("Injected source unavailability."),
                "Throw during generation" => throw new IOException("Injected renderer exception."),
                _ => DocumentRenderResult.Success("# " + DocumentMarkdown.Text(record.Title!) + "\n\n" + DocumentMarkdown.Text(record.Body!.Value.GetProperty("statement").GetString()!) + "\n"),
            };
        }
    }

    private sealed class Fixture : IDisposable
    {
        public TestEstate Estate { get; } = new(recordFormat: 3);
        public EstateService Service { get; private set; }
        public Fixture(bool localOnly = false)
        {
            IReadOnlyList<RecordFile> seed = localOnly ? EstateLoader.EnumerateRecordFiles(Estate.Root) : [];
            if (localOnly)
            {
                string path = Path.Combine(Estate.Root, EstateLayout.ManifestPath);
                JsonObject manifest = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
                manifest["profile"] = "local-only";
                File.WriteAllBytes(path, CanonicalJson.SerializePrettyUtf8(JsonSerializer.SerializeToElement(manifest)));
                Directory.Delete(Path.Combine(Estate.Root, "records"), recursive: true);
            }
            Service = EstateService.Open(Estate.Root);
            foreach (RecordFile record in seed)
            {
                var findings = new FindingList();
                RecordEnvelope envelope = RecordParser.Parse(record.Bytes, record.Path, findings)!;
                Service.Session.Write(new AuthoredRecord(record.Path, record.Bytes, envelope));
            }
            WriteOutcome device = Service.DeviceCreate(new(Estate.OperatorId, "Test operator device", "file"));
            Assert.True(device.Ok, JsonSerializer.Serialize(device.Problem));
            Assert.True(Service.DeviceAdmit(new(device.Written!.Id, Estate.OperatorId, "Test authority.")).Ok);
        }
        public JsonObject DecisionBody(string title, string statement) => new()
        {
            ["title"] = title, ["statement"] = statement, ["summary"] = "A test decision.", ["synthesis"] = "A fixed source.",
            ["effects"] = new JsonArray("Test rendering."), ["affects"] = new JsonArray(), ["decidedBy"] = Estate.OperatorId,
            ["decidedAt"] = "2026-10-18T12:00:00Z", ["stake"] = "medium", ["notAuthorized"] = new JsonArray("publication"),
        };
        public static JsonObject Declaration() => new()
        {
            ["pathPattern"] = "docs/generated/{month}/{date}-{slug}-{id}.md", ["title"] = "Decisions", ["kind"] = "generated",
            ["generator"] = new JsonObject { ["id"] = Renderer.Id, ["inputs"] = new JsonArray("format=1", "audience=Internal", "selection=all-revisions") },
            ["source"] = "estate-file", ["triggers"] = new JsonArray(), ["summary"] = "Every exact decision revision.",
        };
        public RecordWritten Decision(string title, string statement) => Ok(Service.NewRecord(new("decision", Estate.OperatorId, Element(DecisionBody(title, statement)))));
        public RecordWritten Declare() => Ok(Service.NewRecord(new("living-document", Estate.OperatorId, Element(Declaration()))));
        public void Reopen() { Service.Dispose(); Service = EstateService.Open(Estate.Root); }
        public static JsonElement Element(JsonObject body) => JsonSerializer.SerializeToElement(body);
        private static RecordWritten Ok(WriteOutcome result) { Assert.True(result.Ok, JsonSerializer.Serialize(result.Problem)); return result.Written!; }
        public void Dispose() { Service.Dispose(); Estate.Dispose(); }
    }
}
