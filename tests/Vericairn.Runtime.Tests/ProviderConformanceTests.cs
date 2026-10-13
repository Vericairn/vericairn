// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Diagnostics;
using Vericairn.Records.Identity;
using Vericairn.Records.Model;
using Vericairn.Runtime.Providers;

namespace Vericairn.Runtime.Tests;

/// <summary>The git adapter contract, satisfied by the git command and by a recorded replay: two genuinely different implementations, one suite.</summary>
public abstract class SourceAdapterConformance
{
    /// <summary>The implementation under test.</summary>
    protected abstract ISourceAdapter Adapter { get; }

    /// <summary>A repository with commits, a tag, an uncommitted change and an untracked file.</summary>
    protected abstract string RepositoryPath { get; }

    /// <summary>A directory that is not a repository.</summary>
    protected abstract string NotARepositoryPath { get; }

    [Fact]
    public void TheDescriptorNamesTheContract()
    {
        Assert.Equal(ISourceAdapter.Contract, Adapter.Descriptor.Contract);
        Assert.Contains("inspect", Adapter.Descriptor.Operations);
        Assert.Contains(Adapter.Descriptor.Status, ProviderDescriptor.Statuses);
    }

    [Fact]
    public void ADirectoryThatIsNotARepositoryIsRefusedNotInvented()
    {
        Assert.False(Adapter.IsRepository(NotARepositoryPath));
        Assert.Throws<ProviderRefusedException>(() => Adapter.Inspect(NotARepositoryPath));
    }

    [Fact]
    public void ARepositoryIsObservedWithoutBeingChanged()
    {
        Assert.True(Adapter.IsRepository(RepositoryPath));
        RepositoryObservation observation = Adapter.Inspect(RepositoryPath);
        Assert.Matches("^[0-9a-f]{40}$", observation.Commit);
        Assert.Matches("^[0-9a-f]{40}$", observation.RootCommit);
        Assert.Equal(observation.Modified == 0 && observation.Untracked == 0, observation.Clean);
        Assert.False(observation.Clean);
        Assert.Equal(1, observation.Modified);
        Assert.Equal(1, observation.Untracked);
        Assert.Equal(observation.Tags.Order(StringComparer.Ordinal), observation.Tags);
        Assert.Contains("v0.1.0", observation.Tags);
        Assert.True(observation.Recent.Count is > 0 and <= 10);
        Assert.Equal(observation.Commit, observation.Recent[0].Commit);
        Assert.True(RecordParser.IsTimestamp(observation.AsOf));
        Assert.All(observation.Recent, c => Assert.True(RecordParser.IsTimestamp(c.At)));
        RepositoryObservation again = Adapter.Inspect(RepositoryPath);
        Assert.Equal((observation.Commit, observation.Branch, observation.Remote, observation.RootCommit, observation.Clean, observation.Modified, observation.Untracked), (again.Commit, again.Branch, again.Remote, again.RootCommit, again.Clean, again.Modified, again.Untracked));
        Assert.Equal(observation.Tags, again.Tags);
        Assert.Equal(observation.Recent, again.Recent);
    }
}

