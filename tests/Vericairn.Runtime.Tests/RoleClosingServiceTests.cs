// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using Vericairn.Protocol;
using Vericairn.Records.Estate;
using Vericairn.Records.History;
using Vericairn.Records.Model;
using Vericairn.Records.Signing;
using Vericairn.Records.Types;
using Vericairn.Runtime.Service;

namespace Vericairn.Runtime.Tests;

/// <summary>
/// Role closings written with the removal (ID-14; design/signed-history.md § 8): in an estate that has adopted history checkpoints, a
/// principal revision removing a role from a person is stored after the person's role closing in one batch, signed by the remover's
/// device; a removal that comes without it is refused with nothing written; an estate that has not adopted writes removals as before.
/// </summary>
public sealed class RoleClosingServiceTests
{
    private static readonly string[] Effects = ["an effect"];
    private static readonly string[] NotAuthorized = ["publication"];
    private static readonly string[] BuilderAndReviewer = ["builder", "reviewer"];
    private static readonly string[] Reviewer = ["reviewer"];

    private static JsonElement Person(string name, params string[] roles) => JsonSerializer.SerializeToElement(new { kind = "human", name, roles });

    private static JsonElement Decision(string by, string title) => JsonSerializer.SerializeToElement(new
    {
        title, statement = "Decide.", summary = "A decision.", synthesis = "For tests.", effects = Effects, affects = Array.Empty<string>(),
        decidedBy = by, decidedAt = "2026-10-13T12:00:00Z", stake = "medium", notAuthorized = NotAuthorized,
    });

    private static string Describe(WriteOutcome outcome) => outcome.Problem is null ? "ok" : outcome.Problem.Message + " " + string.Join(" | ", outcome.Problem.Findings.Select(f => f.Code + ": " + f.Message));

    [Fact]
    public void RemovingAPersonsRoleStoresTheRoleClosingFirstAndRefusesTheirBackdatedRecordsAfterIt()
    {
        using var f = new HistoryTriggerTests.Fixture();
        string op = f.Records.Estate.OperatorId;
        WriteOutcome appointed = f.Api.NewRecord(new NewRecordRequest("principal", op, Person("P", "operator", "reviewer")));
        Assert.True(appointed.Ok, Describe(appointed));
        string p = appointed.Written!.Id;
        string device = f.HeldDevice(p);
        WriteOutcome decided = f.Api.NewRecord(new NewRecordRequest("decision", p, Decision(p, "Before the removal")));
        Assert.True(decided.Ok, Describe(decided));
        // The removal is dated at the runtime's clock, in whole seconds: a later second than the decision.
        Thread.Sleep(1100);

        WriteOutcome removed = f.Api.NewRecord(new NewRecordRequest("principal", op, Person("P", "reviewer"), Supersedes: p));
        Assert.True(removed.Ok, Describe(removed));
        Assert.Equal(("closing", null), (removed.Checkpoint!.Trigger, removed.Checkpoint.Skipped));
        EstateModel model = f.Api.Session.Load();
        Assert.False(model.Findings.HasErrors, string.Join('\n', model.Findings.Where(x => x.Severity == Severity.Error)));
        RecordEnvelope closing = model.ById[removed.Checkpoint.Written!.Id];
        RecordEnvelope removal = model.ById[removed.Written!.Id];
        Assert.Equal(("role", p), CheckpointBody.ReadCloses(closing.Body));
        Assert.Equal(op, closing.By);
        Assert.Equal(f.OperatorDevice, Assert.Single(closing.Signatures).Device);
        Assert.True(string.CompareOrdinal(closing.At, removal.At) <= 0);
        Assert.Single(model.Trust.Closings);
        Assert.DoesNotContain(model.Findings, x => x.Code is "history.closing-unfollowed" or "history.closing-missing");
        Assert.True(model.Counts(model.ById[decided.Written!.Id]));
        Assert.False(model.HasRole(p, Roles.Operator));

        // P's device signs a decision dated before the removal, after it: the closing refuses it.
        f.Records.Estate.SetClock(model.ById[decided.Written.Id].AtTime.AddSeconds(-1));
        RecordEnvelope backdated = f.Records.Estate.Add("decision", p, Decision(p, "Backdated").GetRawText(), signer: new StoreSigner(new FileKeyStore(VericairnHome.Path()), device));
        model = f.Api.Session.Load();
        Assert.False(model.Counts(model.ById[backdated.Id]));
        Assert.Contains(model.Findings, x => x.Code == "history.after-closing" && x.Location.StartsWith(backdated.ExpectedPath, StringComparison.Ordinal));
    }

