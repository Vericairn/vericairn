// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using Vericairn.Protocol;
using Vericairn.Records.Estate;
using Vericairn.Records.Hashing;
using Vericairn.Runtime.Service;
using Vericairn.TestSupport;

namespace Vericairn.Runtime.Tests;

public sealed partial class ReleaseDocumentGateTests
{
    [Theory]
    [InlineData("clean", null)]
    [InlineData("tracked", "release.clone-dirty")]
    [InlineData("staged", "release.clone-dirty")]
    [InlineData("untracked", "release.clone-dirty")]
    [InlineData("non-default", "release.default-head")]
    [InlineData("unknown-default", "release.default-head")]
    [InlineData("missing-clone", "release.clone")]
    public void PreflightChecksRealClonesAndDefaultHeadWithoutTouchingGitOrEstateBytes(string state, string? objection)
    {
        using var f = new Fixture(covers: false);
        string reviewed = f.Commit("Reviewed release guide.\n");
        f.Review(reviewed);
        f.Provider.HeadSha = reviewed;
        if (state is "tracked" or "staged") File.WriteAllText(Path.Combine(f.Repository, "README.md"), "Uncommitted guide.\n");
        if (state == "staged") Git(f.Repository, "add", "README.md");
        if (state == "untracked") File.WriteAllText(Path.Combine(f.Repository, "scratch.md"), "Untracked file.\n");
        if (state == "non-default") f.Commit("Another default branch head.\n");
        if (state == "unknown-default") Git(f.Repository, "symbolic-ref", "--delete", "refs/remotes/origin/HEAD");
        if (state == "missing-clone") RepositoryClones.Register(f.Estate.Estate.Id, f.Resource.Id, Path.Combine(f.Scratch.Root, "missing"));
        ReleaseSubmitRequest request = f.Request with { Expected = f.Estate.Load().ById[f.Request.Product].ComputedHash, ClientBuild = EstateService.Version };
        string[] estate = Snapshot(f.Estate.Root), source = Snapshot(f.Repository);

        ReleaseResponse check = f.Service.ReleaseCheck(request);

        Assert.Equal(objection is null, check.Ok);
        if (objection is not null) Assert.Contains(check.Check!.Objections, o => o.Code == objection);
        if (state != "missing-clone") Assert.DoesNotContain(check.Check!.Objections, o => o.Code == "document.release-hold");
        Assert.All(check.Check!.Objections, o => Assert.NotEmpty(o.NextAction));
        Assert.Equal(estate, Snapshot(f.Estate.Root));
        Assert.Equal(source, Snapshot(f.Repository));
        Assert.Empty(f.Provider.Dispatches);
        ReleaseResponse submitted = f.Service.ReleaseSubmit(request);
        Assert.Equal(check.Ok, submitted.Ok);
        if (objection is not null) Assert.Equal(check.Problem!.Findings, submitted.Problem!.Findings);
    }

    [Fact]
    public void DirtyDocumentCloneInAnotherRepositoryIsAlsoAnObjection()
    {
        using var f = new Fixture(covers: false, otherRepository: true);
        f.Review(f.Commit("Reviewed guide.\n"));
        File.WriteAllText(Path.Combine(f.Repository, "untracked.md"), "Local work.\n");
        ReleaseResponse checkedResponse = f.Service.ReleaseCheck(f.Request);
        Assert.False(checkedResponse.Ok);
        Assert.Contains(checkedResponse.Check!.Objections, o => o.Code == "release.clone-dirty" && o.Location == f.Resource.Id);
        Assert.DoesNotContain(checkedResponse.Check.Objections, o => o.Code == "document.release-hold");
        Assert.Empty(f.Provider.Dispatches);
    }

    [Fact]
    public void OpeningAReadOnlyCheckDoesNotCreateLiveStorageOrRecoverBatchJournals()
    {
        using var f = new Fixture(covers: false);
        string commit = f.Commit("Reviewed guide.\n");
        f.Review(commit);
        f.Provider.HeadSha = commit;
        string directory = Path.GetDirectoryName(f.Service.Session.Live.Path)!;
        string batches = Path.Combine(directory, "batches");
        Directory.CreateDirectory(batches);
        File.WriteAllText(Path.Combine(batches, "unreadable.json"), "{}");
        string[] estate = Snapshot(f.Estate.Root), live = Snapshot(directory);
        using (EstateService reader = EstateService.OpenReadOnly(f.Estate.Root))
        {
            reader.Providers = f.Service.Providers;
            ReleaseResponse check = reader.ReleaseCheck(f.Request);
            Assert.False(check.Ok);
            Assert.Contains(check.Check!.Objections, o => o.Code.StartsWith("batch.", StringComparison.Ordinal));
        }
        Assert.Equal(estate, Snapshot(f.Estate.Root));
        Assert.Equal(live, Snapshot(directory));
        ReleaseResponse submitted = f.Service.ReleaseSubmit(f.Request);
        Assert.False(submitted.Ok);
        Assert.Contains(submitted.Problem!.Findings, f => f.Code == "batch.unreadable");
        Assert.Equal(estate, Snapshot(f.Estate.Root));
        Assert.Equal(live, Snapshot(directory));
        Assert.Empty(f.Provider.Dispatches);
    }

    private static string[] Snapshot(string root) => Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
        .Select(file => Path.GetRelativePath(root, file) + " " + SharedFileHash.Sha256(file)).Order(StringComparer.Ordinal).ToArray();
}
