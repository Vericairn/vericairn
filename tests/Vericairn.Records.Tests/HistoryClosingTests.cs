// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json.Nodes;
using Vericairn.Records.Estate;
using Vericairn.Records.History;
using Vericairn.Records.Model;
using Vericairn.Records.Signing;
using Vericairn.Records.Types;

namespace Vericairn.Records.Tests;

/// <summary>
/// Closings and authority as it stood at a record's time (design/signed-history.md § 8): what a revoked device, a replaced key or a
/// narrowed grant still accounts for, whatever date an author writes, and the dates that history checkpoints question.
/// </summary>
public class HistoryClosingTests
{
    private const string Helper = """{"kind":"agent","name":"Helper","roles":["reviewer"],"agent":{"vendor":"Example","product":"Example Agent"}}""";

    [Fact]
    public void ARevokedDevicesBackdatedRecordIsRefusedAfterItsClosingAndALateOneAnchored()
    {
        using var f = new HistoryFixture();
        f.Require();
        f.Adopt();
        RecordEnvelope before = f.Estate.Add("work-order", f.Estate.BuilderId, HistoryFixture.Order("CLS-1", "Before the closing"), signer: f.BuilderSigner);
        RecordEnvelope closing = f.Close("device", f.BuilderDevice.Id);
        RecordEnvelope revocation = f.Revoke(f.BuilderDevice, f.BuilderAdmission);
        EstateModel model = f.Load();
        Assert.False(model.Findings.HasErrors, Errors(model));
        Assert.True(model.Counts(before));
        Assert.True(model.Counts(revocation));
        Assert.Equal(closing.Id, Assert.Single(model.Trust.Closings).Record.Id);
        Assert.DoesNotContain(model.Findings, x => x.Code == "history.closing-unfollowed");

        // Signed with the revoked device's key and dated before its closing and its revocation: the date admits it, the closing does not.
        f.Estate.SetClock(before.AtTime.AddSeconds(-2));
        RecordEnvelope backdated = f.Estate.Add("work-order", f.Estate.BuilderId, HistoryFixture.Order("CLS-2", "Backdated"), signer: f.BuilderSigner);
        // One the builder wrote in its own copy before the closing and merged after it is flagged the same way.
        RecordEnvelope late = f.Estate.Add("work-order", f.Estate.BuilderId, HistoryFixture.Order("CLS-3", "Written elsewhere before the closing"), signer: f.BuilderSigner);
        model = f.Load();
        Assert.Equal(SignatureChecks.AdmissionState.Admitted, SignatureChecks.AdmissionStateAt(model, f.BuilderDevice, backdated.At));
        Assert.False(model.Counts(model.ById[backdated.Id]));
        Assert.False(model.Counts(model.ById[late.Id]));
        Finding refused = Assert.Single(model.Findings, x => x.Code == "history.after-closing" && x.Location.StartsWith(backdated.ExpectedPath, StringComparison.Ordinal));
        Assert.Contains(closing.Id, refused.Message, StringComparison.Ordinal);
        Assert.Contains(EstateStatus.HistoryProblems(model), x => x.Code == "history.after-closing" && x.Location.StartsWith(late.ExpectedPath, StringComparison.Ordinal));
        Assert.DoesNotContain(EstateStatus.WorkItems(model), i => i.Key is "CLS-2" or "CLS-3");
        Assert.True(model.Counts(model.ById[before.Id]));
        Assert.Contains(EstateStatus.WorkItems(model), i => i.Key == "CLS-1");

        // The root operator anchors the late record after reading it; the backdated one stays refused.
        f.Checkpoint(anchored: [late]);
        model = f.Load();
        Assert.True(model.Counts(model.ById[late.Id]), string.Join('\n', model.Findings));
        Assert.False(model.Counts(model.ById[backdated.Id]));
        Assert.DoesNotContain(model.Findings, x => x.Code == "history.after-closing" && x.Location.StartsWith(late.ExpectedPath, StringComparison.Ordinal));
        Assert.Contains(EstateStatus.WorkItems(model), i => i.Key == "CLS-3");
    }

