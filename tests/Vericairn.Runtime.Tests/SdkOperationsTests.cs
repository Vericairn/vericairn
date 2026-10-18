// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using Vericairn.Protocol;
using Vericairn.Sdk;

namespace Vericairn.Runtime.Tests;

public sealed class SdkOperationsTests
{
    [Fact]
    public void EverySynchronousApiOperationHasATypedCancellableCounterpart()
    {
        MethodInfo[] synchronous = typeof(IEstateApi).GetMethods().Where(m => !m.Name.EndsWith("Async", StringComparison.Ordinal)).ToArray();
        Assert.Equal(67, synchronous.Length);
        foreach (MethodInfo method in synchronous)
        {
            Type[] parameters = [.. method.GetParameters().Select(p => p.ParameterType), typeof(CancellationToken)];
            MethodInfo? counterpart = typeof(IEstateApi).GetMethod(method.Name + "Async", parameters);
            Assert.NotNull(counterpart);
            Assert.Equal(typeof(Task<>).MakeGenericType(method.ReturnType), counterpart.ReturnType);
            Assert.NotNull(typeof(VericairnClient).GetMethod(counterpart.Name, parameters));
            Assert.Equal(method.GetParameters().Select(p => p.DefaultValue), counterpart.GetParameters().SkipLast(1).Select(p => p.DefaultValue));
        }
    }

