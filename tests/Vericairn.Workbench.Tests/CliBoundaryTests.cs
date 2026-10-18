// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Net;
using System.Net.Http;
using Vericairn.Cli;
using Vericairn.Protocol;
using Vericairn.Sdk;

namespace Vericairn.Workbench.Tests;

public sealed class CliBoundaryTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void CliReportsTheSharedCauseAndUncertaintyWithoutWritingAJsonResult(bool cancel, bool effect)
    {
        using var stop = new CancellationTokenSource();
        int calls = 0;
        using var http = new HttpClient(new Handler(async (_, token) =>
        {
            calls++;
            if (cancel) stop.Cancel();
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return new HttpResponseMessage(HttpStatusCode.OK);
        })) { BaseAddress = new Uri("http://localhost") };
        using var client = new VericairnClient(http) { OperationTimeout = _ => TimeSpan.FromMilliseconds(30) };
        var output = new StringWriter();
        var error = new StringWriter();
        var context = new CommandContext(output, error, stop.Token);
        int code = context.Invoke(() =>
        {
            var api = new CancellationBoundApi(client, context.CancellationToken);
            if (effect) api.Attempt(new("assignment", "contributor", "workspace", "note"));
            else api.Health();
            output.WriteLine("unexpected response");
            return 0;
        });
        Exception expected = cancel ? new ClientCancelledException(effect, stop.Token) : new ClientTimeoutException(effect);
        ClientFailure failure = ClientFailure.FromException(expected)!;
        Assert.Equal(cancel ? 130 : 124, code);
        Assert.Equal(failure.ExitCode, code);
        Assert.Equal("error: " + failure.Message + error.NewLine, error.ToString());
        Assert.Empty(output.ToString());
        Assert.Equal(1, calls);
    }

    [Fact]
    public void PreCancelledCommandDoesNotDispatchAndDoesNotClaimAnUncertainEffect()
    {
        using var stop = new CancellationTokenSource();
        stop.Cancel();
        var error = new StringWriter();
        var context = new CommandContext(error: error, cancellationToken: stop.Token);
        bool dispatched = false;
        int code = context.Invoke(() => { dispatched = true; return 0; });
        Assert.Equal(130, code);
        Assert.False(dispatched);
        Assert.Equal("error: The request was cancelled." + error.NewLine, error.ToString());
    }

    [Fact]
    public void LostWriteResponseAtTheCliKeepsItsUncertaintyAndExistingErrorExitCode()
    {
        using var http = new HttpClient(new Handler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("truncated response"),
        }))) { BaseAddress = new Uri("http://localhost") };
        using var client = new VericairnClient(http);
        var error = new StringWriter();
        var context = new CommandContext(error: error, cancellationToken: TestContext.Current.CancellationToken);
        int code = context.Invoke(() =>
        {
            new CancellationBoundApi(client, context.CancellationToken).Attempt(new("assignment", "contributor", "workspace", "note"));
            return 0;
        });
        Assert.Equal(2, code);
        Assert.Contains("The outcome is unknown; reconcile the original operation before retrying.", error.ToString(), StringComparison.Ordinal);
    }

    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => send(request, cancellationToken);
    }
}