    [Fact]
    public void AClosedDevicesLaterAdmissionsAndRevocationsCountForNothing()
    {
        using var f = new HistoryFixture();
        (RecordEnvelope second, RecordEnvelope secondAdmission) = f.Admitted(f.Estate.OperatorId, "The operator's second device");
        f.Require();
        f.Adopt();
        RecordEnvelope closing = f.Close("device", second.Id);
        f.Revoke(second, secondAdmission);
        RecordEnvelope added = f.Device(f.Estate.OperatorId, "A device added after the closing");
        DateTimeOffset resume = f.Estate.Clock;

        // Signed by the closed device and dated before its closing: an admission of the new device and a revocation of the builder's.
        f.Estate.SetClock(closing.AtTime.AddSeconds(-30));
        RecordEnvelope admission = f.Estate.Add("admission", f.Estate.OperatorId, HistoryFixture.Admission(f.Estate.OperatorId, "Admitted by a closed device."), added.Reference, signer: f.Signer(second));
        RecordEnvelope revocation = f.Revoke(f.BuilderDevice, f.BuilderAdmission, f.Signer(second));
        f.Estate.SetClock(resume);
        RecordEnvelope byAdded = f.Estate.Add("work-order", f.Estate.OperatorId, HistoryFixture.Order("CLS-4", "Signed by the added device"), signer: f.Signer(added));
        RecordEnvelope byBuilder = f.Estate.Add("work-order", f.Estate.BuilderId, HistoryFixture.Order("CLS-5", "Signed by the builder"), signer: f.BuilderSigner);
        EstateModel model = f.Load();
        Assert.False(model.Counts(model.ById[admission.Id]));
        Assert.False(model.Counts(model.ById[revocation.Id]));
        Assert.Equal(SignatureChecks.AdmissionState.Unadmitted, SignatureChecks.AdmissionStateAt(model, added, byAdded.At));
        Assert.False(SignatureChecks.IsAuthentic(model, model.ById[byAdded.Id]));
        Assert.False(model.Counts(model.ById[byAdded.Id]));
        Assert.Equal(SignatureChecks.AdmissionState.Admitted, SignatureChecks.AdmissionStateAt(model, f.BuilderDevice, byBuilder.At));
        Assert.True(model.Counts(model.ById[byBuilder.Id]));
        Finding refused = Assert.Single(model.Findings, x => x.Code == "history.after-closing" && x.Location.StartsWith(admission.ExpectedPath, StringComparison.Ordinal));
        Assert.Contains("neither admits nor revokes", refused.Message, StringComparison.Ordinal);
        Assert.Contains(model.Findings, x => x.Code == "history.after-closing" && x.Location.StartsWith(revocation.ExpectedPath, StringComparison.Ordinal));
    }

    [Fact]
    public void AReplacedKeysBackdatedRecordIsRefusedAfterItsClosing()
    {
        using var f = new HistoryFixture();
        f.Require();
        f.Adopt();
        RecordEnvelope early = f.Estate.Add("work-order", f.Estate.BuilderId, HistoryFixture.Order("KEY-1", "With the first key"), signer: f.BuilderSigner);
        RecordEnvelope closing = f.Close("key", f.BuilderDevice.Id, f.Estate.BuilderId, f.BuilderSigner);
        (RecordEnvelope rotation, StoreSigner next) = f.Rotate(f.BuilderDevice, f.BuilderSigner);
        RecordEnvelope fresh = f.Estate.Add("work-order", f.Estate.BuilderId, HistoryFixture.Order("KEY-2", "With the new key"), signer: next);
        EstateModel model = f.Load();
        Assert.False(model.Findings.HasErrors, Errors(model));
        Assert.True(model.Counts(early));
        Assert.True(model.Counts(rotation));
        Assert.True(model.Counts(fresh));
        Assert.Equal(closing.Id, Assert.Single(model.Trust.Closings).Record.Id);
        Assert.DoesNotContain(model.Findings, x => x.Code == "history.closing-unfollowed");

        // Signed with the replaced key and dated before the rotation, and a rename signed with it: neither counts.
        f.Estate.SetClock(early.AtTime);
        RecordEnvelope backdated = f.Estate.Add("work-order", f.Estate.BuilderId, HistoryFixture.Order("KEY-3", "Backdated with the first key"), signer: f.BuilderSigner);
        JsonObject renamed = HistoryFixture.Body(f.BuilderDevice);
        renamed["name"] = "Renamed with the replaced key";
        RecordEnvelope rename = f.Estate.Add("device", f.Estate.BuilderId, renamed.ToJsonString(), supersedes: f.BuilderDevice.Id, signer: f.BuilderSigner);
        model = f.Load();
        Assert.False(model.Counts(model.ById[backdated.Id]));
        Assert.Contains(model.Findings, x => x.Code == "history.after-closing" && x.Location.StartsWith(backdated.ExpectedPath, StringComparison.Ordinal) &&
            x.Message.Contains(f.BuilderDevice.Id, StringComparison.Ordinal));
        Assert.False(model.Counts(model.ById[rename.Id]));
        Assert.Equal([f.BuilderDevice.Id, rotation.Id], model.Trust.DeviceChain(f.BuilderDevice.Id).Select(r => r.Id));
        Assert.True(model.Counts(model.ById[fresh.Id]));
        Assert.True(model.Counts(model.ById[early.Id]));
    }

