// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using Vericairn.Records.Estate;
using Vericairn.Records.Model;

namespace Vericairn.Records.Tests;

/// <summary>The root operator is named by the charter the estate's manifest names.</summary>
public class RootCharterTests
{
    [Fact]
    public void TheRootOperatorComesFromTheCharterTheManifestNames()
    {
        using var f = new HistoryFixture();
        RecordEnvelope order = f.Estate.Add("work-order", f.Estate.OperatorId, HistoryFixture.Order("ROOT-1"), signer: f.OperatorSigner);
        f.Require();
        f.Adopt();
        Assert.False(f.Load().Findings.HasErrors);

        // Another charter that supersedes nothing, with a lower id, naming another principal.
        f.Estate.SetClock(new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero));
        RecordEnvelope other = f.Estate.Add("principal", f.Estate.OperatorId, """{"kind":"human","name":"Other","roles":["operator"]}""");
        RecordEnvelope charter = f.Estate.Add("estate", other.Id, $$$"""{"name":"Test Estate","slug":"test-estate","purpose":"Exercise the record format.","operator":"{{{other.Id}}}","recordFormat":3,"policies":{"signatures":"optional","releaseAcceptance":["operator"]}}""", id: "01AAAAAAAAAAAAAAAAAAAAAAAA");
        EstateModel model = f.Load();
        Assert.Equal(f.Estate.OperatorId, model.RootOperator);
        Assert.Equal(f.Estate.Estate.Id, model.FirstEstate!.Id);
        Assert.Equal("required", model.CurrentEstate!.Body.GetProperty("policies").GetProperty("signatures").GetString());
        Assert.True(SignatureChecks.IsAuthentic(model, order));
        Assert.True(SignatureChecks.IsAdmitted(model, f.OperatorDevice));
        Assert.DoesNotContain(model.Findings, x => x.Code == "signature.unadmitted");
        Assert.False(model.Counts(charter));
    }

    [Fact]
    public void ADeviceAdmittingItselfUnderAnotherFirstCharterIsNotAdmitted()
    {
        using var f = new HistoryFixture();
        f.Require();
        f.Estate.SetClock(new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero));
        RecordEnvelope other = f.Estate.Add("principal", f.Estate.OperatorId, """{"kind":"human","name":"Other","roles":["operator"]}""");
        f.Estate.Add("estate", other.Id, $$$"""{"name":"Test Estate","slug":"test-estate","purpose":"Exercise the record format.","operator":"{{{other.Id}}}","recordFormat":3,"policies":{"signatures":"optional","releaseAcceptance":["operator"]}}""", id: "01AAAAAAAAAAAAAAAAAAAAAAAA");
        RecordEnvelope device = f.Device(other.Id, "Other's device");
        RecordEnvelope admission = f.Estate.Add("admission", other.Id, HistoryFixture.Admission(other.Id, "Itself."), device.Reference, signer: f.Signer(device));
        f.Estate.SetClock(new DateTimeOffset(2026, 10, 13, 13, 0, 0, TimeSpan.Zero));
        RecordEnvelope signed = f.Estate.Add("work-order", other.Id, HistoryFixture.Order("ROOT-2", "Signed by the other device"), signer: f.Signer(device));
        EstateModel model = f.Load();
        Assert.Equal(f.Estate.OperatorId, model.RootOperator);
        Assert.NotEqual(admission.Id, SignatureChecks.FirstAuthority(model, model.RootOperator)?.Id);
        Assert.False(SignatureChecks.IsAdmitted(model, device));
        Assert.False(SignatureChecks.IsAuthentic(model, signed));
        Assert.False(model.Counts(signed));
        Assert.True(SignatureChecks.IsAdmitted(model, f.OperatorDevice));
    }
}
