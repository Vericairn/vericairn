// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Reflection;
using System.Text.Json;
using Vericairn.Protocol;

namespace Vericairn.Runtime.Tests;

public sealed class ReleaseReviewProtocolTests
{
    private static readonly string[] Parameters = ["version", "product", "decision"];
    [Fact]
    public void BothRoutesDescribeTheCompleteResultAndAllInputs()
    {
        const string route = "/v1/releases/{version}/review-brief";
        using JsonDocument document = JsonDocument.Parse(OpenApiDocument.Json);
        foreach (string method in new[] { "GET", "POST" })
        {
            Assert.Contains((method, route), OpenApiDocument.Routes);
            JsonElement operation = document.RootElement.GetProperty("paths").GetProperty(route).GetProperty(method.ToLowerInvariant());
            Assert.Equal("#/components/schemas/ReleaseReviewBriefResponse", operation.GetProperty("responses").GetProperty("200").GetProperty("content").GetProperty("application/json").GetProperty("schema").GetProperty("$ref").GetString());
            JsonElement version = Assert.Single(operation.GetProperty("parameters").EnumerateArray(), p => p.GetProperty("name").GetString() == "version");
            Assert.Equal("path", version.GetProperty("in").GetString());
            Assert.True(version.GetProperty("required").GetBoolean());
            if (method == "GET") Assert.Equal(Parameters, operation.GetProperty("parameters").EnumerateArray().Select(p => p.GetProperty("name").GetString()));
            else Assert.Equal("#/components/schemas/ReleaseReviewAssignRequest", operation.GetProperty("requestBody").GetProperty("content").GetProperty("application/json").GetProperty("schema").GetProperty("$ref").GetString());
        }
        foreach (Type type in new[] { typeof(ReleaseReviewAssignRequest), typeof(ReleaseReviewBriefResponse) })
        {
            JsonElement properties = document.RootElement.GetProperty("components").GetProperty("schemas").GetProperty(type.Name).GetProperty("properties");
            Assert.Equal(type.GetProperties().Select(p => JsonNamingPolicy.CamelCase.ConvertName(p.Name)).Order(StringComparer.Ordinal), properties.EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal));
        }
    }

    [Fact]
    public void AllAddedInterfaceMembersHaveDefaultImplementations()
    {
        MethodInfo[] methods = typeof(IEstateApi).GetMethods().Where(m => m.Name.StartsWith("ReleaseReview", StringComparison.Ordinal)).ToArray();
        Assert.Equal(4, methods.Length);
        Assert.All(methods, method => Assert.False(method.IsAbstract));
        Assert.Equal(TimeSpan.FromMinutes(15), ClientOperation.Deadline(nameof(IEstateApi.ReleaseReviewAssign)));
    }
}
