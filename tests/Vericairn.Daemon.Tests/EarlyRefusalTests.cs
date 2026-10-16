// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.IO.Pipelines;
using System.Net.Sockets;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Vericairn.Protocol;

namespace Vericairn.Daemon.Tests;

/// <summary>All HTTP refusals complete before bounded discard, regardless of which stage refused the request.</summary>
[Collection("daemon")]
public class EarlyRefusalTests
{
    [Theory]
    [InlineData("hello", false)]
    [InlineData("hello", true)]
    [InlineData("confirm", false)]
    [InlineData("confirm", true)]
    public async Task PairingSizeRefusalsDoNotParseTheRemainder(string operation, bool chunked)
    {
        await using LocalNetworkHarness harness = await LocalNetworkHarness.StartAsync();
        byte[] body = Encoding.ASCII.GetBytes("{\"mac\":\"" + new string('!', (int)RequestAuthenticator.PairingBodyLimit + 1024));
        Assert.True(body.Length <= DaemonHost.RefusedBodyLimit);
        string framing = chunked ? "Transfer-Encoding: chunked\r\n" : $"Content-Length: {body.Length}\r\n";
        byte[] wireBody = chunked ? Encoding.ASCII.GetBytes($"{body.Length:X}\r\n{Encoding.ASCII.GetString(body)}\r\n0\r\n\r\n") : body;
        byte[] head = Encoding.ASCII.GetBytes($"POST /v1/pairing/{operation} HTTP/1.1\r\nHost: x\r\nContent-Type: application/json\r\nConnection: close\r\n{framing}\r\n");
        string answer = await Raw.ExchangeAsync(harness.Address, null, harness.Host.Endpoint.Loopback!.Certificate, [.. head, .. wireBody], TimeSpan.FromSeconds(15));
        Assert.StartsWith("HTTP/1.1 413", answer, StringComparison.Ordinal);
        Assert.DoesNotContain("<<timeout", answer, StringComparison.Ordinal);
        Assert.Single(harness.Socket.Records("device"));
    }

