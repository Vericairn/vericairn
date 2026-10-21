// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Net;
using System.IO.Compression;
using System.Text.Json.Nodes;
using Vericairn.Protocol;
using Vericairn.Providers.NuGet;
using Vericairn.Records.Hashing;
using Vericairn.Runtime.Providers;

namespace Vericairn.Runtime.Tests;

public sealed class NuGetRegistryProviderTests
{
    [Theory]
    [InlineData("listed", "listed", "not-deprecated")]
    [InlineData("unlisted", "unlisted", "not-deprecated")]
    [InlineData("deprecated", "listed", "deprecated")]
    public async Task RegistryFactsRetainTheExactResponseDigests(string fixture, string listing, string deprecation)
    {
        using var handler = new RecordedNuGetHandler { Registration = fixture };
        using var client = new HttpClient(handler);
        var provider = new NuGetRegistryProvider(client);
        PackageRegistryState state = await provider.ObserveAsync("Example.Widget", "1.0.0", TestContext.Current.CancellationToken);
        Assert.Equal("example.widget", state.Package);
        Assert.Equal("published", state.Publication);
        Assert.Equal(listing, state.Listing);
        Assert.Equal(deprecation, state.Deprecation);
        Assert.Equal(handler.Requests, state.Evidence.Select(e => e.Url));
        Assert.Equal(new[] { "service-index", "versions", fixture }.Select(n => ContentHash.Sha256(RecordedNuGetHandler.Fixture(n))), state.Evidence.Select(e => e.Hash));
        Assert.All(state.Evidence, e => Assert.Equal(200, e.Status));
        Assert.Equal(new[] { "service-index", "versions", fixture }.Select(n => (long)RecordedNuGetHandler.Fixture(n).Length), state.Evidence.Select(e => e.Bytes));
        if (fixture == "deprecated")
        {
            Assert.Equal(["Legacy", "Other"], state.Details!.Reasons);
            Assert.Equal("Use the replacement package.", state.Details.Message);
            Assert.Equal("Example.Replacement", state.Details.AlternatePackage);
            Assert.Equal("[2.0.0,)", state.Details.AlternateRange);
        }
        else Assert.Null(state.Details);
    }

    [Theory]
    [InlineData("absent", "empty-registration")]
    [InlineData("not-found", "not-found")]
    [InlineData("absent", "listed")]
    public async Task AbsentVersionsAreNeverUnlisted(string versions, string registration)
    {
        using var handler = new RecordedNuGetHandler { Versions = versions, Registration = registration };
        using var client = new HttpClient(handler);
        var provider = new NuGetRegistryProvider(client);
        PackageRegistryState state = await provider.ObserveAsync("Example.Widget", "2.0.0", TestContext.Current.CancellationToken);
        Assert.Equal("not-found", state.Publication);
        Assert.Equal("unknown", state.Listing);
        Assert.Equal("unknown", state.Deprecation);
        Assert.Null(state.PackageContent);
        Assert.Null(state.Details);
        if (registration == "not-found") Assert.Equal([200, 404, 404], state.Evidence.Select(e => e.Status));
    }

