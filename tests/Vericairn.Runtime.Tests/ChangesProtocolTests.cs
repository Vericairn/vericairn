// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using Vericairn.Protocol;

namespace Vericairn.Runtime.Tests;

public sealed class ChangesProtocolTests
{
    [Theory]
    [InlineData("push", typeof(ChangesPushRequest))]
    [InlineData("open", typeof(ChangesOpenRequest))]
    [InlineData("merge", typeof(ChangesMergeRequest))]
    [InlineData("observe", typeof(ChangesObserveRequest))]
    public void EachOperationHasAnApiMethodAndAnExplicitSchema(string action, Type request)
    {
        Assert.Contains(("POST", "/v1/changes/" + action), OpenApiDocument.Routes);
        Assert.Contains(typeof(IEstateApi).GetMethods(), method => method.ReturnType == typeof(ChangeResponse)
            && method.GetParameters() is [var parameter] && parameter.ParameterType == request);
        using JsonDocument document = JsonDocument.Parse(OpenApiDocument.Json);
        JsonElement operation = document.RootElement.GetProperty("paths").GetProperty("/v1/changes/" + action).GetProperty("post");
        Assert.Equal("#/components/schemas/" + request.Name, operation.GetProperty("requestBody").GetProperty("content").GetProperty("application/json").GetProperty("schema").GetProperty("$ref").GetString());
        Assert.Equal("#/components/schemas/ChangeResponse", operation.GetProperty("responses").GetProperty("200").GetProperty("content").GetProperty("application/json").GetProperty("schema").GetProperty("$ref").GetString());
        string[] fields = document.RootElement.GetProperty("components").GetProperty("schemas").GetProperty(request.Name).GetProperty("properties").EnumerateObject().Select(p => p.Name).Order().ToArray();
        Assert.Equal(request.GetProperties().Select(p => JsonNamingPolicy.CamelCase.ConvertName(p.Name)).Order(), fields);
    }

    [Fact]
    public void ResponsePreservesAnUncertainOutcomeAndItsRecordedIdentity()
    {
        var response = new ChangeResponse("example-change", "uncertain", "Observe before retrying.", false,
            new("records/operation/example.json", "example-operation", "sha256:" + new string('a', 64)), CheckStatus: "uncertain");
        string json = JsonSerializer.Serialize(response, ProtocolJson.Options);
        Assert.Equal(response, JsonSerializer.Deserialize<ChangeResponse>(json, ProtocolJson.Options));
        Assert.Contains("\"status\":\"uncertain\"", json, StringComparison.Ordinal);
    }
}
