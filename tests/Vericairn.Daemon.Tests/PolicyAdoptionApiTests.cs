// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Template;
using Microsoft.Extensions.DependencyInjection;
using Vericairn.Protocol;
using Vericairn.Runtime.Service;
using Vericairn.Runtime.Tests;
using Vericairn.Sdk;

namespace Vericairn.Daemon.Tests;

[Collection("daemon")]
public sealed class PolicyAdoptionApiTests
{
    [Fact]
    public async Task CompleteAdoptionTraversesTheMappedApiRoutesWithoutOpeningASocket()
    {
        DaemonTestHome.Ensure();
        // The journey deliberately uses the synchronous SDK. ASP.NET's in-memory delegates must
        // run without the test's synchronization context, just as they do on a server request thread.
        await Task.Run(() => PolicyAdoptionJourney.Run(api => new RouteSurface(new RouteHandler(api))), TestContext.Current.CancellationToken)
            .WaitAsync(TestTiming.HangTimeout, TestContext.Current.CancellationToken);
    }

    private sealed class RouteSurface : PolicyAdoptionJourney.Surface
    {
        private readonly VericairnClient client;
        internal RouteSurface(RouteHandler handler) : this(new VericairnClient(new HttpClient(handler) { BaseAddress = new Uri("http://fixture.invalid") })) { }
        private RouteSurface(VericairnClient client) : base(client) { this.client = client; }
        public override void Dispose() { client.Dispose(); base.Dispose(); }
    }

    // Exercise ApiRoutes.Map's actual binding and response delegates. No host is started and there
    // is no transport fallback: an unexpected route fails this test instead of reaching a network.
    internal sealed class RouteHandler : HttpMessageHandler
    {
        private readonly ServiceProvider services;
        private readonly RouteEndpoint[] endpoints;
        internal RouteHandler(IEstateApi api)
        {
            // Route binding needs routing and logging services, not a host, configuration files or file watchers.
            services = new ServiceCollection().AddLogging().AddRouting().BuildServiceProvider();
            var routes = new FixtureRoutes(services);
            ApiRoutes.Map(routes, api, () => throw new InvalidOperationException("The fixture cannot shut down a host."));
            endpoints = routes.DataSources.SelectMany(s => s.Endpoints).OfType<RouteEndpoint>().ToArray();
            Assert.NotEmpty(endpoints);
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Uri uri = request.RequestUri!;
            var context = new DefaultHttpContext { RequestServices = services, RequestAborted = cancellationToken };
            context.Request.Method = request.Method.Method;
            context.Request.Path = uri.AbsolutePath;
            context.Request.QueryString = new QueryString(uri.Query);
            byte[] bytes = request.Content is null ? [] : await request.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
            context.Request.Body = new MemoryStream(bytes);
            context.Request.ContentLength = bytes.Length;
            context.Request.ContentType = request.Content?.Headers.ContentType?.ToString();
            context.Features.Set<IHttpRequestBodyDetectionFeature>(new BodyFeature(request.Content is not null));
            using var output = new MemoryStream();
            context.Response.Body = output;
            var selected = endpoints.Select(e => (Endpoint: e, Values: new RouteValueDictionary()))
                .Where(pair => pair.Endpoint.Metadata.GetMetadata<HttpMethodMetadata>()!.HttpMethods.Contains(request.Method.Method, StringComparer.Ordinal) &&
                    new TemplateMatcher(TemplateParser.Parse(pair.Endpoint.RoutePattern.RawText!), new()).TryMatch(context.Request.Path, pair.Values)).ToArray();
            var route = Assert.Single(selected);
            context.Request.RouteValues = route.Values;
            context.SetEndpoint(route.Endpoint);
            await route.Endpoint.RequestDelegate!(context).ConfigureAwait(false);
            return new((System.Net.HttpStatusCode)context.Response.StatusCode) { Content = new ByteArrayContent(output.ToArray()) };
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) services.Dispose();
            base.Dispose(disposing);
        }
        private sealed class BodyFeature(bool canHaveBody) : IHttpRequestBodyDetectionFeature
        {
            public bool CanHaveBody => canHaveBody;
        }
        private sealed class FixtureRoutes(IServiceProvider services) : IEndpointRouteBuilder
        {
            public IApplicationBuilder CreateApplicationBuilder() => new ApplicationBuilder(ServiceProvider);
            public IServiceProvider ServiceProvider { get; } = services;
            public ICollection<EndpointDataSource> DataSources { get; } = new List<EndpointDataSource>();
        }
    }
}
