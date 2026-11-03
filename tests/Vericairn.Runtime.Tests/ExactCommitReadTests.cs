// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json.Nodes;
using Vericairn.Adapters.Git;
using Vericairn.Records.Estate;
using Vericairn.Records.Hashing;
using Vericairn.Records.Model;
using Vericairn.Records.Tests;
using Vericairn.Runtime.Policies;
using Vericairn.Runtime.Service;

namespace Vericairn.Runtime.Tests;

/// <summary>Exact-commit repository reads: one batch process per commit, retained across reads, with the per-file results.</summary>
public sealed class ExactCommitReadTests
{
    [Fact]
    public void BatchReadsMatchThePerFileReadOnEveryCommitAndRefusal()
    {
        using var fixture = new PolicyFixture();
        (string clone, RecordEnvelope resource) = Repository(fixture);
        byte[] large = new byte[1 << 20];
        new Random(92).NextBytes(large);
        GitHistory.Import(clone, history =>
        {
            history.Commit("Regular files",
                File("docs/guide.md", "Open311 guidance.\n"), File("README.md", "Read me.\n"), File("src/a/b/c.txt", "nested\n"),
                File("tool.sh", "#!/bin/sh\necho tool\n", "100755"), File("empty.txt", ""), File("copy.md", "Open311 guidance.\n"),
                File("unicode-é.md", "accent\n"), File("spaced name.md", "space\n"), ("100644", "large.bin", large));
            history.Commit("Changed, deleted and added", ["README.md"], File("docs/guide.md", "Changed guidance.\n"), File("docs/added.md", "added\n"));
            history.Commit("A link", [], ("120000", "link", Encoding.UTF8.GetBytes("docs/guide.md")));
            history.Gitlink("A submodule", ["link"], "vendor/sub", new string('1', 40));
            history.Commit("An unsafe path", ["vendor/sub"], File("a\\b.md", "backslash\n"));
            history.Commit("Regular again", ["a\\b.md"]);
        });
        string[] commits = GitHistory.Commits(clone);
        Assert.Equal(6, commits.Length);
        string blob = GitHistory.Run(clone, "rev-parse", commits[0] + ":README.md").Trim();
        string tree = GitHistory.Run(clone, "rev-parse", commits[0] + "^{tree}").Trim();
        string[] cases = [.. commits, commits[0].ToUpperInvariant(), commits[0][..39], new string('0', 40), new string('0', 64), blob, tree, "HEAD"];
        EstateModel model = fixture.Estate.Load();
        var source = new GitPolicySource();
        // The first pass reads through git; the second answers from retained commits. Both match the per-file reference.
        for (int pass = 0; pass < 2; pass++)
            foreach (string commit in cases)
                Same(Reference(clone, resource.Id, commit), source.Read(model, resource, commit));
        Assert.True(source.Read(model, resource, commits[0]).Complete);
        Assert.False(source.Read(model, resource, commits[2]).Complete);
        Assert.False(source.Read(model, resource, commits[3]).Complete);
        Assert.False(source.Read(model, resource, commits[4]).Complete);
        Assert.Equal(ContentHash.Sha256(large), Assert.Single(source.Read(model, resource, commits[0]).Files, f => f.Path == "large.bin").Hash);
    }

    [Fact]
    public void ABlobAboveTheBoundRefusesTheCommitWithoutReadingIt()
    {
        using var fixture = new PolicyFixture();
        (string clone, RecordEnvelope resource) = Repository(fixture);
        GitHistory.Import(clone, history => history.Commit("Oversized", File("small.md", "small\n"),
            ("100644", "oversized.bin", new byte[GitBlobBatch.MaximumBlobBytes + 1])));
        string commit = Assert.Single(GitHistory.Commits(clone));
        var started = new StrongBox<int>();
        GitFileSource.StartedProcesses.Value = started;
        try
        {
            PolicyRepository read = new GitPolicySource().Read(fixture.Estate.Load(), resource, commit);
            Assert.False(read.Complete);
            Assert.Empty(read.Files);
            Assert.InRange(started.Value, 1, 3);
        }
        finally { GitFileSource.StartedProcesses.Value = null; }
    }

