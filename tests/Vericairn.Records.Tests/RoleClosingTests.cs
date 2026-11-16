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
/// Role closings (ID-14; design/signed-history.md § 8): removing a role from a person's principal record writes a closing for that
/// person, signed by the root operator or another standing operator. From it on, anything the person writes counts only when the closing
/// or a checkpoint it follows witnesses it, whatever its date, and is history.after-closing otherwise, in every read and for every
/// closing; what the person gave as operator before the closing, the appointments, grants and closings it witnesses, keeps counting for
/// the strongest acts, whoever is removed later. Estates that have not adopted are unchanged.
/// </summary>
public sealed class RoleClosingTests
{
    private static string Person(string name, params string[] roles) =>
        new JsonObject { ["kind"] = "human", ["name"] = name, ["roles"] = new JsonArray([.. roles.Select(r => (JsonNode)r)]) }.ToJsonString();

    private static string Decision(string by, string title) =>
        new JsonObject
        {
            ["title"] = title, ["statement"] = "Decide.", ["summary"] = "A decision.", ["synthesis"] = "For tests.", ["effects"] = new JsonArray("an effect"), ["affects"] = new JsonArray(),
            ["decidedBy"] = by, ["decidedAt"] = "2026-10-13T12:00:00Z", ["stake"] = "medium", ["notAuthorized"] = new JsonArray("publication"),
        }.ToJsonString();

    private static string Grant(string grantee, string basis, params string[] scopes) =>
        new JsonObject { ["grantee"] = grantee, ["scope"] = new JsonArray([.. scopes.Select(s => (JsonNode)s)]), ["reserved"] = new JsonArray(), ["conditions"] = new JsonArray(), ["basis"] = basis }.ToJsonString();

    /// <summary>
    /// An estate whose root operator appointed a person, P, as operator with an admitted device; P appointed Q an operator, granted Q
    /// administer on a decision of P's, and Q has an admitted device; the estate then requires signatures and adopts unless asked not to.
    /// </summary>
    private sealed class Estate : IDisposable
    {
        public Estate(bool adopt = true)
        {
            F = new HistoryFixture();
            string op = F.Estate.OperatorId;
            P = F.Estate.Add("principal", op, Person("P", "operator", "reviewer"), signer: F.OperatorSigner);
            (PDevice, _) = F.Admitted(P.Id, "P's device");
            PSigner = F.Signer(PDevice);
            Q = F.Estate.Add("principal", P.Id, Person("Q", "operator"), signer: PSigner);
            (QDevice, _) = F.Admitted(Q.Id, "Q's device");
            QSigner = F.Signer(QDevice);
            Basis = F.Estate.Add("decision", P.Id, Decision(P.Id, "P's basis"), signer: PSigner);
            Administer = F.Estate.Add("authority", P.Id, Grant(Q.Id, Basis.Id, Scopes.Administer), signer: PSigner);
            F.Require();
            if (adopt) F.Adopt();
        }

        public HistoryFixture F { get; }

        public RecordEnvelope P { get; }

        public RecordEnvelope PDevice { get; }

        public StoreSigner PSigner { get; }

        public RecordEnvelope Q { get; }

        public RecordEnvelope QDevice { get; }

        public StoreSigner QSigner { get; }

        public RecordEnvelope Basis { get; }

        public RecordEnvelope Administer { get; }

        /// <summary>The root operator's removal of P's operator role, keeping reviewer, after P's role closing unless asked not to.</summary>
        public (RecordEnvelope? Closing, RecordEnvelope Removal) Remove(bool close = true, string? by = null, IRecordSigner? signer = null)
        {
            RecordEnvelope? closing = close ? F.Close("role", P.Id, by, signer) : null;
            RecordEnvelope removal = F.Estate.Add("principal", by ?? F.Estate.OperatorId, Person("P", "reviewer"), supersedes: P.Id, signer: signer ?? F.OperatorSigner);
            return (closing, removal);
        }

        public void Dispose() => F.Dispose();
    }

