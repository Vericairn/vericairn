// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Vericairn.Mcp;
using Vericairn.Protocol;
using Vericairn.Records.Canonical;
using Vericairn.Records.Tests;
using Vericairn.Runtime.Service;
using Vericairn.Sdk;

namespace Vericairn.Runtime.Tests;

[Collection("home")]
public sealed class ProgressContractTests
{
    [Theory]
    [MemberData(nameof(RequestedProgressTests.Values), MemberType = typeof(RequestedProgressTests))]
    public async Task SdkSyncAsyncAndBoundClientsKeepTheValueAndStatusThroughTheApi(string value)
    {
        using var fixture = new ProgressFixture();
        fixture.Set("estate", ProgressRequestTests.FullRequest);
        using EstateService service = EstateService.Open(fixture.Estate.Root);
        using var handler = new ProgressHandler(service);
        using var client = new VericairnClient(new HttpClient(handler) { BaseAddress = new Uri("https://fixture.invalid") });
        var bound = new CancellationBoundApi(client, TestContext.Current.CancellationToken);
        WorkProgressRequest request = RequestedProgressTests.Request(fixture, value);
        int records = service.Session.Load().Records.Count;
        foreach (IEstateApi api in new IEstateApi[] { client, bound })
        {
            Check(api.WorkProgress(request));
            Check(await api.WorkProgressAsync(request, TestContext.Current.CancellationToken));
        }
        Assert.Equal(4, handler.Calls);
        Assert.Equal(records, service.Session.Load().Records.Count);
        WorkProgressResponse completion = await client.WorkProgressAsync(request with { Completed = true }, TestContext.Current.CancellationToken);
        Check(completion);
        Assert.NotNull(completion.Written);
        Assert.Equal(records + 1, service.Session.Load().Records.Count);
        WorkProgressResponse refused = client.WorkProgress(request with { Phase = "malformed", Progress = RunEstate.Json("""{"type":"best-effort","percent":50,"unit":"files"}""") });
        Assert.False(refused.Ok);
        Assert.Null(refused.Written);
        Assert.Equal(records + 1, service.Session.Load().Records.Count);

        void Check(WorkProgressResponse response)
        {
            Assert.True(response.Ok, response.Problem?.Message);
            Assert.Equal(request.StatusText, response.Claim!.StatusText);
            RequestedProgressTests.EqualValue(value, response.Claim.Progress!.Value);
        }
    }

    [Theory]
    [MemberData(nameof(RequestedProgressTests.Values), MemberType = typeof(RequestedProgressTests))]
    public async Task McpAcceptsAndRetainsEachValueForLiveClaimsAndOneCompletion(string value)
    {
        using var fixture = new ProgressFixture();
        fixture.Set("goal", ProgressRequestTests.FullRequest);
        using EstateService service = EstateService.Open(fixture.Estate.Root);
        ToolRegistry tools = RequestedProgressTests.Tools(fixture, service);
        int records = service.Session.Load().Records.Count;
        JsonObject arguments = RequestedProgressTests.Arguments(fixture, value);
        WorkProgressResponse active = Read(await tools.CallAsync("work_progress", arguments, TestContext.Current.CancellationToken));
        Assert.Null(active.Written);
        Assert.Equal(records, service.Session.Load().Records.Count);
        arguments["state"] = "completed";
        WorkProgressResponse completed = Read(await tools.CallAsync("work_progress", arguments, TestContext.Current.CancellationToken));
        Assert.Equal(completed.Written, Read(await tools.CallAsync("work_progress", arguments, TestContext.Current.CancellationToken)).Written);
        Assert.Equal(records + 1, service.Session.Load().Records.Count);

        WorkProgressResponse Read(ToolCallResult result)
        {
            Assert.False(result.IsError, result.Text);
            WorkProgressResponse response = JsonSerializer.Deserialize<WorkProgressResponse>(result.Text, ProtocolJson.Options)!;
            Assert.True(response.Ok);
            RequestedProgressTests.EqualValue(value, response.Claim!.Progress!.Value);
            Assert.Equal("Reading the widget.", response.Claim.StatusText);
            return response;
        }
    }

