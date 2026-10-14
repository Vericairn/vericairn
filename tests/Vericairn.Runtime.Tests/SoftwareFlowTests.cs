// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using System.Text.Json.Nodes;
using Vericairn.Protocol;
using Vericairn.Records.Estate;
using Vericairn.Records.Model;
using Vericairn.Records.Tests;
using Vericairn.Runtime.Providers;
using Vericairn.Runtime.Service;
using Vericairn.Runtime.Software;
using Vericairn.Runtime.Upgrade;

namespace Vericairn.Runtime.Tests;

/// <summary>Adoption and release bodies carry no machine path; the release flow is intent then observed effect; a synthetic migration proves the upgrade path.</summary>
public class SoftwareFlowTests
{
    private const string Commit = "0123456789abcdef0123456789abcdef01234567";
    private const string Root = "fedcba9876543210fedcba9876543210fedcba98";

    [Theory]
    [InlineData("https://github.com/Example/widget.git", "Example/widget")]
    [InlineData("https://github.com/Example/widget", "Example/widget")]
    [InlineData("git@github.com:Example/widget.git", "Example/widget")]
    [InlineData("ssh://git@github.com/Example/widget.git", "Example/widget")]
    [InlineData("https://gitlab.example/Example/widget.git", null)]
    [InlineData("/Users/someone/repos/widget", null)]
    [InlineData(null, null)]
    public void GitHubRemotesAreRecognized(string? remote, string? expected) => Assert.Equal(expected, SourceAdoption.GitHubRepository(remote));

    [Fact]
    public void AdoptionIdentifiesARepositoryByRemoteOrRootCommitAndNeverByPath()
    {
        var local = new RepositoryObservation(Commit, "main", "/Users/someone/repos/widget", Root, false, 2, 1, [], [new RecentCommit(Commit, "2026-10-13T12:00:00Z", "Add")], "2026-10-13T12:00:01Z");
        JsonObject resource = SourceAdoption.ResourceBody("Widget", "source", "private", local);
        Assert.Equal(Root, resource["location"]!["rootCommit"]!.GetValue<string>());
        Assert.Null(resource["location"]!["github"]);
        Assert.Null(resource["location"]!["url"]);
        JsonObject observation = SourceAdoption.ObservationBody(local);
        Assert.DoesNotContain("/Users/", observation.ToJsonString(), StringComparison.Ordinal);
        Assert.Contains("not recorded", observation.ToJsonString(), StringComparison.Ordinal);
        Assert.Equal(2, observation["facts"]!["modified"]!.GetValue<int>());

        var hosted = local with { Remote = "git@github.com:Example/widget.git" };
        Assert.Equal("Example/widget", SourceAdoption.ResourceBody("Widget", "source", "public", hosted)["location"]!["github"]!.GetValue<string>());
        Assert.Equal("git@github.com:Example/widget.git", SourceAdoption.ObservationBody(hosted)["facts"]!["remote"]!.GetValue<string>());
    }

