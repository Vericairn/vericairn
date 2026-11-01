// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using System.Text.Json.Nodes;
using Vericairn.Records.Authoring;
using Vericairn.Records.Canonical;
using Vericairn.Records.Model;

namespace Vericairn.Records.Tests;

public class ProductCadenceTests
{
    public static PolicyFixture RecordedFixture()
    {
        var f = new PolicyFixture();
        RecordEnvelope policy = f.Policy();
        JsonObject body = ReleaseLineFixture.ProductBody();
        body["lines"] = new JsonArray(new JsonObject { ["name"] = "current", ["channel"] = "stable" },
            new JsonObject { ["name"] = "1.x", ["channel"] = "lts", ["endOfSupport"] = "2028-01-01T00:00:00Z" });
        body["cadence"] = new JsonObject { ["policy"] = PolicyFixture.Ref(policy), ["rule"] = "monthly" };
        RecordEnvelope product = f.Add("product", body);
        RecordEnvelope source = Publish(ReleaseLineFixture.Body("2.1.0"));
        Publish(ReleaseLineFixture.Body("1.9.1", "1.x", "lts", source));
        f.AssertValid();
        return f;

        RecordEnvelope Publish(JsonObject release)
        {
            RecordEnvelope candidate = f.Add("release", release, subject: product.Reference);
            f.Add("acceptance", new() { ["statement"] = "Accept the fixture release.", ["basis"] = new JsonArray(), ["self"] = true }, subject: candidate.Reference);
            release["status"] = "published";
            release["publishedAt"] = RecordParser.FormatTimestamp(f.Estate.Clock.AddSeconds(1));
            release["url"] = "https://example.invalid/releases/" + release["version"]!.GetValue<string>();
            return f.Add("release", release, subject: product.Reference, supersedes: candidate.Id);
        }
    }

    [Theory]
    [InlineData("valid", null)]
    [InlineData("hash", "policy.reference")]
    [InlineData("missing", "body.reference")]
    [InlineData("type", "policy.reference")]
    [InlineData("rule", "product.cadence")]
    [InlineData("not-duty", "product.cadence")]
    [InlineData("no-rule", "body.missing")]
    [InlineData("no-policy", "body.missing")]
    [InlineData("extra", "body.unknown-key")]
    public void CadenceNamesAnExactRecurringDuty(string scenario, string? code)
    {
        using var f = new PolicyFixture();
        RecordEnvelope policy = f.Policy(f.PolicyBody(duty: scenario != "not-duty"));
        JsonObject pin = PolicyFixture.Ref(policy);
        JsonObject cadence = new() { ["policy"] = pin, ["rule"] = scenario == "not-duty" ? "age" : "monthly" };
        switch (scenario)
        {
            case "hash": pin["hash"] = "sha256:" + new string('0', 64); break;
            case "missing": pin["id"] = "01M57TPYNPYJYZKTGCETXTHRT8"; break;
            case "type": pin["type"] = "product"; break;
            case "rule": cadence["rule"] = "unknown"; break;
            case "no-rule": cadence.Remove("rule"); break;
            case "no-policy": cadence.Remove("policy"); break;
            case "extra": cadence["automatic"] = true; break;
        }
        JsonObject body = ReleaseLineFixture.ProductBody();
        body["cadence"] = cadence;
        RecordEnvelope product = f.Add("product", body);
        var model = f.Estate.Load();
        if (code is null)
        {
            Assert.False(model.Findings.HasErrors, string.Join('\n', model.Findings));
            Assert.Equal(CanonicalJson.Serialize(PolicyFixture.Element(body)), CanonicalJson.Serialize(model.ById[product.Id].Body));
            Assert.Empty(model.OfType("release"));
            Assert.Empty(model.OfType("operation"));
        }
        else Assert.Contains(model.Findings, x => x.Code == code && x.Location.StartsWith(product.ExpectedPath, StringComparison.Ordinal));
    }

    [Fact]
    public void ProductFieldsHaveGoldenCanonicalBytesAndRoundTrip()
    {
        const string Golden = """{"cadence":{"policy":{"hash":"sha256:1111111111111111111111111111111111111111111111111111111111111111","id":"01M57TPYNPYJYZKTGCETXTHRT8","type":"estate-policy"},"rule":"monthly"},"lines":[{"channel":"stable","name":"current"},{"channel":"lts","endOfSupport":"2028-01-01T00:00:00Z","name":"1.x"}]}""";
        using JsonDocument document = JsonDocument.Parse(Golden);
        AuthoredRecord written = RecordAuthor.Create(new("product", "01M57TPYNPYJYZKTGCETXTHRT8", document.RootElement, Format: 3));
        var findings = new FindingList();
        RecordEnvelope? read = RecordParser.Parse(written.Bytes, written.RelativePath, findings);
        Assert.NotNull(read);
        Assert.False(findings.HasErrors);
        Assert.Equal(Golden, CanonicalJson.Serialize(read.Body));
        Assert.Equal(written.Envelope.ComputedHash, read.ComputedHash);
    }
}
