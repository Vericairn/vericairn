// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json.Nodes;
using Vericairn.Protocol;
using Vericairn.Records.Estate;
using Vericairn.Records.Model;
using Vericairn.Records.Tests;
using Vericairn.Runtime.Service;

namespace Vericairn.Runtime.Tests;

/// <summary>Signed release planning records shared by runtime and command journeys, inside the suite's isolated home.</summary>
public sealed class ReleasePlanningFixture : IDisposable
{
    public PolicyFixture Records { get; } = new();
    public EstateService Api { get; }
    public RecordEnvelope Product { get; }
    public RecordEnvelope Source { get; }
    public RecordEnvelope Target { get; }
    public RecordEnvelope First { get; }
    public RecordEnvelope Second { get; }
    public RecordEnvelope Active { get; }
    public RecordEnvelope Candidate { get; }
    public string Operator => Records.Estate.OperatorId;
    public EstateModel Model => Api.Session.Load();
    public WorkMovePlanRequest Move => new(Operator, Source.ComputedHash,
        new Dictionary<string, string>(StringComparer.Ordinal) { [First.Id] = First.ComputedHash, [Second.Id] = Second.ComputedHash },
        Target.Id, Target.ComputedHash);
    public ReleaseWithdrawRequest Withdraw => new(Candidate.Id, Operator, Candidate.ComputedHash, "fixture-releases", "The candidate is being replaced.");

    public ReleasePlanningFixture()
    {
        Api = EstateService.Open(Records.Estate.Root);
        foreach (string principal in new[] { Operator, Records.Estate.BuilderId })
        {
            WriteOutcome created = Api.DeviceCreate(new(principal, "Planning fixture writer", "file"));
            Assert.True(created.Ok, created.Problem?.Message);
            RecordEnvelope device = Model.ById[created.Written!.Id];
            Records.Estate.SetClock(Model.Records.Max(r => r.AtTime));
            Records.Add("admission", new() { ["principal"] = principal, ["status"] = "admitted", ["note"] = "Fixture writer." }, subject: device.Reference);
        }
        Product = Records.Add("product", new()
        {
            ["name"] = "Widget", ["slug"] = "widget", ["summary"] = "A planning fixture.",
            ["editions"] = new JsonArray(new JsonObject { ["name"] = "Community", ["slug"] = "community", ["summary"] = "Fixture." }),
            ["surfaces"] = new JsonArray(new JsonObject { ["name"] = "CLI", ["slug"] = "cli", ["summary"] = "Fixture." }),
        });
        Source = Carrier("PLAN-1", "1.0.0");
        Target = Carrier("PLAN-2", "1.1.0");
        First = Member("WORK-1");
        Second = Member("WORK-2");
        Active = Member("WORK-3");
        Records.Add("work-status", new() { ["status"] = "active", ["note"] = "Already in progress." }, subject: Active.Reference);
        Candidate = Records.Add("release", new()
        {
            ["version"] = "1.0.0", ["repository"] = "Example/widget", ["commit"] = new string('a', 40), ["status"] = "candidate",
            ["artifacts"] = new JsonArray(new JsonObject { ["name"] = "widget.zip", ["hash"] = "sha256:" + new string('b', 64), ["bytes"] = 10, ["platform"] = "any" }),
            ["recordFormat"] = new JsonObject { ["reads"] = new JsonArray(1, 2, 3), ["writes"] = 3 },
            ["evidence"] = new JsonArray(), ["qualified"] = new JsonArray(), ["signing"] = "unsigned fixture", ["limitations"] = new JsonArray("Synthetic fixture."),
        }, subject: Product.Reference);
        Api.PolicyClock = new PlanningClock(this);
        Records.AssertValid();
    }

    public RecordEnvelope Member(string key)
    {
        JsonObject body = OrderBody(key);
        body["placement"] = new JsonObject { ["kind"] = "release", ["carrier"] = Source.Id, ["product"] = Product.Id, ["version"] = "1.0.0" };
        return Records.Add("work-order", body);
    }

    private RecordEnvelope Carrier(string key, string version)
    {
        JsonObject body = OrderBody(key);
        body["releasePlan"] = new JsonObject { ["product"] = Product.Id, ["version"] = version };
        return Records.Add("work-order", body);
    }

    private static JsonObject OrderBody(string key) => new()
    {
        ["key"] = key, ["kind"] = "ordinary", ["title"] = "Plan fixture work", ["intent"] = "Keep exact release placement.",
        ["scope"] = new JsonArray("fixture"), ["exitQuestion"] = "Is the placement recorded?", ["inputs"] = new JsonArray(), ["deliverables"] = new JsonArray("planned work"),
    };

    public void Dispose() { Api.Dispose(); Records.Dispose(); }

    private sealed class PlanningClock(ReleasePlanningFixture fixture) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => fixture.Model.Records.Max(r => r.AtTime).AddSeconds(1);
    }
}
