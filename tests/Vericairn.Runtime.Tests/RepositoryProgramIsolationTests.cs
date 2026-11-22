// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Diagnostics;
using Vericairn.Adapters.Git;
using Vericairn.Runtime.Providers;

namespace Vericairn.Runtime.Tests;

/// <summary>
/// The source adapter reads repositories Vericairn did not create. Their own configuration can name programs; observing the
/// repository runs none of them and observes exactly what it would without them.
/// </summary>
public sealed class RepositoryProgramIsolationTests
{
    [Theory]
    [InlineData(RepositoryProgramProbe.Fsmonitor)]
    [InlineData(RepositoryProgramProbe.Filter)]
    [InlineData(RepositoryProgramProbe.Signature)]
    [InlineData(RepositoryProgramProbe.Hooks)]
    [InlineData(RepositoryProgramProbe.Pager)]
    public void ObservingARepositoryRunsNoProgramItsConfigurationNames(string kind)
    {
        using var probe = new RepositoryProgramProbe(kind);
        var adapter = new GitCommandAdapter();
        RepositoryObservation before = adapter.Inspect(probe.Repository);
        ReleaseSourceObservation? releaseBefore = adapter.InspectRelease(probe.Repository);
        Assert.Equal((false, 1, 1), (before.Clean, before.Modified, before.Untracked));
        Assert.Equal(["v0.1.0"], before.Tags);
        Assert.Equal("Signed commit", before.Recent[0].Summary);

        probe.Arm();
        Assert.True(adapter.IsRepository(probe.Repository));
        RepositoryObservation after = adapter.Inspect(probe.Repository);
        ReleaseSourceObservation? releaseAfter = adapter.InspectRelease(probe.Repository);
        // The policy source's working-tree read of a registered clone goes through the same runner.
        Assert.Equal(0, GitFileSource.RunIsolated(probe.Repository, out _, "ls-files", "--cached", "--others", "--exclude-standard", "-z"));
        // So do the overlap check's reads of a registered clone.
        Assert.NotNull(adapter.TrackedPaths(probe.Repository, "main"));
        Assert.True(adapter.Reachable(probe.Repository, before.RootCommit, "main"));
        Assert.True(adapter.Precedes(probe.Repository, before.RootCommit, before.Commit));
        Assert.NotNull(adapter.ChangedPaths(probe.Repository, before.RootCommit, before.Commit));
        Assert.False(probe.Ran, $"Observing the repository ran the program its {kind} configuration names.");

        Assert.Equal((before.Commit, before.Branch, before.Remote, before.RootCommit, before.Clean, before.Modified, before.Untracked),
            (after.Commit, after.Branch, after.Remote, after.RootCommit, after.Clean, after.Modified, after.Untracked));
        Assert.Equal(before.Tags, after.Tags);
        Assert.Equal(before.Recent, after.Recent);
        Assert.NotNull(releaseBefore);
        Assert.Equal(releaseBefore, releaseAfter);

        // Plain git in the same repository does run the program, so the probe was armed.
        if (probe.Control() is bool armed) Assert.True(armed, $"Plain git did not run the {kind} program; the probe proves nothing.");
    }

