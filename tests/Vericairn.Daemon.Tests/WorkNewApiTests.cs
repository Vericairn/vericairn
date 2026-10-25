// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Template;
using Microsoft.Extensions.DependencyInjection;
using Vericairn.Protocol;
using Vericairn.Records.Canonical;
using Vericairn.Records.Tests;
using Vericairn.Runtime.Service;
using Vericairn.Runtime.Tests;
using Vericairn.Sdk;

namespace Vericairn.Daemon.Tests;

[Collection("daemon")]
public sealed class WorkNewApiTests
{
    public WorkNewApiTests() => DaemonTestHome.Ensure();

    [Fact]
    public void ContractDeclaresTheFieldsReceiptAndBothInterfaceMembers()
    {
        Assert.Contains(("POST", "/v1/work/new"), OpenApiDocument.Routes);
        using JsonDocument document = JsonDocument.Parse(OpenApiDocument.Json);
        JsonElement operation = document.RootElement.GetProperty("paths").GetProperty("/v1/work/new").GetProperty("post");
        Assert.Equal("#/components/schemas/WorkNewRequest", operation.GetProperty("requestBody").GetProperty("content").GetProperty("application/json").GetProperty("schema").GetProperty("$ref").GetString());
        Assert.Equal("#/components/schemas/WriteOutcome", operation.GetProperty("responses").GetProperty("200").GetProperty("content").GetProperty("application/json").GetProperty("schema").GetProperty("$ref").GetString());
        JsonElement schema = document.RootElement.GetProperty("components").GetProperty("schemas").GetProperty("WorkNewRequest");
        Assert.Equal(typeof(WorkNewRequest).GetProperties().Select(p => JsonNamingPolicy.CamelCase.ConvertName(p.Name)).Order(),
            schema.GetProperty("properties").EnumerateObject().Select(p => p.Name).Order());
        Assert.Equal("ordinary", schema.GetProperty("properties").GetProperty("kind").GetProperty("default").GetString());
        foreach (string field in new[] { "scope", "deliverables", "after", "inputs" })
            Assert.Equal("string", schema.GetProperty("properties").GetProperty(field).GetProperty("items").GetProperty("type").GetString());
        foreach (Type surface in new[] { typeof(IEstateApi), typeof(VericairnClient), typeof(CancellationBoundApi) })
        {
            Assert.Equal(typeof(WriteOutcome), surface.GetMethod("WorkNew", [typeof(WorkNewRequest)])?.ReturnType);
            Assert.Equal(typeof(Task<WriteOutcome>), surface.GetMethod("WorkNewAsync", [typeof(WorkNewRequest), typeof(CancellationToken)])?.ReturnType);
        }
        Assert.True(RequestScope.Covers("POST /v1/work/new"));
        Assert.True(RequestScope.Covers(typeof(WorkNewRequest)));
    }