    [Fact]
    public void ARetainedCommitIsNotReadAgainButMustStillResolveInItsClone()
    {
        using var fixture = new PolicyFixture();
        (string clone, RecordEnvelope resource) = Repository(fixture);
        GitHistory.Import(clone, history => history.Commit("Source", File("docs/guide.md", "guide\n"), File("docs/other.md", "other\n")));
        string commit = Assert.Single(GitHistory.Commits(clone));
        EstateModel model = fixture.Estate.Load();
        var source = new GitPolicySource();
        PolicyRepository first = source.Read(model, resource, commit);
        var started = new StrongBox<int>();
        GitFileSource.StartedProcesses.Value = started;
        try
        {
            Same(first, source.Read(model, resource, commit));
            // Only the commit's presence in the clone is confirmed again; no tree or blob is read.
            Assert.Equal(1, started.Value);
            Directory.Move(clone, clone + "-moved");
            Assert.False(source.Read(model, resource, commit).Complete);
            Directory.Move(clone + "-moved", clone);
            Same(first, source.Read(model, resource, commit));
        }
        finally { GitFileSource.StartedProcesses.Value = null; }
    }

    [Fact]
    public void RetainedCommitsStayWithinTheirByteBoundAndShareBlobs()
    {
        using var fixture = new PolicyFixture();
        (string clone, RecordEnvelope resource) = Repository(fixture);
        byte[] shared = new byte[4096], first = new byte[4096], second = new byte[4096];
        new Random(1).NextBytes(shared);
        new Random(2).NextBytes(first);
        new Random(3).NextBytes(second);
        GitHistory.Import(clone, history =>
        {
            history.Commit("First", ("100644", "shared.bin", shared), ("100644", "own.bin", first));
            history.Commit("Second", [], ("100644", "own.bin", second));
        });
        string[] commits = GitHistory.Commits(clone);
        PolicyRepository[] expected = [.. commits.Select(c => Reference(clone, resource.Id, c))];
        EstateModel model = fixture.Estate.Load();
        // Three distinct blobs fit the larger bound; the blob both commits share is held once, so both stay retained.
        var large = new GitPolicySource(3 * 4096);
        var small = new GitPolicySource(2 * 4096);
        foreach (string commit in commits)
        {
            Assert.True(large.Read(model, resource, commit).Complete);
            Assert.True(small.Read(model, resource, commit).Complete);
        }
        Assert.Equal(3 * 4096, large.RetainedBytes);
        Assert.Equal(2 * 4096, small.RetainedBytes);
        var started = new StrongBox<int>();
        GitFileSource.StartedProcesses.Value = started;
        try
        {
            Same(expected[0], large.Read(model, resource, commits[0]));
            Same(expected[1], large.Read(model, resource, commits[1]));
            Assert.Equal(2, started.Value);
            started.Value = 0;
            // Under the smaller bound the second commit released the first; the first is read again, but only its own blob.
            Same(expected[1], small.Read(model, resource, commits[1]));
            Assert.Equal(1, started.Value);
            started.Value = 0;
            Same(expected[0], small.Read(model, resource, commits[0]));
            Assert.Equal(3, started.Value);
            Assert.Equal(2 * 4096, small.RetainedBytes);
        }
        finally { GitFileSource.StartedProcesses.Value = null; }
    }

    internal static (string Clone, RecordEnvelope Resource) Repository(PolicyFixture fixture)
    {
        string clone = Path.Combine(fixture.Estate.Root, "fixture-repository");
        Directory.CreateDirectory(clone);
        GitHistory.Run(clone, "init", "--quiet");
        RecordEnvelope resource = fixture.Add("resource", new JsonObject
        {
            ["kind"] = "repository", ["name"] = "Fixture repository", ["role"] = "Exact commit source fixture.",
            ["location"] = new JsonObject { ["github"] = "Example/fixture-" + fixture.Estate.Estate.Id.ToLowerInvariant() }, ["visibility"] = "private",
        });
        RepositoryClones.Register(fixture.Estate.Estate.Id, resource.Id, clone);
        return (clone, resource);
    }

    internal static (string Mode, string Path, byte[] Bytes) File(string path, string text, string mode = "100644") => (mode, path, Encoding.UTF8.GetBytes(text));

    internal static void Same(PolicyRepository expected, PolicyRepository actual)
    {
        Assert.Equal((expected.Identity, expected.Revision, expected.Contract, expected.Complete, expected.Evidence), (actual.Identity, actual.Revision, actual.Contract, actual.Complete, actual.Evidence));
        Assert.Equal(expected.Files.Select(f => (f.Path, f.Hash, f.Available, f.IsLink)), actual.Files.Select(f => (f.Path, f.Hash, f.Available, f.IsLink)));
        foreach ((PolicyFile wanted, PolicyFile read) in expected.Files.Zip(actual.Files)) Assert.True(wanted.Bytes.AsSpan().SequenceEqual(read.Bytes.AsSpan()), wanted.Path);
    }

