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

    // FIX-137: a checked push reads one file at its head or its target branch's head, absent as null and never through the clone's filters.
    [Fact]
    public void APushReadsAFileAtItsHeadOrTargetOnly()
    {
        Git(repository, "init", "--initial-branch=main");
        File.WriteAllText(Path.Combine(repository, "readme.txt"), "Initial content.\n");
        Git(repository, "add", "readme.txt");
        Git(repository, "commit", "-m", "Initial content");
        Git(directory, "clone", "--bare", "--", repository, remote);
        Git(repository, "checkout", "-b", "docs/set");
        Directory.CreateDirectory(Path.Combine(repository, "docs", "releases", "1.0.0"));
        File.WriteAllText(Path.Combine(repository, "docs", "releases", "1.0.0", "manifest.json"), "{\"format\":\"vericairn.docs/1\"}\n");
        File.WriteAllText(Path.Combine(repository, ".gitattributes"), "*.json filter=rewrite\n");
        Git(repository, "add", ".");
        Git(repository, "commit", "-m", "Add the set");
        string head = Git(repository, "rev-parse", "HEAD");
        Git(repository, "config", "filter.rewrite.smudge", "echo rewritten");
        var adapter = Adapter();
        PushPlan plan = adapter.Prepare(repository, remote, "docs/set", "main", head);
        Assert.Equal("{\"format\":\"vericairn.docs/1\"}\n", adapter.FileText(repository, plan, plan.Head, "docs/releases/1.0.0/manifest.json"));
        Assert.Null(adapter.FileText(repository, plan, plan.TargetHead, "docs/releases/1.0.0/manifest.json"));
        Assert.Null(adapter.FileText(repository, plan, plan.Head, "docs/releases/1.0.0"));
        Assert.Null(adapter.FileText(repository, plan, plan.Head, "docs/releases/2.0.0/manifest.json"));
        Assert.Throws<ProviderRefusedException>(() => adapter.FileText(repository, plan, new string('e', 40), "readme.txt"));
        Assert.Throws<ProviderRefusedException>(() => adapter.FileText(repository, plan, plan.Head, "docs/../readme.txt"));
    }

    // CODE-81: a checked push reads every commit of its range, merge commits included, with its whole message, trailers included.
    [Fact]
    public void APushReadsEveryCommitMessageOfItsRangeIncludingMerges()
    {
        Git(repository, "init", "--initial-branch=main");
        File.WriteAllText(Path.Combine(repository, "readme.txt"), "Initial content.\n");
        Git(repository, "add", "readme.txt");
        Git(repository, "commit", "-m", "Initial content");
        Git(directory, "clone", "--bare", "--", repository, remote);
        Git(repository, "checkout", "-b", "work/change");
        File.WriteAllText(Path.Combine(repository, "addition.txt"), "Checked addition.\n");
        Git(repository, "add", "addition.txt");
        Git(repository, "commit", "-m", "Add checked content\n\nWhy it changed.\n\nSigned-off-by: Test <test@example.invalid>");
        string first = Git(repository, "rev-parse", "HEAD");
        Git(repository, "checkout", "main");
        File.WriteAllText(Path.Combine(repository, "other.txt"), "Other content.\n");
        Git(repository, "add", "other.txt");
        Git(repository, "commit", "-m", "Other content");
        string other = Git(repository, "rev-parse", "HEAD");
        Git(repository, "checkout", "work/change");
        Git(repository, "merge", "--no-ff", "-m", "Merge main into the change\n\nSigned-off-by: Test <test@example.invalid>", "main");
        string head = Git(repository, "rev-parse", "HEAD");
        var adapter = Adapter();
        PushPlan plan = adapter.Prepare(repository, remote, "work/change", "main", head);
        IReadOnlyList<PushedCommit> commits = adapter.CommitMessages(repository, plan)!;
        // The remote's main is still the initial commit, so the range holds both sides of the merge and the merge itself.
        Assert.Equal(new[] { head, other, first }.Order(StringComparer.Ordinal), commits.Select(c => c.Commit).Order(StringComparer.Ordinal));
        Assert.Equal("Merge main into the change\n\nSigned-off-by: Test <test@example.invalid>\n", Assert.Single(commits, c => c.Commit == head).Message);
        Assert.Equal("Add checked content\n\nWhy it changed.\n\nSigned-off-by: Test <test@example.invalid>\n", Assert.Single(commits, c => c.Commit == first).Message);
        // FIX-137: each commit's paths, read on their own, a clean merge naming none, since none differs from every one of its parents.
        IReadOnlyDictionary<string, IReadOnlyList<string>> paths = adapter.ChangedPaths(repository, plan)!;
        Assert.Equal(new[] { head, other, first }.Order(StringComparer.Ordinal), paths.Keys.Order(StringComparer.Ordinal));
        Assert.Equal(["addition.txt"], paths[first]);
        Assert.Equal(["other.txt"], paths[other]);
        Assert.Empty(paths[head]);
    }

    // FIX-137: a path is kept exactly as git names it with -z: a directory whose name begins with a line break is not docs/.
    [Fact]
    public void APathIsReadExactlyAsGitNamesIt()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Windows file names cannot hold a line break.");
        Git(repository, "init", "--initial-branch=main");
        File.WriteAllText(Path.Combine(repository, "readme.txt"), "Initial content.\n");
        Git(repository, "add", "readme.txt");
        Git(repository, "commit", "-m", "Initial content");
        Git(directory, "clone", "--bare", "--", repository, remote);
        Git(repository, "checkout", "-b", "docs/set");
        Directory.CreateDirectory(Path.Combine(repository, "\ndocs"));
        File.WriteAllText(Path.Combine(repository, "\ndocs", "code.cs"), "class Code { }\n");
        Directory.CreateDirectory(Path.Combine(repository, "docs"));
        File.WriteAllText(Path.Combine(repository, "docs", "guide.md"), "A guide.\n");
        Git(repository, "add", "--", "\ndocs/code.cs");
        Git(repository, "commit", "-m", "Add code under a line-break directory");
        string first = Git(repository, "rev-parse", "HEAD");
        Git(repository, "add", "--", "docs/guide.md");
        Git(repository, "commit", "-m", "Add a guide");
        string head = Git(repository, "rev-parse", "HEAD");
        var adapter = Adapter();
        PushPlan plan = adapter.Prepare(repository, remote, "docs/set", "main", head);
        IReadOnlyDictionary<string, IReadOnlyList<string>> paths = adapter.ChangedPaths(repository, plan)!;
        Assert.Equal(["\ndocs/code.cs"], paths[first]);
        Assert.Equal(["docs/guide.md"], paths[head]);
    }

    // CODE-81: a commit the target branch already holds is in the repository already, so a pushed branch that takes in a newer target
    // carries none of its commits to the message check, such as the merge commits a checked merge creates without the push's trailer.
    [Fact]
    public void AMessageReadLeavesOutTheCommitsTheTargetAlreadyHolds()
    {
        Git(repository, "init", "--initial-branch=main");
        File.WriteAllText(Path.Combine(repository, "readme.txt"), "Initial content.\n");
        Git(repository, "add", "readme.txt");
        Git(repository, "commit", "-m", "Initial content");
        Git(directory, "clone", "--bare", "--", repository, remote);
        Git(repository, "checkout", "-b", "work/change");
        File.WriteAllText(Path.Combine(repository, "addition.txt"), "Checked addition.\n");
        Git(repository, "add", "addition.txt");
        Git(repository, "commit", "-m", "Add checked content\n\nSigned-off-by: Test <test@example.invalid>");
        string pushed = Git(repository, "rev-parse", "HEAD");
        Git(repository, "push", remote, "work/change");
        Git(repository, "checkout", "main");
        File.WriteAllText(Path.Combine(repository, "other.txt"), "Other content.\n");
        Git(repository, "add", "other.txt");
        Git(repository, "commit", "-m", "Merge pull request #2 from Example/work/other");
        string landed = Git(repository, "rev-parse", "HEAD");
        Git(repository, "push", remote, "main");
        Git(repository, "checkout", "work/change");
        Git(repository, "merge", "--no-ff", "-m", "Take in main\n\nSigned-off-by: Test <test@example.invalid>", "main");
        string head = Git(repository, "rev-parse", "HEAD");
        var adapter = Adapter();
        PushPlan plan = adapter.Prepare(repository, remote, "work/change", "main", head);
        Assert.Equal((pushed, landed), (plan.Base, plan.TargetHead));
        Assert.Equal(head, Assert.Single(adapter.CommitMessages(repository, plan)!).Commit);
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

    // CODE-81: a clone's info/grafts rewrites parents as replace objects do, and turning replace objects off does not disable it. A graft
    // making the head's parent the base must not hide the middle commit, whose message carries a blocked term, from the message read,
    // while the push, which reads the real objects, would still deliver it.
    [Fact]
    public void AGraftCannotHideACommitFromTheMessageRead()
    {
        CommitAt("2026-01-01T00:00:00Z", "readme.txt", "Initial content.\n", "init", "init", "--initial-branch=main");
        string basis = Git(repository, "rev-parse", "HEAD");
        Git(directory, "clone", "--bare", "--", repository, remote);
        Git(repository, "checkout", "-b", "work/change");
        CommitAt("2026-01-02T00:00:00Z", "addition.txt", "First local commit.\n", "Add the widget\n\nWritten in a Tern session.");
        string middle = Git(repository, "rev-parse", "HEAD");
        CommitAt("2026-01-03T00:00:00Z", "addition.txt", "Second local commit.\n", "Tidy the widget");
        string head = Git(repository, "rev-parse", "HEAD");
        File.WriteAllText(Path.Combine(repository, ".git", "info", "grafts"), head + " " + basis + "\n");
        Assert.Equal(basis, Git(repository, "rev-parse", "HEAD^"));

        var adapter = Adapter();
        PushPlan plan = adapter.Prepare(repository, remote, "work/change", "main", head);
        IReadOnlyList<PushedCommit> commits = adapter.CommitMessages(repository, plan)!;
        Assert.Equal(new[] { head, middle }.Order(StringComparer.Ordinal), commits.Select(c => c.Commit).Order(StringComparer.Ordinal));
        Assert.Equal(new[] { head, middle }.Order(StringComparer.Ordinal), adapter.ChangedPaths(repository, plan)!.Keys.Order(StringComparer.Ordinal));
        Assert.Contains("Written in a Tern session.", adapter.PublishedText(repository, plan), StringComparison.Ordinal);
        var settings = new Vericairn.Records.Estate.CommitMessages.Settings(Vericairn.Records.Estate.CommitMessages.Refuse, [],
            [new("terms", Vericairn.Records.Estate.BlockedTerms.TermsOf(System.Text.Json.JsonSerializer.SerializeToElement(new { terms = TernOnly })))], []);
        Assert.Equal(middle, Vericairn.Records.Estate.CommitMessages.Check(settings, commits.Select(c => (c.Commit, c.Message))).Refusal?.Commit);
    }

    // The same graft class against the fast-forward check: a graft claiming the remote branch's head as the middle commit's parent must
    // not make a non-ancestor pass, so the leased push never overwrites the remote branch.
    [Fact]
    public void AGraftCannotMakeANonAncestorPassTheFastForwardCheck()
    {
        CommitAt("2026-01-01T00:00:00Z", "readme.txt", "Initial content.\n", "init", "init", "--initial-branch=main");
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
        File.WriteAllText(Path.Combine(repository, ".git", "info", "grafts"), middle + " " + remoteOnly + "\n");
        Assert.Equal(remoteOnly, Git(repository, "rev-parse", middle + "^"));

        var adapter = Adapter();
        ProviderRefusedException refused = Assert.Throws<ProviderRefusedException>(() => adapter.Prepare(repository, remote, "work/change", "main", head));
        Assert.Contains("not a known ancestor", refused.Message, StringComparison.Ordinal);
        Assert.Equal(remoteOnly, Git(remote, "rev-parse", "refs/heads/work/change"));
    }

    private static readonly string[] TernOnly = ["Tern"];

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
        // git writes the commit-graph read-only; clear that (the ReadOnly attribute on Windows, the owner write bit elsewhere) before rewriting it.
        File.SetAttributes(path, File.GetAttributes(path) & ~FileAttributes.ReadOnly);
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead | UnixFileMode.OtherRead);
        File.WriteAllBytes(path, data);
    }

    public void Dispose() => WorkspaceCleanup.Delete(directory);
}
