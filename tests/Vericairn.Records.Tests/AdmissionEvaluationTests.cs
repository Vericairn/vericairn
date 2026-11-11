// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using System.Text.Json.Nodes;
using Vericairn.Records.Authoring;
using Vericairn.Records.Canonical;
using Vericairn.Records.Estate;
using Vericairn.Records.Model;
using Vericairn.Records.Types;

namespace Vericairn.Records.Tests;

/// <summary>Admissions count from the estate's first authority outward, whatever order the records are read in.</summary>
public class AdmissionEvaluationTests
{
    [Fact]
    public void AdmissionsThatDoNotLeadBackToTheFirstAuthorityAdmitNobody()
    {
        using var f = new HistoryFixture();
        RecordEnvelope x = f.Device(f.Estate.OperatorId, "Device X");
        RecordEnvelope y = f.Device(f.Estate.OperatorId, "Device Y");
        DateTimeOffset second = f.Estate.Clock.AddSeconds(5);
        f.Estate.SetClock(second.AddSeconds(-1));
        RecordEnvelope admitsY = f.Estate.Add("admission", f.Estate.OperatorId, HistoryFixture.Admission(f.Estate.OperatorId, "Admitted by X."), y.Reference, signer: f.Signer(x));
        f.Estate.SetClock(second.AddSeconds(-1));
        f.Estate.Add("admission", f.Estate.OperatorId, HistoryFixture.Admission(f.Estate.OperatorId, "Admitted by Y."), x.Reference, signer: f.Signer(y));
        RecordEnvelope signed = f.Estate.Add("work-order", f.Estate.OperatorId, HistoryFixture.Order("ADM-1", "Signed by device X"), signer: f.Signer(x));
        EstateModel model = f.Load();
        Assert.False(SignatureChecks.IsAdmitted(model, x));
        Assert.False(SignatureChecks.IsAdmitted(model, y));
        Assert.False(SignatureChecks.IsAuthentic(model, signed));
        Assert.False(model.Counts(signed));
        Assert.Contains(model.Findings, finding => finding.Location.StartsWith(admitsY.ExpectedPath, StringComparison.Ordinal) && finding.Code == "signature.unadmitted");
        Assert.DoesNotContain(EstateStatus.WorkItems(model), i => i.Key == "ADM-1");
    }

    [Fact]
    public void AnAdmissionWhoseSignatureFailsAdmitsNobody()
    {
        using var f = new HistoryFixture();
        RecordEnvelope device = f.Device(f.Estate.OperatorId, "Another device");
        RecordEnvelope donor = f.Estate.Add("work-order", f.Estate.OperatorId, HistoryFixture.Order("ADM-2"), signer: f.OperatorSigner);
        JsonObject envelope = JsonNode.Parse(RecordAuthor.Create(new RecordDraft("admission", f.Estate.OperatorId, JsonDocument.Parse(HistoryFixture.Admission(f.Estate.OperatorId, "Admitted.")).RootElement, device.Reference, null, f.Estate.Clock.AddSeconds(1), null, 3), []).Bytes)!.AsObject();
        envelope["signatures"] = JsonNode.Parse(File.ReadAllText(f.PathOf(donor)))!["signatures"]!.DeepClone();
        byte[] bytes = CanonicalJson.SerializePrettyUtf8(JsonSerializer.SerializeToElement(envelope));
        RecordEnvelope admission = RecordParser.Parse(bytes, "admission", new FindingList())!;
        File.WriteAllBytes(Path.Combine(f.Root, admission.ExpectedPath), bytes);
        RecordEnvelope signed = f.Estate.Add("work-order", f.Estate.OperatorId, HistoryFixture.Order("ADM-3"), signer: f.Signer(device));
        EstateModel model = f.Load();
        Assert.Contains(model.Findings, finding => finding.Code == "signature.invalid" && finding.Location.StartsWith(admission.ExpectedPath, StringComparison.Ordinal));
        Assert.False(SignatureChecks.IsAdmitted(model, device));
        Assert.False(model.Counts(signed));
        Assert.False(SignatureChecks.IsAuthentic(model, signed));
    }

