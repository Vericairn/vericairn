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
        Closing acting = Assert.Single(model.Trust.Closings);
        Assert.Equal(removal.At, acting.Until);
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
    public void TheAdoptionCheckListsRecordsByAPersonWhoseRoleWasRemovedBeforeAdoption()
    {
        using var f = new HistoryTriggerTests.Fixture(adopt: false);
        string op = f.Records.Estate.OperatorId;
        WriteOutcome appointed = f.Api.NewRecord(new NewRecordRequest("principal", op, Person("P", "reviewer", "contributor")));
        Assert.True(appointed.Ok, Describe(appointed));
        string p = appointed.Written!.Id;
        string device = f.HeldDevice(p);
        f.Records.Estate.SetClock(DateTimeOffset.UtcNow.AddSeconds(-1));
        RecordEnvelope order = f.Records.Estate.Add("work-order", p, Vericairn.Records.Tests.HistoryFixture.Order("RCS-1", "Written while P was a contributor"),
            signer: new StoreSigner(new FileKeyStore(VericairnHome.Path()), device));
        // The removal is dated at the runtime's clock, in whole seconds: a later second than the work order.
        Thread.Sleep(1100);
        WriteOutcome removed = f.Api.NewRecord(new NewRecordRequest("principal", op, Person("P", "reviewer"), Supersedes: p));
        Assert.True(removed.Ok, Describe(removed));

        HistoryCheckpointResponse plan = f.Api.HistoryCheckpoint(new HistoryCheckpointRequest(null, Adopt: true, Check: true)).Checkpoint!;
        HistoryRecordItem listed = Assert.Single(plan.ByRemovedRoles, r => r.Id == order.Id);
        Assert.Equal("roles removed since: contributor", listed.Note);
        Assert.DoesNotContain(plan.ByRemovedRoles, r => r.Id == removed.Written!.Id);
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
