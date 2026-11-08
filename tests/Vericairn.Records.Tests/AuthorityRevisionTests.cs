// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json.Nodes;
using Vericairn.Records.Estate;
using Vericairn.Records.Model;
using Vericairn.Records.Types;

namespace Vericairn.Records.Tests;

/// <summary>
/// Which grant and principal revisions authority reads accept: none dated before the record it supersedes, and for the strongest acts only
/// those written by the root operator or an operator who still stands.
/// </summary>
public class AuthorityRevisionTests
{
    /// <summary>
    /// An adopted estate with a second operator, appointed by the root operator and holding an admitted device, whose role the root
    /// operator removes an hour later; returns the person, their device and when they were appointed.
    /// </summary>
    private static (RecordEnvelope Person, RecordEnvelope Device, DateTimeOffset Appointed) FormerOperator(HistoryFixture f)
    {
        RecordEnvelope person = f.Estate.Add("principal", f.Estate.OperatorId, """{"kind":"human","name":"Second person","roles":["operator"]}""", signer: f.OperatorSigner);
        (RecordEnvelope device, _) = f.Admitted(person.Id, "The second person's device");
        DateTimeOffset appointed = f.Estate.Clock;
        f.Estate.SetClock(f.Estate.Clock.AddHours(1));
        f.Require();
        f.Adopt();
        f.Estate.Add("principal", f.Estate.OperatorId, """{"kind":"human","name":"Second person","roles":["reviewer"]}""", supersedes: person.Id, signer: f.OperatorSigner);
        return (person, device, appointed);
    }

    [Fact]
    public void APrincipalRevisionDatedBeforeTheRecordItSupersedesCountsForNothing()
    {
        using var f = new HistoryFixture();
        RecordEnvelope person = f.Estate.Add("principal", f.Estate.OperatorId, """{"kind":"human","name":"Third person","roles":["operator"]}""", signer: f.OperatorSigner);
        DateTimeOffset first = f.Estate.Clock;
        f.Estate.SetClock(first.AddHours(1));
        RecordEnvelope reviewer = f.Estate.Add("principal", f.Estate.OperatorId, """{"kind":"human","name":"Third person","roles":["reviewer"]}""", supersedes: person.Id, signer: f.OperatorSigner);
        DateTimeOffset resume = f.Estate.Clock;
        f.Estate.SetClock(first.AddMinutes(10));
        RecordEnvelope earlier = f.Estate.Add("principal", f.Estate.OperatorId, """{"kind":"human","name":"Third person","roles":["operator"]}""", supersedes: reviewer.Id, signer: f.OperatorSigner);
        f.Estate.SetClock(resume);
        EstateModel model = f.Load();
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
        RecordEnvelope grant = f.Grant(f.Estate.BuilderId, decision, [Scopes.LocalEngineering]);
        f.Estate.SetClock(grant.AtTime.AddMinutes(-5));
        RecordEnvelope earlier = f.Grant(f.Estate.BuilderId, decision, [Scopes.Administer], supersedes: grant.Id);
        EstateModel model = f.Load();
        Assert.False(model.Counts(model.ById[earlier.Id]));
        Assert.Contains(model.Findings, x => x.Code == "record.supersedes" && x.Location == earlier.ExpectedPath);
        Assert.False(model.HasScope(f.Estate.BuilderId, Scopes.Administer));
        Assert.True(model.HasScope(f.Estate.BuilderId, Scopes.LocalEngineering));
    }

