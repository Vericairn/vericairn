// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using System.Text.Json.Nodes;
using Vericairn.Mcp;
using Vericairn.Protocol;
using Vericairn.Records.Estate;
using Vericairn.Records.Model;
using Vericairn.Records.Tests;
using Vericairn.Runtime.Policies;
using Vericairn.Runtime.Service;

namespace Vericairn.Runtime.Tests;

public sealed class PolicyWriteTests
{
    [Fact]
    public void CreationAndRemovalCheckExpectedHashesAndSignTheActualAuthor()
    {
        using var fixture = new Writer();
        JsonElement body = PolicyFixture.Element(PolicyFixture.WritingBody());
        int before = fixture.Model.Records.Count;
        Assert.Equal("conflict", fixture.Api.WritingExceptionAdd(new(fixture.Operator, body, "stale")).Problem!.Code);
        Assert.Equal(before, fixture.Model.Records.Count);
        RecordWritten added = Ok(fixture.Api.WritingExceptionAdd(new(fixture.Operator, body, PolicyBasis.Hash(body))));
        RecordEnvelope record = fixture.Model.ById[added.Id];
        Assert.Equal(fixture.Operator, record.By);
        Assert.True(SignatureChecks.IsAuthentic(fixture.Model, record));
        Assert.Equal("conflict", fixture.Api.WritingExceptionRemove(added.Id, new(fixture.Operator, "stale", "Remove the entry.")).Problem!.Code);
        RecordWritten removed = Ok(fixture.Api.WritingExceptionRemove(added.Id, new(fixture.Operator, added.Hash, "Remove the entry.")));
        Assert.True(SignatureChecks.IsAuthentic(fixture.Model, fixture.Model.ById[removed.Id]));
        Assert.Equal("conflict", fixture.Api.WritingExceptionRemove(added.Id, new(fixture.Operator, added.Hash, "An obsolete preparation.")).Problem!.Code);
        Assert.Equal(before + 2, fixture.Model.Records.Count);
    }

    [Fact]
    public void ExactApprovalAndKindValidationApplyToTypedAndGenericAuthoring()
    {
        using var fixture = new Writer();
        JsonObject definition = fixture.Fixture.PolicyBody();
        definition["startsAt"] = RecordParser.FormatTimestamp(fixture.Clock.Now.AddMinutes(1));
        definition["rules"]![0]!["parameters"]!["schedule"]!["firstDueAt"] = definition["startsAt"]!.DeepClone();
        JsonElement body = fixture.Approve(definition);
        RecordWritten policy = Ok(fixture.Api.PolicyDeclare(new(fixture.Operator, body, PolicyBasis.Hash(body))));
        Assert.Equal(fixture.Operator, fixture.Model.ById[policy.Id].By);
        var revised = PolicyFixture.Body(fixture.Model.ById[policy.Id]);
        revised["title"] = "Revised maintenance title";
        JsonElement revision = PolicyFixture.Element(revised);
        Assert.Equal("conflict", fixture.Api.PolicyRevise("maintenance", new(fixture.Operator, revision, "stale")).Problem!.Code);
        Ok(fixture.Api.PolicyRevise("maintenance", new(fixture.Operator, revision, policy.Hash)));
        Assert.Equal("conflict", fixture.Api.NewRecord(new("estate-policy", fixture.Operator, revision, Supersedes: policy.Id)).Problem!.Code);

        JsonObject invalid = fixture.Fixture.PolicyBody("invalid-parameters", duty: false);
        invalid["startsAt"] = RecordParser.FormatTimestamp(fixture.Clock.Now.AddMinutes(1));
        invalid["rules"] = new JsonArray(new JsonObject { ["key"] = "placement", ["kind"] = "vericairn.estate/placement", ["version"] = 1, ["parameters"] = new JsonObject { ["invented"] = true } });
        body = fixture.Approve(invalid);
        int before = fixture.Model.Records.Count;
        WriteOutcome refused = fixture.Api.PolicyDeclare(new(fixture.Operator, body, PolicyBasis.Hash(body)));
        Assert.False(refused.Ok);
        Assert.Contains(refused.Problem!.Findings, f => f.Code == "policy.parameters");
        Assert.Equal(before, fixture.Model.Records.Count);
        WriteOutcome generic = fixture.Api.NewRecord(new("estate-policy", fixture.Operator, body, Expected: PolicyBasis.Hash(body)));
        Assert.False(generic.Ok);
        Assert.Contains(generic.Problem!.Findings, f => f.Code == "policy.parameters");
        Assert.Equal(before, fixture.Model.Records.Count);
    }

