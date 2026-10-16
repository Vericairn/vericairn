// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text;
using Vericairn.Protocol;
using Vericairn.Records.Canonical;
using Vericairn.Records.Estate;
using Vericairn.Records.Hashing;
using Vericairn.Records.Model;
using Vericairn.Records.Tests;
using Vericairn.Runtime.Service;

namespace Vericairn.Runtime.Tests;

public class ContextTests
{
    [Fact]
    public void SharedDecisionsDoNotImportUnrelatedWorkOrItsMissingInputs()
    {
        using var estate = new TestEstate(recordFormat: 2);
        RecordEnvelope decision = estate.Add("decision", estate.OperatorId, $$"""{"title":"Shared rule","stake":"medium","statement":"Keep originals.","summary":"Keep originals.","synthesis":"Preserve the source.","effects":["retain originals"],"notAuthorized":["deleting originals"],"affects":[],"decidedBy":"{{estate.OperatorId}}","decidedAt":"2026-10-13T12:00:00Z"}""");
        RecordEnvelope missing = AddDocument(estate, "docs/other-step.md", Encoding.UTF8.GetBytes("Required only for the other step."));
        File.Delete(Path.Combine(estate.Root, "docs/other-step.md"));
        JsonObject current = JsonNode.Parse(HandoffTests.Order)!.AsObject();
        current["inputs"] = new JsonArray(new JsonObject { ["type"] = "decision", ["id"] = decision.Id });
        RecordEnvelope active = estate.Add("work-order", estate.OperatorId, current.ToJsonString());
        JsonObject other = current.DeepClone().AsObject();
        other["key"] = "OTHER";
        other["inputs"]!.AsArray().Add(new JsonObject { ["type"] = "document-revision", ["id"] = missing.Id });
        RecordEnvelope unrelated = estate.Add("work-order", estate.OperatorId, other.ToJsonString());
        JsonObject shared = JsonNode.Parse(decision.Body.GetRawText())!.AsObject();
        shared["affects"] = new JsonArray(active.Id, unrelated.Id);
        RecordEnvelope affecting = estate.Add("decision", estate.OperatorId, shared.ToJsonString());
        using EstateService service = EstateService.Open(estate.Root);
        ContextResponse response = service.Context("H-1");
        Assert.Null(response.Problem);
        Assert.DoesNotContain(response.Package!.Payload.GetProperty("manifest").GetProperty("content").GetProperty("sources").EnumerateArray(), s => s.TryGetProperty("id", out JsonElement id) && id.GetString() == unrelated.Id);
        Assert.Single(response.Package.Payload.GetProperty("withheld").EnumerateArray());
        Assert.Contains(response.Package.Payload.GetProperty("originals").EnumerateArray(), s => s.TryGetProperty("id", out JsonElement id) && id.GetString() == affecting.Id && s.GetProperty("mandatory").GetBoolean());
        Assert.Equal("incomplete-context", service.Context("OTHER").Problem!.Code);
    }

    [Fact]
    public void APrerequisiteWorkRecordDoesNotMakeItsHistoricalFileInputsMandatoryForThisStep()
    {
        using var estate = new TestEstate(recordFormat: 2);
        RecordEnvelope missing = AddDocument(estate, "docs/prior-step.md", Encoding.UTF8.GetBytes("A prior step's original."));
        File.Delete(Path.Combine(estate.Root, "docs/prior-step.md"));
        JsonObject previous = JsonNode.Parse(HandoffTests.Order)!.AsObject();
        previous["key"] = "PRIOR";
        previous["inputs"] = new JsonArray(new JsonObject { ["type"] = "document-revision", ["id"] = missing.Id });
        RecordEnvelope prior = estate.Add("work-order", estate.OperatorId, previous.ToJsonString());
        JsonObject current = JsonNode.Parse(HandoffTests.Order)!.AsObject();
        current["inputs"] = new JsonArray(new JsonObject { ["type"] = "work-order", ["id"] = prior.Id });
        estate.Add("work-order", estate.OperatorId, current.ToJsonString());
        using EstateService service = EstateService.Open(estate.Root);
        ContextResponse response = service.Context("H-1");
        Assert.Null(response.Problem);
        Assert.Contains(response.Package!.Payload.GetProperty("originals").EnumerateArray(), s => s.TryGetProperty("id", out JsonElement id) && id.GetString() == prior.Id && s.GetProperty("mandatory").GetBoolean());
        Assert.Equal("incomplete-context", service.Context("PRIOR").Problem!.Code);
    }

