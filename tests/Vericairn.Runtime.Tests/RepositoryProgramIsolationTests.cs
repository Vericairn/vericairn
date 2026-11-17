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
    public void EveryFilterDriverTheConfigurationNamesIsEmptied()
    {
        using GitHardening hardening = GitHardening.Create();
        Assert.True(hardening.NeutralizeFilters("filter.lfs.clean\0filter.lfs.required\0filter.a b.C.process\0filter..smudge\0filter.clean\0"));
        var info = new ProcessStartInfo("git");
        hardening.Apply(info, ["status"]);
        string[] filters = [.. info.ArgumentList.Where(a => a.StartsWith("filter.", StringComparison.Ordinal))];
        foreach (string driver in new[] { "lfs", "a b.C", "" })
            Assert.Equal(["filter." + driver + ".clean=", "filter." + driver + ".smudge=", "filter." + driver + ".process=", "filter." + driver + ".required=false"],
                filters.Where(f => f.StartsWith("filter." + driver + ".", StringComparison.Ordinal)));
        Assert.Equal(12, filters.Length);
        Assert.Equal("status", info.ArgumentList[^1]);
        Assert.Contains("core.fsmonitor=false", info.ArgumentList);
        Assert.Contains("core.hooksPath=" + Path.Combine(hardening.Control, "hooks"), info.ArgumentList);
        Assert.Equal(Path.Combine(hardening.Control, "config"), info.Environment["GIT_CONFIG_GLOBAL"]);
        Assert.Equal("1", info.Environment["GIT_CONFIG_NOSYSTEM"]);
        Assert.Equal("0", info.Environment["GIT_CONFIG_COUNT"]);

        // A driver no option can name, or more drivers than an observation carries, are refused rather than partly emptied.
        using GitHardening unnamed = GitHardening.Create();
        Assert.False(unnamed.NeutralizeFilters("filter.a=b.clean\0"));
        using GitHardening crowded = GitHardening.Create();
        Assert.False(crowded.NeutralizeFilters(string.Concat(Enumerable.Range(0, 33).Select(i => $"filter.d{i}.clean\0"))));
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
