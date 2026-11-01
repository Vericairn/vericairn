// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json.Nodes;
using Vericairn.Records.Model;

namespace Vericairn.Records.Tests;

public sealed class ResultProgressContextTests
{
    [Theory]
    [InlineData(3, false)]
    [InlineData(2, true)]
    public void ClaimContextIsAnOptionalFormatThreeFieldAndNeverSuppliesEvidence(long format, bool invalid)
    {
        using var fixture = new ProgressFixture(format);
        JsonObject body = Body();
        fixture.Estate.Add("result", fixture.Estate.BuilderId, body.ToJsonString(), fixture.Attempt.Reference);
        var findings = fixture.Estate.Load().Findings;
        Assert.Equal(invalid, findings.Any(f => f.Severity == Severity.Error));
        Assert.Empty(body["evidence"]!.AsArray());
        Assert.Empty(fixture.Estate.Load().OfType("acceptance"));
    }

    [Theory]
    [InlineData("progress", "{\"type\":\"best-effort\",\"percent\":101}")]
    [InlineData("progress", "{\"type\":\"accurate\",\"done\":1,\"unit\":\"files\",\"percent\":10}")]
    [InlineData("statusText", "\"two\\nlines\"")]
    [InlineData("at", "\"unknown\"")]
    [InlineData("evidence", "true")]
    public void RetainedContextUsesTheTypedClaimRulesAndRefusesUnknownMembers(string field, string value)
    {
        using var fixture = new ProgressFixture();
        JsonObject body = Body();
        body["progressClaim"]![field] = JsonNode.Parse(value);
        fixture.Estate.Add("result", fixture.Estate.BuilderId, body.ToJsonString(), fixture.Attempt.Reference);
        Assert.Contains(fixture.Estate.Load().Findings, f => f.Severity == Severity.Error);
    }

    private static JsonObject Body() => JsonNode.Parse("""
        {"outcome":"partial","summary":"Review continues.","outputs":[],"evidence":[],"checks":[],"limitations":[],"unresolvedEffects":[],"nextStep":"Inspect the work.",
         "progressClaim":{"phase":"review","note":"Reviewing.","at":"2026-10-13T12:00:00Z","progress":{"type":"best-effort","percent":100},"statusText":"The estimate is not completion evidence."}}
        """)!.AsObject();
}