    private static Finding AfterClosing(EstateModel model, RecordEnvelope record) =>
        Assert.Single(model.Findings, x => x.Code == "history.after-closing" && x.Location.StartsWith(record.ExpectedPath, StringComparison.Ordinal));

    [Fact]
    public void AFormerOperatorsBackdatedDecisionGrantAndPrincipalRevisionAfterTheClosingCountForNothing()
    {
        using var e = new Estate();
        HistoryFixture f = e.F;
        RecordEnvelope before = f.Estate.Add("decision", e.P.Id, Decision(e.P.Id, "Before the closing"), signer: e.PSigner);
        (RecordEnvelope? closing, RecordEnvelope removal) = e.Remove();
        EstateModel model = f.Load();
        Assert.False(model.Findings.HasErrors, string.Join('\n', model.Findings.Where(x => x.Severity == Severity.Error)));
        Closing acting = Assert.Single(model.Trust.Closings);
        Assert.Equal(("role", e.P.Id, e.P.Id), (acting.Kind, acting.Target, acting.Identity));
        Assert.DoesNotContain(model.Findings, x => x.Code is "history.closing-unfollowed" or "history.closing-missing");
        Assert.True(model.Counts(before));
        Assert.False(model.HasRole(e.P.Id, Roles.Operator));

        // Written after the closing with P's admitted device, dated into the time P held the operator role.
        f.Estate.SetClock(before.AtTime.AddSeconds(-5));
        RecordEnvelope decision = f.Estate.Add("decision", e.P.Id, Decision(e.P.Id, "Backdated"), signer: e.PSigner);
        RecordEnvelope grant = f.Estate.Add("authority", e.P.Id, Grant(e.Q.Id, e.Basis.Id, Scopes.AcceptWork), signer: e.PSigner);
        RecordEnvelope appointment = f.Estate.Add("principal", e.P.Id, Person("Z", "operator"), signer: e.PSigner);
        RecordEnvelope demotion = f.Estate.Add("principal", e.P.Id, Person("Q", "reviewer"), supersedes: e.Q.Id, signer: e.PSigner);
        RecordEnvelope admission = f.Estate.Add("admission", e.P.Id, HistoryFixture.Admission(e.P.Id, "Backdated admission."), f.Device(e.P.Id, "Another device of P").Reference, signer: e.PSigner);
        model = f.Load();
        foreach (RecordEnvelope refused in new[] { decision, grant, appointment, demotion, admission })
        {
            Assert.False(model.Counts(model.ById[refused.Id]), refused.Type);
            Finding finding = AfterClosing(model, refused);
            Assert.Contains(closing!.Id, finding.Message, StringComparison.Ordinal);
            Assert.Contains(e.P.Id, finding.Message, StringComparison.Ordinal);
            // P keeps the reviewer role, so the refusal names the rule, not a role the closed revision gave.
            Assert.Contains("a role closing refuses everything its person writes unless the closing", finding.Message, StringComparison.Ordinal);
            Assert.DoesNotContain("gave them the role", finding.Message, StringComparison.Ordinal);
        }

        // Nothing it wrote takes effect: Q keeps the operator role, Z has none, and the grant gives nothing.
        Assert.True(model.HasRole(e.Q.Id, Roles.Operator));
        Assert.False(model.HasRole(appointment.Id, Roles.Operator));
        Assert.False(model.HasScope(e.Q.Id, Scopes.AcceptWork));
        Assert.True(model.Counts(model.ById[before.Id]));
        Assert.Contains(EstateStatus.HistoryProblems(model), x => x.Code == "history.after-closing" && x.Location.StartsWith(decision.ExpectedPath, StringComparison.Ordinal));

        // Records of P's dated at the removal, after it and long after it are refused the same way: whatever its date.
        f.Estate.SetClock(removal.AtTime.AddSeconds(-1));
        RecordEnvelope atRemoval = f.Estate.Add("work-order", e.P.Id, HistoryFixture.Order("RCL-1", "At the removal"), signer: e.PSigner);
        RecordEnvelope later = f.Estate.Add("work-order", e.P.Id, HistoryFixture.Order("RCL-2", "After the removal"), signer: e.PSigner);
        f.Estate.SetClock(removal.AtTime.AddDays(40));
        RecordEnvelope muchLater = f.Estate.Add("decision", e.P.Id, Decision(e.P.Id, "Long after the removal"), signer: e.PSigner);
        model = f.Load();
        Assert.Equal(removal.At, atRemoval.At);
        foreach (RecordEnvelope refused in new[] { atRemoval, later, muchLater })
        {
            Assert.False(model.Counts(model.ById[refused.Id]));
            AfterClosing(model, refused);
        }

        // The root operator anchors one after reading it; it then counts, and the others stay refused.
        f.Checkpoint(anchored: [decision]);
        model = f.Load();
        Assert.True(model.Counts(model.ById[decision.Id]));
        Assert.False(model.Counts(model.ById[grant.Id]));
    }

