// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using Vericairn.Protocol;
using Vericairn.Records.Hashing;
using Vericairn.Records.Tests;
using Vericairn.Runtime.Providers;
using Vericairn.Runtime.Service;
using Vericairn.Runtime.Store;

namespace Vericairn.Cli.Tests;

[Collection("cli")]
public sealed class ReleaseCheckTests
{
    [Fact]
    public void EmbeddedCheckDoesNotCreateALiveStoreOnFirstUse()
    {
        Directory.Delete(CliHarness.TempDirectory("release-check-bootstrap"));
        using var estate = new TestEstate(recordFormat: 2);
        string directory = Path.GetDirectoryName(EstateSession.LiveStorePath(estate.Root, estate.Estate.Id, "repository-synced"))!;
        Assert.False(Directory.Exists(directory));
        string[] before = Snapshot(estate.Root);

        CliHarness.Outcome result = CliHarness.Run("--estate", estate.Root, "--json", "release", "submit", "--check", "--product", estate.Estate.Id,
            "--version", "1.0.0", "--commit", new string('a', 40), "--expected", estate.Estate.ComputedHash, "--by", estate.OperatorId);

        Assert.Equal(2, result.Code);
        Assert.Equal("check-refused", result.Json.GetProperty("status").GetString());
        Assert.Contains("release.product", result.Out, StringComparison.Ordinal);
        Assert.False(Directory.Exists(directory));
        Assert.Equal(before, Snapshot(estate.Root));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CheckReportsExactInputsAndObjectionsThroughTheReadOnlyOperation(bool json)
    {
        Directory.Delete(CliHarness.TempDirectory("release-check-bootstrap")); // Initialize the harness's isolated home first.
        using var estate = new TestEstate(recordFormat: 2);
        var product = estate.Add("product", estate.OperatorId, """
            {"name":"Widget","slug":"widget","summary":"Fixture product.","editions":[{"name":"Community","slug":"community","summary":"Fixture edition."}],
             "surfaces":[{"name":"CLI","slug":"cli","summary":"Command line."}],"release":{"destination":{"provider":"github-releases","settings":{"repository":"Example/widget"}}}}
            """);
        using var api = EstateService.Open(estate.Root);
        var provider = new FakeReleaseProvider();
        api.Providers = new ProviderRegistry();
        api.Providers.Register(provider);
        ReleaseGateSource.Prepare(api, "Example/widget", provider.HeadSha);
        string[] before = Snapshot(estate.Root);
        string[] arguments = ["--estate", estate.Root, .. (json ? new[] { "--json" } : []), "release", "submit", "--check", "--product", product.Id,
            "--version", "1.0.0", "--commit", provider.HeadSha, "--expected", "stale", "--by", estate.OperatorId];

        CliHarness.Outcome result = CliHarness.RunWithApi(api, null, arguments);

        Assert.Equal(2, result.Code);
        Assert.Contains("release.expected", result.Out, StringComparison.Ordinal);
        Assert.Contains("Next:", result.Out, StringComparison.Ordinal);
        if (json)
        {
            Assert.Equal("check-refused", result.Json.GetProperty("status").GetString());
            Assert.Equal(provider.HeadSha, result.Json.GetProperty("check").GetProperty("commit").GetString());
            Assert.Equal(CommandContext.VersionText(), result.Json.GetProperty("check").GetProperty("clientBuild").GetString());
            Assert.NotEmpty(result.Json.GetProperty("check").GetProperty("objections").EnumerateArray());
        }
        else Assert.Contains("nothing retained", result.Out, StringComparison.Ordinal);
        Assert.Empty(provider.Dispatches);
        Assert.Equal(before, Snapshot(estate.Root));
        Assert.Empty(api.Records("operation"));

        arguments[Array.IndexOf(arguments, "stale")] = product.ComputedHash;
        CliHarness.Outcome passed = CliHarness.RunWithApi(api, null, arguments);
        Assert.Equal(0, passed.Code);
        Assert.Empty(provider.Dispatches);
        Assert.Equal(before, Snapshot(estate.Root));
    }

    private static string[] Snapshot(string root) => Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
        .Select(file => Path.GetRelativePath(root, file) + " " + ContentHash.Sha256File(file)).Order(StringComparer.Ordinal).ToArray();
}
