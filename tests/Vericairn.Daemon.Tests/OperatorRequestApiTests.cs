// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Template;
using Microsoft.Extensions.DependencyInjection;
using Vericairn.Protocol;
using Vericairn.Records.Model;
using Vericairn.Runtime.Tests;
using Vericairn.Sdk;
using RequestData = Vericairn.Records.Tests.OperatorRequestTests;

namespace Vericairn.Daemon.Tests;

[Collection("daemon")]
public sealed class OperatorRequestApiTests
{
    public OperatorRequestApiTests() => DaemonTestHome.Ensure();

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SdkAndApiCreateListReviseCarryAndCloseExactRequestsWithoutASocket(bool asynchronous)
    {
        using var f = new OperatorCloseFixture();
        RecordEnvelope work = RequestData.Work(f.Estate);
        await using ServiceProvider services = new ServiceCollection().AddLogging().AddRouting().BuildServiceProvider();
        var routes = new MemoryRoutes(services);
        ApiRoutes.Map(routes, f.Api, () => Assert.Fail("Request authoring cannot shut down the runtime."));
        using var client = new VericairnClient(new HttpClient(new RouteTransport(routes, services)) { BaseAddress = new Uri("http://localhost/") });
        var input = new OperatorRequestWriteRequest(f.Estate.BuilderId, JsonSerializer.SerializeToElement(RequestData.Body(f.Estate)), OperatorRequestTests.Subject(work));
        WriteOutcome created = asynchronous ? await client.OperatorRequestWriteAsync(input, TestContext.Current.CancellationToken) : client.OperatorRequestWrite(input);
        Assert.True(created.Ok, created.Problem?.Message);
        OperatorRequestsResponse queue = asynchronous ? await client.OperatorRequestsAsync(f.Estate.OperatorId, TestContext.Current.CancellationToken) : client.OperatorRequests(f.Estate.OperatorId);
        Assert.Equal(1, queue.Json.GetProperty("waiting").GetInt32());
        Assert.Equal(f.Api.OperatorRequests(f.Estate.OperatorId).Json.GetRawText(), queue.Json.GetRawText());
        input = input with { Supersedes = created.Written!.Id, Expected = created.Written.Hash };
        WriteOutcome revision = await client.OperatorRequestWriteAsync(input, TestContext.Current.CancellationToken);
        Assert.True(revision.Ok, revision.Problem?.Message);
        Assert.False(client.OperatorRequestDone(created.Written.Id, new(f.Estate.OperatorId)).Ok);
        var bound = new CancellationBoundApi(client, TestContext.Current.CancellationToken);
        WriteOutcome done = asynchronous
            ? await bound.OperatorRequestDoneAsync(revision.Written!.Id, new(f.Estate.OperatorId), TestContext.Current.CancellationToken)
            : bound.OperatorRequestDone(revision.Written!.Id, new(f.Estate.OperatorId));
        Assert.True(done.Ok, done.Problem?.Message);
        RecordEnvelope ask = f.Estate.Add("ask", f.Estate.BuilderId, new JsonObject
        {
            ["to"] = f.Estate.OperatorId, ["question"] = "Perform another action?", ["recommendation"] = "Perform it.", ["consequence"] = "Work waits.", ["state"] = "open",
        }.ToJsonString(), work.Reference);
        var carryInput = new OperatorRequestCarryRequest(f.Estate.OperatorId, ask.ComputedHash, input.Body, input.Subject);
        WriteOutcome carried = asynchronous ? await bound.OperatorRequestCarryAsync(ask.Id, carryInput, TestContext.Current.CancellationToken) : bound.OperatorRequestCarry(ask.Id, carryInput);
        Assert.True(carried.Ok, carried.Problem?.Message);
        var closeInput = new OperatorRequestCloseRequest(f.Estate.OperatorId, Reason: "No longer needed.");
        WriteOutcome dismissed = asynchronous ? await client.OperatorRequestDismissAsync(carried.Written!.Id, closeInput, TestContext.Current.CancellationToken) : client.OperatorRequestDismiss(carried.Written!.Id, closeInput);
        Assert.True(dismissed.Ok, dismissed.Problem?.Message);
        Assert.Equal(0, (await bound.OperatorRequestsAsync(cancellationToken: TestContext.Current.CancellationToken)).Json.GetProperty("waiting").GetInt32());
        Assert.Equal(2, f.Estate.Load().OfType("operator-request-close").Count());
        Assert.Equal(0, f.Api.Validate().Errors);
    }