    [Fact]
    public void ALaterGrantAuthorizesNothingEarlierOnceAdopted()
    {
        // In an estate that has not adopted checkpoints a grant is read as it is now, as before.
        using (var plain = new HistoryFixture())
        {
            RecordEnvelope basis = plain.Decision();
            plain.Require();
            RecordEnvelope early = plain.Estate.Add("principal", plain.Estate.BuilderId, Helper, signer: plain.BuilderSigner);
            plain.Grant(plain.Estate.BuilderId, basis, [Scopes.Administer]);
            EstateModel before = plain.Load();
            Assert.False(SignedHistory.Adopted(before));
            Assert.DoesNotContain(before.Findings, x => x.Code == "principal.author");
            Assert.True(before.HasScopeAt(plain.Estate.BuilderId, Scopes.Administer, before.ById[early.Id]));
        }

        using var f = new HistoryFixture();
        RecordEnvelope decision = f.Decision();
        f.Require();
        f.Adopt();
        RecordEnvelope earlier = f.Estate.Add("principal", f.Estate.BuilderId, Helper, signer: f.BuilderSigner);
        f.Grant(f.Estate.BuilderId, decision, [Scopes.Administer]);
        RecordEnvelope later = f.Estate.Add("principal", f.Estate.BuilderId, Helper.Replace("Helper", "Second helper", StringComparison.Ordinal), signer: f.BuilderSigner);
        EstateModel model = f.Load();
        Assert.Contains(model.Findings, x => x.Code == "principal.author" && x.Location == earlier.ExpectedPath);
        Assert.DoesNotContain(model.Findings, x => x.Code == "principal.author" && x.Location == later.ExpectedPath);
        Assert.False(model.HasScopeAt(f.Estate.BuilderId, Scopes.Administer, model.ById[earlier.Id]));
        Assert.True(model.HasScopeAt(f.Estate.BuilderId, Scopes.Administer, model.ById[later.Id]));
        Assert.True(model.HasScope(f.Estate.BuilderId, Scopes.Administer));
    }

    [Fact]
    public void TheOperatorRoleIsReadAsItStoodAtARecordsTime()
    {
        using var f = new HistoryFixture();
        RecordEnvelope person = f.Estate.Add("principal", f.Estate.OperatorId, """{"kind":"human","name":"Second person","roles":["reviewer"]}""", signer: f.OperatorSigner);
        (RecordEnvelope device, _) = f.Admitted(person.Id, "The second person's device");
        f.Require();
        f.Adopt();
        RecordEnvelope early = f.Estate.Add("principal", person.Id, Helper, signer: f.Signer(device));
        f.Estate.Add("principal", f.Estate.OperatorId, """{"kind":"human","name":"Second person","roles":["operator"]}""", supersedes: person.Id, signer: f.OperatorSigner);
        RecordEnvelope late = f.Estate.Add("principal", person.Id, Helper.Replace("Helper", "Second helper", StringComparison.Ordinal), signer: f.Signer(device));
        EstateModel model = f.Load();
        Assert.Contains(model.Findings, x => x.Code == "principal.author" && x.Location == early.ExpectedPath);
        Assert.DoesNotContain(model.Findings, x => x.Code == "principal.author" && x.Location == late.ExpectedPath);
        Assert.DoesNotContain(Roles.Operator, model.RolesAt(person.Id, model.ById[early.Id]));
        Assert.Contains(Roles.Operator, model.RolesAt(person.Id, model.ById[late.Id]));
        Assert.True(model.HasRole(person.Id, Roles.Operator));
    }