    [Fact]
    public async Task SlowReadYieldsAndCallerCancellationReachesTheTransport()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var interrupted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var client = Client(async (_, token) =>
        {
            entered.SetResult();
            try { await Task.Delay(Timeout.InfiniteTimeSpan, token); }
            finally { interrupted.TrySetResult(); }
            return Json(new { });
        });
        using var stop = new CancellationTokenSource();
        Task<HealthResponse> request = client.HealthAsync(stop.Token);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Assert.False(request.IsCompleted);
        stop.Cancel();
        ClientCancelledException failure = await Assert.ThrowsAsync<ClientCancelledException>(() => request);
        Assert.False(failure.OutcomeUnknown);
        Assert.Equal(stop.Token, failure.CancellationToken);
        await interrupted.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Assert.Equal("cancelled", ClientFailure.FromException(failure)?.Code);
    }

    [Fact]
    public async Task DeadlineIsDistinctFromCallerCancellation()
    {
        using var client = Client(async (_, token) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return Json(new { });
        }, TimeSpan.FromMilliseconds(50));
        ClientTimeoutException failure = await Assert.ThrowsAsync<ClientTimeoutException>(() => client.HealthAsync(TestContext.Current.CancellationToken));
        Assert.False(failure.OutcomeUnknown);
        Assert.Equal("timed-out", ClientFailure.FromException(failure)?.Code);
    }

    [Fact]
    public async Task DeadlineCoversAResponseBodyThatStallsAfterHeaders()
    {
        using var body = new StalledStream();
        using var client = Client((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(body) }), TimeSpan.FromMilliseconds(50));
        await Assert.ThrowsAsync<ClientTimeoutException>(() => client.HealthAsync(TestContext.Current.CancellationToken));
        await body.Interrupted.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task InterruptedWriteKeepsTheEffectUncertainAndIsNeverRetried(bool cancel)
    {
        int effects = 0;
        var effected = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var client = Client(async (_, token) =>
        {
            Interlocked.Increment(ref effects);
            effected.SetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return Json(new { });
        }, cancel ? TimeSpan.FromSeconds(5) : TimeSpan.FromMilliseconds(100));
        using var stop = new CancellationTokenSource();
        Task<WriteOutcome> request = client.AttemptAsync(new("assignment", "contributor", "workspace", "note"), stop.Token);
        await effected.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        if (cancel) stop.Cancel();
        Exception failure = cancel
            ? await Assert.ThrowsAsync<ClientCancelledException>(() => request)
            : await Assert.ThrowsAsync<ClientTimeoutException>(() => request);
        Assert.True(((IClientOperationFailure)failure).OutcomeUnknown);
        Assert.Contains("reconcile", ClientFailure.FromException(failure)!.Message, StringComparison.Ordinal);
        Assert.Equal(1, effects);
    }

    [Fact]
    public async Task AlreadyCancelledWriteDoesNotReachTheTransport()
    {
        int calls = 0;
        using var client = Client((_, _) => { calls++; return Task.FromResult(Json(new { })); });
        using var stop = new CancellationTokenSource();
        stop.Cancel();
        ClientCancelledException failure = await Assert.ThrowsAsync<ClientCancelledException>(() => client.AttemptAsync(new("assignment", "contributor", "workspace", "note"), stop.Token));
        Assert.False(failure.OutcomeUnknown);
        Assert.Equal(0, calls);
    }

    [Fact]
    public async Task LostWriteResponseIsUncertainButAReadOnlyPostIsNotAnEffect()
    {
        using var client = Client((_, _) => throw new HttpRequestException("connection ended"));
        ClientUnavailableException write = await Assert.ThrowsAsync<ClientUnavailableException>(() => client.ObjectPutAsync([1], TestContext.Current.CancellationToken));
        Assert.True(write.OutcomeUnknown);
        ClientUnavailableException read = await Assert.ThrowsAsync<ClientUnavailableException>(() => client.ResumeHandoffAsync(null!, TestContext.Current.CancellationToken));
        Assert.False(read.OutcomeUnknown);
    }

    [Fact]
    public async Task ExistingResponseShapesAndSynchronousCallsStillWork()
    {
        using var client = Client((request, _) => Task.FromResult(request.RequestUri!.AbsolutePath switch
        {
            "/v1/health" => Json(new HealthResponse("1", "test", "estate", "root", "repository-synced", 2, "now", 1)),
            "/v1/objects/missing" => new HttpResponseMessage(HttpStatusCode.NotFound),
            _ => Json(WriteOutcome.Fail(new Problem("refused", "deliberate refusal", [])), HttpStatusCode.UnprocessableEntity),
        }));
        Assert.Equal("estate", client.Health().Estate);
        Assert.Null(await client.ObjectGetAsync("missing", TestContext.Current.CancellationToken));
        WriteOutcome refused = await client.AttemptAsync(new("assignment", "contributor", "workspace", "note"), TestContext.Current.CancellationToken);
        Assert.False(refused.Ok);
        Assert.Equal("refused", refused.Problem?.Code);
        Assert.True(ClientOperation.Deadline("RunStart") > TimeSpan.FromMinutes(5));
        Assert.True(ClientOperation.Deadline("Health") < TimeSpan.FromMinutes(5));
    }

    [Fact]
    public async Task EmbeddedEffectMayFinishAfterCancellationWithoutBeingCalledAgain()
    {
        using var finish = new ManualResetEventSlim();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var effected = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var stop = new CancellationTokenSource();
        Task<int> request = ClientOperation.RunSynchronousAsync("Attempt", () =>
        {
            entered.SetResult();
            finish.Wait(TimeSpan.FromSeconds(5));
            effected.SetResult();
            return 1;
        }, true, stop.Token);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        stop.Cancel();
        Assert.True((await Assert.ThrowsAsync<ClientCancelledException>(() => request)).OutcomeUnknown);
        finish.Set();
        await effected.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
    }

    private static VericairnClient Client(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond, TimeSpan? timeout = null) =>
        new(new HttpClient(new Handler(respond)) { BaseAddress = new Uri("http://localhost/") }) { OperationTimeout = _ => timeout };

    private static HttpResponseMessage Json<T>(T value, HttpStatusCode status = HttpStatusCode.OK) => new(status) { Content = JsonContent.Create(value, options: ProtocolJson.Options) };

    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => respond(request, cancellationToken);
    }

    private sealed class StalledStream : Stream
    {
        public TaskCompletionSource Interrupted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
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
            try { await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken); }
            finally { Interrupted.TrySetResult(); }
            return 0;
        }
    }
}