    [Fact]
    public async Task ADeviceCannotAttributeItsWriteToAnotherPrincipal()
    {
        using var f = new OperatorCloseFixture();
        RecordEnvelope request = OperatorRequestTests.Request(f);
        var scope = new RequestScope(f.Api, new LocalNetworkLedger(f.Estate.Root));
        var context = new DefaultHttpContext();
        context.Request.Method = "POST";
        context.Request.Path = "/v1/operator/requests/" + request.Id + "/done";
        context.SetEndpoint(new RouteEndpoint(_ => Task.CompletedTask, Microsoft.AspNetCore.Routing.Patterns.RoutePatternFactory.Parse("/v1/operator/requests/{id}/done"), 0, EndpointMetadataCollection.Empty, "done"));
        context.Items[RequestAuthenticator.DeviceItem] = new AuthenticatedDevice("device", f.Estate.BuilderId, "public-key", false);
        bool reached = false;
        object? response = await scope.InvokeAsync(new DefaultEndpointFilterInvocationContext(context, request.Id, new OperatorRequestCloseRequest(f.Estate.OperatorId)), _ =>
        { reached = true; return ValueTask.FromResult<object?>(null); });
        Assert.False(reached);
        Assert.Equal(StatusCodes.Status401Unauthorized, Assert.IsAssignableFrom<IStatusCodeHttpResult>(response).StatusCode);
        Assert.Empty(f.Estate.Load().OfType("operator-request-close"));
    }

    [Fact]
    public void EveryNewRouteAndBoundRequestHasAnExplicitScopeAndSchema()
    {
        foreach ((string method, string path) in OpenApiDocument.Routes.Where(r => r.Path.StartsWith("/v1/operator/request", StringComparison.Ordinal) || r.Path.Contains("/asks/", StringComparison.Ordinal)))
            Assert.True(RequestScope.Covers(method + " " + path));
        Assert.True(RequestScope.Covers(typeof(OperatorRequestWriteRequest)));
        Assert.True(RequestScope.Covers(typeof(OperatorRequestCarryRequest)));
        Assert.True(RequestScope.Covers(typeof(OperatorRequestCloseRequest)));
        using JsonDocument contract = JsonDocument.Parse(OpenApiDocument.Json);
        JsonElement schemas = contract.RootElement.GetProperty("components").GetProperty("schemas");
        Assert.Equal(["by"], schemas.GetProperty("OperatorRequestCloseRequest").GetProperty("required").EnumerateArray().Select(v => v.GetString()));
        Assert.Equal("#/components/schemas/OperatorRequestWords", schemas.GetProperty("OperatorRequestCloseRequest").GetProperty("properties").GetProperty("operatorWords").GetProperty("$ref").GetString());
    }

    private sealed class MemoryRoutes(IServiceProvider services) : IEndpointRouteBuilder
    {
        public IServiceProvider ServiceProvider => services;
        public ICollection<EndpointDataSource> DataSources { get; } = [];
        public IApplicationBuilder CreateApplicationBuilder() => new ApplicationBuilder(services);
    }

    private sealed class RouteTransport(MemoryRoutes routes, IServiceProvider services) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
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
            var candidates = routes.DataSources.SelectMany(s => s.Endpoints).OfType<RouteEndpoint>().Select(e => (Endpoint: e, Values: new RouteValueDictionary()))
                .Where(pair => pair.Endpoint.Metadata.GetMetadata<HttpMethodMetadata>()!.HttpMethods.Contains(request.Method.Method, StringComparer.Ordinal)
                    && new TemplateMatcher(TemplateParser.Parse(pair.Endpoint.RoutePattern.RawText!), new()).TryMatch(context.Request.Path, pair.Values));
            var route = Assert.Single(candidates);
            context.Request.RouteValues = route.Values;
            context.SetEndpoint(route.Endpoint);
            await route.Endpoint.RequestDelegate!(context);
            return new HttpResponseMessage((HttpStatusCode)context.Response.StatusCode) { Content = new ByteArrayContent(output.ToArray()) };
        }
    }

    private sealed record BodyDetection(bool CanHaveBody) : IHttpRequestBodyDetectionFeature;
}
