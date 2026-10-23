// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Vericairn.Protocol;
using Vericairn.Providers.GitHub.Releases;
using Vericairn.Records.Authoring;
using Vericairn.Records.Canonical;
using Vericairn.Records.Estate;
using Vericairn.Records.Model;
using Vericairn.Records.Tests;
using Vericairn.Records.Updates;
using Vericairn.Runtime.Providers;
using Vericairn.Runtime.Service;
using Vericairn.Runtime.Store;
using Vericairn.Runtime.Upgrade;

namespace Vericairn.Runtime.Tests;

[Collection("upgrade")]
public sealed class PublishedDependencyUpgradeTests
{
    [Fact]
    public async Task AReleasePinMovesToANewerVerifiedObservationAndRetainsBoth()
    {
        using var scratch = new TestEstate();
        using var previous = new PublishedReleaseFixture("9.1.0");
        VerifiedRelease oldRelease = await previous.VerifyAsync();
        string root = Path.Combine(scratch.Root, "dependent");
        var initialized = EstateInitializer.Initialize(root, new EstateInitialization("Example", "example", "Use a published dependency.", "Operator", "Example", null,
            VerifiedRelease.Repository, PublishedReleaseFixture.Commit) { Release = oldRelease });
        string[] original = Snapshot(Path.Combine(root, "records"));
        using var fixture = new PublishedReleaseFixture();
        using var client = new HttpClient(fixture);
        using EstateService service = EstateService.OpenForUpgrade(root);
        service.DependencyBuild = fixture.Build;
        service.DependencyPlatform = "linux-x64";
        service.Providers = new() { VericairnReleases = new GitHubReleaseSource(client) };
        var (plan, problem) = service.Upgrade(new(PublishedReleaseFixture.Version, initialized.Operator.Id, false));
        Assert.Null(problem);
        Assert.True(plan!.Applied);
        Assert.Equal("release 9.1.0", plan.FromPin);
        Assert.Equal("release 9.2.0", plan.ToPin);
        Assert.Empty(plan.Migrations);
        EstateModel model = EstateLoader.Load(root);
        Assert.False(model.Findings.HasErrors, string.Join('\n', model.Findings));
        Assert.Equal(2, model.OfType("observation").Count());
        Assert.Empty(model.OfType("release"));
        Assert.Equal(plan.Release, model.Manifest!.Release!.Id);
        Assert.All(original, line => Assert.Contains(line, Snapshot(Path.Combine(root, "records"))));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TheEstatesOwnProductWithTheSameVersionDoesNotStandInForVericairn(bool published)
    {
        using var estate = new TestEstate(3);
        RecordEnvelope product = estate.Add("product", estate.OperatorId,
            """{"name":"Example","slug":"example","summary":"The estate's own product.","editions":[{"name":"Community","slug":"community","summary":"Free."}],"surfaces":[{"name":"CLI","slug":"cli","summary":"Command line."}]}""");
        var body = new JsonObject
        {
            ["version"] = PublishedReleaseFixture.Version, ["repository"] = "Example/product", ["commit"] = PublishedReleaseFixture.Commit,
            ["status"] = "candidate", ["artifacts"] = new JsonArray(new JsonObject { ["name"] = "example.zip", ["platform"] = "linux-x64", ["hash"] = "sha256:" + new string('a', 64), ["bytes"] = 10 }),
            ["recordFormat"] = new JsonObject { ["reads"] = new JsonArray(1, 2, 3), ["writes"] = 3 }, ["evidence"] = new JsonArray(),
            ["qualified"] = new JsonArray(), ["signing"] = "unsigned developer build", ["limitations"] = new JsonArray(),
        };
        RecordEnvelope candidate = estate.Add("release", estate.OperatorId, body.ToJsonString(), product.Reference);
        if (published)
        {
            estate.Add("acceptance", estate.OperatorId, """{"statement":"Accepted the example product.","basis":[],"self":true}""", candidate.Reference);
            body["status"] = "published";
            body["publishedAt"] = RecordParser.FormatTimestamp(estate.Clock);
            body["url"] = "https://example.invalid/releases/current";
            estate.Add("release", estate.OperatorId, body.ToJsonString(), product.Reference, candidate.Id);
        }
        using var fixture = new PublishedReleaseFixture();
        using var client = new HttpClient(fixture);
        using EstateService service = Open(estate, fixture, client);
        var (plan, problem) = service.Upgrade(new(PublishedReleaseFixture.Version, estate.OperatorId, false));
        Assert.Null(problem);
        Assert.True(plan!.Applied);
        EstateModel model = estate.Load();
        Assert.Equal("Example/product", EstateModel.BodyString(Assert.Single(model.Current("release")), "repository"));
        Assert.Equal(Assert.Single(model.OfType("observation")).Reference, model.Manifest!.Release);
        Assert.Equal(VerifiedRelease.Repository, model.Manifest.Repository);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void SourcePinUpgradesAcrossEveryKnownFormatAndKeepsHistoricalBytes(long fromFormat)
    {
        using var estate = new TestEstate(fromFormat);
        using var fixture = new PublishedReleaseFixture();
        using var client = new HttpClient(fixture);
        using EstateService service = Open(estate, fixture, client);
        string[] historical = Snapshot(Path.Combine(estate.Root, "records"));
        string[] before = Snapshot(estate.Root);
        fixture.BeforeResponse = _ => Assert.Equal(before, Snapshot(estate.Root));
        var (check, checkProblem) = service.Upgrade(new(PublishedReleaseFixture.Version, null, true));
        Assert.Null(checkProblem);
        Assert.False(check!.Applied);
        Assert.Equal(fromFormat, check.FromFormat);
        Assert.Contains(PublishedReleaseFixture.Commit, check.FromPin, StringComparison.Ordinal);
        Assert.Equal(3, check.ToFormat);
        Assert.Equal(3 - fromFormat, check.Migrations.Count);
        Assert.Equal(before, Snapshot(estate.Root));
        var (applied, problem) = service.Upgrade(new(PublishedReleaseFixture.Version, estate.OperatorId, false));
        Assert.Null(problem);
        Assert.True(applied!.Applied);
        EstateModel model = estate.Load();
        Assert.False(model.Findings.HasErrors, string.Join('\n', model.Findings));
        Assert.Equal("release", model.Manifest!.Mode);
        Assert.Equal(PublishedReleaseFixture.Version, model.Manifest.Version);
        Assert.Equal(3, model.Manifest.RecordFormat);
        Assert.Empty(model.OfType("release"));
        Assert.Empty(model.OfType("product"));
        RecordEnvelope observation = Assert.Single(model.OfType("observation"));
        RecordEnvelope migration = Assert.Single(model.OfType("migration"));
        Assert.Equal(fromFormat, observation.Format);
        Assert.Equal(3, migration.Format);
        Assert.Equal(observation.Reference, model.Manifest.Release);
        Assert.Equal(observation.Id, EstateUpgrade.Plan(model, PublishedReleaseFixture.Version).Release.Id);
        Assert.Equal(observation.Reference, migration.Subject);
        Assert.Equal(observation.Id, EstateModel.BodyString(migration, "release"));
        Assert.Equal(check.FromPin, EstateModel.BodyString(migration, "fromPin"));
        Assert.Equal(check.ToPin, EstateModel.BodyString(migration, "toPin"));
        Assert.All(historical, line => Assert.Contains(line, Snapshot(Path.Combine(estate.Root, "records"))));
        fixture.BeforeResponse = null;
        string[] once = Snapshot(estate.Root);
        var (again, repeated) = service.Upgrade(new(PublishedReleaseFixture.Version, estate.OperatorId, false));
        Assert.Null(repeated);
        Assert.False(again!.Applied);
        Assert.Equal(once, Snapshot(estate.Root));
    }

    [Theory]
    [InlineData("checksum", "Download")]
    [InlineData("archive", "Download")]
    [InlineData("missing-sums", "Obtain")]
    [InlineData("missing-platform", "Download")]
    [InlineData("duplicate-line", "Download")]
    [InlineData("draft", "Choose")]
    [InlineData("prerelease", "Choose")]
    [InlineData("release-commit", "Obtain")]
    [InlineData("tag-commit", "Investigate")]
    [InlineData("moved-tag", "Investigate")]
    [InlineData("offline", "--from <directory>")]
    [InlineData("malformed", "Download")]
    public void EveryPublicationRefusalWritesNothingAndNamesItsNextAction(string fault, string next)
    {
        using var estate = new TestEstate(2);
        using var fixture = new PublishedReleaseFixture();
        fixture.Fault(fault, "linux-x64");
        using var client = new HttpClient(fixture);
        using EstateService service = Open(estate, fixture, client);
        string[] before = Snapshot(estate.Root);
        string live = Path.GetDirectoryName(service.Session.Live.Path)!;
        string[] state = Snapshot(live);
        foreach (bool check in new[] { true, false })
        {
            var (plan, problem) = service.Upgrade(new(PublishedReleaseFixture.Version, estate.OperatorId, check));
            Assert.Null(plan);
            Assert.NotNull(problem);
            Assert.Contains(next, problem.Message, StringComparison.Ordinal);
            Assert.Contains("Nothing changed.", problem.Message, StringComparison.Ordinal);
            Assert.Equal(before, Snapshot(estate.Root));
            Assert.Equal(state, Snapshot(live));
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void IncompatibleTargetFormatsRefuseBeforeAnyObservationOrMigration(bool olderWriter)
    {
        using var estate = new TestEstate(3);
        using var fixture = new PublishedReleaseFixture();
        fixture.Build = olderWriter ? fixture.Build with { Writes = 2 } : fixture.Build with { Reads = [2, 3] };
        using var client = new HttpClient(fixture);
        using EstateService service = Open(estate, fixture, client);
        string[] before = Snapshot(estate.Root);
        var (plan, problem) = service.Upgrade(new(PublishedReleaseFixture.Version, estate.OperatorId, false));
        Assert.Null(plan);
        Assert.Contains(olderWriter ? "writes record format 2, older" : "does not read record format 1", problem!.Message, StringComparison.Ordinal);
        Assert.Contains("Choose a release", problem.Message, StringComparison.Ordinal);
        Assert.Equal(before, Snapshot(estate.Root));
    }

    [Fact]
    public void OfflineUpgradeNeverConsultsTheRegisteredNetworkSource()
    {
        using var estate = new TestEstate(2);
        using var fixture = new PublishedReleaseFixture();
        string directory = Path.Combine(estate.Root, "download");
        fixture.WriteDirectory(directory);
        fixture.Offline = true;
        using var client = new HttpClient(fixture);
        using EstateService service = Open(estate, fixture, client);
        var (plan, problem) = service.Upgrade(new(PublishedReleaseFixture.Version, estate.OperatorId, false) { From = directory });
        Assert.Null(problem);
        Assert.True(plan!.Applied);
        Assert.Empty(fixture.Requests);
        Assert.DoesNotContain(directory, Assert.Single(estate.Load().OfType("observation")).Body.GetRawText(), StringComparison.Ordinal);
    }

    [Fact]
    public void CheckOnFirstOpenCreatesNoPersistentState()
    {
        using var estate = new TestEstate(2);
        using var fixture = new PublishedReleaseFixture();
        using var client = new HttpClient(fixture);
        string live = Path.GetDirectoryName(EstateSession.LiveStorePath(estate.Root, estate.Estate.Id, "repository-synced"))!;
        string[] before = Snapshot(estate.Root);
        Assert.False(Directory.Exists(live));
        using (EstateService service = Open(estate, fixture, client, readOnly: true))
        {
            var (plan, problem) = service.Upgrade(new(PublishedReleaseFixture.Version, null, true));
            Assert.Null(problem);
            Assert.NotNull(plan);
        }
        Assert.False(Directory.Exists(live));
        Assert.Equal(before, Snapshot(estate.Root));
    }

    [Theory]
    [InlineData("checksum")]
    [InlineData("archive")]
    [InlineData("draft")]
    [InlineData("prerelease")]
    [InlineData("moved-tag")]
    [InlineData("missing-sums")]
    [InlineData("offline")]
    public void ApplyRefusalOnFirstOpenCreatesNoLiveCache(string fault)
    {
        using var estate = new TestEstate(2);
        using var fixture = new PublishedReleaseFixture();
        fixture.Fault(fault, "linux-x64");
        using var client = new HttpClient(fixture);
        string live = Path.GetDirectoryName(EstateSession.LiveStorePath(estate.Root, estate.Estate.Id, "repository-synced"))!;
        string[] before = Snapshot(estate.Root);
        using (EstateService service = EstateService.OpenForUpgrade(estate.Root))
        {
            service.DependencyBuild = fixture.Build;
            service.DependencyPlatform = "linux-x64";
            service.Providers = new() { VericairnReleases = new GitHubReleaseSource(client) };
            var (plan, problem) = service.Upgrade(new(PublishedReleaseFixture.Version, estate.OperatorId, false));
            Assert.Null(plan);
            Assert.NotNull(problem);
        }
        Assert.False(Directory.Exists(live));
        Assert.Equal(before, Snapshot(estate.Root));
    }

    [Fact]
    public void FirstApplyCreatesStorageOnlyAfterTheArchiveAndTagVerify()
    {
        using var estate = new TestEstate(2);
        using var fixture = new PublishedReleaseFixture();
        using var client = new HttpClient(fixture);
        string live = Path.GetDirectoryName(EstateSession.LiveStorePath(estate.Root, estate.Estate.Id, "repository-synced"))!;
        string[] before = Snapshot(estate.Root);
        fixture.BeforeResponse = _ => { Assert.False(Directory.Exists(live)); Assert.Equal(before, Snapshot(estate.Root)); };
        using EstateService service = EstateService.OpenForUpgrade(estate.Root);
        service.DependencyBuild = fixture.Build;
        service.DependencyPlatform = "linux-x64";
        service.Providers = new() { VericairnReleases = new GitHubReleaseSource(client) };
        var (plan, problem) = service.Upgrade(new(PublishedReleaseFixture.Version, estate.OperatorId, false));
        Assert.Null(problem);
        Assert.True(plan!.Applied);
        Assert.True(Directory.Exists(live));
        Assert.False(estate.Load().Findings.HasErrors);
    }

    [Fact]
    public void WrongClientOrTargetBuildRefusesBeforeAnyReleaseRequest()
    {
        using var estate = new TestEstate(2);
        using var fixture = new PublishedReleaseFixture();
        using var client = new HttpClient(fixture);
        using EstateService service = Open(estate, fixture, client);
        string[] before = Snapshot(estate.Root);
        foreach (UpgradeRequest request in new UpgradeRequest[]
        {
            new("9.3.0", estate.OperatorId, false),
            new(PublishedReleaseFixture.Version, estate.OperatorId, false) { ClientBuild = "9.1.0+" + PublishedReleaseFixture.Commit },
        })
        {
            Assert.Contains("restart any serving daemon", service.Upgrade(request).Problem!.Message, StringComparison.Ordinal);
            Assert.Equal(before, Snapshot(estate.Root));
        }
        Assert.Empty(fixture.Requests);
    }

    [Fact]
    public void FailedRecordJudgmentDoesNotLeaveAnObservationBehind()
    {
        using var estate = new TestEstate(2);
        using var fixture = new PublishedReleaseFixture();
        using var client = new HttpClient(fixture);
        using EstateService service = Open(estate, fixture, client);
        string[] before = Snapshot(estate.Root);
        var (plan, problem) = service.Upgrade(new(PublishedReleaseFixture.Version, estate.BuilderId, false));
        Assert.Null(plan);
        Assert.NotNull(problem);
        Assert.Contains(problem.Findings, f => f.Code == "migration.author");
        Assert.Equal(before, Snapshot(estate.Root));
        Assert.Empty(estate.Load().OfType("observation"));
    }

    [Fact]
    public void FailedMigrationLeavesNoObservationOrStagedFile()
    {
        using var estate = new TestEstate(2);
        using var fixture = new PublishedReleaseFixture();
        using var client = new HttpClient(fixture);
        using EstateService service = Open(estate, fixture, client);
        using var migrations = EstateUpgrade.UseMigrations([new FailedMigration()]);
        string[] before = Snapshot(estate.Root);
        var (plan, problem) = service.Upgrade(new(PublishedReleaseFixture.Version, estate.OperatorId, false));
        Assert.Null(plan);
        Assert.Contains("nothing changed", problem!.Message, StringComparison.Ordinal);
        Assert.Equal(before, Snapshot(estate.Root));
    }

    [Fact]
    public void InterruptedManifestMoveResumesTheSameObservationAndMigration()
    {
        using var estate = new TestEstate(2);
        using var fixture = new PublishedReleaseFixture();
        using var client = new HttpClient(fixture);
        using (EstateService service = Open(estate, fixture, client))
        {
            EstateService.UpgradeStage = stage => { if (stage == "recorded") throw new StoppedUpgrade(); };
            try { Assert.Throws<StoppedUpgrade>(() => service.Upgrade(new(PublishedReleaseFixture.Version, estate.OperatorId, false))); }
            finally { EstateService.UpgradeStage = null; }
        }
        EstateModel interrupted = estate.Load();
        RecordEnvelope observation = Assert.Single(interrupted.OfType("observation"));
        RecordEnvelope migration = Assert.Single(interrupted.OfType("migration"));
        Assert.Contains(interrupted.Findings, f => f.Code == "upgrade.incomplete");
        Assert.False(interrupted.Findings.HasErrors, string.Join('\n', interrupted.Findings));
        using (EstateService service = Open(estate, fixture, client))
        {
            var (applied, problem) = service.Upgrade(new(PublishedReleaseFixture.Version, estate.OperatorId, false));
            Assert.Null(problem);
            Assert.True(applied!.Applied);
            Assert.Equal(migration.Id, applied.Migration!.Id);
        }
        EstateModel moved = estate.Load();
        Assert.Equal(observation.Reference, moved.Manifest!.Release);
        Assert.Single(moved.OfType("observation"));
        Assert.Single(moved.OfType("migration"));
        Assert.False(moved.Findings.HasErrors, string.Join('\n', moved.Findings));
    }

    [Fact]
    public async Task LocalOnlyCheckAndRefusalLeaveTheCanonicalDatabaseUnchanged()
    {
        using var scratch = new TestEstate();
        using var fixture = new PublishedReleaseFixture();
        VerifiedRelease release = await fixture.VerifyAsync();
        string root = Path.Combine(scratch.Root, "local");
        var initialization = new EstateInitialization("Local", "local", "A dependency pin.", "Operator", "Example", null,
            VerifiedRelease.Repository, PublishedReleaseFixture.Commit, Profile: "local-only") { Release = release };
        PreparedEstate prepared = EstateInitializer.Prepare(root, initialization);
        Directory.CreateDirectory(Path.Combine(root, "estate"));
        File.WriteAllBytes(Path.Combine(root, EstateLayout.ManifestPath), prepared.ManifestBytes);
        using EstateSession writer = EstateSession.Open(root);
        writer.Write(prepared.Operator);
        writer.Write(prepared.Estate);
        writer.Write(prepared.Release!);
        string live = Path.GetDirectoryName(writer.Live.Path)!;
        string[] state = Snapshot(live), before = Snapshot(root);
        using var client = new HttpClient(fixture);
        using EstateService service = EstateService.OpenReadOnly(root);
        service.DependencyBuild = fixture.Build;
        service.DependencyPlatform = "linux-x64";
        service.Providers = new() { VericairnReleases = new GitHubReleaseSource(client) };
        Assert.Null(service.Upgrade(new(PublishedReleaseFixture.Version, null, true)).Problem);
        fixture.Fault("archive", "linux-x64");
        Assert.NotNull(service.Upgrade(new(PublishedReleaseFixture.Version, null, true)).Problem);
        Assert.Equal(state, Snapshot(live));
        Assert.Equal(before, Snapshot(root));
    }

    private static EstateService Open(TestEstate estate, PublishedReleaseFixture fixture, HttpClient client, bool readOnly = false)
    {
        EstateService service = readOnly ? EstateService.OpenReadOnly(estate.Root) : EstateService.Open(estate.Root);
        service.Providers = new ProviderRegistry { VericairnReleases = new GitHubReleaseSource(client) };
        service.DependencyBuild = fixture.Build;
        service.DependencyPlatform = "linux-x64";
        return service;
    }

    private static string[] Snapshot(string root) => Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
        .Select(file => Path.GetRelativePath(root, file) + " " + SharedFileHash.Sha256(file)).Order(StringComparer.Ordinal).ToArray();
    private sealed class StoppedUpgrade : Exception;
    private sealed class FailedMigration : IRecordFormatMigration
    {
        public string Name => "recorded-failed-step";
        public long FromFormat => 2;
        public long ToFormat => 3;
        public void Apply(MigrationContext context)
        {
            context.Stage("docs/staged.txt", Encoding.UTF8.GetBytes("never written"));
            throw new InvalidOperationException("The recorded migration failed; correct it before retrying.");
        }
    }
}
