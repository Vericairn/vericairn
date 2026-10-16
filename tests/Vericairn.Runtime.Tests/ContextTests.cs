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
    }

    [Fact]
    public void PinnedRetrievalSurvivesSourceEditsAndRejectsTampering()
    {
        using var estate = new TestEstate(recordFormat: 2);
        estate.Add("work-order", estate.OperatorId, HandoffTests.Order);
        byte[] bytes = Encoding.UTF8.GetBytes("Original café.\n");
        RecordEnvelope revision = AddDocument(estate, "docs/context.md", bytes);
        using EstateService service = EstateService.Open(estate.Root);
        int count = estate.Load().Records.Count;
        ContextPackage package = service.Context("H-1").Package!;
        Assert.Equal(count, estate.Load().Records.Count);
        string manifest = package.Payload.GetProperty("manifest").GetProperty("hash").GetString()!;
        JsonElement identity = package.Payload.GetProperty("manifest").GetProperty("content").GetProperty("sources").EnumerateArray().Single(s => s.GetProperty("kind").GetString() == "document");
        string key = identity.GetProperty("key").GetString()!;
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
