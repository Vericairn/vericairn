// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json.Nodes;
using Vericairn.Records.Estate;
using Vericairn.Records.History;
using Vericairn.Records.Model;
using Vericairn.Records.Types;

namespace Vericairn.Records.Tests;

/// <summary>
/// Which grant and principal revisions authority reads accept. In every read, none dated before the record below it in its line, and a
/// revision on top of such a one follows the line there. For the strongest acts (anchoring, rooting an adoption, signing a closing), a
/// revision is read as written when the root operator or an operator at its time who stands now wrote it, as a removal when its author was
/// given the operator role by then but had lost it or does not stand now, and not at all when its author was never given the role.
/// </summary>
public class AuthorityRevisionTests
{
    private static string Person(string name, string role) => $$"""{"kind":"human","name":"{{name}}","roles":["{{role}}"]}""";

    private static string GrantBody(string grantee, RecordEnvelope basis, params string[] scopes) =>
        new JsonObject { ["grantee"] = grantee, ["scope"] = new JsonArray([.. scopes.Select(s => (JsonNode)s)]), ["reserved"] = new JsonArray(), ["conditions"] = new JsonArray(), ["basis"] = basis.Id }.ToJsonString();

    private static bool Acts(EstateModel model, RecordEnvelope closing) => model.Trust.Closings.Any(c => c.Record.Id == closing.Id);

    private static bool ReportedAsRemoval(EstateModel model, RecordEnvelope record) =>
        model.Findings.Any(x => x.Code == "history.revision-author" && x.Severity == Severity.Warning && x.Location == record.ExpectedPath);

    /// <summary>
    /// An adopted estate with a second operator, appointed by the root operator and holding an admitted device, whose role the root
    /// operator removes an hour later; returns the person, their device, when they were appointed and the removal.
    /// </summary>
    private static (RecordEnvelope Person, RecordEnvelope Device, DateTimeOffset Appointed, RecordEnvelope Removal) FormerOperator(HistoryFixture f)
    {
        RecordEnvelope person = f.Estate.Add("principal", f.Estate.OperatorId, Person("Second person", Roles.Operator), signer: f.OperatorSigner);
        (RecordEnvelope device, _) = f.Admitted(person.Id, "The second person's device");
        DateTimeOffset appointed = f.Estate.Clock;
        f.Estate.SetClock(f.Estate.Clock.AddHours(1));
        f.Require();
        f.Adopt();
        RecordEnvelope removal = f.Estate.Add("principal", f.Estate.OperatorId, Person("Second person", Roles.Reviewer), supersedes: person.Id, signer: f.OperatorSigner);
        return (person, device, appointed, removal);
    }

    [Fact]
    public void APrincipalRevisionDatedBeforeTheRecordItSupersedesCountsForNothing()
    {
        using var f = new HistoryFixture();
        RecordEnvelope person = f.Estate.Add("principal", f.Estate.OperatorId, Person("Third person", Roles.Operator), signer: f.OperatorSigner);
        DateTimeOffset first = f.Estate.Clock;
        f.Estate.SetClock(first.AddHours(1));
        RecordEnvelope reviewer = f.Estate.Add("principal", f.Estate.OperatorId, Person("Third person", Roles.Reviewer), supersedes: person.Id, signer: f.OperatorSigner);
        DateTimeOffset resume = f.Estate.Clock;
        f.Estate.SetClock(first.AddMinutes(10));
        RecordEnvelope earlier = f.Estate.Add("principal", f.Estate.OperatorId, Person("Third person", Roles.Operator), supersedes: reviewer.Id, signer: f.OperatorSigner);
        f.Estate.SetClock(resume);
        EstateModel model = f.Load();
        Assert.True(model.Trust.AuthenticBySignatures(model.ById[earlier.Id]));
        Assert.False(model.Counts(model.ById[earlier.Id]));
        Assert.Contains(model.Findings, x => x.Code == "record.supersedes" && x.Location == earlier.ExpectedPath);
        Assert.Equal(reviewer.Id, model.CurrentPrincipal(person.Id)!.Id);
        Assert.False(model.HasRole(person.Id, Roles.Operator));
    }

