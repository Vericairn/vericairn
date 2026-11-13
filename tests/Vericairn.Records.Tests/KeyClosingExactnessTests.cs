// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using Vericairn.Records.Estate;
using Vericairn.Records.Model;
using Vericairn.Records.Signing;

namespace Vericairn.Records.Tests;

/// <summary>
/// The key-closing check (FIX-121) gives the answers it gave when it verified each device revision's signatures under every closed key:
/// each test compares it with that check, kept as <see cref="EstateTrust.RevisionRefusedByVerifying"/>, on every device revision of
/// generated histories, of a signature that verifies under its own key and another, of signatures and closed keys outside the
/// arithmetic, and with a wrong recovery put in place of the arithmetic; and its counters show each revision signature recovered once,
/// however many closings read it.
/// </summary>
public sealed class KeyClosingExactnessTests(ITestOutputHelper output)
{
    [Fact]
    public void OnGeneratedHistoriesTheCheckAnswersAsVerifyingUnderEachClosedKeyAndNeverFallsBack()
    {
        int refusedByKey = 0, refusedByDevice = 0, kept = 0;
        long recoveries = 0;
        for (int seed = 1; seed <= 24; seed++)
        {
            using var f = new HistoryFixture();
            Generate(f, new Random(seed));
            EstateModel model = f.Load();
            foreach (RecordEnvelope revision in DeviceRevisions(model))
            {
                RecordEnvelope previous = model.ById[revision.Supersedes!];
                bool expected = model.Trust.RevisionRefusedByVerifying(revision, previous);
                Assert.True(expected == model.Trust.DeviceRevisionRefused(revision), $"Seed {seed}: device revision {revision.Id} is read {!expected} refused, verified {expected}.");
                if (!expected) kept++;
                else if (model.Trust.Closings.Any(c => c.Kind == "device" && c.Identity == model.Trust.IdentityOf(previous))) refusedByDevice++;
                else refusedByKey++;
            }

            // The guards never fire on signatures their devices made: every recovery is confirmed and complete.
            Assert.Equal(0, model.Trust.RecoveryFallbacks);
            Assert.Equal(0, model.Trust.DroppedKeys);
            Assert.Equal(0, model.Trust.ClosedKeyVerifications);
            recoveries += model.Trust.KeyRecoveries;
        }

        string counts = $"{refusedByKey} revisions refused by a key closing, {refusedByDevice} by a device closing, {kept} kept, {recoveries} signatures recovered";
        output.WriteLine(counts);
        Assert.True(refusedByKey > 0 && kept > 0 && recoveries > 0, counts);
    }

    [Theory]
    [InlineData("the other key it verifies under", true)]
    [InlineData("its own key", true)]
    [InlineData("an unrelated key", false)]
    public void ARevisionIsRefusedExactlyWhenTheClosedKeyIsOneItsSignatureVerifiesUnder(string closed, bool refused)
    {
        using var f = new HistoryFixture();
        f.Require();
        f.Adopt();
        // A rename of the builder's device signed with its key, written in a copy: its file is held back until the closing is written.
        RecordEnvelope rename = Rename(f, f.BuilderDevice, f.Estate.BuilderId, f.BuilderSigner, "Renamed in a copy");
        byte[] bytes = f.Remove(rename);
        byte[] digest = RecordSigning.Digest(rename.ComputedHash);
        string signature = Assert.Single(rename.Signatures).Signature;
        string own = HistoryFixture.Body(f.BuilderDevice)["publicKey"]!.GetValue<string>();
        byte[] ownPoint = RecordSigning.PublicPoint(own)!;
        byte[] other = P256KeyRecovery.Candidates(digest, Base64Url.DecodeFromChars(signature))!.Single(k => !k.AsSpan().SequenceEqual(ownPoint));
        using var unrelated = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        string key = closed switch
        {
            "the other key it verifies under" => Encode(other),
            "its own key" => own,
            _ => RecordSigning.EncodePublicKey(unrelated.ExportSubjectPublicKeyInfo()),
        };
        Assert.Equal(refused, RecordSigning.Verify(key, digest, signature));

        // A revision of the device carrying that key, which counts for nothing without a signature by it, and the device's closing of the key.
        JsonObject body = HistoryFixture.Body(f.BuilderDevice);
        body["publicKey"] = key;
        body["name"] = "Carrier of the closed key";
        RecordEnvelope carrier = f.Estate.Add("device", f.Estate.BuilderId, body.ToJsonString(), supersedes: f.BuilderDevice.Id, signer: f.BuilderSigner);
        RecordEnvelope closing = f.Close("key", carrier.Id, f.Estate.BuilderId, f.BuilderSigner);
        f.Restore(rename, bytes);

        EstateModel model = f.Load();
        Assert.Contains(model.Trust.Closings, c => c.Record.Id == closing.Id);
        RecordEnvelope loaded = model.ById[rename.Id];
        Assert.Equal(refused, model.Trust.RevisionRefusedByVerifying(loaded, model.ById[f.BuilderDevice.Id]));
        Assert.Equal(refused, model.Trust.DeviceRevisionRefused(loaded));
        Assert.Equal(!refused, model.Counts(loaded));
        Assert.True(model.Trust.KeyRecoveries > 0);
        Assert.Equal(0, model.Trust.RecoveryFallbacks);
        Assert.Equal(0, model.Trust.ClosedKeyVerifications);
    }