    [Fact]
    public void AnOperatorWhoClosedAnothersRoleIsRemovedWithTheirOwnRoleClosingAndTheirClosingKeepsActing()
    {
        using var f = new HistoryTriggerTests.Fixture();
        string op = f.Records.Estate.OperatorId;
        WriteOutcome o1 = f.Api.NewRecord(new NewRecordRequest("principal", op, Person("O1", "operator")));
        Assert.True(o1.Ok, Describe(o1));
        f.HeldDevice(o1.Written!.Id);
        WriteOutcome p = f.Api.NewRecord(new NewRecordRequest("principal", op, Person("P", "operator")));
        Assert.True(p.Ok, Describe(p));
        string pDevice = f.HeldDevice(p.Written!.Id);
        WriteOutcome decided = f.Api.NewRecord(new NewRecordRequest("decision", p.Written.Id, Decision(p.Written.Id, "P's before the closing")));
        Assert.True(decided.Ok, Describe(decided));
        Thread.Sleep(1100);

        // O1 removes P's role, with P's role closing signed by O1's device.
        WriteOutcome pRemoved = f.Api.NewRecord(new NewRecordRequest("principal", o1.Written.Id, Person("P", "reviewer"), Supersedes: p.Written.Id));
        Assert.True(pRemoved.Ok, Describe(pRemoved));
        string pClosing = pRemoved.Checkpoint!.Written!.Id;
        Thread.Sleep(1100);

        // The root operator removes O1 in turn, with O1's role closing; O1's closing of P keeps acting.
        WriteOutcome o1Removed = f.Api.NewRecord(new NewRecordRequest("principal", op, Person("O1", "reviewer"), Supersedes: o1.Written.Id));
        Assert.True(o1Removed.Ok, Describe(o1Removed));
        Assert.Equal("closing", o1Removed.Checkpoint!.Trigger);
        EstateModel model = f.Api.Session.Load();
        Assert.False(model.Findings.HasErrors, string.Join('\n', model.Findings.Where(x => x.Severity == Severity.Error)));
        Assert.Contains(model.Trust.Closings, c => c.Record.Id == pClosing);
        Assert.Contains(model.Trust.Closings, c => c.Record.Id == o1Removed.Checkpoint.Written!.Id);

        f.Records.Estate.SetClock(model.ById[decided.Written!.Id].AtTime.AddSeconds(-1));
        RecordEnvelope backdated = f.Records.Estate.Add("decision", p.Written.Id, Decision(p.Written.Id, "Backdated").GetRawText(), signer: new StoreSigner(new FileKeyStore(VericairnHome.Path()), pDevice));
        model = f.Api.Session.Load();
        Assert.False(model.Counts(model.ById[backdated.Id]));
    }

