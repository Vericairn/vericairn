// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Net;
using System.Text;
using System.Text.Json;
using Vericairn.Providers.GitHub;
using Vericairn.Records.Updates;
using Vericairn.Sdk;

namespace Vericairn.Runtime.Tests;

public sealed class ReleaseSourceContractTests
{
    [Theory]
    [InlineData("v9.2.0", "v9.2.0")]
    [InlineData("9.2.0+build", "9.2.0+build")]
    [InlineData("v9.2.0-rc.1", null)]
    [InlineData("v9.2.0\nforged notice", null)]
    [InlineData(null, null)]
    public async Task GitHubAndRecordedSourcesReturnOnlyStablePublishedTags(string? tag, string? expected)
    {
        using var handler = new ReleaseHandler(tag);
        using var client = new HttpClient(handler);
        IReleaseSource[] sources = [new GitHubReleaseSource(client), new RecordedReleaseSource(tag)];
        foreach (IReleaseSource source in sources)
            Assert.Equal(expected, (await source.LatestAsync(TestContext.Current.CancellationToken))?.Tag);
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task BothSourcesObserveCancellation()
    {
        using var handler = new ReleaseHandler("v9.2.0");
        using var client = new HttpClient(handler);
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        IReleaseSource[] sources = [new GitHubReleaseSource(client), new RecordedReleaseSource("v9.2.0")];
        foreach (IReleaseSource source in sources)
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => source.LatestAsync(cancellation.Token));
    }

    [Fact]
    public async Task ClientPolicySharesTheDailyCacheAndOffSwitchAcrossBothSourceImplementations()
    {
        string home = Path.Combine(Path.GetTempPath(), "vericairn-release-contract-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(home);
        try
        {
            var clock = new Clock();
            var store = new ReleaseUpdateStore(home);
            var recorded = new ReleaseUpdates(store, new RecordedReleaseSource("v9.2.0"), clock);
            Assert.Equal("v9.2.0", (await recorded.CheckAsync(TestContext.Current.CancellationToken))?.Tag);
            using var handler = new ReleaseHandler("v9.3.0");
            using var client = new HttpClient(handler);
            var github = new ReleaseUpdates(new(home), new GitHubReleaseSource(client), clock);
            clock.Now = clock.Now.AddHours(23);
            Assert.Equal("v9.2.0", (await github.CheckAsync(TestContext.Current.CancellationToken))?.Tag);
            Assert.Equal(0, handler.Calls);
            clock.Now = clock.Now.AddHours(1);
            Assert.Equal("v9.3.0", (await github.CheckAsync(TestContext.Current.CancellationToken))?.Tag);
            Assert.Equal(1, handler.Calls);
            DateTimeOffset? attempted = store.ReadState().LastAttemptAt;
            store.Configure(false);
            clock.Now = clock.Now.AddDays(2);
            Assert.Equal("v9.3.0", (await recorded.CheckAsync(TestContext.Current.CancellationToken))?.Tag);
            Assert.Equal("v9.3.0", (await github.CheckAsync(TestContext.Current.CancellationToken))?.Tag);
            Assert.Equal(1, handler.Calls);
            Assert.Equal(attempted, store.ReadState().LastAttemptAt);
        }
        finally { Directory.Delete(home, recursive: true); }
    }

    private sealed class Clock : TimeProvider
    {
        internal DateTimeOffset Now { get; set; } = new(2026, 10, 18, 12, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class ReleaseHandler(string? tag) : HttpMessageHandler
    {
        internal int Calls { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Calls++;
            string body = JsonSerializer.Serialize(new { tag_name = tag, draft = false, prerelease = false, published_at = "2026-10-18T12:00:00Z" });
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") });
        }
    }
}
