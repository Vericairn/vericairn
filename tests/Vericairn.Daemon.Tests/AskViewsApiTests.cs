// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Vericairn.Protocol;
using Vericairn.Runtime.Tests;
using Vericairn.Sdk;

namespace Vericairn.Daemon.Tests;

[Collection("daemon")]
public sealed class AskViewsApiTests
{
    public AskViewsApiTests() => DaemonTestHome.Ensure();

    [Fact]
    public async Task CurrentAnswersReachApiRoutesAndEverySdkReadWithoutWritingAskRevisions()
    {
        using var fixture = new AnsweredAskFixture();
        using var http = new HttpClient(new ReadRoutes(fixture.Service)) { BaseAddress = new Uri("http://fixture.invalid") };
        using var client = new VericairnClient(http);
        var bound = new CancellationBoundApi(client, TestContext.Current.CancellationToken);
        foreach (bool answered in new[] { false, true, false, true })
        {
            fixture.Decide(answered);
            Dictionary<string, string> before = fixture.Records();
            AnsweredAskFixture.AssertOpenViews(client, fixture, answered);
            AnsweredAskFixture.AssertOpenViews(bound, fixture, answered);
            foreach (IEstateApi api in new IEstateApi[] { client, bound })
            {
                AsksResponse asks = await api.AsksAsync(fixture.Estate.OperatorId, cancellationToken: TestContext.Current.CancellationToken);
                Assert.True(JsonElement.DeepEquals(fixture.Service.Asks(fixture.Estate.OperatorId).Json, asks.Json));
                StatusResponse status = await api.StatusAsync(cancellationToken: TestContext.Current.CancellationToken);
                Assert.True(JsonElement.DeepEquals(fixture.Service.Status().Json, status.Json));
                ProgressResponse progress = await api.ProgressAsync(cancellationToken: TestContext.Current.CancellationToken);
                Assert.True(JsonElement.DeepEquals(fixture.Service.Progress().Json, progress.Json));
                OperatorActionsResponse actions = await api.OperatorActionsAsync(cancellationToken: TestContext.Current.CancellationToken);
                Assert.True(JsonElement.DeepEquals(fixture.Service.OperatorActions().Json, actions.Json));
            }
            Assert.Equal(before.OrderBy(p => p.Key), fixture.Records().OrderBy(p => p.Key));
            fixture.AssertAskUnchanged(answered);
        }
    }

    // Dispatch the real HTTP route delegates in memory, without opening a network or local socket.
    private sealed class ReadRoutes : HttpMessageHandler
    {
        private readonly ServiceProvider services = new ServiceCollection().AddLogging().AddRouting().BuildServiceProvider();
        private readonly RouteEndpoint[] endpoints;

        internal ReadRoutes(IEstateApi api)
        {
            var routes = new RouteBuilder(services);
            ApiRoutes.Map(routes, api, () => throw new InvalidOperationException("Reading asks cannot shut down a runtime."));
            endpoints = routes.DataSources.SelectMany(s => s.Endpoints).OfType<RouteEndpoint>().ToArray();
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            var context = new DefaultHttpContext { RequestServices = services, RequestAborted = cancellationToken };
            context.Request.Method = "GET";
            context.Request.Path = request.RequestUri!.AbsolutePath;
            context.Request.QueryString = new QueryString(request.RequestUri.Query);
            RouteEndpoint endpoint = Assert.Single(endpoints, e => e.RoutePattern.RawText == context.Request.Path
                && e.Metadata.GetMetadata<HttpMethodMetadata>()!.HttpMethods.Contains("GET", StringComparer.Ordinal));
            using var output = new MemoryStream();
            context.Response.Body = output;
            await endpoint.RequestDelegate!(context).ConfigureAwait(false);
            return new((HttpStatusCode)context.Response.StatusCode) { Content = new ByteArrayContent(output.ToArray()) };
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) services.Dispose();
            base.Dispose(disposing);
        }

        private sealed class RouteBuilder(IServiceProvider services) : IEndpointRouteBuilder
        {
            public IServiceProvider ServiceProvider => services;
            public ICollection<EndpointDataSource> DataSources { get; } = [];
            public IApplicationBuilder CreateApplicationBuilder() => new ApplicationBuilder(services);
        }
    }
}
