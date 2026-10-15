// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Vericairn.Protocol;
using Vericairn.Records.Estate;
using Vericairn.Records.Model;
using Vericairn.Records.Tests;
using Vericairn.Packs.Software;
using Vericairn.Runtime.Providers;
using Vericairn.Runtime.Release;
using Vericairn.Runtime.Service;
using Vericairn.Runtime.Upgrade;

namespace Vericairn.Runtime.Tests;

// The upgrade tests change the process-wide formats and migrations a test reads, so they run one at a time.
/// <summary>Adoption and release bodies carry no machine path; the release flow is intent then observed effect; a synthetic migration proves the upgrade path.</summary>
[Collection("upgrade")]
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

    private static readonly Dictionary<string, string> GitHub = new(StringComparer.Ordinal) { ["repository"] = "Example/widget", ["workflow"] = "release.yml", ["ref"] = "main", ["checksums"] = "SHA256SUMS" };

    [Fact]
    public void TheReleaseFlowIsIntentThenObservedEffect()
    {
        var provider = new FakeReleaseProvider();
        JsonObject intent = ReleaseFlow.IntentBody("github-releases", "Example/widget", GitHub, "0.2.0", "01PRODUCT");
        Assert.Equal("not-started", intent["status"]!.GetValue<string>());
        Assert.Equal("github-releases:Example/widget:v0.2.0", intent["identity"]!.GetValue<string>());
        Assert.Equal("Example/widget", intent["target"]!["repository"]!.GetValue<string>());
        Assert.Equal("release.yml", intent["target"]!["workflow"]!.GetValue<string>());
        Assert.Equal("01PRODUCT", intent["target"]!["product"]!.GetValue<string>());

        DispatchReceipt receipt = provider.Dispatch(new DispatchRequest("Example/widget", GitHub, new Dictionary<string, string>(StringComparer.Ordinal) { ["version"] = "0.2.0" }));
        JsonObject dispatched = ReleaseFlow.AfterDispatch(Element(intent), receipt);
        Assert.Equal("dispatched", dispatched["status"]!.GetValue<string>());
        Assert.Equal(receipt.RunId, dispatched["target"]!["run"]!.GetValue<string>());

        RunObservation running = provider.ObserveRun("Example/widget", receipt.RunId!)!;
        Assert.Throws<InvalidOperationException>(() => ReleaseFlow.AfterObservation(Element(dispatched), running, null, "0.2.0", ReleaseProfile.None, "SHA256SUMS"));

        provider.CompleteRun("Example/widget", receipt.RunId!, true, "0.2.0");
        RunObservation done = provider.ObserveRun("Example/widget", receipt.RunId!)!;
        ReleaseObservation draft = provider.ObserveRelease("Example/widget", "v0.2.0", GitHub)!;
        Assert.Empty(ReleaseProfile.None.Missing(draft, "0.2.0", "SHA256SUMS"));
        JsonObject observed = ReleaseFlow.AfterObservation(Element(dispatched), done, draft, "0.2.0", ReleaseProfile.None, "SHA256SUMS");
        Assert.Equal("observed-success", observed["status"]!.GetValue<string>());
        Assert.Equal(receipt.RunId, observed["observed"]!["reference"]!.GetValue<string>());

        // With nothing declared, the generic rule classifies Vericairn's own assets exactly as a declaration would.
        JsonObject candidate = ReleaseFlow.CandidateBody("0.2.0", "Example/widget", done.HeadSha, draft, ["run passed"], ["linux-x64"], "unsigned developer build", ["none signed"], ReleaseProfile.None, "SHA256SUMS");
        Assert.Equal("candidate", candidate["status"]!.GetValue<string>());
        Assert.Equal(14, candidate["artifacts"]!.AsArray().Count);
        Assert.Equal(12, candidate["artifacts"]!.AsArray().Count(a => a!["platform"]!.GetValue<string>() != "any"));
        Assert.Equal("osx-arm64", candidate["artifacts"]!.AsArray().Single(a => a!["name"]!.GetValue<string>() == "vericairn-workbench-0.2.0-osx-arm64.tar.gz")!["platform"]!.GetValue<string>());
        Assert.Equal("win-arm64", candidate["artifacts"]!.AsArray().Single(a => a!["name"]!.GetValue<string>() == "vericairn-0.2.0-win-arm64.zip")!["platform"]!.GetValue<string>());
        Assert.NotNull(candidate["sbom"]);
        Assert.NotNull(candidate["notices"]);
        Assert.Equal(RecordFormats.Current, candidate["recordFormat"]!["writes"]!.GetValue<long>());
        Assert.Throws<InvalidOperationException>(() => ReleaseFlow.PublishedBody(Element(candidate), draft));
        provider.Publish("Example/widget", "v0.2.0");
        JsonObject published = ReleaseFlow.PublishedBody(Element(candidate), provider.ObserveRelease("Example/widget", "v0.2.0", GitHub)!);
        Assert.Equal("published", published["status"]!.GetValue<string>());
        Assert.NotNull(published["publishedAt"]);

        // A failed run is a known failure with the run as reference; a refusal before dispatch is a known failure too.
        DispatchReceipt second = provider.Dispatch(new DispatchRequest("Example/widget", GitHub, new Dictionary<string, string>(StringComparer.Ordinal) { ["version"] = "0.3.0" }));
        provider.CompleteRun("Example/widget", second.RunId!, false, "0.3.0");
        JsonObject failed = ReleaseFlow.AfterObservation(Element(ReleaseFlow.AfterDispatch(Element(ReleaseFlow.IntentBody("github-releases", "Example/widget", GitHub, "0.3.0", "01PRODUCT")), second)), provider.ObserveRun("Example/widget", second.RunId!), null, "0.3.0", ReleaseProfile.None, "SHA256SUMS");
        Assert.Equal("known-failure", failed["status"]!.GetValue<string>());
        Assert.Equal("known-failure", ReleaseFlow.AfterRefusal(Element(intent), "not logged in")["status"]!.GetValue<string>());
        Assert.Equal("uncertain", ReleaseFlow.AfterUncertainty(Element(intent), "timed out")["status"]!.GetValue<string>());
        JsonObject retry = ReleaseFlow.Retry(Element(failed), second, "The failed run produced nothing.");
        Assert.Equal("dispatched", retry["status"]!.GetValue<string>());
        Assert.Null(retry["observed"]);
    }

    [Fact]
    public void ADeclaredProfileClassifiesAnotherProductsArtifactsAndSaysWhatIsMissing()
    {
        using JsonDocument product = JsonDocument.Parse("""
            {"name":"Gadget","release":{"destination":{"provider":"github-releases","settings":{"repository":"Example/gadget"}},"checksums":"CHECKSUMS.txt",
             "artifacts":[{"name":"gadget-{version}-{platform}.zip","role":"distributable","platforms":["linux-x64","win-x64"]},{"name":"gadget-{version}.spdx.json","role":"sbom"},{"name":"LICENSES-{version}.txt","role":"notices"}]}}
            """);
        ReleaseProfile profile = ReleaseProfile.FromProductBody(product.RootElement);
        Assert.True(profile.DeclaresArtifacts);
        Assert.Equal("github-releases", profile.Destination!.Provider);
        Assert.Equal("Example/gadget", profile.Destination.Settings["repository"]);
        Assert.Equal(["gadget-1.0.0-linux-x64.zip", "gadget-1.0.0-win-x64.zip", "gadget-1.0.0.spdx.json", "LICENSES-1.0.0.txt"], profile.Expected("1.0.0").Select(e => e.Name));
        Assert.Equal(new ExpectedArtifact("gadget-1.0.0-win-x64.zip", "distributable", "win-x64"), profile.Classify("gadget-1.0.0-win-x64.zip", "1.0.0"));
        Assert.Equal("notices", profile.Classify("LICENSES-1.0.0.txt", "1.0.0").Role);
        Assert.Equal(new ExpectedArtifact("extra-linux-arm64.tar.gz", "distributable", "linux-arm64"), profile.Classify("extra-linux-arm64.tar.gz", "1.0.0"));

        ReleaseObservation partial = new("v1.0.0", true, null, Commit, null, [new ReleaseAsset("gadget-1.0.0-linux-x64.zip", 10, "sha256:" + new string('1', 64)), new ReleaseAsset("gadget-1.0.0-win-x64.zip", 10, null), new ReleaseAsset("CHECKSUMS.txt", 1, null)], "2026-10-14T00:00:00Z");
        Assert.Equal(["gadget-1.0.0-win-x64.zip", "gadget-1.0.0.spdx.json", "LICENSES-1.0.0.txt"], profile.Missing(partial, "1.0.0", "CHECKSUMS.txt"));

        // The declared checksum file and the product's settings come before the provider's defaults; the request's own come last.
        var descriptor = new FakeReleaseProvider().Descriptor;
        IReadOnlyDictionary<string, string> settings = profile.Settings(descriptor, "github-releases", new Dictionary<string, string>(StringComparer.Ordinal) { ["ref"] = "release" });
        Assert.Equal("Example/gadget", settings["repository"]);
        Assert.Equal("CHECKSUMS.txt", settings["checksums"]);
        Assert.Equal("release.yml", settings["workflow"]);
        Assert.Equal("release", settings["ref"]);
        Assert.DoesNotContain("repository", ReleaseProfile.None.Settings(descriptor, "github-releases", null).Keys);
    }

    [Fact]
    public void TheCandidateCarriesTheRecordFormatsTheReleaseStatesInItsDocumentationSet()
    {
        using var estate = new TestEstate(recordFormat: 2);
        string op = estate.OperatorId;
        RecordEnvelope product = estate.Add("product", op, """
            {"name":"Widget","slug":"widget","summary":"A widget.","editions":[{"name":"Community","slug":"community","summary":"Free."}],"surfaces":[{"name":"CLI","slug":"cli","summary":"Command line."}],
             "release":{"destination":{"provider":"github-releases","settings":{"repository":"Example/widget"}},"artifacts":[{"name":"widget-{version}-{platform}.zip","role":"distributable","platforms":["linux-x64"]},{"name":"widget-{version}-docs.zip","role":"documentation"}]}}
            """);
        var provider = new FakeReleaseProvider();
        var registry = new ProviderRegistry();
        registry.Register(provider);
        using var service = EstateService.Open(estate.Root);
        service.Providers = registry;

        // The release's documentation set states formats other than this build's: the candidate records the release's, from that manifest.
        (JsonElement stated, string message) = Candidate(service, provider, product.Id, op, "0.9.0", DocumentationSet("0.9.0", ""","recordFormat":{"reads":[1,2,3],"writes":3}"""));
        Assert.Equal([1L, 2L, 3L], stated.GetProperty("recordFormat").GetProperty("reads").EnumerateArray().Select(f => f.GetInt64()));
        Assert.Equal(3, stated.GetProperty("recordFormat").GetProperty("writes").GetInt64());
        string evidence = stated.GetProperty("evidence").EnumerateArray().Last().GetProperty("summary").GetString()!;
        Assert.StartsWith("Record formats as the release states them in the manifest of its documentation set widget-0.9.0-docs.zip (sha256:", evidence, StringComparison.Ordinal);
        Assert.EndsWith("): reads 1, 2, 3; writes 3.", evidence, StringComparison.Ordinal);
        Assert.DoesNotContain("states no record formats", message, StringComparison.Ordinal);

        // A set from before the manifest stated them: the candidate carries this runtime's formats, and the evidence and the answer say so.
        (JsonElement older, string olderMessage) = Candidate(service, provider, product.Id, op, "0.9.1", DocumentationSet("0.9.1", string.Empty));
        Assert.Equal(RecordFormats.Reads, older.GetProperty("recordFormat").GetProperty("reads").EnumerateArray().Select(f => f.GetInt64()));
        Assert.Equal(RecordFormats.Current, older.GetProperty("recordFormat").GetProperty("writes").GetInt64());
        Assert.StartsWith("The draft states no record formats, since widget-0.9.1-docs.zip has a manifest that states none; recorded are those of the Vericairn ", older.GetProperty("evidence").EnumerateArray().Last().GetProperty("summary").GetString(), StringComparison.Ordinal);
        Assert.Contains("The draft states no record formats, so the candidate carries this runtime's (" + ReleaseRecordFormat.ThisBuild.Describe() + ")", olderMessage, StringComparison.Ordinal);
        Assert.Equal(0, service.Check().Errors);
    }

    [Fact]
    public void ADocumentationSetStatesRecordFormatsOnlyInAManifestOfItsVersion()
    {
        ReleaseRecordFormat? stated = ReleaseFlow.DocumentedRecordFormat(DocumentationSet("1.0.0", ""","recordFormat":{"reads":[2,1],"writes":2}"""), "1.0.0", out string missing);
        Assert.Equal([1L, 2L], stated!.Reads);
        Assert.Equal(2, stated.Writes);
        Assert.Empty(missing);
        Assert.NotNull(ReleaseFlow.DocumentedRecordFormat(Zip(("manifest.json", Manifest("1.0.0", ""","recordFormat":{"reads":[1],"writes":1}"""))), "1.0.0", out _));

        Assert.Null(ReleaseFlow.DocumentedRecordFormat(DocumentationSet("1.0.0", ""","recordFormat":{"reads":[1,2],"writes":2}"""), "1.0.1", out missing));
        Assert.Equal("holds no manifest.json of its set", missing);
        Assert.Null(ReleaseFlow.DocumentedRecordFormat(Zip(("1.0.0/manifest.json", Manifest("1.0.1", ""","recordFormat":{"reads":[1,2],"writes":2}"""))), "1.0.0", out missing));
        Assert.Equal("holds no vericairn.docs/1 manifest of version 1.0.0", missing);
        Assert.Null(ReleaseFlow.DocumentedRecordFormat(DocumentationSet("1.0.0", string.Empty), "1.0.0", out missing));
        Assert.Equal("has a manifest that states none", missing);
        foreach (string malformed in new[] { ""","recordFormat":{"reads":[1,2],"writes":3}""", ""","recordFormat":{"reads":[],"writes":1}""", ""","recordFormat":{"reads":[0,1],"writes":1}""", ""","recordFormat":{"reads":[1,1],"writes":1}""", ""","recordFormat":2""" })
        {
            Assert.Null(ReleaseFlow.DocumentedRecordFormat(DocumentationSet("1.0.0", malformed), "1.0.0", out missing));
            Assert.StartsWith("has a manifest whose recordFormat is malformed", missing, StringComparison.Ordinal);
        }

        Assert.Null(ReleaseFlow.DocumentedRecordFormat(Encoding.UTF8.GetBytes("not a zip"), "1.0.0", out missing));
        Assert.Equal("is not a zip archive holding a readable manifest", missing);
        Assert.Null(ReleaseFlow.DocumentedRecordFormat(Zip(("1.0.0/manifest.json", "{not json")), "1.0.0", out _));
    }

    [Theory]
    [InlineData("tool-1.0.0-linux-x64.tar.gz", "linux-x64", "distributable")]
    [InlineData("tool_1.0.0_osx-arm64.zip", "osx-arm64", "distributable")]
    [InlineData("tool-1.0.0-win-arm64", "win-arm64", "distributable")]
    [InlineData("tool-1.0.0-sbom.cdx.json", "any", "sbom")]
    [InlineData("tool.spdx.json", "any", "sbom")]
    [InlineData("THIRD-PARTY-NOTICES.md", "any", "notices")]
    [InlineData("tool-1.0.0-docs.zip", "any", "documentation")]
    [InlineData("readme.txt", "any", "other")]
    [InlineData("tool-linux-x64-extras.tar.gz", "linux-x64", "distributable")]
    [InlineData("tool-xlinux-x64.zip", "any", "other")]
    public void TheGenericRuleNeedsNoDeclaration(string name, string platform, string role)
    {
        ExpectedArtifact artifact = ReleaseProfile.Generic(name);
        Assert.Equal(platform, artifact.Platform);
        Assert.Equal(role, artifact.Role);
    }

    [Fact]
    public void ASyntheticMigrationProvesTheUpgradePathWithoutChangingTheRealFormat()
    {
        using var estate = new TestEstate();
        string op = estate.OperatorId;
        RecordEnvelope product = estate.Add("product", op, """{"name":"Example","slug":"example","summary":"A product.","editions":[{"name":"Community","slug":"community","summary":"Free."}],"surfaces":[{"name":"CLI","slug":"cli","summary":"Command line."}]}""");
        string artifacts = string.Join(',', ReleaseFlow.Platforms.Select((p, i) => $$"""{"name":"{{EstateUpgrade.DistributableName("0.9.0", p)}}","hash":"sha256:{{new string((char)('1' + i), 64)}}","bytes":10,"platform":"{{p}}"}"""));
        string candidateBody = $$"""{"version":"0.9.0","repository":"Example/example","commit":"{{Commit}}","status":"candidate","artifacts":[{{artifacts}}],"recordFormat":{"reads":[1,2,3],"writes":3},"evidence":[],"qualified":[],"signing":"unsigned developer build","limitations":[]}""";
        RecordEnvelope candidate = estate.Add("release", op, candidateBody, new RecordReference("product", product.Id, null));
        estate.Add("acceptance", op, """{"statement":"Accepted.","basis":[],"self":true}""", new RecordReference("release", candidate.Id, candidate.ComputedHash));
        estate.Add("release", op, candidateBody.Replace("\"status\":\"candidate\"", "\"status\":\"published\",\"publishedAt\":\"2026-10-13T13:00:00Z\",\"url\":\"https://example.invalid/releases/0.9.0\"", StringComparison.Ordinal), new RecordReference("product", product.Id, null), candidate.Id);
        Assert.DoesNotContain(estate.Load().Findings, f => f.Severity == Severity.Error);

        var synthetic = new SyntheticMigration(2, 3, fail: false);
        using (RecordFormats.Extend(3))
        using (EstateUpgrade.UseMigrations([.. EstateUpgrade.Migrations, synthetic]))
        {
            using (var service = EstateService.Open(estate.Root))
            {
                (UpgradeResponse? plan, Problem? problem) = service.Upgrade(new UpgradeRequest("0.9.0", null, true));
                Assert.Null(problem);
                Assert.Equal(["record-format-2", "synthetic-2-to-3"], plan!.Migrations);
                Assert.Equal(1, plan.FromFormat);
                Assert.Equal(3, plan.ToFormat);
                Assert.False(File.Exists(Path.Combine(estate.Root, "docs", "migrated.txt")));

                (UpgradeResponse? applied, Problem? failure) = service.Upgrade(new UpgradeRequest("0.9.0", op, false));
                Assert.Null(failure);
                Assert.True(applied!.Applied);
                Assert.NotNull(applied.Migration);
            }

            // The repository now says format 3, the records are still format 1 and still read, and the migration record names the steps and the staged file.
            Assert.Equal("synthetic\n", File.ReadAllText(Path.Combine(estate.Root, "docs", "migrated.txt")));
            using var upgraded = EstateService.Open(estate.Root);
            Assert.Equal(3, upgraded.Session.Manifest.RecordFormat);
            FindingsResponse check = upgraded.Check();
            Assert.Equal(0, check.Errors);
            Assert.Contains("record format 3", upgraded.Status().Text, StringComparison.Ordinal);
            RecordEnvelope migration = upgraded.Session.Load().Current("migration").Single();
            Assert.Contains("docs/migrated.txt written by the migrations", migration.Body.GetProperty("changes").EnumerateArray().Select(c => c.GetString()));
            Assert.Equal(1, synthetic.Applications);
        }

        // Beyond this process's knowledge, a format-3 manifest is refused rather than misread.
        InvalidOperationException refused = Assert.Throws<InvalidOperationException>(() => EstateService.Open(estate.Root));
        Assert.Contains("record format 3", refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TheMoveToFormat2RewritesNoRecordAndNewRecordsFollowItsRules()
    {
        using var estate = new TestEstate();
        string op = estate.OperatorId;
        estate.Add("decision", estate.BuilderId, $$"""{"title":"Before","statement":"Keep going.","summary":"Format 1.","synthesis":"A format-1 decision has no stake.","effects":["x"],"affects":[],"decidedBy":"{{op}}","decidedAt":"2026-10-13T12:00:00Z"}""");
        RecordEnvelope product = estate.Add("product", op, """{"name":"Example","slug":"example","summary":"A product.","editions":[{"name":"Community","slug":"community","summary":"Free."}],"surfaces":[{"name":"CLI","slug":"cli","summary":"Command line."}]}""");
        string artifacts = string.Join(',', ReleaseFlow.Platforms.Select((p, i) => $$"""{"name":"{{EstateUpgrade.DistributableName("0.9.0", p)}}","hash":"sha256:{{new string((char)('1' + i), 64)}}","bytes":10,"platform":"{{p}}"}"""));
        string candidateBody = $$"""{"version":"0.9.0","repository":"Example/example","commit":"{{Commit}}","status":"candidate","artifacts":[{{artifacts}}],"recordFormat":{"reads":[1,2],"writes":2},"evidence":[],"qualified":[],"signing":"unsigned developer build","limitations":[]}""";
        RecordEnvelope candidate = estate.Add("release", op, candidateBody, new RecordReference("product", product.Id, null));
        estate.Add("acceptance", op, """{"statement":"Accepted.","basis":[],"self":true}""", new RecordReference("release", candidate.Id, candidate.ComputedHash));
        estate.Add("release", op, candidateBody.Replace("\"status\":\"candidate\"", "\"status\":\"published\",\"publishedAt\":\"2026-10-13T13:00:00Z\",\"url\":\"https://example.invalid/releases/0.9.0\"", StringComparison.Ordinal), new RecordReference("product", product.Id, null), candidate.Id);
        string records = Path.Combine(estate.Root, "records");
        Dictionary<string, byte[]> before = Directory.EnumerateFiles(records, "*.json", SearchOption.AllDirectories).ToDictionary(f => f, File.ReadAllBytes);

        using (var service = EstateService.Open(estate.Root))
        {
            (UpgradeResponse? applied, Problem? problem) = service.Upgrade(new UpgradeRequest("0.9.0", op, false));
            Assert.Null(problem);
            Assert.Equal(["record-format-2"], applied!.Migrations);
            Assert.Equal(2, applied.ToFormat);

            // No record was rewritten: every file is byte for byte what it was, and the only new one is the migration record, in format 2.
            Dictionary<string, byte[]> after = Directory.EnumerateFiles(records, "*.json", SearchOption.AllDirectories).ToDictionary(f => f, File.ReadAllBytes);
            Assert.All(before, file => Assert.Equal(file.Value, after[file.Key]));
            string added = Assert.Single(after.Keys.Except(before.Keys));
            Assert.Contains("\"format\": \"vericairn.record/2\"", File.ReadAllText(added), StringComparison.Ordinal);
            EstateModel model = service.Session.Load();
            Assert.Equal(2, model.Manifest!.RecordFormat);
            Assert.DoesNotContain(model.Findings, f => f.Severity == Severity.Error);
            Assert.All(model.Records.Where(r => before.ContainsKey(Path.Combine(estate.Root, r.ExpectedPath.Replace('/', Path.DirectorySeparatorChar)))), r => Assert.Equal(1, r.Format));
            RecordEnvelope migration = model.Current("migration").Single();
            Assert.Equal(2, migration.Format);
            Assert.Contains(migration.Body.GetProperty("changes").EnumerateArray().Select(c => c.GetString()), c => c!.StartsWith("record-format-2: ", StringComparison.Ordinal));

            // A decision written now states format 2 and must say how much rides on it and what it does not authorize.
            WriteOutcome bare = service.NewRecord(new NewRecordRequest("decision", estate.BuilderId, Element(new JsonObject { ["title"] = "After", ["statement"] = "Go on.", ["summary"] = "Format 2.", ["synthesis"] = "A format-2 decision.", ["effects"] = new JsonArray("x"), ["affects"] = new JsonArray(), ["decidedBy"] = op, ["decidedAt"] = "2026-10-13T14:00:00Z" })));
            Assert.False(bare.Ok);
            Assert.Contains(bare.Problem!.Findings, f => f.Code == "body.missing" && f.Location.EndsWith("#body.stake", StringComparison.Ordinal));
            WriteOutcome staked = service.NewRecord(new NewRecordRequest("decision", estate.BuilderId, Element(new JsonObject { ["title"] = "After", ["statement"] = "Go on.", ["summary"] = "Format 2.", ["synthesis"] = "A format-2 decision.", ["effects"] = new JsonArray("x"), ["affects"] = new JsonArray(), ["decidedBy"] = op, ["decidedAt"] = "2026-10-13T14:00:00Z", ["stake"] = "low", ["notAuthorized"] = new JsonArray("publication") })));
            Assert.True(staked.Ok, staked.Problem?.Message);
            Assert.Equal(2, service.Session.Load().ById[staked.Written!.Id].Format);
        }
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

        using (EstateUpgrade.UseMigrations([new SyntheticMigration(1, 2, fail: true)]))
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

    /// <summary>Submits a version, completes its run with a distributable and the documentation set, observes it and returns the candidate's body and the answer.</summary>
    private static (JsonElement Body, string Message) Candidate(EstateService service, FakeReleaseProvider provider, string product, string by, string version, byte[] documentation)
    {
        ReleaseResponse submitted = service.ReleaseSubmit(new ReleaseSubmitRequest(product, version, null, by));
        Assert.True(submitted.Ok, submitted.Message);
        string docs = $"widget-{version}-docs.zip";
        provider.CompleteRun("Example/widget", submitted.Run!, true, version, [$"widget-{version}-linux-x64.zip", docs], contents: new Dictionary<string, byte[]>(StringComparer.Ordinal) { [docs] = documentation });
        ReleaseResponse observed = service.ReleaseObserve(new ReleaseObserveRequest(version, null, by, ["linux-x64"], [], Product: product));
        Assert.True(observed.Candidate is not null, observed.Message);
        return (service.Record(observed.Candidate!.Id)!.Envelope.GetProperty("body").Clone(), observed.Message);
    }

    /// <summary>A documentation set's archive as the release workflow packs it: the set's directory, named by its version, with its manifest.</summary>
    private static byte[] DocumentationSet(string version, string extra) =>
        Zip(($"{version}/README.md", $"# Widget {version}\n"), ($"{version}/manifest.json", Manifest(version, extra)));

    private static string Manifest(string version, string extra) =>
        $$"""{"format":"vericairn.docs/1","version":"{{version}}","commit":"{{Commit}}","documents":["README.md"]{{extra}}}""";

    private static byte[] Zip(params (string Name, string Text)[] entries)
    {
        using var stream = new MemoryStream();
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach ((string name, string text) in entries)
            {
                using Stream entry = zip.CreateEntry(name).Open();
                entry.Write(Encoding.UTF8.GetBytes(text));
            }
        }

        return stream.ToArray();
    }

    /// <summary>A migration between two formats, a pretend one beyond what this release writes among them, that stages one file or fails on purpose.</summary>
    private sealed class SyntheticMigration : IRecordFormatMigration
    {
        private readonly bool fail;

        public SyntheticMigration(long from, long to, bool fail)
        {
            FromFormat = from;
            ToFormat = to;
            this.fail = fail;
        }

        public int Applications { get; private set; }

        public string Name => $"synthetic-{FromFormat}-to-{ToFormat}";

        public long FromFormat { get; }

        public long ToFormat { get; }

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
