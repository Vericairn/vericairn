// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text;
using Vericairn.Runtime.Guides;

namespace Vericairn.Runtime.Tests;

public sealed class CatalogWorkspaceTests
{
    [Fact]
    public void ExportsAreIdenticalAndLeaveInputsAndPrivateHomesUntouched()
    {
        using var local = new Workspace();
        Dictionary<string, byte[]> before = local.Inputs();
        CatalogBundle bundle = local.Load();
        Assert.True(bundle.Success, string.Join("; ", bundle.Findings));
        foreach (string output in new[] { "first", "second" })
        {
            string destination = Path.Combine(local.Root, output);
            Assert.Empty(CatalogWorkspace.Write(bundle, destination));
            CatalogComparison compared = CatalogWorkspace.Compare(local.Load(), destination);
            Assert.Empty(compared.Findings);
            Assert.Empty(compared.Changes);
            Assert.False(Directory.Exists(Path.Combine(destination, ".git")));
            Assert.Equal(bundle.Files.Count, Directory.GetFiles(destination, "*", SearchOption.AllDirectories).Length);
            foreach ((string path, byte[] bytes) in bundle.Files)
                Assert.Equal(bytes, File.ReadAllBytes(Path.Combine(destination, path.Replace('/', Path.DirectorySeparatorChar))));
        }
        foreach ((string path, byte[] bytes) in before) Assert.Equal(bytes, File.ReadAllBytes(Path.Combine(local.Root, path)));
        Assert.Empty(Directory.EnumerateFileSystemEntries(Path.Combine(local.Root, "home")));
    }

    [Fact]
    public void ComparisonReportsAddChangeAndRemoveWithoutWritingOrReadingGitMetadata()
    {
        using var local = new Workspace();
        CatalogBundle bundle = local.Load();
        string destination = Path.Combine(local.Root, "destination");
        Assert.Empty(CatalogWorkspace.Write(bundle, destination));
        File.Delete(Path.Combine(destination, "README.md"));
        File.WriteAllText(Path.Combine(destination, "NOTICE.md"), "Changed notice");
        File.WriteAllText(Path.Combine(destination, "old.md"), "Old public file");
        File.WriteAllBytes(Path.Combine(destination, ".git"), [0xff]);
        CatalogComparison result = CatalogWorkspace.Compare(bundle, destination);
        Assert.Empty(result.Findings);
        Assert.Equal([new("NOTICE.md", "change"), new("README.md", "add"), new CatalogChange("old.md", "remove")], result.Changes);
        Assert.False(File.Exists(Path.Combine(destination, "README.md")));
        Assert.Equal("Changed notice", File.ReadAllText(Path.Combine(destination, "NOTICE.md")));
        Assert.Equal("Old public file", File.ReadAllText(Path.Combine(destination, "old.md")));
        Assert.Equal(new byte[] { 0xff }, File.ReadAllBytes(Path.Combine(destination, ".git")));
        Assert.NotEmpty(CatalogWorkspace.Write(bundle, destination));
        Assert.False(File.Exists(Path.Combine(destination, "README.md")));
    }

    [Fact]
    public void MissingPriorGuideIsARequiredRetirementPageAndDirectoryCollisionsRefuse()
    {
        using var local = new Workspace();
        CatalogBundle bundle = local.Load();
        string destination = Path.Combine(local.Root, "destination");
        Assert.Empty(CatalogWorkspace.Write(bundle, destination));
        string prior = Path.Combine(destination, "guides", "adapter", "fixture.prior.md");
        File.WriteAllText(prior, "Prior public guide");
        CatalogComparison missing = CatalogWorkspace.Compare(bundle, destination);
        Assert.Contains(missing.Findings, f => f.Code == "retained-page-required");
        Assert.Contains(missing.Changes, c => c.Path == "guides/adapter/fixture.prior.md" && c.Action == "remove");
        Assert.Equal("Prior public guide", File.ReadAllText(prior));
        File.Delete(Path.Combine(destination, "README.md"));
        Directory.CreateDirectory(Path.Combine(destination, "README.md"));
        Assert.Contains(CatalogWorkspace.Compare(bundle, destination).Findings, f => f.Code == "destination-type");
    }

    [Fact]
    public void AllowlistIsCheckedBeforeSourcesAndUnlistedFilesNeverEnterTheBundle()
    {
        using var local = new Workspace();
        File.WriteAllBytes(Path.Combine(local.Root, "catalog", "unlisted.md"), [0xff]);
        CatalogBundle good = local.Load();
        Assert.True(good.Success, string.Join("; ", good.Findings));
        Assert.DoesNotContain(good.Files.Keys, name => name.Contains("unlisted", StringComparison.Ordinal));
        File.WriteAllBytes(Path.Combine(local.Root, "plan.json"), CatalogFixture.Json(local.Fixture.Plan with { Allowlist = [] }));
        Directory.Delete(Path.Combine(local.Root, "catalog"), recursive: true);
        CatalogBundle bad = local.Load();
        Assert.Equal("export-allowlist", Assert.Single(bad.Findings).Code);
        Assert.Empty(bad.Files);
    }

