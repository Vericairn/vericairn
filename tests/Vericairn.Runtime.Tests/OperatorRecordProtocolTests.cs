// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using Vericairn.Protocol;

namespace Vericairn.Runtime.Tests;

public sealed class OperatorRecordProtocolTests
{
    [Fact]
    public void TheAddedOperationAndItsSchemasDescribeTheSynchronousContract()
    {
        using JsonDocument document = JsonDocument.Parse(OpenApiDocument.Json);
        JsonElement operation = document.RootElement.GetProperty("paths").GetProperty("/v1/operator/record").GetProperty("post");
        Assert.Equal("#/components/schemas/OperatorRecordRequest", operation.GetProperty("requestBody").GetProperty("content").GetProperty("application/json").GetProperty("schema").GetProperty("$ref").GetString());
        Assert.Equal("#/components/schemas/OperatorRecordResponse", operation.GetProperty("responses").GetProperty("200").GetProperty("content").GetProperty("application/json").GetProperty("schema").GetProperty("$ref").GetString());
        foreach (Type type in new[] { typeof(OperatorRecordRequest), typeof(OperatorRecordResponse) })
        {
            string[] fields = document.RootElement.GetProperty("components").GetProperty("schemas").GetProperty(type.Name).GetProperty("properties").EnumerateObject().Select(p => p.Name).Order().ToArray();
            Assert.Equal(type.GetProperties().Select(p => JsonNamingPolicy.CamelCase.ConvertName(p.Name)).Order(), fields);
        }
        Assert.Contains(typeof(IEstateApi).GetMethods(), m => m.Name == "OperatorRecord" && m.ReturnType == typeof(OperatorRecordResponse));
        Assert.Equal(typeof(Task<OperatorRecordResponse>), typeof(IEstateApi).GetMethod("OperatorRecordAsync", [typeof(OperatorRecordRequest), typeof(CancellationToken)])?.ReturnType);
        JsonElement request = document.RootElement.GetProperty("components").GetProperty("schemas").GetProperty("OperatorRecordRequest");
        Assert.Equal("[\"type\",\"by\",\"body\"]", JsonSerializer.Serialize(request.GetProperty("required")));
        Assert.Equal(120, request.GetProperty("properties").GetProperty("waitSeconds").GetProperty("default").GetInt32());
    }
}
