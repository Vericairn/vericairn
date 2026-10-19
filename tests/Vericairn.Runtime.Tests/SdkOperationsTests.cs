// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json;
using Vericairn.Protocol;
using Vericairn.Sdk;

namespace Vericairn.Runtime.Tests;

public sealed class SdkOperationsTests
{
    [Theory]
    [InlineData("transport")]
    [InlineData("response")]
    [InlineData("cancelled")]
    [InlineData("json")]
    [InlineData("io")]
    public void EverySynchronousRequestPreservesTheOriginalException(string kind)
    {
        Exception original = kind switch
        {
            "transport" => new HttpRequestException("connection failed"),
            "response" => new HttpRequestException("runtime refused the request", null, HttpStatusCode.ServiceUnavailable),
            "cancelled" => new TaskCanceledException("legacy timeout"),
            "json" => new JsonException("unreadable response"),
            _ => new IOException("response ended"),
        };
        int calls = 0;
        using var client = Client((_, _) => { calls++; throw original; }, TimeSpan.Zero);
        MethodInfo[] methods = typeof(VericairnClient).GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(method => !method.IsSpecialName && !method.Name.EndsWith("Async", StringComparison.Ordinal) && method.Name != "Dispose").ToArray();
        foreach (MethodInfo method in methods)
        {
            object?[] arguments = method.GetParameters().Select(parameter => parameter.HasDefaultValue ? parameter.DefaultValue :
                parameter.ParameterType == typeof(string) ? (object)"argument" : parameter.ParameterType == typeof(byte[]) ? new byte[] { 1 } : null).ToArray();
            TargetInvocationException reflected = Assert.Throws<TargetInvocationException>(() => method.Invoke(client, arguments));
            Assert.Same(original, reflected.InnerException);
        }
        Assert.Equal(methods.Length, calls);
    }

