// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using Vericairn.Records.Estate;
using Vericairn.Records.Model;

namespace Vericairn.Records.Tests;

/// <summary>The root operator is named by the estate's first charter: the one witnessed history names once adopted, or else the manifest.</summary>
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

    [Fact]
    public void AnAdoptedEstateKeepsTheWitnessedCharterWhenTheManifestNamesAnother()
    {
        using var f = new HistoryFixture();
        RecordEnvelope order = f.Estate.Add("work-order", f.Estate.OperatorId, HistoryFixture.Order("ROOT-3"), signer: f.OperatorSigner);
        f.Require();
        f.Adopt();
        (RecordEnvelope other, RecordEnvelope charter, RecordEnvelope device) = Another(f);
        RecordEnvelope signed = f.Estate.Add("work-order", other.Id, HistoryFixture.Order("ROOT-4", "Signed by the other device"), signer: f.Signer(device));
        f.Estate.WriteManifest(charter.Id);

        EstateModel model = f.Load();
        Assert.Equal(f.Estate.Estate.Id, model.FirstEstate!.Id);
        Assert.Equal(f.Estate.OperatorId, model.RootOperator);
        Finding finding = Assert.Single(model.Findings, x => x.Code == "history.manifest-root");
        Assert.Equal(Severity.Error, finding.Severity);
        Assert.Contains(f.Estate.Estate.Id, finding.Message, StringComparison.Ordinal);
        Assert.True(SignatureChecks.IsAuthentic(model, order));
        Assert.True(model.Counts(order));
        Assert.False(SignatureChecks.IsAdmitted(model, device));
        Assert.False(model.Counts(signed));
        Assert.False(model.Counts(charter));
    }

    [Fact]
    public void ACharterFirstWitnessedAfterAdoptionNeverBecomesTheFirst()
    {
        using var f = new HistoryFixture();
        f.Require();
        f.Adopt();
        (_, RecordEnvelope charter, RecordEnvelope device) = Another(f);
        f.Estate.SetClock(new DateTimeOffset(2026, 10, 14, 13, 0, 0, TimeSpan.Zero));
        f.Checkpoint();
        f.Estate.WriteManifest(charter.Id);

        EstateModel model = f.Load();
        Assert.Equal(f.Estate.Estate.Id, model.FirstEstate!.Id);
        Assert.Equal(f.Estate.OperatorId, model.RootOperator);
        Assert.Contains("history.manifest-root", HistoryFixture.Codes(model));
        Assert.False(SignatureChecks.IsAdmitted(model, device));
    }

    [Fact]
    public void AnUnadoptedEstateFollowsTheManifest()
    {
        using var f = new HistoryFixture();
        f.Require();
        (RecordEnvelope other, RecordEnvelope charter, RecordEnvelope device) = Another(f);
        f.Estate.WriteManifest(charter.Id);

        EstateModel model = f.Load();
        Assert.Equal(charter.Id, model.FirstEstate!.Id);
        Assert.Equal(other.Id, model.RootOperator);
        Assert.True(SignatureChecks.IsAdmitted(model, device));
        Assert.DoesNotContain(model.Findings, x => x.Code is "history.manifest-root" or "history.root-ambiguous");
    }

    [Fact]
    public void TwoWitnessedFirstChartersLeaveNoRootOperatorAndNothingCounting()
    {
        using var f = new HistoryFixture();
        RecordEnvelope order = f.Estate.Add("work-order", f.Estate.OperatorId, HistoryFixture.Order("ROOT-5"), signer: f.OperatorSigner);
        f.Require();
        f.Adopt();
        (RecordEnvelope other, RecordEnvelope charter, RecordEnvelope device) = Another(f);
        f.Estate.WriteManifest(charter.Id);

        // A second adoption under the other charter, prepared while the first charter's file is away so that it never lists it.
        byte[] first = f.Remove(f.Estate.Estate);
        f.Estate.SetClock(new DateTimeOffset(2026, 10, 14, 13, 0, 0, TimeSpan.Zero));
        f.Checkpoint(by: other.Id, signer: f.Signer(device), trigger: "adoption");
        f.Restore(f.Estate.Estate, first);
        RecordEnvelope unsigned = f.Estate.Add("work-order", other.Id, HistoryFixture.Order("ROOT-6", "Unsigned by the other principal"));

        EstateModel model = f.Load();
        Assert.Contains("history.root-ambiguous", HistoryFixture.Codes(model));
        Assert.DoesNotContain("history.manifest-root", HistoryFixture.Codes(model));
        Assert.Null(model.FirstEstate);
        Assert.Null(model.RootOperator);
        Assert.False(model.Counts(order));
        Assert.False(model.Counts(unsigned));
        Assert.Empty(model.CountingRecords);
        Assert.False(SignatureChecks.IsAdmitted(model, device));
        Assert.False(SignatureChecks.IsAdmitted(model, f.OperatorDevice));
    }

    [Fact]
    public void ASecondAdoptionThatAlsoListsTheWitnessedCharterLeavesItTheFirst()
    {
        using var f = new HistoryFixture();
        f.Require();
        f.Adopt();
        (RecordEnvelope other, RecordEnvelope charter, RecordEnvelope device) = Another(f);
        f.Estate.WriteManifest(charter.Id);
        f.Estate.SetClock(new DateTimeOffset(2026, 10, 14, 13, 0, 0, TimeSpan.Zero));
        f.Checkpoint(by: other.Id, signer: f.Signer(device), trigger: "adoption");

        EstateModel model = f.Load();
        Assert.Equal(f.Estate.Estate.Id, model.FirstEstate!.Id);
        Assert.Equal(f.Estate.OperatorId, model.RootOperator);
        Assert.Contains("history.manifest-root", HistoryFixture.Codes(model));
        Assert.DoesNotContain("history.root-ambiguous", HistoryFixture.Codes(model));
        Assert.False(SignatureChecks.IsAdmitted(model, device));
    }

    /// <summary>Another principal, a charter of its own that supersedes nothing with a lower id, and a device it admits itself.</summary>
    private static (RecordEnvelope Other, RecordEnvelope Charter, RecordEnvelope Device) Another(HistoryFixture f)
    {
        f.Estate.SetClock(new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero));
        RecordEnvelope other = f.Estate.Add("principal", f.Estate.OperatorId, """{"kind":"human","name":"Other","roles":["operator"]}""");
        RecordEnvelope charter = f.Estate.Add("estate", other.Id, $$$"""{"name":"Test Estate","slug":"test-estate","purpose":"Exercise the record format.","operator":"{{{other.Id}}}","recordFormat":3,"policies":{"signatures":"optional","releaseAcceptance":["operator"]}}""", id: "01AAAAAAAAAAAAAAAAAAAAAAAA");
        RecordEnvelope device = f.Device(other.Id, "Other's device");
        f.Estate.Add("admission", other.Id, HistoryFixture.Admission(other.Id, "Itself."), device.Reference, signer: f.Signer(device));
        f.Estate.SetClock(new DateTimeOffset(2026, 10, 13, 13, 0, 0, TimeSpan.Zero));
        return (other, charter, device);
    }
}
