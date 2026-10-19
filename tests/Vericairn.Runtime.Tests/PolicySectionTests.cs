// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using System.Text.Json.Nodes;
using Vericairn.Protocol;
using Vericairn.Records.Model;
using Vericairn.Records.Tests;
using Vericairn.Runtime.Service;

namespace Vericairn.Runtime.Tests;

public sealed class PolicySectionTests
{
    [Fact]
    public void CheckStatusAndProgressShareFindingsCountsAndOperatorDutiesWithoutRecords()
    {
        using var fixture = new PolicyFixture();
        fixture.Policy();
        fixture.Estate.SetClock(DateTimeOffset.Parse("2026-10-17T13:00:00Z", System.Globalization.CultureInfo.InvariantCulture));
        fixture.Add("writing-exception", PolicyFixture.WritingBody());
        Directory.CreateDirectory(Path.Combine(fixture.Estate.Root, "docs"));
        File.WriteAllText(Path.Combine(fixture.Estate.Root, "docs", "prose.md"), "Open311 and Open311, but October13.\n");
        fixture.AssertValid();
        using var api = EstateService.Open(fixture.Estate.Root);
        int before = api.Session.Load().Records.Count;
        PolicyResponse policies = api.Policies();
        FindingsResponse check = api.Check();
        StatusResponse status = api.Status();
        ProgressResponse progress = api.Progress();
        string expected = JsonSerializer.Serialize(policies, ProtocolJson.Options);
        Assert.Equal(expected, JsonSerializer.Serialize(check.Policies, ProtocolJson.Options));
        Assert.Equal(expected, status.Json.GetProperty("policies").GetRawText());
        Assert.Equal(expected, progress.Json.GetProperty("policies").GetRawText());
        Assert.Contains(check.Findings, f => f.Code == "policy.unmet" && f.Severity == "warning" && f.Message.Contains("deadline", StringComparison.Ordinal));
        Assert.Single(check.Findings, f => f.Code == "writing.spacing");
        Assert.Equal(2, check.Findings.Count(f => f.Code == "writing.excepted"));
        Assert.Equal(2, Assert.Single(check.WritingExceptions!.Entries).Matches);
        Assert.Contains("Policies\n", status.Text, StringComparison.Ordinal);
        Assert.Contains("Writing exceptions\n", progress.Text, StringComparison.Ordinal);
        JsonElement duty = Assert.Single(progress.Json.GetProperty("operator").GetProperty("duties").EnumerateArray());
        Assert.Equal(fixture.Estate.OperatorId, duty.GetProperty("owner").GetString());
        Assert.Empty(duty.GetProperty("asks").EnumerateArray());
        Assert.Equal(before, api.Session.Load().Records.Count);
    }

    [Fact]
    public void ExceptedDebtAndUnavailableKindsRemainVisibleBesideBuiltInErrors()
    {
        using var fixture = new PolicyFixture();
        RecordEnvelope policy = fixture.Policy();
        fixture.Policy(fixture.PolicyBody("unknown", duty: false));
        fixture.Estate.SetClock(DateTimeOffset.Parse("2026-10-17T13:00:00Z", System.Globalization.CultureInfo.InvariantCulture));
        fixture.Add("policy-exception", fixture.ExceptionBody(policy), subject: policy.Reference);
        Directory.CreateDirectory(Path.Combine(fixture.Estate.Root, "docs"));
        File.WriteAllText(Path.Combine(fixture.Estate.Root, "docs", "prose.md"), "A line.\r\n");
        using var api = EstateService.Open(fixture.Estate.Root);
        FindingsResponse check = api.Check();
        Assert.Contains(check.Findings, f => f.Code == "policy.excepted" && f.Severity == "info");
        Assert.Contains(check.Findings, f => f.Code == "policy.unavailable" && f.Severity == "warning");
        Assert.Contains(check.Findings, f => f.Code == "layout.line-endings");
        PolicyFindingDto debt = Assert.Single(check.Policies!.Findings, f => f.Disposition == "excepted");
        Assert.Equal("unmet", debt.Verdict);
        Assert.False(debt.Duty!.Completed);
    }

    [Fact]
    public void ExactOccurrenceAskGroupsWithBuilderDutyAndCompletionClearsTheDerivedWait()
    {
        using var fixture = new PolicyFixture();
        JsonObject definition = fixture.PolicyBody();
        definition["owner"] = fixture.Estate.BuilderId;
        RecordEnvelope policy = fixture.Policy(definition);
        fixture.Estate.SetClock(DateTimeOffset.Parse("2026-10-17T13:00:00Z", System.Globalization.CultureInfo.InvariantCulture));
        RecordEnvelope result = fixture.Result(fixture.DutyLink(policy));
        RecordEnvelope ask = fixture.Add("ask", new JsonObject
        {
            ["to"] = fixture.Estate.OperatorId, ["question"] = "Review this exact occurrence?", ["recommendation"] = "Inspect the retained evidence.",
            ["consequence"] = "The duty remains incomplete.", ["state"] = "open",
        }, builder: true, subject: result.Reference);
        fixture.AssertValid();
        using var api = EstateService.Open(fixture.Estate.Root);
        int before = api.Session.Load().Records.Count;
        JsonElement row = Assert.Single(api.Progress().Json.GetProperty("operator").GetProperty("duties").EnumerateArray());
        Assert.Equal(fixture.Estate.BuilderId, row.GetProperty("owner").GetString());
        Assert.Equal(ask.Id, Assert.Single(row.GetProperty("asks").EnumerateArray()).GetProperty("id").GetString());
        Assert.Equal(before, api.Session.Load().Records.Count);
        fixture.Review(result);
        Assert.Empty(api.Progress().Json.GetProperty("operator").GetProperty("duties").EnumerateArray());
        Assert.Single(api.Progress().Json.GetProperty("operator").GetProperty("asks").EnumerateArray());
    }
}