    [Fact]
    public void SignaturesOutsideTheArithmeticAndAClosedKeyThatDoesNotImportAreVerifiedAsBefore()
    {
        using var f = new HistoryFixture();
        f.Require();
        f.Adopt();
        // Written in a copy: renames of the builder's device whose signatures' r is the group order or s is zero, and one signed with its key.
        byte[] valid = Convert.FromHexString("6B17D1F2E12C4247F8BCE6E563A440F277037D812DEB33A0F4A13945D898C296");
        byte[] order = Convert.FromHexString("FFFFFFFF00000000FFFFFFFFFFFFFFFFBCE6FAADA7179E84F3B9CAC2FC632551");
        var held = new List<(RecordEnvelope Record, byte[] Bytes)>();
        foreach (byte[] signature in (byte[][])[[.. order, .. valid], [.. valid, .. new byte[32]]])
        {
            RecordEnvelope malformed = Rename(f, f.BuilderDevice, f.Estate.BuilderId, new FixedSigner(f.BuilderDevice.Id, signature), "Malformed");
            held.Add((malformed, f.Remove(malformed)));
        }

        RecordEnvelope signed = Rename(f, f.BuilderDevice, f.Estate.BuilderId, f.BuilderSigner, "Signed with the closed key");
        held.Add((signed, f.Remove(signed)));

        // The device closes a key that does not import, carried by a revision of the device, and then its own key.
        JsonObject body = HistoryFixture.Body(f.BuilderDevice);
        body["publicKey"] = "AAAA";
        RecordEnvelope carrier = f.Estate.Add("device", f.Estate.BuilderId, body.ToJsonString(), supersedes: f.BuilderDevice.Id, signer: f.BuilderSigner);
        f.Close("key", carrier.Id, f.Estate.BuilderId, f.BuilderSigner);
        f.Close("key", f.BuilderDevice.Id, f.Estate.BuilderId, f.BuilderSigner);
        foreach ((RecordEnvelope record, byte[] bytes) in held) f.Restore(record, bytes);

        EstateModel model = f.Load();
        Assert.Equal(2, model.Trust.Closings.Count(c => c.Kind == "key"));
        AssertAnswersAsVerifying(model);
        Assert.True(model.Trust.DeviceRevisionRefused(model.ById[signed.Id]));
        Assert.False(model.Trust.DeviceRevisionRefused(model.ById[held[0].Record.Id]));
        Assert.False(model.Trust.DeviceRevisionRefused(model.ById[held[1].Record.Id]));
        // The two malformed signatures fall back and are verified under both closed keys, their verdicts under the device's own key already
        // known from reading which of its keys they verify with; every signature is verified under the key that does not import, read
        // first, before the closing of the device's own key refuses the third.
        Assert.Equal(2, model.Trust.RecoveryFallbacks);
        Assert.Equal(0, model.Trust.DroppedKeys);
        Assert.Equal(3, model.Trust.ClosedKeyVerifications);
    }