    [Fact]
    public void ExceptionSourcesAreReevaluatedAndRevocationKeepsItsOriginalTarget()
    {
        using var fixture = new Writer();
        JsonObject definition = fixture.Fixture.PolicyBody();
        definition["startsAt"] = RecordParser.FormatTimestamp(fixture.Clock.Now.AddSeconds(-30));
        definition["rules"]![0]!["parameters"]!["schedule"]!["firstDueAt"] = definition["startsAt"]!.DeepClone();
        definition["rules"]![0]!["parameters"]!["graceDays"] = 0;
        RecordEnvelope policy = fixture.Fixture.Policy(definition);
        JsonObject grant = fixture.Fixture.ExceptionBody(policy);
        grant["expiresAt"] = RecordParser.FormatTimestamp(fixture.Clock.Now.AddDays(1));
        int before = fixture.Model.Records.Count;
        RecordWritten excepted = Ok(fixture.Api.PolicyExcept("maintenance", new(fixture.Operator, PolicyFixture.Element(grant), policy.ComputedHash)));
        PolicyResponse evaluation = fixture.Api.PolicyEvaluate(RecordParser.FormatTimestamp(fixture.Clock.Now));
        Assert.Contains(evaluation.Findings, f => f.Disposition == "excepted");
        var revoke = PolicyFixture.Body(fixture.Model.ById[excepted.Id]);
        revoke["state"] = "revoked";
        revoke["reason"] = "End the bounded delay.";
        Ok(fixture.Api.PolicyRevokeException("maintenance", excepted.Id, new(fixture.Operator, PolicyFixture.Element(revoke), excepted.Hash)));
        Assert.DoesNotContain(fixture.Api.PolicyEvaluate(RecordParser.FormatTimestamp(fixture.Clock.Now)).Findings, f => f.Disposition == "excepted");
        Assert.Equal(before + 2, fixture.Model.Records.Count);

        JsonObject future = fixture.Fixture.ExceptionBody(policy);
        future["targets"]![0]!["occurrence"]!["number"] = 100;
        future["expiresAt"] = RecordParser.FormatTimestamp(fixture.Clock.Now.AddDays(1));
        Assert.False(fixture.Api.PolicyExcept("maintenance", new(fixture.Operator, PolicyFixture.Element(future), policy.ComputedHash)).Ok);
        Assert.Equal(before + 2, fixture.Model.Records.Count);
    }

    [Fact]
    public async Task McpUsesDistinctCurrentGrantsAndTheServerStillChecksExactTokenBounds()
    {
        using var fixture = new Writer(builder: true);
        var registry = new ToolRegistry(fixture.Api, fixture.Fixture.Estate.BuilderId);
        Assert.DoesNotContain(await registry.VisibleAsync(TestContext.Current.CancellationToken), t => t.Name == "writing_exception_add");
        RecordEnvelope authority = fixture.Fixture.Grant("manage-writing-exceptions", PolicyFixture.WritingBounds(), RecordParser.FormatTimestamp(fixture.Clock.Now.AddDays(1)));
        var names = (await registry.VisibleAsync(TestContext.Current.CancellationToken)).Select(t => t.Name).ToArray();
        Assert.Contains("writing_exception_add", names);
        Assert.DoesNotContain("policy_declare", names);
        JsonObject body = PolicyFixture.WritingBody(authority);
        body["token"] = "Other311";
        int before = fixture.Model.Records.Count;
        ToolCallResult denied = await registry.CallAsync("writing_exception_add", new JsonObject { ["body"] = body, ["expected"] = PolicyBasis.Hash(PolicyFixture.Element(body)) }, TestContext.Current.CancellationToken);
        Assert.True(denied.IsError);
        Assert.Equal(before, fixture.Model.Records.Count);
        fixture.Clock.Now = fixture.Clock.Now.AddDays(2);
        Assert.DoesNotContain(await registry.VisibleAsync(TestContext.Current.CancellationToken), t => t.Name == "writing_exception_add");
        Assert.True((await registry.CallAsync("policy_declare", new(), TestContext.Current.CancellationToken)).Unknown);
    }

