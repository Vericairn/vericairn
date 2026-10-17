// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using Vericairn.Adapters.Git;
using Vericairn.Providers.GitHub;
using Vericairn.Runtime.Providers;

namespace Vericairn.Runtime.Tests;

public sealed class ProviderProcessTests
{
    [Theory]
    [InlineData("main")]
    [InlineData("release/1.0")]
    public void GitHubResolvesTheBuildRefAtTheProvider(string reference)
    {
        const string commit = "0123456789abcdef0123456789abcdef01234567";
        var provider = new GitHubReleaseProvider(info =>
        {
            Assert.Equal(["api", $"repos/Example/widget/commits/{Uri.EscapeDataString(reference)}", "--jq", ".sha"], info.ArgumentList);
            return new(ProcessOutcome.Exited, true, 0, commit + "\n", string.Empty);
        });
        Assert.Equal(commit, provider.ResolveCommit("Example/widget", new Dictionary<string, string> { ["ref"] = reference }));
    }

    [Theory]
    [InlineData("NotStarted", false)]
    [InlineData("TimedOut", true)]
    [InlineData("Canceled", true)]
    [InlineData("OutputLimitExceeded", true)]
    [InlineData("Failed", true)]
    public void GitNeverParsesAnIncompleteObservation(string outcome, bool started)
    {
        var adapter = new GitCommandAdapter(info =>
        {
            Assert.Equal("git", info.FileName);
            Assert.Equal("0", info.Environment["GIT_OPTIONAL_LOCKS"]);
            return Failure(outcome, started);
        });
        Assert.Throws<ProviderRefusedException>(() => adapter.IsRepository(Path.GetTempPath()));
        Assert.Throws<ProviderRefusedException>(() => adapter.Inspect(Path.GetTempPath()));
    }

    [Theory]
    [InlineData("NotStarted", false)]
    [InlineData("Canceled", false)]
    [InlineData("TimedOut", true)]
    [InlineData("Canceled", true)]
    [InlineData("OutputLimitExceeded", true)]
    [InlineData("Failed", true)]
    public void GhDistinguishesFailureToStartFromAnUnknownDispatch(string outcome, bool started)
    {
        int calls = 0;
        var provider = new GitHubReleaseProvider(info =>
        {
            Assert.Equal("gh", info.FileName);
            Assert.Equal("1", info.Environment["GH_PROMPT_DISABLED"]);
            if (++calls == 1)
            {
                Assert.Equal("auth", info.ArgumentList[0]);
                return Success();
            }

            Assert.Equal("workflow", info.ArgumentList[0]);
            return Failure(outcome, started);
        });
        if (started)
        {
            Assert.Throws<ProviderUncertainException>(() => provider.Dispatch(Request()));
        }
        else
        {
            Assert.Throws<ProviderRefusedException>(() => provider.Dispatch(Request()));
        }

        Assert.Equal(2, calls);
    }

    [Fact]
    public void APreflightTimeoutHasNoReleaseEffect()
    {
        int calls = 0;
        var provider = new GitHubReleaseProvider(_ =>
        {
            calls++;
            return new(ProcessOutcome.TimedOut, true, null, string.Empty, string.Empty);
        });
        Assert.Throws<ProviderRefusedException>(() => provider.Dispatch(Request()));
        Assert.Equal(1, calls);
    }

    [Fact]
    public void ARunLookupThatCannotStartDoesNotEraseTheEarlierDispatch()
    {
        int calls = 0;
        var provider = new GitHubReleaseProvider(_ => ++calls <= 2 ? Success() : Failure("NotStarted", false));
        Assert.Throws<ProviderUncertainException>(() => provider.Dispatch(Request()));
        Assert.Equal(3, calls);
    }

    [Theory]
    [InlineData("HTTP 403", true)]
    [InlineData("connection lost", false)]
    public void ACompletedNonzeroGhExitKeepsItsRefusedOrUncertainMeaning(string error, bool refused)
    {
        int calls = 0;
        var provider = new GitHubReleaseProvider(_ => ++calls == 1 ? Success() : new(ProcessOutcome.Exited, true, 1, string.Empty, error));
        if (refused)
        {
            Assert.Throws<ProviderRefusedException>(() => provider.Dispatch(Request()));
        }
        else
        {
            Assert.Throws<ProviderUncertainException>(() => provider.Dispatch(Request()));
        }

        Assert.Equal(2, calls);
    }

    [Fact]
    public void ASilentDispatchUsesTheBoundedRunnerAndRemainsUncertain()
    {
        int calls = 0;
        var provider = new GitHubReleaseProvider(_ => ++calls == 1 ? Success()
            : BoundedProcess.Run(ProcessHarness.Command("silent"), TimeSpan.FromMilliseconds(300), cancellationToken: TestContext.Current.CancellationToken));
        ProviderUncertainException uncertain = Assert.Throws<ProviderUncertainException>(() => provider.Dispatch(Request()));
        Assert.Contains("deadline", uncertain.Message, StringComparison.Ordinal);
        Assert.Equal(2, calls);
    }

    private static ProcessResult Success() => new(ProcessOutcome.Exited, true, 0, string.Empty, string.Empty);
    private static ProcessResult Failure(string outcome, bool started) => new(Enum.Parse<ProcessOutcome>(outcome), started, null, "partial output", "partial error");
    private static DispatchRequest Request() => new("Example/widget", new Dictionary<string, string>(), new Dictionary<string, string> { ["version"] = "1.0.0" });
}