    [Fact]
    public void AWrongRecoveryInPlaceOfTheArithmeticFallsBackAndChangesNoAnswer()
    {
        using var f = new HistoryFixture();
        f.Require();
        f.Adopt();
        // Rotations of the builder's device, each after a closing of the key it replaces, and a rename from the first record of the device,
        // signed with its first key, closed: one revision refused and the rest kept.
        RecordEnvelope current = f.BuilderDevice;
        IRecordSigner signer = f.BuilderSigner;
        for (int k = 0; k < 4; k++)
        {
            f.Close("key", current.Id, f.Estate.BuilderId, signer);
            (current, StoreSigner next) = f.Rotate(current, signer, f.BuilderDevice.Id);
            signer = next;
        }

        RecordEnvelope fork = Rename(f, f.BuilderDevice, f.Estate.BuilderId, f.BuilderSigner, "Renamed with the first key");
        EstateModel right = f.Load();
        AssertAnswersAsVerifying(right);
        Assert.True(right.Trust.DeviceRevisionRefused(right.ById[fork.Id]));
        Assert.Equal(0, right.Trust.RecoveryFallbacks);

        foreach (string wrong in P256KeyRecoveryTests.WrongRecoveries)
        {
            RecoveredKeys.SubstituteForTests = P256KeyRecoveryTests.Wrong(wrong);
            try
            {
                EstateModel model = f.Load();
                AssertAnswersAsVerifying(model);
                Assert.True(model.Trust.DeviceRevisionRefused(model.ById[fork.Id]));
                Assert.True(model.Trust.RecoveryFallbacks > 0, wrong);
                Assert.Equal(model.Trust.KeyRecoveries, model.Trust.RecoveryFallbacks);
                // A key off the curve, or on it but not one the signature verifies under, is dropped, never relied on.
                Assert.Equal(wrong is "one corrupted" or "one replaced" or "unrelated", model.Trust.DroppedKeys > 0);
            }
            finally
            {
                RecoveredKeys.SubstituteForTests = null;
            }
        }
    }

    [Fact]
    public void EachRevisionSignatureIsRecoveredOnceHoweverManyClosingsReadIt()
    {
        const int Rotations = 12;
        using HistoryFixture closedEach = new(), closedFirst = new();
        WriteRotations(closedEach, Rotations, closeEach: true);
        WriteRotations(closedFirst, Rotations, closeEach: false);
        foreach ((HistoryFixture f, int closings) in (ReadOnlySpan<(HistoryFixture, int)>)[(closedEach, Rotations), (closedFirst, 1)])
        {
            EstateModel model = f.Load();
            Assert.Equal(closings, model.Trust.Closings.Count);
            AssertAnswersAsVerifying(model);
            // Every rotation after the first is read against at least one closing it is not the rotation of: two signatures each, recovered
            // once, whether one closing or eleven read them, and no signature verified under a closed key.
            Assert.Equal(2 * (Rotations - 1), model.Trust.KeyRecoveries);
            Assert.Equal(0, model.Trust.ClosedKeyVerifications);
            Assert.Equal(0, model.Trust.RecoveryFallbacks);
            Assert.Equal(0, model.Trust.DroppedKeys);
        }
    }

    /// <summary>Asserts that every device revision is read refused exactly when verifying under each closed key refuses it.</summary>
    private static void AssertAnswersAsVerifying(EstateModel model)
    {
        foreach (RecordEnvelope revision in DeviceRevisions(model))
        {
            bool expected = model.Trust.RevisionRefusedByVerifying(revision, model.ById[revision.Supersedes!]);
            Assert.True(expected == model.Trust.DeviceRevisionRefused(revision), $"Device revision {revision.Id} is read {!expected} refused, verified {expected}.");
        }
    }

    private static IEnumerable<RecordEnvelope> DeviceRevisions(EstateModel model) =>
        model.Records.Where(r => r.Type == "device" && r.Supersedes is not null && model.ById.TryGetValue(r.Supersedes, out RecordEnvelope? previous) && previous.Type == "device");