    [Fact]
    public void AReadmissionThroughAnAdministratorsDeviceCountsWhateverTheReadingOrder()
    {
        using var f = new HistoryFixture();
        RecordEnvelope decision = f.Estate.Add("decision", f.Estate.OperatorId, $$"""{"title":"Administration","statement":"The administrator admits devices.","summary":"A delegation.","synthesis":"For tests.","effects":["administer"],"affects":[],"decidedBy":"{{f.Estate.OperatorId}}","decidedAt":"2026-10-13T12:00:00Z","stake":"medium","notAuthorized":["publication"]}""", signer: f.OperatorSigner);
        RecordEnvelope administrator = f.Estate.Add("principal", f.Estate.OperatorId, """{"kind":"human","name":"Administrator","roles":["contributor"]}""", signer: f.OperatorSigner);
        f.Estate.Add("authority", f.Estate.OperatorId, $$"""{"grantee":"{{administrator.Id}}","scope":["administer"],"reserved":[],"conditions":[],"basis":"{{decision.Id}}"}""", signer: f.OperatorSigner);
        RecordEnvelope laptop = f.Device(administrator.Id, "Administrator's laptop");
        RecordEnvelope first = f.Estate.Add("admission", f.Estate.OperatorId, HistoryFixture.Admission(administrator.Id, "Admitted by the operator."), laptop.Reference, signer: f.OperatorSigner);
        RecordEnvelope second = f.Device(f.Estate.OperatorId, "Operator's second machine");
        f.Estate.Add("admission", administrator.Id, HistoryFixture.Admission(f.Estate.OperatorId, "Admitted by the administrator."), second.Reference, signer: f.Signer(laptop));
        string revokedAt = RecordParser.FormatTimestamp(f.Estate.Clock.AddSeconds(1));
        RecordEnvelope revocation = f.Estate.Add("admission", f.Estate.OperatorId, HistoryFixture.Admission(administrator.Id, "Revoked.", "revoked", revokedAt), laptop.Reference, supersedes: first.Id, signer: f.Signer(second));
        f.Estate.Add("admission", f.Estate.OperatorId, HistoryFixture.Admission(administrator.Id, "Admitted again."), laptop.Reference, supersedes: revocation.Id, signer: f.Signer(second));
        RecordEnvelope later = f.Estate.Add("work-order", administrator.Id, HistoryFixture.Order("ADM-4", "Signed after readmission"), signer: f.Signer(laptop));
        EstateModel model = f.Load();
        Assert.False(model.Findings.HasErrors, string.Join('\n', model.Findings));
        Assert.True(SignatureChecks.IsAdmitted(model, laptop));
        Assert.True(SignatureChecks.IsAuthentic(model, later));

        var reversed = new EstateModel(model.Root, [.. model.Records.Reverse()], model.ById, model.SupersededBy, new FindingList()) { Manifest = model.Manifest };
        Assert.True(SignatureChecks.IsAdmitted(reversed, laptop));
        Assert.True(SignatureChecks.IsAuthentic(reversed, later));
        Assert.Equal(model.Records.Select(r => model.Counts(r)), model.Records.Select(r => reversed.Counts(r)));
    }

    [Fact]
    public void AnAdmissionSignedWithAKeyItDoesNotNameAdmitsNothing()
    {
        using var f = new HistoryFixture();
        f.Require();
        RecordEnvelope x = f.Device(f.Estate.BuilderId, "Device X");
        // An admission by the operator that names the operator's device but carries the builder's signature.
        JsonElement body = JsonDocument.Parse(HistoryFixture.Admission(f.Estate.BuilderId, "Named for the operator's device.")).RootElement;
        AuthoredRecord authored = RecordAuthor.Create(new RecordDraft("admission", f.Estate.OperatorId, body, x.Reference, null, f.Estate.Clock.AddSeconds(5), null, 3), [f.BuilderSigner]);
        JsonObject json = JsonNode.Parse(System.Text.Encoding.UTF8.GetString(authored.Bytes))!.AsObject();
        json["signatures"]![0]!["device"] = f.OperatorDevice.Id;
        File.WriteAllText(Path.Combine(f.Root, authored.Envelope.ExpectedPath), json.ToJsonString());
        f.Estate.SetClock(f.Estate.Clock.AddSeconds(10));
        RecordEnvelope order = f.Estate.Add("work-order", f.Estate.BuilderId, HistoryFixture.Order("ADM-4"), signer: f.Signer(x));
        EstateModel model = f.Load();
        Assert.Contains(model.Findings, x => x.Code == "signature.invalid");
        Assert.Equal(SignatureChecks.AdmissionState.Unadmitted, SignatureChecks.AdmissionStateAt(model, x, order.At));
        Assert.False(SignatureChecks.IsAuthentic(model, order));
        Assert.False(model.Counts(order));
    }

