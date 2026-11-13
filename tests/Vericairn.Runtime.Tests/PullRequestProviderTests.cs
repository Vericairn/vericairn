// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Diagnostics;
using System.Text.Json;
using Vericairn.Providers.GitHub;
using Vericairn.Runtime.Changes;
using Vericairn.Runtime.Providers;

namespace Vericairn.Runtime.Tests;

public sealed class PullRequestProviderTests
{
    private static readonly string Head = new('a', 40);
    private static readonly string MergeCommit = new('b', 40);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void BothImplementationsOpenObserveMergeAndObserveTheExactTargetCommit(bool github)
    {
        var recorded = new RecordedGitHub();
        var memory = new MemoryPullRequestProvider { Checks = [new("tests", Head, "completed", "success")] };
        memory.SetTarget("Example/widget", new("main", MergeCommit, [new("tests", MergeCommit, "completed", "success")]));
        IPullRequestProvider provider = github ? new GitHubPullRequestProvider(recorded.Run) : memory;
        Assert.Equal(IPullRequestProvider.Contract, provider.Descriptor.Contract);
        ChangeReceipt opened = provider.Open("Example/widget", "work/change", "main", Head, "Checked change", "Review it.");
        Assert.Equal("1", opened.PullRequest);
        PullRequestObservation pull = provider.Observe("Example/widget", "1")!;
        Assert.Equal(Head, pull.Head);
        Assert.Equal("main", pull.Target);
        Assert.Empty(ChangeGate.Refusals(Head, ["tests"], pull.Checks, pull.Complete));
        ChangeReceipt merged = provider.Merge("Example/widget", "1", Head);
        Assert.Equal(MergeCommit, merged.MergeCommit);
        TargetObservation target = provider.ObserveTarget("Example/widget", "main", MergeCommit);
        Assert.Empty(ChangeGate.Refusals(MergeCommit, ["tests"], target.Checks, target.Complete));
        if (github)
        {
            string[] merge = recorded.Calls.Single(c => c[0] == "api" && c[1] == "repos/Example/widget/pulls/1/merge");
            Assert.Equal("PUT", merge[Array.IndexOf(merge, "--method") + 1]);
            Assert.Contains("sha=" + Head, merge);
            Assert.Contains("merge_method=merge", merge);
            Assert.DoesNotContain("--auto", merge);
            Assert.Contains(recorded.Calls, c => c.Any(a => a.Contains("head_sha=" + MergeCommit, StringComparison.Ordinal) && a.Contains("event=push", StringComparison.Ordinal)));
            Assert.DoesNotContain(recorded.Calls, c => c.Contains("auth"));
        }
    }

