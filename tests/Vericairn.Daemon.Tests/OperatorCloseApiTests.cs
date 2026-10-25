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

[Collection("daemon")]
public sealed class OperatorCloseApiTests
{
    public OperatorCloseApiTests() => DaemonTestHome.Ensure();

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SdkAndApiCarryTheExactListingPartialSelectionAndAllWithoutASocket(bool asynchronous)
    {
        using var f = new OperatorCloseFixture();
        f.Add("STEP-1");
        f.Add("STEP-2");
        await using ServiceProvider services = new ServiceCollection().AddLogging().AddRouting().BuildServiceProvider();
        var routes = new MemoryRoutes(services);
        ApiRoutes.Map(routes, f.Api, () => Assert.Fail("Closing work cannot shut down the runtime."));
        using var client = new VericairnClient(new HttpClient(new RouteTransport(routes, services)) { BaseAddress = new Uri("http://localhost/") });
        OperatorCloseCandidatesResponse listed = asynchronous ? await client.OperatorCloseCandidatesAsync(TestContext.Current.CancellationToken) : client.OperatorCloseCandidates();
        Assert.True(listed.Ok);
        Assert.Equal(JsonSerializer.Serialize(f.Api.OperatorCloseCandidates(), ProtocolJson.Options), JsonSerializer.Serialize(listed, ProtocolJson.Options));
        var request = new OperatorCloseRequest(f.Estate.OperatorId, [listed.Items[1].ToSelection()]);
        OperatorCloseResponse partial = asynchronous ? await client.OperatorCloseAsync(request, TestContext.Current.CancellationToken) : client.OperatorClose(request);
        Assert.True(partial.Ok, partial.Problem?.Message);
        Assert.Single(partial.Written);
        OperatorCloseCandidatesResponse remaining = new CancellationBoundApi(client, TestContext.Current.CancellationToken).OperatorCloseCandidates();
        Assert.Equal("STEP-1", Assert.Single(remaining.Items).Key);
        OperatorCloseResponse all = await new CancellationBoundApi(client, TestContext.Current.CancellationToken).OperatorCloseAsync(
            new(f.Estate.OperatorId, [.. remaining.Items.Select(i => i.ToSelection())], All: true), TestContext.Current.CancellationToken);
        Assert.True(all.Ok, all.Problem?.Message);
        Assert.Single(all.Written);
        Assert.Empty(client.OperatorCloseCandidates().Items);
        Assert.Equal(2, f.Estate.Load().OfType("acceptance").Count());
        Assert.Equal(0, f.Api.Validate().Errors);
    }

    [Fact]
    public async Task AStaleSdkRequestReturnsAConflictWithNoReceiptsOrAcceptances()
    {
        using var f = new OperatorCloseFixture();
        f.Add("STEP-1");
        var second = f.Add("STEP-2");
        await using ServiceProvider services = new ServiceCollection().AddLogging().AddRouting().BuildServiceProvider();
        var routes = new MemoryRoutes(services);
        ApiRoutes.Map(routes, f.Api, () => { });
        using var client = new VericairnClient(new HttpClient(new RouteTransport(routes, services)) { BaseAddress = new Uri("http://localhost/") });
        OperatorCloseCandidatesResponse listed = await client.OperatorCloseCandidatesAsync(TestContext.Current.CancellationToken);
        f.Review(second.Result, supersedes: second.Review.Id);
        int before = f.Estate.Load().Records.Count;
        OperatorCloseResponse refused = await client.OperatorCloseAsync(new(f.Estate.OperatorId, [.. listed.Items.Select(i => i.ToSelection())], true), TestContext.Current.CancellationToken);
        Assert.False(refused.Ok);
        Assert.Equal("conflict", refused.Problem!.Code);
        Assert.Empty(refused.Written);
        Assert.Equal(before, f.Estate.Load().Records.Count);
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
        context.Request.Path = "/v1/operator/close";
        context.Items[RequestAuthenticator.DeviceItem] = new AuthenticatedDevice("device", f.Estate.BuilderId, "public-key", localNetwork);
        bool reached = false;
        object? response = await scope.InvokeAsync(new DefaultEndpointFilterInvocationContext(context, f.Request()), _ =>
        { reached = true; return ValueTask.FromResult<object?>(null); });
        Assert.False(reached);
        Assert.Equal(StatusCodes.Status401Unauthorized, Assert.IsAssignableFrom<IStatusCodeHttpResult>(response).StatusCode);
        Assert.Empty(f.Estate.Load().OfType("acceptance"));
    }

    [Fact]
    public void TheContractDescribesBothRoutesAndEveryExactSelectionField()
    {
        Assert.True(RequestScope.Covers("GET /v1/operator/close"));
        Assert.True(RequestScope.Covers("POST /v1/operator/close"));
        Assert.True(RequestScope.Covers(typeof(OperatorCloseRequest)));
        using JsonDocument contract = JsonDocument.Parse(OpenApiDocument.Json);
        JsonElement path = contract.RootElement.GetProperty("paths").GetProperty("/v1/operator/close");
        Assert.Equal("getOperatorClose", path.GetProperty("get").GetProperty("operationId").GetString());
        Assert.False(path.GetProperty("get").TryGetProperty("requestBody", out _));
        Assert.Equal("#/components/schemas/OperatorCloseRequest", path.GetProperty("post").GetProperty("requestBody").GetProperty("content").GetProperty("application/json").GetProperty("schema").GetProperty("$ref").GetString());
        JsonElement schemas = contract.RootElement.GetProperty("components").GetProperty("schemas");
        Assert.Equal(["by", "selection"], schemas.GetProperty("OperatorCloseRequest").GetProperty("required").EnumerateArray().Select(v => v.GetString()));
        Assert.Equal(["key", "workOrder", "result", "reviews"], schemas.GetProperty("OperatorCloseSelection").GetProperty("required").EnumerateArray().Select(v => v.GetString()));
    }

    private sealed class MemoryRoutes(IServiceProvider services) : IEndpointRouteBuilder
    {
        public IServiceProvider ServiceProvider => services;
        public ICollection<EndpointDataSource> DataSources { get; } = [];
        public IApplicationBuilder CreateApplicationBuilder() => new ApplicationBuilder(services);
    }

    // Exercise the actual route delegates and both SDK transports without opening a local socket.
    private sealed class RouteTransport(MemoryRoutes routes, IServiceProvider services) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RouteEndpoint endpoint = Assert.Single(routes.DataSources.SelectMany(s => s.Endpoints).OfType<RouteEndpoint>(),
                e => e.RoutePattern.RawText == request.RequestUri!.AbsolutePath && e.Metadata.GetMetadata<HttpMethodMetadata>()!.HttpMethods.Contains(request.Method.Method));
            var context = new DefaultHttpContext { RequestServices = services, RequestAborted = cancellationToken };
            context.Request.Method = request.Method.Method;
            context.Request.Path = request.RequestUri!.AbsolutePath;
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
