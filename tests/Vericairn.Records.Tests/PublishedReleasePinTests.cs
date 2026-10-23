// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Vericairn.Records.Authoring;
using Vericairn.Records.Canonical;
using Vericairn.Records.Estate;
using Vericairn.Records.Hashing;
using Vericairn.Records.Model;
using Vericairn.Records.Updates;

namespace Vericairn.Records.Tests;

public sealed class PublishedReleasePinTests
{
    [Theory]
    [InlineData("linux-x64")]
    [InlineData("linux-arm64")]
    [InlineData("osx-x64")]
    [InlineData("osx-arm64")]
    [InlineData("win-x64")]
    [InlineData("win-arm64")]
    public async Task FreshEstatePinsVerifiedPublishedBytesWithoutAProductRelease(string platform)
    {
        using var scratch = new TestEstate();
        using var fixture = new PublishedReleaseFixture();
        string root = Path.Combine(scratch.Root, "fresh");
        fixture.BeforeResponse = _ => Assert.False(Directory.Exists(root));
        VerifiedRelease verified = await fixture.VerifyAsync(platform);
        Assert.Equal(["release.json", "tag.json", "SHA256SUMS", ReleaseArchive.FileName(PublishedReleaseFixture.Version, platform), "tag.json"], fixture.Requests);
        Assert.Equal(ContentHash.Sha256(fixture.Files["SHA256SUMS"]), verified.ChecksumsHash);
        Assert.Equal(12, verified.Archives.Count);
        Assert.Contains(verified.Archives, a => a.Name == ReleaseArchive.WorkbenchFileName(PublishedReleaseFixture.Version, platform));
        EstateInitializer.Initialize(root, Initialization() with { Release = verified });
        EstateModel model = EstateLoader.Load(root);
        Assert.False(model.Findings.HasErrors, string.Join('\n', model.Findings));
        Assert.Equal(3, RecordFormats.Current);
        Assert.Equal(3, model.Records.Count);
        Assert.Empty(model.OfType("release"));
        Assert.Empty(model.OfType("product"));
        RecordEnvelope observed = Assert.Single(model.OfType("observation"));
        Assert.Equal("vericairn-release", EstateModel.BodyString(observed, "kind"));
        Assert.Equal(model.CurrentEstate!.Reference, observed.Subject);
        Assert.Equal(observed.Reference, model.Manifest!.Release);
        Assert.Equal("release", model.Manifest.Mode);
        Assert.Equal(6, model.Manifest.Artifacts!.Count);
        Assert.Equal(PublishedReleaseFixture.Version, model.Manifest.Version);
        Assert.Equal(verified.Build.Identity, observed.Body.GetProperty("facts").GetProperty("observer").GetString()![10..]);
        Assert.Equal("unsigned developer build", observed.Body.GetProperty("facts").GetProperty("signing").GetString());
        Assert.Equal(platform, observed.Body.GetProperty("facts").GetProperty("verifiedPlatform").GetString());
    }

    [Fact]
    public async Task OfflineDirectoryUsesTheSameChecksAndIsNeverChanged()
    {
        using var scratch = new TestEstate();
        using var fixture = new PublishedReleaseFixture();
        string directory = Path.Combine(scratch.Root, "download");
        fixture.WriteDirectory(directory);
        string[] before = Snapshot(directory);
        VerifiedRelease result = await PublishedReleaseVerifier.FromDirectoryAsync(directory, fixture.Build, "win-arm64", TestContext.Current.CancellationToken);
        Assert.Equal("win-arm64", result.Platform);
        Assert.Empty(fixture.Requests);
        Assert.Equal(before, Snapshot(directory));
        File.WriteAllText(Path.Combine(directory, ReleaseArchive.FileName(PublishedReleaseFixture.Version, "win-arm64")), "tampered");
        string[] tampered = Snapshot(directory);
        var refusal = await Assert.ThrowsAsync<InvalidOperationException>(() => PublishedReleaseVerifier.FromDirectoryAsync(directory, fixture.Build, "win-arm64", TestContext.Current.CancellationToken));
        Assert.Contains("differs from its SHA256SUMS", refusal.Message, StringComparison.Ordinal);
        Assert.Contains("Download", refusal.Message, StringComparison.Ordinal);
        Assert.Equal(tampered, Snapshot(directory));
    }

    [Theory]
    [InlineData("checksum")]
    [InlineData("archive")]
    [InlineData("missing-sums")]
    [InlineData("missing-platform")]
    [InlineData("duplicate-line")]
    [InlineData("draft")]
    [InlineData("prerelease")]
    [InlineData("release-commit")]
    [InlineData("tag-commit")]
    [InlineData("malformed")]
    public async Task OfflineRefusalsLeaveEveryInputAndEstateByteUnchanged(string fault)
    {
        using var scratch = new TestEstate();
        using var fixture = new PublishedReleaseFixture();
        fixture.Fault(fault, "linux-x64");
        string directory = Path.Combine(scratch.Root, "download");
        fixture.WriteDirectory(directory);
        string[] before = Snapshot(scratch.Root);
        var refusal = await Assert.ThrowsAsync<InvalidOperationException>(() => PublishedReleaseVerifier.FromDirectoryAsync(directory, fixture.Build, "linux-x64", TestContext.Current.CancellationToken));
        Assert.Contains("Nothing changed.", refusal.Message, StringComparison.Ordinal);
        Assert.Matches("(Download|Obtain|Choose|Investigate)", refusal.Message);
        Assert.Equal(before, Snapshot(scratch.Root));
    }