/// <summary>The git command adapter against a real repository made for the test.</summary>
public sealed class GitCommandAdapterTests : SourceAdapterConformance, IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "vericairn-git-" + Ulid.NewUlid().Value);

    public GitCommandAdapterTests()
    {
        Directory.CreateDirectory(RepositoryPath);
        Directory.CreateDirectory(NotARepositoryPath);
        Git("init", "-q", "-b", "main");
        File.WriteAllText(Path.Combine(RepositoryPath, "README.md"), "widget\n");
        Git("add", "README.md");
        Git("commit", "-q", "-m", "First commit");
        Git("tag", "v0.1.0");
        File.WriteAllText(Path.Combine(RepositoryPath, "widget.txt"), "widget\n");
        Git("add", "widget.txt");
        Git("commit", "-q", "-m", "Add the widget");
        File.AppendAllText(Path.Combine(RepositoryPath, "README.md"), "changed\n");
        File.WriteAllText(Path.Combine(RepositoryPath, "notes.txt"), "untracked\n");
    }

    protected override ISourceAdapter Adapter { get; } = new GitCommandAdapter();

    protected override string RepositoryPath => Path.Combine(root, "repo");

    protected override string NotARepositoryPath => Path.Combine(root, "plain");

    [Fact]
    public void ARepositoryWithoutCommitsIsRefusedWithTheNextStep()
    {
        string empty = Path.Combine(root, "empty");
        Directory.CreateDirectory(empty);
        var info = new ProcessStartInfo("git") { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, WorkingDirectory = empty };
        info.ArgumentList.Add("init");
        info.ArgumentList.Add("-q");
        using (Process process = Process.Start(info)!)
        {
            process.WaitForExit();
        }

        Assert.False(Adapter.IsRepository(empty));
        ProviderRefusedException refused = Assert.Throws<ProviderRefusedException>(() => Adapter.Inspect(empty));
        Assert.Contains("no commits", refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TheObservationNeverContainsTheMachinePath()
    {
        RepositoryObservation observation = Adapter.Inspect(RepositoryPath);
        string text = System.Text.Json.JsonSerializer.Serialize(observation);
        Assert.DoesNotContain(root, text, StringComparison.Ordinal);
        Assert.Null(observation.Remote);
    }

    public void Dispose()
    {
        try
        {
            foreach (string file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
            {
                File.SetAttributes(file, FileAttributes.Normal);
            }

            Directory.Delete(root, recursive: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Best effort.
        }
    }

    private void Git(params string[] arguments)
    {
        var info = new ProcessStartInfo("git") { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, WorkingDirectory = RepositoryPath };
        info.ArgumentList.Add("-c");
        info.ArgumentList.Add("user.name=Test");
        info.ArgumentList.Add("-c");
        info.ArgumentList.Add("user.email=test@example.invalid");
        info.ArgumentList.Add("-c");
        info.ArgumentList.Add("commit.gpgsign=false");
        foreach (string argument in arguments)
        {
            info.ArgumentList.Add(argument);
        }

        using Process process = Process.Start(info)!;
        string error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        Assert.True(process.ExitCode == 0, $"git {string.Join(' ', arguments)}: {error}");
    }
}

/// <summary>The recorded adapter: the same contract from fixtures, qualifying no repository.</summary>
public sealed class RecordedSourceAdapterTests : SourceAdapterConformance
{
    private const string Commit = "0123456789abcdef0123456789abcdef01234567";
    private const string Root = "fedcba9876543210fedcba9876543210fedcba98";

    public RecordedSourceAdapterTests()
    {
        RepositoryPath = Path.Combine(Path.GetTempPath(), "vericairn-recorded-repo");
        NotARepositoryPath = Path.Combine(Path.GetTempPath(), "vericairn-recorded-plain");
        Adapter = new RecordedSourceAdapter(new Dictionary<string, RepositoryObservation>(StringComparer.Ordinal)
        {
            [Path.GetFullPath(RepositoryPath)] = new(Commit, "main", "https://github.com/Example/widget.git", Root, false, 1, 1, ["v0.1.0", "v0.1.1"], [new RecentCommit(Commit, "2026-10-13T12:00:01Z", "Add the widget"), new RecentCommit(Root, "2026-10-13T12:00:00Z", "First commit")], "2026-10-13T12:00:02Z"),
        });
    }

    protected override ISourceAdapter Adapter { get; }

    protected override string RepositoryPath { get; }

    protected override string NotARepositoryPath { get; }
}

/// <summary>The GitHub Releases provider contract against the in-memory implementation, and against GitHub itself when a live run is asked for.</summary>
public class ReleaseProviderConformanceTests
{
    private const string Repository = "Example/widget";

    [Fact]
    public void TheFakeProviderBehavesAsGitHubWould()
    {
        var provider = new FakeReleaseProvider();
        Assert.Equal(IReleaseProvider.Contract, provider.Descriptor.Contract);
        Assert.Null(provider.ObserveRun(Repository, "1"));
        Assert.Null(provider.ObserveRelease(Repository, "v9.9.9"));

        DispatchReceipt receipt = provider.Dispatch(new DispatchRequest(Repository, "release.yml", "main", new Dictionary<string, string>(StringComparer.Ordinal) { ["version"] = "0.2.0" }));
        Assert.NotNull(receipt.RunId);
        Assert.True(RecordParser.IsTimestamp(receipt.At));
        RunObservation run = provider.ObserveRun(Repository, receipt.RunId)!;
        Assert.Equal("in_progress", run.Status);
        Assert.Null(run.Conclusion);
        Assert.Null(provider.ObserveRelease(Repository, "v0.2.0"));

        provider.CompleteRun(Repository, receipt.RunId, true, "0.2.0");
        Assert.Equal("success", provider.ObserveRun(Repository, receipt.RunId)!.Conclusion);
        ReleaseObservation release = provider.ObserveRelease(Repository, "v0.2.0")!;
        Assert.True(release.Draft);
        Assert.Null(release.PublishedAt);
        Assert.Equal(run.HeadSha, release.TargetCommit);
        Assert.Equal(9, release.Assets.Count);
        Assert.All(release.Assets.Where(a => a.Name != "SHA256SUMS"), a => Assert.StartsWith("sha256:", a.Hash, StringComparison.Ordinal));
        Assert.Contains("--draft=false", provider.ManualPublicationCommand(Repository, "v0.2.0"), StringComparison.Ordinal);
        Assert.Contains("v0.2.0", provider.ManualPublicationCommand(Repository, "v0.2.0"), StringComparison.Ordinal);

        provider.Publish(Repository, "v0.2.0");
        ReleaseObservation published = provider.ObserveRelease(Repository, "v0.2.0")!;
        Assert.False(published.Draft);
        Assert.True(RecordParser.IsTimestamp(published.PublishedAt));

        provider.NextDispatch = FakeReleaseProvider.DispatchBehavior.RefuseAuthentication;
        Assert.Throws<ProviderRefusedException>(() => provider.Dispatch(new DispatchRequest(Repository, "release.yml", "main", new Dictionary<string, string>(StringComparer.Ordinal))));
        provider.NextDispatch = FakeReleaseProvider.DispatchBehavior.Uncertain;
        Assert.Null(provider.Dispatch(new DispatchRequest(Repository, "release.yml", "main", new Dictionary<string, string>(StringComparer.Ordinal))).RunId);
    }

    [Fact]
    public void TheGitHubCliProviderObservesAPublishedReleaseWhenALiveRunIsAskedFor()
    {
        Assert.SkipUnless(Environment.GetEnvironmentVariable("VERICAIRN_LIVE_GITHUB") == "1", "Set VERICAIRN_LIVE_GITHUB=1 with a logged-in GitHub CLI to observe Vericairn/vericairn v0.1.0 for real.");
        var provider = new GhReleaseProvider();
        Assert.Equal(IReleaseProvider.Contract, provider.Descriptor.Contract);
        ReleaseObservation release = provider.ObserveRelease("Vericairn/vericairn", "v0.1.0")!;
        Assert.False(release.Draft);
        Assert.NotNull(release.PublishedAt);
        Assert.Contains(release.Assets, a => a.Name == "vericairn-0.1.0-osx-arm64.tar.gz" && a.Hash is not null);
        Assert.Null(provider.ObserveRelease("Vericairn/vericairn", "v0.0.0-nope"));
    }
}
