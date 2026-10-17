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
            var updates = new ReleaseUpdates(store, source, TimeProvider.System);
            // An earlier CLI client using this home has already learned the release.
            await updates.CheckAsync(TestContext.Current.CancellationToken);
            await fixture.UiAsync(async () =>
            {
                var runtime = new FakeRuntimeManager();
                var shell = new ShellViewModel(_ => throw new InvalidOperationException("No estate needed"), runtime,
                    new Preferences(Path.Combine(home, "workbench")), new ReleaseUpdates(new(home), source, TimeProvider.System));
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
                    new Preferences(Path.Combine(home, "workbench")), new ReleaseUpdates(store, source, TimeProvider.System));
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

    private sealed class Source : IReleaseSource
    {
        internal int Calls { get; private set; }
        internal bool Offline { get; init; }
        public Task<PublishedRelease?> LatestAsync(CancellationToken cancellationToken)
        {
            Calls++;
            if (Offline) throw new HttpRequestException("offline");
            return Task.FromResult(PublishedRelease.FromTag("v9.2.0"));
        }
    }
}
