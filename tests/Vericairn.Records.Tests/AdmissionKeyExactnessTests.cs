// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Globalization;
using System.Text.Json.Nodes;
using Vericairn.Records.Estate;
using Vericairn.Records.Model;
using Vericairn.Records.Signing;
using Vericairn.Records.Types;

namespace Vericairn.Records.Tests;

/// <summary>
/// Which admissions count is kept by what it reads of a set of closings, the records they refuse and the grant closings among them
/// (FIX-131), and gives the answers it gave when it was kept under the ids of every closing in the set: each test compares a model judged
/// with that key, kept as <see cref="EstateTrust.AdmissionsKeyedByClosingIdsForTests"/>, with the same model judged as it is now, on
/// generated histories in which closings refuse admissions, principal and grant revisions or nothing, and grant closings bound what an
/// administrator admits; an audit judges afresh every judgment answered from what was kept and finds none that differs; and the counters
/// show many closings that refuse nothing sharing one judgment.
/// </summary>
public sealed class AdmissionKeyExactnessTests(ITestOutputHelper output)
{
    [Fact]
    public void OnGeneratedHistoriesTheKeyByRefusedRecordsAnswersAsTheKeyByClosingIds()
    {
        long judgments = 0, oracleJudgments = 0, audits = 0;
        int refusedAdmissions = 0, closingCount = 0, grantClosings = 0, notCounting = 0;
        for (int seed = 1; seed <= 24; seed++)
        {
            using var f = new HistoryFixture();
            Generate(f, new Random(seed));
            EstateModel oracle = Load(f, keyedByClosingIds: true, audited: false);
            EstateModel model = Load(f, keyedByClosingIds: false, audited: true);
            AssertSameJudgment(oracle, model, $"Seed {seed}");

            Assert.Equal(0, model.Trust.AdmissionAuditMismatches);
            Assert.True(model.Trust.AdmissionJudgments <= oracle.Trust.AdmissionJudgments, $"Seed {seed}: {model.Trust.AdmissionJudgments} judgments against {oracle.Trust.AdmissionJudgments}.");
            judgments += model.Trust.AdmissionJudgments;
            oracleJudgments += oracle.Trust.AdmissionJudgments;
            audits += model.Trust.AdmissionAudits;
            closingCount += model.Trust.Closings.Count;
            grantClosings += model.Trust.Closings.Count(c => c.Kind == "grant");
            notCounting += model.Trust.NotCounting().Count;
            refusedAdmissions += model.Findings.Count(x => x.Code == "history.after-closing" && x.Location.StartsWith("records/admission/", StringComparison.Ordinal));
        }

        string counts = string.Create(CultureInfo.InvariantCulture,
            $"{closingCount} closings acting ({grantClosings} of grants), {refusedAdmissions} admissions refused by a closing, {notCounting} records not counting; {judgments} judgments of which admissions count against {oracleJudgments} keyed by closing ids; {audits} kept judgments audited");
        output.WriteLine(counts);
        Assert.True(refusedAdmissions > 0 && grantClosings > 0 && audits > 0 && judgments < oracleJudgments, counts);
    }

    [Fact]
    public void ClosingsThatRefuseNothingAdmissionsReadShareOneJudgment()
    {
        const int Rotations = 16;
        using HistoryFixture closedEach = new(), closedFirst = new();
        WriteRotations(closedEach, Rotations, closeEach: true);
        WriteRotations(closedFirst, Rotations, closeEach: false);
        EstateModel each = Load(closedEach, keyedByClosingIds: false, audited: true);
        EstateModel first = Load(closedFirst, keyedByClosingIds: false, audited: false);
        EstateModel eachByIds = Load(closedEach, keyedByClosingIds: true, audited: false);
        AssertSameJudgment(eachByIds, each, "A closing before each rotation");
        Assert.Equal(Rotations, each.Trust.Closings.Count);
        Assert.Single(first.Trust.Closings);
        output.WriteLine($"{each.Trust.AdmissionJudgments} judgments with a closing before each of {Rotations} rotations ({eachByIds.Trust.AdmissionJudgments} keyed by closing ids); {first.Trust.AdmissionJudgments} with a closing before the first only");

        // Every closing names the operator's first device, which signed every admission, and witnesses them all: each set of closings a
        // closing is judged against reads as no closings, so sixteen closings judge admissions as often as one, where keyed by closing ids
        // each set was judged on its own.
        Assert.Equal(first.Trust.AdmissionJudgments, each.Trust.AdmissionJudgments);
        Assert.True(eachByIds.Trust.AdmissionJudgments >= Rotations, $"{eachByIds.Trust.AdmissionJudgments} judgments keyed by closing ids.");
        Assert.Equal(0, each.Trust.AdmissionAuditMismatches);
        Assert.True(each.Trust.AdmissionAudits > 0);
    }

