// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Diagnostics;
using Vericairn.Adapters.Git;
using Vericairn.Runtime.Providers;
using Vericairn.Runtime.Workers;

namespace Vericairn.Runtime.Tests;

public sealed class LocalPushTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "vericairn-push-test-" + Guid.NewGuid().ToString("N"));
    private readonly string repository;
    private readonly string remote;
    private readonly string configuration;
    private readonly string hooks;

    public LocalPushTests()
    {
        repository = Path.Combine(directory, "work");
        remote = Path.Combine(directory, "remote.git");
        configuration = Path.Combine(directory, "gitconfig");
        hooks = Path.Combine(directory, "hooks");
        Directory.CreateDirectory(repository);
        Directory.CreateDirectory(hooks);
        File.WriteAllText(configuration, string.Empty);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ALocalBareRepositoryReceivesOnlyTheExactScannedHead(bool existingBranch)
    {
        Git(repository, "init", "--initial-branch=main");
        File.WriteAllText(Path.Combine(repository, "readme.txt"), "Initial content.\n");
        Git(repository, "add", "readme.txt");
        Git(repository, "commit", "-m", "Initial content");
        string basis = Git(repository, "rev-parse", "HEAD");
        Git(directory, "clone", "--bare", "--", repository, remote);
        if (existingBranch) Git(remote, "update-ref", "refs/heads/work/change", basis);
        Git(repository, "checkout", "-b", "work/change");
        File.WriteAllText(Path.Combine(repository, "addition.txt"), "Checked addition.\n");
        Git(repository, "add", "addition.txt");
        Git(repository, "commit", "-m", "Add checked content");
        string head = Git(repository, "rev-parse", "HEAD");
        var adapter = new GitPushAdapter(info =>
        {
            // Every remote access in this test is to the bare repository this test just created.
            if (info.ArgumentList.Contains("ls-remote") || info.ArgumentList.Contains("push")) Assert.Contains(remote, info.ArgumentList);
            return Run(info);
        });
        PushPlan plan = adapter.Prepare(repository, remote, "work/change", "main", head);
        Assert.Equal(basis + ".." + head, plan.Range);
        Assert.Equal(existingBranch ? basis : null, plan.RemoteHead);
        adapter.Push(repository, remote, plan, new("test-scanner", "1.0.0", plan.Range, "passed", 0));
        Assert.Equal(head, adapter.Observe(repository, remote, "work/change"));
        Assert.Equal(basis, adapter.Observe(repository, remote, "main"));
        Assert.Empty(Git(remote, "tag", "--list"));
    }

    private string Git(string workingDirectory, params string[] args)
    {
        var info = new ProcessStartInfo("git") { WorkingDirectory = workingDirectory, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (string argument in args) info.ArgumentList.Add(argument);
        ProcessResult result = Run(info);
        Assert.Equal(ProcessOutcome.Exited, result.Outcome);
        Assert.True(result.ExitCode == 0, "The isolated git command did not succeed.");
        return result.StandardOutput.Trim();
    }

    private ProcessResult Run(ProcessStartInfo info)
    {
        info.Environment["GIT_CONFIG_NOSYSTEM"] = "1";
        info.Environment["GIT_CONFIG_GLOBAL"] = configuration;
        info.Environment["GIT_TERMINAL_PROMPT"] = "0";
        string[] arguments = info.ArgumentList.ToArray();
        info.ArgumentList.Clear();
        foreach (string value in new[] { "user.name=Test", "user.email=test@example.invalid", "commit.gpgsign=false", "core.hooksPath=" + hooks })
        { info.ArgumentList.Add("-c"); info.ArgumentList.Add(value); }
        foreach (string argument in arguments) info.ArgumentList.Add(argument);
        return BoundedProcess.Run(info, TestTiming.HangTimeout);
    }

    public void Dispose() => WorkspaceCleanup.Delete(directory);
}
