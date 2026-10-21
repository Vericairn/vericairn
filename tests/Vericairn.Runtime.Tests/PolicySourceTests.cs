// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Diagnostics;
using System.Text;
using System.Text.Json.Nodes;
using Vericairn.Adapters.Git;
using Vericairn.Records.Estate;
using Vericairn.Records.Hashing;
using Vericairn.Records.Tests;
using Vericairn.Runtime.Policies;
using Vericairn.Runtime.Service;

namespace Vericairn.Runtime.Tests;

/// <summary>Tests that replace the process-wide policy sources run alone.</summary>
[CollectionDefinition("policy-sources", DisableParallelization = true)]
public sealed class PolicySourcesTestGroup;

[Collection("policy-sources")]
public sealed class PolicySourceTests
{
    [Fact]
    public void HistoricalDutyEvidenceKeepsItsCommitWhenTheWorkingRepositoryChanges()
    {
        using var fixture = new PolicyFixture();
        string clone = Path.Combine(fixture.Estate.Root, "fixture-repository");
        Directory.CreateDirectory(clone);
        Git(clone, "init", "--quiet");
        string file = Path.Combine(clone, "guide.md");
        File.WriteAllText(file, "Open311\n");
        Git(clone, "add", "guide.md");
        Git(clone, "-c", "user.name=Fixture", "-c", "user.email=fixture@example.invalid", "commit", "--quiet", "--no-gpg-sign", "-m", "Prior evidence");
        string commit = Git(clone, "rev-parse", "HEAD").Trim();
        var repository = fixture.Add("resource", new JsonObject
        {
            ["kind"] = "repository", ["name"] = "Evidence repository", ["role"] = "Retain exact proof sources.",
            ["location"] = new JsonObject { ["rootCommit"] = commit }, ["visibility"] = "private",
        });
        RepositoryClones.Register(fixture.Estate.Estate.Id, repository.Id, clone);
        var policy = fixture.Policy();
        fixture.Estate.SetClock(DateTimeOffset.Parse("2026-10-17T13:00:00Z", System.Globalization.CultureInfo.InvariantCulture));
        JsonObject link = fixture.DutyLink(policy);
        link["scope"]!["repositories"] = new JsonArray(new JsonObject { ["record"] = PolicyFixture.Ref(repository), ["commit"] = commit });
        var result = fixture.Result(link);
        fixture.Review(result);
        File.WriteAllText(file, "Current prose.\n");
        Git(clone, "add", "guide.md");
        Git(clone, "-c", "user.name=Fixture", "-c", "user.email=fixture@example.invalid", "commit", "--quiet", "--no-gpg-sign", "-m", "Later source");
        IPolicyRepositorySource? previous = PolicySources.RepositorySource;
        try
        {
            PolicySources.RepositorySource = new GitPolicySource();
            using var api = EstateService.Open(fixture.Estate.Root);
            var evidence = api.Policy("maintenance")!;
            Assert.True(Assert.Single(evidence.Findings, f => f.Duty?.Occurrence.GetProperty("number").GetInt64() == 0).Duty!.Completed);
            Assert.Equal(commit, Assert.Single(evidence.Basis.Repositories, r => r.Evidence && r.Identity == repository.Id).Revision);
            Assert.DoesNotContain(api.WritingExceptions().Warnings, w => w.Location.Identity == repository.Id);
            string basis = evidence.Basis.Digest;
            File.WriteAllText(file, "Another current revision.\n");
            Assert.NotEqual(basis, api.Policy("maintenance")!.Basis.Digest);
            Assert.True(Assert.Single(api.Policy("maintenance")!.Findings, f => f.Duty?.Occurrence.GetProperty("number").GetInt64() == 0).Duty!.Completed);
            // Missing exact history is unavailable, never inferred from today's bytes.
            Directory.Move(clone, clone + "-unavailable");
            Assert.Contains(api.Policy("maintenance")!.Findings, f => f.Verdict == "unavailable" && f.ReleaseHold);
        }
        finally { PolicySources.RepositorySource = previous; }
    }

    [Fact]
    public void ExactCommitSourcesIgnoreWorkingTreeChangesAndNeverFetchMissingCommits()
    {
        using var fixture = new PolicyFixture();
        string clone = Path.Combine(fixture.Estate.Root, "fixture-repository");
        Directory.CreateDirectory(clone);
        Git(clone, "init", "--quiet");
        string file = Path.Combine(clone, "guide.md");
        File.WriteAllText(file, "original\n");
        Git(clone, "add", "guide.md");
        Git(clone, "-c", "user.name=Fixture", "-c", "user.email=fixture@example.invalid", "commit", "--quiet", "--no-gpg-sign", "-m", "Fixture source");
        string commit = Git(clone, "rev-parse", "HEAD").Trim();
        var resource = fixture.Add("resource", new JsonObject
        {
            ["kind"] = "repository", ["name"] = "Fixture repository", ["role"] = "Policy source fixture.",
            ["location"] = new JsonObject { ["rootCommit"] = commit }, ["visibility"] = "private",
        });
        RepositoryClones.Register(fixture.Estate.Estate.Id, resource.Id, clone);
        var source = new GitPolicySource();
        File.WriteAllText(file, "changed\n");
        File.WriteAllText(Path.Combine(clone, "untracked.md"), "untracked\n");
        PolicyRepository exact = source.Read(fixture.Estate.Load(), resource, commit);
        Assert.True(exact.Complete);
        Assert.Equal(commit, exact.Revision);
        Assert.Equal(ContentHash.Sha256(Encoding.UTF8.GetBytes("original\n")), Assert.Single(exact.Files).Hash);
        PolicyRepository working = source.Read(fixture.Estate.Load(), resource, null);
        Assert.True(working.Complete);
        Assert.Equal(2, working.Files.Length);
        Assert.Equal(ContentHash.Sha256(Encoding.UTF8.GetBytes("changed\n")), Assert.Single(working.Files, f => f.Path == "guide.md").Hash);
        Assert.False(source.Read(fixture.Estate.Load(), resource, new string('0', 40)).Complete);
        Assert.Equal("changed\n", File.ReadAllText(file));
        Assert.Equal(commit, Git(clone, "rev-parse", "HEAD").Trim());
    }

    private static string Git(string root, params string[] arguments)
    {
        var start = new ProcessStartInfo("git") { WorkingDirectory = root, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        foreach (string argument in arguments) start.ArgumentList.Add(argument);
        using Process process = Process.Start(start)!;
        string output = process.StandardOutput.ReadToEnd();
        string error = process.StandardError.ReadToEnd();
        Assert.True(process.WaitForExit((int)TestTiming.HangTimeout.TotalMilliseconds));
        Assert.True(process.ExitCode == 0, error);
        return output;
    }
}
