// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using System.Text.Json.Nodes;
using Vericairn.Records.Authoring;
using Vericairn.Records.Canonical;
using Vericairn.Records.Estate;
using Vericairn.Records.Model;

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
}