    /// <summary>
    /// A history of two devices, the operator's first and the builder's, in an adopted estate: closings of a key a revision holds, often
    /// before a rotation from it; rotations and renames from the latest revision or, sometimes, an earlier one, signed with the key it
    /// held; renames written in a copy and put back after the closings that follow; renames dated before the records around them;
    /// checkpoints; and at most one closing of the builder's device by the operator.
    /// </summary>
    private static void Generate(HistoryFixture f, Random random)
    {
        f.Require();
        f.Adopt();
        List<(RecordEnvelope Revision, IRecordSigner Signer)>[] lines = [[(f.OperatorDevice, f.OperatorSigner)], [(f.BuilderDevice, f.BuilderSigner)]];
        string[] principals = [f.Estate.OperatorId, f.Estate.BuilderId];
        var held = new List<(RecordEnvelope Record, byte[] Bytes)>();
        bool deviceClosed = false;
        for (int step = 0; step < 14; step++)
        {
            int d = random.Next(lines.Length);
            List<(RecordEnvelope Revision, IRecordSigner Signer)> line = lines[d];
            string principal = principals[d], identity = line[0].Revision.Id, name = "Step " + step.ToString(System.Globalization.CultureInfo.InvariantCulture);
            (RecordEnvelope at, IRecordSigner signer) = random.Next(4) == 0 ? line[random.Next(line.Count)] : line[^1];
            IRecordSigner current = line[^1].Signer;
            IRecordSigner operatorSigner = lines[0][^1].Signer;
            int action = random.Next(10);
            if (action == 9 && deviceClosed) action = 8;
            switch (action)
            {
                case 0 or 1:
                    f.Close("key", at.Id, principal, current);
                    break;
                case 2 or 3 or 4:
                    if (random.Next(3) > 0) f.Close("key", at.Id, principal, current);
                    (RecordEnvelope rotation, StoreSigner next) = f.Rotate(at, signer, identity);
                    line.Add((rotation, next));
                    break;
                case 5:
                    line.Add((Rename(f, at, principal, signer, name), signer));
                    break;
                case 6:
                    RecordEnvelope copy = Rename(f, at, principal, signer, name + " in a copy");
                    held.Add((copy, f.Remove(copy)));
                    break;
                case 7:
                    DateTimeOffset resume = f.Estate.Clock;
                    f.Estate.SetClock(resume.AddMinutes(-30));
                    Rename(f, at, principal, signer, name + " backdated");
                    f.Estate.SetClock(resume);
                    break;
                case 8:
                    f.Checkpoint(signer: operatorSigner);
                    break;
                default:
                    f.Close("device", lines[1][0].Revision.Id, f.Estate.OperatorId, operatorSigner);
                    deviceClosed = true;
                    break;
            }
        }

        foreach ((RecordEnvelope record, byte[] bytes) in held) f.Restore(record, bytes);
    }

    /// <summary>Rotations of the operator's first device, each after a closing of the key it replaces or, unless <paramref name="closeEach"/>, the first only.</summary>
    private static void WriteRotations(HistoryFixture f, int rotations, bool closeEach)
    {
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

    /// <summary>A rename of a device revision, superseding it, signed as given.</summary>
    private static RecordEnvelope Rename(HistoryFixture f, RecordEnvelope device, string principal, IRecordSigner signer, string name)
    {
        JsonObject body = HistoryFixture.Body(device);
        body["name"] = name;
        return f.Estate.Add("device", principal, body.ToJsonString(), supersedes: device.Id, signer: signer);
    }

    /// <summary>The encoded key of a point, as a device record holds it.</summary>
    private static string Encode(byte[] point)
    {
        using var key = ECDsa.Create(new ECParameters { Curve = ECCurve.NamedCurves.nistP256, Q = new ECPoint { X = point[..32], Y = point[32..] } });
        return RecordSigning.EncodePublicKey(key.ExportSubjectPublicKeyInfo());
    }

    /// <summary>Signs every record with the same signature bytes, whatever its hash, in a device's name.</summary>
    private sealed class FixedSigner(string device, byte[] signature) : IRecordSigner
    {
        public string DeviceId => device;

        public RecordSignature Sign(ReadOnlySpan<byte> digest) => new(RecordSigning.Algorithm, device, Base64Url.EncodeToString(signature));
    }
}
