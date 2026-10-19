// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Collections.Immutable;
using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using Vericairn.Protocol;
using Vericairn.Records.Estate;
using Vericairn.Records.Hashing;
using Vericairn.Records.Model;
using Vericairn.Records.Tests;
using Vericairn.Runtime.Service;
using Vericairn.Runtime.Store;

namespace Vericairn.Runtime.Tests;

/// <summary>Adoption is exercised only in disposable format-3 estates. The named journeys span the foundation fixtures too.</summary>
public sealed class ReadableCompanionJourneyTests
{
    private static readonly EffectsReader Extension = Install();
    private static readonly (string Generator, string Type, string Directory)[] Registers =
    [
        ("vericairn.records/decisions", "decision", "decisions"),
        ("vericairn.records/work-orders", "work-order", "work"),
        ("vericairn.records/releases", "release", "releases"),
        ("vericairn.estate/policies", "estate-policy", "policies"),
        ("vericairn.estate/policy-exceptions", "policy-exception", "policy-exceptions"),
        ("vericairn.estate/writing-exceptions", "writing-exception", "writing-exceptions"),
    ];

    [Fact]
    [Trait("Journey", "D19-A")]
    [Trait("Journey", "D19-B")]
    public void FixtureAdoptionPublishesOneDeclarationPerRegisterWithItsMembersAndNoGeneratedPins()
    {
        using var fixture = new PolicyFixture();
        RecordEnvelope policy = fixture.Policy();
        fixture.Estate.SetClock(new DateTimeOffset(2026, 10, 13, 14, 0, 0, TimeSpan.Zero));
        fixture.Add("policy-exception", fixture.ExceptionBody(policy), subject: policy.Reference);
        fixture.Add("writing-exception", PolicyFixture.WritingBody());
        fixture.BeginAttempt();
        Publication(fixture);
        fixture.AssertValid();
        using EstateService service = EstateService.Open(fixture.Estate.Root);
        RecordWritten device = Ok(service.DeviceCreate(new(fixture.Estate.OperatorId, "Fixture reader writer", "file")));
        fixture.Estate.SetClock(service.Session.Load().ById[device.Id].AtTime);
        fixture.Add("admission", new JsonObject { ["principal"] = fixture.Estate.OperatorId, ["status"] = "admitted", ["note"] = "Fixture adoption writer." },
            subject: service.Session.Load().ById[device.Id].Reference);
        string authored = "# Introduction\n\nThese are exact recorded terms.\n";
        fixture.Estate.WriteFile("docs/introduction.md", authored);
        RecordEnvelope authoredPin = fixture.Add("document-revision", new JsonObject
        {
            ["path"] = "docs/introduction.md", ["title"] = "Introduction", ["hash"] = ContentHash.Sha256(System.Text.Encoding.UTF8.GetBytes(authored)),
            ["bytes"] = System.Text.Encoding.UTF8.GetByteCount(authored), ["mediaType"] = "text/markdown", ["revision"] = 1, ["summary"] = "Authored custody remains exact.",
        });
        byte[] pinBytes = File.ReadAllBytes(Path.Combine(fixture.Estate.Root, authoredPin.ExpectedPath));
        int before = service.Session.Load().Records.Count;
        var declarations = new List<RecordWritten>();
        foreach (var register in Registers)
        {
            JsonObject body = CompanionReadTests.Declaration();
            body["pathPattern"] = "docs/registers/" + register.Directory + "/{month}/{date}-{slug}-{id}.md";
            body["generator"]!["id"] = register.Generator;
            body["title"] = "Fixture " + register.Directory;
            declarations.Add(Ok(service.NewRecord(new("living-document", fixture.Estate.OperatorId, PolicyFixture.Element(body),
                At: RecordParser.FormatTimestamp(fixture.Estate.Clock.AddSeconds(1))))));
        }
        EstateModel model = service.Session.Load();
        Assert.False(model.Findings.HasErrors, string.Join('\n', model.Findings));
        Assert.Equal(before + Registers.Length, model.Records.Count);
        Assert.Equal(Registers.Length, model.OfType("living-document").Count());
        Assert.Equal(authoredPin.Id, Assert.Single(model.OfType("document-revision")).Id);
        Assert.Empty(model.OfType("document-review"));
        Assert.Empty(BatchJournal.All(service.Session).Journals);
        Assert.DoesNotContain(EstateChecks.Run(model), f => f.Code.StartsWith("document.", StringComparison.Ordinal));
        foreach (var register in Registers)
        {
            RecordEnvelope declaration = model.ById[declarations.Single(d => model.ById[d.Id].Body.GetProperty("generator").GetProperty("id").GetString() == register.Generator).Id];
            GeneratedDocumentPlan plan = GeneratedDocumentVerification.Plan(model, declaration);
            Assert.True(plan.Current, string.Join('\n', plan.Problems));
            Assert.Equal(model.OfType(register.Type).Count(), plan.Expected.Length);
            Assert.NotEmpty(plan.Expected);
            Assert.Equal("current", service.CompanionCompare(new(declaration.Id)).Currentness);
            foreach (var member in plan.Expected)
            {
                Assert.Equal(member.Bytes.ToArray(), File.ReadAllBytes(Path.Combine(fixture.Estate.Root, member.Path)));
                Assert.Contains("records/" + register.Type + "/", System.Text.Encoding.UTF8.GetString(member.Bytes.AsSpan()), StringComparison.Ordinal);
            }
        }
        Dictionary<string, string> original = Members(fixture.Estate.Root);
        JsonObject decision = fixture.DecisionBody(); decision["title"] = "An independent later decision";
        Ok(service.NewRecord(new("decision", fixture.Estate.OperatorId, PolicyFixture.Element(decision),
            At: RecordParser.FormatTimestamp(fixture.Estate.Clock.AddSeconds(2)))));
        Dictionary<string, string> after = Members(fixture.Estate.Root);
        Assert.Equal(original.Count + 1, after.Count);
        Assert.All(original, member => Assert.Equal(member.Value, after[member.Key]));
        Assert.Equal(pinBytes, File.ReadAllBytes(Path.Combine(fixture.Estate.Root, authoredPin.ExpectedPath)));
        Assert.Equal(Registers.Length, service.Companions().Destinations.Count);
        Assert.Equal(authoredPin.Id, Assert.Single(service.Session.Load().OfType("document-revision")).Id);
    }