    [Fact]
    public void AdditionsKeepExistingConstructorsAndGoldenWireShapesAndHaveMatchingDiscovery()
    {
        Assert.Single(typeof(WorkProgressRequest).GetConstructors(), c => c.GetParameters().Length == 5);
        Assert.Single(typeof(WorkProgressClaim).GetConstructors(), c => c.GetParameters().Length == 7);
        var legacy = new WorkProgressRequest("attempt", "contributor", "build", "Working.");
        Assert.Equal("""{"attempt":"attempt","by":"contributor","completed":false,"note":"Working.","phase":"build"}""",
            CanonicalJson.Serialize(JsonSerializer.SerializeToElement(legacy, ProtocolJson.Options)));
        var typed = legacy with
        {
            Progress = RunEstate.Json("""{"type":"accurate","done":7,"total":20,"unit":"files","estimatePercent":60}"""),
            StatusText = "Reading the widget.",
        };
        string json = JsonSerializer.Serialize(typed, ProtocolJson.Options);
        Assert.Equal("""{"attempt":"attempt","by":"contributor","completed":false,"note":"Working.","phase":"build","progress":{"done":7,"estimatePercent":60,"total":20,"type":"accurate","unit":"files"},"statusText":"Reading the widget."}""",
            CanonicalJson.Serialize(RunEstate.Json(json)));
        WorkProgressRequest roundTrip = JsonSerializer.Deserialize<WorkProgressRequest>(json, ProtocolJson.Options)!;
        RequestedProgressTests.EqualValue(typed.Progress!.Value.GetRawText(), roundTrip.Progress!.Value);
        Assert.Equal(typed.StatusText, roundTrip.StatusText);

        using var fixture = new ProgressFixture();
        using EstateService service = EstateService.Open(fixture.Estate.Root);
        JsonObject mcp = new ToolRegistry(service, null).All.Single(t => t.Name == "work_progress").InputSchema;
        using JsonDocument document = JsonDocument.Parse(OpenApiDocument.Json);
        JsonElement schemas = document.RootElement.GetProperty("components").GetProperty("schemas");
        foreach (Type contract in new[] { typeof(WorkProgressRequest), typeof(WorkProgressClaim), typeof(WorkProgressResponse) })
            Assert.Equal(contract.GetProperties().Select(p => JsonNamingPolicy.CamelCase.ConvertName(p.Name)).Order(StringComparer.Ordinal),
                schemas.GetProperty(contract.Name).GetProperty("properties").EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal));
        JsonElement properties = schemas.GetProperty("WorkProgressRequest").GetProperty("properties");
        Assert.True(JsonNode.DeepEquals(mcp["properties"]!["progress"], JsonNode.Parse(properties.GetProperty("progress").GetRawText())));
        Assert.True(JsonNode.DeepEquals(mcp["properties"]!["statusText"], JsonNode.Parse(properties.GetProperty("statusText").GetRawText())));
        Assert.Equal(["attempt", "phase", "note", "state"], mcp["required"]!.AsArray().Select(n => n!.GetValue<string>()));
        Assert.Equal(3, properties.GetProperty("progress").GetProperty("oneOf").GetArrayLength());
        Assert.Equal("#/components/schemas/WorkProgressResponse", document.RootElement.GetProperty("paths").GetProperty("/v1/work/progress")
            .GetProperty("post").GetProperty("responses").GetProperty("200").GetProperty("content").GetProperty("application/json").GetProperty("schema").GetProperty("$ref").GetString());
    }

    private sealed class ProgressHandler(EstateService service) : HttpMessageHandler
    {
        public int Calls { get; private set; }

        protected override HttpResponseMessage Send(HttpRequestMessage request, CancellationToken cancellationToken) =>
            SendAsync(request, cancellationToken).GetAwaiter().GetResult();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal("/v1/work/progress", request.RequestUri!.AbsolutePath);
            WorkProgressRequest value = (await request.Content!.ReadFromJsonAsync<WorkProgressRequest>(ProtocolJson.Options, cancellationToken))!;
            Calls++;
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(service.WorkProgress(value), options: ProtocolJson.Options) };
        }
    }
}
