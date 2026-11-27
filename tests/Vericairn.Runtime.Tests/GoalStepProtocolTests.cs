// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Net;
using System.Net.Http.Json;
using Vericairn.Protocol;
using Vericairn.Sdk;

namespace Vericairn.Runtime.Tests;

public sealed class GoalStepProtocolTests
{
    [Fact]
    public async Task APreviewUsesItsOwnRouteEvenWhenAnOlderDaemonRefusesIt()
    {
        Assert.Contains(("POST", "/v1/goals/{id}/step/check"), OpenApiDocument.Routes);
        using var handler = new OlderDaemon();
        using var http = new HttpClient(handler) { BaseAddress = new Uri("http://fixture.invalid") };
        using var client = new VericairnClient(http);
        var preview = new GoalStepRequest("principal") { Check = true };

        Assert.Contains("HTTP 404", Assert.Throws<ClientRefusedException>(() => client.GoalStep("goal", preview)).Message, StringComparison.Ordinal);
        ClientRefusedException refusal = await Assert.ThrowsAsync<ClientRefusedException>(() =>
            client.GoalStepAsync("goal", preview, TestContext.Current.CancellationToken));
        Assert.Contains("HTTP 404", refusal.Message, StringComparison.Ordinal);
        Assert.Equal(2, handler.Previews);
    }

    private sealed class OlderDaemon : HttpMessageHandler
    {
        public int Previews { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            string path = request.RequestUri!.AbsolutePath;
            if (path == "/v1/health")
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = JsonContent.Create(new HealthResponse(ApiVersion.Current, "0.9.4", "estate", ".", "sample", 3, "2026-11-26T00:00:00Z", 1), options: ProtocolJson.Options),
                };
            }

            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal("/v1/goals/goal/step/check", path);
            GoalStepRequest? body = await request.Content!.ReadFromJsonAsync<GoalStepRequest>(ProtocolJson.Options, cancellationToken);
            Assert.True(body?.Check);
            Previews++;
            return new HttpResponseMessage(HttpStatusCode.NotFound) { RequestMessage = request };
        }
    }
}
