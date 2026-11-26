// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using Vericairn.Protocol;
using Vericairn.Records.Authoring;
using Vericairn.Records.Estate;
using Vericairn.Records.Hashing;
using Vericairn.Records.Tests;
using Vericairn.Runtime.Service;
using Vericairn.Runtime.Store;
using Vericairn.TestSupport;

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

    /// <summary>
    /// FIX-171 (0.9.5): a read-only runtime answers a record by id and the records of a type with their bytes, under both profiles; up to
    /// 0.9.4 its load kept none of the files it judged, and each answer failed parsing empty bytes.
    /// </summary>
    [Theory]
    [InlineData("repository-synced")]
    [InlineData("local-only")]
    public void AReadOnlyRuntimeAnswersRecordsWithTheBytesItJudged(string profile)
    {
        using var scratch = new TestEstate();
        string root = Path.Combine(scratch.Root, "estate");
        Directory.CreateDirectory(root);
        var initialization = new EstateInitialization("Read", "read", "Read records read-only.", "Operator", "Example", null,
            "Example/vericairn", new string('b', 40), new DateTimeOffset(2026, 10, 13, 16, 0, 0, TimeSpan.Zero), profile);
        PreparedEstate prepared = EstateInitializer.Prepare(root, initialization);
        string manifest = Path.Combine(root, EstateLayout.ManifestPath);
        Directory.CreateDirectory(Path.GetDirectoryName(manifest)!);
        File.WriteAllBytes(manifest, prepared.ManifestBytes);
        using (EstateSession writer = EstateSession.Open(root))
        {
            writer.Write(prepared.Operator);
            writer.Write(prepared.Estate);
        }

        using EstateService reader = EstateService.OpenReadOnly(root);
        RecordResponse? estate = reader.Record(prepared.Estate.Envelope.Id);
        Assert.NotNull(estate);
        Assert.Equal(prepared.Estate.Envelope.Id, estate.Envelope.GetProperty("id").GetString());
        Assert.Equal("Read records read-only.", estate.Envelope.GetProperty("body").GetProperty("purpose").GetString());
        RecordResponse principal = Assert.Single(reader.Records("principal"));
        Assert.Equal(prepared.Operator.Envelope.Id, principal.Envelope.GetProperty("id").GetString());
    }

    private static string[] Snapshot(string root) => Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
        .Select(file => Path.GetRelativePath(root, file) + " " + SharedFileHash.Sha256(file)).Order(StringComparer.Ordinal).ToArray();
}
