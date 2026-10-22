// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Reflection;
using Vericairn.Protocol;
using Vericairn.Sdk;

namespace Vericairn.Workbench.Tests;

public sealed class DaemonVersionConnectionTests
{
    [Fact]
    public async Task ConnectionShowsTheServingReleaseAndRestartThenClearsTheWarningAfterRestart()
    {
        string current = typeof(VericairnClient).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion.Split('+')[0];
        string release = "0.8.0";
        var endpoint = new DaemonEndpoint("pipe", "fixture", 1, "now", "estate", ApiVersion.Current);
        using var connection = new DaemonConnection("estate", _ => endpoint,
            _ => new VericairnClient(new HttpClient(new Handler(() => release)) { BaseAddress = new("http://fixture.invalid") }));
        await connection.RefreshAsync(TestContext.Current.CancellationToken);
        Assert.Equal("daemon", connection.Mode);
        Assert.Contains("Vericairn 0.8.0", connection.Where, StringComparison.Ordinal);
        Assert.Contains("Vericairn " + current, connection.Where, StringComparison.Ordinal);
        Assert.Contains("vericairn runtime stop", connection.Where, StringComparison.Ordinal);
        Assert.Contains("vericairn runtime start", connection.Where, StringComparison.Ordinal);
        Assert.NotNull(Assert.IsAssignableFrom<IDaemonVersion>(connection.Api).DaemonVersionWarning);
        release = current;
        await connection.RefreshAsync(TestContext.Current.CancellationToken);
        Assert.Equal("daemon", connection.Mode);
        Assert.DoesNotContain("vericairn runtime stop", connection.Where, StringComparison.Ordinal);
        Assert.Null(Assert.IsAssignableFrom<IDaemonVersion>(connection.Api).DaemonVersionWarning);
    }

    [Fact]
    public async Task AnUnsupportedApiStillShowsTheReleaseAndRestartInstructions()
    {
        var endpoint = new DaemonEndpoint("pipe", "fixture", 1, "now", "estate", "0");
        using var connection = new DaemonConnection("estate", _ => endpoint,
            _ => new VericairnClient(new HttpClient(new Handler(() => "0.8.0", "0")) { BaseAddress = new("http://fixture.invalid") }));
        await connection.RefreshAsync(TestContext.Current.CancellationToken);
        Assert.Equal("unavailable", connection.Mode);
        Assert.Null(connection.Api);
        Assert.Contains("Vericairn 0.8.0", connection.Where, StringComparison.Ordinal);
        Assert.Contains("vericairn runtime stop", connection.Where, StringComparison.Ordinal);
        Assert.Contains("vericairn runtime start", connection.Where, StringComparison.Ordinal);
        Assert.Contains("serves API 0", connection.Where, StringComparison.Ordinal);
    }

    private sealed class Handler(Func<string> release, string api = ApiVersion.Current) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Assert.Equal("/v1/health", request.RequestUri!.AbsolutePath);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new HealthResponse(api, release(), "estate", "root", "repository-synced", 3, "now", 1), options: ProtocolJson.Options),
            });
        }
    }
}