    [Fact]
    public void PlacementPinsBothTheWorkRevisionAndTheSelectedRegistry()
    {
        using var fixture = new Writer();
        RecordEnvelope order = fixture.Fixture.Add("work-order", new JsonObject
        {
            ["key"] = "PLACE-1", ["kind"] = "ordinary", ["title"] = "Place fixture work", ["intent"] = "Keep explicit placement.",
            ["scope"] = new JsonArray("fixture"), ["exitQuestion"] = "Is the placement exact?", ["inputs"] = new JsonArray(), ["deliverables"] = new JsonArray("placement"),
        });
        JsonObject definition = fixture.Fixture.PolicyBody("placement", duty: false);
        definition["startsAt"] = RecordParser.FormatTimestamp(fixture.Clock.Now.AddSeconds(-30));
        definition["rules"] = new JsonArray(new JsonObject
        {
            ["key"] = "place", ["kind"] = "vericairn.estate/placement", ["version"] = 1,
            ["parameters"] = new JsonObject { ["backlogs"] = new JsonArray(new JsonObject { ["key"] = "future-work", ["title"] = "Future work", ["owner"] = fixture.Operator, ["reason"] = "A bounded planning location." }) },
        });
        RecordEnvelope policy = fixture.Fixture.Policy(definition);
        int before = fixture.Model.Records.Count;
        Assert.Equal("conflict", fixture.Api.WorkPlace("PLACE-1", new(fixture.Operator, order.ComputedHash, "stale", Backlog: "future-work")).Problem!.Code);
        Assert.False(fixture.Api.WorkPlace("PLACE-1", new(fixture.Operator, order.ComputedHash, policy.ComputedHash, Backlog: "unknown")).Ok);
        Assert.Equal(before, fixture.Model.Records.Count);
        RecordWritten placed = Ok(fixture.Api.WorkPlace("PLACE-1", new(fixture.Operator, order.ComputedHash, policy.ComputedHash, Backlog: "future-work")));
        Assert.Equal("future-work", fixture.Model.ById[placed.Id].Body.GetProperty("placement").GetProperty("key").GetString());
        Assert.True(SignatureChecks.IsAuthentic(fixture.Model, fixture.Model.ById[placed.Id]));
        Assert.Equal("conflict", fixture.Api.WorkPlace("PLACE-1", new(fixture.Operator, order.ComputedHash, policy.ComputedHash, Backlog: "future-work")).Problem!.Code);
    }

    private static RecordWritten Ok(WriteOutcome outcome)
    {
        Assert.True(outcome.Ok, JsonSerializer.Serialize(outcome, ProtocolJson.Options));
        return outcome.Written!;
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        internal DateTimeOffset Now { get; set; } = now;
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class Writer : IDisposable
    {
        internal PolicyFixture Fixture { get; } = new();
        internal EstateService Api { get; }
        internal FixedClock Clock { get; }
        internal string Operator => Fixture.Estate.OperatorId;
        internal EstateModel Model => Api.Session.Load();
        internal Writer(bool builder = false)
        {
            Api = EstateService.Open(Fixture.Estate.Root);
            RecordWritten device = Ok(Api.DeviceCreate(new(builder ? Fixture.Estate.BuilderId : Operator, "Fixture writer", "file")));
            Fixture.Estate.SetClock(Model.ById[device.Id].AtTime);
            Fixture.Add("admission", new JsonObject { ["principal"] = builder ? Fixture.Estate.BuilderId : Operator, ["status"] = "admitted", ["note"] = "Fixture runtime writer." }, subject: Model.ById[device.Id].Reference);
            Clock = new(Fixture.Estate.Clock.AddMinutes(1));
            Api.PolicyClock = Clock;
        }
        internal JsonElement Approve(JsonObject body)
        {
            RecordEnvelope approval = Fixture.Approve(body);
            body["basis"] = new JsonArray(new JsonObject { ["decision"] = PolicyFixture.Ref(approval), ["requirement"] = "definition" });
            return PolicyFixture.Element(body);
        }
        public void Dispose() { Api.Dispose(); Fixture.Dispose(); }
    }
}