    [Theory]
    [InlineData("hello")]
    [InlineData("confirm")]
    public async Task WrongLengthMacsAreRefusedWithoutTryingADeviceOrCode(string operation)
    {
        await using LocalNetworkHarness harness = await LocalNetworkHarness.StartAsync();
        string nonce = PairingProtocol.NewNonce();
        string json = operation == "hello" ? $$"""{"nonce":"{{nonce}}","mac":"AA"}""" : $$"""{"runtimeNonce":"{{nonce}}","device":null,"signature":"not a signature","mac":"AA"}""";
        byte[] body = Encoding.UTF8.GetBytes(json);
        Assert.True(body.Length <= DaemonHost.RefusedBodyLimit);
        byte[] head = Encoding.ASCII.GetBytes($"POST /v1/pairing/{operation} HTTP/1.1\r\nHost: x\r\nContent-Type: application/json\r\nContent-Length: {body.Length}\r\nConnection: close\r\n\r\n");
        string answer = await Raw.ExchangeAsync(harness.Address, null, harness.Host.Endpoint.Loopback!.Certificate, [.. head, .. body], TimeSpan.FromSeconds(15));
        Assert.StartsWith("HTTP/1.1 422", answer, StringComparison.Ordinal);
        Assert.DoesNotContain("<<timeout", answer, StringComparison.Ordinal);
        Assert.Single(harness.Socket.Records("device"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SizeLimitsPreserveTheOriginalReaderForDiscard(bool knownLength)
    {
        await using LocalNetworkHarness harness = await LocalNetworkHarness.StartAsync();
        var context = new DefaultHttpContext();
        context.Request.Method = "POST";
        context.Request.Path = "/v1/objects";
        context.Request.Body = new MemoryStream(Encoding.ASCII.GetBytes(new string('!', 32)));
        context.Response.Body = new MemoryStream();
        context.Features.Set<IHttpMaxRequestBodySizeFeature>(new BodySize(8));
        if (knownLength)
        {
            context.Request.ContentLength = 32;
        }

        PipeReader original = context.Request.BodyReader;
        bool acted = false;
        await harness.Host.HandleRequestAsync(context, async request =>
        {
            using var buffer = new MemoryStream();
            await request.Request.Body.CopyToAsync(buffer, TestContext.Current.CancellationToken);
            acted = true;
        });
        Assert.False(acted);
        Assert.Equal(StatusCodes.Status413PayloadTooLarge, context.Response.StatusCode);
        Assert.Equal("close", context.Response.Headers.Connection);
        ReadResult remainder = await original.ReadAsync(TestContext.Current.CancellationToken);
        Assert.True(remainder.Buffer.IsEmpty);
        original.AdvanceTo(remainder.Buffer.End);
        await original.CompleteAsync();
    }

    [Theory]
    [InlineData(400)]
    [InlineData(401)]
    [InlineData(404)]
    [InlineData(405)]
    [InlineData(413)]
    [InlineData(415)]
    [InlineData(422)]
    [InlineData(503)]
    public async Task AnyHttpRefusalDiscardsWithoutDispatchingItsUnreadRemainder(int status)
    {
        await using LocalNetworkHarness harness = await LocalNetworkHarness.StartAsync();
        var context = new DefaultHttpContext();
        context.Request.Body = new MemoryStream("never parse this"u8.ToArray());
        context.Response.Body = new MemoryStream();
        context.Features.Set<IHttpMaxRequestBodySizeFeature>(new BodySize(1));
        PipeReader original = context.Request.BodyReader;
        await harness.Host.HandleRequestAsync(context, request =>
        {
            request.Response.StatusCode = status;
            return Task.CompletedTask;
        });
        ReadResult remainder = await original.ReadAsync(TestContext.Current.CancellationToken);
        Assert.True(remainder.Buffer.IsEmpty);
        original.AdvanceTo(remainder.Buffer.End);
        await original.CompleteAsync();
    }

    [Theory]
    [InlineData("hello", false)]
    [InlineData("hello", true)]
    [InlineData("confirm", false)]
    [InlineData("confirm", true)]
    public async Task BindingRefusalsCompleteBeforeTheirLateUnparsedBody(string operation, bool malformedJson)
    {
        await using LocalNetworkHarness harness = await LocalNetworkHarness.StartAsync();
        byte[] body = Encoding.ASCII.GetBytes(new string('!', 8 * 1024));
        Assert.True(body.Length <= DaemonHost.RefusedBodyLimit);
        string contentType = malformedJson ? "application/json" : "application/octet-stream";
        byte[] head = Encoding.ASCII.GetBytes($"POST /v1/pairing/{operation} HTTP/1.1\r\nHost: x\r\nContent-Type: {contentType}\r\nContent-Length: {body.Length}\r\nConnection: close\r\n\r\n");
        string answer = await Raw.ExchangeAsync(harness.Address, null, harness.Host.Endpoint.Loopback!.Certificate, [.. head, body[0]], TimeSpan.FromSeconds(15), trickle: body[1..], meanwhile: () => { });
        Assert.StartsWith(malformedJson ? "HTTP/1.1 400" : "HTTP/1.1 415", answer, StringComparison.Ordinal);
        Assert.DoesNotContain("<<timeout", answer, StringComparison.Ordinal);
        Assert.Single(harness.Socket.Records("device"));
    }

    [Fact]
    public async Task TheLanScreenSendsItsFatalAlertBeforeDiscardingUnparsedTransportBytes()
    {
        await using LocalNetworkHarness harness = await LocalNetworkHarness.StartAsync();
        using var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
        await socket.ConnectAsync(harness.Host.Endpoint.LocalNetwork!.Host, harness.Host.Endpoint.LocalNetwork.Port, TestContext.Current.CancellationToken);
        await using var stream = new NetworkStream(socket, ownsSocket: false);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(5));
        // An impossible TLS record type is refused by the screen before the platform TLS sees it.
        await stream.WriteAsync(new byte[] { 99, 3, 3, 0, 0 }, deadline.Token);
        byte[] alert = new byte[7];
        await stream.ReadExactlyAsync(alert, deadline.Token);
        Assert.Equal(new byte[] { 21, 3, 3, 0, 2, 2, 40 }, alert);
        byte[] remainder = new byte[32 * 1024];
        Assert.True(remainder.Length <= DaemonHost.RefusedBodyLimit);
        await stream.WriteAsync(remainder, deadline.Token);
        socket.Shutdown(SocketShutdown.Send);
        Assert.Equal(0, await stream.ReadAsync(new byte[1], deadline.Token));
        Assert.Equal(harness.Estate.Estate.Id, harness.Socket.Health().Estate);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TheLimitNeverOffersExcessBytesAndCleanupBypassesIt(bool synchronous)
    {
        var pipe = new Pipe();
        var limited = new LimitedBodyReader(pipe.Reader, 8);
        await pipe.Writer.WriteAsync("1234"u8.ToArray(), TestContext.Current.CancellationToken);
        ReadResult first = await limited.ReadAsync(TestContext.Current.CancellationToken);
        Assert.Equal(4, first.Buffer.Length);
        limited.AdvanceTo(first.Buffer.End);
        await pipe.Writer.WriteAsync("not JSON"u8.ToArray(), TestContext.Current.CancellationToken);
        await pipe.Writer.CompleteAsync();
        if (synchronous)
        {
            Microsoft.AspNetCore.Http.BadHttpRequestException refused = Assert.Throws<Microsoft.AspNetCore.Http.BadHttpRequestException>(() => limited.TryRead(out _));
            Assert.Equal(StatusCodes.Status413PayloadTooLarge, refused.StatusCode);
        }
        else
        {
            Microsoft.AspNetCore.Http.BadHttpRequestException refused = await Assert.ThrowsAsync<Microsoft.AspNetCore.Http.BadHttpRequestException>(async () => await limited.ReadAsync(TestContext.Current.CancellationToken));
            Assert.Equal(StatusCodes.Status413PayloadTooLarge, refused.StatusCode);
        }

        Assert.Equal(8, await DaemonHost.DrainRefusedBodyAsync(pipe.Reader, DaemonHost.RefusedBodyLimit, TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
        await pipe.Reader.CompleteAsync();
    }

    private sealed class BodySize(long maximum) : IHttpMaxRequestBodySizeFeature
    {
        public bool IsReadOnly => false;

        public long? MaxRequestBodySize { get; set; } = maximum;
    }
}
