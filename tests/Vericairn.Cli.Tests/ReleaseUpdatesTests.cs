// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Diagnostics;
using System.Net;
using System.Text;
using Vericairn.Records.Updates;
using Vericairn.Sdk;

namespace Vericairn.Cli.Tests;

public sealed class ReleaseUpdatesTests : IDisposable
{
    private readonly string home = CliHarness.TempDirectory("release-check");
    private readonly Clock clock = new();
    private readonly Source source = new();

    [Fact]
    public async Task OneRequestPerRollingDayAcrossClientInstancesAndClockCorrections()
    {
        var store = new ReleaseUpdateStore(home);
        var first = new ReleaseUpdates(store, source, clock);
        Assert.Equal("9.2.0", (await first.CheckAsync(TestContext.Current.CancellationToken))?.Version);
        clock.Now = clock.Now.AddHours(23).AddMinutes(59);
        Assert.Equal("9.2.0", (await new ReleaseUpdates(new(home), source, clock).CheckAsync(TestContext.Current.CancellationToken))?.Version);
        Assert.Equal(1, source.Calls);
        clock.Now = clock.Now.AddMinutes(1);
        await first.CheckAsync(TestContext.Current.CancellationToken);
        Assert.Equal(2, source.Calls);
        clock.Now = clock.Now.AddDays(-2);
        await first.CheckAsync(TestContext.Current.CancellationToken);
        Assert.Equal(2, source.Calls);
        Assert.Equal("v9.2.0", store.ReadState().Tag);
        Assert.NotNull(store.ReadState().LastSuccessAt);
    }

    [Fact]
    public async Task SimultaneousClientsReserveOnlyOneAttempt()
    {
        var released = new TaskCompletionSource<PublishedRelease?>(TaskCreationOptions.RunContinuationsAsynchronously);
        source.Answer = _ => released.Task;
        Task<PublishedRelease?> first = new ReleaseUpdates(new(home), source, clock).CheckAsync(TestContext.Current.CancellationToken);
        try
        {
            Assert.Equal(1, source.Calls);
            await new ReleaseUpdates(new(home), source, clock).CheckAsync(TestContext.Current.CancellationToken);
            Assert.Equal(1, source.Calls);
        }
        finally { released.SetResult(PublishedRelease.FromTag("v9.2.0")); }
        Assert.NotNull(await first);
    }

    [Fact]
    public async Task OfflineKeepsLastAnswerAndTimestampAndDoesNotRetryForADay()
    {
        var store = new ReleaseUpdateStore(home);
        var updates = new ReleaseUpdates(store, source, clock);
        await updates.CheckAsync(TestContext.Current.CancellationToken);
        DateTimeOffset? success = store.ReadState().LastSuccessAt;
        clock.Now = clock.Now.AddDays(1);
        source.Answer = _ => throw new HttpRequestException("offline");
        Assert.Equal("9.2.0", (await updates.CheckAsync(TestContext.Current.CancellationToken))?.Version);
        Assert.Equal(success, store.ReadState().LastSuccessAt);
        Assert.Equal(clock.Now, store.ReadState().LastAttemptAt);
        Assert.Equal("9.2.0", (await updates.CheckAsync(TestContext.Current.CancellationToken))?.Version);
        Assert.Equal(2, source.Calls);
    }

    [Fact]
    public async Task FirstFailedAttemptIsAlsoThrottled()
    {
        source.Answer = _ => throw new TaskCanceledException("timeout");
        var updates = new ReleaseUpdates(new(home), source, clock);
        Assert.Null(await updates.CheckAsync(TestContext.Current.CancellationToken));
        Assert.Null(await updates.CheckAsync(TestContext.Current.CancellationToken));
        Assert.Equal(1, source.Calls);
    }

    [Fact]
    public async Task DisabledMakesNoRequestAndRetainsCachedKnowledge()
    {
        var store = new ReleaseUpdateStore(home);
        store.Configure(false);
        var updates = new ReleaseUpdates(store, source, clock);
        Assert.Null(await updates.CheckAsync(TestContext.Current.CancellationToken));
        Assert.Equal(0, source.Calls);
        store.Configure(true);
        await updates.CheckAsync(TestContext.Current.CancellationToken);
        store.Configure(false);
        clock.Now = clock.Now.AddDays(2);
        Assert.Equal("9.2.0", (await updates.CheckAsync(TestContext.Current.CancellationToken))?.Version);
        Assert.Equal(1, source.Calls);
    }

    [Fact]
    public async Task CorruptConfigurationAndUnwritableStateNeverSendARequest()
    {
        var store = new ReleaseUpdateStore(home);
        store.Configure(false);
        string settings = Path.Combine(home, "updates", "settings.json");
        foreach (string invalid in new[] { "{", "{}", "null", "{\"enabled\":false,\"enabled\":true}", "{\"enabled\":true,\"extra\":1}" })
        {
            File.WriteAllText(settings, invalid);
            Assert.Null(await new ReleaseUpdates(store, source, clock).CheckAsync(TestContext.Current.CancellationToken));
        }
        Directory.CreateDirectory(Path.Combine(home, ".git"));
        Assert.Null(await new ReleaseUpdates(store, source, clock).CheckAsync(TestContext.Current.CancellationToken));
        Assert.Equal(0, source.Calls);
    }