    [Fact]
    public void AnActWhoseBatchWouldLeaveAnotherClosingFailingSaysSoRatherThanDescribingItsOwnClosing()
    {
        // A person holding administer closes and revokes a device; narrowing that grant would leave their closing failing. The refusal
        // names the records the findings name, and does not describe the batch's own closing as what failed.
        using var f = new HistoryTriggerTests.Fixture();
        string op = f.Records.Estate.OperatorId;
        WriteOutcome x = f.Api.NewRecord(new NewRecordRequest("principal", op, Person("X", "contributor")));
        Assert.True(x.Ok, Describe(x));
        string xId = x.Written!.Id;
        var grant = new System.Text.Json.Nodes.JsonObject
        {
            ["grantee"] = xId, ["scope"] = new System.Text.Json.Nodes.JsonArray("administer"), ["reserved"] = new System.Text.Json.Nodes.JsonArray(),
            ["conditions"] = new System.Text.Json.Nodes.JsonArray(), ["basis"] = f.Records.Decision.Id,
        };
        WriteOutcome granted = f.Api.NewRecord(new NewRecordRequest("authority", op, Vericairn.Records.Tests.PolicyFixture.Element(grant)));
        Assert.True(granted.Ok, Describe(granted));
        f.HeldDevice(xId);
        Thread.Sleep(1100);
        WriteOutcome revoked = f.Api.DeviceRevoke(new RevokeRequest(f.BuilderDevice, xId, "Revoked by the administrator."));
        Assert.True(revoked.Ok, Describe(revoked));
        Thread.Sleep(1100);
        int records = f.Api.Session.Load().Records.Count;

        grant["scope"] = new System.Text.Json.Nodes.JsonArray("qualify");
        WriteOutcome narrowed = f.Api.NewRecord(new NewRecordRequest("authority", op, Vericairn.Records.Tests.PolicyFixture.Element(grant), Supersedes: granted.Written!.Id));
        Assert.False(narrowed.Ok, "the narrowing was written");
        Assert.Contains("would leave the records the findings name failing", narrowed.Problem!.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("is written after its closing", narrowed.Problem.Message, StringComparison.Ordinal);
        Assert.Contains(narrowed.Problem.Findings, x => x.Location.StartsWith(f.Api.Session.Load().ById[revoked.Checkpoint!.Written!.Id].ExpectedPath, StringComparison.Ordinal));
        Assert.Equal(records, f.Api.Session.Load().Records.Count);
    }

    [Fact]
    public void ARemovalSignedElsewhereCannotCarryItsRoleClosingAndIsRefusedOnceTheEstateHasAdopted()
    {
        using var f = new HistoryTriggerTests.Fixture(adopt: false);
        string op = f.Records.Estate.OperatorId;
        WriteOutcome appointed = f.Api.NewRecord(new NewRecordRequest("principal", op, Person("P", "operator")));
        Assert.True(appointed.Ok, Describe(appointed));
        string p = appointed.Written!.Id;
        string publicKey = EstateModel.BodyString(f.Api.Session.Load().ById[f.OperatorDevice], "publicKey")!;
        var request = new NewRecordRequest("principal", op, Person("P", "reviewer"), Supersedes: p);

        // Prepared on the phone before the estate adopted, signed after: a record signed elsewhere carries no closing, so it is refused.
        RecordPrepareResponse prepared;
        using (f.Api.ForDeviceRequest(f.OperatorDevice, publicKey)) prepared = f.Api.PrepareRecord(request);
        Assert.True(prepared.Ok, prepared.Problem?.Message);
        f.Adopt();
        int records = f.Api.Session.Load().Records.Count;
        string signature = new StoreSigner(new FileKeyStore(VericairnHome.Path()), f.OperatorDevice).Sign(RecordSigning.Digest(prepared.Prepared!.Hash)).Signature;
        WriteOutcome written;
        using (f.Api.ForDeviceRequest(f.OperatorDevice, publicKey)) written = f.Api.NewSignedRecord(new SignedRecordRequest(prepared.Prepared.Plan, signature));
        Assert.False(written.Ok);
        Assert.Contains($"a checkpoint closing role {p}", written.Problem!.Message, StringComparison.Ordinal);
        Assert.Contains("carries no closing", written.Problem.Message, StringComparison.Ordinal);
        EstateModel model = f.Api.Session.Load();
        Assert.Equal(records, model.Records.Count);
        Assert.True(model.HasRole(p, Roles.Operator));

        // Written on the runtime's machine, it carries its closing.
        WriteOutcome removed = f.Api.NewRecord(request);
        Assert.True(removed.Ok, Describe(removed));
        Assert.Equal("closing", removed.Checkpoint!.Trigger);
        Assert.False(f.Api.Session.Load().HasRole(p, Roles.Operator));
    }

    [Fact]
    public void AnEstateThatHasNotAdoptedWritesARoleRemovalAsBefore()
    {
        using var f = new HistoryTriggerTests.Fixture(adopt: false);
        string op = f.Records.Estate.OperatorId;
        WriteOutcome appointed = f.Api.NewRecord(new NewRecordRequest("principal", op, Person("P", "reviewer", "contributor")));
        Assert.True(appointed.Ok, Describe(appointed));
        WriteOutcome removed = f.Api.NewRecord(new NewRecordRequest("principal", op, Person("P", "reviewer"), Supersedes: appointed.Written!.Id));
        Assert.True(removed.Ok, Describe(removed));
        Assert.Null(removed.Checkpoint);
        EstateModel model = f.Api.Session.Load();
        Assert.Empty(model.Trust.Closings);
        Assert.DoesNotContain(model.Findings, x => x.Code.StartsWith("history.closing", StringComparison.Ordinal));
    }

    [Fact]
    public void TheAdoptionCheckListsRecordsByAPersonWhoseOperatorRoleWasRemovedBeforeAdoption()
    {
        using var f = new HistoryTriggerTests.Fixture(adopt: false);
        string op = f.Records.Estate.OperatorId;
        WriteOutcome appointed = f.Api.NewRecord(new NewRecordRequest("principal", op, Person("P", "operator", "contributor")));
        Assert.True(appointed.Ok, Describe(appointed));
        string p = appointed.Written!.Id;
        WriteOutcome reviewer = f.Api.NewRecord(new NewRecordRequest("principal", op, Person("Q", "reviewer", "contributor")));
        Assert.True(reviewer.Ok, Describe(reviewer));
        string q = reviewer.Written!.Id;
        string pDevice = f.HeldDevice(p), qDevice = f.HeldDevice(q);
        f.Records.Estate.SetClock(DateTimeOffset.UtcNow.AddSeconds(-1));
        RecordEnvelope order = f.Records.Estate.Add("work-order", p, Vericairn.Records.Tests.HistoryFixture.Order("RCS-1", "Written while P was an operator"),
            signer: new StoreSigner(new FileKeyStore(VericairnHome.Path()), pDevice));
        RecordEnvelope other = f.Records.Estate.Add("work-order", q, Vericairn.Records.Tests.HistoryFixture.Order("RCS-2", "Written while Q was a reviewer"),
            signer: new StoreSigner(new FileKeyStore(VericairnHome.Path()), qDevice));
        // The removals are dated at the runtime's clock, in whole seconds: a later second than both work orders.
        Thread.Sleep(2100);
        WriteOutcome removed = f.Api.NewRecord(new NewRecordRequest("principal", op, Person("P", "contributor"), Supersedes: p));
        Assert.True(removed.Ok, Describe(removed));
        WriteOutcome lesser = f.Api.NewRecord(new NewRecordRequest("principal", op, Person("Q", "contributor"), Supersedes: q));
        Assert.True(lesser.Ok, Describe(lesser));

        // Only the operator role's removal is listed: another role is read at each record's time and never has a closing.
        HistoryCheckpointResponse plan = f.Api.HistoryCheckpoint(new HistoryCheckpointRequest(null, Adopt: true, Check: true)).Checkpoint!;
        HistoryRecordItem listed = Assert.Single(plan.ByRemovedRoles, r => r.Id == order.Id);
        Assert.Equal("roles removed since: operator", listed.Note);
        Assert.DoesNotContain(plan.ByRemovedRoles, r => r.Id == other.Id || r.Id == removed.Written!.Id);
    }

    [Fact]
    public void RemovingOnlyTheReviewerRoleFromAnOperatorWritesNoClosingAndTheirNextAppointmentIsWritten()
    {
        // The Reviewer's probe, through the runtime: the root operator removes only the reviewer role from P, who stays an operator. No
        // closing is written; P's next decision, appointment and device closing are written and count.
        using var f = new HistoryTriggerTests.Fixture();
        string op = f.Records.Estate.OperatorId;
        WriteOutcome appointed = f.Api.NewRecord(new NewRecordRequest("principal", op, Person("P", "operator", "reviewer")));
        Assert.True(appointed.Ok, Describe(appointed));
        string p = appointed.Written!.Id;
        f.HeldDevice(p);
        Thread.Sleep(1100);

        WriteOutcome removed = f.Api.NewRecord(new NewRecordRequest("principal", op, Person("P", "operator"), Supersedes: p));
        Assert.True(removed.Ok, Describe(removed));
        Assert.True(removed.Checkpoint is null || removed.Checkpoint.Trigger != "closing");
        WriteOutcome decided = f.Api.NewRecord(new NewRecordRequest("decision", p, Decision(p, "After the reviewer role was removed")));
        Assert.True(decided.Ok, Describe(decided));
        WriteOutcome appointment = f.Api.NewRecord(new NewRecordRequest("principal", p, Person("Z", "operator")));
        Assert.True(appointment.Ok, Describe(appointment));
        WriteOutcome revoked = f.Api.DeviceRevoke(new RevokeRequest(f.BuilderDevice, p, "Revoked by P, still an operator."));
        Assert.True(revoked.Ok, Describe(revoked));
        Assert.Equal("closing", revoked.Checkpoint!.Trigger);

        EstateModel model = f.Api.Session.Load();
        Assert.DoesNotContain(model.Trust.Closings, c => c.Kind == "role");
        Assert.Contains(model.Trust.Closings, c => c.Kind == "device" && c.Record.Id == revoked.Checkpoint.Written!.Id);
        Assert.True(model.Counts(model.ById[appointment.Written!.Id]));
        Assert.True(model.HasRole(appointment.Written.Id, Roles.Operator));
        Assert.DoesNotContain(model.Findings, x => x.Code is "history.after-closing" or "history.closing-missing");
    }

    [Fact]
    public void AnAgentsRoleRemovalWritesNoRoleClosing()
    {
        using var f = new HistoryTriggerTests.Fixture();
        string op = f.Records.Estate.OperatorId;
        JsonElement agent = JsonSerializer.SerializeToElement(new { kind = "agent", name = "Helper", roles = BuilderAndReviewer, agent = new { vendor = "Example", product = "Example Agent" } });
        WriteOutcome appointed = f.Api.NewRecord(new NewRecordRequest("principal", op, agent));
        Assert.True(appointed.Ok, Describe(appointed));
        JsonElement fewer = JsonSerializer.SerializeToElement(new { kind = "agent", name = "Helper", roles = Reviewer, agent = new { vendor = "Example", product = "Example Agent" } });
        WriteOutcome removed = f.Api.NewRecord(new NewRecordRequest("principal", op, fewer, Supersedes: appointed.Written!.Id));
        Assert.True(removed.Ok, Describe(removed));
        Assert.True(removed.Checkpoint is null || removed.Checkpoint.Trigger != "closing");
        Assert.Empty(f.Api.Session.Load().Trust.Closings);
    }
}
