// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Vericairn.Mcp;
using Vericairn.Protocol;
using Vericairn.Records.Hashing;
using Vericairn.Records.Identity;
using Vericairn.Sdk;

namespace Vericairn.Runtime.Tests;

public sealed class ReleaseBasisTests
{
    [Fact]
    public void UnknownHashesAndUnsupportedObjectsRemainAbsent()
    {
        using var f = new ReleaseBasisFixture();
        Assert.Null(f.Api.ReleaseBasis("not-a-hash"));
        Assert.Null(f.Api.ReleaseBasis(ContentHash.Sha256("unknown"u8)));
        Assert.Null(f.Api.ReleaseBasis(f.ProofHash));
        Assert.Null(f.Api.ReleaseBasis(f.Api.Session.Objects.Put("{\"format\":\"vericairn.release-basis/99\"}"u8)));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SourceLossIsExplicitAcrossTheRuntimeSdkAndMcp(bool corrupted)
    {
        using var f = new ReleaseBasisFixture();
        int before = f.Api.Session.Load().Records.Count;
        f.LoseObject(f.SourceHash, corrupted);

        ReleaseBasisResponse response = Assert.IsType<ReleaseBasisResponse>(f.Api.ReleaseBasis(f.Hash));

        Assert.Equal(f.Hash, response.Hash);
        Assert.Null(response.Evidence);
        Assert.Equal("release-basis-unavailable", response.Problem!.Code);
        Assert.Equal($"Retained release basis {f.Hash} cannot resolve.", response.Problem.Message);
        Assert.Equal(new FindingDto("error", corrupted ? "release-basis.hash-mismatch" : "release-basis.missing", f.SourceLocation,
            corrupted ? "The referenced object's bytes do not match its hash." : "The referenced object is missing."), Assert.Single(response.Problem.Findings));
        string expected = JsonSerializer.Serialize(response, ProtocolJson.Options);
        using var client = new VericairnClient(new HttpClient(new BasisHandler(response)) { BaseAddress = new Uri("http://fixture.invalid") });
        var bound = new CancellationBoundApi(client, TestContext.Current.CancellationToken);
        Assert.Equal(expected, JsonSerializer.Serialize(client.ReleaseBasis(f.Hash), ProtocolJson.Options));
        Assert.Equal(expected, JsonSerializer.Serialize(await client.ReleaseBasisAsync(f.Hash, TestContext.Current.CancellationToken), ProtocolJson.Options));
        Assert.Equal(expected, JsonSerializer.Serialize(bound.ReleaseBasis(f.Hash), ProtocolJson.Options));
        Assert.Equal(expected, JsonSerializer.Serialize(await bound.ReleaseBasisAsync(f.Hash, TestContext.Current.CancellationToken), ProtocolJson.Options));
        Assert.Equal(expected, JsonSerializer.Serialize(await ((IEstateApi)f.Api).ReleaseBasisAsync(f.Hash, TestContext.Current.CancellationToken), ProtocolJson.Options));
        ToolCallResult read = await new ToolRegistry(f.Api, null).CallAsync("release_basis_get", new() { ["hash"] = f.Hash }, TestContext.Current.CancellationToken);
        Assert.True(read.IsError);
        Assert.Equal(expected, read.Text);
        Assert.Equal(before, f.Api.Session.Load().Records.Count);
    }

    [Fact]
    public void EveryFailedReferenceIsReportedIncludingSharedObjectsAndRecords()
    {
        using var f = new ReleaseBasisFixture();
        JsonArray files = f.Stored["repositories"]![0]!["files"]!.AsArray();
        JsonObject duplicate = files[0]!.DeepClone().AsObject();
        duplicate["path"] = "docs/also-evidence.md";
        files.Add(duplicate);
        string mismatched = ContentHash.Sha256("changed record"u8);
        JsonObject record = f.Stored["records"]![0]!.AsObject();
        record["hash"] = mismatched;
        string missing = Ulid.NewUlid().Value;
        f.Stored["records"]!.AsArray().Add(new JsonObject { ["type"] = "decision", ["id"] = missing, ["hash"] = mismatched });
        string hash = f.Store();
        f.LoseObject(f.SourceHash, corrupted: false);
        f.LoseObject(f.ProofHash, corrupted: true);

        ReleaseBasisResponse response = Assert.IsType<ReleaseBasisResponse>(f.Api.ReleaseBasis(hash));

        Assert.Null(response.Evidence);
        Assert.Equal("release-basis-unavailable", response.Problem!.Code);
        Assert.Collection(response.Problem.Findings,
            finding => AssertFailure(finding, "hash-mismatch", $"record:{record["type"]}:{record["id"]}@{mismatched}"),
            finding => AssertFailure(finding, "missing", $"record:decision:{missing}@{mismatched}"),
            finding => AssertFailure(finding, "missing", f.SourceLocation),
            finding => AssertFailure(finding, "missing", $"source:fixture-repository@fixture-revision:docs/also-evidence.md@{f.SourceHash}"),
            finding => AssertFailure(finding, "hash-mismatch", "object:" + f.ProofHash));
    }

    [Fact]
    public void SourcesAlreadyUnavailableAtAdmissionKeepTheirHistoricalMeaning()
    {
        using var f = new ReleaseBasisFixture();
        f.Stored["repositories"]![0]!["files"]![0]!["available"] = false;
        f.Stored["objects"]![0]!["available"] = false;
        string hash = f.Store();
        f.LoseObject(f.SourceHash, corrupted: false);
        f.LoseObject(f.ProofHash, corrupted: false);

        ReleaseBasisResponse response = Assert.IsType<ReleaseBasisResponse>(f.Api.ReleaseBasis(hash));

        Assert.Null(response.Problem);
        Assert.NotNull(response.Evidence);
        ReleaseBasisFile file = Assert.Single(Assert.Single(response.Evidence.Repositories).Files);
        Assert.False(file.Available);
        Assert.Empty(file.Bytes);
        ReleaseBasisObject proof = Assert.Single(response.Evidence.Objects);
        Assert.False(proof.Available);
        Assert.Empty(proof.Bytes);
    }

    [Fact]
    public void CorruptionOfTheBasisItselfNamesItsHash()
    {
        using var f = new ReleaseBasisFixture();
        f.LoseObject(f.Hash, corrupted: true);
        ReleaseBasisResponse response = Assert.IsType<ReleaseBasisResponse>(f.Api.ReleaseBasis(f.Hash));
        Assert.Null(response.Evidence);
        AssertFailure(Assert.Single(response.Problem!.Findings), "hash-mismatch", "basis:" + f.Hash);
    }

    private static void AssertFailure(FindingDto finding, string reason, string location)
    {
        Assert.Equal("error", finding.Severity);
        Assert.Equal("release-basis." + reason, finding.Code);
        Assert.Equal(location, finding.Location);
        Assert.NotEmpty(finding.Message);
    }

    private sealed class BasisHandler(ReleaseBasisResponse response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Assert.Equal("/v1/release-bases/" + Uri.EscapeDataString(response.Hash), request.RequestUri!.AbsolutePath);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(response, options: ProtocolJson.Options) });
        }
    }
}
