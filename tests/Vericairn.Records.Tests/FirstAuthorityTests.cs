// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using Vericairn.Records.Estate;
using Vericairn.Records.Model;

namespace Vericairn.Records.Tests;

/// <summary>
/// The estate's first authority is the root operator's self-admission that witnessed history names once adopted, or else the one the
/// manifest names, or the only one; never the earliest by its stated date.
/// </summary>
public class FirstAuthorityTests
{
    private static readonly DateTimeOffset Early = new(2020, 1, 1, 0, 0, 0, TimeSpan.Zero);

    /// <summary>A device record naming the root operator, admitting itself, dated before the estate began, and a work order it signs.</summary>
    private static (RecordEnvelope Device, RecordEnvelope Admission, RecordEnvelope Order) Earlier(HistoryFixture f, string key)
    {
        f.Estate.SetClock(Early);
        RecordEnvelope device = f.Device(f.Estate.OperatorId, "Earlier device");
        RecordEnvelope admission = f.Estate.Add("admission", f.Estate.OperatorId, HistoryFixture.Admission(f.Estate.OperatorId, "Itself."), device.Reference, signer: f.Signer(device));
        f.Estate.SetClock(new DateTimeOffset(2026, 10, 14, 13, 0, 0, TimeSpan.Zero));
        RecordEnvelope order = f.Estate.Add("work-order", f.Estate.OperatorId, HistoryFixture.Order(key, "Signed by the earlier device"), signer: f.Signer(device));
        return (device, admission, order);
    }

    private static RecordEnvelope First(HistoryFixture f) => SignatureChecks.FirstAuthority(f.Load(), f.Estate.OperatorId)!;

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void AnEarlierSelfAdmissionNeverDisplacesTheWitnessedFirstAuthority(bool named)
    {
        using var f = new HistoryFixture();
        RecordEnvelope real = f.Estate.Add("work-order", f.Estate.OperatorId, HistoryFixture.Order("FA-1"), signer: f.OperatorSigner);
        f.Require();
        RecordEnvelope first = First(f);
        if (named) f.Estate.NameFirstAuthority(first.Id);
        f.Adopt();
        (RecordEnvelope device, RecordEnvelope admission, RecordEnvelope forged) = Earlier(f, "FA-2");

        EstateModel model = f.Load();
        Assert.False(SignatureChecks.IsAdmitted(model, device));
        Assert.False(model.Counts(forged));
        Assert.DoesNotContain("history.manifest-root", HistoryFixture.Codes(model));
        Assert.DoesNotContain("history.root-ambiguous", HistoryFixture.Codes(model));
        if (named)
        {
            Assert.Equal(first.Id, SignatureChecks.FirstAuthority(model, model.RootOperator)?.Id);
            Assert.True(model.Trust.Adopted);
            Assert.True(SignatureChecks.IsAdmitted(model, f.OperatorDevice));
            Assert.True(model.Counts(real));
            Assert.Contains(model.Findings, x => x.Code == "signature.self-admission" && x.Location.StartsWith(admission.ExpectedPath, StringComparison.Ordinal));
            Assert.DoesNotContain("manifest.first-authority", HistoryFixture.Codes(model));
        }
        else
        {
            // With none named, the witnessed one is not chosen either: a forged history could be what witnesses it.
            Assert.Null(SignatureChecks.FirstAuthority(model, model.RootOperator));
            Assert.Contains("manifest.first-authority", HistoryFixture.Codes(model));
            Assert.False(SignatureChecks.IsAdmitted(model, f.OperatorDevice));
        }
    }

    [Fact]
    public void TheManifestNamesTheFirstAuthorityBeforeAdoption()
    {
        using var f = new HistoryFixture();
        RecordEnvelope real = f.Estate.Add("work-order", f.Estate.OperatorId, HistoryFixture.Order("FA-3"), signer: f.OperatorSigner);
        f.Require();
        RecordEnvelope first = First(f);
        f.Estate.NameFirstAuthority(first.Id);
        (RecordEnvelope device, _, RecordEnvelope forged) = Earlier(f, "FA-4");

        EstateModel model = f.Load();
        Assert.False(model.Trust.Adopted);
        Assert.Equal(first.Id, SignatureChecks.FirstAuthority(model, model.RootOperator)?.Id);
        Assert.True(SignatureChecks.IsAdmitted(model, f.OperatorDevice));
        Assert.False(SignatureChecks.IsAdmitted(model, device));
        Assert.True(model.Counts(real));
        Assert.False(model.Counts(forged));
        Assert.DoesNotContain("manifest.first-authority", HistoryFixture.Codes(model));
    }

