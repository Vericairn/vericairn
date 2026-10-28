// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using Avalonia.Controls;
using Vericairn.Records.Updates;
using Vericairn.Sdk;

namespace Vericairn.Workbench.Tests;

public sealed class ReleaseNoticeTests(HeadlessFixture fixture)
{
    [Fact]
    public async Task ShellShowsTheDisclosureAndUpgradeRoutesWithoutAnEstateAndSharesTheClientCache()
    {
        string home = Seed.Temp("update-banner");
        try
        {
            var store = new ReleaseUpdateStore(home);
            var source = new Source();
            var updates = new ReleaseUpdates(store, source, new ClientClock(new DateTimeOffset(2026, 10, 20, 12, 0, 0, TimeSpan.Zero)));
            // An earlier CLI client using this home has already learned the release.
            await updates.CheckAsync(TestContext.Current.CancellationToken);
            await fixture.UiAsync(async () =>
            {
                var runtime = new FakeRuntimeManager();
                var shell = new ShellViewModel(_ => throw new InvalidOperationException("No estate needed"), runtime,
                    new Preferences(Path.Combine(home, "workbench")), new ReleaseUpdates(new(home), source, new ClientClock(new DateTimeOffset(2026, 10, 20, 12, 0, 0, TimeSpan.Zero))));
                var window = new MainWindow(shell);
                window.Show();
                await shell.RefreshUpdatesAsync();
                Assert.False(shell.HasEstate);
                Assert.Contains("Release check: on", window.FindControl<SelectableTextBlock>("UpdateDisclosureText")!.Text, StringComparison.Ordinal);
                Assert.Contains("updates configure --enabled false", shell.UpdateDisclosure, StringComparison.Ordinal);
                string? text = window.FindControl<SelectableTextBlock>("UpdateNoticeText")!.Text;
                Assert.Contains("Newer Vericairn release: 9.2.0", text, StringComparison.Ordinal);
                Assert.Contains("https://github.com/Vericairn/vericairn/releases/tag/v9.2.0", text, StringComparison.Ordinal);
                Assert.Contains("dotnet tool update --global dotnet-vc --version 9.2.0", text, StringComparison.Ordinal);
                Assert.Contains("Archive download", text, StringComparison.Ordinal);
                Assert.Contains("vericairn estate upgrade --to 9.2.0 --check", text, StringComparison.Ordinal);
                Assert.Empty(runtime.Calls);
                window.Close();
            });
            Assert.Equal(1, source.Calls);
        }
        finally { Directory.Delete(home, recursive: true); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DisabledAndOfflineChecksLeaveTheShellUsable(bool enabled)
    {
        string home = Seed.Temp("update-offline");
        try
        {
            var store = new ReleaseUpdateStore(home);
            store.Configure(enabled);
            var source = new Source { Offline = true };
            await fixture.UiAsync(async () =>
            {
                var shell = new ShellViewModel(_ => throw new InvalidOperationException("No estate needed"), new FakeRuntimeManager(),
                    new Preferences(Path.Combine(home, "workbench")), new ReleaseUpdates(store, source, new ClientClock(new DateTimeOffset(2026, 10, 20, 12, 0, 0, TimeSpan.Zero))));
                await shell.RefreshUpdatesAsync();
                Assert.Empty(shell.UpdateNotice);
                Assert.Equal("No estate open.", shell.Status);
                Assert.Contains(enabled ? "Release check: on" : "Release check: off", shell.UpdateDisclosure, StringComparison.Ordinal);
                await shell.RefreshUpdatesAsync();
            });
            Assert.Equal(enabled ? 1 : 0, source.Calls);
        }
        finally { Directory.Delete(home, recursive: true); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UnansweredSourceAndHeldLockKeepTheCachedNoticeWithinAFewSeconds(bool heldLock)
    {
        string home = Seed.Temp("update-deadline");
        var answer = new TaskCompletionSource<PublishedRelease?>(TaskCreationOptions.RunContinuationsAsynchronously);
        Task? refresh = null;
        try
        {
            var store = new ReleaseUpdateStore(home);
            var clock = new ClientClock(new DateTimeOffset(2026, 10, 20, 12, 0, 0, TimeSpan.Zero));
            DateTimeOffset previous = clock.GetUtcNow().AddDays(-2);
            var cached = new ReleaseCheckState(previous, previous, "v9.2.0");
            using (store.Acquire()) store.WriteState(cached);
            var source = new Source { Answer = _ => answer.Task };
            using FileStream? held = heldLock ? store.Acquire() : null;
            refresh = fixture.UiAsync(async () =>
            {
                var shell = new ShellViewModel(_ => throw new InvalidOperationException("No estate needed"), new FakeRuntimeManager(),
                    new Preferences(Path.Combine(home, "workbench")), new ReleaseUpdates(store, source, clock));
                await shell.RefreshUpdatesAsync();
                Assert.Equal("No estate open.", shell.Status);
                Assert.Contains("Newer Vericairn release: 9.2.0", shell.UpdateNotice, StringComparison.Ordinal);
                Assert.Contains("Release check: on", shell.UpdateDisclosure, StringComparison.Ordinal);
            });
            await refresh.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            Assert.Equal(heldLock ? 0 : 1, source.Calls);
            Assert.Equal(heldLock ? cached : cached with { LastAttemptAt = clock.GetUtcNow() }, store.ReadState());
        }
        finally
        {
            answer.TrySetResult(null);
            if (refresh is not null) await refresh.WaitAsync(TestTiming.HangTimeout, TestContext.Current.CancellationToken);
            Directory.Delete(home, recursive: true);
        }
    }

    private sealed class Source : IReleaseSource
    {
        internal int Calls { get; private set; }
        internal bool Offline { get; init; }
        internal Func<CancellationToken, Task<PublishedRelease?>>? Answer { get; init; }
        public Task<PublishedRelease?> LatestAsync(CancellationToken cancellationToken)
        {
            Calls++;
            if (Offline) throw new HttpRequestException("offline");
            return Answer?.Invoke(cancellationToken) ?? Task.FromResult(PublishedRelease.FromTag("v9.2.0"));
        }
    }
}