    [Theory]
    [InlineData("../outside.md")]
    [InlineData("catalog/../../outside.md")]
    [InlineData("./catalog.md")]
    [InlineData("catalog\\outside.md")]
    public void UnportableSourcePathsAreRefusedEvenInAnOtherwiseMatchingPlan(string path)
    {
        using var local = new Workspace();
        GuideSourceIndex index = local.Fixture.Guides.Index with
        {
            Sources = local.Fixture.Guides.Index.Sources.Select(s => s.Id == "catalog" ? s with { Path = path } : s).ToArray(),
        };
        File.WriteAllBytes(Path.Combine(local.Root, "index.json"), CatalogFixture.Json(index));
        File.WriteAllBytes(Path.Combine(local.Root, "plan.json"), CatalogFixture.Json(CatalogFixture.Selection(index)));
        CatalogBundle bundle = local.Load();
        Assert.False(bundle.Success);
        Assert.Empty(bundle.Files);
        Assert.Contains(bundle.Findings, f => f.Code == "export-allowlist");
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("[{\"key\":\"fixture.shared.adapter\",\"number\":7}]")]
    public void OnlyTheExplicitPublicIssueMapIsIncluded(string json)
    {
        using var local = new Workspace();
        string map = Path.Combine(local.Root, "issues.json");
        File.WriteAllText(map, json);
        CatalogBundle bundle = local.Load(map);
        Assert.True(bundle.Success, string.Join("; ", bundle.Findings));
        Assert.Equal(System.Text.Json.JsonSerializer.Deserialize<CatalogTrackingIssue[]>(json, GuideJson.Options),
            System.Text.Json.JsonSerializer.Deserialize<CatalogTrackingIssue[]>(bundle.Files["indexes/tracking-issues.json"], GuideJson.Options));
        File.WriteAllText(map, "[{\"key\":\"unknown\",\"number\":7}]");
        Assert.False(local.Load(map).Success);
    }

    [Fact]
    public void SourcesAndDestinationsCannotOverlapOrFollowLinks()
    {
        using var local = new Workspace();
        string catalog = Path.Combine(local.Root, "catalog"), contracts = Path.Combine(local.Root, "contracts"), index = Path.Combine(local.Root, "index.json");
        string output = Path.Combine(local.Root, "destination");
        Assert.True(CatalogWorkspace.SeparateOutput(output, catalog, contracts, index));
        foreach (string overlapping in new[] { local.Root, catalog, contracts, Path.Combine(catalog, "output"), index })
            Assert.False(CatalogWorkspace.SeparateOutput(overlapping, catalog, contracts, index));
        string source = Path.Combine(catalog, "catalog", "workshop00.md");
        string target = Path.Combine(local.Root, "copy.md");
        byte[] original = File.ReadAllBytes(source);
        File.WriteAllBytes(target, original);
        File.Delete(source);
        try
        {
            File.CreateSymbolicLink(source, target);
            Directory.CreateSymbolicLink(output, contracts);
        }
        catch (Exception error) when (OperatingSystem.IsWindows() && error is IOException or UnauthorizedAccessException)
        { Assert.Skip("Creating Windows symbolic links requires Developer Mode or the symlink privilege."); }
        Assert.False(local.Load().Success);
        CatalogBundle pure = local.Fixture.Generate();
        Assert.NotEmpty(CatalogWorkspace.Write(pure, output));
        Assert.NotEmpty(CatalogWorkspace.Compare(pure, output).Findings);
        Assert.Equal(original, File.ReadAllBytes(target));
        Assert.Single(Directory.GetFiles(contracts));
    }

    private sealed class Workspace : IDisposable
    {
        public string Root { get; } = CatalogFixture.CreateTemporaryDirectory("catalog-workspace-");
        public CatalogFixture Fixture { get; } = new();
        public Workspace() => Fixture.WriteInputs(Root);
        public CatalogBundle Load(string? issues = null) => CatalogWorkspace.Load(Path.Combine(Root, "index.json"), Path.Combine(Root, "plan.json"),
            Path.Combine(Root, "catalog"), Path.Combine(Root, "contracts"), issues);
        public Dictionary<string, byte[]> Inputs() => Directory.GetFiles(Root, "*", SearchOption.AllDirectories)
            .ToDictionary(p => Path.GetRelativePath(Root, p), File.ReadAllBytes, StringComparer.Ordinal);
        public void Dispose() => Directory.Delete(Root, recursive: true);
    }
}