    [Fact]
    public void ANarrowedGrantWithdrawsNothingWrittenAndAuthorizesNothingAfterItsClosing()
    {
        using var f = new HistoryFixture();
        RecordEnvelope decision = f.Decision();
        RecordEnvelope grant = f.Grant(f.Estate.BuilderId, decision, [Scopes.Administer, Scopes.LocalEngineering]);
        f.Require();
        f.Adopt();
        RecordEnvelope written = f.Estate.Add("principal", f.Estate.BuilderId, Helper, signer: f.BuilderSigner);
        RecordEnvelope closing = f.Close("grant", grant.Id);
        f.Grant(f.Estate.BuilderId, decision, [Scopes.LocalEngineering], supersedes: grant.Id);
        RecordEnvelope after = f.Estate.Add("principal", f.Estate.BuilderId, Helper.Replace("Helper", "After the narrowing", StringComparison.Ordinal), signer: f.BuilderSigner);
        EstateModel model = f.Load();
        Assert.DoesNotContain(model.Findings, x => x.Code == "principal.author" && x.Location == written.ExpectedPath);
        Assert.True(model.Counts(model.ById[written.Id]));
        Assert.True(model.HasScopeAt(f.Estate.BuilderId, Scopes.Administer, model.ById[written.Id]));
        Assert.Contains(model.Findings, x => x.Code == "principal.author" && x.Location == after.ExpectedPath);
        Assert.False(model.HasScopeAt(f.Estate.BuilderId, Scopes.Administer, model.ById[after.Id]));
        Assert.DoesNotContain(model.Findings, x => x.Code is "history.after-closing" or "history.closing-unfollowed");

        // Dated back into the grant's time after its closing, a record relies on the closed revision and counts for nothing.
        f.Estate.SetClock(written.AtTime);
        RecordEnvelope backdated = f.Estate.Add("principal", f.Estate.BuilderId, Helper.Replace("Helper", "Backdated helper", StringComparison.Ordinal), signer: f.BuilderSigner);
        model = f.Load();
        Assert.False(model.Counts(model.ById[backdated.Id]));
        Assert.False(model.HasScopeAt(f.Estate.BuilderId, Scopes.Administer, model.ById[backdated.Id]));
        Finding refused = Assert.Single(model.Findings, x => x.Code == "history.after-closing" && x.Location == backdated.ExpectedPath);
        Assert.Contains(grant.Id, refused.Message, StringComparison.Ordinal);
        Assert.Contains(closing.Id, refused.Message, StringComparison.Ordinal);
        Assert.True(model.Counts(model.ById[written.Id]));

        // Anchored after reading, it counts, and the revision it relied on authorizes it.
        f.Checkpoint(anchored: [backdated]);
        model = f.Load();
        Assert.True(model.Counts(model.ById[backdated.Id]));
        Assert.True(model.HasScopeAt(f.Estate.BuilderId, Scopes.Administer, model.ById[backdated.Id]));
        Assert.DoesNotContain(model.Findings, x => x.Location == backdated.ExpectedPath && x.Code is "principal.author" or "history.after-closing");
    }

    [Fact]
    public void DeletingARevocationAfterItsClosingReopensNothing()
    {
        using var f = new HistoryFixture();
        f.Require();
        f.Adopt();
        RecordEnvelope closing = f.Close("device", f.BuilderDevice.Id);
        RecordEnvelope revocation = f.Revoke(f.BuilderDevice, f.BuilderAdmission);
        f.Remove(revocation);
        RecordEnvelope later = f.Estate.Add("work-order", f.Estate.BuilderId, HistoryFixture.Order("DEL-1", "After the revocation was deleted"), signer: f.BuilderSigner);
        EstateModel model = f.Load();
        Assert.Equal(SignatureChecks.AdmissionState.Admitted, SignatureChecks.AdmissionStateAt(model, f.BuilderDevice, later.At));
        Assert.False(model.Counts(model.ById[later.Id]));
        Assert.Contains(model.Findings, x => x.Code == "history.after-closing" && x.Location.StartsWith(later.ExpectedPath, StringComparison.Ordinal));

        // The closing still closes, and status says that no revocation follows it.
        Finding unfollowed = Assert.Single(model.Findings, x => x.Code == "history.closing-unfollowed");
        Assert.Equal(Severity.Warning, unfollowed.Severity);
        Assert.StartsWith(closing.ExpectedPath, unfollowed.Location, StringComparison.Ordinal);
        Assert.Contains("history.closing-unfollowed", EstateStatus.RenderJson(model, new FindingList()), StringComparison.Ordinal);
        Assert.Contains("history.after-closing", EstateStatus.HistoryJson(model)["problems"]!.ToJsonString(), StringComparison.Ordinal);
    }

