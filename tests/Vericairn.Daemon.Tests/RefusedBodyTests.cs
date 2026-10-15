// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.IO.Pipelines;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Vericairn.Protocol;

namespace Vericairn.Daemon.Tests;

/// <summary>A completed 401 survives cleanup of a late body, without unbounded reads or transport cancellation.</summary>
public class RefusedBodyTests
{
    [Fact]
    public async Task The401IsCompleteBeforeTheUnreadBodyIsDrainedAndTheRequestReturns()
    {
        var body = new Pipe();
        var response = new CompletedResponse();
        var context = new DefaultHttpContext();
        context.Features.Set<IHttpResponseBodyFeature>(response);
        Task refused = DaemonHost.RefuseAuthenticationAsync(context, body.Reader, new Problem("denied", "Sign it afresh.", []));
        await response.Completed.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Assert.Equal(StatusCodes.Status401Unauthorized, context.Response.StatusCode);
        Assert.Equal("close", context.Response.Headers.Connection);
        byte[] answer = response.Bytes.ToArray();
        Assert.Equal(answer.Length, context.Response.ContentLength);
        Assert.Equal("denied", JsonSerializer.Deserialize<Problem>(answer, ProtocolJson.Options)!.Code);
        Assert.False(refused.IsCompleted); // Closing here would leave the next TLS records unread and could erase this response.

        await body.Writer.WriteAsync("the remaining body"u8.ToArray(), TestContext.Current.CancellationToken);
        await body.Writer.CompleteAsync();
        await refused.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        ReadResult remainder = await body.Reader.ReadAsync(TestContext.Current.CancellationToken);
        Assert.True(remainder.IsCompleted);
        Assert.True(remainder.Buffer.IsEmpty);
        body.Reader.AdvanceTo(remainder.Buffer.End);
        await body.Reader.CompleteAsync();
    }

    [Fact]
    public async Task CleanupConsumesNoMoreThanItsByteBudget()
    {
        var body = new Pipe();
        await body.Writer.WriteAsync(Encoding.ASCII.GetBytes(new string('x', 32)), TestContext.Current.CancellationToken);
        Assert.Equal(8, await DaemonHost.DrainRefusedBodyAsync(body.Reader, 8, TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
        ReadResult remainder = await body.Reader.ReadAsync(TestContext.Current.CancellationToken);
        Assert.Equal(24, remainder.Buffer.Length);
        body.Reader.AdvanceTo(remainder.Buffer.End);
        await body.Writer.CompleteAsync();
        await body.Reader.CompleteAsync();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CleanupStopsAtItsDeadlineOrDisconnectWithoutCancellingTheTransport(bool disconnect)
    {
        var body = new Pipe();
        using var aborted = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        Task<long> drain = DaemonHost.DrainRefusedBodyAsync(body.Reader, 64 * 1024, disconnect ? TimeSpan.FromSeconds(30) : TimeSpan.FromMilliseconds(50), aborted.Token);
        if (disconnect)
        {
            aborted.Cancel();
        }

        Assert.Equal(0, await drain.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
        // Only a pending pipe read was cancelled: neither end was completed or its stream cancelled. Data still travels afterwards.
        await body.Writer.WriteAsync("still live"u8.ToArray(), TestContext.Current.CancellationToken);
        ReadResult after = await body.Reader.ReadAsync(TestContext.Current.CancellationToken);
        Assert.False(after.IsCanceled);
        Assert.Equal(10, after.Buffer.Length);
        body.Reader.AdvanceTo(after.Buffer.End);
        await body.Writer.CompleteAsync();
        await body.Reader.CompleteAsync();
    }

    private sealed class CompletedResponse : IHttpResponseBodyFeature
    {
        public MemoryStream Bytes { get; } = new();

        public TaskCompletionSource Completed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Stream Stream => Bytes;

        public PipeWriter Writer => throw new NotSupportedException();

        public void DisableBuffering() { }

        public Task StartAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task SendFileAsync(string path, long offset, long? count, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task CompleteAsync()
        {
            Completed.SetResult();
            return Task.CompletedTask;
        }
    }
}
