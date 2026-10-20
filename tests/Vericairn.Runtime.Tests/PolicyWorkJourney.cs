// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json.Nodes;
using Vericairn.Protocol;
using Vericairn.Records.Estate;
using Vericairn.Records.Model;
using Vericairn.Records.Tests;
using Vericairn.Runtime.Policies;
using Vericairn.Runtime.Service;

namespace Vericairn.Runtime.Tests;

/// <summary>Signed parallel work and policy boundaries, read through the runtime and CLI surfaces.</summary>
public static class PolicyWorkJourney
{
    public static void Run(Func<EstateService, PolicyResponse> read, int assignments)
    {
        using var fixture = new PolicyFixture();
        RecordEnvelope attempt = fixture.BeginAttempt();
        EstateModel model = fixture.Estate.Load();
        RecordEnvelope assignment = WorkView.AssignmentOfAttempt(model, attempt)!;
        RecordEnvelope order = WorkView.OrderOfAssignment(model, assignment)!;
        RecordEnvelope result = fixture.Add("result", PolicyFixture.ResultBody(), builder: true, subject: attempt.Reference);
        for (int i = 1; i < assignments; i++)
        {
            RecordEnvelope parallel = fixture.Add("assignment", PolicyFixture.Body(assignment), subject: order.Reference);
            RecordEnvelope next = fixture.Add("attempt", new()
            {
                ["startedAt"] = RecordParser.FormatTimestamp(fixture.Estate.Clock), ["workspace"] = "example repository", ["note"] = "Contribute parallel work.",
            }, builder: true, subject: parallel.Reference);
            result = fixture.Add("result", PolicyFixture.ResultBody(), builder: true, subject: next.Reference);
        }
        OpenStatus();
        RecordEnvelope reviewWait = Declare("review-wait", FoundationPolicyKinds.Staleness, new()
        {
            ["class"] = "result-awaiting-review", ["maxAgeDays"] = 0, ["reviewRequirement"] = "independent",
        });
        RecordEnvelope consistency = Declare("work-state-consistency", FoundationPolicyKinds.Consistency,
            new() { ["cases"] = new JsonArray("accepted-still-open") });
        using var service = EstateService.Open(fixture.Estate.Root);
        PolicyResponse waiting = Inspect();
        Assert.Equal(assignments, waiting.Findings.Count(f => f.PolicyKey == "review-wait" && f.Verdict == "unmet"));

        RecordEnvelope review = fixture.Review(result);
        fixture.Add("acceptance", new()
        {
            ["statement"] = "Accept the parallel work.", ["basis"] = new JsonArray(review.Id), ["self"] = false,
        }, subject: result.Reference);
        PolicyResponse accepted = Inspect();
        Assert.Equal(0, Assert.Single(accepted.Rules, r => r.Policy.Id == reviewWait.Id).Subjects);
        Assert.Single(accepted.Findings, f => f.PolicyKey == "work-state-consistency" && f.Verdict == "unmet");

        RecordEnvelope historical = Declare("historical-consistency", FoundationPolicyKinds.Consistency,
            new() { ["cases"] = new JsonArray("accepted-still-open") });
        fixture.Policy(PolicyFixture.Body(consistency), previous: consistency);
        PolicyResponse revised = Inspect();
        Assert.Single(revised.Findings, f => f.PolicyKey == "work-state-consistency" && f.Verdict == "unmet");
        Assert.Equal(0, Assert.Single(revised.Rules, r => r.Policy.Id == historical.Id).Subjects);

        OpenStatus();
        RecordEnvelope unsettled = fixture.Result();
        PolicyResponse reopened = Inspect();
        Assert.Single(reopened.Findings, f => f.PolicyKey == "historical-consistency" && f.Verdict == "unmet");
        PolicyFindingDto pending = Assert.Single(reopened.Findings, f => f.PolicyKey == "review-wait" && f.Verdict == "unmet");
        Assert.Equal(unsettled.Id, pending.Anchor!.Id);

        PolicyResponse Inspect()
        {
            fixture.AssertValid();
            int count = fixture.Estate.Load().Records.Count;
            PolicyResponse response = read(service);
            Assert.True(response.Complete);
            Assert.Equal(0, response.Holds);
            Assert.Equal(count, fixture.Estate.Load().Records.Count);
            return response;
        }

        RecordEnvelope Declare(string key, string kind, JsonObject parameters)
        {
            JsonObject body = fixture.PolicyBody(key);
            body["level"] = "warn";
            body["rules"] = new JsonArray(new JsonObject { ["key"] = key, ["kind"] = kind, ["version"] = 1, ["parameters"] = parameters });
            return fixture.Policy(body);
        }

        void OpenStatus() => fixture.Add("work-status", new() { ["status"] = "submitted", ["note"] = "An explicit work claim." }, subject: order.Reference);
    }
}