    [Fact]
    public void ClosingsRefusingDifferentAdmissionsAreJudgedApart()
    {
        using var f = new HistoryFixture();
        f.Require();
        f.Adopt();
        // Written in a copy: two admissions by the operator's first device, each held back until a closing of the key that signed it, so
        // the second closing is judged against a closing refusing the first and the closings that act refuse both.
        (_, RecordEnvelope firstAdmission) = f.Admitted(f.Estate.OperatorId, "Admitted with the first key");
        byte[] firstBytes = f.Remove(firstAdmission);
        f.Close("key", f.OperatorDevice.Id, f.Estate.OperatorId, f.OperatorSigner);
        (RecordEnvelope rotated, StoreSigner signer) = f.Rotate(f.OperatorDevice, f.OperatorSigner);
        RecordEnvelope second = f.Device(f.Estate.OperatorId, "Admitted with the second key");
        RecordEnvelope secondAdmission = f.Estate.Add("admission", f.Estate.OperatorId, HistoryFixture.Admission(f.Estate.OperatorId, "Admitted with the second key."), second.Reference, signer: signer);
        byte[] secondBytes = f.Remove(secondAdmission);
        f.Close("key", rotated.Id, f.Estate.OperatorId, signer);
        f.Rotate(rotated, signer, f.OperatorDevice.Id);
        f.Restore(firstAdmission, firstBytes);
        f.Restore(secondAdmission, secondBytes);

        EstateModel oracle = Load(f, keyedByClosingIds: true, audited: false);
        EstateModel model = Load(f, keyedByClosingIds: false, audited: true);
        AssertSameJudgment(oracle, model, "Two key closings");
        Assert.Equal(2, model.Trust.Closings.Count);
        Assert.False(model.Trust.AdmissionCounts(model.ById[firstAdmission.Id], EstateTrust.Mode.Anchors));
        Assert.False(model.Trust.AdmissionCounts(model.ById[secondAdmission.Id], EstateTrust.Mode.Anchors));
        Assert.Contains(model.Findings, x => x.Code == "history.after-closing" && x.Location == firstAdmission.ExpectedPath + "#signatures");
        Assert.Contains(model.Findings, x => x.Code == "history.after-closing" && x.Location == secondAdmission.ExpectedPath + "#signatures");
        Assert.Equal(0, model.Trust.AdmissionAuditMismatches);
    }

    /// <summary>Loads the estate with which admissions count kept under closing ids, or under refused records with every kept answer audited.</summary>
    private static EstateModel Load(HistoryFixture f, bool keyedByClosingIds, bool audited)
    {
        EstateTrust.AdmissionsKeyedByClosingIdsForTests = keyedByClosingIds;
        EstateTrust.AuditAdmissionsForTests = audited;
        try
        {
            EstateModel model = f.Load();
            _ = model.Trust;
            return model;
        }
        finally
        {
            EstateTrust.AdmissionsKeyedByClosingIdsForTests = false;
            EstateTrust.AuditAdmissionsForTests = false;
        }
    }