    [Fact]
    public void SeveralSelfAdmissionsWithNoneNamedLeaveNoFirstAuthorityUntilOneIsNamed()
    {
        using var f = new HistoryFixture();
        RecordEnvelope real = f.Estate.Add("work-order", f.Estate.OperatorId, HistoryFixture.Order("FA-5"), signer: f.OperatorSigner);
        f.Require();
        RecordEnvelope first = First(f);
        (RecordEnvelope device, RecordEnvelope admission, RecordEnvelope forged) = Earlier(f, "FA-6");

        // Neither is chosen by its date: the estate reports it and no admission counts from either.
        EstateModel model = f.Load();
        Assert.Null(SignatureChecks.FirstAuthority(model, model.RootOperator));
        Finding finding = Assert.Single(model.Findings, x => x.Code == "manifest.first-authority");
        Assert.Equal(EstateLayout.ManifestPath, finding.Location);
        Assert.Contains(first.Id, finding.Message, StringComparison.Ordinal);
        Assert.Contains(admission.Id, finding.Message, StringComparison.Ordinal);
        Assert.Contains(f.OperatorDevice.Id, finding.Message, StringComparison.Ordinal);
        Assert.Contains(device.Id + " \"Earlier device\"", finding.Message, StringComparison.Ordinal);
        Assert.False(SignatureChecks.IsAdmitted(model, device));
        Assert.False(SignatureChecks.IsAdmitted(model, f.OperatorDevice));
        Assert.False(model.Counts(forged));
        Assert.False(model.Counts(real));

        // Naming one settles it.
        f.Estate.NameFirstAuthority(first.Id);
        model = f.Load();
        Assert.Equal(first.Id, SignatureChecks.FirstAuthority(model, model.RootOperator)?.Id);
        Assert.DoesNotContain("manifest.first-authority", HistoryFixture.Codes(model));
        Assert.True(model.Counts(real));
        Assert.False(model.Counts(forged));
    }

    [Fact]
    public void AManifestNamingNoSelfAdmissionLeavesNoFirstAuthority()
    {
        using var f = new HistoryFixture();
        RecordEnvelope real = f.Estate.Add("work-order", f.Estate.OperatorId, HistoryFixture.Order("FA-7"), signer: f.OperatorSigner);
        f.Estate.NameFirstAuthority(f.BuilderAdmission.Id);
        EstateModel model = f.Load();
        Assert.Null(SignatureChecks.FirstAuthority(model, model.RootOperator));
        Assert.Contains("manifest.first-authority", HistoryFixture.Codes(model));
        Assert.False(SignatureChecks.IsAdmitted(model, f.OperatorDevice));
        Assert.False(SignatureChecks.IsAuthentic(model, real));
    }

