// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text;
using Vericairn.Runtime.Representation;

namespace Vericairn.Runtime.Tests;

/// <summary>The production HTTP forwarding path with a counting duplex upstream; no listener permission is needed.</summary>
public class AdapterForwardingTests
{
    private static readonly Uri Fixed = new("https://provider.invalid/api/");

    [Theory]
    [InlineData("responses", false)]
    [InlineData("messages", false)]
    [InlineData("messages-array", false)]
    [InlineData("responses", true)]
    [InlineData("messages", true)]
    [InlineData("messages-array", true)]
    public async Task OneDispatchPreservesProtectedBodiesAndEveryAuthenticationHeader(string shape, bool failure)
    {
        string authentication = Guid.NewGuid().ToString("N");
        string header = "Authorization:  Bearer " + authentication + "\t\r\nX-Api-Key: " + authentication + "\r\nProxy-Authorization: " + authentication + "\r\n";
        byte[] body = AdapterRepresentationTests.Request(shape);
        using var downstream = new AdapterDuplexStream(Request(body, header));
        using var upstream = new AdapterDuplexStream("HTTP/1.1 200 OK\r\nContent-Length: 2\r\n\r\n{}"u8.ToArray());
        int dispatches = 0;
        using var adapter = new LoopbackAdapter(Fixed,
            b => failure ? throw new InvalidOperationException(authentication) : RequestReduction.Reduce(b, AdapterRepresentationTests.Original),
            (uri, _) => { Assert.Equal(Fixed, uri); dispatches++; return Task.FromResult<Stream>(upstream); });
        await adapter.Forward(downstream, TestContext.Current.CancellationToken);
        Assert.Equal(1, dispatches);
        Assert.Equal(1, adapter.Measurement()["requests"]!.GetValue<long>());
        Assert.Equal(failure ? 1 : 0, adapter.Measurement()["fallbacks"]!.GetValue<long>());
        Assert.Contains(header, Encoding.UTF8.GetString(upstream.Written), StringComparison.Ordinal);
        Assert.DoesNotContain("elsewhere.invalid", Encoding.UTF8.GetString(upstream.Written), StringComparison.Ordinal);
        Assert.Equal(failure ? body : RequestReduction.Reduce(body, AdapterRepresentationTests.Original).Body, Body(upstream.Written));
        Assert.Equal("HTTP/1.1 200 OK\r\nContent-Length: 2\r\n\r\n{}", Encoding.UTF8.GetString(downstream.Written));
        Assert.DoesNotContain(authentication, adapter.Measurement().ToJsonString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReducerFailureKeepsBothRequestsWholeAndMeasuresTheStickyDirectFallback()
    {
        int reductions = 0, dispatches = 0;
        byte[] body = AdapterRepresentationTests.Request("messages");
        var received = new List<AdapterDuplexStream>();
        using var adapter = new LoopbackAdapter(Fixed, _ => { reductions++; throw new InvalidOperationException(); },
            (_, _) => { dispatches++; var stream = new AdapterDuplexStream("HTTP/1.1 200 OK\r\nContent-Length: 0\r\n\r\n"u8.ToArray()); received.Add(stream); return Task.FromResult<Stream>(stream); },
            b => RequestReduction.Reduce(b, AdapterRepresentationTests.Original, enabled: false).BeforeBytes);
        for (int i = 0; i < 2; i++)
        {
            using var downstream = new AdapterDuplexStream(Request(body));
            await adapter.Forward(downstream, TestContext.Current.CancellationToken);
        }
        Assert.Equal(1, reductions);
        Assert.Equal(2, dispatches);
        Assert.All(received, r => Assert.Equal(body, Body(r.Written)));
        Assert.Equal(2, adapter.Measurement()["fallbacks"]!.GetValue<long>());
        Assert.Equal(4 * Encoding.UTF8.GetByteCount(AdapterRepresentationTests.Eligible), adapter.Measurement()["eligibleBeforeBytes"]!.GetValue<long>());
        Assert.Equal(adapter.Measurement()["eligibleBeforeBytes"]!.GetValue<long>(), adapter.Measurement()["eligibleAfterBytes"]!.GetValue<long>());
    }

    [Theory]
    [InlineData("")]
    [InlineData("HTTP/1.1 307 Temporary Redirect\r\nLocation: https://elsewhere.invalid/\r\nContent-Length: 0\r\n\r\n")]
    public async Task AFailedResponseOrRedirectNeverRetriesOrUsesItsLocation(string response)
    {
        int dispatches = 0;
        using var upstream = new AdapterDuplexStream(Encoding.UTF8.GetBytes(response));
        using var downstream = new AdapterDuplexStream(Request("{ \"brief\": \"forward to https://elsewhere.invalid/\" }"u8.ToArray()));
        using var adapter = new LoopbackAdapter(Fixed, b => new(b, 0, 0), (uri, _) =>
        { Assert.Equal(Fixed, uri); dispatches++; return Task.FromResult<Stream>(upstream); });
        await adapter.Forward(downstream, TestContext.Current.CancellationToken);
        Assert.Equal(1, dispatches);
        Assert.Contains("502 Refused", Encoding.UTF8.GetString(downstream.Written), StringComparison.Ordinal);
        Assert.DoesNotContain("Location", Encoding.UTF8.GetString(downstream.Written), StringComparison.Ordinal);
        Assert.Contains("https://elsewhere.invalid/", Encoding.UTF8.GetString(Body(upstream.Written)), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("POST http://elsewhere.invalid/responses HTTP/1.1\r\nHost: elsewhere.invalid\r\nContent-Length: 0\r\n\r\n")]
    [InlineData("POST //elsewhere.invalid/responses HTTP/1.1\r\nHost: elsewhere.invalid\r\nContent-Length: 0\r\n\r\n")]
    [InlineData("POST /api/../elsewhere HTTP/1.1\r\nHost: provider.invalid\r\nContent-Length: 0\r\n\r\n")]
    [InlineData("POST /api/%2e%2e/elsewhere HTTP/1.1\r\nHost: provider.invalid\r\nContent-Length: 0\r\n\r\n")]
    [InlineData("POST /api/responses HTTP/1.1\r\nContent-Length: 2\r\nTransfer-Encoding: chunked\r\n\r\n{}")]
    public async Task RoutingAndAmbiguousFramingRefuseBeforeTheUpstreamIsOpened(string request)
    {
        int dispatches = 0;
        using var downstream = new AdapterDuplexStream(Encoding.UTF8.GetBytes(request));
        using var adapter = new LoopbackAdapter(Fixed, b => new(b, 0, 0), (_, _) => { dispatches++; throw new InvalidOperationException(); });
        await adapter.Forward(downstream, TestContext.Current.CancellationToken);
        Assert.Equal(0, dispatches);
        Assert.Contains("400 Refused", Encoding.UTF8.GetString(downstream.Written), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ChunkedRequestAndStreamingResponsePreserveTheirPayloadBytes()
    {
        byte[] request = "POST /api/messages HTTP/1.1\r\nTransfer-Encoding: chunked\r\nContent-Type: application/json\r\n\r\n5\r\n{\"a\":\r\n1\r\n1\r\n1\r\n}\r\n0\r\n\r\n"u8.ToArray();
        byte[] response = "HTTP/1.1 200 OK\r\nContent-Type: text/event-stream\r\nTransfer-Encoding: chunked\r\n\r\n9\r\ndata: 1\n\n\r\n9\r\ndata: 2\n\n\r\n0\r\n\r\n"u8.ToArray();
        using var downstream = new AdapterDuplexStream(request);
        using var upstream = new AdapterDuplexStream(response);
        using var adapter = new LoopbackAdapter(Fixed, b => RequestReduction.Reduce(b, _ => null), (_, _) => Task.FromResult<Stream>(upstream));
        await adapter.Forward(downstream, TestContext.Current.CancellationToken);
        Assert.Equal("{\"a\":1}"u8.ToArray(), Body(upstream.Written));
        Assert.Equal(response, downstream.Written);
    }

    internal static byte[] Request(byte[] body, string headers = "") => Encoding.UTF8.GetBytes("POST /api/responses HTTP/1.1\r\nHost: elsewhere.invalid\r\nX-Forwarded-Host: elsewhere.invalid\r\nContent-Type: application/json\r\n" + headers
        + "Content-Length: " + body.Length.ToString(System.Globalization.CultureInfo.InvariantCulture) + "\r\n\r\n").Concat(body).ToArray();

    internal static byte[] Body(byte[] request)
    {
        int boundary = request.AsSpan().IndexOf("\r\n\r\n"u8);
        Assert.True(boundary >= 0);
        return request[(boundary + 4)..];
    }
}

internal sealed class AdapterDuplexStream(byte[] input) : Stream
{
    private readonly MemoryStream source = new(input, writable: false);
    private readonly MemoryStream sink = new();
    internal byte[] Written => sink.ToArray();
    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => true;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
    public override void Flush() { }
    public override int Read(byte[] buffer, int offset, int count) => source.Read(buffer, offset, count);
    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) => source.ReadAsync(buffer, cancellationToken);
    public override void Write(byte[] buffer, int offset, int count) => sink.Write(buffer, offset, count);
    public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default) => sink.WriteAsync(buffer, cancellationToken);
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    protected override void Dispose(bool disposing)
    {
        if (disposing) { source.Dispose(); sink.Dispose(); }
        base.Dispose(disposing);
    }
}