    /// <summary>Asserts that two judgments of one estate agree on everything closings and admissions decide.</summary>
    private static void AssertSameJudgment(EstateModel expected, EstateModel actual, string label)
    {
        Assert.Equal(expected.Trust.Closings.Select(c => c.Record.Id), actual.Trust.Closings.Select(c => c.Record.Id));
        Assert.Equal(Describe(expected.Trust.ClosingFindings), Describe(actual.Trust.ClosingFindings));
        Assert.Equal(expected.Trust.NotCounting().Order(StringComparer.Ordinal), actual.Trust.NotCounting().Order(StringComparer.Ordinal));
        Assert.Equal(expected.Trust.Unseating().Order(StringComparer.Ordinal), actual.Trust.Unseating().Order(StringComparer.Ordinal));
        Assert.Equal(Describe(expected.Trust.AdmissionFindings()), Describe(actual.Trust.AdmissionFindings()));
        foreach (RecordEnvelope admission in expected.Records.Where(r => r.Type == "admission"))
        {
            foreach (EstateTrust.Mode mode in (ReadOnlySpan<EstateTrust.Mode>)[EstateTrust.Mode.Signatures, EstateTrust.Mode.Anchors])
            {
                bool want = expected.Trust.AdmissionCounts(admission, mode);
                Assert.True(want == actual.Trust.AdmissionCounts(actual.ById[admission.Id], mode), $"{label}: admission {admission.Id} counts {want} keyed by closing ids, {!want} keyed by refused records ({mode}).");
            }
        }

        Assert.Equal(Describe(expected.Findings), Describe(actual.Findings));
    }

    private static string[] Describe(IEnumerable<Finding> findings) => [.. findings.Select(x => $"{x.Severity} {x.Code} {x.Location} {x.Message}")];

