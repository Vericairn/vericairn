// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
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
        foreach (Type surface in new[] { typeof(IEstateApi), typeof(VericairnClient), typeof(CancellationBoundApi) })
        {
            Assert.Contains(surface.GetMethods(), m => m.ReturnType == typeof(PackagesResponse) && m.GetParameters() is [var parameter] && parameter.ParameterType == request);
            Assert.Contains(surface.GetMethods(), m => m.ReturnType == typeof(Task<PackagesResponse>) && m.GetParameters() is [var parameter, var cancellation] && parameter.ParameterType == request && cancellation.ParameterType == typeof(CancellationToken));
        }
    }
}
