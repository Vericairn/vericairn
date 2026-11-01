// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Diagnostics;
using System.Net;
using Vericairn.Providers.GitHub.Releases;
using Vericairn.Records.Updates;
using Vericairn.Sdk;

namespace Vericairn.Runtime.Tests;

public sealed class ReleaseUpdateDeadlineTests : IDisposable
{
    private static readonly TimeSpan Ceiling = TimeSpan.FromSeconds(5);
    private readonly string home = Directory.CreateTempSubdirectory("vericairn-update-deadline-").FullName;
    private readonly ClientClock clock = new(new DateTimeOffset(2026, 10, 20, 12, 0, 0, TimeSpan.Zero));

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UnansweredSourceReturnsCachedAnswerAndReservesTheDayBeforeTheRequest(bool ignoresCancellation)
    {
        var store = SeedCache();
        ReleaseCheckState before = store.ReadState();
        var answer = new TaskCompletionSource<PublishedRelease?>();
        ReleaseCheckState? atRequest = null;
        CancellationToken requested = default;
        var source = new Source(token =>
        {
            atRequest = store.ReadState();
            requested = token;
            return ignoresCancellation ? answer.Task : answer.Task.WaitAsync(token);
        });
        var updates = new ReleaseUpdates(store, source, clock);
        try
        {
            Assert.Equal("v9.2.0", (await WithinDeadline(updates))?.Tag);
            Assert.NotNull(atRequest);
            Assert.Equal(clock.GetUtcNow(), atRequest.LastAttemptAt);
            Assert.Equal(before.LastSuccessAt, atRequest.LastSuccessAt);
            Assert.Equal(before.Tag, atRequest.Tag);
            Assert.True(requested.CanBeCanceled);
            Assert.True(requested.IsCancellationRequested);
            Assert.Equal(atRequest, store.ReadState());
            using (store.Acquire()) { } // An unanswered source cannot retain the shared lock.
            Assert.Equal("v9.2.0", (await WithinDeadline(new(new(home), source, clock)))?.Tag);
            Assert.Equal(1, source.Calls);
        }
        finally { answer.TrySetResult(PublishedRelease.FromTag("v9.3.0")); }
        Assert.Equal(atRequest, store.ReadState());
    }

