// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json.Nodes;
using Vericairn.Records.Model;
using Vericairn.Records.Tests;

namespace Vericairn.TestSupport;

internal sealed class RemotePolicySeed
{
    internal RemotePolicySeed(PolicyFixture fixture)
    {
        Due = fixture.Policy(fixture.PolicyBody("due"));
        Missed = Duty(fixture, "missed");
        Excepted = Duty(fixture, "excepted");
        var late = fixture.PolicyBody("late");
        late["rules"]![0]!["parameters"]!["graceDays"] = 0;
        late["rules"]![0]!["parameters"]!["discharge"]!["records"] = new JsonArray(new JsonObject
        {
            ["key"] = "restore", ["kind"] = "verification", ["method"] = "restore", ["subject"] = "result",
        });
        Late = fixture.Policy(late);
        Unavailable = fixture.Policy(fixture.PolicyBody("unavailable", duty: false));
        var stale = fixture.PolicyBody("stale", duty: false);
        stale["level"] = "warn";
        stale["rules"] = new JsonArray(new JsonObject
        {
            ["key"] = "age", ["kind"] = "vericairn.estate/staleness", ["version"] = 1,
            ["parameters"] = new JsonObject { ["class"] = "planned-without-release", ["maxAgeDays"] = 0 },
        });
        Stale = fixture.Policy(stale);
        fixture.Estate.SetClock(new DateTimeOffset(2026, 10, 14, 12, 0, 0, TimeSpan.Zero));
        Exception = fixture.Add("policy-exception", fixture.ExceptionBody(Excepted), subject: Excepted.Reference);
        JsonObject completed = fixture.DutyLink(Late);
        completed["proofs"] = new JsonArray(new JsonObject { ["clause"] = "restore", ["kind"] = "result-verification" });
        Result = fixture.Result(completed);
        Proof = fixture.Add("verification", new JsonObject
        {
            ["method"] = "restore", ["commit"] = new string('1', 40), ["environment"] = new JsonObject { ["runner"] = "fixture" },
            ["role"] = "automation", ["outcome"] = "passed", ["summary"] = "The exact inventory proof.",
            ["evidence"] = new JsonArray(), ["limitations"] = new JsonArray(), ["evidenceClass"] = "observation",
        }, subject: Result.Reference);
        Review = fixture.Review(Result, new JsonArray(PolicyFixture.Ref(Proof)));
        Pending = fixture.Result(fixture.DutyLink(Due));
        Ask = fixture.Add("ask", new JsonObject
        {
            ["to"] = fixture.Estate.OperatorId, ["question"] = "Review this exact duty result?", ["recommendation"] = "Inspect its proofs on the desktop.",
            ["options"] = new JsonArray("Inspect its proofs on the desktop.", "Defer."), ["consequence"] = "The duty remains incomplete.", ["state"] = "open",
        }, subject: Pending.Reference);
        fixture.AssertValid();
    }

    internal RecordEnvelope Due { get; }
    internal RecordEnvelope Missed { get; }
    internal RecordEnvelope Excepted { get; }
    internal RecordEnvelope Late { get; }
    internal RecordEnvelope Unavailable { get; }
    internal RecordEnvelope Stale { get; }
    internal RecordEnvelope Exception { get; }
    internal RecordEnvelope Result { get; }
    internal RecordEnvelope Proof { get; }
    internal RecordEnvelope Review { get; }
    internal RecordEnvelope Pending { get; }
    internal RecordEnvelope Ask { get; }

    private static RecordEnvelope Duty(PolicyFixture fixture, string key)
    {
        JsonObject body = fixture.PolicyBody(key);
        body["rules"]![0]!["parameters"]!["graceDays"] = 0;
        return fixture.Policy(body);
    }
}