    // The exact-commit read as it was before batching: one rev-parse, one ls-tree and one cat-file process for each file.
    internal static PolicyRepository Reference(string clone, string identity, string commit)
    {
        PolicyRepository Missing() => new(identity, commit, PolicyBasis.RepositoryContract, false, []);
        List<PolicyFile> files = [];
        if (commit.Length is not (40 or 64) || !commit.All(char.IsAsciiHexDigit) ||
            GitFileSource.RunIsolated(clone, out byte[] resolved, "rev-parse", "--verify", "--quiet", commit + "^{commit}") != 0 || Encoding.UTF8.GetString(resolved).Trim() != commit ||
            GitFileSource.RunIsolated(clone, out byte[] tree, "ls-tree", "-r", "-z", "--full-tree", commit) != 0) return Missing();
        foreach (string entry in Encoding.UTF8.GetString(tree).Split('\0', StringSplitOptions.RemoveEmptyEntries))
        {
            int tab = entry.IndexOf('\t');
            if (tab < 0) return Missing();
            string path = entry[(tab + 1)..];
            string[] metadata = entry[..tab].Split(' ');
            if (!PolicyInputs.Path(path) || metadata.Length != 3 || metadata[1] != "blob" || metadata[0] is not ("100644" or "100755")) return Missing();
            if (GitFileSource.RunIsolated(clone, out byte[] bytes, "cat-file", "blob", metadata[2]) != 0) return Missing();
            files.Add(new(path, ContentHash.Sha256(bytes), [.. bytes]));
        }
        return new(identity, commit, PolicyBasis.RepositoryContract, true, [.. files]);
    }
}

/// <summary>Builds fixture histories through one fast-import, so thousands of files need no working tree or process each.</summary>
internal sealed class GitHistory : IDisposable
{
    private static readonly string[] Unprotected = ["-c", "core.protectNTFS=false"];
    private readonly MemoryStream stream = new();
    private int count;

    public void Dispose() => stream.Dispose();

    internal static void Import(string clone, Action<GitHistory> build)
    {
        using var history = new GitHistory();
        build(history);
        var start = new ProcessStartInfo("git") { WorkingDirectory = clone, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        foreach (string argument in Unprotected.Concat(["fast-import", "--quiet"])) start.ArgumentList.Add(argument);
        using Process process = Process.Start(start)!;
        Task<string> output = process.StandardOutput.ReadToEndAsync();
        Task<string> error = process.StandardError.ReadToEndAsync();
        history.stream.Position = 0;
        history.stream.CopyTo(process.StandardInput.BaseStream);
        process.StandardInput.Close();
        Assert.True(process.WaitForExit((int)TestTiming.HangTimeout.TotalMilliseconds));
        Assert.True(process.ExitCode == 0, error.Result + output.Result);
    }

    internal static string[] Commits(string clone) =>
        Run(clone, "rev-list", "--reverse", "refs/heads/fixture").Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    internal static string Run(string clone, params string[] arguments)
    {
        var start = new ProcessStartInfo("git") { WorkingDirectory = clone, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        foreach (string argument in Unprotected.Concat(arguments)) start.ArgumentList.Add(argument);
        using Process process = Process.Start(start)!;
        Task<string> output = process.StandardOutput.ReadToEndAsync();
        Task<string> error = process.StandardError.ReadToEndAsync();
        Assert.True(process.WaitForExit((int)TestTiming.HangTimeout.TotalMilliseconds));
        Assert.True(process.ExitCode == 0, error.Result);
        return output.Result;
    }

    internal void Commit(string message, params (string Mode, string Path, byte[] Bytes)[] files) => Commit(message, [], files);

    internal void Commit(string message, string[] deleted, params (string Mode, string Path, byte[] Bytes)[] files)
    {
        Begin(message, deleted);
        foreach ((string mode, string path, byte[] bytes) in files)
        {
            Write($"M {mode} inline {path}\n");
            Data(bytes);
        }
    }

    internal void Gitlink(string message, string[] deleted, string path, string commit)
    {
        Begin(message, deleted);
        Write($"M 160000 {commit} {path}\n");
    }

    private void Begin(string message, string[] deleted)
    {
        Write("commit refs/heads/fixture\n");
        Write($"committer Fixture <fixture@example.invalid> {1_790_000_000 + count++} +0000\n");
        Data(Encoding.UTF8.GetBytes(message));
        foreach (string path in deleted) Write($"D {path}\n");
    }

    private void Data(byte[] bytes)
    {
        Write($"data {bytes.Length}\n");
        stream.Write(bytes);
        Write("\n");
    }

    private void Write(string text) => stream.Write(Encoding.UTF8.GetBytes(text));
}
