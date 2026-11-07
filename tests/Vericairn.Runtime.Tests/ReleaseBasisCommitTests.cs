// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json.Nodes;
using Vericairn.Adapters.Git;
using Vericairn.Protocol;
using Vericairn.Records.Estate;
using Vericairn.Records.Hashing;
using Vericairn.Records.Model;
using Vericairn.Runtime.Policies;

namespace Vericairn.Runtime.Tests;

/// <summary>A basis names the files of an exact commit by path and hash and reads them from the repository's git objects (FIX-95).</summary>
[Collection("policy-sources")]
public sealed class ReleaseBasisCommitTests
{
    private const string Guide = "Evidence prose at an exact commit.\n";
    private const string Output = "An output a result names, also a file of the commit.\n";
    private static readonly string GuideHash = ContentHash.Sha256(System.Text.Encoding.UTF8.GetBytes(Guide));
    private static readonly string OutputHash = ContentHash.Sha256(System.Text.Encoding.UTF8.GetBytes(Output));

    [Fact]
    public void AFileAtAnExactCommitReadsFromGitWithNoCopyInTheEstate()
    {
        using var f = new ReleaseBasisFixture();
        (string identity, string commit) = Evidence(f);
        string hash = Store(f, "vericairn.release-basis/3", identity, commit, new JsonObject { ["path"] = "docs/guide.md", ["hash"] = GuideHash, ["available"] = true, ["isLink"] = false });
        Assert.Null(f.Api.Session.Objects.Read(GuideHash));

        ReleaseBasisResponse response = Read(f, hash);

        Assert.Null(response.Problem);
        ReleaseBasisRepository repository = Assert.Single(response.Evidence!.Repositories);
        Assert.Equal((identity, commit), (repository.Identity, repository.Revision));
        Assert.Equal(Guide, System.Text.Encoding.UTF8.GetString(Assert.Single(repository.Files).Bytes.ToArray()));
        Assert.Equal("vericairn.release-basis/3", response.Evidence.Format);
        Assert.Null(f.Api.Session.Objects.Read(GuideHash));
    }

    [Fact]
    public void WithoutTheCommitHereTheFileIsMissingAndNamedWithItsCommit()
    {
        using var f = new ReleaseBasisFixture();
        (string identity, string commit) = Evidence(f);
        string other = ContentHash.Sha256("Bytes the commit never held.\n"u8);
        string hash = Store(f, "vericairn.release-basis/3", identity, commit,
            new JsonObject { ["path"] = "docs/guide.md", ["hash"] = GuideHash, ["available"] = true, ["isLink"] = false },
            new JsonObject { ["path"] = "docs/output.md", ["hash"] = other, ["available"] = true, ["isLink"] = false });

        // The commit's own file of another hash is not the file the basis names.
        FindingDto changed = Assert.Single(Read(f, hash).Problem!.Findings);
        Assert.Equal(("release-basis.missing", $"source:{identity}@{commit}:docs/output.md@{other}"), (changed.Code, changed.Location));
        Assert.Contains("git objects at the recorded commit", changed.Message, StringComparison.Ordinal);

        // A clone registered here that does not hold the commit leaves every file of it unresolved.
        string empty = Path.Combine(f.Records.Estate.Root, "empty-repository");
        Directory.CreateDirectory(empty);
        GitHistory.Run(empty, "init", "--quiet");
        RepositoryClones.Register(f.Records.Estate.Estate.Id, identity, empty);
        ReleaseBasisResponse unavailable = Read(f, hash);
        Assert.Null(unavailable.Evidence);
        Assert.Equal([$"source:{identity}@{commit}:docs/guide.md@{GuideHash}", $"source:{identity}@{commit}:docs/output.md@{other}"],
            unavailable.Problem!.Findings.Select(finding => finding.Location));
        Assert.All(unavailable.Problem.Findings, finding => Assert.Equal("release-basis.missing", finding.Code));
    }