    /// <summary>
    /// A history in an adopted estate holding the operator's first device, the builder's device and an administrator's device under a grant
    /// of administer: rotations of each device, often after a closing of the key they replace; admissions of new devices by the operator
    /// and by the administrator, revocations, and principal revisions, some written in a copy and put back after the closings that
    /// follow, some dated back; closings of the builder's or the administrator's device; closings of the administrator's grant, each
    /// followed by a narrowing; and checkpoints.
    /// </summary>
    private static void Generate(HistoryFixture f, Random random)
    {
        RecordEnvelope decision = f.Decision();
        RecordEnvelope person = f.Estate.Add("principal", f.Estate.OperatorId, """{"kind":"human","name":"Administrator","roles":["reviewer"]}""", signer: f.OperatorSigner);
        (RecordEnvelope personDevice, _) = f.Admitted(person.Id, "The administrator's device");
        RecordEnvelope grant = f.Grant(person.Id, decision, [Scopes.Administer, Scopes.LocalEngineering]);
        f.Estate.SetClock(f.Estate.Clock.AddHours(1));
        f.Require();
        f.Adopt();
        string[] principals = [f.Estate.OperatorId, f.Estate.BuilderId, person.Id];
        List<(RecordEnvelope Revision, IRecordSigner Signer)>[] lines = [[(f.OperatorDevice, f.OperatorSigner)], [(f.BuilderDevice, f.BuilderSigner)], [(personDevice, f.Signer(personDevice))]];
        var admitted = new List<(RecordEnvelope Device, RecordEnvelope Admission)>();
        var held = new List<(RecordEnvelope Record, byte[] Bytes)>();
        var closedDevices = new HashSet<int>();
        RecordEnvelope currentGrant = grant;
        RecordEnvelope currentPerson = person;
        for (int step = 0; step < 16; step++)
        {
            string name = "Step " + step.ToString(CultureInfo.InvariantCulture);
            IRecordSigner operatorSigner = lines[0][^1].Signer;
            IRecordSigner personSigner = lines[2][^1].Signer;
            bool copy = random.Next(3) == 0;
            switch (random.Next(10))
            {
                case 0 or 1:
                {
                    int d = random.Next(lines.Length);
                    (RecordEnvelope at, IRecordSigner signer) = lines[d][^1];
                    if (random.Next(3) > 0) f.Close("key", at.Id, principals[d], signer);
                    (RecordEnvelope rotation, StoreSigner next) = f.Rotate(at, signer, lines[d][0].Revision.Id);
                    lines[d].Add((rotation, next));
                    break;
                }

                case 2:
                {
                    int d = random.Next(lines.Length);
                    f.Close("key", lines[d][^1].Revision.Id, principals[d], lines[d][^1].Signer);
                    break;
                }

                case 3:
                {
                    RecordEnvelope device = f.Device(f.Estate.OperatorId, name + " device");
                    Keep(device, f.Estate.Add("admission", f.Estate.OperatorId, HistoryFixture.Admission(f.Estate.OperatorId, name + "."), device.Reference, signer: operatorSigner));
                    break;
                }

                case 4:
                {
                    RecordEnvelope device = f.Device(person.Id, name + " device");
                    Keep(device, f.Estate.Add("admission", person.Id, HistoryFixture.Admission(person.Id, name + "."), device.Reference, signer: personSigner));
                    break;
                }

                case 5 when admitted.Count > 0:
                {
                    (RecordEnvelope device, RecordEnvelope admission) = admitted[random.Next(admitted.Count)];
                    RecordEnvelope revocation = f.Revoke(device, admission, operatorSigner);
                    if (copy) held.Add((revocation, f.Remove(revocation)));
                    break;
                }

                case 6:
                {
                    // A closing of the administrator's grant, then a narrowing; and an admission by the administrator dated into the grant's time.
                    f.Close("grant", currentGrant.Id, f.Estate.OperatorId, operatorSigner);
                    currentGrant = f.Grant(person.Id, decision, random.Next(2) == 0 ? [Scopes.LocalEngineering] : [Scopes.Administer, Scopes.LocalEngineering], supersedes: currentGrant.Id);
                    DateTimeOffset resume = f.Estate.Clock;
                    f.Estate.SetClock(grant.AtTime.AddMinutes(10 + step));
                    RecordEnvelope device = f.Device(person.Id, name + " backdated device");
                    Keep(device, f.Estate.Add("admission", person.Id, HistoryFixture.Admission(person.Id, name + " backdated."), device.Reference, signer: personSigner));
                    f.Estate.SetClock(resume);
                    break;
                }

                case 7:
                {
                    int d = 1 + random.Next(2);
                    if (closedDevices.Add(d)) f.Close("device", lines[d][0].Revision.Id, f.Estate.OperatorId, operatorSigner);
                    else f.Checkpoint(signer: operatorSigner);
                    break;
                }

                case 8:
                {
                    JsonObject body = HistoryFixture.Body(currentPerson);
                    body["name"] = "Administrator, " + name;
                    RecordEnvelope revision = f.Estate.Add("principal", f.Estate.OperatorId, body.ToJsonString(), supersedes: currentPerson.Id, signer: operatorSigner);
                    if (copy) held.Add((revision, f.Remove(revision)));
                    else currentPerson = revision;
                    break;
                }

                default:
                    f.Checkpoint(signer: operatorSigner);
                    break;
            }

            void Keep(RecordEnvelope device, RecordEnvelope admission)
            {
                if (copy) held.Add((admission, f.Remove(admission)));
                else admitted.Add((device, admission));
            }
        }

        foreach ((RecordEnvelope record, byte[] bytes) in held) f.Restore(record, bytes);
    }

    /// <summary>Rotations of the operator's first device, each after a closing of the key it replaces or, unless <paramref name="closeEach"/>, the first only.</summary>
    private static void WriteRotations(HistoryFixture f, int rotations, bool closeEach)
    {
        for (int i = 0; i < 4; i++) f.Admitted(f.Estate.BuilderId, "Builder device " + i.ToString(CultureInfo.InvariantCulture));
        f.Require();
        f.Adopt();
        RecordEnvelope current = f.OperatorDevice;
        IRecordSigner signer = f.OperatorSigner;
        for (int k = 0; k < rotations; k++)
        {
            if (closeEach || k == 0) f.Close("key", current.Id, f.Estate.OperatorId, signer);
            (current, StoreSigner next) = f.Rotate(current, signer, f.OperatorDevice.Id);
            signer = next;
        }
    }
}
