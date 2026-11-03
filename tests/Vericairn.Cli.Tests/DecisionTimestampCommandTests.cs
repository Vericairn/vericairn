// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using Vericairn.Protocol;
using Vericairn.Records.Model;
using Vericairn.Records.Tests;
using Vericairn.Runtime.Service;
using static Vericairn.Records.Tests.DecisionTimestampHistoryTests;

namespace Vericairn.Cli.Tests;

[Collection("cli")]
public sealed class DecisionTimestampCommandTests
{
    private static readonly string[] JsonFlag = ["--json"];

    public DecisionTimestampCommandTests() => CliHarness.Run("--help");

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RecordsNewRefusesLaterTimesAndAcceptsEqualTimes(bool json)
    {
        using var estate = new TestEstate(3);
        using EstateService service = EstateService.Open(estate.Root);
        string[] args = ["--estate", estate.Root, .. json ? JsonFlag : [], "records", "new", "decision",
            "--by", estate.OperatorId, "--at", RecordedAt, "--body", "-"];
        CliHarness.Outcome refused = CliHarness.RunWithApi(service, Body(estate.OperatorId, Later).GetRawText(), args);
        Assert.Equal(2, refused.Code);
        foreach (string fact in new[] { "decision.record-time", Later, RecordedAt })
            Assert.Contains(fact, refused.Out + refused.Err, StringComparison.Ordinal);
        Assert.Empty(service.Records("decision"));

        CliHarness.Outcome accepted = CliHarness.RunWithApi(service, Body(estate.OperatorId, RecordedAt).GetRawText(), args);
        Assert.Equal(0, accepted.Code);
        Assert.Single(service.Records("decision"));
    }

    [Theory]
    [InlineData("check", false)]
    [InlineData("check", true)]
    [InlineData("validate", false)]
    [InlineData("validate", true)]
    public void ExistingDecisionIsReadableAndChecksSucceedWithAWarning(string command, bool json)
    {
        using var estate = new TestEstate(3);
        estate.SetClock(RecordTime.AddSeconds(-1));
        RecordEnvelope decision = estate.Add("decision", estate.OperatorId, Body(estate.OperatorId, Later).GetRawText());
        using EstateService service = EstateService.Open(estate.Root);
        string[] family = command == "check" ? ["estate", "check"] : ["records", "validate"];
        CliHarness.Outcome check = CliHarness.RunWithApi(service, null,
            ["--estate", estate.Root, .. json ? JsonFlag : [], .. family]);
        Assert.Equal(0, check.Code);
        foreach (string fact in new[] { "decision.record-time", Later, RecordedAt })
            Assert.Contains(fact, check.Out, StringComparison.Ordinal);
        if (json)
        {
            FindingsResponse findings = check.Json.Deserialize<FindingsResponse>(ProtocolJson.Options)!;
            Assert.Equal(0, findings.Errors);
            Assert.Equal("warning", Assert.Single(findings.Findings, f => f.Code == "decision.record-time").Severity);
        }
        else Assert.Contains("WARNING", check.Out, StringComparison.OrdinalIgnoreCase);
        CliHarness.Outcome read = CliHarness.RunWithApi(service, null, "--estate", estate.Root, "decisions", decision.Id);
        Assert.Equal(0, read.Code);
        Assert.Contains(Later, read.Out, StringComparison.Ordinal);
    }
}
