// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using System.Text.Json.Nodes;
using Vericairn.Mcp;
using Vericairn.Protocol;
using Vericairn.Sdk;

namespace Vericairn.Runtime.Tests;

public sealed class PackagesProtocolTests
{
    [Theory]
    [InlineData("observe", typeof(PackagesObserveRequest))]
    [InlineData("unlist", typeof(PackagesUnlistRequest))]
    [InlineData("deprecate", typeof(PackagesDeprecateRequest))]
    public void PackageOperationsHaveExplicitRequestAndResponseSchemasAndAsyncSurfaces(string action, Type request)
    {
        Assert.Contains(("POST", "/v1/packages/" + action), OpenApiDocument.Routes);
        using JsonDocument document = JsonDocument.Parse(OpenApiDocument.Json);
        JsonElement operation = document.RootElement.GetProperty("paths").GetProperty("/v1/packages/" + action).GetProperty("post");
        Assert.Equal("#/components/schemas/" + request.Name, operation.GetProperty("requestBody").GetProperty("content").GetProperty("application/json").GetProperty("schema").GetProperty("$ref").GetString());
        Assert.Equal("#/components/schemas/PackagesResponse", operation.GetProperty("responses").GetProperty("200").GetProperty("content").GetProperty("application/json").GetProperty("schema").GetProperty("$ref").GetString());
        Assert.Equal(request.GetProperties().Select(p => JsonNamingPolicy.CamelCase.ConvertName(p.Name)).Order(),
            document.RootElement.GetProperty("components").GetProperty("schemas").GetProperty(request.Name).GetProperty("properties").EnumerateObject().Select(p => p.Name).Order());
        Assert.Equal("boolean", document.RootElement.GetProperty("components").GetProperty("schemas").GetProperty(request.Name).GetProperty("properties").GetProperty("reserved").GetProperty("type").GetString());
        foreach (Type surface in new[] { typeof(IEstateApi), typeof(VericairnClient), typeof(CancellationBoundApi) })
        {
            Assert.Contains(surface.GetMethods(), m => m.ReturnType == typeof(PackagesResponse) && m.GetParameters() is [var parameter] && parameter.ParameterType == request);
            Assert.Contains(surface.GetMethods(), m => m.ReturnType == typeof(Task<PackagesResponse>) && m.GetParameters() is [var parameter, var cancellation] && parameter.ParameterType == request && cancellation.ParameterType == typeof(CancellationToken));
        }
    }

    [Fact]
    public void ExistingConstructorsAndJsonKeepReleaseSelectionAndReservedIsAnOptionalProperty()
    {
        Assert.NotNull(typeof(PackagesObserveRequest).GetConstructor([typeof(string), typeof(string), typeof(string), typeof(string)]));
        Assert.NotNull(typeof(PackagesUnlistRequest).GetConstructor([typeof(string), typeof(string), typeof(string), typeof(string)]));
        Assert.NotNull(typeof(PackagesDeprecateRequest).GetConstructor([typeof(string), typeof(string), typeof(string), typeof(string), typeof(IReadOnlyList<string>), typeof(string), typeof(string), typeof(string)]));
        const string json = """{"by":"operator","product":"product","version":"0.0.1-reserved","package":"Example.Widget","reasons":["Legacy"]}""";
        Assert.False(JsonSerializer.Deserialize<PackagesObserveRequest>(json, ProtocolJson.Options)!.Reserved);
        Assert.False(JsonSerializer.Deserialize<PackagesUnlistRequest>(json, ProtocolJson.Options)!.Reserved);
        Assert.False(JsonSerializer.Deserialize<PackagesDeprecateRequest>(json, ProtocolJson.Options)!.Reserved);
        JsonNode body = JsonNode.Parse(json)!;
        body["reserved"] = true;
        Assert.True(body.Deserialize<PackagesObserveRequest>(ProtocolJson.Options)!.Reserved);
        Assert.True(body.Deserialize<PackagesUnlistRequest>(ProtocolJson.Options)!.Reserved);
        Assert.True(body.Deserialize<PackagesDeprecateRequest>(ProtocolJson.Options)!.Reserved);
    }

    [Fact]
    public async Task PackageWritesAreNeverExposedToMcpWorkers()
    {
        using var fixture = new PackageTestEstate(reserved: true);
        var registry = new ToolRegistry(fixture.Service, fixture.Estate.BuilderId);
        int before = fixture.Service.Session.Load().Records.Count;
        // The current registry exposes no package tools. Any future registration must retain operator-only writes.
        Assert.All(registry.All.Where(tool => tool.Name.StartsWith("packages_", StringComparison.Ordinal)), tool =>
        {
            Assert.True(tool.Writes);
            Assert.True(tool.OperatorOnly);
        });
        foreach (string action in new[] { "observe", "unlist", "deprecate" })
        {
            ToolCallResult response = await registry.CallAsync("packages_" + action, new JsonObject
            {
                ["product"] = fixture.Product.Id, ["version"] = fixture.Version, ["reserved"] = true,
                ["package"] = "Example.Widget", ["reasons"] = new JsonArray("Legacy"),
            }, TestContext.Current.CancellationToken);
            Assert.True(response.Unknown);
        }
        Assert.Equal(before, fixture.Service.Session.Load().Records.Count);
        Assert.Empty(fixture.Handler.Requests);
    }
}