    [Fact]
    public async Task OfflineChecksumsAloneCannotInventReleaseOrTagEvidence()
    {
        using var scratch = new TestEstate();
        using var fixture = new PublishedReleaseFixture();
        string directory = Path.Combine(scratch.Root, "download");
        fixture.WriteDirectory(directory);
        File.Delete(Path.Combine(directory, "tag.json"));
        string[] before = Snapshot(directory);
        var refusal = await Assert.ThrowsAsync<InvalidOperationException>(() => PublishedReleaseVerifier.FromDirectoryAsync(directory, fixture.Build, "linux-x64", TestContext.Current.CancellationToken));
        Assert.Contains("Supply release.json, tag.json", refusal.Message, StringComparison.Ordinal);
        Assert.Equal(before, Snapshot(directory));
    }

    [Theory]
    [InlineData("9.2.0-dev+0123456789abcdef0123456789abcdef01234567")]
    [InlineData("9.2.0")]
    [InlineData("9.2.0+1234567")]
    [InlineData("9.2.0+0123456789abcdef0123456789abcdef01234567.dirty")]
    public void DevelopmentAndInexactBuildsCannotObserveReleaseFormats(string version)
    {
        var refusal = Assert.Throws<InvalidOperationException>(() => ReleaseBuild.Parse(version));
        Assert.Contains("Run the target release's command line", refusal.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task OldReleaseSourcesKeepTheirDefaultExplicitRefusal()
    {
        IReleaseSource source = new RecordedReleaseSource("v9.2.0");
        using var fixture = new PublishedReleaseFixture();
        var refusal = await Assert.ThrowsAsync<InvalidOperationException>(() => source.VerifyAsync(fixture.Build, "linux-x64", TestContext.Current.CancellationToken));
        Assert.Contains("--from <directory>", refusal.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("checksumsHash")]
    [InlineData("commit")]
    [InlineData("artifactHashes")]
    [InlineData("artifactPlatforms")]
    [InlineData("artifactNames")]
    [InlineData("reads")]
    [InlineData("writes")]
    [InlineData("observer")]
    [InlineData("repository")]
    [InlineData("url")]
    [InlineData("checksumsUrl")]
    [InlineData("verifiedPlatform")]
    public async Task ReadersRejectMalformedDependencyFacts(string field)
    {
        using var estate = new TestEstate();
        using var fixture = new PublishedReleaseFixture();
        VerifiedRelease release = await fixture.VerifyAsync();
        JsonObject body = JsonNode.Parse(release.ObservationBody().GetRawText())!.AsObject();
        body["facts"]!.AsObject().Remove(field);
        estate.Add("observation", estate.OperatorId, body.ToJsonString(), estate.Estate.Reference);
        Assert.True(estate.Load().Findings.HasErrors);
    }

    [Theory]
    [InlineData("hash")]
    [InlineData("id")]
    [InlineData("artifacts")]
    [InlineData("version")]
    [InlineData("recordFormat")]
    public async Task ManifestChecksTheObservationHashArtifactsAndFormats(string field)
    {
        using var scratch = new TestEstate();
        using var fixture = new PublishedReleaseFixture();
        VerifiedRelease release = await fixture.VerifyAsync();
        string root = Path.Combine(scratch.Root, "fresh");
        EstateInitializer.Initialize(root, Initialization() with { Release = release });
        string path = Path.Combine(root, EstateLayout.ManifestPath);
        JsonObject manifest = JsonNode.Parse(File.ReadAllBytes(path))!.AsObject();
        switch (field)
        {
            case "hash": manifest["vericairn"]!["release"]!["hash"] = "sha256:" + new string('b', 64); break;
            case "id": manifest["vericairn"]!["release"]!["id"] = scratch.Estate.Id; break;
            case "artifacts": manifest["vericairn"]!["artifacts"]!["linux-x64"] = "sha256:" + new string('b', 64); break;
            case "version": manifest["vericairn"]!["version"] = "9.2.1"; break;
            case "recordFormat": manifest["recordFormat"] = 2; break;
        }
        File.WriteAllBytes(path, CanonicalJson.SerializePrettyUtf8(JsonSerializer.SerializeToElement(manifest)));
        Assert.Contains(EstateLoader.Load(root).Findings, f => f.Code == "manifest.release" && f.Severity == Severity.Error);
    }

    [Fact]
    public async Task AnnotatedTagsArePeeledAndTheirIdentityIsRechecked()
    {
        using var fixture = new PublishedReleaseFixture();
        string annotation = new string('a', 40);
        fixture.Files["tag.json"] = JsonSerializer.SerializeToUtf8Bytes(new { @ref = "refs/tags/v9.2.0", @object = new { type = "tag", sha = annotation } });
        fixture.Files["tag-" + annotation + ".json"] = JsonSerializer.SerializeToUtf8Bytes(new { sha = annotation, @object = new { type = "commit", sha = PublishedReleaseFixture.Commit } });
        Assert.Equal(PublishedReleaseFixture.Commit, (await fixture.VerifyAsync()).Build.Commit);
        fixture.SecondTag = JsonSerializer.SerializeToUtf8Bytes(new { @ref = "refs/tags/v9.2.0", @object = new { type = "commit", sha = PublishedReleaseFixture.Commit } });
        var refusal = await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.VerifyAsync());
        Assert.Contains("tag moved", refusal.Message, StringComparison.Ordinal);
    }

    private static EstateInitialization Initialization() => new("Example", "example", "Depend on a published release.", "Operator", "Example", null,
        VerifiedRelease.Repository, PublishedReleaseFixture.Commit);
    private static string[] Snapshot(string root) => Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
        .Select(file => Path.GetRelativePath(root, file) + " " + ContentHash.Sha256File(file)).Order(StringComparer.Ordinal).ToArray();
}