    [Fact]
    [Trait("Journey", "D19-A")]
    public void ReaderSeesRecordedPlacementPublicationAndHistoricalAcceptanceWithoutChangingTheirMeaning()
    {
        using var fixture = new PolicyFixture();
        RecordEnvelope work = fixture.Add("work-order", new JsonObject
        {
            ["key"] = "READ-FIXTURE", ["kind"] = "ordinary", ["title"] = "Read the recorded placement", ["intent"] = "Keep planning and observation distinct.",
            ["scope"] = new JsonArray("fixtures"), ["exitQuestion"] = "Is the exact basis readable?", ["inputs"] = new JsonArray(), ["deliverables"] = new JsonArray("readable view"),
            ["placement"] = new JsonObject { ["kind"] = "backlog", ["key"] = "later" },
        });
        RecordEnvelope release = Publication(fixture);
        RecordEnvelope result = fixture.Result();
        RecordEnvelope acceptance = fixture.Add("acceptance", new JsonObject { ["statement"] = "Accept this exact result.", ["basis"] = new JsonArray(), ["self"] = false }, subject: result.Reference);
        JsonObject corrected = PolicyFixture.Body(result); corrected["summary"] = "An updated claim requiring its own acceptance.";
        fixture.Add("result", corrected, builder: true, subject: result.Subject, supersedes: result.Id);
        fixture.AssertValid();
        using EstateService service = EstateService.Open(fixture.Estate.Root);
        int before = service.Session.Load().Records.Count;
        string workText = Read(work);
        Assert.Contains("later", workText, StringComparison.Ordinal);
        Assert.Contains("Derived work status and acceptance are separate reader views", workText, StringComparison.Ordinal);
        Assert.Contains("READ-FIXTURE", service.Status().Text, StringComparison.Ordinal);
        string releaseText = Read(release);
        Assert.Contains("published", releaseText, StringComparison.Ordinal);
        Assert.Contains("https://example.invalid/releases/0.8.0", releaseText, StringComparison.Ordinal);
        var request = new CompanionRequest("vericairn.records/items", "item", acceptance.Id);
        CompanionResponse accepted = service.CompanionRender(request);
        Assert.Equal("success", accepted.Status);
        Assert.Equal("historical", accepted.Currentness);
        Assert.Contains("earlier revision", accepted.Reason, StringComparison.Ordinal);
        Assert.Contains(result.ComputedHash, WebUtility.HtmlDecode(accepted.Text), StringComparison.Ordinal);
        Assert.Contains("does not accept a later revision", WebUtility.HtmlDecode(accepted.Text), StringComparison.Ordinal);
        Assert.Equal(before, service.Session.Load().Records.Count);

        string Read(RecordEnvelope record)
        {
            CompanionResponse response = service.CompanionRender(new("vericairn.records/items", "item", record.Id));
            Assert.Equal("success", response.Status);
            Assert.Equal(record.ComputedHash, Assert.Single(response.Sources).Hash);
            Assert.Contains(record.ExpectedPath, response.Text, StringComparison.Ordinal);
            return WebUtility.HtmlDecode(response.Text!);
        }
    }