    [Fact]
    public void AnEarlierBasisWhoseCopiesWereNeverCommittedReadsFromGit()
    {
        // 0.9.1's basis: retained in the earlier format, its copies of exact-commit files and of the objects those copies
        // supplied left out of the estate's main branch.
        using var f = new ReleaseBasisFixture();
        (string identity, string commit) = Evidence(f);
        f.Stored["objects"]!.AsArray().Add(new JsonObject { ["hash"] = OutputHash, ["available"] = true });
        string hash = Store(f, "vericairn.release-basis/2", identity, commit, new JsonObject { ["path"] = "docs/guide.md", ["hash"] = GuideHash, ["available"] = true, ["isLink"] = false });
        Assert.Null(f.Api.Session.Objects.Read(OutputHash));

        ReleaseBasisResponse response = Read(f, hash);

        Assert.Null(response.Problem);
        Assert.Equal("vericairn.release-basis/2", response.Evidence!.Format);
        Assert.Equal(Guide, System.Text.Encoding.UTF8.GetString(Assert.Single(Assert.Single(response.Evidence.Repositories).Files).Bytes.ToArray()));
        Assert.Equal(Output, System.Text.Encoding.UTF8.GetString(Assert.Single(response.Evidence.Objects, o => o.Hash == OutputHash).Bytes.ToArray()));
        Assert.Equal("Retained proof.\n", System.Text.Encoding.UTF8.GetString(Assert.Single(response.Evidence.Objects, o => o.Hash == f.ProofHash).Bytes.ToArray()));
    }

    [Fact]
    public void AnEarlierBasisWithItsCopiesStillReadsWithoutAClone()
    {
        using var f = new ReleaseBasisFixture();
        const string commit = "89abcdef0123456789abcdef0123456789abcdef";
        string hash = Store(f, "vericairn.release-basis/2", "fixture-repository", commit,
            new JsonObject { ["path"] = "docs/evidence.md", ["hash"] = f.SourceHash, ["available"] = true, ["isLink"] = false });

        ReleaseBasisResponse response = Read(f, hash);

        Assert.Null(response.Problem);
        Assert.Equal("Retained admission source.\n", System.Text.Encoding.UTF8.GetString(Assert.Single(Assert.Single(response.Evidence!.Repositories).Files).Bytes.ToArray()));
        f.LoseObject(f.SourceHash, corrupted: false);
        FindingDto lost = Assert.Single(Read(f, hash).Problem!.Findings);
        Assert.Equal(("release-basis.missing", $"source:fixture-repository@{commit}:docs/evidence.md@{f.SourceHash}"), (lost.Code, lost.Location));
    }

    // A registered clone with one commit holding the guide and the output; the resource is the repository's identity.
    private static (string Identity, string Commit) Evidence(ReleaseBasisFixture f)
    {
        (string clone, RecordEnvelope resource) = ExactCommitReadTests.Repository(f.Records);
        GitHistory.Import(clone, history => history.Commit("Evidence", ExactCommitReadTests.File("docs/guide.md", Guide), ExactCommitReadTests.File("docs/output.md", Output)));
        return (resource.Id, Assert.Single(GitHistory.Commits(clone)));
    }

    private static string Store(ReleaseBasisFixture f, string format, string identity, string revision, params JsonObject[] files)
    {
        f.Stored["format"] = format;
        JsonObject repository = f.Stored["repositories"]![0]!.AsObject();
        repository["identity"] = identity;
        repository["revision"] = revision;
        repository["files"] = new JsonArray([.. files]);
        return f.Store();
    }

    private static ReleaseBasisResponse Read(ReleaseBasisFixture f, string hash)
    {
        IPolicyRepositorySource? previous = PolicySources.RepositorySource;
        try
        {
            PolicySources.RepositorySource = new GitPolicySource();
            return Assert.IsType<ReleaseBasisResponse>(f.Api.ReleaseBasis(hash));
        }
        finally { PolicySources.RepositorySource = previous; }
    }
}
