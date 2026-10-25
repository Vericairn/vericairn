// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.CommandLine;
using System.Text.Json;
using System.Text.Json.Nodes;
using Vericairn.Protocol;
using Vericairn.Records.Estate;
using Vericairn.Records.Hashing;
using Vericairn.Records.Model;
using Vericairn.Records.Tests;
using Vericairn.Records.Updates;
using Vericairn.Runtime.Providers;
using Vericairn.Runtime.Service;

namespace Vericairn.Cli.Tests;

[Collection("cli")]
public sealed class PublishedDependencyCommandsTests
{
    [Theory]
    [InlineData("repository-synced", false, true)]
    [InlineData("repository-synced", true, true)]
    [InlineData("local-only", false, true)]
    [InlineData("local-only", true, true)]
    [InlineData("repository-synced", false, false)]
    [InlineData("repository-synced", true, false)]
    [InlineData("local-only", false, false)]
    [InlineData("local-only", true, false)]
    public void InitReleaseUsesRecordedResponsesOrOfflineFilesAndPinsTheObservation(string profile, bool offline, bool json)
    {
        string root = CliHarness.TempDirectory("published-init"), download = CliHarness.TempDirectory("published-download");
        using var fixture = new PublishedReleaseFixture();
        using var client = new HttpClient(fixture);
        IReleaseSource? previous = ProviderRegistry.Current.VericairnReleases;
        ProviderRegistry.Current.VericairnReleases = new Providers.GitHub.Releases.GitHubReleaseSource(client);
        try
        {
            fixture.BeforeResponse = _ => Assert.Empty(Directory.EnumerateFileSystemEntries(root));
            fixture.WriteDirectory(download);
            CliHarness.Outcome result = Run(fixture.Build.Identity, null,
                [.. InitArgs(root, json), "--release", "--profile", profile, .. offline ? new[] { "--from", download } : []]);
            Assert.Equal(0, result.Code);
            Assert.Empty(result.Err);
            EstateModel model = CommandContext.LoadCopy(root);
            Assert.False(model.Findings.HasErrors, string.Join('\n', model.Findings));
            Assert.Equal("release", model.Manifest!.Mode);
            Assert.Equal(PublishedReleaseFixture.Version, model.Manifest.Version);
            Assert.Equal(profile, model.Manifest.Profile);
            Assert.Equal(3, model.Records.Count);
            RecordEnvelopeAssertion(model);
            Assert.Equal(offline ? 0 : 5, fixture.Requests.Count);
            Assert.Equal(profile == "repository-synced", Directory.Exists(Path.Combine(root, "records")));
            EstateBootstrapTests.AssertInstalled(root);
            Assert.Empty(EstateChecks.Run(model));

            RecordEnvelope operatorRecord = Assert.Single(model.OfType("principal"));
            RecordEnvelope estateRecord = Assert.Single(model.OfType("estate"));
            RecordEnvelope observation = Assert.Single(model.OfType("observation"));
            if (json)
            {
                JsonElement output = result.Json;
                Assert.Equal(["operator", "estate", "manifest", "release", "bootstrap"], output.EnumerateObject().Select(p => p.Name));
                Assert.Equal(operatorRecord.Id, output.GetProperty("operator").GetString());
                Assert.Equal(estateRecord.Id, output.GetProperty("estate").GetString());
                Assert.Equal(EstateLayout.ManifestPath, output.GetProperty("manifest").GetString());
                JsonElement receipt = output.GetProperty("release");
                Assert.Equal(["path", "id", "hash"], receipt.EnumerateObject().Select(p => p.Name));
                Assert.Equal(observation.ExpectedPath, receipt.GetProperty("path").GetString());
                Assert.Equal(observation.Id, receipt.GetProperty("id").GetString());
                Assert.Equal(observation.ComputedHash, receipt.GetProperty("hash").GetString());
                Assert.Equal(EstateBootstrapTests.WrittenJson, output.GetProperty("bootstrap").GetRawText());
            }
            else
            {
                Assert.Equal(
                    $"operator  {operatorRecord.Id}  {operatorRecord.ExpectedPath}{Environment.NewLine}"
                    + $"estate    {estateRecord.Id}  {estateRecord.ExpectedPath}{Environment.NewLine}"
                    + $"manifest  {EstateLayout.ManifestPath}{Environment.NewLine}"
                    + $"release   {observation.Id}  {observation.ExpectedPath}  {observation.ComputedHash}{Environment.NewLine}"
                    + $"written   bootstrap{Environment.NewLine}written   bootstrap.ps1{Environment.NewLine}written   .gitattributes{Environment.NewLine}", result.Out);
            }
        }
        finally { ProviderRegistry.Current.VericairnReleases = previous; CliHarness.Delete(root, download); }
    }