    [Fact]
    public async Task LegacyNullResultsRemainNullWhileAnAsyncWriteWithoutAResultIsUncertain()
    {
        using var client = Client((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("null") }));
        Assert.Null(client.Health());
        Assert.Null(client.Attempt(new("assignment", "contributor", "workspace", "note")));
        Assert.True((await Assert.ThrowsAsync<ClientUnavailableException>(() =>
            client.AttemptAsync(new("assignment", "contributor", "workspace", "note"), TestContext.Current.CancellationToken))).OutcomeUnknown);
    }

    [Fact]
    public async Task AnAbsentReleaseBasisIsNullAcrossSynchronousAsyncAndBoundClients()
    {
        using var client = Client((request, _) =>
        {
            Assert.Equal("/v1/release-bases/missing", request.RequestUri!.AbsolutePath);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        });
        Assert.Null(client.ReleaseBasis("missing"));
        Assert.Null(await client.ReleaseBasisAsync("missing", TestContext.Current.CancellationToken));
        Assert.Null(new CancellationBoundApi(client, TestContext.Current.CancellationToken).ReleaseBasis("missing"));
    }

    [Theory]
    [InlineData("kinds")]
    [InlineData("evaluate")]
    [InlineData("access")]
    public async Task PolicyKeysCannotCollideWithNamedReadRoutes(string key)
    {
        using var client = Client((request, _) =>
        {
            Assert.Equal("/v1/estate-policies/" + key + "/view", request.RequestUri!.AbsolutePath);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        });
        Assert.Null(client.Policy(key));
        Assert.Null(await client.PolicyAsync(key, TestContext.Current.CancellationToken));
        Assert.Null(new CancellationBoundApi(client, TestContext.Current.CancellationToken).Policy(key));
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    public async Task ADefiniteRuntimeRefusalIsNeverUnavailableOrUncertain(HttpStatusCode status)
    {
        const string message = "The runtime cannot keep which paired device asks for this write, so it writes nothing.";
        using var client = Client((_, _) => Task.FromResult(Json(new Problem("refused", message, []), status)));
        ClientRefusedException refused = await Assert.ThrowsAsync<ClientRefusedException>(() =>
            client.NewRecordAsync(null!, TestContext.Current.CancellationToken));
        Assert.Equal(status, refused.StatusCode);
        Assert.False(refused.OutcomeUnknown);
        Assert.Equal(new ClientFailure("refused", message, false, 2), ClientFailure.FromException(refused));
        // The synchronous method still exposes HttpClient's exact status exception and message.
        using var response = new HttpResponseMessage(status);
        HttpRequestException original = Assert.Throws<HttpRequestException>(() => response.EnsureSuccessStatusCode());
        HttpRequestException legacy = Assert.Throws<HttpRequestException>(() => client.NewRecord(null!));
        Assert.Equal(original.Message, legacy.Message);
        Assert.Equal(original.StatusCode, legacy.StatusCode);
    }

    [Fact]
    public async Task ATlsRefusalRetainsItsDiagnosticWithoutImplyingAnEffect()
    {
        var original = new HttpRequestException(HttpRequestError.SecureConnectionError, "TLS peer refused", new System.Security.Authentication.AuthenticationException("The certificate was refused."));
        using var client = Client((_, _) => throw original);
        Assert.Same(original, Assert.Throws<HttpRequestException>(() => client.NewRecord(null!)));
        ClientRefusedException refused = await Assert.ThrowsAsync<ClientRefusedException>(() => client.NewRecordAsync(null!, TestContext.Current.CancellationToken));
        Assert.False(refused.OutcomeUnknown);
        Assert.Equal("refused", ClientFailure.FromException(refused)!.Code);
        Assert.DoesNotContain("unknown", refused.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(HttpRequestError.NameResolutionError)]
    [InlineData(HttpRequestError.ConnectionError)]
    public async Task AConnectionThatNeverOpenedHasNoUncertainWrite(HttpRequestError error)
    {
        using var client = Client((_, _) => throw new HttpRequestException(error, "connection failed"));
        Assert.False((await Assert.ThrowsAsync<ClientUnavailableException>(() => client.NewRecordAsync(null!, TestContext.Current.CancellationToken))).OutcomeUnknown);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AStagedOperationBecomesUncertainOnlyWhenItsEffectBegins(bool confirm)
    {
        var prepared = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var proceed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var sent = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        bool dispatched = false;
        using var stop = new CancellationTokenSource();
        Task<bool> pending = ClientOperation.RunAsync("Pair", async token =>
        {
            prepared.SetResult();
            await proceed.Task.WaitAsync(token);
            Volatile.Write(ref dispatched, true);
            sent.SetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return true;
        }, () => Volatile.Read(ref dispatched), cancellationToken: stop.Token);
        await prepared.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        if (confirm)
        {
            proceed.SetResult();
            await sent.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        }
        stop.Cancel();
        ClientCancelledException failure = await Assert.ThrowsAsync<ClientCancelledException>(() => pending);
        Assert.Equal(confirm, failure.OutcomeUnknown);
    }

    [Fact]
    public async Task CancelledPairingAndConnectionProbeStopBeforeOpeningAConnection()
    {
        using var stop = new CancellationTokenSource();
        stop.Cancel();
        ClientCancelledException pairing = await Assert.ThrowsAsync<ClientCancelledException>(() =>
            RuntimePairing.PairAsync("unused", "unused", new PairingOptions("Test device"), stop.Token));
        Assert.False(pairing.OutcomeUnknown);
        string testHome = Path.Combine(Path.GetTempPath(), "vericairn-pre-cancelled-" + Guid.NewGuid().ToString("N"));
        var connection = new RuntimeConnection("unused", "unused", "unused", null, "unused", "unused", "unused", "unused", null, [], "2026-10-18T00:00:00Z");
        ClientCancelledException probe = await Assert.ThrowsAsync<ClientCancelledException>(() =>
            new RuntimeConnections(testHome).ProbeAsync(connection, cancellationToken: stop.Token));
        Assert.False(probe.OutcomeUnknown);
        Assert.False(Directory.Exists(testHome));
    }

    [Fact]
    public void EverySynchronousApiOperationHasATypedCancellableCounterpart()
    {
        MethodInfo[] synchronous = typeof(IEstateApi).GetMethods().Where(m => !m.Name.EndsWith("Async", StringComparison.Ordinal)).ToArray();
        Assert.Equal(90, synchronous.Length);
        foreach (MethodInfo method in synchronous)
        {
            Type[] parameters = [.. method.GetParameters().Select(p => p.ParameterType), typeof(CancellationToken)];
            MethodInfo? counterpart = typeof(IEstateApi).GetMethod(method.Name + "Async", parameters);
            Assert.NotNull(counterpart);
            Assert.Equal(typeof(Task<>).MakeGenericType(method.ReturnType), counterpart.ReturnType);
            Assert.NotNull(typeof(VericairnClient).GetMethod(counterpart.Name, parameters));
            Assert.Equal(method.GetParameters().Select(p => p.DefaultValue), counterpart.GetParameters().SkipLast(1).Select(p => p.DefaultValue));
        }

        foreach (MethodInfo method in typeof(VericairnClient).GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
            .Where(method => !method.IsSpecialName && !method.Name.EndsWith("Async", StringComparison.Ordinal) && method.Name is not ("Dispose" or "Loopback" or "LocalNetwork")))
        {
            MethodInfo? counterpart = typeof(VericairnClient).GetMethod(method.Name + "Async", [.. method.GetParameters().Select(p => p.ParameterType), typeof(CancellationToken)]);
            Assert.NotNull(counterpart);
            Assert.Equal(method.ReturnType == typeof(void) ? typeof(Task) : typeof(Task<>).MakeGenericType(method.ReturnType), counterpart.ReturnType);
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

    [Theory]
    [InlineData("invalid-json")]
    [InlineData("null")]
    [InlineData("empty-outcome")]
    [InlineData("conflicting-outcome")]
    [InlineData("body-lost")]
    [InlineData("server-error")]
    public async Task AnUnusableWriteResponseNeverBecomesAKnownFailure(string response)
    {
        int effects = 0;
        using var client = Client((_, _) =>
        {
            effects++;
            if (response == "body-lost") throw new IOException("response ended after the effect");
            return Task.FromResult(new HttpResponseMessage(response == "server-error" ? HttpStatusCode.InternalServerError : HttpStatusCode.OK)
            {
                Content = new StringContent(response switch
                {
                    "null" => "null",
                    "empty-outcome" => "{}",
                    "conflicting-outcome" => """{"written":{"path":"record.json","id":"record","hash":"hash"},"problem":{"code":"refused","message":"refused","findings":[]}}""",
                    _ => "incomplete response",
                }),
            });
        });
        ClientUnavailableException failure = await Assert.ThrowsAsync<ClientUnavailableException>(() =>
            client.AttemptAsync(new("assignment", "contributor", "workspace", "note"), TestContext.Current.CancellationToken));
        Assert.True(failure.OutcomeUnknown);
        Assert.Equal(1, effects);
        Assert.Contains("reconcile", failure.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.UnprocessableEntity)]
    [InlineData(HttpStatusCode.Conflict)]
    public async Task AnUnreadableRejectionCannotBecomeAnUncertainWrite(HttpStatusCode status)
    {
        using var client = Client((_, _) => Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent("incomplete response") }));
        ClientRefusedException failure = await Assert.ThrowsAsync<ClientRefusedException>(() =>
            client.AttemptAsync(new("assignment", "contributor", "workspace", "note"), TestContext.Current.CancellationToken));
        Assert.False(failure.OutcomeUnknown);
        Assert.Equal(status, failure.StatusCode);
        Assert.Equal("refused", ClientFailure.FromException(failure)!.Code);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ALongOperationCanFinishBeyondTheInteractiveDeadline(bool deliverDeadlineFirst)
    {
        var clock = new ClientClock();
        var checkEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var healthEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var respond = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var http = new HttpClient(new Handler(async (request, token) =>
        {
            (request.RequestUri!.AbsolutePath == "/v1/check" ? checkEntered : healthEntered).SetResult();
            await respond.Task.WaitAsync(token);
            return Json(new FindingsResponse("clean", 0, 0, []));
        })) { BaseAddress = new Uri("http://localhost/") };
        using var client = new VericairnClient(http)
        {
            OperationClock = clock,
            OperationTimeout = operation => operation == nameof(IEstateApi.Check) ? TimeSpan.FromSeconds(5) : TimeSpan.FromMilliseconds(30),
        };
        Assert.Equal(Timeout.InfiniteTimeSpan, http.Timeout);
        Task<FindingsResponse> check = client.CheckAsync(TestContext.Current.CancellationToken);
        Task<HealthResponse> health = client.HealthAsync(TestContext.Current.CancellationToken);
        try
        {
            await Task.WhenAll(checkEntered.Task, healthEntered.Task).WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            // Both requests have started. Force either callback order at the old handler's response time.
            clock.Advance(TimeSpan.FromMilliseconds(150));
            if (deliverDeadlineFirst) clock.FireDueTimers();
            respond.SetResult();
            await Assert.ThrowsAsync<ClientTimeoutException>(() => health);
            Assert.Equal("clean", (await check).Summary);
        }
        finally { respond.TrySetResult(); }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task ACompletedOperationCannotWinAgainstItsDeadline(bool deliverDeadlineFirst, bool mayHaveEffect)
    {
        var clock = new ClientClock();
        TimeSpan timeout = TimeSpan.FromMilliseconds(30);
        int calls = 0;
        Task<int> request = ClientOperation.RunAsync("Attempt", _ =>
        {
            calls++;
            clock.Advance(timeout);
            if (deliverDeadlineFirst) clock.FireDueTimers();
            // WaitAsync accepts an already completed task even when its token is cancelled.
            return Task.FromResult(1);
        }, () => mayHaveEffect, timeout, clock, TestContext.Current.CancellationToken);

        ClientTimeoutException failure = await Assert.ThrowsAsync<ClientTimeoutException>(() => request);
        Assert.Equal(mayHaveEffect, failure.OutcomeUnknown);
        Assert.Equal("timed-out", ClientFailure.FromException(failure)?.Code);
        Assert.Equal(1, calls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AResponseBeforeItsDeadlineOrWithoutADeadlineStillSucceeds(bool infinite)
    {
        var clock = new ClientClock();
        int result = await ClientOperation.RunAsync("Health", _ =>
        {
            clock.Advance(infinite ? TimeSpan.FromDays(1) : TimeSpan.FromMilliseconds(29));
            clock.FireDueTimers();
            return Task.FromResult(1);
        }, () => false, infinite ? Timeout.InfiniteTimeSpan : TimeSpan.FromMilliseconds(30), clock, TestContext.Current.CancellationToken);
        Assert.Equal(1, result);
    }

    [Fact]
    public async Task CallerCancellationKeepsItsIdentityWhenACompletedResponseAndDeadlineAlsoArrive()
    {
        var clock = new ClientClock();
        using var stop = new CancellationTokenSource();
        Task<int> request = ClientOperation.RunAsync("Attempt", _ =>
        {
            clock.Advance(TimeSpan.FromMilliseconds(30));
            clock.FireDueTimers();
            stop.Cancel();
            return Task.FromResult(1);
        }, () => true, TimeSpan.FromMilliseconds(30), clock, stop.Token);

        ClientCancelledException failure = await Assert.ThrowsAsync<ClientCancelledException>(() => request);
        Assert.True(failure.OutcomeUnknown);
        Assert.Equal(stop.Token, failure.CancellationToken);
        Assert.Equal("cancelled", ClientFailure.FromException(failure)?.Code);
    }

    [Fact]
    public async Task ATransportCancellationWithoutACallerOrDeadlineIsUnavailable()
    {
        using var client = Client((_, _) => throw new TaskCanceledException("transport stopped independently"));
        ClientUnavailableException failure = await Assert.ThrowsAsync<ClientUnavailableException>(() => client.HealthAsync(TestContext.Current.CancellationToken));
        Assert.False(failure.OutcomeUnknown);
        Assert.Equal("unavailable", ClientFailure.FromException(failure)?.Code);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ATransportTimeoutKeepsItsCauseWhenWrappedByHttpOrCancellation(bool http)
    {
        var timeout = new TimeoutException("transport deadline elapsed");
        Exception original = http ? new HttpRequestException("request ended", timeout) : new TaskCanceledException("request ended", timeout);
        using var client = Client((_, _) => throw original);
        Exception? legacy = Record.Exception(() => client.Attempt(new("assignment", "contributor", "workspace", "note")));
        Assert.Same(original, legacy);
        ClientTimeoutException failure = await Assert.ThrowsAsync<ClientTimeoutException>(() =>
            client.AttemptAsync(new("assignment", "contributor", "workspace", "note"), TestContext.Current.CancellationToken));
        Assert.True(failure.OutcomeUnknown);
        Assert.Equal("timed-out", ClientFailure.FromException(original)!.Code);
        Assert.Equal("timed-out", ClientFailure.FromException(failure)!.Code);
        Assert.Equal("cancelled", ClientFailure.FromException(new ClientCancelledException(true, default, timeout))!.Code);
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