    [Fact]
    public async Task ApiAndSynchronousAsyncAndBoundSdkCreateTheSameSignedWorkOrders()
    {
        using var estate = new TestEstate(3);
        using EstateService service = EstateService.Open(estate.Root);
        string device = WorkNewCases.AdmitBuilder(estate, service);
        using var http = new HttpClient(new WorkRouteHandler(service)) { BaseAddress = new Uri("http://fixture.invalid") };
        using var sdk = new VericairnClient(http);
        WorkNewRequest request = WorkNewCases.Valid(estate.BuilderId);
        using HttpResponseMessage response = await http.PostAsJsonAsync(new Uri("/v1/work/new", UriKind.Relative), request, ProtocolJson.Options, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        RecordWritten first = WorkNewCases.Written((await response.Content.ReadFromJsonAsync<WriteOutcome>(ProtocolJson.Options, TestContext.Current.CancellationToken))!);
        Assert.Equal(CanonicalJson.SerializePretty(WorkNewCases.Body()), CanonicalJson.SerializePretty(service.Record(first.Id)!.Envelope.GetProperty("body")));
        RecordWritten second = WorkNewCases.Written(sdk.WorkNew(request with { Key = "W-2", Inputs = ["work-order:" + first.Id], After = [first.Id] }));
        RecordWritten third = WorkNewCases.Written(await sdk.WorkNewAsync(request with { Key = "W-3", Kind = "milestone" }, TestContext.Current.CancellationToken));
        var bound = new CancellationBoundApi(sdk, TestContext.Current.CancellationToken);
        RecordWritten fourth = WorkNewCases.Written(bound.WorkNew(request with { Key = "W-4" }));
        RecordWritten fifth = WorkNewCases.Written(await bound.WorkNewAsync(request with { Key = "W-5" }, TestContext.Current.CancellationToken));
        foreach (RecordWritten receipt in new[] { first, second, third, fourth, fifth })
        {
            JsonElement envelope = service.Record(receipt.Id)!.Envelope;
            Assert.Equal(estate.BuilderId, envelope.GetProperty("by").GetString());
            Assert.Equal(receipt.Hash, envelope.GetProperty("hash").GetString());
            Assert.Equal(device, Assert.Single(envelope.GetProperty("signatures").EnumerateArray()).GetProperty("device").GetString());
        }
        JsonElement body = service.Record(second.Id)!.Envelope.GetProperty("body");
        Assert.Equal(first.Id, Assert.Single(body.GetProperty("after").EnumerateArray()).GetString());
        Assert.Equal(first.Id, Assert.Single(body.GetProperty("inputs").EnumerateArray()).GetProperty("id").GetString());
        Assert.Equal("milestone", service.Record(third.Id)!.Envelope.GetProperty("body").GetProperty("kind").GetString());
        Assert.Equal(0, service.Validate().Errors);
    }

    [Theory]
    [MemberData(nameof(WorkNewCases.Refusals), MemberType = typeof(WorkNewCases))]
    public async Task ApiAndSdkRefusalsMatchTheGenericRecordRoute(string fault, string finding)
    {
        using var estate = new TestEstate(3);
        using EstateService service = EstateService.Open(estate.Root);
        using var http = new HttpClient(new WorkRouteHandler(service)) { BaseAddress = new Uri("http://fixture.invalid") };
        using var sdk = new VericairnClient(http);
        (WorkNewRequest request, JsonElement body) = WorkNewCases.Invalid(fault, estate.OperatorId);
        int before = service.Session.Load().Records.Count;
        WriteOutcome generic = sdk.NewRecord(new NewRecordRequest("work-order", request.By, body));
        using HttpResponseMessage response = await http.PostAsJsonAsync(new Uri("/v1/work/new", UriKind.Relative), request, ProtocolJson.Options, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        WorkNewCases.SameRefusal(generic, (await response.Content.ReadFromJsonAsync<WriteOutcome>(ProtocolJson.Options, TestContext.Current.CancellationToken))!);
        WorkNewCases.SameRefusal(generic, sdk.WorkNew(request));
        WorkNewCases.SameRefusal(generic, await sdk.WorkNewAsync(request, TestContext.Current.CancellationToken));
        var bound = new CancellationBoundApi(sdk, TestContext.Current.CancellationToken);
        WorkNewCases.SameRefusal(generic, bound.WorkNew(request));
        Assert.Contains(generic.Problem!.Findings, f => f.Code == finding);
        Assert.Equal(before, service.Session.Load().Records.Count);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ADeviceCannotCreateWorkAsAnotherPrincipal(bool localNetwork)
    {
        using var estate = new TestEstate(3);
        using EstateService service = EstateService.Open(estate.Root);
        string scratch = Path.Combine(Path.GetTempPath(), "vericairn-work-new-scope-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(scratch);
        try
        {
            var scope = new RequestScope(service, new LocalNetworkLedger(scratch));
            var context = new DefaultHttpContext();
            context.Request.Method = "POST";
            context.Request.Path = "/v1/work/new";
            context.Items[RequestAuthenticator.DeviceItem] = new AuthenticatedDevice("device", estate.BuilderId, "public-key", localNetwork);
            bool reached = false;
            object? response = await scope.InvokeAsync(new DefaultEndpointFilterInvocationContext(context, WorkNewCases.Valid(estate.OperatorId)), _ =>
            { reached = true; return ValueTask.FromResult<object?>(null); });
            Assert.False(reached);
            Assert.Equal(StatusCodes.Status401Unauthorized, Assert.IsAssignableFrom<IStatusCodeHttpResult>(response).StatusCode);
            Assert.Empty(service.Session.Load().OfType("work-order"));
        }
        finally { Directory.Delete(scratch, recursive: true); }
    }

    // The real route delegates, binding and responses, with no host or sockets.
    internal sealed class WorkRouteHandler : HttpMessageHandler
    {
        private readonly ServiceProvider services = new ServiceCollection().AddLogging().AddRouting().BuildServiceProvider();
        private readonly RouteEndpoint[] endpoints;

        public WorkRouteHandler(IEstateApi api)
        {
            var routes = new RouteBuilder(services);
            ApiRoutes.Map(routes, api, () => throw new InvalidOperationException("This fixture has no host to shut down."));
            endpoints = routes.DataSources.SelectMany(s => s.Endpoints).OfType<RouteEndpoint>().ToArray();
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var context = new DefaultHttpContext { RequestServices = services, RequestAborted = cancellationToken };
            context.Request.Method = request.Method.Method;
            context.Request.Path = request.RequestUri!.AbsolutePath;
            context.Request.QueryString = new QueryString(request.RequestUri.Query);
            byte[] bytes = request.Content is null ? [] : await request.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
            context.Request.Body = new MemoryStream(bytes);
            context.Request.ContentLength = bytes.Length;
            context.Request.ContentType = request.Content?.Headers.ContentType?.ToString();
            context.Features.Set<IHttpRequestBodyDetectionFeature>(new BodyFeature(request.Content is not null));
            using var output = new MemoryStream();
            context.Response.Body = output;
            var matches = endpoints.Select(e => (Endpoint: e, Values: new RouteValueDictionary()))
                .Where(pair => pair.Endpoint.Metadata.GetMetadata<HttpMethodMetadata>()!.HttpMethods.Contains(request.Method.Method, StringComparer.Ordinal)
                    && new TemplateMatcher(TemplateParser.Parse(pair.Endpoint.RoutePattern.RawText!), new()).TryMatch(context.Request.Path, pair.Values));
            var route = Assert.Single(matches);
            context.Request.RouteValues = route.Values;
            context.SetEndpoint(route.Endpoint);
            await route.Endpoint.RequestDelegate!(context).ConfigureAwait(false);
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

        private sealed class BodyFeature(bool canHaveBody) : IHttpRequestBodyDetectionFeature
        {
            public bool CanHaveBody => canHaveBody;
        }
    }
}
