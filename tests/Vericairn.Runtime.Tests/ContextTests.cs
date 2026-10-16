// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using System.Text.Json.Nodes;
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
