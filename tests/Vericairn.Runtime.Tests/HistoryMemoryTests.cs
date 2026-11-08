// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using Vericairn.Adapters.Git;
using Vericairn.Protocol;
using Vericairn.Records.Identity;
using Vericairn.Records.Model;
using Vericairn.Records.Tests;
using Vericairn.Runtime.Providers;
using Vericairn.Runtime.Service;
using Vericairn.Runtime.Store;

namespace Vericairn.Runtime.Tests;

/// <summary>
/// The machine's memory of an estate's history and what adoption's check reads from git (design/signed-history.md §§ 7 and 10; OPS-07):
/// the memory refuses a link, a place inside the estate and an oversized file; the main line is the branch the origin's HEAD names, else
/// main, else master; and the check names each anchored record's adding commit and its date, points out a record dated well before it,
/// and lists record files no commit holds.
/// </summary>
public class HistoryMemoryTests
{
    private static readonly string[] GitIdentity = ["-c", "user.name=Test", "-c", "user.email=test@example.invalid", "-c", "commit.gpgsign=false"];

    [Fact]
    public void TheMemoryRefusesALinkAPlaceInsideTheEstateAndAnOversizedFile()
    {
        using var estate = new TestEstate(3);
        string home = Path.Combine(Path.GetTempPath(), "vericairn-memory-home-" + Ulid.NewUlid().Value);
        string outside = Path.Combine(Path.GetTempPath(), "vericairn-memory-outside-" + Ulid.NewUlid().Value);
        try
        {
            Assert.Throws<IOException>(() => new HistoryMemory(estate.Root, estate.Estate.Id, "repository-synced", Path.Combine(estate.Root, "home")));

            var memory = new HistoryMemory(estate.Root, estate.Estate.Id, "repository-synced", home);
            Assert.Null(memory.Read());
            var verified = new HistoryMemory.Verified(Ulid.NewUlid().Value, "sha256:" + new string('a', 64), "records/history-checkpoint/2026-11/x.json", "main", new string('b', 40), "2026-11-08T00:00:00Z");
            memory.Write(new HistoryMemory.Remembered(estate.Estate.Id, verified, null));
            Assert.Equal(verified, memory.Read()!.Checkpoint);
            string file = Directory.EnumerateFiles(home, "history.json", SearchOption.AllDirectories).Single();

            // A value over the size limit is never written, and the memory kept stays as it was.
            Assert.Throws<JsonException>(() => memory.Write(new HistoryMemory.Remembered(estate.Estate.Id, verified with { Path = new string('p', 20_000) }, null)));
            Assert.Equal(verified, memory.Read()!.Checkpoint);

            // An oversized file is refused when read.
            File.WriteAllText(file, "{\"estate\":\"" + new string('e', 20_000) + "\"}");
            Assert.Throws<JsonException>(() => memory.Read());

            // So is a link, whatever it leads to.
            Directory.CreateDirectory(outside);
            string target = Path.Combine(outside, "history.json");
            File.Delete(file);
            File.WriteAllText(target, "{}");
            bool linked = true;
            try { File.CreateSymbolicLink(file, target); }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or PlatformNotSupportedException) { linked = false; }
            if (linked)
            {
                Assert.Throws<IOException>(() => memory.Read());
                Assert.Throws<IOException>(() => memory.Write(new HistoryMemory.Remembered(estate.Estate.Id, verified, null)));
            }
        }
        finally
        {
            if (Directory.Exists(home)) Directory.Delete(home, recursive: true);
            if (Directory.Exists(outside)) Directory.Delete(outside, recursive: true);
        }
    }

    [Fact]
    public void TheMainLineIsTheBranchTheOriginsHeadNamesElseMainElseMaster()
    {
        TestPrerequisites.RequireGit();
        string root = Path.Combine(Path.GetTempPath(), "vericairn-mainline-" + Ulid.NewUlid().Value);
        Directory.CreateDirectory(root);
        try
        {
            var git = new GitCommandAdapter();
            Git(root, "init", "-q", "-b", "master");
            Assert.Null(git.MainLine(root));
            File.WriteAllText(Path.Combine(root, "a.txt"), "a\n");
            Git(root, "add", "-A");
            Git(root, [.. GitIdentity, "commit", "-q", "-m", "First"]);
            string first = Git(root, "rev-parse", "HEAD");
            Assert.Equal(new EstateMainLine("master", first), git.MainLine(root));

            Git(root, "branch", "main");
            File.WriteAllText(Path.Combine(root, "b.txt"), "b\n");
            Git(root, "add", "-A");
            Git(root, [.. GitIdentity, "commit", "-q", "-m", "Second on master"]);
            Assert.Equal(new EstateMainLine("main", first), git.MainLine(root));

            // The origin's HEAD names the main line when that branch is held locally; a checkout elsewhere changes nothing.
            Git(root, "branch", "trunk");
            string trunk = Git(root, "rev-parse", "trunk");
            Git(root, "symbolic-ref", "refs/remotes/origin/HEAD", "refs/remotes/origin/trunk");
            Assert.Equal(new EstateMainLine("trunk", trunk), git.MainLine(root));
            Git(root, "checkout", "-q", "-b", "feature");
            Assert.Equal(new EstateMainLine("trunk", trunk), git.MainLine(root));
            Git(root, "symbolic-ref", "refs/remotes/origin/HEAD", "refs/remotes/origin/elsewhere");
            Assert.Equal(new EstateMainLine("main", first), git.MainLine(root));
            Assert.Equal("a\n", System.Text.Encoding.UTF8.GetString(git.FileAtCommit(root, first, "a.txt")!));
            Assert.Null(git.FileAtCommit(root, first, "b.txt"));
            Assert.Null(git.FileAtCommit(root, first, "--output=x"));
        }
        finally
        {
            foreach (string file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)) File.SetAttributes(file, FileAttributes.Normal);
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void AdoptionsCheckNamesAddingCommitsAndPointsOutBackdatedAndUncommittedRecords()
    {
        TestPrerequisites.RequireGit();
        using var f = new PolicyFixture();
        string root = f.Estate.Root;
        Git(root, "init", "-q", "-b", "main");
        Git(root, "config", "core.autocrlf", "false");
        f.Estate.SetClock(DateTimeOffset.UtcNow.AddDays(-30));
        RecordEnvelope backdated = f.Add("work-order", JsonNode.Parse(HistoryFixture.Order("OLD-1"))!.AsObject(), sign: false);
        Git(root, "add", "-A");
        Git(root, [.. GitIdentity, "commit", "-q", "-m", "The estate"]);
        f.Estate.SetClock(DateTimeOffset.UtcNow.AddMinutes(-1));
        RecordEnvelope uncommitted = f.Add("work-order", JsonNode.Parse(HistoryFixture.Order("NEW-1"))!.AsObject(), sign: false);
        using EstateService api = EstateService.Open(root);
        api.Providers = new ProviderRegistry();
        api.Providers.Register(new GitCommandAdapter());

        (HistoryCheckpointResponse? plan, Problem? problem) = api.HistoryCheckpoint(new HistoryCheckpointRequest(null, Adopt: true, Check: true));
        Assert.True(problem is null, problem?.Message);
        HistoryRecordItem old = Assert.Single(plan!.Anchored, a => a.Id == backdated.Id);
        Assert.NotNull(old.Commit);
        Assert.NotNull(old.CommitAt);
        Assert.Contains("more than a day before the commit that added it", old.Note, StringComparison.Ordinal);
        HistoryRecordItem fresh = Assert.Single(plan.Anchored, a => a.Id == uncommitted.Id);
        Assert.Null(fresh.Commit);
        Assert.Contains("no commit holds this file yet", fresh.Note, StringComparison.Ordinal);
        Assert.Equal([uncommitted.ExpectedPath], plan.Uncommitted);
        Assert.Null(plan.Unavailable);
        Assert.StartsWith("sha256:", plan.PlanHash, StringComparison.Ordinal);
    }

    private static string Git(string directory, params string[] arguments)
    {
        var info = new ProcessStartInfo("git") { WorkingDirectory = directory, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        foreach (string argument in arguments) info.ArgumentList.Add(argument);
        using Process process = Process.Start(info)!;
        string output = process.StandardOutput.ReadToEnd();
        string error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        Assert.True(process.ExitCode == 0, $"git {string.Join(' ', arguments)}: {error}");
        return output.Trim();
    }
}
