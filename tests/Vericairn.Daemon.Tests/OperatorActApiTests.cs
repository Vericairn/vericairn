// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Vericairn.Protocol;
using Vericairn.Runtime.Tests;
using Vericairn.Sdk;

namespace Vericairn.Daemon.Tests;

/// <summary>OPS-24 (0.9.4): the reviewed act over the API and the SDK, bound to the authenticated device.</summary>
[Collection("daemon")]
public sealed class OperatorActApiTests
{
    public OperatorActApiTests() => DaemonTestHome.Ensure();

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SdkAndApiCarryTheReviewedListAndTheActWithoutASocket(bool asynchronous)
    {
        using var f = new OperatorCloseFixture();
        f.Add("STEP-1");
        OperatorAnswerTests.Ask(f, "Ship it?", "Ship it now.");
        await using ServiceProvider services = new ServiceCollection().AddLogging().AddRouting().BuildServiceProvider();
        var routes = new MemoryRoutes(services);
        ApiRoutes.Map(routes, f.Api, () => Assert.Fail("An act cannot shut down the runtime."));
        using var client = new VericairnClient(new HttpClient(new RouteTransport(routes, services)) { BaseAddress = new Uri("http://localhost/") });
        OperatorActListResponse listed = asynchronous
            ? await client.OperatorActItemsAsync(f.Estate.OperatorId, TestContext.Current.CancellationToken) : client.OperatorActItems(f.Estate.OperatorId);
        Assert.True(listed.Ok, listed.Problem?.Message);
        Assert.Equal(JsonSerializer.Serialize(f.Api.OperatorActItems(f.Estate.OperatorId), ProtocolJson.Options), JsonSerializer.Serialize(listed, ProtocolJson.Options));
        Assert.Equal("denied", client.OperatorActItems(f.Estate.BuilderId).Problem?.Code);
        var request = new OperatorActRequest(f.Estate.OperatorId, [listed.Items[0].Choose(), listed.Items[1].Choose("wait")], All: true);
        OperatorActResponse acted = asynchronous ? await client.OperatorActAsync(request, TestContext.Current.CancellationToken) : client.OperatorAct(request);
        Assert.True(acted.Ok, acted.Problem?.Message);
        Assert.Equal("close:STEP-1", Assert.Single(acted.Written).Item);
        OperatorActListResponse remaining = await new CancellationBoundApi(client, TestContext.Current.CancellationToken).OperatorActItemsAsync(f.Estate.OperatorId, TestContext.Current.CancellationToken);
        Assert.Equal("answer", Assert.Single(remaining.Items).Kind);
        Assert.Equal(0, f.Api.Validate().Errors);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TheRouteBindsTheActingOperatorToTheAuthenticatedDevice(bool localNetwork)
    {
        using var f = new OperatorCloseFixture();
        f.Add("STEP-1");
        var scope = new RequestScope(f.Api, new LocalNetworkLedger(f.Estate.Root));
        var context = new DefaultHttpContext();
        context.Request.Method = "POST";
        context.Request.Path = "/v1/operator/act";
        context.Items[RequestAuthenticator.DeviceItem] = new AuthenticatedDevice("device", f.Estate.BuilderId, "public-key", localNetwork);
        bool reached = false;
        var request = new OperatorActRequest(f.Estate.OperatorId, [.. f.Api.OperatorActItems(f.Estate.OperatorId).Items.Select(i => i.Choose())]);
        object? response = await scope.InvokeAsync(new DefaultEndpointFilterInvocationContext(context, request), _ =>
        { reached = true; return ValueTask.FromResult<object?>(null); });
        Assert.False(reached);
        Assert.Equal(StatusCodes.Status401Unauthorized, Assert.IsAssignableFrom<IStatusCodeHttpResult>(response).StatusCode);
        Assert.Empty(f.Estate.Load().OfType("acceptance"));
    }

    [Fact]
    public void TheContractDescribesBothRoutesAndEveryChoiceField()
    {
        Assert.True(RequestScope.Covers("GET /v1/operator/act"));
        Assert.True(RequestScope.Covers("POST /v1/operator/act"));
        Assert.True(RequestScope.Covers(typeof(OperatorActRequest)));
        using JsonDocument contract = JsonDocument.Parse(OpenApiDocument.Json);
        JsonElement path = contract.RootElement.GetProperty("paths").GetProperty("/v1/operator/act");
        Assert.Equal("by", path.GetProperty("get").GetProperty("parameters")[0].GetProperty("name").GetString());
        Assert.Equal("#/components/schemas/OperatorActRequest", path.GetProperty("post").GetProperty("requestBody").GetProperty("content").GetProperty("application/json").GetProperty("schema").GetProperty("$ref").GetString());
        JsonElement schemas = contract.RootElement.GetProperty("components").GetProperty("schemas");
        Assert.Equal(["by", "choices"], schemas.GetProperty("OperatorActRequest").GetProperty("required").EnumerateArray().Select(v => v.GetString()));
        Assert.Equal(["sign", "wait", "dismiss"], schemas.GetProperty("OperatorActChoice").GetProperty("properties").GetProperty("choice").GetProperty("enum").EnumerateArray().Select(v => v.GetString()));
    }

    private sealed class MemoryRoutes(IServiceProvider services) : IEndpointRouteBuilder
    {
        public IServiceProvider ServiceProvider => services;
        public ICollection<EndpointDataSource> DataSources { get; } = [];
        public IApplicationBuilder CreateApplicationBuilder() => new ApplicationBuilder(services);
    }

    // Exercise the actual route delegates and both SDK transports without opening a local socket, the query string included.
    private sealed class RouteTransport(MemoryRoutes routes, IServiceProvider services) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RouteEndpoint endpoint = Assert.Single(routes.DataSources.SelectMany(s => s.Endpoints).OfType<RouteEndpoint>(),
                e => e.RoutePattern.RawText == request.RequestUri!.AbsolutePath && e.Metadata.GetMetadata<HttpMethodMetadata>()!.HttpMethods.Contains(request.Method.Method));
            var context = new DefaultHttpContext { RequestServices = services, RequestAborted = cancellationToken };
            context.Request.Method = request.Method.Method;
            context.Request.Path = request.RequestUri!.AbsolutePath;
            context.Request.QueryString = new QueryString(request.RequestUri.Query);
            byte[] input = request.Content is null ? [] : await request.Content.ReadAsByteArrayAsync(cancellationToken);
            using var body = new MemoryStream(input);
            context.Request.Body = body;
            context.Request.ContentType = "application/json";
            context.Request.ContentLength = input.Length;
            context.Features.Set<IHttpRequestBodyDetectionFeature>(new BodyDetection(input.Length > 0));
            using var output = new MemoryStream();
            context.Response.Body = output;
            await endpoint.RequestDelegate!(context);
            return new HttpResponseMessage((HttpStatusCode)context.Response.StatusCode) { Content = new ByteArrayContent(output.ToArray()) };
        }
    }

    private sealed record BodyDetection(bool CanHaveBody) : IHttpRequestBodyDetectionFeature;
}
