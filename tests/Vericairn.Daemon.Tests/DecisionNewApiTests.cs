// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Template;
using Microsoft.Extensions.DependencyInjection;
using Vericairn.Protocol;
using Vericairn.Records.Hashing;
using Vericairn.Records.Model;
using Vericairn.Records.Tests;
using Vericairn.Runtime.Service;
using Vericairn.Runtime.Tests;
using Vericairn.Sdk;

namespace Vericairn.Daemon.Tests;

[Collection("daemon")]
public sealed class DecisionNewApiTests
{
    public DecisionNewApiTests() => DaemonTestHome.Ensure();

    [Fact]
    public void ContractAndDefaultInterfacesDescribeTheGuidedWrite()
    {
        using JsonDocument document = JsonDocument.Parse(OpenApiDocument.Json);
        JsonElement operation = document.RootElement.GetProperty("paths").GetProperty("/v1/decisions/new").GetProperty("post");
        foreach (string code in new[] { "200", "422" })
            Assert.Contains("DecisionNewResponse", operation.GetProperty("responses").GetProperty(code).GetRawText(), StringComparison.Ordinal);
        JsonElement schema = document.RootElement.GetProperty("components").GetProperty("schemas").GetProperty("DecisionNewRequest");
        Assert.Equal(typeof(DecisionNewRequest).GetProperties().Select(p => JsonNamingPolicy.CamelCase.ConvertName(p.Name)).Order(),
            schema.GetProperty("properties").EnumerateObject().Select(p => p.Name).Order());
        Assert.False(typeof(IEstateApi).GetMethod("DecisionFromAsk")!.IsAbstract);
        Assert.False(typeof(IEstateApi).GetMethod("DecisionFromAskAsync")!.IsAbstract);
        Assert.True(RequestScope.Covers("POST /v1/decisions/new"));
        Assert.True(RequestScope.Covers(typeof(DecisionNewRequest)));
    }

