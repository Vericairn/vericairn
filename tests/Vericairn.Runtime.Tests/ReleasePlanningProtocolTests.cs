// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Vericairn.Protocol;
using Vericairn.Sdk;

namespace Vericairn.Runtime.Tests;

public sealed class ReleasePlanningProtocolTests
{
    [Theory]
    [InlineData("/v1/releases/withdraw", typeof(ReleaseWithdrawRequest), typeof(ReleaseResponse))]
    [InlineData("/v1/work/{key}/move-plan", typeof(WorkMovePlanRequest), typeof(WorkMovePlanResponse))]
    public void BothRoutesDescribeTheirRequestAndCompleteResponse(string route, Type request, Type response)
    {
        Assert.Contains(("POST", route), OpenApiDocument.Routes);
        using JsonDocument document = JsonDocument.Parse(OpenApiDocument.Json);
        JsonElement operation = document.RootElement.GetProperty("paths").GetProperty(route).GetProperty("post");
        Assert.Equal("#/components/schemas/" + request.Name, operation.GetProperty("requestBody").GetProperty("content").GetProperty("application/json").GetProperty("schema").GetProperty("$ref").GetString());
        Assert.Equal("#/components/schemas/" + response.Name, operation.GetProperty("responses").GetProperty("200").GetProperty("content").GetProperty("application/json").GetProperty("schema").GetProperty("$ref").GetString());
        foreach (Type type in new[] { request, response, typeof(ReleaseObserveRequest) })
        {
            JsonElement properties = document.RootElement.GetProperty("components").GetProperty("schemas").GetProperty(type.Name).GetProperty("properties");
            Assert.Equal(type.GetProperties().Select(p => JsonNamingPolicy.CamelCase.ConvertName(p.Name)).Order(StringComparer.Ordinal), properties.EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal));
        }
    }

    [Theory]
    [InlineData("sync")]
    [InlineData("async")]
    [InlineData("bound")]
    public async Task SdkAndBoundClientsPreserveTheRequestsResultsAndRefusals(string surface)
    {
        using var f = new ReleasePlanningFixture();
        using var handler = new PlanningHandler(f);
        using var http = new HttpClient(handler) { BaseAddress = new Uri("http://fixture.invalid") };
        using var sdk = new VericairnClient(http);
        IEstateApi api = surface == "bound" ? new CancellationBoundApi(sdk, TestContext.Current.CancellationToken) : sdk;
        int before = f.Model.Records.Count;
        WorkMovePlanResponse moved = surface == "async" ? await api.WorkMovePlanAsync("PLAN-1", f.Move, TestContext.Current.CancellationToken) : api.WorkMovePlan("PLAN-1", f.Move);
        Assert.True(moved.Ok, JsonSerializer.Serialize(moved, ProtocolJson.Options));
        ReleaseResponse withdrawn = surface == "async" ? await api.ReleaseWithdrawAsync(f.Withdraw, TestContext.Current.CancellationToken) : api.ReleaseWithdraw(f.Withdraw);
        Assert.True(withdrawn.Ok, JsonSerializer.Serialize(withdrawn, ProtocolJson.Options));
        Assert.Equal(before + 4, f.Model.Records.Count);
        WorkMovePlanResponse staleMove = surface == "async" ? await api.WorkMovePlanAsync("PLAN-1", f.Move, TestContext.Current.CancellationToken) : api.WorkMovePlan("PLAN-1", f.Move);
        ReleaseResponse staleRelease = surface == "async" ? await api.ReleaseWithdrawAsync(f.Withdraw, TestContext.Current.CancellationToken) : api.ReleaseWithdraw(f.Withdraw);
        Assert.Equal("conflict", staleMove.Problem!.Code);
        Assert.Empty(staleMove.Written);
        Assert.Equal("conflict", staleRelease.Problem!.Code);
        Assert.Null(staleRelease.Release);
        Assert.Null(staleRelease.Operation);
        Assert.Equal(before + 4, f.Model.Records.Count);
        Assert.Equal(4, handler.Calls);
    }

    private sealed class PlanningHandler(ReleasePlanningFixture fixture) : HttpMessageHandler
    {
        public int Calls { get; private set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            Assert.Equal(HttpMethod.Post, request.Method);
            object response;
            switch (request.RequestUri!.AbsolutePath)
            {
                case "/v1/releases/withdraw":
                    var withdrawal = await request.Content!.ReadFromJsonAsync<ReleaseWithdrawRequest>(ProtocolJson.Options, cancellationToken);
                    Assert.Equal(fixture.Withdraw, withdrawal);
                    response = fixture.Api.ReleaseWithdraw(withdrawal!);
                    break;
                case "/v1/work/PLAN-1/move-plan":
                    var move = await request.Content!.ReadFromJsonAsync<WorkMovePlanRequest>(ProtocolJson.Options, cancellationToken);
                    Assert.Equal(JsonSerializer.Serialize(fixture.Move, ProtocolJson.Options), JsonSerializer.Serialize(move, ProtocolJson.Options));
                    response = fixture.Api.WorkMovePlan("PLAN-1", move!);
                    break;
                default: throw new InvalidOperationException("Unexpected planning route.");
            }
            return new(HttpStatusCode.OK) { Content = JsonContent.Create(response, response.GetType(), options: ProtocolJson.Options) };
        }
    }
}
