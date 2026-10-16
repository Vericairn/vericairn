// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using Vericairn.Protocol;
using Vericairn.Records.Canonical;
using Vericairn.Records.Estate;
using Vericairn.Records.Hashing;
using Vericairn.Records.Model;
using Vericairn.Records.Tests;
using Vericairn.Runtime.Service;

namespace Vericairn.Runtime.Tests;

public class HandoffTests
{
    internal const string Order = """{"key":"H-1","kind":"ordinary","title":"Handoff","intent":"Continue from records.","scope":["widget"],"exitQuestion":"Complete?","inputs":[],"deliverables":["widget"]}""";

    [Fact]
    public void TheWholeWorkAndEveryOriginalAreRetainedApartFromThePresentation()
    {
        using var estate = new TestEstate(recordFormat: 2);
        RecordEnvelope order = estate.Add("work-order", estate.OperatorId, Order);
        RecordEnvelope assignment = estate.Add("assignment", estate.OperatorId, $$$"""{"contributor":"{{{estate.BuilderId}}}","brief":{{{RunEstate.Brief}}}}""", order.Reference);
        RecordEnvelope attempt = estate.Add("attempt", estate.BuilderId, """{"startedAt":"2026-10-13T12:00:05Z","workspace":"widget clone","note":"Continue."}""", assignment.Reference);
        RecordEnvelope result = estate.Add("result", estate.BuilderId, """{"outcome":"partial","summary":"One check failed: café.","outputs":[],"evidence":[],"checks":[{"name":"tests","outcome":"failed","summary":"Still broken."}],"limitations":["No publication."],"unresolvedEffects":["Original operation remains uncertain."],"nextStep":"Fix the failing check."}""", attempt.Reference);
        estate.Add("work-order", estate.OperatorId, Order.Replace("Handoff", "Revised handoff", StringComparison.Ordinal), supersedes: order.Id);
        estate.Add("ask", estate.BuilderId, $$"""{"to":"{{estate.OperatorId}}","question":"Continue?","recommendation":"Check first.","consequence":"Wait.","state":"open"}""");
        using EstateService service = EstateService.Open(estate.Root);
        int before = estate.Load().Records.Count;
        HandoffResponse response = service.Handoff();
        Assert.Null(response.Problem);
        HandoffPackage package = Assert.IsType<HandoffPackage>(response.Package);
        byte[] bytes = CanonicalJson.SerializePrettyUtf8(package.Payload);
        Assert.Equal(bytes.LongLength, package.Bytes);
        Assert.Equal(ContentHash.Sha256(bytes), package.Hash);
        Assert.Equal(before, package.Payload.GetProperty("originals").GetArrayLength());
        Assert.Equal(before, estate.Load().Records.Count);
        Assert.Contains("Fix the failing check.", response.Text, StringComparison.Ordinal);
        Assert.Contains("Still broken.", response.Text, StringComparison.Ordinal);
        Assert.Contains("Original operation remains uncertain.", response.Text, StringComparison.Ordinal);
        Assert.Contains(result.ComputedHash, response.Text, StringComparison.Ordinal);
        Assert.Equal("off", package.Payload.GetProperty("presentation").GetProperty("synthesis").GetString());
        Assert.Equal(JsonValueKind.Null, package.Payload.GetProperty("accounting").GetProperty("tokens").ValueKind);
        foreach (JsonElement source in package.Payload.GetProperty("originals").EnumerateArray())
        {
            byte[] original = Convert.FromBase64String(source.GetProperty("content").GetString()!);
            Assert.Equal(File.ReadAllBytes(Path.Combine(estate.Root, source.GetProperty("path").GetString()!)), original);
            Assert.Equal(original.LongLength, source.GetProperty("bytes").GetInt64());
            Assert.Equal(ContentHash.Sha256(original), source.GetProperty("byteHash").GetString());
            string id = source.GetProperty("id").GetString()!;
            Assert.Equal(source.GetProperty("hash").GetString(), service.Record(id)!.Hash);
            Assert.Equal("vericairn records show " + id, source.GetProperty("retrieve").GetString());
        }

        Assert.False(service.Record(order.Id)!.Current);
        Assert.Equal(package.Hash, service.Handoff().Package!.Hash);
        Assert.Null(service.Handoff(1).Package);
        Assert.Equal("handoff-over-budget", service.Handoff(1).Problem!.Code);
        Assert.Equal("invalid", service.Handoff(0).Problem!.Code);
        Assert.NotNull(service.Handoff(1_000_000).Package);
    }
}
