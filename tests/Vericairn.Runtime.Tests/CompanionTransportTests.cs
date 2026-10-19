// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Vericairn.Mcp;
using Vericairn.Protocol;
using Vericairn.Records.Tests;
using Vericairn.Runtime.Service;
using Vericairn.Sdk;

namespace Vericairn.Runtime.Tests;

public sealed class CompanionTransportTests
{
    [Fact]
    public async Task McpReadsShareTheRuntimeSelectionAndExposeNoLocalInspectorOperation()
    {
        using var fixture = new PolicyFixture();
        using EstateService service = EstateService.Open(fixture.Estate.Root);
        var registry = new ToolRegistry(service, null);
        Assert.All(registry.All.Where(t => t.Name.StartsWith("docs_", StringComparison.Ordinal)), t => Assert.False(t.Writes));
        var arguments = new JsonObject { ["generator"] = "vericairn.records/items", ["selection"] = "item", ["revision"] = fixture.Decision.Id };
        ToolCallResult rendered = await registry.CallAsync("docs_render", arguments, TestContext.Current.CancellationToken);
        Assert.False(rendered.IsError, rendered.Text);
        CompanionResponse response = JsonSerializer.Deserialize<CompanionResponse>(rendered.Text, ProtocolJson.Options)!;
        Assert.Equal(service.CompanionRender(new("vericairn.records/items", "item", fixture.Decision.Id)).Text, response.Text);
        ToolCallResult sources = await registry.CallAsync("docs_sources", arguments, TestContext.Current.CancellationToken);
        Assert.Null(JsonSerializer.Deserialize<CompanionResponse>(sources.Text, ProtocolJson.Options)!.Text);
        arguments["generator"] = service.Companions().Generators.First(g => g.LocalOnly).Id;
        ToolCallResult local = await registry.CallAsync("docs_render", arguments, TestContext.Current.CancellationToken);
        Assert.True(local.IsError);
        Assert.Null(JsonSerializer.Deserialize<CompanionResponse>(local.Text, ProtocolJson.Options)!.Snapshot);
        Assert.DoesNotContain(registry.All, t => t.Name.Contains("local_inspect", StringComparison.Ordinal));
        arguments["page"] = "0";
        Assert.True((await registry.CallAsync("docs_render", arguments, TestContext.Current.CancellationToken)).IsError);
    }

    [Fact]
    public async Task SdkSyncAndAsyncReadsUseTheSameEscapedRoutesWithoutSendingSourceBytes()
    {
        var seen = new List<string>();
        using var client = new VericairnClient(new HttpClient(new Handler((request, _) =>
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Null(request.Content);
            seen.Add(request.RequestUri!.PathAndQuery);
            object value = request.RequestUri.AbsolutePath switch
            {
                "/v1/companions" => new CompanionsResponse(1, "success", "basis", [], [], [], null),
                "/v1/companions/compare" => new CompanionComparison(1, "success", "current", "basis", null, [], []),
                _ => CompanionResponse.Unavailable("Fixture source unavailable."),
            };
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(value, value.GetType(), options: ProtocolJson.Options) });
        })) { BaseAddress = new Uri("https://fixture.invalid") });
        var selection = new CompanionRequest("example.reader/items", "item", "revision", Field: "field.with spaces", Index: "all");
        Assert.Equal("success", client.Companions().Status);
        Assert.Equal("success", (await client.CompanionsAsync(TestContext.Current.CancellationToken)).Status);
        client.CompanionRender(selection);
        await client.CompanionRenderAsync(selection, TestContext.Current.CancellationToken);
        client.CompanionSources(selection);
        await client.CompanionSourcesAsync(selection, TestContext.Current.CancellationToken);
        client.CompanionCompare(new("docs/example.md"));
        await client.CompanionCompareAsync(new("docs/example.md"), TestContext.Current.CancellationToken);
        for (int i = 0; i < seen.Count; i += 2) Assert.Equal(seen[i], seen[i + 1]);
        Assert.Contains("generator=example.reader%2Fitems", seen[2], StringComparison.Ordinal);
        Assert.Contains("field=field.with%20spaces", seen[2], StringComparison.Ordinal);
        Assert.Contains("selection=docs%2Fexample.md", seen[6], StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("coverage")]
    [InlineData("render")]
    [InlineData("sources")]
    [InlineData("compare")]
    public async Task EverySdkReadHasADeadlineAndCancellationWithoutWriteUncertainty(string operation)
    {
        using var client = new VericairnClient(new HttpClient(new Handler(async (_, token) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return new(HttpStatusCode.OK);
        })) { BaseAddress = new Uri("https://fixture.invalid") }) { OperationTimeout = _ => TimeSpan.FromMilliseconds(50) };
        async Task Read(CancellationToken token)
        {
            switch (operation)
            {
                case "coverage": await client.CompanionsAsync(token); break;
                case "render": await client.CompanionRenderAsync(new("fixture", "item"), token); break;
                case "sources": await client.CompanionSourcesAsync(new("fixture", "item"), token); break;
                default: await client.CompanionCompareAsync(new("fixture"), token); break;
            }
        }
        ClientTimeoutException timeout = await Assert.ThrowsAsync<ClientTimeoutException>(() => Read(TestContext.Current.CancellationToken));
        Assert.Equal(ClientFailure.FromException(new ClientTimeoutException(false))!.Message, ClientFailure.FromException(timeout)!.Message);
        using var stopped = new CancellationTokenSource();
        stopped.Cancel();
        await Assert.ThrowsAsync<ClientCancelledException>(() => Read(stopped.Token));
    }

    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => send(request, cancellationToken);
    }
}