    [Fact]
    public void AClosingSignedBySomeoneWhoMayNotPerformTheActClosesNothing()
    {
        using var f = new HistoryFixture();
        RecordEnvelope decision = f.Decision();
        // The agent holds administer, yet only a person holding it closes a device.
        RecordEnvelope grant = f.Grant(f.Estate.BuilderId, decision, [Scopes.Administer]);
        f.Require();
        f.Adopt();
        RecordEnvelope device = f.Close("device", f.OperatorDevice.Id, f.Estate.BuilderId, f.BuilderSigner);
        RecordEnvelope granted = f.Close("grant", grant.Id, f.Estate.BuilderId, f.BuilderSigner);
        RecordEnvelope key = f.Close("key", f.BuilderDevice.Id);
        RecordEnvelope byOperator = f.Estate.Add("work-order", f.Estate.OperatorId, HistoryFixture.Order("AUT-1", "By the operator"), signer: f.OperatorSigner);
        RecordEnvelope byBuilder = f.Estate.Add("principal", f.Estate.BuilderId, Helper, signer: f.BuilderSigner);
        EstateModel model = f.Load();
        Assert.Empty(model.Trust.Closings);
        foreach (RecordEnvelope closing in new[] { device, granted, key })
        {
            Assert.Contains(model.Findings, x => x.Code == "history.closing-author" && x.Location == closing.ExpectedPath + "#body.closes");
            Assert.True(model.Counts(model.ById[closing.Id]));
        }

        Assert.True(model.Counts(model.ById[byOperator.Id]));
        Assert.True(model.Counts(model.ById[byBuilder.Id]));
        Assert.DoesNotContain(model.Findings, x => x.Code == "principal.author");
        Assert.DoesNotContain(model.Findings, x => x.Code is "history.after-closing" or "history.closing-unfollowed");
        Assert.Equal(key.Id, Assert.Single(SignedHistory.Of(model).Heads).Id);
    }

    [Fact]
    public void ConcurrentClosingsEachAct()
    {
        using var f = new HistoryFixture();
        (RecordEnvelope second, _) = f.Admitted(f.Estate.OperatorId, "The operator's second device");
        (RecordEnvelope third, _) = f.Admitted(f.Estate.OperatorId, "The operator's third device");
        f.Require();
        f.Adopt();

        // Two copies each write a closing that follows the same head: one closes the second device, the other the first.
        EstateModel model = f.Load();
        CheckpointDraft one = SignedHistory.Prepare(model, "closing", closes: ("device", second.Id));
        CheckpointDraft two = SignedHistory.Prepare(model, "closing", closes: ("device", f.OperatorDevice.Id));
        f.StoreObjects(one);
        f.StoreObjects(two);
        RecordEnvelope first = f.Estate.Add("history-checkpoint", f.Estate.OperatorId, one.Body.GetRawText(), signer: f.OperatorSigner);
        RecordEnvelope other = f.Estate.Add("history-checkpoint", f.Estate.OperatorId, two.Body.GetRawText(), signer: f.Signer(second));
        RecordEnvelope byFirst = f.Estate.Add("work-order", f.Estate.OperatorId, HistoryFixture.Order("CON-1"), signer: f.OperatorSigner);
        RecordEnvelope bySecond = f.Estate.Add("work-order", f.Estate.OperatorId, HistoryFixture.Order("CON-2"), signer: f.Signer(second));
        RecordEnvelope byThird = f.Estate.Add("work-order", f.Estate.OperatorId, HistoryFixture.Order("CON-3"), signer: f.Signer(third));
        model = f.Load();
        Assert.Equal([first.Id, other.Id], model.Trust.Closings.Select(c => c.Record.Id).Order(StringComparer.Ordinal));
        Assert.False(model.Counts(model.ById[byFirst.Id]));
        Assert.False(model.Counts(model.ById[bySecond.Id]));
        Assert.True(model.Counts(model.ById[byThird.Id]));

        // Each closing is signed by a device the other closes and does not witness, so neither witnesses anything until anchored.
        Assert.False(model.Counts(model.ById[first.Id]));
        Assert.False(model.Counts(model.ById[other.Id]));
        Assert.Contains(model.Findings, x => x.Code == "history.after-closing" && x.Location.StartsWith(first.ExpectedPath, StringComparison.Ordinal));
        Assert.Contains(model.Findings, x => x.Code == "history.after-closing" && x.Location.StartsWith(other.ExpectedPath, StringComparison.Ordinal));
    }