    [Fact]
    [Trait("Journey", "D19-E")]
    public void RevokedDeviceCannotRetrieveAnEarlierPermittedSelectionOrItsMetadata()
    {
        using var fixture = new PolicyFixture();
        using EstateService service = EstateService.Open(fixture.Estate.Root);
        var request = new CompanionRequest("vericairn.records/items", "item", fixture.Decision.Id);
        string publicKey = fixture.BuilderDevice.Body.GetProperty("publicKey").GetString()!;
        using (service.ForDeviceRequest(fixture.BuilderDevice.Id, publicKey)) Assert.Equal("success", service.CompanionRender(request).Status);
        RecordEnvelope previous = fixture.Estate.Load().Current("admission").Single(a => a.Subject?.Id == fixture.BuilderDevice.Id);
        var revoked = PolicyFixture.Body(previous); revoked["status"] = "revoked";
        revoked["revokedAt"] = RecordParser.FormatTimestamp(fixture.Estate.Clock.AddSeconds(1));
        fixture.Add("admission", revoked, subject: fixture.BuilderDevice.Reference, supersedes: previous.Id);
        fixture.AssertValid();
        using (service.ForDeviceRequest(fixture.BuilderDevice.Id, publicKey))
        {
            CompanionResponse denied = service.CompanionRender(request);
            Assert.Equal("unavailable", denied.Status);
            Assert.Null(denied.Snapshot);
            Assert.Null(denied.InputDigest);
            Assert.Null(denied.ContentHash);
            Assert.Null(denied.Text);
            Assert.Empty(service.CompanionSources(request).Sources);
            Assert.Empty(service.Companions().Coverage);
        }
    }