    [Fact]
    public void AnAdmissionByAnAuthorWhoMayNotAdmitAdmitsNothing()
    {
        using var f = new HistoryFixture();
        f.Require();
        // The agent signs, with its own admitted device, an admission of a new device for the operator, and one for itself.
        RecordEnvelope x = f.Device(f.Estate.OperatorId, "Device X");
        RecordEnvelope forOperator = f.Estate.Add("admission", f.Estate.BuilderId, HistoryFixture.Admission(f.Estate.OperatorId, "By the agent."), x.Reference, signer: f.BuilderSigner);
        RecordEnvelope y = f.Device(f.Estate.BuilderId, "Device Y");
        RecordEnvelope forItself = f.Estate.Add("admission", f.Estate.BuilderId, HistoryFixture.Admission(f.Estate.BuilderId, "By the agent."), y.Reference, signer: f.BuilderSigner);
        RecordEnvelope asOperator = f.Estate.Add("work-order", f.Estate.OperatorId, HistoryFixture.Order("ADM-5"), signer: f.Signer(x));
        RecordEnvelope asAgent = f.Estate.Add("work-order", f.Estate.BuilderId, HistoryFixture.Order("ADM-6"), signer: f.Signer(y));
        EstateModel model = f.Load();
        Assert.Contains(model.Findings, x => x.Code == "admission.author");
        Assert.True(SignatureChecks.IsAuthentic(model, model.ById[forOperator.Id]));
        Assert.False(model.Counts(model.ById[forOperator.Id]));
        Assert.False(model.Counts(model.ById[forItself.Id]));
        Assert.Equal(SignatureChecks.AdmissionState.Unadmitted, SignatureChecks.AdmissionStateAt(model, x, asOperator.At));
        Assert.Equal(SignatureChecks.AdmissionState.Unadmitted, SignatureChecks.AdmissionStateAt(model, y, asAgent.At));
        Assert.False(SignatureChecks.IsAuthentic(model, asOperator));
        Assert.False(model.Counts(asOperator));
        Assert.False(model.Counts(asAgent));
        Assert.True(SignatureChecks.IsAdmitted(model, f.BuilderDevice));
    }

