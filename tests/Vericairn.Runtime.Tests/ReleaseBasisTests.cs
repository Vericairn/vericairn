// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Vericairn.Mcp;
using Vericairn.Protocol;
using Vericairn.Records.Estate;
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

    // FIX-163: a basis past the object limit is retained as a split object; its root reads as the one basis its parts join to.
    [Fact]
    public async Task ABasisRetainedInPartsReadsAsItsOneObjectAcrossTheRuntimeSdkAndMcp()
    {
        using var f = new ReleaseBasisFixture();
        ReleaseBasisResponse single = Assert.IsType<ReleaseBasisResponse>(f.Api.ReleaseBasis(f.Hash));
        (string root, IReadOnlyList<string> parts, IReadOnlyList<string> documents) = f.StoreInParts();
        Assert.Equal(2, parts.Count);

        ReleaseBasisResponse response = Assert.IsType<ReleaseBasisResponse>(f.Api.ReleaseBasis(root));

        Assert.Null(response.Problem);
        Assert.Equal(root, response.Hash);
        string expected = JsonSerializer.Serialize(single with { Hash = root, Evidence = single.Evidence! with { Documents = documents } }, ProtocolJson.Options);
        Assert.Equal(expected, JsonSerializer.Serialize(response, ProtocolJson.Options));
        using var client = new VericairnClient(new HttpClient(new BasisHandler(response)) { BaseAddress = new Uri("http://fixture.invalid") });
        Assert.Equal(expected, JsonSerializer.Serialize(client.ReleaseBasis(root), ProtocolJson.Options));
        Assert.Equal(expected, JsonSerializer.Serialize(await new CancellationBoundApi(client, TestContext.Current.CancellationToken).ReleaseBasisAsync(root, TestContext.Current.CancellationToken), ProtocolJson.Options));
        ToolCallResult read = await new ToolRegistry(f.Api, null).CallAsync("release_basis_get", new() { ["hash"] = root }, TestContext.Current.CancellationToken);
        Assert.False(read.IsError);
        Assert.Equal(expected, read.Text);

        f.LoseObject(parts[1], corrupted: false);
        ReleaseBasisResponse lost = Assert.IsType<ReleaseBasisResponse>(f.Api.ReleaseBasis(root));
        Assert.Null(lost.Evidence);
        Assert.Equal($"Retained release basis {root} cannot resolve.", lost.Problem!.Message);
        Assert.Equal(new FindingDto("error", "release-basis.missing", $"basis-part:2@{parts[1]}", "The basis part is missing."), Assert.Single(lost.Problem.Findings));
        ToolCallResult unavailable = await new ToolRegistry(f.Api, null).CallAsync("release_basis_get", new() { ["hash"] = root }, TestContext.Current.CancellationToken);
        Assert.True(unavailable.IsError);
        Assert.Equal(JsonSerializer.Serialize(lost, ProtocolJson.Options), unavailable.Text);
    }

    [Fact]
    public void ARootWhosePartsJoinToNoSupportedBasisIsInvalidNotAbsent()
    {
        using var f = new ReleaseBasisFixture();
        byte[] other = System.Text.Encoding.UTF8.GetBytes("{\"format\":\"vericairn.release-basis/99\",\"padding\":\"" + new string('x', (int)EstateLayout.MaxObjectBytes) + "\"}");
        int first = (int)EstateLayout.MaxObjectBytes;
        string root = f.StoreRoot(other.Length, ContentHash.Sha256(other),
            [f.Api.Session.Objects.Put(other.AsSpan(0, first)), f.Api.Session.Objects.Put(other.AsSpan(first))]);
        ReleaseBasisResponse response = Assert.IsType<ReleaseBasisResponse>(f.Api.ReleaseBasis(root));
        Assert.Null(response.Evidence);
        AssertFailure(Assert.Single(response.Problem!.Findings), "invalid", "basis:" + root);
        string malformed = f.Api.Session.Objects.Put("{\"format\":\"vericairn.split-object/1\",\"parts\":[]}"u8);
        AssertFailure(Assert.Single(f.Api.ReleaseBasis(malformed)!.Problem!.Findings), "invalid", "basis:" + malformed);
    }

    // A root is untrusted: one claiming more than a release basis may hold, or repeating one part far past its length, is refused
    // before any part is measured or read, so parts that are not even held are never reported missing.
    [Fact]
    public void ARootPastTheBasisBoundOrRepeatingAPartIsRefusedBeforeAnyPartIsRead()
    {
        using var f = new ReleaseBasisFixture();
        long claimed = 300L * 1024 * 1024;
        string[] absent = [.. Enumerable.Range(0, (int)((claimed + EstateLayout.MaxObjectBytes - 1) / EstateLayout.MaxObjectBytes)).Select(i => ContentHash.Sha256(BitConverter.GetBytes(i)))];
        // With its count, such a root is larger than any root of a basis within the bound; with fewer parts it states too long a basis.
        string past = f.StoreRoot(claimed, ContentHash.Sha256("whole"u8), absent);
        FindingDto bound = Assert.Single(f.Api.ReleaseBasis(past)!.Problem!.Findings);
        Assert.Equal(("release-basis.invalid", "basis:" + past), (bound.Code, bound.Location));
        Assert.StartsWith($"The root is {f.Api.Session.Objects.Read(past)!.Length} bytes; the root of content up to 268435456 bytes is at most ", bound.Message, StringComparison.Ordinal);
        string stated = f.StoreRoot(claimed, ContentHash.Sha256("whole"u8), absent[..2]);
        FindingDto length = Assert.Single(f.Api.ReleaseBasis(stated)!.Problem!.Findings);
        Assert.Equal(("release-basis.invalid", "basis:" + stated, $"The root names {claimed} bytes, more than the 268435456 the reader accepts."), (length.Code, length.Location, length.Message));

        string part = f.Api.Session.Objects.Put(new byte[EstateLayout.MaxObjectBytes]);
        string repeated = f.StoreRoot(1, ContentHash.Sha256("x"u8), Enumerable.Repeat(part, 113_000));
        int size = f.Api.Session.Objects.Read(repeated)!.Length;
        Assert.True(size <= EstateLayout.MaxObjectBytes);
        FindingDto repeats = Assert.Single(f.Api.ReleaseBasis(repeated)!.Problem!.Findings);
        Assert.Equal(("release-basis.invalid", "basis:" + repeated), (repeats.Code, repeats.Location));
        Assert.StartsWith($"The root is {size} bytes; the root of content up to 268435456 bytes is at most ", repeats.Message, StringComparison.Ordinal);
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
