// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Vericairn.Protocol;
using Vericairn.Records.Canonical;
using Vericairn.Records.Tests;
using Vericairn.Runtime.Service;
using Vericairn.Sdk;

namespace Vericairn.Daemon.Tests;

[Collection("daemon")]
public class OperatorActionsApiTests
{
    public OperatorActionsApiTests() => DaemonTestHome.Ensure();

    [Fact]
    public async Task QueueRouteIsAReadAndPreservesTheCompleteProjectionWithoutOpeningASocket()
    {
        using var estate = new TestEstate(2);
        estate.Add("ask", estate.BuilderId, $$"""{"to":"{{estate.OperatorId}}","question":"Proceed?","recommendation":"Review first.","consequence":"Work waits.","state":"open"}""");
        using EstateService api = EstateService.Open(estate.Root);
        await using ServiceProvider services = new ServiceCollection().AddLogging().AddRouting().BuildServiceProvider();
        var routes = new MemoryRoutes(services);
        ApiRoutes.Map(routes, api, () => throw new InvalidOperationException("A read cannot shut down the service."));
        RouteEndpoint endpoint = Assert.Single(routes.DataSources.SelectMany(s => s.Endpoints).OfType<RouteEndpoint>(),
            e => e.RoutePattern.RawText == "/v1/operator/actions");
        Assert.Equal(["GET"], endpoint.Metadata.GetMetadata<HttpMethodMetadata>()!.HttpMethods);
        var context = new DefaultHttpContext { RequestServices = services };
        context.Request.Method = "GET";
        context.Request.QueryString = new QueryString("?recipient=" + estate.OperatorId + "&status=comprehensive&progress=none");
        await using var output = new MemoryStream();
        context.Response.Body = output;
        int before = estate.Load().Records.Count;
        await endpoint.RequestDelegate!(context);
        output.Position = 0;
        OperatorActionsResponse response = (await JsonSerializer.DeserializeAsync<OperatorActionsResponse>(output, ProtocolJson.Options, TestContext.Current.CancellationToken))!;
        Assert.Equal(200, context.Response.StatusCode);
        Assert.False(response.HasErrors);
        Assert.StartsWith("status (comprehensive)", response.Text, StringComparison.Ordinal);
        Assert.Equal(CanonicalJson.SerializePretty(api.OperatorActions().Json), CanonicalJson.SerializePretty(response.Json));
        Assert.Equal(before, estate.Load().Records.Count);
        using JsonDocument contract = JsonDocument.Parse(OpenApiDocument.Json);
        JsonElement operation = contract.RootElement.GetProperty("paths").GetProperty("/v1/operator/actions").GetProperty("get");
        Assert.Equal("getOperatorActions", operation.GetProperty("operationId").GetString());
        Assert.False(operation.TryGetProperty("requestBody", out _));
        Assert.Contains(operation.GetProperty("parameters").EnumerateArray(), p => p.GetProperty("name").GetString() == "recipient");
        Assert.Contains("OperatorActionsResponse", operation.GetProperty("responses").GetProperty("200").GetProperty("description").GetString()!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SdkReadsTheSameQueueOverTheDaemonsLocalSocket()
    {
        using var estate = new TestEstate(2);
        estate.Add("ask", estate.BuilderId, $$"""{"to":"{{estate.OperatorId}}","question":"Proceed?","recommendation":"Review first.","consequence":"Work waits.","state":"open"}""");
        await using DaemonHost host = await DaemonTestHome.StartAsync(estate.Root, loopback: false, cancellationToken: TestContext.Current.CancellationToken);
        using var client = new VericairnClient(host.Endpoint);
        int before = estate.Load().Records.Count;
        OperatorActionsResponse remote = client.OperatorActions(estate.OperatorId, view: new(Progress: "none", Status: "short"));
        Assert.False(remote.HasErrors);
        Assert.Equal(CanonicalJson.SerializePretty(host.Service.OperatorActions().Json), CanonicalJson.SerializePretty(remote.Json));
        Assert.StartsWith("status (short)", remote.Text, StringComparison.Ordinal);
        Assert.Equal(before, estate.Load().Records.Count);
        Assert.Empty(estate.Load().Current("device"));
    }

    private sealed class MemoryRoutes(IServiceProvider services) : IEndpointRouteBuilder
    {
        public IServiceProvider ServiceProvider => services;
        public ICollection<EndpointDataSource> DataSources { get; } = new List<EndpointDataSource>();
        public IApplicationBuilder CreateApplicationBuilder() => new ApplicationBuilder(services);
    }
}
