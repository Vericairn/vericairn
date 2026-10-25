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
        var adapter = Adapter();
        PushPlan plan = adapter.Prepare(repository, remote, "work/change", "main", head);
        Assert.Equal(basis + ".." + head, plan.Range);
        Assert.Equal(existingBranch ? basis : null, plan.RemoteHead);
        adapter.Push(repository, remote, plan, new("test-scanner", "1.0.0", plan.Range, "passed", 0));
        Assert.Equal(head, adapter.Observe(repository, remote, "work/change"));
        Assert.Equal(basis, adapter.Observe(repository, remote, "main"));
        Assert.Empty(Git(remote, "tag", "--list"));
    }

    [Fact]
    public void TheClonesAndTheUsersRewritesAndHooksDoNotChangeWhereOrHowACheckedPushGoes()
    {
        string decoy = Path.Combine(directory, "decoy.git");
        string clientHooks = Path.Combine(directory, "client-hooks");
        string marker = Path.Combine(directory, "hook-ran");
        Git(repository, "init", "--initial-branch=main");
        File.WriteAllText(Path.Combine(repository, "readme.txt"), "Initial content.\n");
        Git(repository, "add", "readme.txt");
        Git(repository, "commit", "-m", "Initial content");
        Git(directory, "clone", "--bare", "--", repository, remote);
        Git(directory, "clone", "--bare", "--", repository, decoy);
        Git(repository, "checkout", "-b", "work/change");
        File.WriteAllText(Path.Combine(repository, "addition.txt"), "Checked addition.\n");
        Git(repository, "add", "addition.txt");
        Git(repository, "commit", "-m", "Add checked content");
        string head = Git(repository, "rev-parse", "HEAD");

        // The clone's own configuration sends every fetch and push for the declared remote to the decoy and runs a hook that would
        // refuse the push; the user's global configuration rewrites pushes too. None of it may apply to a checked push.
        Directory.CreateDirectory(clientHooks);
        string hook = Path.Combine(clientHooks, "pre-push");
        File.WriteAllText(hook, "#!/bin/sh\necho ran > '" + marker.Replace('\\', '/') + "'\nexit 1\n");
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(hook, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        Git(repository, "config", "url." + decoy + ".insteadOf", remote);
        Git(repository, "config", "url." + decoy + ".pushInsteadOf", remote);
        Git(repository, "config", "core.hooksPath", clientHooks);
        Git(repository, "config", "--file", configuration, "url." + decoy + ".pushInsteadOf", remote);
        Git(repository, "config", "--file", configuration, "credential.helper", "store --file=" + Path.Combine(directory, "unused").Replace('\\', '/'));

        var adapter = Adapter();
        PushPlan plan = adapter.Prepare(repository, remote, "work/change", "main", head);
        Assert.Null(plan.RemoteHead);
        adapter.Push(repository, remote, plan, new("test-scanner", "1.0.0", plan.Range, "passed", 0));
        Assert.Equal(head, adapter.Observe(repository, remote, "work/change"));
        Assert.Equal(head, Git(remote, "rev-parse", "refs/heads/work/change"));
        Assert.Empty(Git(decoy, "for-each-ref", "refs/heads/work"));
        Assert.False(File.Exists(marker));
    }

    // The adapter isolates its own git processes. Only its read of the user's credential settings sees this test's stand-in
    // for the user's global configuration.
    private GitPushAdapter Adapter() => new(info =>
    {
        // Every remote access in this test is to the bare repository this test just created.
        if (info.ArgumentList.Contains("ls-remote") || info.ArgumentList.Contains("push")) Assert.Contains(remote, info.ArgumentList);
        if (info.ArgumentList.Contains("config"))
        {
            info.Environment["GIT_CONFIG_NOSYSTEM"] = "1";
            info.Environment["GIT_CONFIG_GLOBAL"] = configuration;
        }
        return BoundedProcess.Run(info, TestTiming.HangTimeout);
    });

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

    [Fact]
    public void ACraftedCommitGraphCannotMakeANonAncestorPassTheFastForwardCheck()
    {
        // The remote's work/change is a commit the local head does not descend from, so a push to it is not a fast-forward and must
        // be refused. A clone writes its own commit-graph; tampering with that cache to claim the remote commit is an ancestor must
        // not change the adapter's answer, because the check reads the real objects.
        CommitAt("2026-01-01T00:00:00Z", "readme.txt", "Initial content.\n", "init", "init", "--initial-branch=main");
        string basis = Git(repository, "rev-parse", "HEAD");

        Git(repository, "checkout", "-b", "remote-only");
        CommitAt("2026-01-01T00:00:01Z", "remote-only.txt", "A remote commit not in the local head.\n", "Remote-only commit");
        string remoteOnly = Git(repository, "rev-parse", "HEAD");

        Git(directory, "clone", "--bare", "--", repository, remote);
        Git(remote, "update-ref", "refs/heads/work/change", remoteOnly);

        Git(repository, "checkout", "main");
        Git(repository, "checkout", "-b", "work/change");
        CommitAt("2026-01-02T00:00:00Z", "addition.txt", "First local commit.\n", "First local commit");
        string middle = Git(repository, "rev-parse", "HEAD");
        CommitAt("2026-01-03T00:00:00Z", "addition.txt", "Second local commit.\n", "Second local commit");
        string head = Git(repository, "rev-parse", "HEAD");

        // Build the cache, then rewire the middle commit's first parent to the remote-only commit. git then reports the remote-only
        // commit as an ancestor of the head unless the cache is ignored; the honest graph's generation numbers do not block the walk
        // because the remote-only commit is dated before the rewired commit.
        Git(repository, "commit-graph", "write", "--reachable");
        RewireFirstParent(Path.Combine(repository, ".git", "objects", "info", "commit-graph"), middle, remoteOnly);

        var adapter = Adapter();
        ProviderRefusedException refused = Assert.Throws<ProviderRefusedException>(
            () => adapter.Prepare(repository, remote, "work/change", "main", head));
        Assert.Contains("not a known ancestor", refused.Message, StringComparison.Ordinal);
        Assert.Equal(remoteOnly, Git(remote, "rev-parse", "refs/heads/work/change"));
    }

    // Commit one file at an exact author and committer date, so the commit-graph's generation numbers are predictable.
    private void CommitAt(string date, string file, string content, string message, params string[] before)
    {
        if (before.Length > 0) Git(repository, before);
        File.WriteAllText(Path.Combine(repository, file), content);
        Git(repository, "add", file);
        GitAt(repository, date, "commit", "-m", message);
    }

    private string GitAt(string workingDirectory, string date, params string[] args)
    {
        string? author = Environment.GetEnvironmentVariable("GIT_AUTHOR_DATE");
        string? committer = Environment.GetEnvironmentVariable("GIT_COMMITTER_DATE");
        Environment.SetEnvironmentVariable("GIT_AUTHOR_DATE", date);
        Environment.SetEnvironmentVariable("GIT_COMMITTER_DATE", date);
        try { return Git(workingDirectory, args); }
        finally
        {
            Environment.SetEnvironmentVariable("GIT_AUTHOR_DATE", author);
            Environment.SetEnvironmentVariable("GIT_COMMITTER_DATE", committer);
        }
    }

    // Overwrite one commit's first-parent slot in a version-1 commit-graph so the cache misreports ancestry. The checksum trailer
    // is left stale on purpose: git still trusts the cache, which is why a checked push must not consult it.
    private static void RewireFirstParent(string path, string child, string parent)
    {
        byte[] data = File.ReadAllBytes(path);
        Assert.True(data[0] == (byte)'C' && data[1] == (byte)'G' && data[2] == (byte)'P' && data[3] == (byte)'H' && data[4] == 1);
        int hashLength = data[5] == 1 ? 20 : 32;
        int chunks = data[6];
        var offsets = new Dictionary<string, long>();
        for (int i = 0; i <= chunks; i++)
        {
            int entry = 8 + 12 * i;
            string id = System.Text.Encoding.ASCII.GetString(data, entry, 4);
            offsets[id] = System.Buffers.Binary.BinaryPrimitives.ReadInt64BigEndian(data.AsSpan(entry + 4, 8));
        }
        long names = offsets["OIDL"];
        long records = offsets["CDAT"];
        int count = (int)((records - names) / hashLength);
        int Index(string hex)
        {
            byte[] id = Convert.FromHexString(hex);
            for (int i = 0; i < count; i++)
                if (data.AsSpan((int)names + i * hashLength, hashLength).SequenceEqual(id)) return i;
            throw new InvalidOperationException("The commit is not in the graph.");
        }
        long slot = records + (long)Index(child) * (hashLength + 16) + hashLength;
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan((int)slot, 4), (uint)Index(parent));
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead | UnixFileMode.OtherRead);
        File.WriteAllBytes(path, data);
    }

    public void Dispose() => WorkspaceCleanup.Delete(directory);
}