    [Fact]
    public void TheWorkingTreeIsReadThroughAGitDirectoryVericairnWrites()
    {
        using var probe = new RepositoryProgramProbe(RepositoryProgramProbe.Fsmonitor);
        probe.Git("config", "core.filemode", "false");
        probe.Git("config", "core.eol", "crlf x");
        probe.Git("config", "core.sparseCheckout", "false");
        // The repository's own exclude file still applies, from the copy in the git directory Vericairn writes.
        File.WriteAllText(Path.Combine(probe.Repository, ".git", "info", "exclude"), "ignored.txt\n");
        File.WriteAllText(Path.Combine(probe.Repository, "ignored.txt"), "ignored\n");
        string[]? arguments = null;
        string? view = null, configuration = null, index = null;
        var adapter = new GitCommandAdapter(info =>
        {
            if (info.ArgumentList.Contains("status"))
            {
                arguments = [.. info.ArgumentList];
                view = arguments.Single(a => a.StartsWith("--git-dir=", StringComparison.Ordinal))["--git-dir=".Length..];
                configuration = File.ReadAllText(Path.Combine(view, "config"));
                index = info.Environment["GIT_INDEX_FILE"];
            }

            return BoundedProcess.Run(info, TestTiming.HangTimeout);
        });
        RepositoryObservation observation = adapter.Inspect(probe.Repository);

        Assert.Equal((1, 1), (observation.Modified, observation.Untracked));
        Assert.NotNull(arguments);
        Assert.StartsWith(Path.Combine(Path.GetTempPath(), "vericairn-git-"), view, StringComparison.Ordinal);
        Assert.False(Directory.Exists(view), "The working-tree read's git directory outlived the observation.");
        // The status process reads Vericairn's configuration, the repository's own index and only plain comparison settings.
        Assert.Equal("[core]\n\trepositoryformatversion = 0\n\tbare = false\n", configuration);
        Assert.EndsWith(Path.Combine("repository", ".git", "index"), index, StringComparison.Ordinal);
        Assert.Contains("core.filemode=false", arguments);
        Assert.DoesNotContain(arguments, a => a.StartsWith("core.eol", StringComparison.Ordinal));
        Assert.Contains("core.sparsecheckout=false", arguments);
    }

    [Fact]
    public void AConfigurationChangedDuringObservationIsNotReadByTheWorkingTreeRead()
    {
        using var probe = new RepositoryProgramProbe(RepositoryProgramProbe.Filter);
        RepositoryObservation before = new GitCommandAdapter().Inspect(probe.Repository);
        ReleaseSourceObservation? releaseBefore = new GitCommandAdapter().InspectRelease(probe.Repository);
        string configuration = Path.Combine(probe.Repository, ".git", "config");
        int changes = 0;
        // Just before each status process starts, after every earlier process of the observation has read the configuration, the
        // repository gains a filter driver for the file its attributes route through it.
        var adapter = new GitCommandAdapter(info =>
        {
            if (info.ArgumentList.Contains("status"))
            {
                changes++;
                File.AppendAllText(configuration, "[filter \"probe\"]\n\tclean = \"" + probe.Command + "\"\n\trequired = true\n");
                File.SetLastWriteTimeUtc(Path.Combine(probe.Repository, "widget.dat"), new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc));
            }

            return BoundedProcess.Run(info, TestTiming.HangTimeout);
        });
        RepositoryObservation after = adapter.Inspect(probe.Repository);
        ReleaseSourceObservation? releaseAfter = adapter.InspectRelease(probe.Repository);