    [Fact]
    public void TheReleaseFlowIsIntentThenObservedEffect()
    {
        var provider = new FakeReleaseProvider();
        JsonObject intent = ReleaseFlow.IntentBody("Example/widget", "release.yml", "main", "0.2.0", "01PRODUCT");
        Assert.Equal("not-started", intent["status"]!.GetValue<string>());
        Assert.Equal("github-releases:Example/widget:v0.2.0", intent["identity"]!.GetValue<string>());

        DispatchReceipt receipt = provider.Dispatch(new DispatchRequest("Example/widget", "release.yml", "main", new Dictionary<string, string>(StringComparer.Ordinal) { ["version"] = "0.2.0" }));
        JsonObject dispatched = ReleaseFlow.AfterDispatch(Element(intent), receipt);
        Assert.Equal("dispatched", dispatched["status"]!.GetValue<string>());
        Assert.Equal(receipt.RunId, dispatched["target"]!["run"]!.GetValue<string>());

        RunObservation running = provider.ObserveRun("Example/widget", receipt.RunId!)!;
        Assert.Throws<InvalidOperationException>(() => ReleaseFlow.AfterObservation(Element(dispatched), running, null, "0.2.0"));

        provider.CompleteRun("Example/widget", receipt.RunId!, true, "0.2.0");
        RunObservation done = provider.ObserveRun("Example/widget", receipt.RunId!)!;
        ReleaseObservation draft = provider.ObserveRelease("Example/widget", "v0.2.0")!;
        Assert.Empty(ReleaseFlow.MissingDistributables(draft, "0.2.0"));
        JsonObject observed = ReleaseFlow.AfterObservation(Element(dispatched), done, draft, "0.2.0");
        Assert.Equal("observed-success", observed["status"]!.GetValue<string>());
        Assert.Equal(receipt.RunId, observed["observed"]!["reference"]!.GetValue<string>());

        JsonObject candidate = ReleaseFlow.CandidateBody("0.2.0", "Example/widget", done.HeadSha, draft, ["run passed"], ["linux-x64"], "unsigned developer build", ["none signed"]);
        Assert.Equal("candidate", candidate["status"]!.GetValue<string>());
        Assert.Equal(14, candidate["artifacts"]!.AsArray().Count);
        Assert.Equal(12, candidate["artifacts"]!.AsArray().Count(a => a!["platform"]!.GetValue<string>() != "any"));
        Assert.Equal("osx-arm64", candidate["artifacts"]!.AsArray().Single(a => a!["name"]!.GetValue<string>() == "vericairn-workbench-0.2.0-osx-arm64.tar.gz")!["platform"]!.GetValue<string>());
        Assert.NotNull(candidate["sbom"]);
        Assert.NotNull(candidate["notices"]);
        Assert.Equal(RecordFormats.Current, candidate["recordFormat"]!["writes"]!.GetValue<long>());
        Assert.Throws<InvalidOperationException>(() => ReleaseFlow.PublishedBody(Element(candidate), draft));
        provider.Publish("Example/widget", "v0.2.0");
        JsonObject published = ReleaseFlow.PublishedBody(Element(candidate), provider.ObserveRelease("Example/widget", "v0.2.0")!);
        Assert.Equal("published", published["status"]!.GetValue<string>());
        Assert.NotNull(published["publishedAt"]);

        // A failed run is a known failure with the run as reference; a refusal before dispatch is a known failure too.
        DispatchReceipt second = provider.Dispatch(new DispatchRequest("Example/widget", "release.yml", "main", new Dictionary<string, string>(StringComparer.Ordinal) { ["version"] = "0.3.0" }));
        provider.CompleteRun("Example/widget", second.RunId!, false, "0.3.0");
        JsonObject failed = ReleaseFlow.AfterObservation(Element(ReleaseFlow.AfterDispatch(Element(ReleaseFlow.IntentBody("Example/widget", "release.yml", "main", "0.3.0", "01PRODUCT")), second)), provider.ObserveRun("Example/widget", second.RunId!), null, "0.3.0");
        Assert.Equal("known-failure", failed["status"]!.GetValue<string>());
        Assert.Equal("known-failure", ReleaseFlow.AfterRefusal(Element(intent), "not logged in")["status"]!.GetValue<string>());
        Assert.Equal("uncertain", ReleaseFlow.AfterUncertainty(Element(intent), "timed out")["status"]!.GetValue<string>());
        JsonObject retry = ReleaseFlow.Retry(Element(failed), second, "The failed run produced nothing.");
        Assert.Equal("dispatched", retry["status"]!.GetValue<string>());
        Assert.Null(retry["observed"]);
    }

    [Fact]
    public void ASyntheticMigrationProvesTheUpgradePathWithoutChangingTheRealFormat()
    {
        using var estate = new TestEstate();
        string op = estate.OperatorId;
        RecordEnvelope product = estate.Add("product", op, """{"name":"Example","slug":"example","summary":"A product.","editions":[{"name":"Community","slug":"community","summary":"Free."}],"surfaces":[{"name":"CLI","slug":"cli","summary":"Command line."}]}""");
        string artifacts = string.Join(',', ReleaseFlow.Platforms.Select((p, i) => $$"""{"name":"{{EstateUpgrade.DistributableName("0.9.0", p)}}","hash":"sha256:{{new string((char)('1' + i), 64)}}","bytes":10,"platform":"{{p}}"}"""));
        string candidateBody = $$"""{"version":"0.9.0","repository":"Example/example","commit":"{{Commit}}","status":"candidate","artifacts":[{{artifacts}}],"recordFormat":{"reads":[1,2],"writes":2},"evidence":[],"qualified":[],"signing":"unsigned developer build","limitations":[]}""";
        RecordEnvelope candidate = estate.Add("release", op, candidateBody, new RecordReference("product", product.Id, null));
        estate.Add("acceptance", op, """{"statement":"Accepted.","basis":[],"self":true}""", new RecordReference("release", candidate.Id, candidate.ComputedHash));
        estate.Add("release", op, candidateBody.Replace("\"status\":\"candidate\"", "\"status\":\"published\",\"publishedAt\":\"2026-10-13T13:00:00Z\",\"url\":\"https://example.invalid/releases/0.9.0\"", StringComparison.Ordinal), new RecordReference("product", product.Id, null), candidate.Id);
        Assert.DoesNotContain(estate.Load().Findings, f => f.Severity == Severity.Error);

        var synthetic = new SyntheticMigration(fail: false);
        using (RecordFormats.Extend(2))
        using (EstateUpgrade.UseMigrations([synthetic]))
        {
            using (var service = EstateService.Open(estate.Root))
            {
                (UpgradeResponse? plan, Problem? problem) = service.Upgrade(new UpgradeRequest("0.9.0", null, true));
                Assert.Null(problem);
                Assert.Equal(["synthetic-1-to-2"], plan!.Migrations);
                Assert.Equal(1, plan.FromFormat);
                Assert.Equal(2, plan.ToFormat);
                Assert.False(File.Exists(Path.Combine(estate.Root, "docs", "migrated.txt")));

                (UpgradeResponse? applied, Problem? failure) = service.Upgrade(new UpgradeRequest("0.9.0", op, false));
                Assert.Null(failure);
                Assert.True(applied!.Applied);
                Assert.NotNull(applied.Migration);
            }

            // The repository now says format 2, the records are still format 1 and still read, and the migration record names the step and the staged file.
            Assert.Equal("synthetic\n", File.ReadAllText(Path.Combine(estate.Root, "docs", "migrated.txt")));
            using var upgraded = EstateService.Open(estate.Root);
            Assert.Equal(2, upgraded.Session.Manifest.RecordFormat);
            FindingsResponse check = upgraded.Check();
            Assert.Equal(0, check.Errors);
            Assert.Contains("record format 2", upgraded.Status().Text, StringComparison.Ordinal);
            RecordEnvelope migration = upgraded.Session.Load().Current("migration").Single();
            Assert.Contains("docs/migrated.txt written by the migrations", migration.Body.GetProperty("changes").EnumerateArray().Select(c => c.GetString()));
            Assert.Equal(1, synthetic.Applications);
        }

        // Beyond this process's knowledge, a format-2 manifest is refused rather than misread.
        Assert.Throws<InvalidOperationException>(() => EstateService.Open(estate.Root));
    }