    [Theory]
    [InlineData("http")]
    [InlineData("sync")]
    [InlineData("async")]
    [InlineData("bound")]
    [InlineData("bound-async")]
    public async Task ApiAndEverySdkSurfacePreserveAnswerPinsAndRefusals(string surface)
    {
        using var fixture = new DecisionFixture();
        using var http = new HttpClient(new PinRouteHandler(fixture.Service)) { BaseAddress = new Uri("http://fixture.invalid") };
        using var client = new VericairnClient(http);
        var bound = new CancellationBoundApi(client, TestContext.Current.CancellationToken);
        DecisionNewRequest request = fixture.Request;
        var before = fixture.Records();
        DecisionNewResponse refused = await Submit(request with { Documents = [] });
        Assert.False(refused.Ok);
        Assert.Empty(refused.Pins);
        Assert.Null(refused.Written);
        Assert.Contains("equal exactly", refused.Problem!.Message, StringComparison.Ordinal);
        Assert.Equal(before, fixture.Records());
        DecisionNewResponse result = await Submit(request);
        Assert.True(result.Ok, JsonSerializer.Serialize(result.Problem));
        Assert.Single(result.Pins);
        JsonElement decision = fixture.Service.Record(result.Written!.Id)!.Envelope.GetProperty("body");
        Assert.Equal(request.Statement, decision.GetProperty("statement").GetString());
        Assert.Equal(request.AnsweredAt, decision.GetProperty("decidedAt").GetString());
        Assert.Equal(result.Pins[0].Hash, decision.GetProperty("changeSet").GetProperty("pins")[0].GetProperty("hash").GetString());
        Assert.Equal(0, fixture.Service.Check().Errors);

        async Task<DecisionNewResponse> Submit(DecisionNewRequest input)
        {
            if (surface == "sync") return client.DecisionFromAsk(input);
            if (surface == "async") return await client.DecisionFromAskAsync(input, TestContext.Current.CancellationToken);
            if (surface == "bound") return bound.DecisionFromAsk(input);
            if (surface == "bound-async") return await bound.DecisionFromAskAsync(input, TestContext.Current.CancellationToken);
            using HttpResponseMessage response = await http.PostAsJsonAsync(new Uri("/v1/decisions/new", UriKind.Relative), input,
                ProtocolJson.Options, TestContext.Current.CancellationToken);
            Assert.Equal(input.Documents.Count == 0 ? HttpStatusCode.UnprocessableEntity : HttpStatusCode.OK, response.StatusCode);
            return (await response.Content.ReadFromJsonAsync<DecisionNewResponse>(ProtocolJson.Options, TestContext.Current.CancellationToken))!;
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AuthenticatedCallerCannotRecordAsAnotherPrincipal(bool localNetwork)
    {
        using var fixture = new DecisionFixture();
        var scope = new RequestScope(fixture.Service, new LocalNetworkLedger(fixture.Estate.Root));
        var context = new DefaultHttpContext();
        context.Request.Method = "POST";
        context.Request.Path = "/v1/decisions/new";
        context.Items[RequestAuthenticator.DeviceItem] = new AuthenticatedDevice("device", fixture.Estate.BuilderId, "public-key", localNetwork);
        var before = fixture.Records();
        bool reached = false;
        object? result = await scope.InvokeAsync(new DefaultEndpointFilterInvocationContext(context, fixture.Request), _ =>
        {
            reached = true;
            return ValueTask.FromResult<object?>(null);
        });
        Assert.False(reached);
        Assert.Equal(StatusCodes.Status401Unauthorized, Assert.IsAssignableFrom<IStatusCodeHttpResult>(result).StatusCode);
        Assert.Equal(before, fixture.Records());
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"written\":null,\"pins\":[]}")]
    public async Task AnIncompleteSuccessReplyKeepsTheWriteOutcomeUnknown(string body)
    {
        using var fixture = new DecisionFixture();
        using var client = new VericairnClient(new HttpClient(new ReplyHandler(body)) { BaseAddress = new Uri("http://fixture.invalid") });
        ClientUnavailableException sync = Assert.Throws<ClientUnavailableException>(() => client.DecisionFromAsk(fixture.Request));
        Assert.True(sync.OutcomeUnknown);
        ClientUnavailableException asynchronous = await Assert.ThrowsAsync<ClientUnavailableException>(() => client.DecisionFromAskAsync(fixture.Request, TestContext.Current.CancellationToken));
        Assert.True(asynchronous.OutcomeUnknown);
    }

    [Fact]
    public async Task ApiAndSdkRetainPinsWhenStorageStopsBeforeTheDecision()
    {
        using var fixture = new DecisionFixture();
        fixture.Service.BeforeStoringTogether = record =>
        {
            if (record.Envelope.Type == "decision") throw new IOException("Simulated interruption.");
        };
        using var client = new VericairnClient(new HttpClient(new PinRouteHandler(fixture.Service)) { BaseAddress = new Uri("http://fixture.invalid") });
        DecisionNewResponse result = await client.DecisionFromAskAsync(fixture.Request, TestContext.Current.CancellationToken);
        Assert.False(result.Ok);
        Assert.Null(result.Written);
        RecordWritten pin = Assert.Single(result.Pins);
        Assert.NotNull(fixture.Service.Record(pin.Id));
        Assert.Equal("reconcile-required", result.Problem!.Code);
    }

    private sealed class ReplyHandler(string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") });
    }

    // Exercise the real API binding and response delegates without a host, file watchers or sockets.
    private sealed class PinRouteHandler : HttpMessageHandler
    {
        private readonly ServiceProvider services = new ServiceCollection().AddLogging().AddRouting().BuildServiceProvider();
        private readonly RouteEndpoint[] endpoints;

        public PinRouteHandler(IEstateApi api)
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
            byte[] bytes = request.Content is null ? [] : await request.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
            context.Request.Body = new MemoryStream(bytes);
            context.Request.ContentLength = bytes.Length;
            context.Request.ContentType = request.Content?.Headers.ContentType?.ToString();
            context.Features.Set<IHttpRequestBodyDetectionFeature>(new BodyFeature(request.Content is not null));
            using var output = new MemoryStream();
            context.Response.Body = output;
            var matched = endpoints.Select(e => (Endpoint: e, Values: new RouteValueDictionary()))
                .Where(pair => pair.Endpoint.Metadata.GetMetadata<HttpMethodMetadata>()!.HttpMethods.Contains(request.Method.Method, StringComparer.Ordinal)
                    && new TemplateMatcher(TemplateParser.Parse(pair.Endpoint.RoutePattern.RawText!), new()).TryMatch(context.Request.Path, pair.Values));
            var route = Assert.Single(matched);
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