        Assert.False(probe.Ran, "The working-tree read ran a filter driver configured after the earlier reads.");
        Assert.Equal(2, changes);
        Assert.Equal((before.Clean, before.Modified, before.Untracked), (after.Clean, after.Modified, after.Untracked));
        Assert.Equal(releaseBefore, releaseAfter);
        Assert.True(probe.Control() == true, "Plain git did not run the late filter driver; the probe proves nothing.");
    }

    [Fact]
    public void AWorkingTreeGitCannotReadIsRefusedNotObservedAsClean()
    {
        using var probe = new RepositoryProgramProbe(RepositoryProgramProbe.Fsmonitor);
        // An index git cannot read: status fails, and an observation must not count that as no changes.
        File.WriteAllBytes(Path.Combine(probe.Repository, ".git", "index"), "DIRC"u8.ToArray());
        var adapter = new GitCommandAdapter();
        ProviderRefusedException refused = Assert.Throws<ProviderRefusedException>(() => adapter.Inspect(probe.Repository));
        Assert.Contains("could not read the working tree", refused.Message, StringComparison.Ordinal);
        Assert.Null(adapter.InspectRelease(probe.Repository));
    }

    [Fact]
    public void ARepositoryAnotherAccountOwnsIsRefusedWithThatCause()
    {
        using var probe = new RepositoryProgramProbe(RepositoryProgramProbe.Fsmonitor);
        var adapter = new GitCommandAdapter(info =>
        {
            info.Environment["GIT_TEST_ASSUME_DIFFERENT_OWNER"] = "1";
            return BoundedProcess.Run(info, TestTiming.HangTimeout);
        });
        ProviderRefusedException refused = Assert.Throws<ProviderRefusedException>(() => adapter.Inspect(probe.Repository));
        Assert.Contains("owned by another account", refused.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(probe.Root, refused.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("sparse-cone", "inside")]
    [InlineData("sparse-cone", "outside")]
    [InlineData("sparse-no-cone", "inside")]
    [InlineData("sparse-no-cone", "outside")]
    [InlineData("sparse-index", "inside")]
    [InlineData("sparse-index", "outside")]
    [InlineData("linked-worktree", "inside")]
    [InlineData("split-index", "inside")]
    [InlineData("sha256", "inside")]
    public void TheWorkingTreeReadSeesWhatGitSeesOnEveryLayout(string layout, string change)
    {
        using var scratch = new ScratchRepository(layout == "sha256");
        string repository = scratch.Repository;
        switch (layout)
        {
            case "sparse-cone":
                ScratchRepository.Git(repository, "sparse-checkout", "set", "--cone", "inside");
                break;
            case "sparse-no-cone":
                ScratchRepository.Git(repository, "sparse-checkout", "set", "--no-cone", "/inside/");
                break;
            case "sparse-index":
                ScratchRepository.Git(repository, "sparse-checkout", "set", "--cone", "--sparse-index", "inside");
                break;
            case "linked-worktree":
                repository = Path.Combine(scratch.Root, "linked");
                ScratchRepository.Git(scratch.Repository, "worktree", "add", "-q", repository);
                break;
            case "split-index":
                ScratchRepository.Git(repository, "update-index", "--split-index");
                break;
        }

        // A changed file inside the patterns, or one present again outside them, where git's sparse checkout compares it.
        string changed = Path.Combine(repository, change, change + ".txt");
        Directory.CreateDirectory(Path.GetDirectoryName(changed)!);
        File.WriteAllText(changed, "changed\n");
        File.WriteAllText(Path.Combine(repository, "untracked.txt"), "untracked\n");

        RepositoryObservation observation = new GitCommandAdapter().Inspect(repository);
        (int modified, int untracked) = ScratchRepository.Status(repository);
        Assert.Equal((1, 1), (modified, untracked));
        Assert.Equal((modified, untracked), (observation.Modified, observation.Untracked));
        Assert.False(new GitCommandAdapter().InspectRelease(repository)?.Clean ?? true);
    }

    [Fact]
    public void AWorkingTreeDirectoryGitCannotOpenIsRefusedNotObservedAsClean()
    {
        if (OperatingSystem.IsWindows())
        {
            Assert.Skip("Directory permission bits are a Unix test of git's unreadable-directory warning.");
            return;
        }

        using var scratch = new ScratchRepository(false);
        string closed = Path.Combine(scratch.Repository, "closed");
        Directory.CreateDirectory(closed);
        File.WriteAllText(Path.Combine(closed, "inside.txt"), "untracked\n");
        File.SetUnixFileMode(closed, UnixFileMode.None);
        try
        {
            bool readable;
            try { readable = Directory.EnumerateFileSystemEntries(closed).Any(); }
            catch (UnauthorizedAccessException) { readable = false; }
            Assert.SkipWhen(readable, "This account reads directories regardless of their permission bits.");
            ProviderRefusedException refused = Assert.Throws<ProviderRefusedException>(() => new GitCommandAdapter().Inspect(scratch.Repository));
            Assert.Contains("could not read the working tree", refused.Message, StringComparison.Ordinal);
            Assert.Null(new GitCommandAdapter().InspectRelease(scratch.Repository));
        }
        finally
        {
            File.SetUnixFileMode(closed, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
    }

    // The overlap check's reads of a registered clone start as every hardened read does, and read grafts from a file that does not exist.
    [Fact]
    public void TheOverlapReadsStartHardened()
    {
        using GitHardening hardening = GitHardening.Create();
        ProcessStartInfo info = GitCommandAdapter.OverlapRead(Path.GetTempPath(), hardening, ["rev-parse", "HEAD"]);
        Assert.Equal("--no-pager", info.ArgumentList[0]);
        Assert.Contains("core.hooksPath=" + Path.Combine(hardening.Control, "hooks"), info.ArgumentList);
        Assert.All(GitHardening.Settings, setting => Assert.Contains(setting, info.ArgumentList));
        Assert.Equal(["rev-parse", "HEAD"], info.ArgumentList.TakeLast(2));
        Assert.Equal("1", info.Environment["GIT_CONFIG_NOSYSTEM"]);
        Assert.Equal("1", info.Environment["GIT_ATTR_NOSYSTEM"]);
        Assert.StartsWith(hardening.Control, info.Environment["GIT_CONFIG_GLOBAL"], StringComparison.Ordinal);
        string grafts = info.Environment["GIT_GRAFT_FILE"]!;
        Assert.True(Path.IsPathFullyQualified(grafts) && !File.Exists(grafts));
    }

    [Fact]
    public void TheSecretScannerAndItsReadabilityCheckRunHardened()
    {
        var calls = new List<(string File, string[] Arguments, Dictionary<string, string?> Environment)>();
        var scanner = new GitleaksScanner(info =>
        {
            calls.Add((info.FileName, [.. info.ArgumentList], new Dictionary<string, string?>(info.Environment)));
            // The scanner is current, and git cannot read the range, so the scan stops before gitleaks reads history.
            return info.FileName == "git" ? new(ProcessOutcome.Exited, true, 128, string.Empty, string.Empty) : new(ProcessOutcome.Exited, true, 0, "8.30.1", string.Empty);
        });
        PushPlan plan = new("work/change", "main", new string('a', 40), new string('b', 40), null, new string('b', 40));
        Assert.Equal("incomplete", scanner.Scan(Path.GetTempPath(), plan).Verdict);

        Dictionary<string, string?> scannerEnvironment = calls.Single(c => c.File == "gitleaks").Environment;
        Dictionary<string, string> carried = Configuration(scannerEnvironment);
        Assert.Equal("1", scannerEnvironment["GIT_CONFIG_NOSYSTEM"]);
        Assert.Equal("1", scannerEnvironment["GIT_NO_LAZY_FETCH"]);
        foreach (string setting in GitHardening.Settings)
            Assert.Equal(setting.Split('=', 2)[1], carried[setting.Split('=', 2)[0]]);
        Assert.True(carried.ContainsKey("core.hooksPath"));

        Assert.Contains(calls, c => c.File == "git");
        Assert.All(calls.Where(c => c.File == "git"), call =>
        {
            Assert.Contains("core.fsmonitor=false", call.Arguments);
            Assert.Contains("log.showSignature=false", call.Arguments);
            Assert.Contains("protocol.allow=never", call.Arguments);
            Assert.Equal("1", call.Environment["GIT_CONFIG_NOSYSTEM"]);
            Assert.Equal("false", Configuration(call.Environment)["core.fsmonitor"]);
        });
    }

    // A scan over a range that moves a submodule reads the submodule's change as its two commit ids, whatever form the clone's
    // diff.submodule asks for: neither of the scan's patch reads starts git in the submodule, whose own configuration and attributes
    // name a diff driver here, and the scan gives the verdict it gives without that setting.
    [Fact]
    public void AScanOverASubmoduleMoveRunsNoProgramTheSubmoduleNames()
    {
        using var scans = new SecretScanConfigurationTests();
        _ = scans.Start();
        string root = Path.GetDirectoryName(scans.Repository)!;
        string origin = Path.Combine(root, "origin");
        string marker = Path.Combine(root, "marker");
        Directory.CreateDirectory(origin);
        scans.Git("-C", origin, "init", "--quiet", "--initial-branch=main");
        scans.Git("-C", origin, "commit", "--quiet", "--allow-empty", "-m", "First submodule commit");
        scans.Git("-c", "protocol.file.allow=always", "submodule", "add", "--quiet", origin, "sub");
        scans.Git("commit", "--quiet", "-m", "Add the submodule");
        string basis = scans.Git("rev-parse", "HEAD");
        string submodule = Path.Combine(scans.Repository, "sub");
        File.WriteAllText(Path.Combine(submodule, "widget.txt"), "widget\n");
        scans.Git("-C", submodule, "add", "widget.txt");
        scans.Git("-C", submodule, "commit", "--quiet", "-m", "Second submodule commit");
        scans.Git("add", "sub");
        // The range moves the submodule and adds a credential beside it.
        string head = scans.Commit([("settings.txt", "credential = " + SecretScanConfigurationTests.Token() + "\n")]);
        SecretScan plain = scans.Scan(basis, head);
        Assert.Equal("findings", plain.Verdict);

        // The clone asks for a submodule's change as a diff of the submodule's files, and the submodule's own configuration and
        // attributes send every file through a diff driver's command.
        scans.Git("config", "diff.submodule", "diff");
        string modules = Path.Combine(scans.Repository, ".git", "modules", "sub");
        scans.Git("--git-dir=" + modules, "config", "diff.probe.command", "printf ran > '" + marker.Replace('\\', '/').Replace("'", "'\\''", StringComparison.Ordinal) + "'");
        Directory.CreateDirectory(Path.Combine(modules, "info"));
        File.WriteAllText(Path.Combine(modules, "info", "attributes"), "* diff=probe\n");
        SecretScan armed = scans.Scan(basis, head);

        Assert.False(File.Exists(marker), "The scan ran the diff driver the submodule's configuration names.");
        Assert.Equal((plain.Verdict, plain.FindingCount), (armed.Verdict, armed.FindingCount));
        // Plain git's patch read of the range, without the scan's submodule form, does run it, so the probe was armed.
        scans.Git("log", "-p", "--no-ext-diff", "--no-textconv", basis + ".." + head);
        Assert.True(File.Exists(marker), "Plain git did not run the submodule's diff driver; the probe proves nothing.");
    }

    // The push adapter's reads of the clone being pushed, here a checked push's patch and path reads, start as every hardened read
    // does: they run no program the clone's configuration names, and read what they read without it.
    [Fact]
    public void ThePushReadsRunNoProgramTheClonesConfigurationNames()
    {
        using var probe = new RepositoryProgramProbe(RepositoryProgramProbe.FsmonitorIndex);
        string basis = probe.Git("rev-parse", "HEAD");
        File.WriteAllText(Path.Combine(probe.Repository, "release.md"), "Release notes\n");
        probe.Git("add", "release.md");
        probe.Git("commit", "-q", "-m", "Add release notes");
        string head = probe.Git("rev-parse", "HEAD");
        var plan = new PushPlan("work/change", "main", head, basis, null, basis);
        var adapter = new GitPushAdapter();
        string? text = adapter.PublishedText(probe.Repository, plan);
        IReadOnlyDictionary<string, IReadOnlyList<string>>? paths = adapter.ChangedPaths(probe.Repository, plan);
        Assert.Contains("Release notes", text, StringComparison.Ordinal);
        Assert.NotNull(paths);
        Assert.Equal(["release.md"], paths[head]);

        probe.Arm();
        string? textAfter = adapter.PublishedText(probe.Repository, plan);
        IReadOnlyDictionary<string, IReadOnlyList<string>>? pathsAfter = adapter.ChangedPaths(probe.Repository, plan);
        Assert.False(probe.Ran, "The push reads ran the program the clone's fsmonitor configuration names.");
        Assert.Equal(text, textAfter);
        Assert.NotNull(pathsAfter);
        Assert.Equal(paths[head], pathsAfter[head]);

        // Plain git's read of a commit's paths, which loads the index, does run it, so the probe was armed.
        Assert.True(probe.Control() == true, "Plain git did not run the fsmonitor program; the probe proves nothing.");
    }

    // A committed repository with an inside/ and an outside/ directory and a file at its root, for the layout tests.
    private sealed class ScratchRepository : IDisposable
    {
        private static readonly string[] Identity = ["-c", "user.name=Test", "-c", "user.email=test@example.invalid", "-c", "commit.gpgsign=false", "-c", "core.autocrlf=false"];
        private static readonly string[] Sha256 = ["--object-format=sha256"];

        internal ScratchRepository(bool sha256)
        {
            Root = Path.Combine(Path.GetTempPath(), "vericairn-layout-" + Guid.NewGuid().ToString("N"));
            Repository = Path.Combine(Root, "repository");
            Directory.CreateDirectory(Repository);
            Git(Repository, ["init", "-q", "-b", "main", .. sha256 ? Sha256 : []]);
            foreach (string name in new[] { "inside", "outside" })
            {
                Directory.CreateDirectory(Path.Combine(Repository, name));
                File.WriteAllText(Path.Combine(Repository, name, name + ".txt"), name + "\n");
            }

            File.WriteAllText(Path.Combine(Repository, "root.txt"), "root\n");
            Git(Repository, "add", ".");
            Git(Repository, "commit", "-q", "-m", "First commit");
        }

        internal string Root { get; }

        internal string Repository { get; }

        // Plain git's own count, without writing the index, so the observation and git read the same index.
        internal static (int Modified, int Untracked) Status(string directory)
        {
            string[] lines = Git(directory, "status", "--porcelain=v1", "--untracked-files=all", "--ignore-submodules=dirty").Split('\n', StringSplitOptions.RemoveEmptyEntries);
            return (lines.Count(l => !l.StartsWith("??", StringComparison.Ordinal)), lines.Count(l => l.StartsWith("??", StringComparison.Ordinal)));
        }

        internal static string Git(string directory, params string[] arguments)
        {
            var info = new ProcessStartInfo("git") { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true, WorkingDirectory = directory };
            foreach (string argument in Identity.Concat(arguments)) info.ArgumentList.Add(argument);
            info.Environment["GIT_OPTIONAL_LOCKS"] = "0";
            ProcessResult result = BoundedProcess.Run(info, TestTiming.HangTimeout);
            Assert.True(result is { Outcome: ProcessOutcome.Exited, ExitCode: 0 }, "git " + string.Join(' ', arguments) + ": " + result.StandardError);
            return result.StandardOutput;
        }

        public void Dispose()
        {
            try
            {
                foreach (string file in Directory.EnumerateFiles(Root, "*", SearchOption.AllDirectories)) File.SetAttributes(file, FileAttributes.Normal);
                Directory.Delete(Root, recursive: true);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // Best effort.
            }
        }
    }

    private static Dictionary<string, string> Configuration(Dictionary<string, string?> environment)
    {
        // A setting given twice takes its last value, as git reads it.
        int count = int.Parse(environment["GIT_CONFIG_COUNT"]!, System.Globalization.CultureInfo.InvariantCulture);
        var settings = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (int i in Enumerable.Range(0, count)) settings[environment["GIT_CONFIG_KEY_" + i]!] = environment["GIT_CONFIG_VALUE_" + i]!;
        return settings;
    }
}
