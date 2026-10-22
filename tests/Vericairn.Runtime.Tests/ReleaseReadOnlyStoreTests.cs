// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using Vericairn.Protocol;
using Vericairn.Records.Authoring;
using Vericairn.Records.Estate;
using Vericairn.Records.Hashing;
using Vericairn.Records.Tests;
using Vericairn.Runtime.Service;
using Vericairn.Runtime.Store;

namespace Vericairn.Runtime.Tests;

[Collection("home")]
public sealed class ReleaseReadOnlyStoreTests
{
    [Fact]
    public void RepositoryCheckCreatesNoLiveDirectoryEvenOnTheFirstOpen()
    {
        using var estate = new TestEstate();
        string live = Path.GetDirectoryName(EstateSession.LiveStorePath(estate.Root, estate.Estate.Id, "repository-synced"))!;
        Assert.False(Directory.Exists(live));
        string[] before = Snapshot(estate.Root);
        using (EstateService reader = EstateService.OpenReadOnly(estate.Root))
        {
            ReleaseResponse response = reader.ReleaseCheck(new(estate.Estate.Id, "1.0.0", null, estate.OperatorId));
            Assert.False(response.Ok);
            Assert.NotEmpty(response.Check!.Objections);
        }
        Assert.False(Directory.Exists(live));
        Assert.Equal(before, Snapshot(estate.Root));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LocalOnlyCheckLeavesCanonicalDatabasesAndObjectsByteForByteUnchanged(bool committedWal)
    {
        using var scratch = new TestEstate();
        string root = Path.Combine(scratch.Root, "local");
        Directory.CreateDirectory(root);
        var initialization = new EstateInitialization("Local", "local", "Check local records.", "Operator", "Example", null,
            "Example/vericairn", new string('b', 40), new DateTimeOffset(2026, 10, 13, 16, 0, 0, TimeSpan.Zero), "local-only");
        PreparedEstate prepared = EstateInitializer.Prepare(root, initialization);
        string manifest = Path.Combine(root, EstateLayout.ManifestPath);
        Directory.CreateDirectory(Path.GetDirectoryName(manifest)!);
        File.WriteAllBytes(manifest, prepared.ManifestBytes);
        using EstateSession writer = EstateSession.Open(root);
        writer.Write(prepared.Operator);
        writer.Write(prepared.Estate);
        string hash = writer.Objects.Put("kept evidence"u8);
        string live = Path.GetDirectoryName(writer.Live.Path)!;
        if (!committedWal) writer.Dispose();
        string[] before = Snapshot(scratch.Root), state = Snapshot(live);
        using (EstateService reader = EstateService.OpenReadOnly(root))
        {
            ReleaseResponse response = reader.ReleaseCheck(new(prepared.Estate.Envelope.Id, "1.0.0", null, prepared.Operator.Envelope.Id));
            Assert.False(response.Ok);
            Assert.NotEmpty(response.Check!.Objections);
            Assert.Equal(2, reader.Session.Read().Records.Count);
            Assert.Single(reader.Session.Objects.List());
            Assert.Equal("kept evidence"u8.ToArray(), reader.Session.Objects.Read(hash));
        }
        Assert.Equal(before, Snapshot(scratch.Root));
        Assert.Equal(state, Snapshot(live));
    }

    private static string[] Snapshot(string root) => Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
        .Select(file => Path.GetRelativePath(root, file) + " " + ContentHash.Sha256File(file)).Order(StringComparer.Ordinal).ToArray();
}
