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

namespace Vericairn.Runtime.Tests;

public sealed class PolicySourceTests
{
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
        Assert.True(process.WaitForExit(30000));
        Assert.True(process.ExitCode == 0, error);
        return output;
    }
}