    [Fact]
    public void AGrantRevisionDatedBeforeTheGrantItSupersedesCountsForNothing()
    {
        using var f = new HistoryFixture();
        RecordEnvelope decision = f.Decision();
        // Both revisions are dated after the operator's device was admitted, so its signature is not what refuses the earlier one.
        f.Estate.SetClock(f.Estate.Clock.AddMinutes(10));
        RecordEnvelope grant = f.Grant(f.Estate.BuilderId, decision, [Scopes.LocalEngineering]);
        f.Estate.SetClock(grant.AtTime.AddMinutes(-5));
        RecordEnvelope earlier = f.Grant(f.Estate.BuilderId, decision, [Scopes.Administer], supersedes: grant.Id);
        EstateModel model = f.Load();
        Assert.True(model.Trust.AuthenticBySignatures(model.ById[earlier.Id]));
        Assert.False(model.Counts(model.ById[earlier.Id]));
        Assert.Contains(model.Findings, x => x.Code == "record.supersedes" && x.Location == earlier.ExpectedPath);
        Assert.False(model.HasScope(f.Estate.BuilderId, Scopes.Administer));
        Assert.True(model.HasScope(f.Estate.BuilderId, Scopes.LocalEngineering));
        Assert.True(model.IsCurrent(model.ById[grant.Id]));
    }