    [Fact]
    public void AGrantFromAnOperatorWhoNoLongerStandsGivesNoAnchoring()
    {
        using var f = new HistoryFixture();
        RecordEnvelope decision = f.Decision();
        RecordEnvelope administrator = f.Estate.Add("principal", f.Estate.OperatorId, """{"kind":"human","name":"Administrator","roles":["reviewer"]}""", signer: f.OperatorSigner);
        (RecordEnvelope administratorDevice, _) = f.Admitted(administrator.Id, "The administrator's device");
        (RecordEnvelope person, RecordEnvelope device, DateTimeOffset appointed) = FormerOperator(f);
        RecordEnvelope unsigned = f.Estate.Add("work-order", f.Estate.OperatorId, HistoryFixture.Order("AUR-1", "Unsigned"));
        DateTimeOffset resume = f.Estate.Clock;

        // A grant of administer written by the former operator and dated into the time the role stood.
        f.Estate.SetClock(appointed.AddMinutes(10));
        RecordEnvelope grant = f.Estate.Add("authority", person.Id,
            new JsonObject { ["grantee"] = administrator.Id, ["scope"] = new JsonArray("administer"), ["reserved"] = new JsonArray(), ["conditions"] = new JsonArray(), ["basis"] = decision.Id }.ToJsonString(),
            signer: f.Signer(device));
        f.Estate.SetClock(resume);
        RecordEnvelope anchoring = f.Checkpoint(administrator.Id, f.Signer(administratorDevice), anchored: [unsigned]);
        RecordEnvelope closing = f.Close("device", f.OperatorDevice.Id, administrator.Id, f.Signer(administratorDevice));
        EstateModel model = f.Load();
        Assert.True(model.Counts(model.ById[grant.Id]));
        Assert.False(model.Trust.MayAnchor(model.ById[anchoring.Id]));
        Assert.False(model.Counts(model.ById[unsigned.Id]));
        Assert.Contains(model.Findings, x => x.Code == "history.anchor-author" && x.Location.StartsWith(anchoring.ExpectedPath, StringComparison.Ordinal));
        Assert.DoesNotContain(model.Trust.Closings, c => c.Record.Id == closing.Id);
    }

    [Fact]
    public void AnOperatorRoleFromAnOperatorWhoNoLongerStandsGivesNoneOfTheStrongestActs()
    {
        using var f = new HistoryFixture();
        (RecordEnvelope person, RecordEnvelope device, DateTimeOffset appointed) = FormerOperator(f);
        DateTimeOffset resume = f.Estate.Clock;
        f.Estate.SetClock(appointed.AddMinutes(10));
        RecordEnvelope appointee = f.Estate.Add("principal", person.Id, """{"kind":"human","name":"Appointee","roles":["operator"]}""", signer: f.Signer(device));
        f.Estate.SetClock(resume);
        (RecordEnvelope appointeeDevice, _) = f.Admitted(appointee.Id, "The appointee's device");
        RecordEnvelope closing = f.Close("device", f.OperatorDevice.Id, appointee.Id, f.Signer(appointeeDevice));
        EstateModel model = f.Load();
        Assert.DoesNotContain(model.Trust.Closings, c => c.Record.Id == closing.Id);
        Assert.Contains(model.Findings, x => x.Code == "history.closing-author" && x.Location == closing.ExpectedPath + "#body.closes");
    }

    [Fact]
    public void AStandingOperatorsRevisionsCountAndAnOperatorWhoseRoleIsRemovedClosesNothing()
    {
        using var f = new HistoryFixture();
        RecordEnvelope second = f.Estate.Add("principal", f.Estate.OperatorId, """{"kind":"human","name":"Second operator","roles":["operator"]}""", signer: f.OperatorSigner);
        (RecordEnvelope secondDevice, _) = f.Admitted(second.Id, "The second operator's device");
        f.Require();
        f.Adopt();
        // The standing second operator appoints a third, whose device closing acts.
        RecordEnvelope third = f.Estate.Add("principal", second.Id, """{"kind":"human","name":"Third operator","roles":["operator"]}""", signer: f.Signer(secondDevice));
        (RecordEnvelope thirdDevice, _) = f.Admitted(third.Id, "The third operator's device");
        RecordEnvelope acting = f.Close("device", f.BuilderDevice.Id, third.Id, f.Signer(thirdDevice));
        EstateModel model = f.Load();
        Assert.True(model.Counts(model.ById[third.Id]));
        Assert.Contains(model.Trust.Closings, c => c.Record.Id == acting.Id);

        // The second operator removes the third's role. As for anchoring, a role taken away withdraws the power to close for every
        // checkpoint: neither the third's earlier closing nor its later one acts.
        f.Estate.Add("principal", second.Id, """{"kind":"human","name":"Third operator","roles":["reviewer"]}""", supersedes: third.Id, signer: f.Signer(secondDevice));
        RecordEnvelope later = f.Close("device", f.OperatorDevice.Id, third.Id, f.Signer(thirdDevice));
        model = f.Load();
        Assert.False(model.HasRole(third.Id, Roles.Operator));
        Assert.DoesNotContain(model.Trust.Closings, c => c.Record.Id == acting.Id);
        Assert.DoesNotContain(model.Trust.Closings, c => c.Record.Id == later.Id);
    }
}