    [Fact]
    public void TheRecoveryDeviceSignsTheClosingBeforeARevocationAndNoOther()
    {
        using var f = new HistoryFixture();
        RecordEnvelope recovery = f.RecoveryDevice();
        f.Require();
        f.Adopt();
        RecordEnvelope closing = f.Close("device", f.BuilderDevice.Id, signer: f.Signer(recovery));
        RecordEnvelope revocation = f.Revoke(f.BuilderDevice, f.BuilderAdmission, f.Signer(recovery));
        EstateModel model = f.Load();
        Assert.False(model.Findings.HasErrors, Errors(model));
        Assert.Equal(closing.Id, Assert.Single(model.Trust.Closings).Record.Id);
        Assert.True(model.Counts(model.ById[closing.Id]));
        Assert.True(model.Counts(model.ById[revocation.Id]));
        Assert.Contains(model.Findings, x => x.Code == "recovery.unreviewed" && x.Location == closing.ExpectedPath);

        RecordEnvelope keyClosing = f.Close("key", recovery.Id, signer: f.Signer(recovery));
        model = f.Load();
        Assert.Contains(model.Findings, x => x.Code == "signature.recovery-scope" && x.Location.StartsWith(keyClosing.ExpectedPath, StringComparison.Ordinal));
        Assert.False(model.Counts(model.ById[keyClosing.Id]));
        Assert.Single(model.Trust.Closings);
    }

    [Fact]
    public void ASignedRecordDatedWellAfterACheckpointWitnessingItIsQuestioned()
    {
        using var f = new HistoryFixture();
        f.Require();
        DateTimeOffset now = f.Estate.Clock;
        // Dated an hour ahead before adoption: once the adoption witnesses it, it keeps the reading it had then.
        f.Estate.SetClock(now.AddHours(1));
        RecordEnvelope found = f.Estate.Add("work-order", f.Estate.OperatorId, HistoryFixture.Order("DAW-1", "Found at adoption"), signer: f.OperatorSigner);
        f.Estate.SetClock(now.AddSeconds(5));
        f.Adopt();
        f.Estate.SetClock(now.AddMinutes(5).AddSeconds(20));
        RecordEnvelope near = f.Estate.Add("work-order", f.Estate.OperatorId, HistoryFixture.Order("DAW-2", "Within five minutes"), signer: f.OperatorSigner);
        f.Estate.SetClock(now.AddMinutes(30));
        RecordEnvelope ahead = f.Estate.Add("work-order", f.Estate.OperatorId, HistoryFixture.Order("DAW-3", "Half an hour ahead"), signer: f.OperatorSigner);
        f.Estate.SetClock(now.AddSeconds(30));
        RecordEnvelope checkpoint = f.Checkpoint();
        EstateModel model = f.Load();
        Assert.False(model.Findings.HasErrors, Errors(model));
        Finding questioned = Assert.Single(model.Findings, x => x.Code == "history.dated-after-witness");
        Assert.Equal(Severity.Warning, questioned.Severity);
        Assert.Equal(ahead.ExpectedPath, questioned.Location);
        Assert.Contains(checkpoint.Id, questioned.Message, StringComparison.Ordinal);
        Assert.True(model.Counts(model.ById[ahead.Id]));

        // A checkpoint that follows witnesses all it follows does: dated earlier still, it questions the nearer record too.
        f.Estate.SetClock(now.AddSeconds(10));
        RecordEnvelope earlier = f.Checkpoint();
        model = f.Load();
        Assert.Contains(model.Findings, x => x.Code == "history.dated-after-witness" && x.Location == near.ExpectedPath && x.Message.Contains(earlier.Id, StringComparison.Ordinal));
        Assert.DoesNotContain(model.Findings, x => x.Code == "history.dated-after-witness" && x.Location == found.ExpectedPath);
    }

