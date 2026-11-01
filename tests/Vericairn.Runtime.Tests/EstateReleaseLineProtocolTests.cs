// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Vericairn.Protocol;
using Vericairn.Records.Tests;
using Vericairn.Runtime.Service;
using Vericairn.Sdk;

namespace Vericairn.Runtime.Tests;

public class EstateReleaseLineProtocolTests
{
    [Theory]
    [InlineData("sync")]
    [InlineData("async")]
    [InlineData("bound")]
    public async Task ExplicitSelectionsSurviveTheClientBoundaryAndOrdinaryRequestsStillStayOnTheirLine(string surface)
    {
        using var f = new EstateLineFixture();
        f.Publish("1.1.0", "1.x", "lts");
        f.Publish("2.0.0");
        f.Follow("1.x", "lts", "1.0.0");
        using var service = EstateService.Open(f.Estate.Root);
        using var handler = new UpgradeHandler(service);
        using var http = new HttpClient(handler) { BaseAddress = new Uri("http://fixture.invalid") };
        using var client = new VericairnClient(http);
        IEstateApi api = surface == "bound" ? new CancellationBoundApi(client, TestContext.Current.CancellationToken) : client;
        byte[] before = File.ReadAllBytes(f.ManifestPath);
        var ordinary = new UpgradeRequest("2.0.0", null, true);
        var refused = surface == "async" ? await api.UpgradeAsync(ordinary, TestContext.Current.CancellationToken) : api.Upgrade(ordinary);
        Assert.Null(refused.Plan);
        Assert.Equal("refused", refused.Problem!.Code);
        Assert.Contains("--line current", refused.Problem.Message, StringComparison.Ordinal);
        var explicitSelection = ordinary with { Line = "current", Channel = "prerelease" };
        var allowed = surface == "async" ? await api.UpgradeAsync(explicitSelection, TestContext.Current.CancellationToken) : api.Upgrade(explicitSelection);
        Assert.Null(allowed.Problem);
        Assert.Equal("current", allowed.Plan!.Line);
        Assert.Equal("prerelease", allowed.Plan.Channel);
        Assert.Equal("release 2.0.0 [current/prerelease]", allowed.Plan.ToPin);
        Assert.False(allowed.Plan.Applied);
        Assert.Equal(before, File.ReadAllBytes(f.ManifestPath));
        Assert.Equal(2, handler.Calls);
    }

    [Fact]
    public void TheUpgradeContractAddsOptionalChoicesAndPreservesOlderRequestDefaults()
    {
        UpgradeRequest request = JsonSerializer.Deserialize<UpgradeRequest>("""{"version":"2.0.0","check":true}""", ProtocolJson.Options)!;
        Assert.Null(request.Line);
        Assert.Null(request.Channel);
        using JsonDocument document = JsonDocument.Parse(OpenApiDocument.Json);
        JsonElement schema = document.RootElement.GetProperty("components").GetProperty("schemas").GetProperty("UpgradeRequest");
        foreach (string choice in new[] { "line", "channel" })
        {
            Assert.Equal("string", schema.GetProperty("properties").GetProperty(choice).GetProperty("type").GetString());
            Assert.False(schema.TryGetProperty("required", out JsonElement required) && required.EnumerateArray().Any(p => p.GetString() == choice));
        }
    }

    private sealed class UpgradeHandler(EstateService service) : HttpMessageHandler
    {
        public int Calls { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal("/v1/estate/upgrade", request.RequestUri!.AbsolutePath);
            UpgradeRequest upgrade = (await request.Content!.ReadFromJsonAsync<UpgradeRequest>(ProtocolJson.Options, cancellationToken))!;
            var result = service.Upgrade(upgrade);
            return result.Plan is { } plan
                ? new(HttpStatusCode.OK) { Content = JsonContent.Create(plan, options: ProtocolJson.Options) }
                : new(HttpStatusCode.Conflict) { Content = JsonContent.Create(result.Problem!, options: ProtocolJson.Options) };
        }
    }
}
