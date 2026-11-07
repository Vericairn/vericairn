// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Vericairn.Protocol;
using Vericairn.Records.Tests;
using Vericairn.Runtime.Service;
using Vericairn.Sdk;

namespace Vericairn.Daemon.Tests;

[Collection("daemon")]
public sealed class OperatorRecordApiTests
{
    public OperatorRecordApiTests() => DaemonTestHome.Ensure();

    [Fact(Timeout = TestTiming.HangTimeoutMilliseconds)]
    public async Task TheSynchronousSdkOperationRoundTripsARefusalWithoutAuthoringAnything()
    {
        using var estate = new TestEstate(3);
        await using DaemonHost host = await DaemonTestHome.StartAsync(estate.Root, loopback: false, cancellationToken: TestContext.Current.CancellationToken);
        using var api = new VericairnClient(host.Endpoint);
        int count = host.Service.Session.Load().Records.Count;
        var request = new OperatorRecordRequest("acceptance", estate.OperatorId, JsonSerializer.SerializeToElement(new { statement = "Inspect first." }));
        OperatorRecordResponse response = api.OperatorRecord(request);
        Assert.False(response.Ok);
        Assert.Equal(count, host.Service.Session.Load().Records.Count);
        // The asynchronous counterpart reaches the same route and refuses the same way.
        OperatorRecordResponse asynchronous = await api.OperatorRecordAsync(request, TestContext.Current.CancellationToken);
        Assert.False(asynchronous.Ok);
        Assert.Equal(count, host.Service.Session.Load().Records.Count);
        Assert.Contains("operator record acceptance", response.NextStep, StringComparison.Ordinal);
        Assert.True(RequestScope.Covers("POST /v1/operator/record"));
        Assert.True(RequestScope.Covers(typeof(OperatorRecordRequest)));
    }

    [Fact(Timeout = TestTiming.HangTimeoutMilliseconds)]
    public async Task TheUnfinishedActionReadAndFinishRoundTripWithoutAuthoringAnything()
    {
        using var estate = new TestEstate(3);
        await using DaemonHost host = await DaemonTestHome.StartAsync(estate.Root, loopback: false, cancellationToken: TestContext.Current.CancellationToken);
        using var api = new VericairnClient(host.Endpoint);
        int count = host.Service.Session.Load().Records.Count;
        Assert.Equal(new OperatorUnfinishedResponse(null), api.OperatorUnfinished());
        Assert.Equal(new OperatorUnfinishedResponse(null), await api.OperatorUnfinishedAsync(TestContext.Current.CancellationToken));
        OperatorRecordResponse finish = api.OperatorFinish(new(estate.OperatorId, 0));
        Assert.False(finish.Ok);
        OperatorRecordResponse asynchronous = await api.OperatorFinishAsync(new(estate.OperatorId, 0), TestContext.Current.CancellationToken);
        Assert.False(asynchronous.Ok);
        Assert.Equal(count, host.Service.Session.Load().Records.Count);
        Assert.True(RequestScope.Covers("GET /v1/operator/unfinished"));
        Assert.True(RequestScope.Covers("POST /v1/operator/unfinished"));
        Assert.True(RequestScope.Covers(typeof(OperatorFinishRequest)));
        using JsonDocument contract = JsonDocument.Parse(OpenApiDocument.Json);
        JsonElement path = contract.RootElement.GetProperty("paths").GetProperty("/v1/operator/unfinished");
        Assert.Equal("#/components/schemas/OperatorUnfinishedResponse", path.GetProperty("get").GetProperty("responses").GetProperty("200").GetProperty("content").GetProperty("application/json").GetProperty("schema").GetProperty("$ref").GetString());
        Assert.Equal("#/components/schemas/OperatorFinishRequest", path.GetProperty("post").GetProperty("requestBody").GetProperty("content").GetProperty("application/json").GetProperty("schema").GetProperty("$ref").GetString());
        foreach (Type type in new[] { typeof(OperatorFinishRequest), typeof(OperatorUnfinishedResponse), typeof(OperatorUnfinishedAction) })
        {
            string[] fields = contract.RootElement.GetProperty("components").GetProperty("schemas").GetProperty(type.Name).GetProperty("properties").EnumerateObject().Select(p => p.Name).Order().ToArray();
            Assert.Equal(type.GetProperties().Select(p => JsonNamingPolicy.CamelCase.ConvertName(p.Name)).Order(), fields);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FinishingRefusesANamedOperatorDifferentFromItsAuthenticatedPrincipal(bool localNetwork)
    {
        using var estate = new TestEstate(3);
        using EstateService service = EstateService.Open(estate.Root);
        string scratch = Path.Combine(Path.GetTempPath(), "vericairn-operator-finish-scope-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(scratch);
        try
        {
            var scope = new RequestScope(service, new LocalNetworkLedger(scratch));
            var context = new DefaultHttpContext();
            context.Request.Method = "POST";
            context.Request.Path = "/v1/operator/unfinished";
            context.Items[RequestAuthenticator.DeviceItem] = new AuthenticatedDevice("device", estate.BuilderId, "public-key", localNetwork);
            bool reached = false;
            object? response = await scope.InvokeAsync(new DefaultEndpointFilterInvocationContext(context, new OperatorFinishRequest(estate.OperatorId)), _ =>
            { reached = true; return ValueTask.FromResult<object?>(null); });
            Assert.False(reached);
            Assert.Equal(StatusCodes.Status401Unauthorized, Assert.IsAssignableFrom<IStatusCodeHttpResult>(response).StatusCode);
        }
        finally { Directory.Delete(scratch, recursive: true); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TheNewRouteRefusesANamedAuthorDifferentFromItsAuthenticatedPrincipal(bool localNetwork)
    {
        using var estate = new TestEstate(3);
        using EstateService service = EstateService.Open(estate.Root);
        string scratch = Path.Combine(Path.GetTempPath(), "vericairn-operator-scope-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(scratch);
        try
        {
            var scope = new RequestScope(service, new LocalNetworkLedger(scratch));
            var context = new DefaultHttpContext();
            context.Request.Method = "POST";
            context.Request.Path = "/v1/operator/record";
            context.Items[RequestAuthenticator.DeviceItem] = new AuthenticatedDevice("device", estate.BuilderId, "public-key", localNetwork);
            var request = new OperatorRecordRequest("acceptance", estate.OperatorId, JsonSerializer.SerializeToElement(new { }));
            bool reached = false;
            object? response = await scope.InvokeAsync(new DefaultEndpointFilterInvocationContext(context, request), _ =>
            { reached = true; return ValueTask.FromResult<object?>(null); });
            Assert.False(reached);
            Assert.Equal(StatusCodes.Status401Unauthorized, Assert.IsAssignableFrom<IStatusCodeHttpResult>(response).StatusCode);
            Assert.Empty(service.Session.Load().OfType("acceptance"));
        }
        finally { Directory.Delete(scratch, recursive: true); }
    }
}
