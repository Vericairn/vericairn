// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json.Nodes;
using Vericairn.Adapters.Git;
using Vericairn.Packs.Software;
using Vericairn.Protocol;
using Vericairn.Records.Canonical;
using Vericairn.Records.Estate;
using Vericairn.Records.Model;
using Vericairn.Records.Tests;
using Vericairn.Runtime.Policies;
using Vericairn.Runtime.Service;

namespace Vericairn.Runtime.Tests;

[Collection("policy-sources")]
public sealed class ReferenceCoverageSourceTests
{
    [Fact]
    public void PinnedPublicationReadsItsRetainedSetDespiteLaterHeadAndWorkingEdits()
    {
        using var f = new Fixture();
        f.Source.Retain("0.8.0");
        string selected = f.Source.Commit();
        RecordEnvelope publication = f.Release("0.8.0", selected, published: true);
        f.Pin("0.8.0");
        f.Source.Write("src/program.cs", "// Later build\n");
        f.Source.Commit();
        f.Source.Write("docs/releases/0.8.0/reference-inventory.json", "{}");
        using var api = EstateService.Open(f.Records.Estate.Root);
        PolicyResponse result = api.PolicyEvaluate();
        AssertCovered(result, 3, selected);
        Assert.Equal(publication.Id, Assert.Single(result.Findings).Anchor!.Id);
        AssertCovered(api.PolicyEvaluate(result.Basis.AsOf), 3, selected);
    }

    [Fact]
    public void CandidatePreviewAndSubmissionReadTheCandidatesVersionAndCommit()
    {
        using var f = new Fixture();
        f.Source.Retain("0.8.0");
        string publication = f.Source.Commit();
        f.Release("0.8.0", publication, published: true);
        f.Pin("0.8.0");
        f.Source.Write("src/program.cs", "// Candidate build\n");
        string source = f.Source.Commit();
        f.Source.Retain("0.8.1", source, "vericairn candidate");
        string selected = f.Source.Commit();
        RecordEnvelope candidate = f.Release("0.8.1", selected);
        using var api = EstateService.Open(f.Records.Estate.Root);
        AssertCovered(api.PolicyEvaluate(), 3, publication);
        PolicyResponse preview = api.PolicyEvaluate(release: candidate.Id);
        Assert.True(preview.Complete);
        PolicyFindingDto finding = Assert.Single(preview.Findings);
        Assert.Equal("command:vericairn candidate", finding.Item);
        Assert.Equal("unmet", finding.Verdict);
        Assert.Contains(selected, finding.Actual, StringComparison.Ordinal);
        ReleaseResponse submission = api.ReleaseSubmit(new(f.Product.Id, "0.8.1", "Example/reference", f.Records.Estate.OperatorId,
            Commit: selected, Expected: f.Product.ComputedHash));
        Assert.False(submission.Ok);
        Assert.NotNull(submission.Policies);
        Assert.Equal("submission", submission.Policies.Basis.Origin);
        Assert.Equal("unmet", Assert.Single(submission.Policies.Findings).Verdict);
        Assert.Equal(finding.Item, Assert.Single(submission.Policies.Findings).Item);
    }

    [Theory]
    [InlineData("src/program.cs")]
    [InlineData("tools/generator.cs")]
    [InlineData("Directory.Build.props")]
    [InlineData("Directory.Build.targets")]
    [InlineData("Directory.Packages.props")]
    [InlineData("global.json")]
    [InlineData("Vericairn.slnx")]
    [InlineData(".gitattributes")]
    [InlineData("NuGet.Config")]
    public void EveryBuildPathChangeMakesTheRetainedInventoryUnavailable(string path)
    {
        using var f = new Fixture();
        f.Source.Retain("0.8.0");
        f.Source.Write(path, "changed\n");
        f.Release("0.8.0", f.Source.Commit(), published: true);
        f.Pin("0.8.0");
        using var api = EstateService.Open(f.Records.Estate.Root);
        AssertUnavailable(api.PolicyEvaluate());
    }

    [Fact]
    public void MissingNamedSourceCommitIsUnavailable()
    {
        using var f = new Fixture();
        f.Source.Retain("0.8.0", new string('b', 40));
        f.Release("0.8.0", f.Source.Commit(), published: true);
        f.Pin("0.8.0");
        using var api = EstateService.Open(f.Records.Estate.Root);
        AssertUnavailable(api.PolicyEvaluate());
    }

