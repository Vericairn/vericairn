// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Template;
using Microsoft.Extensions.DependencyInjection;
using Vericairn.Protocol;
using Vericairn.Records.Estate;
using Vericairn.Records.Tests;
using Vericairn.Runtime.Service;
using Vericairn.Sdk;
using static Vericairn.Records.Tests.TemplateFixture;

namespace Vericairn.Daemon.Tests;

[Collection("daemon")]
public sealed class TemplateApiTests
{
    public TemplateApiTests() => DaemonTestHome.Ensure();

    [Fact]
    public async Task RealRoutesCreateReadAndReviewOneSectionAndReturnIncompleteBriefsAndRefusals()
    {
        using var estate = new TestEstate(3);
        var definition = Install(estate, Contract(Section("answer"), Section("notes")));
        using EstateService service = EstateService.Open(estate.Root);
        using var handler = new RouteHandler(service);
        using var sdk = new VericairnClient(new HttpClient(handler) { BaseAddress = new Uri("http://fixture.invalid") });
        var template = Assert.Single((await sdk.TemplatesAsync(TestContext.Current.CancellationToken)).Templates);
        Assert.Equal(definition.Hash, template.Pin.Hash);
        var request = new DocumentNewRequest(estate.OperatorId, template.Pin, "docs/example.md", "Example");
        DocumentNewResponse created = await sdk.DocumentNewAsync(request, TestContext.Current.CancellationToken);
        Assert.True(created.Ok, created.Problem?.Message);
        string id = created.Declaration!.Id;
        Assert.All(sdk.DocumentSections("docs/example.md").Sections, s => Assert.Equal("missing", s.State));
        SectionBriefResponse full = await sdk.SectionBriefAsync(id, "answer", cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(full.Brief!.Complete, full.Problem?.Message);
        SectionBriefResponse small = sdk.SectionBrief(id, "answer", 1);
        Assert.Equal("incomplete", small.Problem!.Code); Assert.False(small.Brief!.Complete);
        Assert.Equal(JsonSerializer.Serialize(full.Brief.Sources), JsonSerializer.Serialize(small.Brief.Sources));
        int count = service.Session.Load().Records.Count;
        DocumentNewResponse repeated = sdk.DocumentNew(request);
        Assert.False(repeated.Ok); Assert.Null(repeated.Declaration); Assert.Equal(count, service.Session.Load().Records.Count);
        string text = Fill(File.ReadAllText(Path.Combine(estate.Root, "docs/example.md")), definition, "answer", "A complete answer.\n");
        Revision(estate, "docs/example.md", text);
        var review = new DocumentReviewRequest(estate.OperatorId, "current", "Read the exact answer.") { Sections = [new("answer", "current", "draft")] };
        WriteOutcome reviewed = await sdk.DocumentReviewAsync(id, review, TestContext.Current.CancellationToken);
        Assert.True(reviewed.Ok, reviewed.Problem?.Message);
        DocumentSectionsResponse sections = sdk.DocumentSections(id);
        Assert.Equal("reviewed", sections.Sections.Single(s => s.Id == "answer").State);
        Assert.Equal("missing", sections.Sections.Single(s => s.Id == "notes").State);
        Assert.Equal("not-found", sdk.DocumentSections("docs/absent.md").Problem!.Code);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task InstantiationUsesTheAuthenticatedPrincipalsExistingStanding(bool localNetwork)
    {
        using var estate = new TestEstate(3);
        var definition = Install(estate, Contract(Section("answer")));
        using EstateService service = EstateService.Open(estate.Root);
        var scope = new RequestScope(service, new LocalNetworkLedger(estate.Root));
        var context = new DefaultHttpContext(); context.Request.Method = "POST"; context.Request.Path = "/v1/documents/new";
        context.Items[RequestAuthenticator.DeviceItem] = new AuthenticatedDevice("device", estate.BuilderId, "public-key", localNetwork);
        var request = new DocumentNewRequest(estate.OperatorId, new(definition.Id, definition.Version, definition.Hash), "docs/example.md", "Example");
        bool reached = false;
        object? response = await scope.InvokeAsync(new DefaultEndpointFilterInvocationContext(context, request), _ =>
        { reached = true; return ValueTask.FromResult<object?>(null); });
        Assert.False(reached);
        Assert.Equal(StatusCodes.Status401Unauthorized, Assert.IsAssignableFrom<IStatusCodeHttpResult>(response).StatusCode);
        Assert.Empty(service.Session.Load().OfType("living-document"));
        Assert.False(File.Exists(Path.Combine(estate.Root, "docs/example.md")));
    }

    [Fact]
    public void AddedRoutesAndEveryProtocolRequestHaveAnExplicitScopeRule()
    {
        foreach ((string method, string path) in OpenApiDocument.Routes) Assert.True(RequestScope.Covers(method + " " + path), method + " " + path);
        Assert.All(typeof(Problem).Assembly.GetExportedTypes().Where(t => t.Name.EndsWith("Request", StringComparison.Ordinal)), type => Assert.True(RequestScope.Covers(type), type.Name));
    }

    private sealed class RouteHandler : HttpMessageHandler
    {
        private readonly ServiceProvider services = new ServiceCollection().AddLogging().AddRouting().BuildServiceProvider();
        private readonly RouteEndpoint[] endpoints;

        internal RouteHandler(IEstateApi api)
        {
            var routes = new RouteBuilder(services);
            ApiRoutes.Map(routes, api, () => throw new InvalidOperationException("The route fixture has no host."));
            endpoints = routes.DataSources.SelectMany(s => s.Endpoints).OfType<RouteEndpoint>().ToArray();
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var context = new DefaultHttpContext { RequestServices = services, RequestAborted = cancellationToken };
            context.Request.Method = request.Method.Method; context.Request.Path = request.RequestUri!.AbsolutePath; context.Request.QueryString = new(request.RequestUri.Query);
            byte[] bytes = request.Content is null ? [] : await request.Content.ReadAsByteArrayAsync(cancellationToken);
            context.Request.Body = new MemoryStream(bytes); context.Request.ContentLength = bytes.Length; context.Request.ContentType = request.Content?.Headers.ContentType?.ToString();
            context.Features.Set<IHttpRequestBodyDetectionFeature>(new BodyFeature(request.Content is not null));
            using var output = new MemoryStream(); context.Response.Body = output;
            var matched = endpoints.Select(e => (Endpoint: e, Values: new RouteValueDictionary())).Where(pair =>
                pair.Endpoint.Metadata.GetMetadata<HttpMethodMetadata>()!.HttpMethods.Contains(request.Method.Method, StringComparer.Ordinal)
                && new TemplateMatcher(TemplateParser.Parse(pair.Endpoint.RoutePattern.RawText!), new()).TryMatch(context.Request.Path, pair.Values));
            var route = Assert.Single(matched); context.Request.RouteValues = route.Values; context.SetEndpoint(route.Endpoint);
            await route.Endpoint.RequestDelegate!(context);
            return new((HttpStatusCode)context.Response.StatusCode) { Content = new ByteArrayContent(output.ToArray()) };
        }

        protected override void Dispose(bool disposing) { if (disposing) services.Dispose(); base.Dispose(disposing); }
        private sealed class RouteBuilder(IServiceProvider services) : IEndpointRouteBuilder
        {
            public IServiceProvider ServiceProvider => services;
            public ICollection<EndpointDataSource> DataSources { get; } = [];
            public IApplicationBuilder CreateApplicationBuilder() => new ApplicationBuilder(services);
        }
        private sealed class BodyFeature(bool canHaveBody) : IHttpRequestBodyDetectionFeature { public bool CanHaveBody => canHaveBody; }
    }
}