    [Fact]
    public void AnotherOperatorAndAnAdministratorMayAdmit()
    {
        using var f = new HistoryFixture();
        RecordEnvelope decision = f.Estate.Add("decision", f.Estate.OperatorId, $$"""{"title":"Basis","statement":"Grant authority.","summary":"A basis.","synthesis":"For tests.","effects":["grant"],"affects":[],"decidedBy":"{{f.Estate.OperatorId}}","decidedAt":"2026-10-13T12:00:00Z","stake":"medium","notAuthorized":["publication"]}""", signer: f.OperatorSigner);
        RecordEnvelope second = f.Estate.Add("principal", f.Estate.OperatorId, """{"kind":"human","name":"Second Operator","roles":["operator"]}""", signer: f.OperatorSigner);
        RecordEnvelope secondDevice = f.Device(second.Id, "Second operator's device");
        f.Estate.Add("admission", f.Estate.OperatorId, HistoryFixture.Admission(second.Id, "Second operator."), secondDevice.Reference, signer: f.OperatorSigner);
        RecordEnvelope admin = f.Estate.Add("principal", f.Estate.OperatorId, """{"kind":"human","name":"Admin","roles":["reviewer"]}""", signer: f.OperatorSigner);
        RecordEnvelope adminDevice = f.Device(admin.Id, "Admin device");
        f.Estate.Add("admission", f.Estate.OperatorId, HistoryFixture.Admission(admin.Id, "Admin device."), adminDevice.Reference, signer: f.OperatorSigner);
        f.Estate.Add("authority", f.Estate.OperatorId, $$"""{"grantee":"{{admin.Id}}","scope":["administer"],"reserved":[],"conditions":[],"basis":"{{decision.Id}}"}""", signer: f.OperatorSigner);
        // A reviewer whose administer grant the agent wrote holds nothing.
        RecordEnvelope other = f.Estate.Add("principal", f.Estate.OperatorId, """{"kind":"human","name":"Other","roles":["reviewer"]}""", signer: f.OperatorSigner);
        RecordEnvelope otherDevice = f.Device(other.Id, "Other's device");
        f.Estate.Add("admission", f.Estate.OperatorId, HistoryFixture.Admission(other.Id, "Other's device."), otherDevice.Reference, signer: f.OperatorSigner);
        f.Estate.Add("authority", f.Estate.BuilderId, $$"""{"grantee":"{{other.Id}}","scope":["administer"],"reserved":[],"conditions":[],"basis":"{{decision.Id}}"}""", signer: f.BuilderSigner);
        f.Require();

        RecordEnvelope bySecond = f.Device(f.Estate.BuilderId, "Admitted by the second operator");
        f.Estate.Add("admission", second.Id, HistoryFixture.Admission(f.Estate.BuilderId, "By the second operator."), bySecond.Reference, signer: f.Signer(secondDevice));
        RecordEnvelope byAdmin = f.Device(f.Estate.BuilderId, "Admitted by the administrator");
        f.Estate.Add("admission", admin.Id, HistoryFixture.Admission(f.Estate.BuilderId, "By the administrator."), byAdmin.Reference, signer: f.Signer(adminDevice));
        RecordEnvelope byOther = f.Device(f.Estate.BuilderId, "Admitted by the reviewer");
        f.Estate.Add("admission", other.Id, HistoryFixture.Admission(f.Estate.BuilderId, "By the reviewer."), byOther.Reference, signer: f.Signer(otherDevice));
        RecordEnvelope order = f.Estate.Add("work-order", f.Estate.BuilderId, HistoryFixture.Order("ADM-7"), signer: f.Signer(byAdmin));
        EstateModel model = f.Load();
        Assert.True(SignatureChecks.IsAdmitted(model, bySecond));
        Assert.True(SignatureChecks.IsAdmitted(model, byAdmin));
        Assert.False(SignatureChecks.IsAdmitted(model, byOther));
        Assert.True(model.Counts(order));
    }

    [Fact]
    public void AnAdministratorsPowerToAdmitIsReadAsItStoodAtTheAdmission()
    {
        using var f = new HistoryFixture();
        RecordEnvelope decision = f.Decision();
        RecordEnvelope admin = f.Estate.Add("principal", f.Estate.OperatorId, """{"kind":"human","name":"Admin","roles":["reviewer"]}""", signer: f.OperatorSigner);
        (RecordEnvelope adminDevice, _) = f.Admitted(admin.Id, "Admin device");
        f.Require();
        // Admitted before any grant, under the grant, and after a narrowing that takes administer away.
        RecordEnvelope early = f.Device(f.Estate.BuilderId, "Admitted before the grant");
        f.Estate.Add("admission", admin.Id, HistoryFixture.Admission(f.Estate.BuilderId, "Before the grant."), early.Reference, signer: f.Signer(adminDevice));
        RecordEnvelope grant = f.Grant(admin.Id, decision, [Scopes.Administer, Scopes.LocalEngineering]);
        RecordEnvelope granted = f.Device(f.Estate.BuilderId, "Admitted under the grant");
        f.Estate.Add("admission", admin.Id, HistoryFixture.Admission(f.Estate.BuilderId, "Under the grant."), granted.Reference, signer: f.Signer(adminDevice));
        f.Grant(admin.Id, decision, [Scopes.LocalEngineering], supersedes: grant.Id);
        RecordEnvelope late = f.Device(f.Estate.BuilderId, "Admitted after the narrowing");
        f.Estate.Add("admission", admin.Id, HistoryFixture.Admission(f.Estate.BuilderId, "After the narrowing."), late.Reference, signer: f.Signer(adminDevice));
        RecordEnvelope order = f.Estate.Add("work-order", f.Estate.BuilderId, HistoryFixture.Order("ADM-8"), signer: f.Signer(granted));
        EstateModel model = f.Load();
        Assert.False(SignatureChecks.IsAdmitted(model, early));
        Assert.True(SignatureChecks.IsAdmitted(model, granted));
        Assert.False(SignatureChecks.IsAdmitted(model, late));
        Assert.True(model.Counts(model.ById[order.Id]));
    }