    [Fact]
    public void AnIncompleteComparisonChangesTheBasisAndCannotSatisfyCoverage()
    {
        using var f = new Fixture();
        f.Source.Retain("0.8.0");
        string selected = f.Source.Commit();
        f.Release("0.8.0", selected, published: true);
        f.Pin("0.8.0");
        using var api = EstateService.Open(f.Records.Estate.Root);
        PolicyResponse complete = api.PolicyEvaluate();
        AssertCovered(complete, 3, selected);
        PolicySources.RepositorySource = new FilesOnlySource();
        PolicyResponse incomplete = api.PolicyEvaluate();
        AssertUnavailable(incomplete);
        Assert.Contains("complete matching build-tree observation", Assert.Single(incomplete.Findings).Actual, StringComparison.Ordinal);
        Assert.NotEqual(complete.Basis.Digest, incomplete.Basis.Digest);
        Assert.Equal(complete.Basis.RecordDigest, incomplete.Basis.RecordDigest);
        Assert.False(Assert.Single(incomplete.Basis.Repositories.SelectMany(r => r.TreeComparisons)).Complete);
    }

    [Fact]
    public void AFileModeChangeIsABuildSourceChange()
    {
        using var f = new Fixture();
        f.Source.Retain("0.8.0");
        f.Source.Commit();
        f.Source.Git("update-index", "--chmod=+x", "src/program.cs");
        f.Source.Git("commit", "--quiet", "-m", "Change fixture executable mode");
        f.Release("0.8.0", f.Source.Git("rev-parse", "HEAD"), published: true);
        f.Pin("0.8.0");
        using var api = EstateService.Open(f.Records.Estate.Root);
        AssertUnavailable(api.PolicyEvaluate());
    }

    [Fact]
    public void ARootInventoryCannotReplaceAMissingReleaseSet()
    {
        using var f = new Fixture();
        foreach (PolicyFile file in ReferenceInventoryTests.Repository(commit: f.Source.SourceCommit).Files)
            f.Source.Write(file.Path, PolicyInputs.Read(file));
        f.Release("0.8.0", f.Source.Commit(), published: true);
        f.Pin("0.8.0");
        using var api = EstateService.Open(f.Records.Estate.Root);
        AssertUnavailable(api.PolicyEvaluate());
        Assert.Contains("inventory is missing", Assert.Single(api.PolicyEvaluate().Findings).Actual, StringComparison.Ordinal);
    }

