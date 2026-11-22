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

/// <summary>OPS-16 (0.9.4): the act answering asks with their recommendations over the API and the SDK, bound to the authenticated device.</summary>
[Collection("daemon")]
public sealed class OperatorAnswerApiTests
{
    public OperatorAnswerApiTests() => DaemonTestHome.Ensure();

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SdkAndApiCarryTheListingASelectionAndAllWithoutASocket(bool asynchronous)
    {
        using var f = new OperatorCloseFixture();
        OperatorAnswerTests.Ask(f, "First?", "Take the first.");
        OperatorAnswerTests.Ask(f, "Second?", "Take the second.");
        await using ServiceProvider services = new ServiceCollection().AddLogging().AddRouting().BuildServiceProvider();
        var routes = new MemoryRoutes(services);
        ApiRoutes.Map(routes, f.Api, () => Assert.Fail("Answering asks cannot shut down the runtime."));
        using var client = new VericairnClient(new HttpClient(new RouteTransport(routes, services)) { BaseAddress = new Uri("http://localhost/") });
        OperatorAnswerCandidatesResponse listed = asynchronous
            ? await client.OperatorAnswerCandidatesAsync(f.Estate.OperatorId, TestContext.Current.CancellationToken) : client.OperatorAnswerCandidates(f.Estate.OperatorId);
        Assert.True(listed.Ok);
        Assert.Empty(client.OperatorAnswerCandidates(f.Estate.BuilderId).Items);
        Assert.Equal(JsonSerializer.Serialize(f.Api.OperatorAnswerCandidates(f.Estate.OperatorId), ProtocolJson.Options), JsonSerializer.Serialize(listed, ProtocolJson.Options));
        var request = new OperatorAnswerRequest(f.Estate.OperatorId, [listed.Items[1].ToSelection()]);
        OperatorAnswerResponse partial = asynchronous ? await client.OperatorAnswerAsync(request, TestContext.Current.CancellationToken) : client.OperatorAnswer(request);
        Assert.True(partial.Ok, partial.Problem?.Message);
        Assert.NotNull(partial.Act);
        Assert.Single(partial.Written);
        OperatorAnswerCandidatesResponse remaining = new CancellationBoundApi(client, TestContext.Current.CancellationToken).OperatorAnswerCandidates();
        OperatorAnswerResponse all = await new CancellationBoundApi(client, TestContext.Current.CancellationToken).OperatorAnswerAsync(
            new(f.Estate.OperatorId, [.. remaining.Items.Select(i => i.ToSelection())], All: true), TestContext.Current.CancellationToken);
        Assert.True(all.Ok, all.Problem?.Message);
        Assert.NotEqual(partial.Act, all.Act);
        Assert.Empty(client.OperatorAnswerCandidates().Items);
        Assert.Equal(2, f.Estate.Load().OfType("decision").Count());
        Assert.Equal(0, f.Api.Validate().Errors);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TheRouteBindsTheActingOperatorToTheAuthenticatedDevice(bool localNetwork)
    {
        using var f = new OperatorCloseFixture();
        OperatorAnswerTests.Ask(f, "First?", "Take the first.");
        var scope = new RequestScope(f.Api, new LocalNetworkLedger(f.Estate.Root));
        var context = new DefaultHttpContext();
        context.Request.Method = "POST";
        context.Request.Path = "/v1/operator/answers";
        context.Items[RequestAuthenticator.DeviceItem] = new AuthenticatedDevice("device", f.Estate.BuilderId, "public-key", localNetwork);
        bool reached = false;
        var request = new OperatorAnswerRequest(f.Estate.OperatorId, [.. f.Api.OperatorAnswerCandidates().Items.Select(i => i.ToSelection())]);
        object? response = await scope.InvokeAsync(new DefaultEndpointFilterInvocationContext(context, request), _ =>
        { reached = true; return ValueTask.FromResult<object?>(null); });
        Assert.False(reached);
        Assert.Equal(StatusCodes.Status401Unauthorized, Assert.IsAssignableFrom<IStatusCodeHttpResult>(response).StatusCode);
        Assert.Empty(f.Estate.Load().OfType("decision"));
    }

    [Fact]
    public void TheContractDescribesBothRoutesAndEveryExactSelectionField()
    {
        Assert.True(RequestScope.Covers("GET /v1/operator/answers"));
        Assert.True(RequestScope.Covers("POST /v1/operator/answers"));
        Assert.True(RequestScope.Covers(typeof(OperatorAnswerRequest)));
        using JsonDocument contract = JsonDocument.Parse(OpenApiDocument.Json);
        JsonElement path = contract.RootElement.GetProperty("paths").GetProperty("/v1/operator/answers");
        Assert.Equal("to", path.GetProperty("get").GetProperty("parameters")[0].GetProperty("name").GetString());
        Assert.Equal("#/components/schemas/OperatorAnswerRequest", path.GetProperty("post").GetProperty("requestBody").GetProperty("content").GetProperty("application/json").GetProperty("schema").GetProperty("$ref").GetString());
        JsonElement schemas = contract.RootElement.GetProperty("components").GetProperty("schemas");
        Assert.Equal(["by", "selection"], schemas.GetProperty("OperatorAnswerRequest").GetProperty("required").EnumerateArray().Select(v => v.GetString()));
        Assert.Equal(["ask", "recommendation"], schemas.GetProperty("OperatorAnswerSelection").GetProperty("required").EnumerateArray().Select(v => v.GetString()));
        Assert.True(schemas.GetProperty("OperatorAnswerResponse").GetProperty("properties").TryGetProperty("act", out _));
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
