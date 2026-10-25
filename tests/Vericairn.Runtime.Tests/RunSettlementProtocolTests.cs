// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Vericairn.Protocol;
using Vericairn.Sdk;

namespace Vericairn.Runtime.Tests;

public sealed class RunSettlementProtocolTests
{
    [Fact]
    public void RecoveryHasAnAdditiveContractAndDefaultInterfaceImplementations()
    {
        Assert.Contains(("POST", "/v1/runs/{id}/settle"), OpenApiDocument.Routes);
        using JsonDocument document = JsonDocument.Parse(OpenApiDocument.Json);
        JsonElement operation = document.RootElement.GetProperty("paths").GetProperty("/v1/runs/{id}/settle").GetProperty("post");
        Assert.Equal("#/components/schemas/RunSettleRequest", operation.GetProperty("requestBody").GetProperty("content").GetProperty("application/json").GetProperty("schema").GetProperty("$ref").GetString());
        Assert.Equal("#/components/schemas/RunSettleResponse", operation.GetProperty("responses").GetProperty("200").GetProperty("content").GetProperty("application/json").GetProperty("schema").GetProperty("$ref").GetString());
        foreach (Type contract in new[] { typeof(RunSettleRequest), typeof(RunSettleResponse) })
            Assert.Equal(contract.GetProperties().Select(p => JsonNamingPolicy.CamelCase.ConvertName(p.Name)).Order(),
                document.RootElement.GetProperty("components").GetProperty("schemas").GetProperty(contract.Name).GetProperty("properties").EnumerateObject().Select(p => p.Name).Order());
        Assert.False(typeof(IEstateApi).GetMethod(nameof(IEstateApi.RunSettle))!.IsAbstract);
        Assert.False(typeof(IEstateApi).GetMethod(nameof(IEstateApi.RunSettleAsync))!.IsAbstract);
    }

    [Fact]
    public async Task SynchronousAsyncAndCancellationBoundClientsSendTheSameRequestAndPreserveTheReceipt()
    {
        using var handler = new SettlementHandler();
        using var http = new HttpClient(handler) { BaseAddress = new Uri("http://fixture.invalid") };
        using var client = new VericairnClient(http);
        var bound = new CancellationBoundApi(client, TestContext.Current.CancellationToken);
        RunSettleResponse[] responses =
        [
            client.RunSettle("run:one/part", new("principal")),
            await client.RunSettleAsync("run:one/part", new("principal"), TestContext.Current.CancellationToken),
            bound.RunSettle("run:one/part", new("principal")),
            await bound.RunSettleAsync("run:one/part", new("principal"), TestContext.Current.CancellationToken),
        ];
        Assert.Equal(4, handler.Calls);
        Assert.All(responses, response => Assert.Equal(SettlementHandler.Response, response));
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        ClientCancelledException failure = await Assert.ThrowsAsync<ClientCancelledException>(() =>
            client.RunSettleAsync("run:one/part", new("principal"), cancelled.Token));
        Assert.False(failure.OutcomeUnknown);
        Assert.Equal(4, handler.Calls);
    }

    private sealed class SettlementHandler : HttpMessageHandler
    {
        internal static readonly RunSettleResponse Response = new("run", "observed-success", "Recovered.", true, new("operation.json", "settlement", "sha256:" + new string('a', 64)));
        internal int Calls { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal("/v1/runs/run%3Aone%2Fpart/settle", request.RequestUri!.AbsolutePath);
            Assert.Equal(new RunSettleRequest("principal"), await request.Content!.ReadFromJsonAsync<RunSettleRequest>(ProtocolJson.Options, cancellationToken));
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(Response, options: ProtocolJson.Options) };
        }
    }
}
