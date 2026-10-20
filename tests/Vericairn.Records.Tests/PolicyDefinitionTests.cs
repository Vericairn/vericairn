// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using System.Text.Json.Nodes;
using Vericairn.Records.Canonical;
using Vericairn.Records.Types;

namespace Vericairn.Records.Tests;

public sealed class PolicyDefinitionTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void HashMatchesApprovalAndExcludesOnlyTopLevelBasis(bool duty)
    {
        using var fixture = new PolicyFixture();
        JsonObject body = fixture.PolicyBody(duty: duty);
        string hash = PolicyDefinition.Hash(PolicyFixture.Element(body));
        var approval = fixture.Approve(body);
        Assert.Equal(approval.Body.GetProperty("policies")[0].GetProperty("definitionHash").GetString(), hash);
        body["basis"] = new JsonArray(new JsonObject { ["decision"] = PolicyFixture.Ref(approval), ["requirement"] = "definition" });
        Assert.Equal(hash, PolicyDefinition.Hash(PolicyFixture.Element(body)));
        fixture.Add("estate-policy", body);
        fixture.AssertValid();

        body["basis"] = new JsonArray();
        Assert.Equal(hash, PolicyDefinition.Hash(PolicyFixture.Element(body)));
        body["title"] = "A changed definition";
        Assert.NotEqual(hash, PolicyDefinition.Hash(PolicyFixture.Element(body)));
        string edited = PolicyDefinition.Hash(PolicyFixture.Element(body));
        body["rules"]![0]!["parameters"]!["basis"] = "Nested basis is part of the definition";
        Assert.NotEqual(edited, PolicyDefinition.Hash(PolicyFixture.Element(body)));
    }

    [Fact]
    public void MemberOrderWhitespaceAndEscapingDoNotChangeTheHash()
    {
        using var fixture = new PolicyFixture();
        JsonObject body = fixture.PolicyBody();
        string hash = PolicyDefinition.Hash(PolicyFixture.Element(body));
        var reordered = new JsonObject(body.Reverse().Select(p => KeyValuePair.Create(p.Key, p.Value?.DeepClone())));
        string json = reordered.ToJsonString(new JsonSerializerOptions { WriteIndented = true }).Replace("Test", "\\u0054est", StringComparison.Ordinal);
        using JsonDocument parsed = JsonDocument.Parse(json);
        Assert.Equal(hash, PolicyDefinition.Hash(parsed.RootElement));
    }

    [Theory]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("42")]
    [InlineData("\"policy\"")]
    [InlineData("{}")]
    [InlineData("{\"key\":\"example\",\"rules\":[]}")]
    [InlineData("{\"type\":\"estate-policy\",\"body\":{}}")]
    [InlineData("{\"token\":\"Open311\",\"state\":\"active\",\"basis\":{\"kind\":\"operator\"}}")]
    public void OtherJsonValuesAreNotPolicyBodies(string json)
    {
        using JsonDocument parsed = JsonDocument.Parse(json);
        Assert.Contains("prepared policy or duty body", Assert.Throws<CanonicalJsonException>(() => PolicyDefinition.Hash(parsed.RootElement)).Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("rules", "[]")]
    [InlineData("rules", "[null]")]
    [InlineData("appliesTo", "null")]
    [InlineData("maxExceptionDays", "0")]
    [InlineData("basis", "{}")]
    [InlineData("title", "\"\"")]
    public void MalformedPolicyShapesAreRefused(string field, string value)
    {
        using var fixture = new PolicyFixture();
        JsonObject body = fixture.PolicyBody();
        body[field] = JsonNode.Parse(value);
        Assert.Throws<CanonicalJsonException>(() => PolicyDefinition.Hash(PolicyFixture.Element(body)));
    }

    [Theory]
    [InlineData("\"duplicate\":1,\"duplicate\":2", "Duplicate object key")]
    [InlineData("\"number\":1.5", "plain integer")]
    [InlineData("\"number\":9007199254740992", "safe range")]
    public void NonCanonicalValuesCannotProduceAnApprovalHash(string member, string message)
    {
        using var fixture = new PolicyFixture();
        string body = fixture.PolicyBody().ToJsonString();
        using JsonDocument parsed = JsonDocument.Parse(body[..^1] + "," + member + "}");
        Assert.Contains(message, Assert.Throws<CanonicalJsonException>(() => PolicyDefinition.Hash(parsed.RootElement)).Message, StringComparison.Ordinal);
    }
}
