// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using System.Text.Json.Nodes;
using Vericairn.Mcp;
using Vericairn.Protocol;
using Vericairn.Records.Authoring;
using Vericairn.Records.Estate;
using Vericairn.Records.Model;
using Vericairn.Records.Tests;
using Vericairn.Runtime.Service;
using static Vericairn.Records.Tests.DecisionTimestampHistoryTests;

namespace Vericairn.Runtime.Tests;

public sealed class DecisionHistoryMcpTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PromptAnswersReturnTheSharedRefusalWithBothTimes(bool standardPrompt)
    {
        using var writer = new PromptWriter();
        PromptRow row = writer.Add();
        RecordEnvelope ask = writer.Fixture.Add("ask", new JsonObject
        {
            ["to"] = writer.Operator, ["question"] = "Which evidence?", ["recommendation"] = "Use the assigned evidence.",
            ["consequence"] = "Work waits.", ["state"] = "open",
        }, builder: true);
        var registry = new ToolRegistry(writer.Api, writer.Operator);
        var arguments = new JsonObject
        {
            ["body"] = new JsonObject
            {
                ["ask"] = ask.Id,
                ["selection"] = JsonSerializer.SerializeToNode(new PromptSelection(row.Identity, row.Hash,
                    new Dictionary<string, string> { ["topic"] = "the assigned evidence" }), ProtocolJson.Options),
            },
        };
        int before = writer.Model.Records.Count;
        string? at = null, decidedAt = null;
        writer.Api.BeforeValidatingRecords = records =>
        {
            // Prompt answers supply their own times. Inject the ordering a clock rollback could produce at the real
            // admission boundary, retaining valid signatures so chronology is the only reason for refusal.
            AuthoredRecord proposed = Assert.Single(records);
            RecordEnvelope candidate = proposed.Envelope;
            Assert.Equal("decision", candidate.Type);
            at = candidate.At;
            decidedAt = RecordParser.FormatTimestamp(candidate.AtTime.AddSeconds(1));
            JsonObject body = PolicyFixture.Body(candidate);
            body["decidedAt"] = decidedAt;
            AuthoredRecord later = RecordAuthor.Create(new RecordDraft(candidate.Type, candidate.By,
                PolicyFixture.Element(body), candidate.Subject, candidate.Supersedes, candidate.AtTime, candidate.Id, candidate.Format), writer.Fixture.OperatorSigner);
            Assert.Equal(proposed.Bytes.Length, later.Bytes.Length);
            later.Bytes.CopyTo(proposed.Bytes, 0);
        };

        string diagnostic;
        if (standardPrompt)
        {
            InvalidOperationException error = await Assert.ThrowsAsync<InvalidOperationException>(() => registry.PromptGetAsync(row.Identity, new JsonObject
            {
                ["topic"] = "the assigned evidence", ["_expected"] = row.Hash,
                ["_target"] = new JsonObject { ["ask"] = ask.Id }.ToJsonString(),
            }, TestContext.Current.CancellationToken));
            diagnostic = error.Message;
        }
        else
        {
            ToolCallResult refused = await registry.CallAsync("prompt_use", arguments, TestContext.Current.CancellationToken);
            Assert.False(refused.Unknown);
            Assert.True(refused.IsError);
            PromptUseResponse result = JsonSerializer.Deserialize<PromptUseResponse>(refused.Text, ProtocolJson.Options)!;
            Assert.False(result.Ok);
            Assert.Null(result.Written);
            Assert.Null(result.Text);
            FindingDto finding = Assert.Single(result.Problem!.Findings);
            Assert.Equal("error", finding.Severity);
            Assert.Equal("decision.record-time", finding.Code);
            diagnostic = refused.Text;
        }
        Assert.NotNull(at);
        Assert.NotNull(decidedAt);
        Assert.Contains("decision.record-time", diagnostic, StringComparison.Ordinal);
        Assert.Contains(at, diagnostic, StringComparison.Ordinal);
        Assert.Contains(decidedAt, diagnostic, StringComparison.Ordinal);
        Assert.Equal(before, writer.Model.Records.Count);
        Assert.Equal("open", Asks.State(writer.Model, ask).State);

        writer.Api.BeforeValidatingRecords = null;
        ToolCallResult accepted = await registry.CallAsync("prompt_use", arguments, TestContext.Current.CancellationToken);
        Assert.False(accepted.IsError, accepted.Text);
        PromptUseResponse written = JsonSerializer.Deserialize<PromptUseResponse>(accepted.Text, ProtocolJson.Options)!;
        RecordEnvelope answer = writer.Model.ById[written.Written!.Id];
        Assert.True(string.CompareOrdinal(EstateModel.BodyString(answer, "decidedAt"), answer.At) <= 0);
        Assert.Equal("answered", Asks.State(writer.Model, ask).State);
    }

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