    [Theory]
    [InlineData("NotStarted", false)]
    [InlineData("TimedOut", true)]
    [InlineData("OutputLimitExceeded", true)]
    [InlineData("Canceled", true)]
    [InlineData("Failed", true)]
    public void IncompleteProviderReadsNeverBecomeSuccessfulChecks(string outcome, bool started)
    {
        var provider = new GitHubPullRequestProvider(_ => new(Enum.Parse<ProcessOutcome>(outcome), started, null, "{}", "untrusted diagnostics"));
        ProviderRefusedException error = Assert.Throws<ProviderRefusedException>(() => provider.Observe("Example/widget", "1"));
        Assert.DoesNotContain("untrusted", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("NotStarted", false)]
    [InlineData("TimedOut", true)]
    [InlineData("OutputLimitExceeded", true)]
    [InlineData("Canceled", true)]
    [InlineData("Failed", true)]
    public void MergeKeepsUnknownEffectsUncertain(string outcome, bool started)
    {
        var provider = new GitHubPullRequestProvider(_ => new(Enum.Parse<ProcessOutcome>(outcome), started, null, string.Empty, "untrusted diagnostics"));
        Exception exception = started
            ? Assert.Throws<ProviderUncertainException>(() => provider.Merge("Example/widget", "1", Head))
            : Assert.Throws<ProviderRefusedException>(() => provider.Merge("Example/widget", "1", Head));
        Assert.DoesNotContain("untrusted", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ASecondPageFailureCannotBeHiddenBySuccessfulFirstPageChecks()
    {
        var output = new RecordedGitHub();
        var provider = new GitHubPullRequestProvider(info =>
        {
            if (info.ArgumentList.Any(a => a.Contains("/check-runs?", StringComparison.Ordinal)))
            {
                Assert.Contains("--paginate", info.ArgumentList);
                Assert.Contains("--slurp", info.ArgumentList);
                return Success(JsonSerializer.Serialize(new[] {
                    new { total_count = 2, check_runs = new[] { new { name = "tests", head_sha = Head, status = "completed", conclusion = "success" } } },
                    new { total_count = 2, check_runs = new[] { new { name = "another", head_sha = Head, status = "completed", conclusion = "failure" } } },
                }));
            }
            return output.Run(info);
        });
        PullRequestObservation observed = provider.Observe("Example/widget", "1")!;
        Assert.Equal(2, observed.Checks.Count);
        Assert.NotEmpty(ChangeGate.Refusals(Head, ["tests"], observed.Checks, observed.Complete));
    }

    [Fact]
    public void ChecksForAnotherCommitAndLegacyStatusFailuresArePreserved()
    {
        var output = new RecordedGitHub();
        var provider = new GitHubPullRequestProvider(info =>
        {
            if (info.ArgumentList.Any(a => a.Contains("/status?", StringComparison.Ordinal)))
                return Success(JsonSerializer.Serialize(new[] { new { total_count = 1, sha = MergeCommit, statuses = new[] { new { context = "legacy", state = "failure" } } } }));
            return output.Run(info);
        });
        PullRequestObservation observed = provider.Observe("Example/widget", "1")!;
        Assert.Contains(observed.Checks, c => c.Name == "legacy" && c.Head == MergeCommit && c.Conclusion == "failure");
        Assert.NotEmpty(ChangeGate.Refusals(Head, ["tests"], observed.Checks, true));
    }

    [Fact]
    public void AnOpeningRefusesAPushedHeadThatMovedBeforeTheRequest()
    {
        int calls = 0;
        var provider = new GitHubPullRequestProvider(_ => { calls++; return Success(JsonSerializer.Serialize(new { sha = MergeCommit })); });
        Assert.Throws<ProviderRefusedException>(() => provider.Open("Example/widget", "work/change", "main", Head, "Change", "Body"));
        Assert.Equal(1, calls);
    }

    [Fact]
    public void AStartedOpeningWithAnUnreadableReceiptStaysUncertain()
    {
        var output = new RecordedGitHub();
        var provider = new GitHubPullRequestProvider(info => info.ArgumentList[0] == "pr" && info.ArgumentList[1] == "view"
            ? new(ProcessOutcome.TimedOut, true, null, string.Empty, string.Empty) : output.Run(info));
        Assert.Throws<ProviderUncertainException>(() => provider.Open("Example/widget", "work/change", "main", Head, "Change", "Body"));
        Assert.Contains(output.Calls, c => c[0] == "pr" && c[1] == "create");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void BothImplementationsKeepTheMergePinnedWhenTheHeadMovesAfterChecks(bool github)
    {
        var recorded = new RecordedGitHub();
        var memory = new MemoryPullRequestProvider { Checks = [new("tests", Head, "completed", "success")] };
        IPullRequestProvider provider = github ? new GitHubPullRequestProvider(recorded.Run) : memory;
        provider.Open("Example/widget", "work/change", "main", Head, "Change", "Body");
        Assert.Empty(ChangeGate.Refusals(Head, ["tests"], provider.Observe("Example/widget", "1")!.Checks, true));
        recorded.CurrentHead = MergeCommit;
        memory.Set("Example/widget", new("1", "work/change", "main", MergeCommit, "open", null, []));
        Exception error = Assert.ThrowsAny<Exception>(() => provider.Merge("Example/widget", "1", Head));
        Assert.True(error is ProviderRefusedException or ProviderUncertainException);
        Assert.Equal("open", provider.Observe("Example/widget", "1")!.State);
    }

    [Fact]
    public void TruncatedInventoriesCannotAuthorizeAMerge()
    {
        var output = new RecordedGitHub();
        var provider = new GitHubPullRequestProvider(info => info.ArgumentList.Any(a => a.Contains("/check-runs?", StringComparison.Ordinal))
            ? Success(JsonSerializer.Serialize(new[] { new { total_count = 2, check_runs = new[] { new { name = "tests", head_sha = Head, status = "completed", conclusion = "success" } } } }))
            : output.Run(info));
        Assert.Throws<ProviderRefusedException>(() => provider.Observe("Example/widget", "1"));
    }

    [Theory]
    [InlineData("name")]
    [InlineData("head")]
    public void MissingCheckMetadataCannotBecomeAQualifiedObservation(string missing)
    {
        var output = new RecordedGitHub();
        var provider = new GitHubPullRequestProvider(info => info.ArgumentList.Any(a => a.Contains("/check-runs?", StringComparison.Ordinal))
            ? Success(JsonSerializer.Serialize(new[] { new { total_count = 1, check_runs = new[] {
                new { name = missing == "name" ? "" : "tests", head_sha = missing == "head" ? "" : Head, status = "completed", conclusion = "success" } } } }))
            : output.Run(info));
        Assert.Throws<ProviderRefusedException>(() => provider.Observe("Example/widget", "1"));
    }

    [Fact]
    public void TheGitHubProviderEnqueuesOnlyThePinnedHeadThroughTheMergeQueueAndReadsItsPlace()
    {
        var calls = new List<string[]>();
        bool queued = false;
        var provider = new GitHubPullRequestProvider(info =>
        {
            string[] args = [.. info.ArgumentList];
            calls.Add(args);
            Assert.Equal("gh", info.FileName);
            Assert.Equal(["api", "graphql"], args[..2]);
            string query = args.Single(a => a.StartsWith("query=", StringComparison.Ordinal));
            if (query.Contains("enqueuePullRequest", StringComparison.Ordinal))
            {
                queued = true;
                return Success("""{"data":{"enqueuePullRequest":{"mergeQueueEntry":{"state":"QUEUED"}}}}""");
            }
            if (query.Contains("timelineItems", StringComparison.Ordinal)) return Success("""{"data":{"repository":{"pullRequest":{"timelineItems":{"nodes":[]}}}}}""");
            return Success(JsonSerializer.Serialize(new { data = new { repository = new { pullRequest = new { id = "PR_kwDOexample", number = 1, headRefOid = Head,
                isMergeQueueEnabled = true, mergeQueueEntry = queued ? new { state = "AWAITING_CHECKS", headCommit = new { oid = Head } } : null } } } }));
        });
        Assert.Equal(new MergeQueueObservation(true, "absent"), provider.ObserveQueue("Example/widget", "1"));
        Assert.Equal("1", provider.Enqueue("Example/widget", "1", Head).PullRequest);
        string[] mutation = calls.Single(c => c.Any(a => a.Contains("enqueuePullRequest", StringComparison.Ordinal)));
        Assert.Contains("id=PR_kwDOexample", mutation);
        Assert.Contains("head=" + Head, mutation);
        Assert.Contains("expectedHeadOid:$head", mutation.Single(a => a.StartsWith("query=", StringComparison.Ordinal)), StringComparison.Ordinal);
        Assert.Equal(new MergeQueueObservation(true, "queued", Head), provider.ObserveQueue("Example/widget", "1"));
        // A head that moved is never enqueued, and nothing merges directly.
        Assert.Throws<ProviderRefusedException>(() => provider.Enqueue("Example/widget", "1", MergeCommit));
        Assert.Single(calls, c => c.Any(a => a.Contains("enqueuePullRequest", StringComparison.Ordinal)));
        Assert.DoesNotContain(calls, c => c.Any(a => a.EndsWith("/merge", StringComparison.Ordinal)));
    }

    [Theory]
    [InlineData("UNMERGEABLE", null, "failed")]
    [InlineData(null, "CI_FAILURE", "failed")]
    [InlineData(null, "MERGE_CONFLICT", "failed")]
    [InlineData(null, "MANUAL", "removed")]
    [InlineData(null, "something odd!", "removed")]
    [InlineData(null, null, "absent")]
    public void TheGitHubProviderReadsAQueueEntryOrItsLatestRemovalAsTheQueuesOutcome(string? entry, string? reason, string state)
    {
        var provider = new GitHubPullRequestProvider(info =>
        {
            string query = info.ArgumentList.Single(a => a.StartsWith("query=", StringComparison.Ordinal));
            if (query.Contains("timelineItems", StringComparison.Ordinal))
                return Success(JsonSerializer.Serialize(new { data = new { repository = new { pullRequest = new { timelineItems = new { nodes = new[] { new { reason } }.Where(_ => reason is not null).ToArray() } } } } }));
            return Success(JsonSerializer.Serialize(new { data = new { repository = new { pullRequest = new { id = "PR_kwDOexample", number = 1, headRefOid = Head,
                isMergeQueueEnabled = true, mergeQueueEntry = entry is null ? null : new { state = entry, headCommit = new { oid = Head } } } } } }));
        });
        MergeQueueObservation observed = provider.ObserveQueue("Example/widget", "1");
        Assert.True(observed.Complete);
        Assert.Equal(state, observed.State);
        Assert.Equal(entry is null ? null : Head, observed.Head);
        Assert.Equal(reason switch { null => null, "something odd!" => "UNKNOWN", _ => reason }, observed.Reason);
    }

    [Fact]
    public void AGitHubQueueAnswerWithErrorsIsIncompleteAndAWrongQueueRunIsIncomplete()
    {
        var errors = new GitHubPullRequestProvider(_ => Success("""{"data":null,"errors":[{"message":"untrusted diagnostics"}]}"""));
        ProviderRefusedException refused = Assert.Throws<ProviderRefusedException>(() => errors.ObserveQueue("Example/widget", "1"));
        Assert.DoesNotContain("untrusted", refused.Message, StringComparison.Ordinal);
        var other = new GitHubPullRequestProvider(Runs("gh-readonly-queue/release/pr-1-" + Head));
        Assert.False(other.ObserveQueuedTarget("Example/widget", "main", MergeCommit).Complete);
    }

    [Fact]
    public void TheGitHubQueuedTargetReadsTheQueuesRunOfTheExactCommitBesideThePushRun()
    {
        var calls = new List<string[]>();
        var provider = new GitHubPullRequestProvider(info => { calls.Add([.. info.ArgumentList]); return Runs("gh-readonly-queue/main/pr-1-" + Head)(info); });
        TargetObservation target = provider.ObserveQueuedTarget("Example/widget", "main", MergeCommit);
        Assert.True(target.Complete);
        Assert.Contains(target.Checks, c => c.Name == "tests" && c.Conclusion == "skipped");
        Assert.Empty(ChangeGate.QueuedRefusals(MergeCommit, ["tests"], target.Checks, target.Complete));
        Assert.NotEmpty(ChangeGate.Refusals(MergeCommit, ["tests"], target.Checks, target.Complete));
        Assert.Contains(calls, c => c.Any(a => a.Contains("head_sha=" + MergeCommit, StringComparison.Ordinal) && a.Contains("event=merge_group", StringComparison.Ordinal)));
        Assert.Contains(calls, c => c.Any(a => a.Contains("head_sha=" + MergeCommit, StringComparison.Ordinal) && a.Contains("event=push", StringComparison.Ordinal)));
    }

    // The runs of the merge commit: main's push run, whose job the queue's run already passed and is skipped, and the queue's own run.
    private static Func<ProcessStartInfo, ProcessResult> Runs(string queueBranch) => info =>
    {
        string route = info.ArgumentList[1];
        if (route.Contains("/actions/runs?", StringComparison.Ordinal) && route.Contains("event=push", StringComparison.Ordinal))
            return Success(JsonSerializer.Serialize(new[] { new { total_count = 1, workflow_runs = new[] { new { id = 10, name = "CI", head_sha = MergeCommit, head_branch = "main", @event = "push", status = "completed", conclusion = "success" } } } }));
        if (route.Contains("/actions/runs?", StringComparison.Ordinal) && route.Contains("event=merge_group", StringComparison.Ordinal))
            return Success(JsonSerializer.Serialize(new[] { new { total_count = 1, workflow_runs = new[] { new { id = 11, name = "CI", head_sha = MergeCommit, head_branch = queueBranch, @event = "merge_group", status = "completed", conclusion = "success" } } } }));
        if (route.Contains("/runs/10/jobs?", StringComparison.Ordinal))
            return Success(JsonSerializer.Serialize(new[] { new { total_count = 1, jobs = new[] { new { name = "tests", head_sha = MergeCommit, status = "completed", conclusion = "skipped" } } } }));
        if (route.Contains("/runs/11/jobs?", StringComparison.Ordinal))
            return Success(JsonSerializer.Serialize(new[] { new { total_count = 1, jobs = new[] { new { name = "tests", head_sha = MergeCommit, status = "completed", conclusion = "success" } } } }));
        throw new InvalidOperationException("Unscripted provider invocation.");
    };

    [Fact]
    public void TheMemoryProviderRefusesADirectMergeWhereAQueueIsRequiredAndSettlesItsQueue()
    {
        var memory = new MemoryPullRequestProvider { Checks = [new("tests", Head, "completed", "success")], QueueRequired = true };
        memory.Open("Example/widget", "work/change", "main", Head, "Change", "Body");
        Assert.Throws<ProviderRefusedException>(() => memory.Merge("Example/widget", "1", Head));
        Assert.Throws<ProviderRefusedException>(() => memory.Enqueue("Example/widget", "1", MergeCommit));
        Assert.Equal("1", memory.Enqueue("Example/widget", "1", Head).PullRequest);
        Assert.Equal(new MergeQueueObservation(true, "queued", Head), memory.ObserveQueue("Example/widget", "1"));
        Assert.Throws<ProviderRefusedException>(() => memory.Enqueue("Example/widget", "1", Head));
        memory.SettleQueue("Example/widget", "1", "merged");
        Assert.Equal(("merged", MergeCommit), (memory.Observe("Example/widget", "1")!.State, memory.Observe("Example/widget", "1")!.MergeCommit));
        Assert.Equal(new MergeQueueObservation(true, "absent"), memory.ObserveQueue("Example/widget", "1"));
        Assert.Equal(2, memory.Effects);
    }

    private static ProcessResult Success(string output = "") => new(ProcessOutcome.Exited, true, 0, output, string.Empty);

    private sealed class RecordedGitHub
    {
        internal List<string[]> Calls { get; } = [];
        internal string CurrentHead { get; set; } = Head;
        private bool merged;

        internal ProcessResult Run(ProcessStartInfo info)
        {
            Assert.Equal("gh", info.FileName);
            string[] args = info.ArgumentList.ToArray();
            Calls.Add(args);
            if (args[0] == "pr" && args[1] == "create") return Success();
            if (args[0] == "api" && args[1] == "repos/Example/widget/pulls/1/merge")
            {
                if (!args.Contains("sha=" + CurrentHead)) return new(ProcessOutcome.Exited, true, 1, string.Empty, "Head mismatch.");
                merged = true;
                return Success();
            }
            if (args[0] == "pr" && args[1] == "view")
                return Success(JsonSerializer.Serialize(new { number = 1, headRefOid = CurrentHead, headRefName = "work/change", baseRefName = "main",
                    state = merged ? "MERGED" : "OPEN", mergeCommit = merged ? new { oid = MergeCommit } : null, isCrossRepository = false }));
            if (args[0] == "api")
            {
                if (args[1].Contains("/check-runs?", StringComparison.Ordinal))
                    return Success(JsonSerializer.Serialize(new[] { new { total_count = 1, check_runs = new[] { new { name = "tests", head_sha = Head, status = "completed", conclusion = "success" } } } }));
                if (args[1].Contains("/status?", StringComparison.Ordinal)) return Success(JsonSerializer.Serialize(new[] { new { total_count = 0, sha = Head, statuses = Array.Empty<object>() } }));
                if (args[1].Contains("/actions/runs?", StringComparison.Ordinal))
                    return Success(JsonSerializer.Serialize(new[] { new { total_count = 1, workflow_runs = new[] { new { id = 10, name = "CI", head_sha = MergeCommit, head_branch = "main", @event = "push", status = "completed", conclusion = "success" } } } }));
                if (args[1].Contains("/jobs?", StringComparison.Ordinal))
                    return Success(JsonSerializer.Serialize(new[] { new { total_count = 1, jobs = new[] { new { name = "tests", head_sha = MergeCommit, status = "completed", conclusion = "success" } } } }));
                if (args[1].Contains("/commits/", StringComparison.Ordinal)) return Success(JsonSerializer.Serialize(new { sha = Head }));
            }
            throw new InvalidOperationException("Unscripted provider invocation.");
        }
    }
}