    [Fact]
    public void APrincipalRevisionOnTopOfARefusedOneFollowsTheLineBelowItInEveryRead()
    {
        using var f = new HistoryFixture();
        RecordEnvelope person = f.Estate.Add("principal", f.Estate.OperatorId, Person("Third person", Roles.Operator), signer: f.OperatorSigner);
        (RecordEnvelope device, _) = f.Admitted(person.Id, "The third person's device");
        DateTimeOffset first = f.Estate.Clock;
        f.Require();
        f.Adopt();
        f.Estate.SetClock(first.AddHours(1));
        RecordEnvelope removal = f.Estate.Add("principal", f.Estate.OperatorId, Person("Third person", Roles.Reviewer), supersedes: person.Id, signer: f.OperatorSigner);
        f.Estate.SetClock(first.AddMinutes(10));
        RecordEnvelope refused = f.Estate.Add("principal", f.Estate.OperatorId, Person("Third person", Roles.Reviewer), supersedes: removal.Id, signer: f.OperatorSigner);
        // On top of the refused revision but still dated before the removal: compared with the removal, it is refused as well.
        RecordEnvelope stillEarlier = f.Estate.Add("principal", f.Estate.OperatorId, Person("Third person", Roles.Operator), supersedes: refused.Id, signer: f.OperatorSigner);
        f.Estate.SetClock(first.AddHours(2));
        RecordEnvelope again = f.Estate.Add("principal", f.Estate.OperatorId, Person("Third person", Roles.Operator), supersedes: refused.Id, signer: f.OperatorSigner);
        RecordEnvelope closing = f.Close("device", f.BuilderDevice.Id, person.Id, f.Signer(device));
        EstateModel model = f.Load();

        Assert.False(model.Counts(model.ById[refused.Id]));
        Assert.False(model.Counts(model.ById[stillEarlier.Id]));
        Assert.True(model.Counts(model.ById[again.Id]));
        Assert.Equal(again.Id, model.CurrentPrincipal(person.Id)!.Id);
        Assert.True(model.IsCurrent(model.ById[again.Id]));
        Assert.False(model.IsCurrent(model.ById[removal.Id]));
        Assert.Equal([again.Id], model.Current("principal").Where(p => model.Chain(p)[0] == person.Id).Select(p => p.Id));
        Assert.True(model.HasRole(person.Id, Roles.Operator));
        Assert.True(model.HasRoleAt(person.Id, Roles.Operator, model.ById[closing.Id]));
        Assert.True(Acts(model, closing));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AGrantRevisionOnTopOfARefusedOneFollowsTheLineBelowItInEveryRead(bool narrows)
    {
        using var f = new HistoryFixture();
        RecordEnvelope decision = f.Decision();
        RecordEnvelope administrator = f.Estate.Add("principal", f.Estate.OperatorId, Person("Administrator", Roles.Reviewer), signer: f.OperatorSigner);
        (RecordEnvelope device, _) = f.Admitted(administrator.Id, "The administrator's device");
        f.Estate.SetClock(f.Estate.Clock.AddMinutes(10));
        RecordEnvelope grant = f.Grant(administrator.Id, decision, [Scopes.Administer]);
        f.Estate.SetClock(grant.AtTime.AddMinutes(-5));
        RecordEnvelope refused = f.Grant(administrator.Id, decision, [Scopes.LocalEngineering], supersedes: grant.Id);
        f.Estate.SetClock(grant.AtTime.AddMinutes(5));
        RecordEnvelope onTop = f.Grant(administrator.Id, decision, narrows ? [Scopes.LocalEngineering] : [Scopes.Administer, Scopes.LocalEngineering], supersedes: refused.Id);
        f.Require();
        f.Adopt();
        RecordEnvelope unsigned = f.Estate.Add("work-order", f.Estate.OperatorId, HistoryFixture.Order("AUR-2", "Unsigned"));
        RecordEnvelope anchoring = f.Checkpoint(administrator.Id, f.Signer(device), anchored: [unsigned]);
        EstateModel model = f.Load();

        Assert.False(model.Counts(model.ById[refused.Id]));
        Assert.True(model.Counts(model.ById[onTop.Id]));
        Assert.True(model.IsCurrent(model.ById[onTop.Id]));
        Assert.False(model.IsCurrent(model.ById[grant.Id]));
        Assert.Equal([onTop.Id], model.Current("authority").Select(g => g.Id));
        Assert.Equal(!narrows, model.HasScope(administrator.Id, Scopes.Administer));
        Assert.Equal(!narrows, model.HasScopeAt(administrator.Id, Scopes.Administer, model.ById[anchoring.Id]));
        Assert.Equal(!narrows, model.Trust.MayAnchor(model.ById[anchoring.Id]));
        Assert.Equal(!narrows, model.Counts(model.ById[unsigned.Id]));
    }

    [Fact]
    public void AGrantFromAnOperatorWhoNoLongerStandsGivesNoAnchoring()
    {
        using var f = new HistoryFixture();
        RecordEnvelope decision = f.Decision();
        RecordEnvelope administrator = f.Estate.Add("principal", f.Estate.OperatorId, Person("Administrator", Roles.Reviewer), signer: f.OperatorSigner);
        (RecordEnvelope administratorDevice, _) = f.Admitted(administrator.Id, "The administrator's device");
        (RecordEnvelope person, RecordEnvelope device, DateTimeOffset appointed, _) = FormerOperator(f);
        RecordEnvelope unsigned = f.Estate.Add("work-order", f.Estate.OperatorId, HistoryFixture.Order("AUR-1", "Unsigned"));
        DateTimeOffset resume = f.Estate.Clock;

        // A grant of administer written by the former operator, dated into the time the role stood and after the device's admission.
        f.Estate.SetClock(appointed.AddMinutes(10));
        RecordEnvelope grant = f.Estate.Add("authority", person.Id, GrantBody(administrator.Id, decision, Scopes.Administer), signer: f.Signer(device));
        f.Estate.SetClock(resume);
        RecordEnvelope anchoring = f.Checkpoint(administrator.Id, f.Signer(administratorDevice), anchored: [unsigned]);
        RecordEnvelope closing = f.Close("device", f.OperatorDevice.Id, administrator.Id, f.Signer(administratorDevice));
        EstateModel model = f.Load();
        Assert.True(model.Trust.AuthenticBySignatures(model.ById[grant.Id]));
        Assert.True(model.Counts(model.ById[grant.Id]));
        Assert.False(model.Trust.MayAnchor(model.ById[anchoring.Id]));
        Assert.False(model.Counts(model.ById[unsigned.Id]));
        Assert.Contains(model.Findings, x => x.Code == "history.anchor-author" && x.Location.StartsWith(anchoring.ExpectedPath, StringComparison.Ordinal));
        Assert.False(Acts(model, closing));
        Assert.True(ReportedAsRemoval(model, grant));
    }

    [Fact]
    public void AnOperatorRoleFromAnOperatorWhoNoLongerStandsGivesNoneOfTheStrongestActs()
    {
        using var f = new HistoryFixture();
        (RecordEnvelope person, RecordEnvelope device, DateTimeOffset appointed, _) = FormerOperator(f);
        DateTimeOffset resume = f.Estate.Clock;
        // An appointment written by the former operator, dated into the time the role stood and after the device's admission.
        f.Estate.SetClock(appointed.AddMinutes(10));
        RecordEnvelope appointee = f.Estate.Add("principal", person.Id, Person("Appointee", Roles.Operator), signer: f.Signer(device));
        f.Estate.SetClock(resume);
        (RecordEnvelope appointeeDevice, _) = f.Admitted(appointee.Id, "The appointee's device");
        RecordEnvelope closing = f.Close("device", f.OperatorDevice.Id, appointee.Id, f.Signer(appointeeDevice));
        EstateModel model = f.Load();
        Assert.True(model.Trust.AuthenticBySignatures(model.ById[appointee.Id]));
        Assert.False(Acts(model, closing));
        Assert.Contains(model.Findings, x => x.Code == "history.closing-author" && x.Location == closing.ExpectedPath + "#body.closes");
        Assert.True(ReportedAsRemoval(model, appointee));
    }

    [Fact]
    public void TwoPersonsWhoAppointEachOtherGiveEachOtherNothing()
    {
        using var f = new HistoryFixture();
        (RecordEnvelope former, RecordEnvelope formerDevice, DateTimeOffset appointed, RecordEnvelope removal) = FormerOperator(f);
        DateTimeOffset resume = f.Estate.Clock;
        // The former operator dates an appointment of a second person into the time the role stood; that person, admitted by the root
        // operator's device, then appoints the former operator again.
        f.Estate.SetClock(appointed.AddMinutes(10));
        RecordEnvelope second = f.Estate.Add("principal", former.Id, Person("Second appointee", Roles.Operator), signer: f.Signer(formerDevice));
        f.Estate.SetClock(resume);
        (RecordEnvelope secondDevice, _) = f.Admitted(second.Id, "The second appointee's device");
        RecordEnvelope again = f.Estate.Add("principal", second.Id, Person("Second person", Roles.Operator), supersedes: removal.Id, signer: f.Signer(secondDevice));
        RecordEnvelope bySecond = f.Close("device", f.BuilderDevice.Id, second.Id, f.Signer(secondDevice));
        RecordEnvelope byFormer = f.Close("device", f.OperatorDevice.Id, former.Id, f.Signer(formerDevice));
        EstateModel model = f.Load();
        Assert.False(Acts(model, bySecond));
        Assert.False(Acts(model, byFormer));
        Assert.Contains(model.Findings, x => x.Code == "history.closing-author" && x.Location == bySecond.ExpectedPath + "#body.closes");
        Assert.Contains(model.Findings, x => x.Code == "history.closing-author" && x.Location == byFormer.ExpectedPath + "#body.closes");
        Assert.True(ReportedAsRemoval(model, second));
        Assert.True(ReportedAsRemoval(model, again));
    }

    [Fact]
    public void ARemovalByAnOperatorKeepsCountingAfterThatOperatorIsRemoved()
    {
        using var f = new HistoryFixture();
        RecordEnvelope remover = f.Estate.Add("principal", f.Estate.OperatorId, Person("Remover", Roles.Operator), signer: f.OperatorSigner);
        (RecordEnvelope removerDevice, _) = f.Admitted(remover.Id, "The remover's device");
        RecordEnvelope removed = f.Estate.Add("principal", f.Estate.OperatorId, Person("Removed", Roles.Operator), signer: f.OperatorSigner);
        (RecordEnvelope removedDevice, _) = f.Admitted(removed.Id, "The removed person's device");
        f.Require();
        f.Adopt();
        f.Estate.SetClock(f.Estate.Clock.AddHours(1));
        RecordEnvelope removal = f.Estate.Add("principal", remover.Id, Person("Removed", Roles.Reviewer), supersedes: removed.Id, signer: f.Signer(removerDevice));
        f.Estate.SetClock(f.Estate.Clock.AddHours(1));
        RecordEnvelope closing = f.Close("device", f.BuilderDevice.Id, removed.Id, f.Signer(removedDevice));
        EstateModel model = f.Load();
        Assert.False(Acts(model, closing));

        // The root operator then removes the remover's role: the removal it wrote still counts in every read.
        f.Estate.SetClock(f.Estate.Clock.AddHours(1));
        f.Estate.Add("principal", f.Estate.OperatorId, Person("Remover", Roles.Reviewer), supersedes: remover.Id, signer: f.OperatorSigner);
        RecordEnvelope later = f.Close("device", f.OperatorDevice.Id, removed.Id, f.Signer(removedDevice));
        model = f.Load();
        Assert.True(model.Counts(model.ById[removal.Id]));
        Assert.False(model.HasRole(removed.Id, Roles.Operator));
        Assert.False(Acts(model, closing));
        Assert.False(Acts(model, later));
        Assert.Contains(model.Findings, x => x.Code == "history.closing-author" && x.Location == later.ExpectedPath + "#body.closes");
        Assert.False(ReportedAsRemoval(model, removal));
    }

    [Fact]
    public void ANarrowingByAnOperatorKeepsCountingAfterThatOperatorIsRemoved()
    {
        using var f = new HistoryFixture();
        RecordEnvelope decision = f.Decision();
        RecordEnvelope narrower = f.Estate.Add("principal", f.Estate.OperatorId, Person("Narrower", Roles.Operator), signer: f.OperatorSigner);
        (RecordEnvelope narrowerDevice, _) = f.Admitted(narrower.Id, "The narrower's device");
        RecordEnvelope administrator = f.Estate.Add("principal", f.Estate.OperatorId, Person("Administrator", Roles.Reviewer), signer: f.OperatorSigner);
        (RecordEnvelope administratorDevice, _) = f.Admitted(administrator.Id, "The administrator's device");
        RecordEnvelope grant = f.Grant(administrator.Id, decision, [Scopes.Administer]);
        f.Require();
        f.Adopt();
        f.Estate.SetClock(f.Estate.Clock.AddHours(1));
        RecordEnvelope narrowing = f.Estate.Add("authority", narrower.Id, GrantBody(administrator.Id, decision, Scopes.LocalEngineering), supersedes: grant.Id, signer: f.Signer(narrowerDevice));
        f.Estate.SetClock(f.Estate.Clock.AddHours(1));
        f.Estate.Add("principal", f.Estate.OperatorId, Person("Narrower", Roles.Reviewer), supersedes: narrower.Id, signer: f.OperatorSigner);
        f.Estate.SetClock(f.Estate.Clock.AddHours(1));
        RecordEnvelope unsigned = f.Estate.Add("work-order", f.Estate.OperatorId, HistoryFixture.Order("AUR-3", "Unsigned"));
        RecordEnvelope anchoring = f.Checkpoint(administrator.Id, f.Signer(administratorDevice), anchored: [unsigned]);
        RecordEnvelope closing = f.Close("device", f.BuilderDevice.Id, administrator.Id, f.Signer(administratorDevice));
        EstateModel model = f.Load();
        Assert.True(model.Counts(model.ById[narrowing.Id]));
        Assert.False(model.HasScope(administrator.Id, Scopes.Administer));
        Assert.False(model.Trust.MayAnchor(model.ById[anchoring.Id]));
        Assert.False(model.Counts(model.ById[unsigned.Id]));
        Assert.False(Acts(model, closing));
    }

    [Fact]
    public void ARemovalByAPersonNeverGivenTheOperatorRoleChangesNothingForTheStrongestActs()
    {
        using var f = new HistoryFixture();
        RecordEnvelope reviewer = f.Estate.Add("principal", f.Estate.OperatorId, Person("Reviewer", Roles.Reviewer), signer: f.OperatorSigner);
        (RecordEnvelope reviewerDevice, _) = f.Admitted(reviewer.Id, "The reviewer's device");
        RecordEnvelope second = f.Estate.Add("principal", f.Estate.OperatorId, Person("Second operator", Roles.Operator), signer: f.OperatorSigner);
        (RecordEnvelope secondDevice, _) = f.Admitted(second.Id, "The second operator's device");
        f.Require();
        f.Adopt();
        RecordEnvelope third = f.Estate.Add("principal", second.Id, Person("Third operator", Roles.Operator), signer: f.Signer(secondDevice));
        (RecordEnvelope thirdDevice, _) = f.Admitted(third.Id, "The third operator's device");
        RecordEnvelope byThird = f.Close("device", f.BuilderDevice.Id, third.Id, f.Signer(thirdDevice));
        f.Estate.Add("principal", reviewer.Id, Person("Second operator", Roles.Reviewer), supersedes: second.Id, signer: f.Signer(reviewerDevice));
        RecordEnvelope bySecond = f.Close("device", reviewerDevice.Id, second.Id, f.Signer(secondDevice));
        EstateModel model = f.Load();
        Assert.True(Acts(model, byThird));
        Assert.True(Acts(model, bySecond));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TwoOperatorsWhoRemoveEachOtherHoldNoneOfTheStrongestActsUntilTheRootOperatorSettlesIt(bool closeAndRevoke)
    {
        using var f = new HistoryFixture();
        RecordEnvelope first = f.Estate.Add("principal", f.Estate.OperatorId, Person("First operator", Roles.Operator), signer: f.OperatorSigner);
        (RecordEnvelope firstDevice, _) = f.Admitted(first.Id, "The first operator's device");
        RecordEnvelope second = f.Estate.Add("principal", f.Estate.OperatorId, Person("Second operator", Roles.Operator), signer: f.OperatorSigner);
        (RecordEnvelope secondDevice, RecordEnvelope secondAdmission) = f.Admitted(second.Id, "The second operator's device");
        f.Require();
        f.Adopt();
        f.Estate.SetClock(f.Estate.Clock.AddHours(1));
        RecordEnvelope removal = f.Estate.Add("principal", first.Id, Person("Second operator", Roles.Reviewer), supersedes: second.Id, signer: f.Signer(firstDevice));
        RecordEnvelope? closedSecond = null;
        if (closeAndRevoke)
        {
            closedSecond = f.Close("device", secondDevice.Id, first.Id, f.Signer(firstDevice));
            f.Revoke(secondDevice, secondAdmission, f.Signer(firstDevice), first.Id);
        }

        // The second operator answers with a removal of the first; with its device closed and revoked, it dates the answer before both.
        DateTimeOffset resume = f.Estate.Clock;
        if (closeAndRevoke) f.Estate.SetClock(removal.AtTime.AddSeconds(-20));
        RecordEnvelope answer = f.Estate.Add("principal", second.Id, Person("First operator", Roles.Reviewer), supersedes: first.Id, signer: f.Signer(secondDevice));
        RecordEnvelope bySecond = f.Close("device", f.BuilderDevice.Id, second.Id, f.Signer(secondDevice));
        f.Estate.SetClock(resume.AddMinutes(1));
        RecordEnvelope byFirst = f.Close("device", f.BuilderDevice.Id, first.Id, f.Signer(firstDevice));
        EstateModel model = f.Load();
        Assert.False(Acts(model, bySecond));
        Assert.False(Acts(model, byFirst));
        // The first operator's closing of the second's device rests on the first operator standing, which the dispute takes away.
        if (closedSecond is not null) Assert.False(Acts(model, closedSecond));

        // The root operator settles it: it closes the second operator's device and writes the first operator's role again.
        f.Estate.SetClock(f.Estate.Clock.AddHours(1));
        RecordEnvelope byRoot = f.Close("device", secondDevice.Id);
        f.Estate.Add("principal", f.Estate.OperatorId, Person("First operator", Roles.Operator), supersedes: answer.Id, signer: f.OperatorSigner);
        RecordEnvelope settled = f.Close("device", f.BuilderDevice.Id, first.Id, f.Signer(firstDevice));
        model = f.Load();
        Assert.True(Acts(model, byRoot));
        Assert.True(Acts(model, settled));
        Assert.False(Acts(model, bySecond));
        Assert.True(model.HasRole(first.Id, Roles.Operator));
        Assert.False(model.HasRole(second.Id, Roles.Operator));
    }

    [Fact]
    public void AnAdoptionRestingOnALaterRemovedOperatorsGrantIsNoLongerRootedAndTheGrantIsReported()
    {
        using var f = new HistoryFixture();
        RecordEnvelope decision = f.Decision();
        RecordEnvelope second = f.Estate.Add("principal", f.Estate.OperatorId, Person("Second operator", Roles.Operator), signer: f.OperatorSigner);
        (RecordEnvelope secondDevice, _) = f.Admitted(second.Id, "The second operator's device");
        RecordEnvelope administrator = f.Estate.Add("principal", f.Estate.OperatorId, Person("Administrator", Roles.Reviewer), signer: f.OperatorSigner);
        (RecordEnvelope administratorDevice, _) = f.Admitted(administrator.Id, "The administrator's device");
        RecordEnvelope grant = f.Estate.Add("authority", second.Id, GrantBody(administrator.Id, decision, Scopes.Administer), signer: f.Signer(secondDevice));
        f.Require();
        EstateModel model = f.Load();
        RecordEnvelope adoption = f.Checkpoint(administrator.Id, f.Signer(administratorDevice), trigger: "adoption", anchored: model.Records.Where(r => r.Signatures.Count == 0));
        model = f.Load();
        Assert.True(model.Trust.Adopted);
        Assert.True(model.Trust.MayAnchor(model.ById[adoption.Id]));
        Assert.False(ReportedAsRemoval(model, grant));

        f.Estate.SetClock(f.Estate.Clock.AddHours(1));
        f.Estate.Add("principal", f.Estate.OperatorId, Person("Second operator", Roles.Reviewer), supersedes: second.Id, signer: f.OperatorSigner);
        model = f.Load();
        Assert.True(model.Counts(model.ById[grant.Id]));
        Assert.False(model.Trust.MayAnchor(model.ById[adoption.Id]));
        Assert.False(model.Trust.Adopted);
        Finding warning = Assert.Single(model.Findings, x => x.Code == "history.revision-author");
        Assert.Equal(Severity.Warning, warning.Severity);
        Assert.Equal(grant.ExpectedPath, warning.Location);
        Assert.Contains("no longer counts for anchoring and closings", warning.Message, StringComparison.Ordinal);
        Assert.Contains("A standing operator should write it again", warning.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ARevisionReadAsARemovalIsReportedOnlyInAnEstateHoldingACheckpoint()
    {
        using var f = new HistoryFixture();
        RecordEnvelope second = f.Estate.Add("principal", f.Estate.OperatorId, Person("Second operator", Roles.Operator), signer: f.OperatorSigner);
        (RecordEnvelope secondDevice, _) = f.Admitted(second.Id, "The second operator's device");
        RecordEnvelope appointee = f.Estate.Add("principal", second.Id, Person("Appointee", Roles.Operator), signer: f.Signer(secondDevice));
        f.Estate.SetClock(f.Estate.Clock.AddHours(1));
        f.Estate.Add("principal", f.Estate.OperatorId, Person("Second operator", Roles.Reviewer), supersedes: second.Id, signer: f.OperatorSigner);
        EstateModel model = f.Load();
        Assert.DoesNotContain(model.Findings, x => x.Code == "history.revision-author");

        f.Adopt();
        model = f.Load();
        Assert.True(ReportedAsRemoval(model, appointee));
        Assert.Single(model.Findings, x => x.Code == "history.revision-author");
    }

    [Fact]
    public void AStandingOperatorsRevisionsCountAndAnOperatorWhoseRoleIsRemovedClosesNothing()
    {
        using var f = new HistoryFixture();
        RecordEnvelope second = f.Estate.Add("principal", f.Estate.OperatorId, Person("Second operator", Roles.Operator), signer: f.OperatorSigner);
        (RecordEnvelope secondDevice, _) = f.Admitted(second.Id, "The second operator's device");
        f.Require();
        f.Adopt();
        // The standing second operator appoints a third, whose device closing acts.
        RecordEnvelope third = f.Estate.Add("principal", second.Id, Person("Third operator", Roles.Operator), signer: f.Signer(secondDevice));
        (RecordEnvelope thirdDevice, _) = f.Admitted(third.Id, "The third operator's device");
        RecordEnvelope acting = f.Close("device", f.BuilderDevice.Id, third.Id, f.Signer(thirdDevice));
        EstateModel model = f.Load();
        Assert.True(model.Counts(model.ById[third.Id]));
        Assert.True(Acts(model, acting));

        // The second operator removes the third's role. As for anchoring, a role taken away withdraws the power to close for every
        // checkpoint: neither the third's earlier closing nor its later one acts.
        f.Estate.Add("principal", second.Id, Person("Third operator", Roles.Reviewer), supersedes: third.Id, signer: f.Signer(secondDevice));
        RecordEnvelope later = f.Close("device", f.OperatorDevice.Id, third.Id, f.Signer(thirdDevice));
        model = f.Load();
        Assert.False(model.HasRole(third.Id, Roles.Operator));
        Assert.False(Acts(model, acting));
        Assert.False(Acts(model, later));
    }

    [Fact]
    public void AnEstateWhoseOperatorsAllStandReadsAsWritten()
    {
        using var f = new HistoryFixture();
        RecordEnvelope decision = f.Decision();
        RecordEnvelope second = f.Estate.Add("principal", f.Estate.OperatorId, Person("Second operator", Roles.Operator), signer: f.OperatorSigner);
        (RecordEnvelope secondDevice, _) = f.Admitted(second.Id, "The second operator's device");
        f.Require();
        f.Adopt();
        // A chain of appointments, each by an operator who stands, and a grant of administer by one of them.
        RecordEnvelope third = f.Estate.Add("principal", second.Id, Person("Third operator", Roles.Operator), signer: f.Signer(secondDevice));
        (RecordEnvelope thirdDevice, _) = f.Admitted(third.Id, "The third operator's device");
        RecordEnvelope fourth = f.Estate.Add("principal", third.Id, Person("Fourth operator", Roles.Operator), signer: f.Signer(thirdDevice));
        (RecordEnvelope fourthDevice, _) = f.Admitted(fourth.Id, "The fourth operator's device");
        RecordEnvelope administrator = f.Estate.Add("principal", fourth.Id, Person("Administrator", Roles.Reviewer), signer: f.Signer(fourthDevice));
        (RecordEnvelope administratorDevice, _) = f.Admitted(administrator.Id, "The administrator's device");
        f.Estate.Add("authority", third.Id, GrantBody(administrator.Id, decision, Scopes.Administer), signer: f.Signer(thirdDevice));
        RecordEnvelope unsigned = f.Estate.Add("work-order", f.Estate.OperatorId, HistoryFixture.Order("AUR-4", "Unsigned"));
        RecordEnvelope anchoring = f.Checkpoint(administrator.Id, f.Signer(administratorDevice), anchored: [unsigned]);
        RecordEnvelope byFourth = f.Close("device", f.BuilderDevice.Id, fourth.Id, f.Signer(fourthDevice));
        EstateModel model = f.Load();
        Assert.True(model.Trust.MayAnchor(model.ById[anchoring.Id]));
        Assert.True(model.Counts(model.ById[unsigned.Id]));
        Assert.True(Acts(model, byFourth));
        Assert.DoesNotContain(model.Findings, x => x.Code == "history.revision-author");
        Assert.DoesNotContain(model.Findings, x => x.Severity == Severity.Error && SignedHistory.ProblemCodes.Contains(x.Code));
    }

    [Fact]
    public void AnOperatorTheRootOperatorRemovesAndAppointsAgainActsOnlyWhileTheRoleStood()
    {
        using var f = new HistoryFixture();
        RecordEnvelope person = f.Estate.Add("principal", f.Estate.OperatorId, Person("Second operator", Roles.Operator), signer: f.OperatorSigner);
        (RecordEnvelope device, _) = f.Admitted(person.Id, "The second operator's device");
        f.Require();
        f.Adopt();
        // In the first tenure the operator appoints another and closes a device.
        RecordEnvelope early = f.Estate.Add("principal", person.Id, Person("Early appointee", Roles.Operator), signer: f.Signer(device));
        (RecordEnvelope earlyDevice, _) = f.Admitted(early.Id, "The early appointee's device");
        (RecordEnvelope spare1, _) = f.Admitted(f.Estate.OperatorId, "Spare 1");
        (RecordEnvelope spare2, _) = f.Admitted(f.Estate.OperatorId, "Spare 2");
        (RecordEnvelope spare3, _) = f.Admitted(f.Estate.OperatorId, "Spare 3");
        RecordEnvelope during = f.Close("device", spare1.Id, person.Id, f.Signer(device));
        f.Estate.SetClock(f.Estate.Clock.AddHours(1));
        RecordEnvelope removal = f.Estate.Add("principal", f.Estate.OperatorId, Person("Second operator", Roles.Reviewer), supersedes: person.Id, signer: f.OperatorSigner);
        RecordEnvelope between = f.Close("device", spare2.Id, person.Id, f.Signer(device));
        RecordEnvelope late = f.Estate.Add("principal", person.Id, Person("Late appointee", Roles.Operator), signer: f.Signer(device));
        (RecordEnvelope lateDevice, _) = f.Admitted(late.Id, "The late appointee's device");
        f.Estate.SetClock(f.Estate.Clock.AddHours(1));
        f.Estate.Add("principal", f.Estate.OperatorId, Person("Second operator", Roles.Operator), supersedes: removal.Id, signer: f.OperatorSigner);
        RecordEnvelope after = f.Close("device", spare3.Id, person.Id, f.Signer(device));
        RecordEnvelope byEarly = f.Close("device", f.BuilderDevice.Id, early.Id, f.Signer(earlyDevice));
        RecordEnvelope byLate = f.Close("device", f.OperatorDevice.Id, late.Id, f.Signer(lateDevice));
        EstateModel model = f.Load();
        Assert.True(Acts(model, during));
        Assert.False(Acts(model, between));
        Assert.True(Acts(model, after));
        Assert.True(Acts(model, byEarly));
        Assert.False(Acts(model, byLate));
    }
}