    [Theory]
    [InlineData("repository-synced")]
    [InlineData("local-only")]
    public void RequiringSignaturesAfterReleaseInitKeepsTheCheckCleanAcrossCharterRevisions(string profile)
    {
        string root = CliHarness.TempDirectory("published-charter"), scratch = CliHarness.TempDirectory("published-charter-inputs");
        using var fixture = new PublishedReleaseFixture();
        try
        {
            fixture.WriteDirectory(scratch);
            CliHarness.Outcome initialized = Run(fixture.Build.Identity, null,
                [.. InitArgs(root), "--release", "--from", scratch, "--profile", profile]);
            Assert.True(initialized.Code == 0, initialized.Out + initialized.Err);
            EstateModel model = CommandContext.LoadCopy(root);
            RecordEnvelope original = model.CurrentEstate!;
            RecordEnvelope release = Assert.Single(model.OfType("observation"));
            byte[] manifest = File.ReadAllBytes(Path.Combine(root, EstateLayout.ManifestPath));
            Dictionary<string, string> records = EstateLoader.EnumerateRecordFiles(root).ToDictionary(file => file.Path, file => ContentHash.Sha256(file.Bytes));
            string device = CliHarness.Ok("--estate", root, "--json", "devices", "create", "--principal", original.By,
                "--name", "Operator device", "--custody", "file").Id;
            CliHarness.Ok("--estate", root, "--json", "devices", "admit", device, "--by", original.By, "--note", "The first authority.");

            RecordEnvelope current = original;
            for (int revision = 1; revision <= 2; revision++)
            {
                JsonObject body = JsonNode.Parse(current.Body.GetRawText())!.AsObject();
                body["policies"]!["signatures"] = "required";
                body["purpose"] = $"Signed charter revision {revision}.";
                string charter = CliHarness.WriteJson(scratch, "charter.json", body.ToJsonString());
                CliHarness.Outcome revised = CliHarness.Ok("--estate", root, "--json", "records", "new", "estate",
                    "--by", original.By, "--body", charter, "--supersedes", current.Id);
                CliHarness.Outcome check = CliHarness.Ok("--estate", root, "estate", "check");
                Assert.Empty(check.Err);
                Assert.Contains($"{5 + revision} records: 0 errors, 0 warnings", check.Out, StringComparison.Ordinal);
                Assert.DoesNotContain("record.subject-stale", check.Out, StringComparison.Ordinal);

                model = CommandContext.LoadCopy(root);
                current = model.CurrentEstate!;
                Assert.Equal(revised.Id, current.Id);
                Assert.Single(current.Signatures);
                Assert.Equal(release.Reference, model.Manifest!.Release);
                Assert.Equal(original.Reference, model.ById[release.Id].Subject);
                Assert.Equal(manifest, File.ReadAllBytes(Path.Combine(root, EstateLayout.ManifestPath)));
                Assert.All(records, pair => Assert.Equal(pair.Value, ContentHash.Sha256File(Path.Combine(root, pair.Key))));
                Assert.All(model.Records, record => Assert.Equal(3, record.Format));
            }

            string observationBody = CliHarness.WriteJson(scratch, "observation.json",
                """{"kind":"repository","asOf":"2026-10-13T12:00:00Z","summary":"Observed the original charter.","facts":{},"limitations":[]}""");
            CliHarness.Outcome observed = CliHarness.Ok("--estate", root, "--json", "records", "new", "observation",
                "--by", original.By, "--body", observationBody, "--subject", $"estate:{original.Id}:{original.ComputedHash}");
            CliHarness.Outcome stale = CliHarness.Ok("--estate", root, "--json", "estate", "check");
            Assert.Empty(stale.Err);
            Assert.Equal(0, stale.Json.GetProperty("errors").GetInt32());
            Assert.Equal(1, stale.Json.GetProperty("warnings").GetInt32());
            JsonElement finding = Assert.Single(stale.Json.GetProperty("findings").EnumerateArray());
            Assert.Equal("record.subject-stale", finding.GetProperty("Code").GetString());
            Assert.Equal("warning", finding.GetProperty("severity").GetString());
            Assert.Equal(observed.Json.GetProperty("path").GetString(), finding.GetProperty("Location").GetString());
            Assert.Equal(8, CommandContext.LoadCopy(root).Records.Count);
        }
        finally { CliHarness.Delete(root, scratch); }
    }

    [Theory]
    [InlineData("checksum")]
    [InlineData("archive")]
    [InlineData("moved-tag")]
    [InlineData("draft")]
    [InlineData("prerelease")]
    [InlineData("missing-sums")]
    [InlineData("offline")]
    public void InitReleaseRefusesBeforeCreatingTheEstate(string fault)
    {
        string root = CliHarness.TempDirectory("published-refusal");
        using var fixture = new PublishedReleaseFixture();
        fixture.Fault(fault, ReleaseBuild.Platform);
        using var client = new HttpClient(fixture);
        IReleaseSource? previous = ProviderRegistry.Current.VericairnReleases;
        ProviderRegistry.Current.VericairnReleases = new Providers.GitHub.Releases.GitHubReleaseSource(client);
        try
        {
            CliHarness.Outcome result = Run(fixture.Build.Identity, null, [.. InitArgs(root), "--release"]);
            Assert.Equal(2, result.Code);
            Assert.Contains("Nothing changed.", result.Err, StringComparison.Ordinal);
            Assert.Matches("(Download|Obtain|Choose|Investigate|--from)", result.Err);
            Assert.Empty(Directory.EnumerateFileSystemEntries(root));
        }
        finally { ProviderRegistry.Current.VericairnReleases = previous; CliHarness.Delete(root); }
    }

