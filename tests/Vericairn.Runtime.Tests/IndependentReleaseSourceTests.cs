// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Net;
using System.Text;
using System.Text.Json;
using Vericairn.Records.Updates;
using IndependentSource = Vericairn.Providers.GitHub.Releases.GitHubReleaseSource;
using CompatibilitySource = Vericairn.Providers.GitHub.GitHubReleaseSource;

namespace Vericairn.Runtime.Tests;

public sealed class IndependentReleaseSourceTests
{
    [Theory]
    [InlineData("v9.2.0", "v9.2.0")]
    [InlineData("9.2.0+build", "9.2.0+build")]
    [InlineData("v9.2.0-rc.1", null)]
    [InlineData("v9.2.0\nforged notice", null)]
    [InlineData(null, null)]
    public async Task IndependentSourceAndCompatibilityEntryPointHonorTheRecordedSourceContract(string? tag, string? expected)
    {
        using var handler = new AnonymousReleaseHandler(tag);
        using var client = new HttpClient(handler);
        IReleaseSource[] sources = [new IndependentSource(client), new CompatibilitySource(client), new RecordedReleaseSource(tag)];
        foreach (IReleaseSource source in sources)
            Assert.Equal(expected, (await source.LatestAsync(TestContext.Current.CancellationToken))?.Tag);
        Assert.Equal(2, handler.Calls);
    }

    [Fact]
    public async Task IndependentSourceAndCompatibilityEntryPointPreserveCancellation()
    {
        using var handler = new AnonymousReleaseHandler("v9.2.0");
        using var client = new HttpClient(handler);
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        IReleaseSource[] sources = [new IndependentSource(client), new CompatibilitySource(client)];
        foreach (IReleaseSource source in sources)
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => source.LatestAsync(cancellation.Token));
        Assert.Equal(0, handler.Calls);
    }

    private sealed class AnonymousReleaseHandler(string? tag) : HttpMessageHandler
    {
        internal int Calls { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Calls++;
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Equal("https://api.github.com/repos/Vericairn/vericairn/releases/latest", request.RequestUri!.AbsoluteUri);
            Assert.Null(request.Content);
            var header = Assert.Single(request.Headers);
            Assert.Equal("User-Agent", header.Key);
            Assert.Equal("Vericairn", Assert.Single(header.Value));
            string body = JsonSerializer.Serialize(new { tag_name = tag, draft = false, prerelease = false, published_at = "2026-10-18T12:00:00Z" });
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") });
        }
    }
}
