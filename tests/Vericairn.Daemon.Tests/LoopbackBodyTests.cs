// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Buffers.Text;
using System.IO.Pipelines;
using System.Net;
using Microsoft.AspNetCore.Http;
using Vericairn.Protocol;

namespace Vericairn.Daemon.Tests;

[Collection("daemon")]
public class LoopbackBodyTests
{
    [Theory]
    [InlineData("unknown")]
    [InlineData("unadmitted")]
    [InlineData("revoked")]
    public async Task ADeviceWithoutStandingIsRefusedBeforeAnyBodyRead(string standing)
    {
        await using LocalNetworkHarness harness = await LocalNetworkHarness.StartAsync();
        string device = Device(harness, standing);
        var body = new MemoryStream(new byte[1024]);
        DefaultHttpContext context = Request(harness, device, body);
        Problem? problem = await harness.Host.Authenticator.VerifyAsync(context);
        Assert.Equal("denied", problem?.Code);
        Assert.Equal(0, body.Position);
    }

    [Fact]
    public async Task AFailedLoopbackDeviceCheckUsesOnlyTheBoundedDiscardAndNeverDispatches()
    {
        await using LocalNetworkHarness harness = await LocalNetworkHarness.StartAsync();
        DefaultHttpContext context = Request(harness, "unknown", new MemoryStream(new byte[128 * 1024]));
        context.Connection.LocalIpAddress = IPAddress.Loopback;
        PipeReader original = context.Request.BodyReader;
        bool dispatched = false;
        await harness.Host.HandleRequestAsync(context, _ =>
        {
            dispatched = true;
            return Task.CompletedTask;
        });
        Assert.False(dispatched);
        Assert.Equal(StatusCodes.Status401Unauthorized, context.Response.StatusCode);
        Assert.Equal("close", context.Response.Headers.Connection);
        long remaining = 0;
        while (true)
        {
            ReadResult read = await original.ReadAsync(TestContext.Current.CancellationToken);
            remaining += read.Buffer.Length;
            original.AdvanceTo(read.Buffer.End);
            if (read.IsCompleted)
            {
                break;
            }
        }

        Assert.Equal(64 * 1024, remaining);
        await original.CompleteAsync();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AnAdmittedDevicesBodyCannotExceedEightMebibytes(bool declaredLength)
    {
        await using LocalNetworkHarness harness = await LocalNetworkHarness.StartAsync();
        const int Size = 8 * 1024 * 1024 + 1;
        var body = new MemoryStream(new byte[Size]);
        DefaultHttpContext context = Request(harness, harness.OperatorDevice, body);
        if (declaredLength)
        {
            context.Request.ContentLength = Size;
        }

        BadHttpRequestException refused = await Assert.ThrowsAsync<BadHttpRequestException>(() => harness.Host.Authenticator.VerifyAsync(context));
        Assert.Equal(StatusCodes.Status413PayloadTooLarge, refused.StatusCode);
        if (declaredLength)
        {
            Assert.Equal(0, body.Position);
        }
    }

    [Fact]
    public async Task ABodyReadHasAThirtySecondDeadlineEvenWithAFreshSignature()
    {
        await using LocalNetworkHarness harness = await LocalNetworkHarness.StartAsync();
        var time = new DeadlineTime();
        var authenticator = new RequestAuthenticator(harness.Host.Service, 10, () => harness.Host.IdentityPublicKey, time);
        authenticator.StartAccepting();
        var pipe = new Pipe();
        DefaultHttpContext context = Request(harness, harness.OperatorDevice, pipe.Reader.AsStream(leaveOpen: true));
        context.Request.Headers[RequestSigning.SignedAtHeader] = RequestSigning.FormatTime(time.GetUtcNow());
        using var aborted = new CancellationTokenSource();
        context.RequestAborted = aborted.Token;
        Task<Problem?> verifying = authenticator.VerifyAsync(context);
        try
        {
            Assert.False(verifying.IsCompleted);
            Assert.Equal(TimeSpan.FromSeconds(30), time.Scheduled);
            time.Elapse(TimeSpan.FromSeconds(30));
            Problem? problem = await verifying.WaitAsync(TestTiming.HangTimeout, TestContext.Current.CancellationToken);
            Assert.Equal("denied", problem?.Code);
            Assert.Equal(TimeSpan.FromSeconds(30), time.Scheduled);
            Assert.False(context.RequestAborted.IsCancellationRequested);
        }
        finally
        {
            await aborted.CancelAsync();
            await pipe.Writer.CompleteAsync();
            try
            {
                await verifying;
            }
            catch (OperationCanceledException)
            {
                // A failing implementation must not leave its old five-minute body read running in the test process.
            }

            await pipe.Reader.CompleteAsync();
        }
    }

    private static DefaultHttpContext Request(LocalNetworkHarness harness, string device, Stream body)
    {
        var context = new DefaultHttpContext();
        context.Request.Method = "POST";
        context.Request.Path = "/v1/objects";
        context.Request.Body = body;
        context.Response.Body = new MemoryStream();
        context.Request.Headers[RequestSigning.DeviceHeader] = device;
        context.Request.Headers[RequestSigning.SignedAtHeader] = RequestSigning.FormatTime(harness.Time.GetUtcNow());
        context.Request.Headers[RequestSigning.SignatureHeader] = Base64Url.EncodeToString(new byte[64]);
        return context;
    }

    private static string Device(LocalNetworkHarness harness, string standing)
    {
        if (standing == "unknown")
        {
            return "unknown";
        }

        WriteOutcome created = harness.Socket.DeviceCreate(new DeviceCreateRequest(harness.Estate.BuilderId, "Loopback body test", "file"));
        Assert.True(created.Ok, created.Problem?.Message);
        string device = created.Written!.Id;
        if (standing == "revoked")
        {
            Assert.True(harness.Socket.DeviceAdmit(new AdmitRequest(device, harness.Estate.OperatorId, "Test admission.")).Ok);
            Assert.True(harness.Socket.DeviceRevoke(new RevokeRequest(device, harness.Estate.OperatorId, "Test revocation.")).Ok);
        }

        return device;
    }

    private sealed class DeadlineTime : TimeProvider
    {
        private readonly DateTimeOffset now = DateTimeOffset.FromUnixTimeSeconds(DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        private readonly ClientClock timers = new();

        public TimeSpan? Scheduled { get; private set; }

        public override DateTimeOffset GetUtcNow() => now;
        internal void Elapse(TimeSpan elapsed) => timers.Elapse(elapsed);

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            Scheduled = dueTime;
            return timers.CreateTimer(callback, state, dueTime, period);
        }
    }
}