    [Fact]
    public void UnreadableOriginalsDiscloseNoPathSizeOrIdentity()
    {
        if (OperatingSystem.IsWindows() || Environment.UserName == "root")
        {
            Assert.Skip("This permission-denial case requires Unix file modes and an unprivileged reader.");
            return;
        }

        using var estate = new TestEstate(recordFormat: 2);
        estate.Add("work-order", estate.OperatorId, HandoffTests.Order);
        byte[] bytes = Encoding.UTF8.GetBytes("An access-controlled original.");
        AddDocument(estate, "docs/restricted.md", bytes);
        string path = Path.Combine(estate.Root, "docs/restricted.md");
        using EstateService service = EstateService.Open(estate.Root);
        File.SetUnixFileMode(path, UnixFileMode.None);
        try
        {
            ContextResponse denied = service.Context("H-1");
            Assert.Null(denied.Package);
            Assert.Equal("Completeness cannot be confirmed.\n", denied.Text);
            Assert.Empty(denied.Problem!.Findings);
        }
        finally
        {
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }

        ContextPackage package = service.Context("H-1").Package!;
        string manifest = package.Payload.GetProperty("manifest").GetProperty("hash").GetString()!;
        JsonElement document = package.Payload.GetProperty("manifest").GetProperty("content").GetProperty("sources").EnumerateArray().Single(s => s.GetProperty("kind").GetString() == "document");
        string original = Path.Combine(estate.Root, "objects", ContentHash.Sha256(bytes)[7..]);
        File.SetUnixFileMode(original, UnixFileMode.None);
        try
        {
            ContextReadResponse denied = service.ContextRead(manifest, document.GetProperty("key").GetString()!);
            Assert.Null(denied.Identity);
            Assert.Null(denied.Content);
            Assert.Equal("Completeness cannot be confirmed.", denied.Problem!.Message);
        }
        finally
        {
            File.SetUnixFileMode(original, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
    }

    [Fact]
    public void GoverningAndUnresolvedReferencesRemainMandatoryEvenWhenNamedInText()
    {
        using var estate = new TestEstate(recordFormat: 2);
        RecordEnvelope decision = estate.Add("decision", estate.OperatorId, $$"""{"title":"Before a retry","stake":"medium","statement":"Inspect the original effect before retrying.","summary":"Reconcile.","synthesis":"The original operation must be reconciled.","effects":["inspect the original"],"notAuthorized":["a blind retry"],"affects":[],"decidedBy":"{{estate.OperatorId}}","decidedAt":"2026-10-13T12:00:00Z"}""");
        JsonObject body = JsonNode.Parse(HandoffTests.Order)!.AsObject();
        body["intent"] = "Resolve the uncertain effect under decision " + decision.Id + " before retrying.";
        estate.Add("work-order", estate.OperatorId, body.ToJsonString());
        using EstateService service = EstateService.Open(estate.Root);
        ContextPackage package = service.Context("H-1").Package!;
        JsonElement source = package.Payload.GetProperty("originals").EnumerateArray().Single(s => s.TryGetProperty("id", out JsonElement id) && id.GetString() == decision.Id);
        Assert.True(source.GetProperty("mandatory").GetBoolean());
        Assert.Equal(File.ReadAllBytes(Path.Combine(estate.Root, decision.ExpectedPath)), Convert.FromBase64String(source.GetProperty("content").GetString()!));
    }

    [Fact]
    public void RetentionRefusesAnOversizedArtifactWithoutWritingAnInvalidObject()
    {
        using var estate = new TestEstate(recordFormat: 2);
        estate.Add("work-order", estate.OperatorId, HandoffTests.Order);
        byte[] bytes = Encoding.UTF8.GetBytes(new string('a', 512 * 1024));
        for (int i = 0; i < 13; i++)
        {
            AddDocument(estate, "docs/source-" + i.ToString(System.Globalization.CultureInfo.InvariantCulture) + ".md", bytes);
        }

        using EstateService service = EstateService.Open(estate.Root);
        ContextResponse refused = service.Context("H-1");
        Assert.Null(refused.Package);
        Assert.Equal("context-retention-limit", refused.Problem!.Code);
        Assert.Empty(service.Objects());
    }

    [Fact]
    public void BriefCarriesTheSharedPackageAndMeasuresBothCompleteRenderings()
    {
        using var estate = new TestEstate(recordFormat: 2);
        RecordEnvelope order = estate.Add("work-order", estate.OperatorId, HandoffTests.Order);
        RecordEnvelope assignment = estate.Add("assignment", estate.OperatorId, $$$"""{"contributor":"{{{estate.BuilderId}}}","brief":{{{RunEstate.Brief}}}}""", order.Reference);
        RecordEnvelope attempt = estate.Add("attempt", estate.BuilderId, """{"startedAt":"2026-10-13T12:00:05Z","workspace":"widget clone","note":"Continue."}""", assignment.Reference);
        RecordEnvelope result = estate.Add("result", estate.BuilderId, """{"outcome":"partial","summary":"One check failed: café.","outputs":[],"evidence":[],"checks":[{"name":"tests","outcome":"failed","summary":"Still broken."}],"limitations":["No publication."],"unresolvedEffects":["Original operation remains uncertain."],"nextStep":"Reconcile the original operation before retrying."}""", attempt.Reference);
        AddDocument(estate, "docs/missing.md", Encoding.UTF8.GetBytes("Optional source."));
        File.Delete(Path.Combine(estate.Root, "docs/missing.md"));
        using EstateService service = EstateService.Open(estate.Root);
        BriefResponse brief = service.Brief("H-1")!;
        Assert.Null(brief.Problem);
        Assert.Contains("# Brief: H-1", brief.Markdown, StringComparison.Ordinal);
        Assert.Contains("Original operation remains uncertain.", brief.Markdown, StringComparison.Ordinal);
        Assert.Contains("Still broken.", brief.Markdown, StringComparison.Ordinal);
        Assert.Contains(result.ComputedHash, brief.Markdown, StringComparison.Ordinal);
        Assert.Contains("Withheld originals", brief.Markdown, StringComparison.Ordinal);
        Assert.DoesNotContain(estate.Root, brief.Markdown, StringComparison.Ordinal);
        Assert.Equal(Encoding.UTF8.GetByteCount(brief.Markdown), brief.MarkdownMeasurement!.Bytes);
        Assert.Equal(ContentHash.Sha256(Encoding.UTF8.GetBytes(brief.Markdown)), brief.MarkdownMeasurement.Hash);
        byte[] json = CanonicalJson.SerializePrettyUtf8(brief.Json);
        Assert.Equal(json.LongLength, brief.JsonMeasurement!.Bytes);
        Assert.Equal(ContentHash.Sha256(json), brief.JsonMeasurement.Hash);
        Assert.Equal(json.LongLength, brief.Json.GetProperty("accounting").GetProperty("bytes").GetInt64());
        JsonElement context = brief.Json.GetProperty("context");
        Assert.Equal(service.Context("H-1").Package!.Hash, context.GetProperty("hash").GetString());
        Assert.Single(context.GetProperty("payload").GetProperty("withheld").EnumerateArray());
        Assert.Contains(context.GetProperty("payload").GetProperty("originals").EnumerateArray(), s => s.TryGetProperty("id", out JsonElement id) && id.GetString() == result.Id);
    }

    [Fact]
    public void HandoffUsesTheSameSourceManifestAndAccountingAndKeepsLegacyResumeReadable()
    {
        using var estate = new TestEstate(recordFormat: 2);
        estate.Add("work-order", estate.OperatorId, HandoffTests.Order);
        using EstateService service = EstateService.Open(estate.Root);
        HandoffResponse handoff = service.Handoff();
        Assert.Null(handoff.Problem);
        HandoffPackage package = handoff.Package!;
        Assert.Equal(ContextAssembly.Format, package.Payload.GetProperty("contextFormat").GetString());
        Assert.True(ContextAccounting.Verify(new ContextPackage(package.Hash, package.Bytes, package.Payload)));
        Assert.Equal(Encoding.UTF8.GetByteCount(handoff.Text), handoff.TextMeasurement!.Bytes);
        Assert.Equal(ContentHash.Sha256(Encoding.UTF8.GetBytes(handoff.Text)), handoff.TextMeasurement.Hash);
        string manifest = package.Payload.GetProperty("manifest").GetProperty("hash").GetString()!;
        foreach (JsonElement original in package.Payload.GetProperty("originals").EnumerateArray())
        {
            Assert.Equal(Convert.FromBase64String(original.GetProperty("content").GetString()!), service.ContextRead(manifest, original.GetProperty("key").GetString()!).Content);
        }

        JsonObject legacy = JsonNode.Parse(package.Payload.GetRawText())!.AsObject();
        legacy.Remove("contextFormat");
        legacy.Remove("manifest");
        legacy.Remove("derivatives");
        legacy.Remove("excerpts");
        ContextPackage old = ContextAssembly.Measure(legacy);
        Assert.Null(service.ResumeHandoff(new HandoffPackage(old.Hash, old.Bytes, old.Payload)).Problem);
        JsonObject damaged = JsonNode.Parse(package.Payload.GetRawText())!.AsObject();
        damaged["manifest"]!["content"]!["sources"]![0]!["byteHash"] = "sha256:" + new string('a', 64);
        ContextPackage modified = ContextAccounting.Measure(damaged, null);
        Assert.Equal("invalid-handoff", service.ResumeHandoff(new HandoffPackage(modified.Hash, modified.Bytes, modified.Payload)).Problem!.Code);
    }

    [Fact]
    public void BudgetCountsTheFinalUtf8PayloadIncludingWithheldEntriesAndItsOwnAccounting()
    {
        using var estate = new TestEstate(recordFormat: 2);
        estate.Add("work-order", estate.OperatorId, HandoffTests.Order);
        byte[] optional = Encoding.UTF8.GetBytes(string.Concat(Enumerable.Repeat("café 漢字\n", 20_000)));
        AddDocument(estate, "docs/large.md", optional);
        using EstateService service = EstateService.Open(estate.Root);
        ContextPackage unlimited = service.Context("H-1").Package!;
        Assert.Equal("limit not set", unlimited.Payload.GetProperty("accounting").GetProperty("capSource").GetString());
        long cap = unlimited.Bytes / 2;
        ContextResponse response = service.Context("H-1", cap);
        ContextPackage bounded = Assert.IsType<ContextPackage>(response.Package);
        JsonElement accounting = bounded.Payload.GetProperty("accounting");
        Assert.Equal(Encoding.UTF8.GetByteCount(response.Text), bounded.Bytes);
        Assert.Equal(bounded.Bytes, accounting.GetProperty("bytes").GetInt64());
        Assert.Equal(cap - bounded.Bytes, accounting.GetProperty("remainingBytes").GetInt64());
        Assert.Equal(0, accounting.GetProperty("overageBytes").GetInt64());
        Assert.Equal(JsonValueKind.Null, accounting.GetProperty("tokens").ValueKind);
        Assert.Equal(JsonValueKind.Null, accounting.GetProperty("model").ValueKind);
        Assert.Equal("not claimed", accounting.GetProperty("modelFit").GetString());
        Assert.Contains("separate from the run spending cap", accounting.GetProperty("allocationKind").GetString(), StringComparison.Ordinal);
        JsonElement withheld = Assert.Single(bounded.Payload.GetProperty("withheld").EnumerateArray());
        Assert.Equal(optional.LongLength, withheld.GetProperty("originalBytes").GetInt64());
        Assert.Equal("budget", withheld.GetProperty("reason").GetString());
        string manifest = bounded.Payload.GetProperty("manifest").GetProperty("hash").GetString()!;
        Assert.Equal(optional, service.ContextRead(manifest, withheld.GetProperty("key").GetString()!).Content);
        Assert.Equal(bounded.Hash, service.Context("H-1", cap).Package!.Hash);
        Assert.True(ContextAccounting.Verify(bounded));
        JsonObject edited = JsonNode.Parse(bounded.Payload.GetRawText())!.AsObject();
        edited["withheld"] = new JsonArray();
        Assert.False(ContextAccounting.Verify(bounded with { Payload = JsonSerializer.SerializeToElement(edited) }));
        Assert.Null(service.Context("H-1", 1).Package);
        Assert.Equal("context-over-budget", service.Context("H-1", 1).Problem!.Code);
        Assert.Equal("invalid", service.Context("H-1", 0).Problem!.Code);
        Assert.Equal("invalid", service.Context("H-1", long.MaxValue).Problem!.Code);
    }

    [Fact]
    public void MandatoryPayloadAndManifestOverheadAreNeverTruncatedToFit()
    {
        byte[] original = Encoding.UTF8.GetBytes("Required: café 漢字.\n");
        var source = new ContextAssembly.Source(new JsonObject { ["kind"] = "document", ["path"] = "docs/required.md", ["byteHash"] = ContentHash.Sha256(original), ["bytes"] = original.LongLength }, original, true, "before deciding");
        var basis = new JsonObject { ["scope"] = "one required original" };
        Assert.Equal("context-over-budget", ContextAssembly.Select(basis, [source], "The full required instructions.", original.Length).Problem!.Code);
        long cap = 10_000;
        for (int i = 0; i < 10; i++)
        {
            long measured = ContextAssembly.Build(basis, [source], "The full required instructions.", cap).Bytes;
            if (cap == measured)
            {
                break;
            }

            cap = measured;
        }

        ContextResponse exact = ContextAssembly.Select(basis, [source], "The full required instructions.", cap);
        Assert.NotNull(exact.Package);
        Assert.Equal(cap, Encoding.UTF8.GetByteCount(exact.Text));
        Assert.Equal(original, Convert.FromBase64String(exact.Package.Payload.GetProperty("originals")[0].GetProperty("content").GetString()!));
        ContextResponse overflow = ContextAssembly.Select(basis, [source], "The full required instructions.", cap - 1);
        Assert.Null(overflow.Package);
        Assert.Equal("context-over-budget", overflow.Problem!.Code);
        Assert.Contains("Nothing was truncated or dispatched", overflow.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void AccountingRemainsExactAcrossDecimalBoundaries()
    {
        for (int cap = 900; cap <= 1100; cap++)
        {
            ContextPackage package = ContextAccounting.Measure(new JsonObject { ["text"] = "A multibyte payload: 漢字 café." }, cap);
            Assert.True(ContextAccounting.Verify(package));
            JsonElement accounting = package.Payload.GetProperty("accounting");
            Assert.Equal(Math.Max(0, cap - package.Bytes), accounting.GetProperty("remainingBytes").GetInt64());
            Assert.Equal(Math.Max(0, package.Bytes - cap), accounting.GetProperty("overageBytes").GetInt64());
        }
    }

    [Fact]
    public void SelectionOrdersMandatoryMaterialFirstAndLabelsEveryPartialOriginal()
    {
        byte[] bytes = Encoding.UTF8.GetBytes("café and more material\n");
        var identity = new JsonObject { ["kind"] = "document", ["path"] = "docs/source.md", ["byteHash"] = ContentHash.Sha256(bytes), ["bytes"] = bytes.LongLength };
        var excerpt = new ContextAssembly.Source(identity, bytes, false, "optional for this step", ExcerptBytes: 5);
        var mandatory = new ContextAssembly.Source(new JsonObject { ["kind"] = "document", ["path"] = "docs/z-required.md", ["byteHash"] = ContentHash.Sha256(bytes), ["bytes"] = bytes.LongLength }, bytes, true, "before continuing");
        var missing = new ContextAssembly.Source(new JsonObject { ["kind"] = "document", ["path"] = "docs/missing.md", ["byteHash"] = "sha256:" + new string('a', 64), ["bytes"] = null }, null, false, "optional for this step", State: "not-retained");
        ContextPackage first = ContextAssembly.Build(new JsonObject(), [excerpt, missing, mandatory], "lead");
        Assert.Equal(first.Hash, ContextAssembly.Build(new JsonObject(), [mandatory, missing, excerpt], "lead").Hash);
        Assert.True(first.Payload.GetProperty("manifest").GetProperty("content").GetProperty("sources")[0].GetProperty("mandatory").GetBoolean());
        Assert.Equal(2, first.Payload.GetProperty("withheld").GetArrayLength());
        JsonElement held = first.Payload.GetProperty("withheld").EnumerateArray().Single(e => e.GetProperty("reason").GetString() == "excerpt");
        Assert.Equal(5, held.GetProperty("includedBytes").GetInt64());
        Assert.Equal(bytes.LongLength - 5, held.GetProperty("withheldBytes").GetInt64());
        Assert.Equal("irreversible reduction of this representation; original retained", held.GetProperty("reduction").GetProperty("loss").GetString());
        Assert.Equal(bytes[..5], Convert.FromBase64String(first.Payload.GetProperty("excerpts")[0].GetProperty("content").GetString()!));
        Assert.Equal(5, held.GetProperty("extent").GetProperty("endExclusive").GetInt32());
        JsonElement unavailable = first.Payload.GetProperty("withheld").EnumerateArray().Single(e => e.GetProperty("state").GetString() == "not-retained");
        Assert.Equal(JsonValueKind.Null, unavailable.GetProperty("originalBytes").ValueKind);
        Assert.Equal("irreversible reduction; original unavailable", unavailable.GetProperty("reduction").GetProperty("loss").GetString());
        Assert.Throws<InvalidOperationException>(() => ContextAssembly.Build(new JsonObject(), [mandatory with { Inline = false }], "lead"));
        Assert.Throws<InvalidOperationException>(() => ContextAssembly.Build(new JsonObject(), [mandatory with { Original = null }], "lead"));
        JsonObject recordIdentity = identity.DeepClone().AsObject();
        recordIdentity["kind"] = "record";
        Assert.Throws<InvalidOperationException>(() => ContextAssembly.Build(new JsonObject(), [excerpt with { Identity = recordIdentity }], "lead"));
    }

    [Fact]
    public void PinnedRetrievalSurvivesSourceEditsAndRejectsTampering()
    {
        using var estate = new TestEstate(recordFormat: 2);
        RecordEnvelope order = estate.Add("work-order", estate.OperatorId, HandoffTests.Order);
        byte[] bytes = Encoding.UTF8.GetBytes("Original café.\n");
        RecordEnvelope revision = AddDocument(estate, "docs/context.md", bytes);
        using EstateService service = EstateService.Open(estate.Root);
        int count = estate.Load().Records.Count;
        ContextPackage package = service.Context("H-1").Package!;
        Assert.Equal(count, estate.Load().Records.Count);
        string manifest = package.Payload.GetProperty("manifest").GetProperty("hash").GetString()!;
        JsonElement identity = package.Payload.GetProperty("manifest").GetProperty("content").GetProperty("sources").EnumerateArray().Single(s => s.GetProperty("kind").GetString() == "document");
        string key = identity.GetProperty("key").GetString()!;
        JsonElement recordSource = package.Payload.GetProperty("manifest").GetProperty("content").GetProperty("sources").EnumerateArray()
            .Single(s => s.TryGetProperty("id", out JsonElement id) && id.GetString() == order.Id);
        estate.Add("work-order", estate.OperatorId, HandoffTests.Order.Replace("Continue from records.", "A newer instruction.", StringComparison.Ordinal), supersedes: order.Id);
        ContextReadResponse pinnedRecord = service.ContextRead(manifest, recordSource.GetProperty("key").GetString()!);
        Assert.Equal(order.ComputedHash, pinnedRecord.Identity!.Value.GetProperty("hash").GetString());
        Assert.Equal(File.ReadAllBytes(Path.Combine(estate.Root, order.ExpectedPath)), pinnedRecord.Content);
        File.WriteAllText(Path.Combine(estate.Root, "docs/context.md"), "Changed working file.");
        Assert.Equal(bytes, service.ContextRead(manifest, key).Content);
        string hash = ContentHash.Sha256(bytes);
        string heldPath = Path.Combine(estate.Root, "objects", hash[7..]);
        File.WriteAllText(heldPath, "Corrupted original.");
        ContextReadResponse corrupted = service.ContextRead(manifest, key);
        Assert.Equal("invalid-context", corrupted.Problem!.Code);
        Assert.Null(corrupted.Content);
        File.Delete(heldPath);
        Assert.Equal("original-unavailable", service.ContextRead(manifest, key).Problem!.Code);
        Assert.Null(service.ContextRead(manifest, key).Content);
        Assert.Equal(revision.Id, identity.GetProperty("revision").GetString());
    }

    [Fact]
    public void MissingRequiredDocumentRefusesAndOptionalUnknownSizeIsHonest()
    {
        using var estate = new TestEstate(recordFormat: 2);
        RecordEnvelope revision = AddDocument(estate, "docs/missing.md", Encoding.UTF8.GetBytes("not kept"));
        File.Delete(Path.Combine(estate.Root, "docs/missing.md"));
        RecordEnvelope order = estate.Add("work-order", estate.OperatorId, HandoffTests.Order);
        using EstateService service = EstateService.Open(estate.Root);
        ContextPackage package = service.Context("H-1").Package!;
        JsonElement withheld = Assert.Single(package.Payload.GetProperty("withheld").EnumerateArray());
        Assert.Equal("missing", withheld.GetProperty("state").GetString());
        Assert.Equal(JsonValueKind.Null, withheld.GetProperty("originalBytes").ValueKind);
        Assert.Contains("--original", withheld.GetProperty("retrieve").GetString(), StringComparison.Ordinal);
        JsonObject required = JsonNode.Parse(HandoffTests.Order)!.AsObject();
        required["inputs"] = new JsonArray(new JsonObject { ["type"] = "document-revision", ["id"] = revision.Id, ["hash"] = revision.ComputedHash });
        estate.Add("work-order", estate.OperatorId, required.ToJsonString(), supersedes: order.Id);
        ContextResponse refused = service.Context("H-1");
        Assert.Null(refused.Package);
        Assert.Equal("incomplete-context", refused.Problem!.Code);
        Assert.Contains(refused.Problem.Findings, f => f.Location == "docs/missing.md");
    }

    [Fact]
    public void RevokedReaderGetsNoSourceMetadataEvenForPreviouslyIssuedHandles()
    {
        using var estate = new TestEstate(recordFormat: 2);
        estate.Add("work-order", estate.OperatorId, HandoffTests.Order);
        AddDocument(estate, "docs/confidential.md", Encoding.UTF8.GetBytes("Private original."));
        using EstateService service = EstateService.Open(estate.Root);
        string device = service.DeviceCreate(new DeviceCreateRequest(estate.OperatorId, "Reader", "file")).Written!.Id;
        Assert.True(service.DeviceAdmit(new AdmitRequest(device, estate.OperatorId, "Read this estate.")).Ok);
        DeviceStanding standing = service.Standing(device)!;
        ContextPackage package = service.Context("H-1").Package!;
        string manifest = package.Payload.GetProperty("manifest").GetProperty("hash").GetString()!;
        HandoffPackage handoff = service.Handoff().Package!;
        using (service.ForDeviceRequest(standing.Identity, standing.PublicKey))
        {
            Assert.Null(service.ContextRead(manifest, "s000000").Problem);
            Assert.True(service.DeviceRevoke(new RevokeRequest(device, estate.OperatorId, "Withdraw access.")).Ok);
            ContextReadResponse denied = service.ContextRead(manifest, "s000000");
            Assert.Null(denied.Identity);
            Assert.Null(denied.Content);
            Assert.Equal("Completeness cannot be confirmed.", denied.Problem!.Message);
            Assert.Equal(denied, service.ContextRead("unknown", "unknown"));
            ContextResponse noPackage = service.Context("H-1");
            Assert.Null(noPackage.Package);
            Assert.Equal("Completeness cannot be confirmed.\n", noPackage.Text);
            Assert.Empty(noPackage.Problem!.Findings);
            Assert.Equal("Completeness cannot be confirmed.\n", service.Brief("H-1")!.Markdown);
            Assert.Equal("Completeness cannot be confirmed.\n", service.Handoff().Text);
            Assert.Equal("Completeness cannot be confirmed.\n", service.ResumeHandoff(handoff).Text);
        }
    }

    internal static RecordEnvelope AddDocument(TestEstate estate, string path, byte[] bytes)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(estate.Root, path))!);
        File.WriteAllBytes(Path.Combine(estate.Root, path), bytes);
        return estate.Add("document-revision", estate.OperatorId, $$"""{"path":"{{path}}","title":"Context source","hash":"{{ContentHash.Sha256(bytes)}}","bytes":{{bytes.Length}},"mediaType":"text/markdown","revision":1,"summary":"The original."}""");
    }

    [Fact]
    public void VersionedManifestPinsBasisAndExactOriginalApartFromDerivedPresentation()
    {
        using var estate = new TestEstate(recordFormat: 2);
        RecordEnvelope order = estate.Add("work-order", estate.OperatorId, HandoffTests.Order);
        byte[] original = File.ReadAllBytes(Path.Combine(estate.Root, order.ExpectedPath));
        var basis = new JsonObject { ["estate"] = estate.Estate.Id, ["workOrder"] = order.Id, ["hash"] = order.ComputedHash };
        ContextAssembly.Source source = ContextAssembly.Record(order, new RecordFile(order.ExpectedPath, original), true, "before continuing the work");
        ContextPackage package = ContextAssembly.Build(basis, [source], "A derived lead: café.");
        Assert.Equal("vericairn.context/1", package.Payload.GetProperty("format").GetString());
        JsonElement manifest = package.Payload.GetProperty("manifest");
        Assert.Equal("vericairn.context-manifest/1", manifest.GetProperty("content").GetProperty("format").GetString());
        Assert.Equal(ContentHash.Sha256(CanonicalJson.SerializePrettyUtf8(manifest.GetProperty("content"))), manifest.GetProperty("hash").GetString());
        Assert.Equal(order.ComputedHash, manifest.GetProperty("content").GetProperty("basis").GetProperty("hash").GetString());
        JsonElement retained = Assert.Single(package.Payload.GetProperty("originals").EnumerateArray());
        Assert.Equal(original, Convert.FromBase64String(retained.GetProperty("content").GetString()!));
        Assert.Equal(order.ComputedHash, retained.GetProperty("hash").GetString());
        Assert.Equal(ContentHash.Sha256(original), retained.GetProperty("byteHash").GetString());
        Assert.NotEqual(retained.GetProperty("hash").GetString(), retained.GetProperty("byteHash").GetString());
        Assert.Equal("off", package.Payload.GetProperty("presentation").GetProperty("synthesis").GetString());
        Assert.Equal(package.Hash, ContentHash.Sha256(CanonicalJson.SerializePrettyUtf8(package.Payload)));
        ContextPackage changed = ContextAssembly.Build(basis, [source], "Another lead.");
        Assert.NotEqual(package.Hash, changed.Hash);
        Assert.Equal(manifest.GetRawText(), changed.Payload.GetProperty("manifest").GetRawText());
        Assert.Equal(retained.GetRawText(), changed.Payload.GetProperty("originals")[0].GetRawText());
        Assert.NotEqual(package.Payload.GetProperty("derivatives")[0].GetProperty("hash").GetString(), changed.Payload.GetProperty("derivatives")[0].GetProperty("hash").GetString());
    }
}
