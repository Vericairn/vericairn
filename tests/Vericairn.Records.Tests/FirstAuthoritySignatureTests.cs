// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using Vericairn.Records.Estate;
using Vericairn.Records.Identity;
using Vericairn.Records.Model;
using Vericairn.Records.Signing;

namespace Vericairn.Records.Tests;

/// <summary>
/// The first authority counts only when its self-admission's signature verifies (FIX-134): a self-admission the manifest names, or the
/// only one when it names none, whose signature does not verify under its device's key at its stated time is no first authority, the
/// copy reads manifest.first-authority naming it, and nothing signed under it counts until the manifest names one that verifies. The
/// defective admissions are the forged-admission probes of the review of CODE-72: an unrelated key stated later or earlier, the old key
/// after a rotation, the new key dated before it, an added foreign-key signature, another principal's device signature, a foreign key for
/// another device and a file edited after signing, with a forged device whose own records verify.
/// </summary>
public sealed class FirstAuthoritySignatureTests : IDisposable
{
    private static readonly DateTimeOffset Early = new(2020, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private readonly string keys = Path.Combine(Path.GetTempPath(), "vericairn-first-authority-probes-" + Ulid.NewUlid().Value);
    private readonly FileKeyStore store;

    public FirstAuthoritySignatureTests() => store = new FileKeyStore(keys);

    public static TheoryData<string> Probes() =>
    [
        "unrelated-key-later", "unrelated-key-earlier", "old-key-after-rotation", "new-key-before-rotation", "added-foreign-key-signature",
        "another-principals-device", "foreign-key-for-another-device", "edited-after-signing",
    ];

    /// <summary>A signer with a key no device record holds, naming the device given.</summary>
    private StoreSigner Unrelated(string device)
    {
        string key = Ulid.NewUlid().Value;
        store.Create(key);
        return new StoreSigner(store, key, device);
    }

    /// <summary>
    /// A self-admission of the root operator's first device whose signature does not verify as the probe says, written beside the genuine
    /// one; returns it with the code its verification reports. The clock is where it was afterwards.
    /// </summary>
    private (RecordEnvelope Admission, string Code) Defective(HistoryFixture f, string probe)
    {
        DateTimeOffset resume = f.Estate.Clock;
        string body = HistoryFixture.Admission(f.Estate.OperatorId, "The first authority.");
        RecordEnvelope Add(params IRecordSigner[] signers) =>
            f.Estate.Add("admission", f.Estate.OperatorId, body, f.OperatorDevice.Reference, signers: signers);
        switch (probe)
        {
            case "unrelated-key-later":
                f.Estate.SetClock(resume.AddDays(30));
                return (Add(Unrelated(f.OperatorDevice.Id)), "signature.invalid");
            case "unrelated-key-earlier":
                f.Estate.SetClock(Early);
                RecordEnvelope earlier = Add(Unrelated(f.OperatorDevice.Id));
                f.Estate.SetClock(resume);
                return (earlier, "signature.invalid");
            case "old-key-after-rotation":
            {
                f.Rotate(f.OperatorDevice, f.OperatorSigner);
                return (Add(f.OperatorSigner), "signature.key-time");
            }
            case "new-key-before-rotation":
            {
                (_, StoreSigner next) = f.Rotate(f.OperatorDevice, f.OperatorSigner);
                f.Estate.SetClock(f.OperatorDevice.AtTime);
                RecordEnvelope backdated = Add(next);
                f.Estate.SetClock(resume.AddSeconds(5));
                return (backdated, "signature.key-time");
            }
            case "added-foreign-key-signature":
                return (Add(f.OperatorSigner, Unrelated(f.OperatorDevice.Id)), "signature.invalid");
            case "another-principals-device":
                return (Add(f.OperatorSigner, f.BuilderSigner), "signature.principal");
            case "foreign-key-for-another-device":
            {
                (RecordEnvelope second, _) = f.Admitted(f.Estate.OperatorId, "Second operator device");
                return (Add(f.OperatorSigner, Unrelated(second.Id)), "signature.invalid");
            }
            case "edited-after-signing":
            {
                RecordEnvelope signed = Add(f.OperatorSigner);
                string path = f.PathOf(signed);
                string text = File.ReadAllText(path);
                Assert.Contains("The first authority.", text, StringComparison.Ordinal);
                File.WriteAllText(path, text.Replace("The first authority.", "The first authority, edited.", StringComparison.Ordinal));
                return (signed, "signature.invalid");
            }
            default:
                throw new ArgumentOutOfRangeException(nameof(probe), probe, null);
        }
    }

    private static Finding RootFinding(EstateModel model) => Assert.Single(model.Findings, x => x.Code == "manifest.first-authority");

    [Theory]
    [MemberData(nameof(Probes))]
    public void AManifestNamingASelfAdmissionThatDoesNotVerifyReadsNoFirstAuthorityUntilItNamesOneThatDoes(string probe)
    {
        using var f = new HistoryFixture();
        RecordEnvelope real = f.Estate.Add("work-order", f.Estate.OperatorId, HistoryFixture.Order("FAS-1"), signer: f.OperatorSigner);
        RecordEnvelope builderOrder = f.Estate.Add("work-order", f.Estate.BuilderId, HistoryFixture.Order("FAS-2"), signer: f.BuilderSigner);
        RecordEnvelope genuine = SignatureChecks.FirstAuthority(f.Load(), f.Estate.OperatorId)!;
        (RecordEnvelope bad, string code) = Defective(f, probe);
        Assert.Equal(f.OperatorDevice.Id, bad.Subject?.Id);

        // Read as the first authority, the defective admission's signature fails as the estate verifies every signature, so the
        // command's evidence reads that problem, and the estate reads no first authority.
        FirstAuthorityReading reading = EstateTrust.ReadFirstAuthority(f.Root, EstateLoader.EnumerateRecordFiles(f.Root), File.ReadAllBytes(Path.Combine(f.Root, EstateLayout.ManifestPath)), null);
        FirstAuthorityEvidence evidence = Assert.Single(reading.Candidates, c => c.Admission.Id == bad.Id);
        Assert.StartsWith(code == "signature.invalid" && probe == "edited-after-signing" ? "its content does not match" : code + ": ", evidence.SignatureProblem, StringComparison.Ordinal);
        Assert.Null(Assert.Single(reading.Candidates, c => c.Admission.Id == genuine.Id).SignatureProblem);

        f.Estate.NameFirstAuthority(bad.Id);
        EstateModel model = f.Load();
        Assert.Null(SignatureChecks.FirstAuthority(model, model.RootOperator));
        Finding finding = RootFinding(model);
        Assert.Equal(Severity.Error, finding.Severity);
        Assert.Equal(EstateLayout.ManifestPath, finding.Location);
        Assert.Contains(bad.Id, finding.Message, StringComparison.Ordinal);
        Assert.Contains("does not verify", finding.Message, StringComparison.Ordinal);
        // Nothing signed under it counts: no device rests on it, and no record its device or a device it would admit signed counts.
        Assert.False(SignatureChecks.IsAdmitted(model, f.OperatorDevice));
        Assert.False(SignatureChecks.IsAdmitted(model, f.BuilderDevice));
        Assert.False(model.Counts(real));
        Assert.False(model.Counts(builderOrder));
        Assert.False(model.Counts(bad));

        // Naming the genuine one settles it, and the defective one reports its own error.
        f.Estate.NameFirstAuthority(genuine.Id);
        model = f.Load();
        Assert.Equal(genuine.Id, SignatureChecks.FirstAuthority(model, model.RootOperator)?.Id);
        Assert.DoesNotContain("manifest.first-authority", HistoryFixture.Codes(model));
        Assert.True(SignatureChecks.IsAdmitted(model, f.OperatorDevice));
        Assert.True(model.Counts(real));
        Assert.True(model.Counts(builderOrder));
        Assert.False(model.Counts(bad));
        Assert.Contains(model.Findings, x => x.Severity == Severity.Error && x.Location.StartsWith(bad.ExpectedPath, StringComparison.Ordinal));
    }

    [Theory]
    [MemberData(nameof(Probes))]
    public void TheOnlySelfAdmissionCountsOnlyWhenItsSignatureVerifies(string probe)
    {
        using var f = new HistoryFixture();
        RecordEnvelope real = f.Estate.Add("work-order", f.Estate.OperatorId, HistoryFixture.Order("FAS-3"), signer: f.OperatorSigner);
        RecordEnvelope genuine = SignatureChecks.FirstAuthority(f.Load(), f.Estate.OperatorId)!;
        (RecordEnvelope bad, _) = Defective(f, probe);
        byte[] bytes = f.Remove(genuine);

        // With the genuine one away and none named, the defective one is the only candidate, and it is no first authority.
        EstateModel model = f.Load();
        Assert.Null(SignatureChecks.FirstAuthority(model, model.RootOperator));
        Finding finding = RootFinding(model);
        Assert.Contains(bad.Id, finding.Message, StringComparison.Ordinal);
        Assert.Contains("only self-admission", finding.Message, StringComparison.Ordinal);
        Assert.False(SignatureChecks.IsAdmitted(model, f.OperatorDevice));
        Assert.False(model.Counts(real));

        // Restored, the genuine one is a candidate beside it; none is named, so neither is chosen until the manifest names one.
        f.Restore(genuine, bytes);
        model = f.Load();
        Assert.Null(SignatureChecks.FirstAuthority(model, model.RootOperator));
        Assert.Contains("several", RootFinding(model).Message.Replace("more than once", "several", StringComparison.Ordinal), StringComparison.Ordinal);
        f.Estate.NameFirstAuthority(genuine.Id);
        model = f.Load();
        Assert.Equal(genuine.Id, SignatureChecks.FirstAuthority(model, model.RootOperator)?.Id);
        Assert.True(model.Counts(real));
    }

    [Fact]
    public void AForgedDeviceWhoseOwnRecordsVerifyCountsForNothingWhenItsSelfAdmissionDoesNot()
    {
        // The device record and the records it signs verify under its own key; only its self-admission was signed with another key that
        // names it. Read as the first authority, everything the device signed would count.
        using var f = new HistoryFixture();
        RecordEnvelope real = f.Estate.Add("work-order", f.Estate.OperatorId, HistoryFixture.Order("FAS-4"), signer: f.OperatorSigner);
        RecordEnvelope device = f.Device(f.Estate.OperatorId, "Forged device");
        RecordEnvelope admission = f.Estate.Add("admission", f.Estate.OperatorId, HistoryFixture.Admission(f.Estate.OperatorId, "Itself."), device.Reference, signer: Unrelated(device.Id));
        RecordEnvelope forged = f.Estate.Add("work-order", f.Estate.OperatorId, HistoryFixture.Order("FAS-5", "Signed by the forged device"), signer: f.Signer(device));
        RecordEnvelope builder = f.Device(f.Estate.BuilderId, "Builder device admitted by the forged device");
        f.Estate.Add("admission", f.Estate.OperatorId, HistoryFixture.Admission(f.Estate.BuilderId, "Admitted under the forged device."), builder.Reference, signer: f.Signer(device));
        RecordEnvelope builderOrder = f.Estate.Add("work-order", f.Estate.BuilderId, HistoryFixture.Order("FAS-6"), signer: f.Signer(builder));
        f.Estate.NameFirstAuthority(admission.Id);

        EstateModel model = f.Load();
        Assert.Null(SignatureChecks.FirstAuthority(model, model.RootOperator));
        Assert.Contains(admission.Id, RootFinding(model).Message, StringComparison.Ordinal);
        Assert.False(SignatureChecks.IsAdmitted(model, device));
        Assert.False(SignatureChecks.IsAdmitted(model, builder));
        Assert.False(model.Counts(forged));
        Assert.False(model.Counts(builderOrder));
        Assert.False(model.Counts(real));
        Assert.Contains(model.Findings, x => x.Code == "signature.invalid" && x.Location.StartsWith(admission.ExpectedPath, StringComparison.Ordinal));
    }

    [Fact]
    public void AnAdoptedEstateWhoseCheckpointsConfirmItsGenuineFirstAuthorityIsUnaffected()
    {
        // The adoption witnesses the genuine first authority, which the manifest names; a defective self-admission added later and named
        // by hand never takes it: the copy reads unsettled, as for any other admission the manifest names that history does not confirm,
        // and naming the genuine one again restores the reading.
        using var f = new HistoryFixture();
        RecordEnvelope real = f.Estate.Add("work-order", f.Estate.OperatorId, HistoryFixture.Order("FAS-7"), signer: f.OperatorSigner);
        f.Require();
        RecordEnvelope genuine = SignatureChecks.FirstAuthority(f.Load(), f.Estate.OperatorId)!;
        f.Estate.NameFirstAuthority(genuine.Id);
        f.Adopt();
        EstateModel model = f.Load();
        Assert.True(model.Trust.Adopted);
        Assert.Equal(genuine.Id, SignatureChecks.FirstAuthority(model, model.RootOperator)?.Id);
        Assert.False(model.Findings.HasErrors, string.Join('\n', model.Findings));

        (RecordEnvelope bad, _) = Defective(f, "unrelated-key-earlier");
        model = f.Load();
        Assert.Equal(genuine.Id, SignatureChecks.FirstAuthority(model, model.RootOperator)?.Id);
        Assert.True(model.Counts(real));
        Assert.False(model.Counts(bad));

        f.Estate.NameFirstAuthority(bad.Id);
        model = f.Load();
        Assert.Null(SignatureChecks.FirstAuthority(model, model.RootOperator));
        Assert.True(model.Trust.Unsettled);
        Assert.Contains("history.root-ambiguous", HistoryFixture.Codes(model));
        Assert.Contains(bad.Id, RootFinding(model).Message, StringComparison.Ordinal);
        Assert.False(model.Counts(real));
        Assert.Empty(model.CountingRecords);

        f.Estate.NameFirstAuthority(genuine.Id);
        model = f.Load();
        Assert.Equal(genuine.Id, SignatureChecks.FirstAuthority(model, model.RootOperator)?.Id);
        Assert.True(model.Trust.Adopted);
        Assert.True(model.Counts(real));
    }

    [Fact]
    public void ADefectiveSelfAdmissionIsNeverWitnessedFirst()
    {
        // An adoption signed by the root operator's device while the manifest named a defective self-admission stated earlier, the
        // genuine one's file away: read with the defective one as the first authority, that adoption witnesses it before the genuine one.
        // It is no first authority under any reading, so the adoption confirms nothing for it, and the genuine one named settles the copy.
        using var f = new HistoryFixture();
        RecordEnvelope real = f.Estate.Add("work-order", f.Estate.OperatorId, HistoryFixture.Order("FAS-8"), signer: f.OperatorSigner);
        f.Require();
        RecordEnvelope genuine = SignatureChecks.FirstAuthority(f.Load(), f.Estate.OperatorId)!;
        (RecordEnvelope bad, _) = Defective(f, "unrelated-key-earlier");
        byte[] bytes = f.Remove(genuine);
        f.Estate.NameFirstAuthority(bad.Id);
        RecordEnvelope adoption = f.Adopt();
        f.Restore(genuine, bytes);

        EstateModel model = f.Load();
        Assert.Null(SignatureChecks.FirstAuthority(model, model.RootOperator));
        Assert.Contains(bad.Id, RootFinding(model).Message, StringComparison.Ordinal);
        Assert.DoesNotContain("history.root-ambiguous", HistoryFixture.Codes(model));
        Assert.DoesNotContain("history.manifest-root", HistoryFixture.Codes(model));
        Assert.False(model.Trust.Adopted);
        Assert.False(model.Counts(real));
        Assert.False(model.Counts(adoption));

        f.Estate.NameFirstAuthority(genuine.Id);
        model = f.Load();
        Assert.Equal(genuine.Id, SignatureChecks.FirstAuthority(model, model.RootOperator)?.Id);
        Assert.True(model.Trust.Adopted);
        Assert.True(model.Counts(real));
        Assert.False(model.Counts(bad));
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(keys)) Directory.Delete(keys, recursive: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Best effort.
        }
    }
}