    [Fact]
    public void AnAdmissionDatedBackIntoAClosedGrantAdmitsNothing()
    {
        using var f = new HistoryFixture();
        RecordEnvelope decision = f.Decision();
        RecordEnvelope admin = f.Estate.Add("principal", f.Estate.OperatorId, """{"kind":"human","name":"Admin","roles":["reviewer"]}""", signer: f.OperatorSigner);
        (RecordEnvelope adminDevice, _) = f.Admitted(admin.Id, "Admin device");
        RecordEnvelope grant = f.Grant(admin.Id, decision, [Scopes.Administer, Scopes.LocalEngineering]);
        f.Require();
        f.Adopt();
        RecordEnvelope written = f.Device(f.Estate.BuilderId, "Admitted before the closing");
        RecordEnvelope admitted = f.Estate.Add("admission", admin.Id, HistoryFixture.Admission(f.Estate.BuilderId, "Before the closing."), written.Reference, signer: f.Signer(adminDevice));
        f.Close("grant", grant.Id);
        f.Grant(admin.Id, decision, [Scopes.LocalEngineering], supersedes: grant.Id);
        // After the closing, an admission dated back into the grant's time relies on the closed revision, which the closing does not witness.
        RecordEnvelope backdated = f.Device(f.Estate.BuilderId, "Admitted with an earlier date");
        f.Estate.SetClock(admitted.AtTime);
        f.Estate.Add("admission", admin.Id, HistoryFixture.Admission(f.Estate.BuilderId, "Dated back."), backdated.Reference, signer: f.Signer(adminDevice));
        EstateModel model = f.Load();
        Assert.True(SignatureChecks.IsAdmitted(model, written));
        Assert.False(SignatureChecks.IsAdmitted(model, backdated));
    }

    [Fact]
    public void AnOperatorsOwnRevisionDoesNotChangeWhoMayAdmit()
    {
        using var f = new HistoryFixture();
        RecordEnvelope second = f.Estate.Add("principal", f.Estate.OperatorId, """{"kind":"human","name":"Second Operator","roles":["operator"]}""", signer: f.OperatorSigner);
        (RecordEnvelope secondDevice, _) = f.Admitted(second.Id, "Second operator's device");
        f.Require();
        // The second operator's own revision taking the role away counts for nothing in every read, so they still admit.
        f.Estate.Add("principal", second.Id, """{"kind":"human","name":"Second Operator","roles":[]}""", supersedes: second.Id, signer: f.Signer(secondDevice));
        RecordEnvelope device = f.Device(f.Estate.BuilderId, "Admitted by the second operator");
        RecordEnvelope admission = f.Estate.Add("admission", second.Id, HistoryFixture.Admission(f.Estate.BuilderId, "By the second operator."), device.Reference, signer: f.Signer(secondDevice));
        EstateModel model = f.Load();
        Assert.True(model.HasRole(second.Id, Roles.Operator));
        Assert.DoesNotContain(model.Findings, x => x.Code == "admission.author" && x.Location == admission.ExpectedPath);
        Assert.True(model.Counts(model.ById[admission.Id]));
        Assert.True(SignatureChecks.IsAdmitted(model, device));
    }

