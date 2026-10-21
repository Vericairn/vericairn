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
        await DaemonHost.WriteProblemAsync(context, StatusCodes.Status401Unauthorized, new Problem("denied", "Sign it afresh.", []), close: true);
        Task refused = DaemonHost.CompleteRefusalAsync(context, body.Reader, new ClientClock());
        await response.Completed.Task.WaitAsync(TestTiming.HangTimeout, TestContext.Current.CancellationToken);
        Assert.Equal(StatusCodes.Status401Unauthorized, context.Response.StatusCode);
        Assert.Equal("close", context.Response.Headers.Connection);
        byte[] answer = response.Bytes.ToArray();
        Assert.Equal(answer.Length, context.Response.ContentLength);
        Assert.Equal("denied", JsonSerializer.Deserialize<Problem>(answer, ProtocolJson.Options)!.Code);
        Assert.False(refused.IsCompleted); // Closing here would leave the next TLS records unread and could erase this response.

        await body.Writer.WriteAsync("the remaining body"u8.ToArray(), TestContext.Current.CancellationToken);
        await body.Writer.CompleteAsync();
        await refused.WaitAsync(TestTiming.HangTimeout, TestContext.Current.CancellationToken);
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
        Assert.Equal(8, await DaemonHost.DrainRefusedBodyAsync(body.Reader, 8, TestTiming.HangTimeout, new ClientClock(), TestContext.Current.CancellationToken));
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
        var clock = new ClientClock();
        Task<long> drain = DaemonHost.DrainRefusedBodyAsync(body.Reader, 64 * 1024, TimeSpan.FromSeconds(30), clock, aborted.Token);
        Assert.False(drain.IsCompleted);
        if (disconnect)
        {
            aborted.Cancel();
        }
        else clock.Elapse(TimeSpan.FromSeconds(30));

        Assert.Equal(0, await drain.WaitAsync(TestTiming.HangTimeout, TestContext.Current.CancellationToken));
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