    [Fact]
    public void WhatADateDecidesForRecordsThatCountIsUnchanged()
    {
        using var f = new HistoryFixture();
        f.Require();
        f.Adopt();
        // An anchored record dated into an earlier month keeps its path and its place in time order.
        f.Estate.SetClock(new DateTimeOffset(2026, 9, 30, 23, 0, 0, TimeSpan.Zero));
        RecordEnvelope early = f.Estate.Add("work-order", f.Estate.OperatorId, HistoryFixture.Order("TIM-1", "Dated into September"));
        f.Estate.SetClock(new DateTimeOffset(2026, 10, 14, 0, 0, 0, TimeSpan.Zero));
        f.Checkpoint(anchored: [early]);
        EstateModel model = f.Load();
        Assert.True(model.Counts(model.ById[early.Id]));
        Assert.StartsWith("records/work-order/2026-09/", early.ExpectedPath, StringComparison.Ordinal);
        Assert.Equal(early.Id, model.CountingRecords.Where(r => r.Type == "work-order").OrderBy(r => r.At, StringComparer.Ordinal).ThenBy(r => r.Id, StringComparer.Ordinal).First().Id);

        // The recorded time is the greatest date among records that count: a refused record dated a year ahead moves it nowhere.
        DateTimeOffset recorded = FlowStops.RecordedTime(model);
        f.Estate.SetClock(new DateTimeOffset(2027, 10, 14, 0, 0, 0, TimeSpan.Zero));
        RecordEnvelope unsigned = f.Estate.Add("work-order", f.Estate.OperatorId, HistoryFixture.Order("TIM-2", "Unsigned and a year ahead"));
        model = f.Load();
        Assert.False(model.Counts(model.ById[unsigned.Id]));
        Assert.Equal(recorded, FlowStops.RecordedTime(model));
    }

    [Fact]
    public void AClosingActsByItselfThoughACheckpointBeforeItDoesNotCount()
    {
        using var f = new HistoryFixture();
        RecordEnvelope decision = f.Decision();
        RecordEnvelope grant = f.Grant(f.Estate.BuilderId, decision, [Scopes.Administer]);
        f.Require();
        f.Adopt();
        // The operator's copy prepares the builder device's closing; meanwhile the builder writes an interval checkpoint, and a copy
        // holding it writes the grant's closing after it. Then the device's closing arrives.
        CheckpointDraft deviceClosing = SignedHistory.Prepare(f.Load(), "closing", closes: ("device", f.BuilderDevice.Id));
        f.StoreObjects(deviceClosing);
        RecordEnvelope interval = f.Checkpoint(f.Estate.BuilderId, f.BuilderSigner, "interval");
        RecordEnvelope closing = f.Close("grant", grant.Id);
        f.Grant(f.Estate.BuilderId, decision, [Scopes.LocalEngineering], supersedes: grant.Id);
        f.Estate.Add("history-checkpoint", f.Estate.OperatorId, deviceClosing.Body.GetRawText(), signer: f.OperatorSigner);
        EstateModel model = f.Load();

        // The interval checkpoint is the closed device's, outside what its closing witnesses, so it and the grant's closing after it
        // witness nothing; the grant's closing closes all the same.
        Assert.False(model.Counts(model.ById[interval.Id]));
        Assert.False(model.Counts(model.ById[closing.Id]));
        Assert.Contains(model.Findings, x => x.Code == "history.checkpoint-invalid" && x.Location.StartsWith(closing.ExpectedPath, StringComparison.Ordinal));
        Assert.Equal(2, model.Trust.Closings.Count);
        Assert.Contains(model.Trust.Closings, c => c.Record.Id == closing.Id && c.Kind == "grant");
    }

    [Fact]
    public void AClosingFromADeviceAdmittedOnlyThroughAnAnchorSaysItClosesNothing()
    {
        using var f = new HistoryFixture();
        f.Require();
        f.Adopt();
        RecordEnvelope device = f.Device(f.Estate.OperatorId, "Admitted through an anchor");
        RecordEnvelope admission = f.Estate.Add("admission", f.Estate.OperatorId, HistoryFixture.Admission(f.Estate.OperatorId, "Unsigned."), device.Reference);
        f.Checkpoint(anchored: [admission]);
        RecordEnvelope closing = f.Close("device", f.BuilderDevice.Id, signer: f.Signer(device));
        EstateModel model = f.Load();
        Assert.True(model.Counts(model.ById[closing.Id]));
        Assert.Empty(model.Trust.Closings);
        Assert.Contains(model.Findings, x => x.Code == "history.closing-author" && x.Location == closing.ExpectedPath + "#body.closes");
    }

    private static string Errors(EstateModel model) => string.Join('\n', model.Findings.Where(x => x.Severity == Severity.Error));
}