    [Fact]
    public void AFailedMigrationLeavesTheRepositoryUntouched()
    {
        using var estate = new TestEstate();
        string op = estate.OperatorId;
        RecordEnvelope product = estate.Add("product", op, """{"name":"Example","slug":"example","summary":"A product.","editions":[{"name":"Community","slug":"community","summary":"Free."}],"surfaces":[{"name":"CLI","slug":"cli","summary":"Command line."}]}""");
        string artifacts = string.Join(',', ReleaseFlow.Platforms.Select((p, i) => $$"""{"name":"{{EstateUpgrade.DistributableName("0.9.0", p)}}","hash":"sha256:{{new string((char)('1' + i), 64)}}","bytes":10,"platform":"{{p}}"}"""));
        string candidateBody = $$"""{"version":"0.9.0","repository":"Example/example","commit":"{{Commit}}","status":"candidate","artifacts":[{{artifacts}}],"recordFormat":{"reads":[1,2],"writes":2},"evidence":[],"qualified":[],"signing":"unsigned developer build","limitations":[]}""";
        RecordEnvelope candidate = estate.Add("release", op, candidateBody, new RecordReference("product", product.Id, null));
        estate.Add("acceptance", op, """{"statement":"Accepted.","basis":[],"self":true}""", new RecordReference("release", candidate.Id, candidate.ComputedHash));
        estate.Add("release", op, candidateBody.Replace("\"status\":\"candidate\"", "\"status\":\"published\",\"publishedAt\":\"2026-10-13T13:00:00Z\",\"url\":\"https://example.invalid/releases/0.9.0\"", StringComparison.Ordinal), new RecordReference("product", product.Id, null), candidate.Id);
        byte[] manifestBefore = File.ReadAllBytes(Path.Combine(estate.Root, "estate", "manifest.json"));

        using (RecordFormats.Extend(2))
        using (EstateUpgrade.UseMigrations([new SyntheticMigration(fail: true)]))
        using (var service = EstateService.Open(estate.Root))
        {
            (UpgradeResponse? applied, Problem? problem) = service.Upgrade(new UpgradeRequest("0.9.0", op, false));
            Assert.Null(applied);
            Assert.Contains("nothing changed", problem!.Message, StringComparison.Ordinal);
            Assert.Equal(manifestBefore, File.ReadAllBytes(Path.Combine(estate.Root, "estate", "manifest.json")));
            Assert.False(File.Exists(Path.Combine(estate.Root, "docs", "migrated.txt")));
            Assert.Empty(service.Session.Load().OfType("migration"));
        }
    }

    private static JsonElement Element(JsonObject body)
    {
        using JsonDocument document = JsonDocument.Parse(body.ToJsonString());
        return document.RootElement.Clone();
    }

    /// <summary>A migration from format 1 to a pretend format 2 that stages one file, or fails on purpose.</summary>
    private sealed class SyntheticMigration : IRecordFormatMigration
    {
        private readonly bool fail;

        public SyntheticMigration(bool fail)
        {
            this.fail = fail;
        }

        public int Applications { get; private set; }

        public string Name => "synthetic-1-to-2";

        public long FromFormat => 1;

        public long ToFormat => 2;

        public void Apply(MigrationContext context)
        {
            Applications++;
            if (fail)
            {
                throw new InvalidOperationException("synthetic failure");
            }

            context.Stage("docs/migrated.txt", "synthetic\n"u8.ToArray());
        }
    }
}
