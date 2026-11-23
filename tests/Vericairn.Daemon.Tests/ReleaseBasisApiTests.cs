// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using Vericairn.Protocol;
using Vericairn.Records.Hashing;
using Vericairn.Runtime.Tests;
using Vericairn.Sdk;

namespace Vericairn.Daemon.Tests;

[Collection("daemon")]
public sealed class ReleaseBasisApiTests
{
    public ReleaseBasisApiTests() => DaemonTestHome.Ensure();

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UnknownAndUnsupportedHashesReturnNotFound(bool unsupported)
    {
        using var f = new ReleaseBasisFixture();
        string hash = unsupported ? f.Api.Session.Objects.Put("{\"format\":\"vericairn.release-basis/99\"}"u8) : ContentHash.Sha256("unknown"u8);
        await using DaemonHost host = await DaemonTestHome.StartAsync(f.Records.Estate.Root, cancellationToken: TestContext.Current.CancellationToken);
        using var client = new VericairnClient(DaemonHost.Discover(f.Records.Estate.Root)!);
        Assert.Null(client.ReleaseBasis(hash));
        Assert.Null(await client.ReleaseBasisAsync(hash, TestContext.Current.CancellationToken));
    }

    // FIX-163: a basis retained as a split object reads through the daemon under its root's hash as its one object does.
    [Fact]
    public async Task ABasisRetainedInPartsReadsThroughTheDaemonAsItsOneObject()
    {
        using var f = new ReleaseBasisFixture();
        ReleaseBasisResponse single = Assert.IsType<ReleaseBasisResponse>(f.Api.ReleaseBasis(f.Hash));
        (string root, _, IReadOnlyList<string> documents) = f.StoreInParts();
        ReleaseBasisResponse expected = Assert.IsType<ReleaseBasisResponse>(f.Api.ReleaseBasis(root));
        Assert.NotNull(expected.Evidence);
        Assert.Equal(JsonSerializer.Serialize(single with { Hash = root, Evidence = single.Evidence! with { Documents = documents } }, ProtocolJson.Options),
            JsonSerializer.Serialize(expected, ProtocolJson.Options));
        await using DaemonHost host = await DaemonTestHome.StartAsync(f.Records.Estate.Root, cancellationToken: TestContext.Current.CancellationToken);
        using var client = new VericairnClient(DaemonHost.Discover(f.Records.Estate.Root)!);
        string json = JsonSerializer.Serialize(expected, ProtocolJson.Options);
        Assert.Equal(json, JsonSerializer.Serialize(client.ReleaseBasis(root), ProtocolJson.Options));
        Assert.Equal(json, JsonSerializer.Serialize(await client.ReleaseBasisAsync(root, TestContext.Current.CancellationToken), ProtocolJson.Options));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UnresolvedBasesRetainEveryFindingThroughTheDaemonAndSdk(bool corrupted)
    {
        using var f = new ReleaseBasisFixture();
        f.LoseObject(f.SourceHash, corrupted);
        f.LoseObject(f.ProofHash, !corrupted);
        ReleaseBasisResponse expected = Assert.IsType<ReleaseBasisResponse>(f.Api.ReleaseBasis(f.Hash));
        Assert.Null(expected.Evidence);
        Assert.Equal(2, expected.Problem!.Findings.Count);
        await using DaemonHost host = await DaemonTestHome.StartAsync(f.Records.Estate.Root, cancellationToken: TestContext.Current.CancellationToken);
        using var client = new VericairnClient(DaemonHost.Discover(f.Records.Estate.Root)!);
        string json = JsonSerializer.Serialize(expected, ProtocolJson.Options);
        Assert.Equal(json, JsonSerializer.Serialize(client.ReleaseBasis(f.Hash), ProtocolJson.Options));
        Assert.Equal(json, JsonSerializer.Serialize(await client.ReleaseBasisAsync(f.Hash, TestContext.Current.CancellationToken), ProtocolJson.Options));
        Assert.Equal(json, JsonSerializer.Serialize(new CancellationBoundApi(client, TestContext.Current.CancellationToken).ReleaseBasis(f.Hash), ProtocolJson.Options));
    }
}
