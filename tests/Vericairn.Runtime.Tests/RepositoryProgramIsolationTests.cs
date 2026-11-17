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

        (_, string[] gitArguments, Dictionary<string, string?> gitEnvironment) = calls.Single(c => c.File == "git");
        Assert.Contains("core.fsmonitor=false", gitArguments);
        Assert.Contains("log.showSignature=false", gitArguments);
        Assert.Contains("protocol.allow=never", gitArguments);
        Assert.Equal("1", gitEnvironment["GIT_CONFIG_NOSYSTEM"]);
    }

    private static Dictionary<string, string> Configuration(Dictionary<string, string?> environment)
    {
        int count = int.Parse(environment["GIT_CONFIG_COUNT"]!, System.Globalization.CultureInfo.InvariantCulture);
        return Enumerable.Range(0, count).ToDictionary(i => environment["GIT_CONFIG_KEY_" + i]!, i => environment["GIT_CONFIG_VALUE_" + i]!, StringComparer.Ordinal);
    }
}
