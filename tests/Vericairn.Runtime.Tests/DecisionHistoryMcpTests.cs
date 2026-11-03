// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using System.Text.Json.Nodes;
using Vericairn.Mcp;
using Vericairn.Protocol;
using Vericairn.Records.Model;
using Vericairn.Records.Tests;
using Vericairn.Runtime.Service;
using static Vericairn.Records.Tests.DecisionTimestampHistoryTests;

namespace Vericairn.Runtime.Tests;

public sealed class DecisionHistoryMcpTests
{
    [Fact]
    public async Task ExistingDecisionRemainsReadableAndEstateCheckReportsAWarning()
    {
        using var estate = new TestEstate(3);
        estate.SetClock(RecordTime.AddSeconds(-1));
        RecordEnvelope decision = estate.Add("decision", estate.OperatorId, Body(estate.OperatorId, Later).GetRawText());
        using EstateService service = EstateService.Open(estate.Root);
        var registry = new ToolRegistry(service, estate.BuilderId);
        ToolCallResult read = await registry.CallAsync("decision_get", new JsonObject { ["id"] = decision.Id }, TestContext.Current.CancellationToken);
        Assert.False(read.IsError);
        Assert.Contains(Later, read.Text, StringComparison.Ordinal);
        ToolCallResult check = await registry.CallAsync("estate_check", null, TestContext.Current.CancellationToken);
        Assert.False(check.IsError);
        FindingsResponse findings = JsonSerializer.Deserialize<FindingsResponse>(check.Text, ProtocolJson.Options)!;
        Assert.Equal(0, findings.Errors);
        FindingDto warning = Assert.Single(findings.Findings, f => f.Code == "decision.record-time");
        Assert.Equal("warning", warning.Severity);
        Assert.Contains(RecordedAt, warning.Message, StringComparison.Ordinal);
        Assert.Contains(Later, warning.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("decision_new")]
    [InlineData("records_new")]
    public async Task NoMcpDecisionAuthoringToolCanBypassTheWriteBoundary(string name)
    {
        using var estate = new TestEstate(3);
        using EstateService service = EstateService.Open(estate.Root);
        var registry = new ToolRegistry(service, estate.OperatorId);
        Assert.DoesNotContain(registry.All, tool => tool.Name == name);
        int before = service.Session.Load().Records.Count;
        ToolCallResult result = await registry.CallAsync(name, new JsonObject
        {
            ["type"] = "decision", ["at"] = RecordedAt,
            ["body"] = JsonNode.Parse(Body(estate.OperatorId, Later).GetRawText()),
        }, TestContext.Current.CancellationToken);
        Assert.True(result.Unknown);
        Assert.True(result.IsError);
        Assert.Equal(before, service.Session.Load().Records.Count);
    }
}