    [Theory]
    [InlineData("malformed")]
    [InlineData("oversized")]
    [InlineData("timeout")]
    [InlineData("not-found")]
    [InlineData("empty-registration")]
    public async Task BadIncompleteOrInconsistentResponsesEstablishNoState(string fixture)
    {
        using var handler = new RecordedNuGetHandler { Registration = fixture };
        using var client = new HttpClient(handler);
        await Assert.ThrowsAsync<ProviderRefusedException>(() => new NuGetRegistryProvider(client).ObserveAsync("Example.Widget", "1.0.0", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task PagedRegistrationIsIncludedInTheResponseEvidence()
    {
        using var handler = new RecordedNuGetHandler { Registration = "paged" };
        using var client = new HttpClient(handler);
        PackageRegistryState state = await new NuGetRegistryProvider(client).ObserveAsync("Example.Widget", "1.0.0", TestContext.Current.CancellationToken);
        Assert.Equal("listed", state.Listing);
        Assert.Equal(4, state.Evidence.Count);
        Assert.Equal(ContentHash.Sha256(RecordedNuGetHandler.Fixture("page")), state.Evidence[^1].Hash);
    }

    [Theory]
    [InlineData("http://api.nuget.org/v3/registration5-gz-semver2/")]
    [InlineData("https://example.invalid/v3/registration5-gz-semver2/")]
    [InlineData("https://api.nuget.org:444/v3/registration5-gz-semver2/")]
    [InlineData("https://api.nuget.org/v3/registration5-gz-semver2/?extra=true")]
    public async Task DiscoveredResourcesCannotLeaveThePublicRegistry(string destination)
    {
        JsonNode index = JsonNode.Parse(RecordedNuGetHandler.Fixture("service-index"))!;
        index["resources"]![0]!["@id"] = destination;
        using var handler = new RecordedNuGetHandler { Override = _ => new(HttpStatusCode.OK) { Content = new StringContent(index.ToJsonString()) } };
        using var client = new HttpClient(handler);
        await Assert.ThrowsAsync<ProviderRefusedException>(() => new NuGetRegistryProvider(client).ObserveAsync("Example.Widget", "1.0.0", TestContext.Current.CancellationToken));
        Assert.Single(handler.Requests);
    }

    [Theory]
    [InlineData(302)]
    [InlineData(401)]
    [InlineData(429)]
    [InlineData(500)]
    public async Task RedirectsAndRegistryErrorsAreNotAbsence(int status)
    {
        using var handler = new RecordedNuGetHandler { Override = _ => new((HttpStatusCode)status) { Content = new ByteArrayContent(RecordedNuGetHandler.Fixture("not-found")) } };
        using var client = new HttpClient(handler);
        await Assert.ThrowsAsync<ProviderRefusedException>(() => new NuGetRegistryProvider(client).ObserveAsync("Example.Widget", "1.0.0", TestContext.Current.CancellationToken));
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task ABodyWithoutALengthStillCannotExceedTheResponseLimit()
    {
        byte[] body = new byte[NuGetRegistryProvider.MaximumResponseBytes + 1];
        RecordedNuGetHandler.Fixture("listed").CopyTo(body, 0);
        using var handler = new RecordedNuGetHandler { Override = _ => new(HttpStatusCode.OK) { Content = new StreamContent(new NonSeekableStream(body)) } };
        using var client = new HttpClient(handler);
        await Assert.ThrowsAsync<ProviderRefusedException>(() => new NuGetRegistryProvider(client).ObserveAsync("Example.Widget", "1.0.0", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task CancellationBeforeAReadMakesNoRequest()
    {
        using var handler = new RecordedNuGetHandler();
        using var client = new HttpClient(handler);
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new NuGetRegistryProvider(client).ObserveAsync("Example.Widget", "1.0.0", cancellation.Token));
        Assert.Empty(handler.Requests);
    }

    [Theory]
    [InlineData(".", "1.0.0")]
    [InlineData("..", "1.0.0")]
    [InlineData("Example/Other", "1.0.0")]
    [InlineData("Example.Widget", "../1.0.0")]
    [InlineData("Example.Widget", "1.0.0\n")]
    public async Task InvalidPackageIdentitiesMakeNoRequest(string package, string version)
    {
        using var handler = new RecordedNuGetHandler();
        using var client = new HttpClient(handler);
        await Assert.ThrowsAsync<ProviderRefusedException>(() => new NuGetRegistryProvider(client).ObserveAsync(package, version, TestContext.Current.CancellationToken));
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task BodyReadsReceiveCancellationAfterHeaders()
    {
        using var body = new StalledStream();
        using var handler = new RecordedNuGetHandler { Override = _ => new(HttpStatusCode.OK) { Content = new StreamContent(body) } };
        using var client = new HttpClient(handler);
        using var cancellation = new CancellationTokenSource();
        Task<PackageRegistryState> observation = new NuGetRegistryProvider(client).ObserveAsync("Example.Widget", "1.0.0", cancellation.Token);
        await body.Started.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        await cancellation.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => observation);
        Assert.True(body.Canceled);
    }

    [Fact]
    public async Task RequestCountBoundsPagedInventories()
    {
        JsonNode index = JsonNode.Parse(RecordedNuGetHandler.Fixture("paged"))!;
        JsonNode page = index["items"]![0]!.DeepClone();
        JsonArray pages = index["items"]!.AsArray();
        pages.Clear();
        for (int i = 0; i < 40; i++)
        {
            JsonNode next = page.DeepClone();
            next["count"] = 0;
            next["@id"] = $"https://api.nuget.org/v3/registration5-gz-semver2/example.widget/page/{i}/index.json";
            pages.Add(next);
        }
        index["count"] = pages.Count;
        using var handler = new RecordedNuGetHandler
        {
            Override = url => url.Contains("/registration", StringComparison.Ordinal) ? new(HttpStatusCode.OK)
                { Content = new ByteArrayContent(url.Contains("/page/", StringComparison.Ordinal) ? RecordedNuGetHandler.Fixture("empty-registration") : System.Text.Encoding.UTF8.GetBytes(index.ToJsonString())) } : null,
        };
        using var client = new HttpClient(handler);
        await Assert.ThrowsAsync<ProviderRefusedException>(() => new NuGetRegistryProvider(client).ObserveAsync("Example.Widget", "1.0.0", TestContext.Current.CancellationToken));
        Assert.Equal(NuGetRegistryProvider.MaximumRequests, handler.Requests.Count);
    }

    [Fact]
    public async Task TotalResponseBytesBoundTheWholeObservation()
    {
        JsonNode index = JsonNode.Parse(RecordedNuGetHandler.Fixture("paged"))!;
        JsonNode template = index["items"]![0]!.DeepClone();
        JsonArray pages = index["items"]!.AsArray();
        pages.Clear();
        for (int i = 0; i < 9; i++)
        {
            JsonNode page = template.DeepClone();
            page["count"] = 0;
            page["@id"] = $"https://api.nuget.org/v3/registration5-gz-semver2/example.widget/page/{i}/index.json";
            pages.Add(page);
        }
        index["count"] = pages.Count;
        string emptyPage = System.Text.Encoding.UTF8.GetString(RecordedNuGetHandler.Fixture("empty-registration"));
        byte[] response = System.Text.Encoding.UTF8.GetBytes(emptyPage.PadRight(NuGetRegistryProvider.MaximumResponseBytes - 10));
        using var handler = new RecordedNuGetHandler { Override = url => url.Contains("/registration", StringComparison.Ordinal) ? new(HttpStatusCode.OK)
            { Content = new ByteArrayContent(url.Contains("/page/", StringComparison.Ordinal) ? response : System.Text.Encoding.UTF8.GetBytes(index.ToJsonString())) } : null };
        using var client = new HttpClient(handler);
        ProviderRefusedException refusal = await Assert.ThrowsAsync<ProviderRefusedException>(() => new NuGetRegistryProvider(client).ObserveAsync("Example.Widget", "1.0.0", TestContext.Current.CancellationToken));
        Assert.Contains("byte limit", refusal.Message, StringComparison.Ordinal);
        Assert.InRange(handler.Requests.Count, 4, 12);
    }

    [Fact]
    public async Task APaginatedResponseMustContainItsDeclaredCount()
    {
        using var handler = new RecordedNuGetHandler { Registration = "paged", Override = url => url.Contains("/page/", StringComparison.Ordinal)
            ? new(HttpStatusCode.OK) { Content = new ByteArrayContent(RecordedNuGetHandler.Fixture("empty-registration")) } : null };
        using var client = new HttpClient(handler);
        await Assert.ThrowsAsync<ProviderRefusedException>(() => new NuGetRegistryProvider(client).ObserveAsync("Example.Widget", "1.0.0", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task InvalidGzipIsAnUnavailableObservation()
    {
        using var handler = new RecordedNuGetHandler { Override = _ => GzipResponse(RecordedNuGetHandler.Fixture("listed")) };
        using var client = new HttpClient(handler);
        await Assert.ThrowsAsync<ProviderRefusedException>(() => new NuGetRegistryProvider(client).ObserveAsync("Example.Widget", "1.0.0", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task RepeatedPagesRefuseInsteadOfLooping()
    {
        JsonNode index = JsonNode.Parse(RecordedNuGetHandler.Fixture("paged"))!;
        index["items"]!.AsArray().Add(index["items"]![0]!.DeepClone());
        index["count"] = 2;
        using var handler = new RecordedNuGetHandler { Override = url => url.EndsWith("/example.widget/index.json", StringComparison.Ordinal)
            && url.Contains("/registration", StringComparison.Ordinal) ? new(HttpStatusCode.OK) { Content = new StringContent(index.ToJsonString()) } : null };
        using var client = new HttpClient(handler);
        await Assert.ThrowsAsync<ProviderRefusedException>(() => new NuGetRegistryProvider(client).ObserveAsync("Example.Widget", "1.0.0", TestContext.Current.CancellationToken));
        Assert.Equal(4, handler.Requests.Count);
    }

    [Fact]
    public void ShippedTransportHasNoAmbientCredentialsOrRedirects()
    {
        using SocketsHttpHandler handler = NuGetRegistryProvider.CreateHandler();
        Assert.False(handler.AllowAutoRedirect);
        Assert.False(handler.UseCookies);
        Assert.False(handler.UseProxy);
        Assert.Null(handler.Credentials);
        Assert.Null(handler.ActivityHeadersPropagator);
        Assert.Equal(DecompressionMethods.None, handler.AutomaticDecompression);
    }

    [Fact]
    public async Task GzipRegistrationRetainsTheDigestOfTheReceivedBytes()
    {
        byte[] compressed = Compress(RecordedNuGetHandler.Fixture("deprecated"));
        using var handler = new RecordedNuGetHandler { Override = url => url.Contains("/registration", StringComparison.Ordinal) ? GzipResponse(compressed) : null };
        using var client = new HttpClient(handler);
        PackageRegistryState state = await new NuGetRegistryProvider(client).ObserveAsync("Example.Widget", "1.0.0", TestContext.Current.CancellationToken);
        Assert.Equal("deprecated", state.Deprecation);
        Assert.Equal(ContentHash.Sha256(compressed), state.Evidence[^1].Hash);
        Assert.Equal(compressed.Length, state.Evidence[^1].Bytes);
        Assert.Equal("gzip", state.Evidence[^1].Encoding);
    }

    [Fact]
    public async Task ACompressedBodyCannotExpandPastTheResponseLimit()
    {
        byte[] expanded = new byte[NuGetRegistryProvider.MaximumResponseBytes + 1];
        RecordedNuGetHandler.Fixture("listed").CopyTo(expanded, 0);
        byte[] compressed = Compress(expanded);
        Assert.True(compressed.Length < NuGetRegistryProvider.MaximumResponseBytes);
        using var handler = new RecordedNuGetHandler { Override = _ => GzipResponse(compressed) };
        using var client = new HttpClient(handler);
        await Assert.ThrowsAsync<ProviderRefusedException>(() => new NuGetRegistryProvider(client).ObserveAsync("Example.Widget", "1.0.0", TestContext.Current.CancellationToken));
    }

    private static HttpResponseMessage GzipResponse(byte[] bytes)
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) };
        response.Content.Headers.ContentEncoding.Add("gzip");
        return response;
    }

    private static byte[] Compress(byte[] bytes)
    {
        using var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionMode.Compress, leaveOpen: true)) gzip.Write(bytes);
        return output.ToArray();
    }

    private sealed class StalledStream : Stream
    {
        internal TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal bool Canceled { get; private set; }
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() => throw new NotSupportedException();
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            Started.TrySetResult();
            try { await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken); return 0; }
            catch (OperationCanceledException) { Canceled = true; throw; }
        }
    }

    private sealed class NonSeekableStream(byte[] bytes) : MemoryStream(bytes)
    {
        public override bool CanSeek => false;
    }
}