    [Fact]
    public void ARemovalByAFormerOperatorEndsThePowerToAdmit()
    {
        using var f = new HistoryFixture();
        RecordEnvelope a = f.Estate.Add("principal", f.Estate.OperatorId, """{"kind":"human","name":"Operator A","roles":["operator"]}""", signer: f.OperatorSigner);
        (RecordEnvelope aDevice, _) = f.Admitted(a.Id, "A's device");
        RecordEnvelope b = f.Estate.Add("principal", f.Estate.OperatorId, """{"kind":"human","name":"Operator B","roles":["operator"]}""", signer: f.OperatorSigner);
        (RecordEnvelope bDevice, _) = f.Admitted(b.Id, "B's device");
        f.Require();
        // B removes A; A, who held the operator role earlier, removes B. Both removals count, so neither may admit afterwards.
        f.Estate.Add("principal", b.Id, """{"kind":"human","name":"Operator A","roles":[]}""", supersedes: a.Id, signer: f.Signer(bDevice));
        f.Estate.Add("principal", a.Id, """{"kind":"human","name":"Operator B","roles":[]}""", supersedes: b.Id, signer: f.Signer(aDevice));
        RecordEnvelope byB = f.Device(f.Estate.BuilderId, "Admitted by B after its removal");
        RecordEnvelope admission = f.Estate.Add("admission", b.Id, HistoryFixture.Admission(f.Estate.BuilderId, "By B."), byB.Reference, signer: f.Signer(bDevice));
        RecordEnvelope byA = f.Device(f.Estate.BuilderId, "Admitted by A after its removal");
        f.Estate.Add("admission", a.Id, HistoryFixture.Admission(f.Estate.BuilderId, "By A."), byA.Reference, signer: f.Signer(aDevice));
        EstateModel model = f.Load();
        Assert.False(model.HasRole(b.Id, Roles.Operator));
        Assert.Contains(model.Findings, x => x.Code == "admission.author" && x.Location == admission.ExpectedPath);
        Assert.False(SignatureChecks.IsAdmitted(model, byB));
        Assert.False(SignatureChecks.IsAdmitted(model, byA));
    }

    [Fact]
    public void ANarrowingByAFormerOperatorEndsAnAdministratorsPowerToAdmit()
    {
        using var f = new HistoryFixture();
        RecordEnvelope decision = f.Decision();
        RecordEnvelope a = f.Estate.Add("principal", f.Estate.OperatorId, """{"kind":"human","name":"Operator A","roles":["operator"]}""", signer: f.OperatorSigner);
        (RecordEnvelope aDevice, _) = f.Admitted(a.Id, "A's device");
        RecordEnvelope admin = f.Estate.Add("principal", f.Estate.OperatorId, """{"kind":"human","name":"Admin","roles":["reviewer"]}""", signer: f.OperatorSigner);
        (RecordEnvelope adminDevice, _) = f.Admitted(admin.Id, "Admin device");
        RecordEnvelope grant = f.Grant(admin.Id, decision, [Scopes.Administer, Scopes.LocalEngineering]);
        f.Require();
        RecordEnvelope before = f.Device(f.Estate.BuilderId, "Admitted under the grant");
        f.Estate.Add("admission", admin.Id, HistoryFixture.Admission(f.Estate.BuilderId, "Under the grant."), before.Reference, signer: f.Signer(adminDevice));
        // The root operator removes A; A, who held the operator role earlier, then narrows the administrator's grant, which counts.
        f.Estate.Add("principal", f.Estate.OperatorId, """{"kind":"human","name":"Operator A","roles":[]}""", supersedes: a.Id, signer: f.OperatorSigner);
        f.Estate.Add("authority", a.Id, $$"""{"grantee":"{{admin.Id}}","scope":["local-engineering"],"reserved":[],"conditions":[],"basis":"{{decision.Id}}"}""", supersedes: grant.Id, signer: f.Signer(aDevice));
        RecordEnvelope after = f.Device(f.Estate.BuilderId, "Admitted after the narrowing");
        RecordEnvelope admission = f.Estate.Add("admission", admin.Id, HistoryFixture.Admission(f.Estate.BuilderId, "After the narrowing."), after.Reference, signer: f.Signer(adminDevice));
        EstateModel model = f.Load();
        Assert.False(model.HasScope(admin.Id, Scopes.Administer));
        Assert.Contains(model.Findings, x => x.Code == "admission.author" && x.Location == admission.ExpectedPath);
        Assert.True(SignatureChecks.IsAdmitted(model, before));
        Assert.False(SignatureChecks.IsAdmitted(model, after));
    }
}
