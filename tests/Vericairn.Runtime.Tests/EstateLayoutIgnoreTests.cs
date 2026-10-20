// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using Vericairn.Adapters.Git;
using Vericairn.Records.Estate;
using Vericairn.Records.Model;
using Vericairn.Records.Tests;
using Vericairn.Runtime.Providers;
using Vericairn.Runtime.Service;

namespace Vericairn.Runtime.Tests;

public sealed class EstateLayoutIgnoreTests
{
    [Fact]
    public void IgnoredResiduePassesCheckStatusAndProgressButUnignoredContentFails()
    {
        using var estate = new TestEstate();
        RunEstate.Git(estate.Root, "init", "-q");
        estate.WriteFile(".gitignore", ".DS_Store\n");
        estate.WriteFile(".DS_Store", "residue");
        using EstateService service = EstateService.Open(estate.Root);
        service.Providers = new ProviderRegistry();
        service.Providers.Register(new GitCommandAdapter());
        Assert.Equal(0, service.Check().Errors);
        Assert.Equal(0, service.Check().Warnings);
        Assert.False(service.Status().HasErrors);
        Assert.False(service.Progress().HasErrors);

        estate.WriteFile("unexpected.txt", "still checked\n");
        Assert.Equal(1, service.Check().Errors);
        Assert.True(service.Status().HasErrors);
        Assert.True(service.Progress().HasErrors);
        Assert.Contains(Check(estate), f => f.Code == "layout.unexpected" && f.Location == "unexpected.txt");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TrackedEntriesAndDirectoriesWithTrackedChildrenRemainChecked(bool commit)
    {
        using var estate = new TestEstate();
        RunEstate.Git(estate.Root, "init", "-q");
        estate.WriteFile(".gitignore", ".DS_Store\ncache/\n");
        estate.WriteFile(".DS_Store", "residue");
        estate.WriteFile("cache/kept.txt", "tracked\r\n");
        RunEstate.Git(estate.Root, "add", "-f", "--", ".DS_Store", "cache/kept.txt");
        if (commit) RunEstate.Git(estate.Root, "commit", "-qm", "Track the fixture");
        FindingList checks = Check(estate);
        Assert.Contains(checks, f => f.Code == "layout.unexpected" && f.Location == ".DS_Store");
        Assert.Contains(checks, f => f.Code == "layout.unexpected" && f.Location == "cache");
        Assert.Contains(checks, f => f.Code == "layout.residue" && f.Location == ".DS_Store");
        Assert.Contains(checks, f => f.Code == "layout.line-endings" && f.Location == "cache/kept.txt");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TrackedDirectoriesUseTheRepositoryCaseSetting(bool ignoreCase)
    {
        using var estate = new TestEstate();
        RunEstate.Git(estate.Root, "init", "-q");
        RunEstate.Git(estate.Root, "config", "core.ignorecase", ignoreCase ? "true" : "false");
        estate.WriteFile("Cache/kept.txt", "tracked\n");
        RunEstate.Git(estate.Root, "add", "Cache/kept.txt");
        Directory.Move(Path.Combine(estate.Root, "Cache"), Path.Combine(estate.Root, "temporary"));
        Directory.Move(Path.Combine(estate.Root, "temporary"), Path.Combine(estate.Root, "cache"));
        estate.WriteFile(".gitignore", "/cache/\n");
        Assert.Equal(ignoreCase, Check(estate).Any(f => f.Code == "layout.unexpected" && f.Location == "cache"));
    }

    [Fact]
    public void GitPatternsNegationDirectoryRulesAndLocalExcludesAreRespected()
    {
        using var estate = new TestEstate();
        RunEstate.Git(estate.Root, "init", "-q");
        estate.WriteFile(".gitignore", "*.tmp\n!keep.tmp\n/cache/\nfile-only/\n\\#literal\n\\!literal\n");
        estate.WriteFile(".git/info/exclude", "local.txt\n");
        foreach (string path in new[] { "ignored.tmp", "cache/output.txt", "#literal", "!literal", "local.txt" })
            estate.WriteFile(path, "ignored\r\n");
        foreach (string path in new[] { "keep.tmp", "file-only", "unexpected.txt" })
            estate.WriteFile(path, "still checked\n");
        // A nested ignore cannot exclude its unexpected parent directory.
        estate.WriteFile("nested/.gitignore", "*\n");
        estate.WriteFile("nested/output.txt", "still scanned\r\n");
        FindingList checks = Check(estate);
        Assert.Equal(["file-only", "keep.tmp", "nested", "unexpected.txt"],
            checks.Where(f => f.Code == "layout.unexpected").Select(f => f.Location).ToArray());
        Assert.Single(checks, f => f.Code == "layout.line-endings" && f.Location == "nested/output.txt");
    }

    [Fact]
    public void IgnoreRulesNeverHideRecordsDesignDocumentsOrObjects()
    {
        using var estate = new TestEstate();
        RunEstate.Git(estate.Root, "init", "-q");
        estate.WriteFile(".gitignore", "*\n");
        estate.WriteFile(".DS_Store", "ignored");
        estate.WriteFile("records/invalid.json", "{");
        estate.WriteFile("design/plan.md", "checked\r\n");
        estate.WriteFile("docs/guide.md", "checked\r\n");
        estate.WriteFile("objects/" + new string('a', 64), "wrong content");
        EstateModel model = estate.Load();
        Assert.True(model.Findings.HasErrors);
        FindingList checks = EstateChecks.Run(model, new GitCommandAdapter().IgnoredEstateEntries(estate.Root));
        Assert.DoesNotContain(checks, f => f.Location == ".DS_Store");
        Assert.Contains(checks, f => f.Code == "layout.line-endings" && f.Location == "design/plan.md");
        Assert.Contains(checks, f => f.Code == "layout.line-endings" && f.Location == "docs/guide.md");
        Assert.Contains(checks, f => f.Code == "objects.hash");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NonGitEstatesRetainTheExistingBehavior(bool enclosingRepository)
    {
        using var parent = new TestEstate();
        string root = parent.Root;
        if (enclosingRepository)
        {
            RunEstate.Git(root, "init", "-q");
            root = Path.Combine(root, "nested");
            Directory.CreateDirectory(root);
        }
        File.WriteAllText(Path.Combine(root, ".gitignore"), ".DS_Store\n");
        File.WriteAllText(Path.Combine(root, ".DS_Store"), "residue");
        var adapter = new GitCommandAdapter(_ => throw new InvalidOperationException("No Git process should run."));
        Assert.Null(adapter.IgnoredEstateEntries(root));
        FindingList checks = EstateChecks.Run(EstateLoader.Load(root), adapter.IgnoredEstateEntries(root));
        Assert.Contains(checks, f => f.Code == "layout.unexpected" && f.Location == ".DS_Store");
        Assert.Contains(checks, f => f.Code == "layout.residue" && f.Location == ".DS_Store");
    }

    [Fact]
    public void RepositoryConfiguredExternalIgnoreFilesDoNotExcludeEntries()
    {
        using var estate = new TestEstate();
        using var external = new TestEstate();
        RunEstate.Git(estate.Root, "init", "-q");
        external.WriteFile("ignore", "unexpected.txt\n");
        RunEstate.Git(estate.Root, "config", "core.excludesFile", Path.Combine(external.Root, "ignore"));
        estate.WriteFile("unexpected.txt", "still checked\n");
        Assert.Contains(Check(estate), f => f.Code == "layout.unexpected" && f.Location == "unexpected.txt");
    }

    [Fact]
    public void LinkedWorktreesUseTheirOwnIndexAndIgnoreRules()
    {
        using var estate = new TestEstate();
        using var storage = new TestEstate();
        RunEstate.Git(estate.Root, "init", "-q");
        // The worktree is checked out from the commit, so keep its line endings as committed on every platform.
        RunEstate.Git(estate.Root, "config", "core.autocrlf", "false");
        estate.WriteFile(".gitignore", ".DS_Store\n");
        RunEstate.Git(estate.Root, "add", ".");
        RunEstate.Git(estate.Root, "commit", "-qm", "Create the fixture");
        string worktree = Path.Combine(storage.Root, "checkout");
        RunEstate.Git(estate.Root, "worktree", "add", "--quiet", "--detach", worktree, "HEAD");
        File.WriteAllText(Path.Combine(worktree, ".DS_Store"), "residue");
        var adapter = new GitCommandAdapter();
        Assert.Empty(EstateChecks.Run(EstateLoader.Load(worktree), adapter.IgnoredEstateEntries(worktree)));
        RunEstate.Git(worktree, "add", "-f", "--", ".DS_Store");
        Assert.Contains(EstateChecks.Run(EstateLoader.Load(worktree), adapter.IgnoredEstateEntries(worktree)),
            f => f.Code == "layout.unexpected" && f.Location == ".DS_Store");
    }

    [Fact]
    public void CheckingDoesNotChangeTheIndexIgnoreRulesOrWorktree()
    {
        using var estate = new TestEstate();
        RunEstate.Git(estate.Root, "init", "-q");
        estate.WriteFile(".gitignore", ".DS_Store\ncache/\n");
        RunEstate.Git(estate.Root, "add", ".");
        estate.WriteFile(".DS_Store", "residue");
        estate.WriteFile("cache/output.txt", "ignored\r\n");
        Dictionary<string, byte[]> before = Snapshot();
        Assert.Empty(Check(estate));
        Dictionary<string, byte[]> after = Snapshot();
        Assert.Equal(before.Keys.Order(StringComparer.Ordinal), after.Keys.Order(StringComparer.Ordinal));
        foreach ((string path, byte[] bytes) in before) Assert.Equal(bytes, after[path]);

        Dictionary<string, byte[]> Snapshot() => Directory.EnumerateFiles(estate.Root, "*", SearchOption.AllDirectories)
            .ToDictionary(path => EstateLayout.Relative(estate.Root, path), File.ReadAllBytes, StringComparer.Ordinal);
    }

    [Theory]
    [InlineData("NotStarted")]
    [InlineData("TimedOut")]
    [InlineData("OutputLimitExceeded")]
    [InlineData("Failed")]
    public void IncompleteGitObservationsNeverExcludeEntries(string outcome)
    {
        using var estate = new TestEstate();
        estate.WriteFile(".git/HEAD", "ref: refs/heads/main\n");
        estate.WriteFile(".DS_Store", "residue");
        var adapter = new GitCommandAdapter(info =>
        {
            Assert.Equal(string.Empty, info.Environment["GIT_CONFIG_GLOBAL"]);
            Assert.Equal("1", info.Environment["GIT_CONFIG_NOSYSTEM"]);
            Assert.Equal("0", info.Environment["GIT_OPTIONAL_LOCKS"]);
            Assert.Equal("1", info.Environment["GIT_NO_LAZY_FETCH"]);
            Assert.Contains("core.excludesFile=", info.ArgumentList);
            Assert.Contains("core.fsmonitor=false", info.ArgumentList);
            Assert.Contains("protocol.allow=never", info.ArgumentList);
            return new(Enum.Parse<ProcessOutcome>(outcome), true, null, ".DS_Store\0", string.Empty);
        });
        Assert.Null(adapter.IgnoredEstateEntries(estate.Root));
        Assert.Contains(EstateChecks.Run(estate.Load(), adapter.IgnoredEstateEntries(estate.Root)),
            f => f.Code == "layout.unexpected" && f.Location == ".DS_Store");
    }

    private static FindingList Check(TestEstate estate) =>
        EstateChecks.Run(estate.Load(), new GitCommandAdapter().IgnoredEstateEntries(estate.Root));
}
