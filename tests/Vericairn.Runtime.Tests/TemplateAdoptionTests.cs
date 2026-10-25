// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Vericairn.Protocol;
using Vericairn.Records.Authoring;
using Vericairn.Records.Estate;
using Vericairn.Records.Hashing;
using Vericairn.Records.Model;
using Vericairn.Records.Templates;
using Vericairn.Records.Tests;
using Vericairn.Runtime.Service;
using Vericairn.Runtime.Store;
using static Vericairn.Records.Tests.TemplateFixture;

namespace Vericairn.Runtime.Tests;

public sealed class TemplateAdoptionTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void InterruptedAdoptionReportsEveryReceiptAndRetainsTheOriginalBatch(bool beforePublication, bool conflict)
    {
        using var estate = new TestEstate(3);
        const string path = "design/roadmap.md";
        const string original = "# Roadmap\n\n## Direction\n\nKeep this authored narrative.\n\n";
        estate.WriteFile(path, original);
        var definition = FirstPartyTemplates.Definitions.Single(d => d.Id == "vericairn.estate/roadmap");
        using EstateService service = EstateService.Open(estate.Root);
        var request = new DocumentAdoptRequest(estate.OperatorId, new(definition.Id, definition.Version, definition.Hash), path,
            ContentHash.Sha256File(Path.Combine(estate.Root, path)), IncludeRegions: true, Preview: true);
        string staged = original + "## Releases\n\n" + GeneratedRegions.Start("releases") + "\nOld release rows.\n" + GeneratedRegions.End("releases") + "\n";
        estate.WriteFile(path, staged);
        request = request with { ExpectedContentHash = ContentHash.Sha256File(Path.Combine(estate.Root, path)), Preview = false };
        if (beforePublication) service.BeforePublishingDocuments = _ => throw new IOException("Injected interruption after the declaration.");
        else service.BeforeStoringTogether = record =>
        {
            if (record.Envelope.Type == "living-document") throw new IOException("Injected interruption after the pin.");
        };
        DocumentAdoptResponse adopted = service.DocumentAdopt(request);
        Assert.False(adopted.Ok); Assert.Equal("reconcile-required", adopted.Problem!.Code);
        Assert.NotNull(adopted.Pin); Assert.Equal(beforePublication, adopted.Declaration is not null);
        Assert.Equal(staged, File.ReadAllText(Path.Combine(estate.Root, path)));
        Assert.Equal(Encoding.UTF8.GetBytes(staged), service.Session.Objects.Read(request.ExpectedContentHash));
        BatchJournal journal = Assert.Single(BatchJournal.All(service.Session).Journals);
        DocumentOutputChange output = Assert.Single(journal.Outputs);
        if (conflict) estate.WriteFile(path, original + "An intervening authored edit.\n");
        service.Dispose();
        using EstateService reopened = EstateService.Open(estate.Root);
        Assert.Single(reopened.Session.Load().OfType("document-revision"));
        Assert.Equal(beforePublication ? 1 : 0, reopened.Session.Load().OfType("living-document").Count());
        if (beforePublication && !conflict)
        {
            Assert.Equal(output.AfterHash, ContentHash.Sha256File(Path.Combine(estate.Root, path)));
            Assert.Empty(BatchJournal.All(reopened.Session).Journals);
        }
        else
        {
            Assert.Equal(conflict ? original + "An intervening authored edit.\n" : staged, File.ReadAllText(Path.Combine(estate.Root, path)));
            Assert.Single(BatchJournal.All(reopened.Session).Journals);
        }
    }

    [Theory]
    [InlineData("design", "design-document", false)]
    [InlineData("design", "design-document", true)]
    [InlineData("index", "design-index", false)]
    [InlineData("index", "design-index", true)]
    [InlineData("roadmap", "roadmap", false)]
    [InlineData("roadmap", "roadmap", true)]
    public void EstateShapedDocumentsKeepEveryBytePinAndExistingDuty(string fixture, string template, bool alreadyLiving)
    {
        using var estate = new TestEstate(3);
        string path = "design/" + fixture + ".md";
        string text = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "TemplateAdoption", fixture + ".md"));
        // Preserve a BOM and CRLF as well as the ordinary LF fixture, without depending on checkout defaults.
        if (alreadyLiving) text = "\uFEFF" + text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace("\n", "\r\n", StringComparison.Ordinal);
        Revision(estate, path, "# Earlier revision\n\nThe original content is still in recorded history.\n");
        RecordEnvelope pin = Revision(estate, path, text);
        RecordEnvelope? previous = alreadyLiving ? estate.Add("living-document", estate.OperatorId, new JsonObject
        {
            ["path"] = path, ["title"] = "Retained title", ["kind"] = "authored", ["source"] = "estate-file",
            ["triggers"] = new JsonArray(new JsonObject { ["on"] = "decision", ["level"] = "refuse" }, new JsonObject { ["on"] = "upgrade", ["level"] = "warn" }),
            ["checks"] = new JsonArray("links-resolve"), ["clearedBy"] = "operator", ["summary"] = "Retain every original duty.",
        }.ToJsonString()) : null;
        Dictionary<string, byte[]> history = estate.Load().Records.ToDictionary(r => r.ExpectedPath, r => File.ReadAllBytes(Path.Combine(estate.Root, r.ExpectedPath)), StringComparer.Ordinal);
        byte[] original = File.ReadAllBytes(Path.Combine(estate.Root, path));
        TemplateDefinition definition = FirstPartyTemplates.Definitions.Single(d => d.Id == "vericairn.estate/" + template);
        var overrides = new JsonArray();
        if (fixture == "design")
        {
            foreach ((string id, string heading) in new[] { ("answer", "1. The answer, and what this document decides"), ("body", "2. Design"),
                ("implementation-notes", "3. Implementation, as work orders"), ("journeys", "4. Journeys and tests") })
                overrides.Add(new JsonObject { ["section"] = id, ["action"] = "rename", ["heading"] = heading, ["reason"] = "Retain the estate's numbered headings." });
        }
        using EstateService service = EstateService.Open(estate.Root);
        var request = new DocumentAdoptRequest(estate.OperatorId, new(definition.Id, definition.Version, definition.Hash), path,
            ContentHash.Sha256(original), previous?.ComputedHash, Overrides: Element(overrides));
        int before = service.Session.Load().Records.Count;
        DocumentAdoptResponse preview = service.DocumentAdopt(request with { Preview = true });
        Assert.Null(preview.Problem); Assert.Null(preview.Declaration); Assert.False(preview.Ok);
        Assert.Equal(before, service.Session.Load().Records.Count);
        DocumentAdoptResponse adopted = service.DocumentAdopt(request);
        Assert.True(adopted.Ok, JsonSerializer.Serialize(adopted.Problem));
        Assert.Equal(preview.ProposedDeclaration!.Value.GetRawText(), adopted.ProposedDeclaration!.Value.GetRawText());
        Assert.Equal(original, File.ReadAllBytes(Path.Combine(estate.Root, path)));
        foreach ((string file, byte[] bytes) in history) Assert.Equal(bytes, File.ReadAllBytes(Path.Combine(estate.Root, file)));
        RecordEnvelope current = service.Session.Load().ById[adopted.Declaration!.Id];
        Assert.Equal(definition.Hash, TemplateCatalog.Pin(current)!.Hash);
        Assert.Equal(previous?.Id, current.Supersedes);
        Assert.Equal(pin.Id, service.Session.Load().Current("document-revision").Single().Id);
        if (previous is not null)
            foreach (string field in new[] { "title", "triggers", "checks", "clearedBy", "summary" })
                Assert.Equal(previous.Body.GetProperty(field).GetRawText(), current.Body.GetProperty(field).GetRawText());
        DocumentSectionsResponse states = service.DocumentSections(path);
        Assert.Null(states.Problem);
        Assert.Contains(states.Sections, s => s.Required && s.State == "missing");
        Assert.DoesNotContain(states.Sections, s => s.State == "reviewed");
        if (fixture == "design") Assert.NotNull(states.Sections.Single(s => s.Id == "answer").ContentHash);
        if (fixture == "roadmap") Assert.NotNull(states.Sections.Single(s => s.Id == "narrative").ContentHash);
        Assert.False(service.DocumentAdopt(request).Ok);
        Assert.Equal(before + 1, service.Session.Load().Records.Count);
        Assert.Empty(service.Session.Load().OfType("document-review"));
        Assert.Empty(service.Session.Load().OfType("acceptance"));
    }

    [Fact]
    public void ExpectedBytesAndDeclarationGuardAdoptionAndExistingReadmeNeedsNoNewPin()
    {
        using var estate = new TestEstate(3);
        const string text = "# Example\n\n## Overview\n\nAuthored introduction.\n";
        estate.WriteFile("README.md", text);
        var definition = FirstPartyTemplates.Definitions.Single(d => d.Id == "vericairn.estate/readme");
        using EstateService service = EstateService.Open(estate.Root);
        var request = new DocumentAdoptRequest(estate.OperatorId, new(definition.Id, definition.Version, definition.Hash), "README.md", ContentHash.Sha256(Encoding.UTF8.GetBytes(text)));
        Assert.False(service.DocumentAdopt(request with { ExpectedContentHash = "sha256:" + new string('0', 64) }).Ok);
        Assert.False(service.DocumentAdopt(request with { ExpectedDeclaration = request.ExpectedContentHash }).Ok);
        Assert.False(service.DocumentAdopt(request with { Path = "../README.md" }).Ok);
        Assert.Empty(service.Session.Load().OfType("living-document"));
        Assert.True(service.DocumentAdopt(request).Ok);
        Assert.Equal(text, File.ReadAllText(Path.Combine(estate.Root, "README.md")));
        Assert.Empty(service.Session.Load().OfType("document-revision"));
    }

    [Fact]
    public void AdoptionCannotUseATemplateToBypassTheExistingGeneratedRegionMigrationReview()
    {
        using var estate = new TestEstate(3);
        const string path = "design/roadmap.md";
        Revision(estate, path, "# Roadmap\n\nAn authored narrative and release table.\n");
        RecordEnvelope prior = estate.Add("living-document", estate.OperatorId,
            """{"path":"design/roadmap.md","title":"Roadmap","kind":"authored","source":"estate-file","triggers":[{"on":"decision","level":"refuse"}],"summary":"Keep the roadmap current."}""");
        var definition = FirstPartyTemplates.Definitions.Single(d => d.Id == "vericairn.estate/roadmap");
        using EstateService service = EstateService.Open(estate.Root);
        var request = new DocumentAdoptRequest(estate.OperatorId, new(definition.Id, definition.Version, definition.Hash), path,
            ContentHash.Sha256File(Path.Combine(estate.Root, path)), prior.ComputedHash, IncludeRegions: true);
        int before = service.Session.Load().Records.Count;
        DocumentAdoptResponse preview = service.DocumentAdopt(request with { Preview = true });
        Assert.True(preview.MigrationRequired); Assert.NotNull(preview.MigrationBasis);
        Assert.Equal("incomplete", preview.Problem!.Code);
        DocumentAdoptResponse refused = service.DocumentAdopt(request);
        Assert.False(refused.Ok); Assert.Null(refused.Declaration);
        Assert.Equal("review-required", refused.Problem!.Code);
        Assert.Equal(before, service.Session.Load().Records.Count);
        Assert.Null(TemplateCatalog.Pin(service.Session.Load().Latest(prior)));
        DocumentAdoptResponse attached = service.DocumentAdopt(request with { IncludeRegions = false });
        Assert.True(attached.Ok, attached.Problem?.Message);
        Assert.Equal("review-required", service.DocumentPin(new(path, estate.OperatorId, "Cannot bypass migration.") { RenderTemplate = true }).Problem!.Code);
    }

    [Fact]
    public void ReviewedRoadmapAdoptionRetainsAuthoredHistoryAndThenFollowsPlacementChanges()
    {
        using var estate = new TestEstate(3);
        const string path = "design/roadmap.md";
        string original = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "TemplateAdoption", "roadmap.md"));
        RecordEnvelope oldPin = Revision(estate, path, original);
        byte[] oldPinBytes = File.ReadAllBytes(Path.Combine(estate.Root, oldPin.ExpectedPath));
        RecordEnvelope previous = estate.Add("living-document", estate.OperatorId,
            """{"path":"design/roadmap.md","title":"Roadmap","kind":"authored","source":"estate-file","triggers":[{"on":"decision","level":"refuse"}],"summary":"Keep the roadmap current."}""");
        RecordEnvelope product = FirstPartyTemplateTests.Product(estate);
        RecordEnvelope carrier = FirstPartyTemplateTests.Work(estate, "REL-1", plan: new JsonObject { ["product"] = product.Id, ["version"] = "1.0.0" });
        var placement = new JsonObject { ["kind"] = "release", ["product"] = product.Id, ["version"] = "1.0.0", ["carrier"] = carrier.Id };
        FirstPartyTemplateTests.Work(estate, "EXAMPLE-1", placement);
        RecordEnvelope attempt = Attempt(estate);
        var definition = FirstPartyTemplates.Definitions.Single(d => d.Id == "vericairn.estate/roadmap");
        using EstateService service = EstateService.Open(estate.Root);
        foreach (string principal in new[] { estate.OperatorId, estate.BuilderId })
        {
            WriteOutcome device = service.DeviceCreate(new(principal, "Fixture signing device", "file"));
            Assert.True(device.Ok, device.Problem?.Message);
            Assert.True(service.DeviceAdmit(new(device.Written!.Id, estate.OperatorId, "Sign the fixture's attributable records.")).Ok);
        }
        var request = new DocumentAdoptRequest(estate.OperatorId, new(definition.Id, definition.Version, definition.Hash), path,
            ContentHash.Sha256File(Path.Combine(estate.Root, path)), previous.ComputedHash, IncludeRegions: true, Preview: true);
        DocumentAdoptResponse proposal = service.DocumentAdopt(request);
        AuthoredRecord planned = RecordAuthor.Create(new("living-document", estate.OperatorId, proposal.ProposedDeclaration!.Value, Supersedes: previous.Id, Format: 3));
        Assert.NotEmpty(TemplateDocuments.NewContent(service.Session.Load(), planned.Envelope));
        // The builder explicitly prepares ownership markers and keeps the complete former table in authored history.
        estate.WriteFile("docs/history/roadmap.md", original);
        int begin = original.IndexOf("| Release |", StringComparison.Ordinal), end = original.IndexOf("## Risks", StringComparison.Ordinal);
        string prepared = original[..begin] + GeneratedRegions.Start("releases") + "\n" + original[begin..end]
            + GeneratedRegions.End("releases") + "\n\n" + original[end..];
        estate.WriteFile(path, prepared);
        Assert.True(service.DocumentPin(new(path, estate.OperatorId, "Retain the authored narrative and mark the record-derived release table.")).Ok);
        request = request with { ExpectedContentHash = ContentHash.Sha256File(Path.Combine(estate.Root, path)) };
        proposal = service.DocumentAdopt(request);
        Assert.Null(proposal.Problem); Assert.True(proposal.MigrationRequired); Assert.NotNull(proposal.MigrationPreview);
        Assert.False(service.DocumentAdopt(request with { Preview = false }).Ok);
        JsonObject result = Result(new JsonObject { ["name"] = GeneratedDocumentContract.MigrationOutput,
                ["hash"] = ContentHash.Sha256(Convert.FromBase64String(proposal.MigrationBasis!)) },
            new JsonObject { ["name"] = GeneratedDocumentContract.MigrationPreviewOutput,
                ["hash"] = ContentHash.Sha256(Convert.FromBase64String(proposal.MigrationPreview!)) });
        result["evidence"] = new JsonArray(proposal.RetainedPins.Select(p => (JsonNode)new JsonObject
        {
            ["summary"] = "Retain the exact prior document revision.", ["hash"] = p.Hash, ["location"] = service.Session.Load().ById[p.Id].ExpectedPath,
        }).ToArray());
        WriteOutcome submitted = service.Result(new(attempt.Id, estate.BuilderId, Element(result)));
        Assert.True(submitted.Ok, submitted.Problem?.Message);
        WriteOutcome review = service.NewRecord(new("review", estate.OperatorId,
            JsonSerializer.SerializeToElement(new { independence = "operator", outcome = "pass", summary = "Read the complete migration basis, preview and retained pins.", findings = Array.Empty<string>() }),
            $"result:{submitted.Written!.Id}:{submitted.Written.Hash}"));
        Assert.True(review.Ok, review.Problem?.Message);
        DocumentAdoptResponse adopted = service.DocumentAdopt(request with { Preview = false });
        Assert.True(adopted.Ok, JsonSerializer.Serialize(adopted.Problem));
        Assert.NotNull(adopted.Pin);
        Assert.Equal(proposal.PreparedContent, File.ReadAllText(Path.Combine(estate.Root, path)));
        Assert.Equal(Encoding.UTF8.GetBytes(prepared), service.Session.Objects.Read(request.ExpectedContentHash));
        Assert.Equal(original, File.ReadAllText(Path.Combine(estate.Root, "docs", "history", "roadmap.md")));
        Assert.Equal(oldPinBytes, File.ReadAllBytes(Path.Combine(estate.Root, oldPin.ExpectedPath)));
        Assert.All(service.DocumentSections(path).Sections.Single(s => s.Id == "releases").Checks, c => Assert.Equal("passed", c.Outcome));

        FirstPartyTemplateTests.Work(estate, "EXAMPLE-2", placement);
        Assert.Contains(service.DocumentSections(path).Sections.Single(s => s.Id == "releases").Checks, c => c.Outcome == "failed");
        Assert.True(service.DocumentPin(new(path, estate.OperatorId, "Follow the new placement.") { RenderTemplate = true }).Ok);
        Assert.Contains("EXAMPLE&#45;2", File.ReadAllText(Path.Combine(estate.Root, path)), StringComparison.Ordinal);
        Assert.Equal(TemplateSections.Read(original, definition.Elements)["narrative"].Content,
            TemplateSections.Read(File.ReadAllText(Path.Combine(estate.Root, path)), definition.Elements)["narrative"].Content);
        Assert.All(service.DocumentSections(path).Sections.Single(s => s.Id == "releases").Checks, c => Assert.Equal("passed", c.Outcome));
        Assert.Empty(service.Session.Load().OfType("document-review"));
        Assert.Empty(service.Session.Load().OfType("acceptance"));
    }

    [Theory]
    [InlineData("design-document", "design/retained.md")]
    [InlineData("readme", "README.md")]
    public void AdoptingMarkedGeneratedPartsRetainsTheWholeOriginalAndPinsFreshOutput(string name, string path)
    {
        using var estate = new TestEstate(3);
        const string original = "# A retained document\n\nRevision 3: the authored history.\n\nThis authored paragraph must stay exactly.\n\n";
        RecordEnvelope old = Revision(estate, path, original);
        byte[] history = File.ReadAllBytes(Path.Combine(estate.Root, old.ExpectedPath));
        TemplateDefinition definition = FirstPartyTemplates.Definitions.Single(d => d.Id == "vericairn.estate/" + name);
        using EstateService service = EstateService.Open(estate.Root);
        var request = new DocumentAdoptRequest(estate.OperatorId, new(definition.Id, definition.Version, definition.Hash), path,
            ContentHash.Sha256File(Path.Combine(estate.Root, path)), IncludeRegions: true, Preview: true);
        DocumentAdoptResponse layout = service.DocumentAdopt(request);
        Assert.Equal("incomplete", layout.Problem!.Code);
        Assert.NotNull(layout.TemplateContent); Assert.Null(layout.Declaration);
        string staged = original + layout.TemplateContent;
        string generated = name == "readme" ? "releases" : "revision";
        staged = GeneratedRegions.Replace(staged, generated, "Explicitly selected old generated content.\n")!;
        estate.WriteFile(path, staged);
        request = request with { ExpectedContentHash = ContentHash.Sha256File(Path.Combine(estate.Root, path)) };
        DocumentAdoptResponse preview = service.DocumentAdopt(request);
        Assert.Null(preview.Problem); Assert.NotNull(preview.PreparedContent);
        Assert.Empty(service.Session.Load().OfType("living-document"));
        DocumentAdoptResponse adopted = service.DocumentAdopt(request with { Preview = false });
        Assert.True(adopted.Ok, JsonSerializer.Serialize(adopted.Problem));
        Assert.NotNull(adopted.Pin);
        string actual = File.ReadAllText(Path.Combine(estate.Root, path));
        Assert.Equal(preview.PreparedContent, actual);
        Assert.StartsWith(original, actual, StringComparison.Ordinal);
        Assert.Equal(Encoding.UTF8.GetBytes(staged), service.Session.Objects.Read(request.ExpectedContentHash));
        Assert.Equal(history, File.ReadAllBytes(Path.Combine(estate.Root, old.ExpectedPath)));
        RecordEnvelope pin = service.Session.Load().ById[adopted.Pin.Id];
        Assert.Equal(old.Id, pin.Supersedes);
        Assert.Equal(ContentHash.Sha256File(Path.Combine(estate.Root, path)), pin.Body.GetProperty("hash").GetString());
        Assert.All(service.DocumentSections(path).Sections.Where(s => s.Definition.GetProperty("synthesis").GetProperty("mode").GetString() == "generated"),
            s => Assert.All(s.Checks.Concat(s.Cohesion), c => Assert.Equal("passed", c.Outcome)));
        Assert.DoesNotContain(service.Check().Findings, f => f.Code == "document.drift");
        if (name == "readme") Assert.False(service.DocumentPin(new(path, estate.OperatorId, "Ordinary pins keep their original path contract.")).Ok);
    }
}
