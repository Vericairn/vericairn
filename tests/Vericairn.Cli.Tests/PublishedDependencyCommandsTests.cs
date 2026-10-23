// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.CommandLine;
using Vericairn.Protocol;
using Vericairn.Records.Estate;
using Vericairn.Records.Hashing;
using Vericairn.Records.Tests;
using Vericairn.Records.Updates;
using Vericairn.Runtime.Providers;
using Vericairn.Runtime.Service;

namespace Vericairn.Cli.Tests;

[Collection("cli")]
public sealed class PublishedDependencyCommandsTests
{
    [Theory]
    [InlineData("repository-synced", false)]
    [InlineData("repository-synced", true)]
    [InlineData("local-only", false)]
    [InlineData("local-only", true)]
    public void InitReleaseUsesRecordedResponsesOrOfflineFilesAndPinsTheObservation(string profile, bool offline)
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
                [.. InitArgs(root), "--release", "--profile", profile, .. offline ? new[] { "--from", download } : []]);
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
        }
        finally { ProviderRegistry.Current.VericairnReleases = previous; CliHarness.Delete(root, download); }
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

    private static string[] InitArgs(string root) => ["--estate", root, "--json", "estate", "init", "--name", "Example", "--slug", "example",
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