    [Fact]
    public async Task SynchronouslyBlockedSourceCannotHoldTheCallerOrTheStateLock()
    {
        var store = SeedCache();
        using var release = new ManualResetEventSlim();
        var finished = TestTiming.Signal();
        var source = new Source(_ =>
        {
            try
            {
                // Deliberately ignore cancellation before returning even a Task.
                release.Wait(CancellationToken.None);
                return Task.FromResult(PublishedRelease.FromTag("v9.3.0"));
            }
            finally { finished.TrySetResult(); }
        });
        try
        {
            Assert.Equal("v9.2.0", (await WithinDeadline(new(store, source, clock)))?.Tag);
            Assert.Equal(1, source.Calls);
            Assert.Equal(clock.GetUtcNow(), store.ReadState().LastAttemptAt);
            Assert.Equal("v9.2.0", store.ReadState().Tag);
            using (store.Acquire()) { }
        }
        finally
        {
            release.Set();
            await finished.Task.WaitAsync(Ceiling, TestContext.Current.CancellationToken);
        }
        Assert.Equal("v9.2.0", store.ReadState().Tag);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UnansweredHttpRequestOrResponseBodyIsBoundedWithoutATransportTimeout(bool responseBody)
    {
        var store = SeedCache();
        ReleaseCheckState before = store.ReadState();
        var entered = TestTiming.Signal();
        var release = TestTiming.Signal();
        using var handler = new StalledHandler(responseBody, entered, release.Task);
        using var client = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
        try
        {
            Assert.Equal("v9.2.0", (await WithinDeadline(new(store, new GitHubReleaseSource(client), clock)))?.Tag);
            Assert.True(entered.Task.IsCompletedSuccessfully);
            Assert.Equal(before with { LastAttemptAt = clock.GetUtcNow() }, store.ReadState());
        }
        finally { release.TrySetResult(); }
    }

    [Fact]
    public async Task BlockingCancellationCallbackCannotExtendTheCallersDeadline()
    {
        var store = SeedCache();
        using var release = new ManualResetEventSlim();
        var cancelling = TestTiming.Signal();
        var answer = new TaskCompletionSource<PublishedRelease?>();
        CancellationTokenRegistration registration = default;
        var source = new Source(token =>
        {
            registration = token.Register(() =>
            {
                cancelling.TrySetResult();
                release.Wait(CancellationToken.None);
            });
            return answer.Task;
        });
        var elapsed = Stopwatch.StartNew();
        Task<PublishedRelease?> check = new ReleaseUpdates(store, source, clock).CheckAsync(TestContext.Current.CancellationToken);
        try
        {
            await cancelling.Task.WaitAsync(Ceiling, TestContext.Current.CancellationToken);
            Assert.Equal("v9.2.0", (await check.WaitAsync(Ceiling, TestContext.Current.CancellationToken))?.Tag);
            Assert.True(elapsed.Elapsed < Ceiling, "A cancellation callback extended the release check beyond five seconds.");
            using (store.Acquire()) { }
        }
        finally
        {
            release.Set();
            await registration.DisposeAsync();
            answer.TrySetResult(null);
        }
    }

    [Fact]
    public async Task HeldLockReturnsCachedAnswerWithoutAnAttemptOrRequest()
    {
        var store = SeedCache();
        ReleaseCheckState before = store.ReadState();
        var source = new Source(_ => Task.FromResult(PublishedRelease.FromTag("v9.3.0")));
        using (store.Acquire())
        {
            Assert.Equal("v9.2.0", (await WithinDeadline(new(store, source, clock)))?.Tag);
            Assert.Equal(before, store.ReadState());
            Assert.Equal(0, source.Calls);
        }
        Assert.Equal("v9.3.0", (await WithinDeadline(new(store, source, clock)))?.Tag);
        Assert.Equal(1, source.Calls);
    }

    [Theory]
    [InlineData("timeout")]
    [InlineData("network")]
    [InlineData("unexpected")]
    public async Task FailedSourceKeepsCachedAnswerAndIsThrottled(string failure)
    {
        var store = SeedCache();
        ReleaseCheckState before = store.ReadState();
        var source = new Source(_ => throw failure switch
        {
            "timeout" => new TimeoutException("source deadline"),
            "network" => new HttpRequestException("offline"),
            _ => new InvalidOperationException("source failed"),
        });
        var updates = new ReleaseUpdates(store, source, clock);
        Assert.Equal("v9.2.0", (await WithinDeadline(updates))?.Tag);
        Assert.Equal(before with { LastAttemptAt = clock.GetUtcNow() }, store.ReadState());
        Assert.Equal("v9.2.0", (await WithinDeadline(updates))?.Tag);
        Assert.Equal(1, source.Calls);
    }

    [Fact]
    public async Task CallerCancellationKeepsCacheEvenWhenTheSourceIgnoresIt()
    {
        var store = SeedCache();
        var entered = TestTiming.Signal();
        var answer = new TaskCompletionSource<PublishedRelease?>();
        var source = new Source(_ => { entered.TrySetResult(); return answer.Task; });
        using var cancellation = new CancellationTokenSource();
        Task<PublishedRelease?> check = new ReleaseUpdates(store, source, clock).CheckAsync(cancellation.Token);
        try
        {
            await entered.Task.WaitAsync(Ceiling, TestContext.Current.CancellationToken);
            await cancellation.CancelAsync();
            Assert.Equal("v9.2.0", (await check.WaitAsync(Ceiling, TestContext.Current.CancellationToken))?.Tag);
            Assert.Equal("v9.2.0", store.ReadState().Tag);
            Assert.Equal(clock.GetUtcNow(), store.ReadState().LastAttemptAt);
            using (store.Acquire()) { }
        }
        finally { answer.TrySetException(new HttpRequestException("late failure")); }
    }

    [Fact]
    public async Task AnUnansweredFirstAttemptIsThrottledWithoutInventingACache()
    {
        var store = new ReleaseUpdateStore(home);
        var answer = new TaskCompletionSource<PublishedRelease?>();
        var source = new Source(_ => answer.Task);
        var updates = new ReleaseUpdates(store, source, clock);
        try
        {
            Assert.Null(await WithinDeadline(updates));
            Assert.Equal(new ReleaseCheckState(LastAttemptAt: clock.GetUtcNow()), store.ReadState());
            Assert.Null(await WithinDeadline(updates));
            Assert.Equal(1, source.Calls);
        }
        finally { answer.TrySetResult(null); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AResponseCannotOverwriteADisabledCheckOrANewerAttempt(bool newerAttempt)
    {
        var store = SeedCache();
        var entered = TestTiming.Signal();
        var answer = new TaskCompletionSource<PublishedRelease?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var source = new Source(_ => { entered.TrySetResult(); return answer.Task; });
        Task<PublishedRelease?> check = new ReleaseUpdates(store, source, clock).CheckAsync(TestContext.Current.CancellationToken);
        try
        {
            await entered.Task.WaitAsync(Ceiling, TestContext.Current.CancellationToken);
            if (newerAttempt)
            {
                using (store.Acquire()) store.WriteState(new(clock.GetUtcNow().AddDays(1), clock.GetUtcNow(), "v9.4.0"));
            }
            else store.Configure(false);
            ReleaseCheckState reserved = store.ReadState();
            answer.SetResult(PublishedRelease.FromTag("v9.3.0"));
            Assert.Equal("v9.2.0", (await check.WaitAsync(Ceiling, TestContext.Current.CancellationToken))?.Tag);
            Assert.Equal(reserved, store.ReadState());
        }
        finally { answer.TrySetResult(null); }
    }

    private ReleaseUpdateStore SeedCache()
    {
        var store = new ReleaseUpdateStore(home);
        DateTimeOffset previous = clock.GetUtcNow().AddDays(-2);
        using (store.Acquire()) store.WriteState(new(previous, previous, "v9.2.0"));
        return store;
    }

    private static async Task<PublishedRelease?> WithinDeadline(ReleaseUpdates updates)
    {
        var elapsed = Stopwatch.StartNew();
        // The independent watchdog also catches a regression to blocking synchronously before returning a Task.
        Task<PublishedRelease?> pending = Task.Run(() => updates.CheckAsync(TestContext.Current.CancellationToken), TestContext.Current.CancellationToken);
        PublishedRelease? release = await pending.WaitAsync(Ceiling, TestContext.Current.CancellationToken);
        Assert.True(elapsed.Elapsed < Ceiling, "The optional release check exceeded five seconds.");
        return release;
    }

    private sealed class Source(Func<CancellationToken, Task<PublishedRelease?>> answer) : IReleaseSource
    {
        private int calls;
        internal int Calls => Volatile.Read(ref calls);
        public Task<PublishedRelease?> LatestAsync(CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref calls);
            return answer(cancellationToken);
        }
    }

    private sealed class StalledHandler(bool responseBody, TaskCompletionSource entered, Task release) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (!responseBody)
            {
                entered.TrySetResult();
                await release.ConfigureAwait(false);
            }
            return new(HttpStatusCode.OK) { Content = new StalledContent(entered, release) };
        }
    }

    private sealed class StalledContent(TaskCompletionSource entered, Task release) : HttpContent
    {
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context)
        {
            entered.TrySetResult();
            return release;
        }
        protected override bool TryComputeLength(out long length) { length = 0; return false; }
    }

    public void Dispose() => Directory.Delete(home, recursive: true);
}