    [Fact]
    [Trait("Journey", "D19-F")]
    [Trait("Journey", "D19-I")]
    public void RegisteredExtensionUsesItsOwnInputContractAndMissingVersionsLeaveExactSourceInspection()
    {
        using var fixture = new PolicyFixture();
        using EstateService service = EstateService.Open(fixture.Estate.Root);
        var request = new CompanionRequest(Extension.Id, "list", fixture.Decision.Id, Field: "effects");
        CompanionResponse read = service.CompanionRender(request);
        Assert.Equal("success", read.Status);
        Assert.Contains("Apply the exact approved definition.", WebUtility.HtmlDecode(read.Text), StringComparison.Ordinal);
        Assert.Equal(fixture.Decision.ComputedHash, Assert.Single(read.Sources).Hash);
        Assert.Equal(read.InputDigest, service.CompanionSources(request).InputDigest);
        Assert.Equal("error", service.CompanionRender(request with { Field = "unknown" }).Status);
        foreach (CompanionRequest unavailable in new[] { request with { Contract = 2 }, request with { Generator = "example.journeys/removed" } })
        {
            Assert.Equal("unavailable", service.CompanionRender(unavailable).Status);
            Assert.Equal(fixture.Decision.ComputedHash, service.Record(fixture.Decision.Id)!.Hash);
        }
        Assert.Throws<InvalidOperationException>(() => DocumentGenerators.Register(Extension));
    }

    private static RecordEnvelope Publication(PolicyFixture fixture)
    {
        RecordEnvelope product = fixture.Add("product", JsonNode.Parse("""{"name":"Fixture product","slug":"fixture-product","summary":"Fixture release custody.","editions":[{"name":"Community","slug":"community","summary":"Fixture edition."}],"surfaces":[{"name":"CLI","slug":"cli","summary":"Fixture surface."}]}""")!.AsObject());
        JsonObject body = JsonNode.Parse("""{"version":"0.8.0","repository":"Example/example","commit":"1111111111111111111111111111111111111111","status":"candidate","artifacts":[{"name":"fixture.zip","hash":"sha256:1111111111111111111111111111111111111111111111111111111111111111","bytes":10,"platform":"linux-x64"}],"recordFormat":{"reads":[1,2,3],"writes":3},"evidence":[],"qualified":[],"signing":"unsigned fixture artifact","limitations":[]}""")!.AsObject();
        RecordEnvelope candidate = fixture.Add("release", body, subject: product.Reference);
        fixture.Add("acceptance", new JsonObject { ["statement"] = "Accept the fixture candidate only.", ["basis"] = new JsonArray(), ["self"] = true }, subject: candidate.Reference);
        body["status"] = "published"; body["publishedAt"] = RecordParser.FormatTimestamp(fixture.Estate.Clock.AddSeconds(1));
        body["url"] = "https://example.invalid/releases/0.8.0";
        return fixture.Add("release", body, subject: product.Reference, supersedes: candidate.Id);
    }

    private static RecordWritten Ok(WriteOutcome outcome) { Assert.True(outcome.Ok, JsonSerializer.Serialize(outcome.Problem)); return outcome.Written!; }
    private static Dictionary<string, string> Members(string root) => Directory.EnumerateFiles(Path.Combine(root, "docs", "registers"), "*.md", SearchOption.AllDirectories)
        .Order(StringComparer.Ordinal).ToDictionary(p => Path.GetRelativePath(root, p), ContentHash.Sha256File, StringComparer.Ordinal);
    private static EffectsReader Install() { var reader = new EffectsReader(); DocumentGenerators.Register(reader); return reader; }

    private sealed class EffectsReader : IDocumentGenerator
    {
        public string Id => "example.journeys/effects";
        public DocumentGeneratorDescriptor Descriptor { get; } = new("example.journeys/effects", 1, "example.journeys/effects/1", "sha256:" + new string('e', 64),
            new Dictionary<string, ImmutableArray<string>> { ["audience"] = ["Internal"], ["selection"] = ["item", "list"], ["field"] = ["body", "effects"] }.ToImmutableDictionary(StringComparer.Ordinal),
            ["decision"], ["record"], ["item", "list"], "text/markdown", "Recorded effect order", "Internal", "single");
        public DocumentRenderResult Generate(DocumentGenerationContext context)
        {
            DocumentSourceSnapshot source = Assert.Single(context.Sources);
            string effects = string.Join('\n', source.Body!.Value.GetProperty("effects").EnumerateArray().Select(e => "- " + DocumentMarkdown.Text(e.GetString()!)));
            return DocumentRenderResult.Success("# Recorded effects\n\n" + effects + "\n");
        }
    }
}