    [Fact]
    public void UpgradeOptionsReachTheRuntimeForCheckAndApply()
    {
        string download = CliHarness.TempDirectory("published-upgrade");
        using var estate = new TestEstate(2);
        using var fixture = new PublishedReleaseFixture();
        fixture.WriteDirectory(download);
        using var client = new HttpClient(fixture);
        using EstateService service = EstateService.Open(estate.Root);
        service.DependencyBuild = fixture.Build;
        service.DependencyPlatform = ReleaseBuild.Platform;
        service.Providers = new() { VericairnReleases = new Providers.GitHub.Releases.GitHubReleaseSource(client) };
        try
        {
            string[] before = Snapshot(estate.Root);
            string[] args = ["--estate", estate.Root, "--json", "estate", "upgrade", "--to", PublishedReleaseFixture.Version, "--from", download];
            CliHarness.Outcome check = Run(fixture.Build.Identity, service, [.. args, "--check"]);
            Assert.Equal(0, check.Code);
            Assert.False(check.Json.GetProperty("applied").GetBoolean());
            Assert.Equal(before, Snapshot(estate.Root));
            CliHarness.Outcome applied = Run(fixture.Build.Identity, service, [.. args, "--by", estate.OperatorId]);
            Assert.Equal(0, applied.Code);
            Assert.True(applied.Json.GetProperty("applied").GetBoolean());
            RecordEnvelopeAssertion(estate.Load());
            Assert.Single(estate.Load().OfType("migration"));
            Assert.Empty(fixture.Requests);
        }
        finally { CliHarness.Delete(download); }
    }

    [Fact]
    public void SourceInitStaysOfflineAndRejectsAnUnpairedFromOption()
    {
        string root = CliHarness.TempDirectory("source-offline");
        using var fixture = new PublishedReleaseFixture { Offline = true };
        using var client = new HttpClient(fixture);
        IReleaseSource? previous = ProviderRegistry.Current.VericairnReleases;
        ProviderRegistry.Current.VericairnReleases = new Providers.GitHub.Releases.GitHubReleaseSource(client);
        try
        {
            CliHarness.Outcome invalid = Run(fixture.Build.Identity, null, [.. InitArgs(root), "--from", "download"]);
            Assert.Equal(2, invalid.Code);
            Assert.Contains("--from requires --release", invalid.Err, StringComparison.Ordinal);
            Assert.Empty(Directory.EnumerateFileSystemEntries(root));
            CliHarness.Outcome source = Run(fixture.Build.Identity, null, InitArgs(root));
            Assert.Equal(0, source.Code);
            EstateModel model = CommandContext.LoadCopy(root);
            Assert.Equal("source", model.Manifest!.Mode);
            Assert.Equal(PublishedReleaseFixture.Commit, model.Manifest.Commit);
            Assert.Equal(2, model.Records.Count);
            Assert.Empty(fixture.Requests);
        }
        finally { ProviderRegistry.Current.VericairnReleases = previous; CliHarness.Delete(root); }
    }

    private static void RecordEnvelopeAssertion(EstateModel model)
    {
        Assert.Empty(model.OfType("release"));
        Assert.Empty(model.OfType("product"));
        Assert.Equal(Assert.Single(model.OfType("observation")).Reference, model.Manifest!.Release);
    }

    private static string[] InitArgs(string root, bool json = true) => ["--estate", root, .. json ? new[] { "--json" } : [], "estate", "init", "--name", "Example", "--slug", "example",
        "--purpose", "Use a published dependency.", "--operator-name", "Operator", "--organization", "Example"];

    private static CliHarness.Outcome Run(string version, IEstateApi? api, string[] arguments)
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        var context = new CommandContext(output, error) { BorrowedApi = api };
        var tree = new VericairnCommand(context);
        var estate = new Command("estate");
        estate.Subcommands.Add(new EstateInitCommand(context, version));
        estate.Subcommands.Add(new EstateUpgradeCommand(context, version));
        tree.Subcommands.Add(estate);
        var parsed = tree.Parse(arguments);
        Assert.Empty(parsed.Errors);
        int code = parsed.Invoke();
        return new(code, output.ToString(), error.ToString());
    }

    private static string[] Snapshot(string root) => Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
        .Select(file => Path.GetRelativePath(root, file) + " " + ContentHash.Sha256File(file)).Order(StringComparer.Ordinal).ToArray();
}