    [Fact]
    public void AmbientGitRoutingAndConfigurationDoNotReplaceTheObservedRepository()
    {
        using var f = new Fixture();
        using var decoy = new ReferenceRepository();
        f.Source.Retain("0.8.0");
        string selected = f.Source.Commit();
        f.Release("0.8.0", selected, published: true);
        f.Pin("0.8.0");
        using var api = EstateService.Open(f.Records.Estate.Root);
        PolicyResponse expected = api.PolicyEvaluate();
        AssertCovered(expected, 3, selected);
        var ambient = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["GIT_DIR"] = Path.Combine(decoy.Root, ".git"), ["GIT_WORK_TREE"] = decoy.Root,
            ["GIT_CONFIG_COUNT"] = "1", ["GIT_CONFIG_KEY_0"] = "core.repositoryformatversion", ["GIT_CONFIG_VALUE_0"] = "999",
            ["GIT_CONFIG_PARAMETERS"] = "'core.repositoryformatversion=999'", ["GIT_ICASE_PATHSPECS"] = "1",
        };
        var saved = ambient.Keys.ToDictionary(k => k, Environment.GetEnvironmentVariable, StringComparer.Ordinal);
        try
        {
            foreach (var (key, value) in ambient) Environment.SetEnvironmentVariable(key, value);
            PolicyResponse observed = api.PolicyEvaluate();
            AssertCovered(observed, 3, selected);
            Assert.Equal(expected.Basis.Digest, observed.Basis.Digest);
        }
        finally { foreach (var (key, value) in saved) Environment.SetEnvironmentVariable(key, value); }
    }

    private sealed class FilesOnlySource : IPolicyRepositorySource
    {
        public PolicyRepository Read(EstateModel model, RecordEnvelope repository, string? commit) => new GitPolicySource().Read(model, repository, commit);
    }

    private static void AssertCovered(PolicyResponse response, int count, string commit)
    {
        Assert.True(response.Complete, response.Text);
        PolicyFindingDto finding = Assert.Single(response.Findings);
        Assert.Equal("satisfied", finding.Verdict);
        Assert.Contains(count + " exact reference items", finding.Actual, StringComparison.Ordinal);
        Assert.Contains(commit, finding.Actual, StringComparison.Ordinal);
    }

    private static void AssertUnavailable(PolicyResponse response)
    {
        Assert.False(response.Complete);
        Assert.Equal("unavailable", Assert.Single(response.Findings).Verdict);
        Assert.Equal(0, Assert.Single(response.Rules).Subjects);
    }

    private sealed class Fixture : IDisposable
    {
        internal PolicyFixture Records { get; } = new();
        internal ReferenceRepository Source { get; } = new();
        internal RecordEnvelope Product { get; }
        private readonly IPolicyRepositorySource? previous = PolicySources.RepositorySource;

        internal Fixture()
        {
            Product = Records.Add("product", new() { ["name"] = "Reference fixture", ["slug"] = "reference", ["summary"] = "A synthetic product.",
                ["editions"] = new JsonArray(new JsonObject { ["name"] = "Community", ["slug"] = "community", ["summary"] = "Fixture edition." }),
                ["surfaces"] = new JsonArray(new JsonObject { ["name"] = "CLI", ["slug"] = "cli", ["summary"] = "Fixture interface." }),
                ["release"] = new JsonObject { ["destination"] = new JsonObject { ["provider"] = "github-releases", ["settings"] = new JsonObject { ["repository"] = "Example/reference" } } } });
            RecordEnvelope repository = Records.Add("resource", new() { ["kind"] = "repository", ["name"] = "Reference source", ["role"] = "Retain generated references.",
                ["location"] = new JsonObject { ["github"] = "Example/reference" }, ["visibility"] = "public" }, subject: Product.Reference);
            RepositoryClones.Register(Records.Estate.Estate.Id, repository.Id, Source.Root);
            PolicySources.RepositorySource = new GitPolicySource();
            PolicySources.Register(ReferenceCoverage.Registration(PolicySources.Digest(typeof(ReferenceCoverage).Assembly)));
            JsonObject body = Records.PolicyBody("register-coverage", duty: false);
            body["rules"] = new JsonArray(new JsonObject { ["key"] = "coverage", ["kind"] = ReferenceCoverage.Kind, ["version"] = 1,
                ["parameters"] = new JsonObject { ["product"] = Product.Id, ["repository"] = repository.Id, ["referenceManifest"] = "reference-inventory.json",
                    ["registerRepository"] = Records.Estate.Estate.Id, ["registerPath"] = "design/vocabulary.md", ["categories"] = new JsonArray("command", "record-type", "api-operation") } });
            Records.Policy(body);
            Records.Estate.SetClock(Records.Estate.Clock.AddHours(2));
            Records.Estate.WriteFile("design/vocabulary.md", """
                | Term | Meaning | Not to be confused with | Owning document and section | Status |
                | --- | --- | --- | --- | --- |
                | Runtime | The state owner. | A client | `docs/software.md` § Runtime | current |

                ## Generated reference coverage

                | Category | Reference | Term |
                | --- | --- | --- |
                | command | `vericairn status` | Runtime |
                | record-type | `release` | Runtime |
                | api-operation | `GET /v1/status` | Runtime |
                """ + "\n");
        }

        internal void Pin(string version)
        {
            JsonObject manifest = new() { ["estate"] = Records.Estate.Estate.Id, ["format"] = "vericairn.estate-manifest/1", ["recordFormat"] = 3,
                ["vericairn"] = new JsonObject { ["mode"] = "release", ["repository"] = "Example/reference", ["version"] = version,
                    ["artifacts"] = new JsonObject { ["linux-x64"] = PolicyBasis.Hash("fixture") } } };
            File.WriteAllBytes(Path.Combine(Records.Estate.Root, EstateLayout.ManifestPath), CanonicalJson.SerializePrettyUtf8(PolicyFixture.Element(manifest)));
            Records.AssertValid();
        }

        internal RecordEnvelope Release(string version, string commit, bool published = false)
        {
            JsonObject body = new() { ["version"] = version, ["repository"] = "Example/reference", ["commit"] = commit, ["status"] = "candidate",
                ["artifacts"] = new JsonArray(new JsonObject { ["name"] = "fixture.zip", ["hash"] = PolicyBasis.Hash("fixture"), ["bytes"] = 7, ["platform"] = "any" }),
                ["recordFormat"] = new JsonObject { ["reads"] = new JsonArray(3), ["writes"] = 3 }, ["evidence"] = new JsonArray(), ["qualified"] = new JsonArray(), ["signing"] = "fixture", ["limitations"] = new JsonArray() };
            RecordEnvelope candidate = Records.Add("release", body, subject: Product.Reference);
            if (!published) return candidate;
            Records.Add("acceptance", new() { ["statement"] = "Accept the fixture candidate.", ["basis"] = new JsonArray(), ["self"] = true }, subject: candidate.Reference);
            body["status"] = "published"; body["publishedAt"] = RecordParser.FormatTimestamp(Records.Estate.Clock); body["url"] = "https://example.invalid/reference";
            return Records.Add("release", body, subject: Product.Reference, supersedes: candidate.Id);
        }

        public void Dispose() { PolicySources.RepositorySource = previous; Records.Dispose(); Source.Dispose(); }
    }
}
