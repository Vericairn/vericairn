// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using Vericairn.Adapters.Git;
using Vericairn.Records.Model;
using Vericairn.Records.Tests;
using Vericairn.Runtime.Policies;
using Vericairn.Runtime.Service;

namespace Vericairn.Runtime.Tests;

/// <summary>Exact duty evidence through the estate service: bounded git processes, and no historical prose in current writing.</summary>
[Collection("policy-sources")]
public sealed class DutyEvidenceSourceTests
{
    [Fact]
    public void EvaluatingADutyOverTwoThousandFilesStartsAFewGitProcesses()
    {
        using var fixture = new PolicyFixture();
        (string clone, RecordEnvelope resource) = ExactCommitReadTests.Repository(fixture);
        GitHistory.Import(clone, history => history.Commit("Two thousand files",
            Enumerable.Range(0, 2000).Select(i => ExactCommitReadTests.File($"docs/part-{i / 100:D2}/file-{i:D4}.md", $"File {i}.\n")).ToArray()));
        string commit = Assert.Single(GitHistory.Commits(clone));
        RecordEnvelope policy = Duty(fixture, resource, commit);
        IPolicyRepositorySource? previous = PolicySources.RepositorySource;
        var started = new StrongBox<int>();
        try
        {
            PolicySources.RepositorySource = new GitPolicySource();
            using var api = EstateService.Open(fixture.Estate.Root);
            GitFileSource.StartedProcesses.Value = started;
            var watch = Stopwatch.StartNew();
            var evaluated = api.Policy("maintenance")!;
            watch.Stop();
            Assert.True(Assert.Single(evaluated.Findings, f => f.Duty?.Occurrence.GetProperty("number").GetInt64() == 0).Duty!.Completed);
            Assert.Equal(2000, Assert.Single(evaluated.Basis.Repositories, r => r.Evidence && r.Revision == commit).Files.Count);
            // One rev-parse, one ls-tree and one cat-file --batch; the per-file read started one more process for each file.
            Assert.InRange(started.Value, 1, 3);
            Assert.True(watch.Elapsed < TimeSpan.FromSeconds(20), $"The evaluation took {watch.Elapsed}.");
            // Another evaluation, as after an unrelated write, reads no historical content again: it only confirms the commit.
            started.Value = 0;
            Assert.Equal(evaluated.Basis.Digest, api.Policy("maintenance")!.Basis.Digest);
            Assert.Equal(1, started.Value);
        }
        finally
        {
            GitFileSource.StartedProcesses.Value = null;
            PolicySources.RepositorySource = previous;
        }
    }

    [Fact]
    public void HistoricalProofProseAddsNothingToCurrentWritingCounts()
    {
        using var fixture = new PolicyFixture();
        Directory.CreateDirectory(Path.Combine(fixture.Estate.Root, "docs"));
        System.IO.File.WriteAllText(Path.Combine(fixture.Estate.Root, "docs", "estate-notes.md"), "Current Open311 prose.\n");
        (string clone, RecordEnvelope resource) = ExactCommitReadTests.Repository(fixture);
        GitHistory.Import(clone, history => history.Commit("Historical prose", ExactCommitReadTests.File("docs/guide.md", "Obsolete Open311 prose.\nMore Report42 prose.\n")));
        string commit = Assert.Single(GitHistory.Commits(clone));
        // The clone's working tree carries the same prose: neither its history nor its working tree is current estate writing.
        GitHistory.Run(clone, "checkout", "--quiet", "-f", commit);
        IPolicyRepositorySource? previous = PolicySources.RepositorySource;
        try
        {
            PolicySources.RepositorySource = new GitPolicySource();
            int before, warnings;
            using (var api = EstateService.Open(fixture.Estate.Root))
            {
                before = api.Check().Findings.Count(f => f.Code == "writing.spacing");
                warnings = api.WritingExceptions().Warnings.Count;
            }
            Assert.Equal(1, before);
            Duty(fixture, resource, commit);
            using (var api = EstateService.Open(fixture.Estate.Root))
            {
                var evaluated = api.Policy("maintenance")!;
                Assert.True(Assert.Single(evaluated.Findings, f => f.Duty?.Occurrence.GetProperty("number").GetInt64() == 0).Duty!.Completed);
                Assert.True(Assert.Single(evaluated.Basis.Repositories, r => r.Identity == resource.Id).Evidence);
                Assert.Equal(before, api.Check().Findings.Count(f => f.Code == "writing.spacing"));
                Assert.Equal(warnings, api.WritingExceptions().Warnings.Count);
                Assert.DoesNotContain(api.WritingExceptions().Warnings, w => w.Location.Identity == resource.Id);
            }
        }
        finally { PolicySources.RepositorySource = previous; }
    }

    private static RecordEnvelope Duty(PolicyFixture fixture, RecordEnvelope resource, string commit)
    {
        RecordEnvelope policy = fixture.Estate.Load().Current("estate-policy").FirstOrDefault() ?? fixture.Policy();
        fixture.Estate.SetClock(DateTimeOffset.Parse("2026-10-17T13:00:00Z", System.Globalization.CultureInfo.InvariantCulture));
        JsonObject link = fixture.DutyLink(policy);
        link["scope"]!["repositories"] = new JsonArray(new JsonObject { ["record"] = PolicyFixture.Ref(resource), ["commit"] = commit });
        fixture.Review(fixture.Result(link));
        return policy;
    }
}