    [Fact]
    public void WithoutTheClosingTheSameBackdatedRecordsCountAndTheRemovalIsWarned()
    {
        // The reading role closings end: a removal with no closing leaves P's backdated records counting, as they stood.
        using var e = new Estate();
        HistoryFixture f = e.F;
        RecordEnvelope before = f.Estate.Add("decision", e.P.Id, Decision(e.P.Id, "Before the removal"), signer: e.PSigner);
        (_, RecordEnvelope removal) = e.Remove(close: false);
        f.Estate.SetClock(before.AtTime.AddSeconds(-5));
        RecordEnvelope decision = f.Estate.Add("decision", e.P.Id, Decision(e.P.Id, "Backdated"), signer: e.PSigner);
        EstateModel model = f.Load();
        Assert.True(model.Counts(model.ById[decision.Id]));
        Finding missing = Assert.Single(model.Findings, x => x.Code == "history.closing-missing");
        Assert.Equal(removal.ExpectedPath, missing.Location);
        Assert.Contains("role", missing.Message, StringComparison.Ordinal);
        Assert.Contains("the closings and the appointments and grants the person wrote before the removal stop acting", missing.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AnEstateThatHasNotAdoptedIsUnchanged()
    {
        using var e = new Estate(adopt: false);
        HistoryFixture f = e.F;
        RecordEnvelope before = f.Estate.Add("decision", e.P.Id, Decision(e.P.Id, "Before the removal"), signer: e.PSigner);
        e.Remove(close: false);
        f.Estate.SetClock(before.AtTime.AddSeconds(-5));
        RecordEnvelope decision = f.Estate.Add("decision", e.P.Id, Decision(e.P.Id, "Backdated"), signer: e.PSigner);
        EstateModel model = f.Load();
        Assert.False(model.Trust.Adopted);
        Assert.True(model.Counts(model.ById[decision.Id]));
        Assert.DoesNotContain(model.Findings, x => x.Code is "history.closing-missing" or "history.after-closing");
    }

    [Fact]
    public void WhatTheRemovedOperatorGaveBeforeTheClosingKeepsCountingForTheStrongestActs()
    {
        // With the closing, Q's appointment and administer grant by P, both witnessed by it, keep counting for anchoring and closings.
        using var closed = new Estate();
        closed.Remove();
        HistoryFixture f = closed.F;
        RecordEnvelope device = f.Device(f.Estate.OperatorId, "A device to close");
        EstateModel model = f.Load();
        Assert.True(model.Trust.AnchorerAt(closed.Q.Id, RecordParser.FormatTimestamp(f.Estate.Clock)));
        RecordEnvelope byQ = f.Close("device", device.Id, closed.Q.Id, closed.QSigner);
        model = f.Load();
        Assert.Contains(model.Trust.Closings, c => c.Record.Id == byQ.Id);
        Assert.DoesNotContain(model.Findings, x => x.Code is "history.closing-author" or "history.revision-author");

        // Without it, as before role closings, both read as removals for those acts: Q neither anchors nor closes.
        using var open = new Estate();
        open.Remove(close: false);
        HistoryFixture g = open.F;
        RecordEnvelope other = g.Device(g.Estate.OperatorId, "A device to close");
        model = g.Load();
        Assert.False(model.Trust.AnchorerAt(open.Q.Id, RecordParser.FormatTimestamp(g.Estate.Clock)));
        RecordEnvelope refused = g.Close("device", other.Id, open.Q.Id, open.QSigner);
        model = g.Load();
        Assert.DoesNotContain(model.Trust.Closings, c => c.Record.Id == refused.Id);
        Assert.Contains(model.Findings, x => x.Code == "history.closing-author" && x.Location.StartsWith(refused.ExpectedPath, StringComparison.Ordinal));
    }

    [Fact]
    public void AClosingRefusesThePersonsLaterRecordsWhetherOrNotItsRemovalHasFollowed()
    {
        using var e = new Estate();
        HistoryFixture f = e.F;
        RecordEnvelope closing = f.Close("role", e.P.Id);
        RecordEnvelope after = f.Estate.Add("work-order", e.P.Id, HistoryFixture.Order("RCL-3", "After the closing, no removal yet"), signer: e.PSigner);
        EstateModel model = f.Load();
        Assert.Single(model.Trust.Closings);
        Assert.False(model.Counts(after));
        Assert.Contains(model.Findings, x => x.Code == "history.closing-unfollowed" && x.Location.StartsWith(closing.ExpectedPath, StringComparison.Ordinal));

        // The removal follows it, and a record of P's after the removal is refused like the one before it.
        f.Estate.Add("principal", f.Estate.OperatorId, Person("P", "reviewer"), supersedes: e.P.Id, signer: f.OperatorSigner);
        RecordEnvelope later = f.Estate.Add("work-order", e.P.Id, HistoryFixture.Order("RCL-4", "After the removal"), signer: e.PSigner);
        model = f.Load();
        Assert.DoesNotContain(model.Findings, x => x.Code == "history.closing-unfollowed");
        Assert.False(model.Counts(model.ById[after.Id]));
        Assert.False(model.Counts(model.ById[later.Id]));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ARemovedPersonsRevisionNeitherTakesAnotherOperatorsRoleNorVoidsTheirClosing(bool backdated)
    {
        // O1, an operator the root operator appointed, closes a device. The root operator then closes and removes P's role; P's still
        // admitted device signs a revision of O1's line taking the operator role away, dated after the removal or back into P's time as
        // operator. In every read it counts for nothing: O1 keeps the role, and O1's earlier closing, judged before P's role closing in
        // checkpoint order, still acts.
        using var e = new Estate();
        HistoryFixture f = e.F;
        RecordEnvelope o1 = f.Estate.Add("principal", f.Estate.OperatorId, Person("O1", "operator"), signer: f.OperatorSigner);
        (RecordEnvelope o1Device, _) = f.Admitted(o1.Id, "O1's device");
        RecordEnvelope target = f.Device(f.Estate.OperatorId, "A device O1 closes");
        RecordEnvelope o1Closing = f.Close("device", target.Id, o1.Id, f.Signer(o1Device));
        RecordEnvelope before = f.Estate.Add("decision", e.P.Id, Decision(e.P.Id, "P's before the closing"), signer: e.PSigner);
        (RecordEnvelope? closing, RecordEnvelope removal) = e.Remove();
        f.Estate.SetClock(backdated ? before.AtTime.AddSeconds(-2) : removal.AtTime.AddMinutes(10));
        RecordEnvelope taking = f.Estate.Add("principal", e.P.Id, Person("O1", "reviewer"), supersedes: o1.Id, signer: e.PSigner);

        EstateModel model = f.Load();
        Assert.False(model.Counts(model.ById[taking.Id]));
        Assert.Contains(closing!.Id, AfterClosing(model, taking).Message, StringComparison.Ordinal);
        Assert.True(model.HasRole(o1.Id, Roles.Operator));
        Assert.Contains(model.Trust.Closings, c => c.Record.Id == o1Closing.Id);
        Assert.DoesNotContain(model.Findings, x => x.Code == "history.closing-author" && x.Location.StartsWith(o1Closing.ExpectedPath, StringComparison.Ordinal));
        Assert.True(model.Trust.AnchorerAt(f.Estate.OperatorId, RecordParser.FormatTimestamp(f.Estate.Clock)));
    }

    [Fact]
    public void ARoleClosingAndTheAuthorsOtherClosingsKeepActingWhenTheAuthorIsRemovedWithTheirOwnRoleClosing()
    {
        // O1, appointed by the root operator, closes and removes P's role and closes a device. The root operator later closes and removes
        // O1's role. O1's closings keep the force they had when signed: P's backdated record and the closed device's record stay refused.
        using var e = new Estate();
        HistoryFixture f = e.F;
        RecordEnvelope o1 = f.Estate.Add("principal", f.Estate.OperatorId, Person("O1", "operator"), signer: f.OperatorSigner);
        (RecordEnvelope o1Device, _) = f.Admitted(o1.Id, "O1's device");
        StoreSigner o1Signer = f.Signer(o1Device);
        (RecordEnvelope closedDevice, RecordEnvelope closedAdmission) = f.Admitted(f.Estate.OperatorId, "A device O1 closes");
        RecordEnvelope before = f.Estate.Add("decision", e.P.Id, Decision(e.P.Id, "P's before the closing"), signer: e.PSigner);
        (RecordEnvelope? pClosing, _) = e.Remove(by: o1.Id, signer: o1Signer);
        RecordEnvelope deviceClosing = f.Close("device", closedDevice.Id, o1.Id, o1Signer);
        f.Revoke(closedDevice, closedAdmission, o1Signer, o1.Id);
        RecordEnvelope o1RoleClosing = f.Close("role", o1.Id);
        f.Estate.Add("principal", f.Estate.OperatorId, Person("O1", "reviewer"), supersedes: o1.Id, signer: f.OperatorSigner);

        f.Estate.SetClock(before.AtTime.AddSeconds(-2));
        RecordEnvelope backdated = f.Estate.Add("decision", e.P.Id, Decision(e.P.Id, "Backdated"), signer: e.PSigner);
        RecordEnvelope byClosedDevice = f.Estate.Add("work-order", f.Estate.OperatorId, HistoryFixture.Order("RCL-7", "Signed by the closed device"), signer: f.Signer(closedDevice));
        EstateModel model = f.Load();
        Assert.False(model.HasRole(o1.Id, Roles.Operator));
        Assert.Equal(new[] { pClosing!.Id, deviceClosing.Id, o1RoleClosing.Id }.Order(StringComparer.Ordinal), model.Trust.Closings.Select(c => c.Record.Id).Order(StringComparer.Ordinal));
        Assert.DoesNotContain(model.Findings, x => x.Code == "history.closing-author");
        Assert.False(model.Counts(model.ById[backdated.Id]));
        Assert.False(model.Counts(model.ById[byClosedDevice.Id]));
    }

    [Theory]
    [InlineData("builder")]
    [InlineData("self")]
    [InlineData("root")]
    public void ARoleClosingByOneWhoMayNotRemoveTheRoleClosesNothing(string who)
    {
        using var e = new Estate();
        HistoryFixture f = e.F;
        RecordEnvelope closing = who switch
        {
            "builder" => f.Close("role", e.P.Id, f.Estate.BuilderId, f.BuilderSigner),
            "self" => f.Close("role", e.P.Id, e.P.Id, e.PSigner),
            _ => f.Close("role", f.Estate.OperatorId, e.Q.Id, e.QSigner),
        };
        RecordEnvelope after = f.Estate.Add("work-order", e.P.Id, HistoryFixture.Order("RCL-6", "After a closing that closes nothing"), signer: e.PSigner);
        EstateModel model = f.Load();
        Assert.Empty(model.Trust.Closings);
        Assert.Contains(model.Findings, x => x.Code == "history.closing-author" && x.Location.StartsWith(closing.ExpectedPath, StringComparison.Ordinal));
        Assert.True(model.Counts(after));
    }

    [Fact]
    public void AnotherStandingOperatorMayCloseAndRemoveTheRole()
    {
        using var e = new Estate();
        HistoryFixture f = e.F;
        RecordEnvelope o = f.Estate.Add("principal", f.Estate.OperatorId, Person("O", "operator"), signer: f.OperatorSigner);
        (RecordEnvelope oDevice, _) = f.Admitted(o.Id, "O's device");
        RecordEnvelope before = f.Estate.Add("decision", e.P.Id, Decision(e.P.Id, "Before"), signer: e.PSigner);
        (RecordEnvelope? closing, RecordEnvelope removal) = e.Remove(by: o.Id, signer: f.Signer(oDevice));
        f.Estate.SetClock(before.AtTime.AddSeconds(-5));
        RecordEnvelope backdated = f.Estate.Add("decision", e.P.Id, Decision(e.P.Id, "Backdated"), signer: e.PSigner);
        EstateModel model = f.Load();
        Closing acting = Assert.Single(model.Trust.Closings);
        Assert.Equal(closing!.Id, acting.Record.Id);
        Assert.False(model.Counts(model.ById[backdated.Id]));
        Assert.True(model.Counts(model.ById[before.Id]));
        Assert.False(model.HasRole(e.P.Id, Roles.Operator));
    }

    [Fact]
    public void AnOperatorWhoseStandingRestsOnThePersonClosesNothingWithTheRemoval()
    {
        // Q's operator role rests on P's appointment. Once Q removes P, P's appointment of Q is kept by no closing judged before Q's own,
        // and no closing is read against itself, so Q's closing closes nothing: the root operator, or an operator whose role does not rest
        // on P, closes P's role.
        using var e = new Estate();
        HistoryFixture f = e.F;
        (RecordEnvelope? closing, _) = e.Remove(by: e.Q.Id, signer: e.QSigner);
        EstateModel model = f.Load();
        Assert.Empty(model.Trust.Closings);
        Assert.Contains(model.Findings, x => x.Code == "history.closing-author" && x.Location.StartsWith(closing!.ExpectedPath, StringComparison.Ordinal));
    }

    [Fact]
    public void AnUnsignedRecordOfThePersonUnderAnOptionalCharterIsRefusedToo()
    {
        using var f = new HistoryFixture();
        string op = f.Estate.OperatorId;
        RecordEnvelope p = f.Estate.Add("principal", op, Person("P", "operator"), signer: f.OperatorSigner);
        f.Adopt();
        f.Close("role", p.Id);
        RecordEnvelope removal = f.Estate.Add("principal", op, Person("P", "reviewer"), supersedes: p.Id, signer: f.OperatorSigner);
        f.Estate.SetClock(removal.AtTime.AddSeconds(-30));
        RecordEnvelope unsigned = f.Estate.Add("decision", p.Id, Decision(p.Id, "Unsigned and backdated"));
        EstateModel model = f.Load();
        Assert.False(model.Trust.Required);
        Assert.False(model.Counts(model.ById[unsigned.Id]));
        AfterClosing(model, unsigned);
    }

    [Fact]
    public void ClosingActsNameARoleRemovalOfAPersonOnly()
    {
        using var e = new Estate();
        EstateModel model = e.F.Load();
        using var removes = System.Text.Json.JsonDocument.Parse(Person("P", "reviewer"));
        using var keeps = System.Text.Json.JsonDocument.Parse(Person("P", "operator", "reviewer", "builder"));
        using var agent = System.Text.Json.JsonDocument.Parse("""{"kind":"agent","name":"Test Builder","roles":[],"agent":{"vendor":"Example","product":"Example Agent"}}""");
        Assert.Equal(("role", e.P.Id), ClosingActs.For(model, "principal", removes.RootElement, null, e.P.Id));
        Assert.Null(ClosingActs.For(model, "principal", keeps.RootElement, null, e.P.Id));
        // An agent's record is no person's, and the root operator's role is never closed.
        Assert.Null(ClosingActs.For(model, "principal", agent.RootElement, null, e.F.Estate.BuilderId));
        Assert.Null(ClosingActs.For(model, "principal", removes.RootElement, null, e.F.Estate.OperatorId));
        Assert.True(ClosingActs.RemovesRole(keeps.RootElement, removes.RootElement));
        Assert.False(ClosingActs.RemovesRole(removes.RootElement, keeps.RootElement));
    }
}
