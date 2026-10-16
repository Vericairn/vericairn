// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json.Nodes;
using Vericairn.Records.Model;

namespace Vericairn.Records.Tests;

public class WatchLimitsTests
{
    [Theory]
    [InlineData("goal")]
    [InlineData("roster")]
    public void GoalsAndRostersRecordPartialWatchingLimits(string type)
    {
        using var estate = new TestEstate(recordFormat: 2);
        estate.Add(type, estate.OperatorId, Body(estate, type, """{"stallSeconds":60,"watch":{"normalTokensPerMinute":50000,"burnMultiplier":3,"burnMinimumTokens":100,"burnWindowSeconds":20,"burnCapPercent":10,"loopWarning":3,"loopStop":4,"loopWindow":6}}"""));
        Assert.DoesNotContain(estate.Load().Findings, f => f.Severity == Severity.Error);
    }

    [Theory]
    [InlineData("normalTokensPerMinute", 0)]
    [InlineData("normalTokensPerMinute", 1000000001)]
    [InlineData("burnMultiplier", 1)]
    [InlineData("burnMultiplier", 101)]
    [InlineData("burnMinimumTokens", 0)]
    [InlineData("burnWindowSeconds", 0)]
    [InlineData("burnWindowSeconds", 86401)]
    [InlineData("burnCapPercent", 0)]
    [InlineData("burnCapPercent", 101)]
    [InlineData("loopWarning", 1)]
    [InlineData("loopStop", 2)]
    [InlineData("loopWindow", 2)]
    public void UnboundedOrDisabledWatchingLimitsAreRefused(string name, long value)
    {
        foreach (string type in new[] { "goal", "roster" })
        {
            using var estate = new TestEstate(recordFormat: 2);
            RecordEnvelope record = estate.Add(type, estate.OperatorId, Body(estate, type, $$$"""{"watch":{"{{{name}}}":{{{value}}}}}"""));
            Assert.Contains(estate.Load().Findings, f => f.Code == "body.range" && f.Location == record.ExpectedPath + "#body.limits.watch." + name);
        }
    }

    [Theory]
    [InlineData("{\"loopWarning\":4,\"loopStop\":4}")]
    [InlineData("{\"loopStop\":7,\"loopWindow\":6}")]
    [InlineData("{\"loopWarning\":7,\"loopWindow\":6}")]
    public void SpecifiedLoopLimitsMustBeOrdered(string watch)
    {
        foreach (string type in new[] { "goal", "roster" })
        {
            using var estate = new TestEstate(recordFormat: 2);
            estate.Add(type, estate.OperatorId, Body(estate, type, $$"""{"watch":{{watch}}}"""));
            Assert.Contains(estate.Load().Findings, f => f.Code == "watch.limits");
        }
    }

    private static string Body(TestEstate estate, string type, string limits)
    {
        var body = JsonNode.Parse(type == "goal" ? GoalEvaluationTests.Goal(estate)
            : $$"""{"roles":[{"role":"builder","principals":["{{estate.BuilderId}}"]}]}""")!.AsObject();
        body["limits"] = JsonNode.Parse(limits);
        return body.ToJsonString();
    }
}
