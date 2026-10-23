// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Net;
using Vericairn.Providers.GitHub.Releases;
using Vericairn.Records.Tests;
using Vericairn.Records.Updates;

namespace Vericairn.Runtime.Tests;

public sealed class PublishedReleaseTransportTests
{
    [Fact]
    public async Task CompatibilityAndIndependentSourcesVerifyTheSameRecordedBytes()
    {
        using var fixture = new PublishedReleaseFixture();
        using var client = new HttpClient(fixture);
        IReleaseSource[] sources = [new GitHubReleaseSource(client), new Vericairn.Providers.GitHub.GitHubReleaseSource(client)];
        foreach (IReleaseSource source in sources)
        {
            VerifiedRelease verified = await source.VerifyAsync(fixture.Build, "linux-x64", TestContext.Current.CancellationToken);
            Assert.Equal(PublishedReleaseFixture.Commit, verified.Build.Commit);
            Assert.Equal(12, verified.Archives.Count);
        }
        Assert.Equal(10, fixture.Requests.Count);
    }

    [Theory]
    [InlineData("https://release-assets.githubusercontent.com/recorded/archive", true)]
    [InlineData("https://objects.githubusercontent.com/recorded/archive", true)]
    [InlineData("http://release-assets.githubusercontent.com/recorded/archive", false)]
    [InlineData("https://other.invalid/archive", false)]
    [InlineData("https://release-assets.githubusercontent.com:8443/archive", false)]
    public async Task OnlyTheExpectedAnonymousAssetRedirectsAreFollowed(string destination, bool permitted)
    {
        using var fixture = new PublishedReleaseFixture();
        using var transport = new RedirectHandler(fixture, destination);
        using var client = new HttpClient(transport);
        var source = new GitHubReleaseSource(client);
        if (permitted)
        {
            VerifiedRelease verified = await source.VerifyAsync(fixture.Build, "linux-x64", TestContext.Current.CancellationToken);
            Assert.Equal(fixture.Build.Identity, verified.Build.Identity);
            Assert.Equal(1, transport.RedirectedRequests);
        }
        else
        {
            var refusal = await Assert.ThrowsAsync<InvalidOperationException>(() => source.VerifyAsync(fixture.Build, "linux-x64", TestContext.Current.CancellationToken));
            Assert.Contains("--from <directory>", refusal.Message, StringComparison.Ordinal);
            Assert.Equal(0, transport.RedirectedRequests);
        }
    }

    [Fact]
    public async Task CancellationDoesNotTurnIntoAnOfflineSuccessOrWriteEvidence()
    {
        using var fixture = new PublishedReleaseFixture();
        using var client = new HttpClient(fixture);
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new GitHubReleaseSource(client).VerifyAsync(fixture.Build, "linux-x64", cancellation.Token));
        Assert.Empty(fixture.Requests);
    }

    [Fact]
    public async Task AReleaseWhoseTargetIsABranchStillHasToMatchTheEmbeddedCommitThroughItsTag()
    {
        using var fixture = new PublishedReleaseFixture();
        fixture.ChangeRelease(r => r["target_commitish"] = "main");
        using var client = new HttpClient(fixture);
        var source = new GitHubReleaseSource(client);
        Assert.Equal(PublishedReleaseFixture.Commit, (await source.VerifyAsync(fixture.Build, "linux-x64", TestContext.Current.CancellationToken)).Build.Commit);
        fixture.Fault("tag-commit", "linux-x64");
        var refusal = await Assert.ThrowsAsync<InvalidOperationException>(() => source.VerifyAsync(fixture.Build, "linux-x64", TestContext.Current.CancellationToken));
        Assert.Contains("does not point to", refusal.Message, StringComparison.Ordinal);
    }

    private sealed class RedirectHandler : HttpMessageHandler
    {
        private readonly PublishedReleaseFixture fixture;
        private readonly string destination;
        private readonly HttpMessageInvoker recorded;
        internal int RedirectedRequests { get; private set; }

        internal RedirectHandler(PublishedReleaseFixture fixture, string destination)
        {
            this.fixture = fixture;
            this.destination = destination;
            recorded = new HttpMessageInvoker(fixture, disposeHandler: false);
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri!.AbsoluteUri == destination)
            {
                RedirectedRequests++;
                Assert.Equal("Vericairn", Assert.Single(Assert.Single(request.Headers).Value));
                Assert.Null(request.Content);
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(fixture.Files[ReleaseArchive.FileName(PublishedReleaseFixture.Version, "linux-x64")]) });
            }
            if (request.RequestUri.AbsoluteUri == VerifiedRelease.AssetUrl("v9.2.0", ReleaseArchive.FileName(PublishedReleaseFixture.Version, "linux-x64")))
            {
                var redirect = new HttpResponseMessage(HttpStatusCode.Redirect);
                redirect.Headers.Location = new Uri(destination);
                return Task.FromResult(redirect);
            }
            return recorded.SendAsync(request, cancellationToken);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) recorded.Dispose();
            base.Dispose(disposing);
        }
    }
}