    [Fact]
    public void AnAdoptedEstateWhoseManifestNamesAnotherFirstAuthorityReadsUnsettled()
    {
        using var f = new HistoryFixture();
        RecordEnvelope real = f.Estate.Add("work-order", f.Estate.OperatorId, HistoryFixture.Order("FA-8"), signer: f.OperatorSigner);
        f.Require();
        RecordEnvelope first = First(f);
        f.Adopt();
        (RecordEnvelope device, RecordEnvelope admission, RecordEnvelope forged) = Earlier(f, "FA-9");
        f.Estate.NameFirstAuthority(admission.Id);

        EstateModel model = f.Load();
        Assert.Null(SignatureChecks.FirstAuthority(model, model.RootOperator));
        Assert.Contains("history.root-ambiguous", HistoryFixture.Codes(model));
        Finding finding = Assert.Single(model.Findings, x => x.Code == "history.manifest-root");
        Assert.Equal(EstateLayout.ManifestPath, finding.Location);
        Assert.DoesNotContain("restore", finding.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(SignatureChecks.IsAdmitted(model, device));
        Assert.False(SignatureChecks.IsAdmitted(model, f.OperatorDevice));
        Assert.False(model.Counts(real));
        Assert.False(model.Counts(forged));

        f.Estate.NameFirstAuthority(first.Id);
        model = f.Load();
        Assert.Equal(first.Id, SignatureChecks.FirstAuthority(model, model.RootOperator)?.Id);
        Assert.True(model.Counts(real));
        Assert.False(model.Counts(forged));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void AnAddedSelfAdmissionWithItsOwnAdoptionNeverTakesAnEstateThatHasNotAdopted(bool named)
    {
        using var f = new HistoryFixture();
        RecordEnvelope real = f.Estate.Add("work-order", f.Estate.OperatorId, HistoryFixture.Order("FA-12"), signer: f.OperatorSigner);
        f.Require();
        RecordEnvelope first = First(f);
        if (named) f.Estate.NameFirstAuthority(first.Id);
        (RecordEnvelope device, RecordEnvelope admission, RecordEnvelope forged) = Earlier(f, "FA-13");
        // An adoption under the earlier device, prepared while the first authority's file is away so that it never lists it.
        byte[] bytes = f.Remove(first);
        f.Estate.NameFirstAuthority(null);
        f.Checkpoint(signer: f.Signer(device), trigger: "adoption");
        f.Restore(first, bytes);
        if (named) f.Estate.NameFirstAuthority(first.Id);

        EstateModel model = f.Load();
        Assert.NotEqual(admission.Id, SignatureChecks.FirstAuthority(model, model.RootOperator)?.Id);
        Assert.False(SignatureChecks.IsAdmitted(model, device));
        Assert.False(model.Counts(forged));
        Assert.DoesNotContain(model.Findings, x => x.Message.Contains("firstAuthority to " + admission.Id, StringComparison.Ordinal));
        Assert.Contains(named ? "history.root-ambiguous" : "manifest.first-authority", HistoryFixture.Codes(model));
    }

    [Fact]
    public void TheEstatesOwnAdoptionSettlesTheFirstAuthorityItsManifestNames()
    {
        using var f = new HistoryFixture();
        RecordEnvelope real = f.Estate.Add("work-order", f.Estate.OperatorId, HistoryFixture.Order("FA-14"), signer: f.OperatorSigner);
        f.Require();
        RecordEnvelope first = First(f);
        f.Estate.NameFirstAuthority(first.Id);
        // Another self-admission present when the estate adopts: the adoption lists both, the manifest names the first.
        (RecordEnvelope device, _, RecordEnvelope forged) = Earlier(f, "FA-15");
        f.Adopt();
        EstateModel model = f.Load();
        Assert.True(model.Trust.Adopted);
        Assert.Equal(first.Id, SignatureChecks.FirstAuthority(model, model.RootOperator)?.Id);
        Assert.True(model.Counts(real));
        Assert.False(model.Counts(forged));
        Assert.False(SignatureChecks.IsAdmitted(model, device));
        Assert.DoesNotContain(model.Findings, x => x.Code is "history.manifest-root" or "history.root-ambiguous" or "manifest.first-authority");
    }

    [Fact]
    public void TwoWitnessedFirstAuthoritiesLeaveNothingCounting()
    {
        using var f = new HistoryFixture();
        RecordEnvelope real = f.Estate.Add("work-order", f.Estate.OperatorId, HistoryFixture.Order("FA-10"), signer: f.OperatorSigner);
        f.Require();
        RecordEnvelope first = First(f);
        f.Estate.NameFirstAuthority(first.Id);
        f.Adopt();
        (RecordEnvelope device, _, RecordEnvelope forged) = Earlier(f, "FA-11");

        // A second adoption under the earlier device, prepared while the first authority's file is away so that it never lists it.
        byte[] bytes = f.Remove(first);
        f.Estate.NameFirstAuthority(null);
        f.Checkpoint(signer: f.Signer(device), trigger: "adoption");
        f.Restore(first, bytes);
        f.Estate.NameFirstAuthority(first.Id);

        EstateModel model = f.Load();
        Assert.Contains("history.root-ambiguous", HistoryFixture.Codes(model));
        Assert.DoesNotContain("history.manifest-root", HistoryFixture.Codes(model));
        Assert.Null(SignatureChecks.FirstAuthority(model, model.RootOperator));
        Assert.False(model.Counts(real));
        Assert.False(model.Counts(forged));
        Assert.Empty(model.CountingRecords);
        Assert.False(SignatureChecks.IsAdmitted(model, device));
        Assert.False(SignatureChecks.IsAdmitted(model, f.OperatorDevice));
    }

    [Fact]
    public void AStraySelfAdmissionChangesNothingInAnAdoptedEstateAndOneWithItsOwnAdoptionUnsettlesIt()
    {
        using var f = new HistoryFixture();
        RecordEnvelope real = f.Estate.Add("work-order", f.Estate.OperatorId, HistoryFixture.Order("FA-16"), signer: f.OperatorSigner);
        f.Require();
        RecordEnvelope first = First(f);
        f.Estate.NameFirstAuthority(first.Id);
        f.Adopt();
        RecordEnvelope device = f.Device(f.Estate.OperatorId, "Stray device");
        f.Estate.Add("admission", f.Estate.OperatorId, HistoryFixture.Admission(f.Estate.OperatorId, "Itself."), device.Reference, signer: f.Signer(device));
        RecordEnvelope forged = f.Estate.Add("work-order", f.Estate.OperatorId, HistoryFixture.Order("FA-17", "Signed by the stray device"), signer: f.Signer(device));
        EstateModel model = f.Load();
        Assert.False(model.Trust.Unsettled);
        Assert.True(model.Counts(real));
        Assert.False(model.Counts(forged));

        // With an adoption of its own, prepared while the first authority's file is away, it leaves the copy unsettled, never taking it.
        byte[] bytes = f.Remove(first);
        f.Checkpoint(signer: f.Signer(device), trigger: "adoption");
        f.Restore(first, bytes);
        model = f.Load();
        Assert.True(model.Trust.Unsettled);
        Assert.Contains("history.root-ambiguous", HistoryFixture.Codes(model));
        Assert.False(model.Counts(real));
        Assert.False(model.Counts(forged));
        Assert.False(SignatureChecks.IsAdmitted(model, device));
    }
}