    [Fact]
    public async Task CorruptCacheCannotResetTheDailyLimit()
    {
        var store = new ReleaseUpdateStore(home);
        store.Configure(true);
        File.WriteAllText(Path.Combine(home, "updates", "check.json"), "{");
        Assert.Null(await new ReleaseUpdates(store, source, clock).CheckAsync(TestContext.Current.CancellationToken));
        Assert.Equal(0, source.Calls);
    }

    [Fact]
    public void ProductionTransportDisablesCredentialsCookiesRedirectsAndTracePropagation()
    {
        using SocketsHttpHandler handler = GitHubReleaseSource.CreateHandler();
        Assert.False(handler.AllowAutoRedirect);
        Assert.False(handler.UseCookies);
        Assert.False(handler.UseProxy);
        Assert.Null(handler.Credentials);
        Assert.Null(handler.DefaultProxyCredentials);
        Assert.Null(handler.ActivityHeadersPropagator);
    }

    [Fact]
    public async Task RequestHasOnlyTheFixedEndpointAndRequiredAnonymousUserAgent()
    {
        using var activity = new Activity("identifying-parent-trace").Start();
        using var handler = new Handler(request =>
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Equal("https://api.github.com/repos/Vericairn/vericairn/releases/latest", request.RequestUri!.AbsoluteUri);
            Assert.Empty(request.RequestUri.Query);
            Assert.Null(request.Content);
            var header = Assert.Single(request.Headers);
            Assert.Equal("User-Agent", header.Key);
            Assert.Equal("Vericairn", Assert.Single(header.Value));
            return Json("""{"tag_name":"v9.2.0","draft":false,"prerelease":false,"published_at":"2026-10-17T12:00:00Z","html_url":"https://untrusted.invalid/anything"}""");
        });
        using var client = new HttpClient(handler);
        PublishedRelease? release = await new GitHubReleaseSource(client).LatestAsync(TestContext.Current.CancellationToken);
        Assert.Equal("https://github.com/Vericairn/vericairn/releases/tag/v9.2.0", release?.NotesUrl);
    }

    [Theory]
    [InlineData("{\"tag_name\":\"v9.2.0\",\"draft\":true,\"prerelease\":false,\"published_at\":\"2026-10-17T12:00:00Z\"}")]
    [InlineData("{\"tag_name\":\"v9.2.0\",\"draft\":false,\"prerelease\":true,\"published_at\":\"2026-10-17T12:00:00Z\"}")]
    [InlineData("{\"tag_name\":\"v9.2.0-rc.1\",\"draft\":false,\"prerelease\":false,\"published_at\":\"2026-10-17T12:00:00Z\"}")]
    [InlineData("{\"tag_name\":\"v9.2.0\",\"draft\":false,\"prerelease\":false,\"published_at\":null}")]
    [InlineData("{\"tag_name\":\"v9.2.0\"}")]
    [InlineData("{\"tag_name\":\"v9.2.0\\nforged notice\",\"draft\":false,\"prerelease\":false,\"published_at\":\"2026-10-17T12:00:00Z\"}")]
    [InlineData("[]")]
    [InlineData("{")]
    public async Task DraftPrereleaseAndMalformedResponsesDoNotReplaceCache(string response)
    {
        var store = new ReleaseUpdateStore(home);
        await new ReleaseUpdates(store, source, clock).CheckAsync(TestContext.Current.CancellationToken);
        clock.Now = clock.Now.AddDays(1);
        using var handler = new Handler(_ => Json(response));
        using var client = new HttpClient(handler);
        Assert.Equal("9.2.0", (await new ReleaseUpdates(store, new GitHubReleaseSource(client), clock).CheckAsync(TestContext.Current.CancellationToken))?.Version);
    }

    [Theory]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.MovedPermanently)]
    public async Task HttpFailureIsSilentAndThrottled(HttpStatusCode status)
    {
        int calls = 0;
        using var handler = new Handler(_ => { calls++; return new HttpResponseMessage(status); });
        using var client = new HttpClient(handler);
        var updates = new ReleaseUpdates(new(home), new GitHubReleaseSource(client), clock);
        Assert.Null(await updates.CheckAsync(TestContext.Current.CancellationToken));
        Assert.Null(await updates.CheckAsync(TestContext.Current.CancellationToken));
        Assert.Equal(1, calls);
    }

    [Theory]
    [InlineData("1.10.0", "1.9.9", true)]
    [InlineData("1.9.9", "1.10.0", false)]
    [InlineData("1.2.3", "1.2.3-dev+commit", true)]
    [InlineData("1.2.3+build", "1.2.3+other", false)]
    [InlineData("1.2.3", "1.2.4-dev", false)]
    [InlineData("1.2.3", "unknown", false)]
    public void ComparisonUsesVersionPrecedence(string published, string installed, bool newer) =>
        Assert.Equal(newer, PublishedRelease.FromTag(published)!.IsNewerThan(installed));

    private static HttpResponseMessage Json(string body) => new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromResult(respond(request));
    }
    internal sealed class Clock : TimeProvider
    {
        internal DateTimeOffset Now { get; set; } = new(2026, 10, 17, 12, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }
    internal sealed class Source : IReleaseSource
    {
        internal int Calls { get; private set; }
        internal Func<CancellationToken, Task<PublishedRelease?>> Answer { get; set; } = _ => Task.FromResult(PublishedRelease.FromTag("v9.2.0"));
        public Task<PublishedRelease?> LatestAsync(CancellationToken cancellationToken) { Calls++; return Answer(cancellationToken); }
    }
    public void Dispose() => CliHarness.Delete(home);
}
