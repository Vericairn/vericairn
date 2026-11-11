// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Vericairn.Records.Authoring;
using Vericairn.Records.Canonical;
using Vericairn.Records.Estate;
using Vericairn.Records.Hashing;
using Vericairn.Records.History;
using Vericairn.Records.Identity;
using Vericairn.Records.Model;
using Vericairn.Records.Signing;

namespace Vericairn.Records.Tests;

/// <summary>
/// The cost of history checkpoints at 100,000 records (design/signed-history.md § 11; OPS-07), measured on a generated estate: the
/// inventory's size, the adoption's objects, the checkpoints written one per 500 records, and what verifying them adds to judging the
/// estate. It generates and judges 100,000 records several times, so it runs only when <c>VERICAIRN_COST_TESTS</c> is 1, as continuous
/// integration's cost step sets it on each platform; the measured numbers are written to the test output.
/// </summary>
[Collection("history-cost")]
public sealed class HistoryCostTests(ITestOutputHelper output) : IDisposable
{
    private const int Records = 100_000;
    private const int PerCheckpoint = SignedHistory.IntervalRecords;
    private const string EnvironmentVariable = "VERICAIRN_COST_TESTS";

    private readonly string keyHome = Path.Combine(Path.GetTempPath(), "vericairn-history-cost-keys-" + Ulid.NewUlid().Value);

    [Fact]
    public void CheckpointsAtOneHundredThousandRecordsStayWithinSectionElevensEstimates()
    {
        Assert.SkipUnless(Environment.GetEnvironmentVariable(EnvironmentVariable) == "1", $"The 100,000-record cost test runs when {EnvironmentVariable} is 1.");
        var store = new FileKeyStore(keyHome);
        var clock = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var files = new List<RecordFile>();
        var objects = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        AuthoredRecord Author(string type, string by, JsonNode body, RecordReference? subject = null, string? id = null, IRecordSigner? signer = null)
        {
            clock = clock.AddSeconds(1);
            using JsonDocument document = JsonDocument.Parse(body.ToJsonString());
            AuthoredRecord record = RecordAuthor.Create(new RecordDraft(type, by, document.RootElement, subject, null, clock, id ?? Ulid.NewUlid(clock).Value, 3), signer is null ? [] : [signer]);
            files.Add(new RecordFile(record.RelativePath, record.Bytes));
            return record;
        }

        // The root of trust: the operator, the charter and the operator's first device admitting itself, which signs every checkpoint.
        string op = Ulid.NewUlid(clock).Value;
        Author("principal", op, new JsonObject { ["kind"] = "human", ["name"] = "Operator", ["roles"] = new JsonArray("operator") }, id: op);
        AuthoredRecord charter = Author("estate", op, new JsonObject
        {
            ["name"] = "Cost", ["slug"] = "cost", ["purpose"] = "Measure history checkpoints.", ["operator"] = op, ["recordFormat"] = 3,
            ["policies"] = new JsonObject { ["signatures"] = "optional", ["releaseAcceptance"] = new JsonArray("operator") },
        });
        string device = Ulid.NewUlid(clock.AddSeconds(1)).Value;
        var signer = new StoreSigner(store, device);
        AuthoredRecord deviceRecord = Author("device", op, new JsonObject
        {
            ["principal"] = op, ["name"] = "Operator's device", ["platform"] = "linux", ["custody"] = "file-owner-only", ["algorithm"] = "ecdsa-p256-sha256",
            ["publicKey"] = RecordSigning.EncodePublicKey(store.Create(device)), ["profiles"] = new JsonArray("embedded"), ["presence"] = "none", ["purpose"] = "signing",
        }, id: device, signer: signer);
        Author("admission", op, new JsonObject { ["principal"] = op, ["status"] = "admitted", ["note"] = "The first authority." }, deviceRecord.Envelope.Reference, signer: signer);
        byte[] manifest = Manifest(charter.Envelope.Id);
        string root = Path.Combine(Path.GetTempPath(), "vericairn-history-cost-" + Ulid.NewUlid().Value);

        // The records: ideas recorded as possibilities, unsigned under the optional charter so that generating them measures nothing a
        // checkpoint adds (a signed record's signature is verified with or without checkpoints), of a type whose validation reads no
        // other record, so that judging 100,000 of them measures the history rather than another rule.
        var watch = Stopwatch.StartNew();
        while (files.Count < Records)
        {
            string number = files.Count.ToString(CultureInfo.InvariantCulture);
            Author("possibility", op, new JsonObject
            {
                ["title"] = "A generated possibility " + number, ["idea"] = "Stand for one of an estate's many records, number " + number + ", so that its history can be witnessed.",
                ["origin"] = "The history cost test.", ["disposition"] = "open",
            });
        }

        long recordBytes = files.Sum(f => (long)f.Bytes.Length);
        TimeSpan generated = watch.Elapsed;
        EstateModel baseline = Load(root, files, manifest, objects, out TimeSpan baselineLoad);
        Assert.False(baseline.Findings.HasErrors, string.Join('\n', baseline.Findings.Take(5)));
        TimeSpan baselineTrust = Time(() => EstateTrust.Build(baseline, baseline.Manifest, Reader(objects), null));

        // Adoption of an estate already holding 100,000 records: one record, its inventory in objects under 8 MiB, one signature.
        watch.Restart();
        CheckpointDraft adoption = SignedHistory.Prepare(baseline, "adoption", objects: Reader(objects));
        TimeSpan prepared = watch.Elapsed;
        foreach (byte[] bytes in adoption.Objects) objects[ContentHash.Sha256(bytes)] = bytes;
        AuthoredRecord adoptionRecord = Author("history-checkpoint", op, JsonNode.Parse(adoption.Body.GetRawText())!, signer: signer);
        EstateModel adopted = Load(root, files, manifest, objects, out TimeSpan adoptedLoad);
        Assert.False(adopted.Findings.HasErrors, string.Join('\n', adopted.Findings.Take(5)));
        Assert.True(SignedHistory.Adopted(adopted));
        Assert.Equal(Records, SignedHistory.Of(adopted).Witnessed);
        TimeSpan adoptedTrust = Time(() => EstateTrust.Build(adopted, adopted.Manifest, Reader(objects), null));
        long inventoryBytes = adoption.Objects.Sum(o => (long)o.Length);

        // An estate that grew to 100,000 records with a checkpoint every 500, each following the one before.
        files.Remove(files.Single(f => f.Path == adoptionRecord.RelativePath));
        foreach (byte[] bytes in adoption.Objects) objects.Remove(ContentHash.Sha256(bytes));
        List<RecordEnvelope> ordered = [.. baseline.Records.OrderBy(r => r.Id, StringComparer.Ordinal)];
        var witnessed = new List<(string Id, string Hash)>();
        RecordEnvelope? previous = null;
        var checkpointBytes = new List<int>();
        watch.Restart();
        for (int start = 0; start < ordered.Count; start += PerCheckpoint)
        {
            List<RecordEnvelope> listed = [.. ordered.Skip(start).Take(PerCheckpoint)];
            if (previous is not null) listed.Insert(0, previous);
            IReadOnlyList<byte[]> inventory = HistoryInventory.Write(listed);
            foreach (byte[] bytes in inventory) objects[ContentHash.Sha256(bytes)] = bytes;
            witnessed.AddRange(listed.Select(r => (r.Id, r.ComputedHash)));
            var body = new JsonObject
            {
                ["previous"] = previous is null ? new JsonArray() : new JsonArray(new JsonObject { ["id"] = previous.Id, ["hash"] = previous.ComputedHash }),
                ["inventory"] = new JsonArray([.. inventory.Select(o => (JsonNode)JsonValue.Create(ContentHash.Sha256(o))!)]),
                ["records"] = witnessed.Count, ["recordSetHash"] = RecordSets.Hash(witnessed), ["trigger"] = previous is null ? "adoption" : "interval",
            };
            AuthoredRecord checkpoint = Author("history-checkpoint", op, body, signer: signer);
            checkpointBytes.Add(checkpoint.Bytes.Length);
            previous = checkpoint.Envelope;
        }

        TimeSpan chained = watch.Elapsed;
        EstateModel grown = Load(root, files, manifest, objects, out TimeSpan grownLoad);
        Assert.False(grown.Findings.HasErrors, string.Join('\n', grown.Findings.Take(5)));
        HistoryState state = SignedHistory.Of(grown);
        Assert.Equal(Records + checkpointBytes.Count - 1, state.Witnessed);
        Assert.Equal(1, state.Tail);
        TimeSpan grownTrust = Time(() => EstateTrust.Build(grown, grown.Manifest, Reader(objects), null));

        double perRecord = (double)inventoryBytes / Records;
        output.WriteLine($"history cost at {Records:N0} records on {System.Runtime.InteropServices.RuntimeInformation.OSDescription} ({System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture}), {Environment.ProcessorCount} processors");
        output.WriteLine($"  records: {recordBytes:N0} bytes ({(double)recordBytes / Records:N0} per record), generated in {generated.TotalSeconds:N1} s");
        output.WriteLine($"  inventory: {inventoryBytes:N0} bytes, {perRecord:N1} per record");
        output.WriteLine($"  adoption: 1 record of {adoptionRecord.Bytes.Length:N0} bytes, {adoption.Objects.Count} objects ({string.Join(", ", adoption.Objects.Select(o => o.Length.ToString("N0", CultureInfo.InvariantCulture)))} bytes), 1 signature; prepared in {prepared.TotalSeconds:N2} s");
        output.WriteLine($"  interval checkpoints: {checkpointBytes.Count} of {checkpointBytes.Min():N0} to {checkpointBytes.Max():N0} bytes ({checkpointBytes.Average():N0} mean), written in {chained.TotalSeconds:N1} s");
        output.WriteLine($"  judging the estate (load and validate): {baselineLoad.TotalSeconds:N2} s without checkpoints, {adoptedLoad.TotalSeconds:N2} s adopted, {grownLoad.TotalSeconds:N2} s with {checkpointBytes.Count} checkpoints");
        output.WriteLine($"  signatures, anchors and history alone: {baselineTrust.TotalSeconds:N2} s without checkpoints, {adoptedTrust.TotalSeconds:N2} s adopted, {grownTrust.TotalSeconds:N2} s with {checkpointBytes.Count} checkpoints");

        // § 11's estimates: about 125 bytes per record listed once, an adoption in at most two objects under 8 MiB and one signature, about
        // 200 checkpoints of about 1 KB, and verification a map against hashes the loader already computes.
        Assert.InRange(perRecord, 100, 150);
        Assert.InRange(adoption.Objects.Count, 1, 2);
        Assert.All(adoption.Objects, o => Assert.True(o.LongLength <= EstateLayout.MaxObjectBytes));
        Assert.Single(adoptionRecord.Envelope.Signatures);
        Assert.Equal(Records / PerCheckpoint, checkpointBytes.Count);
        Assert.True(checkpointBytes.Average() < 2048, $"Checkpoints average {checkpointBytes.Average():N0} bytes.");
    }

    /// <summary>
    /// The cost of how the strongest acts read principal lines (FIX-108): a chain of 800 appointments, each appointee holding a device and
    /// appointing the next, and a flat set of 1,600 appointments by one operator, each loaded while that operator stands and after the root
    /// operator removes its role. Each load stays within twice the load of the same estate whose appointments the root operator wrote,
    /// where those reads settle in one pass.
    /// </summary>
    [Theory]
    [InlineData(800, true)]
    [InlineData(1600, false)]
    public void StrictAuthorityReadsStayWithinTwiceAnEstateOfTheRootOperatorsAppointments(int count, bool chain)
    {
        Assert.SkipUnless(Environment.GetEnvironmentVariable(EnvironmentVariable) == "1", $"The appointment cost test runs when {EnvironmentVariable} is 1.");
        (TimeSpan rootStanding, TimeSpan rootRemoved) = Appointments(count, chain, byRoot: true);
        (TimeSpan standing, TimeSpan removed) = Appointments(count, chain, byRoot: false);
        output.WriteLine($"strict authority reads, {count:N0} appointments {(chain ? "in a chain" : "by one operator")}: {standing.TotalMilliseconds:N0} ms standing and {removed.TotalMilliseconds:N0} ms after its base is removed; {rootStanding.TotalMilliseconds:N0} ms and {rootRemoved.TotalMilliseconds:N0} ms when the root operator wrote them");
        Assert.True(standing <= 2 * rootStanding + TimeSpan.FromMilliseconds(50), $"Standing: {standing.TotalMilliseconds:N0} ms against {rootStanding.TotalMilliseconds:N0} ms.");
        Assert.True(removed <= 2 * rootRemoved + TimeSpan.FromMilliseconds(50), $"Removed: {removed.TotalMilliseconds:N0} ms against {rootRemoved.TotalMilliseconds:N0} ms.");
    }

    /// <summary>
    /// Loads an adopted estate in which a second operator, or the root operator when <paramref name="byRoot"/>, writes the appointments,
    /// once while the second operator stands and once after the root operator removes its role; returns the faster of two loads each time.
    /// </summary>
    private static (TimeSpan Standing, TimeSpan Removed) Appointments(int count, bool chain, bool byRoot)
    {
        static string Person(string name, string role) => $$"""{"kind":"human","name":"{{name}}","roles":["{{role}}"]}""";
        using var f = new HistoryFixture();
        RecordEnvelope second = f.Estate.Add("principal", f.Estate.OperatorId, Person("Second operator", "operator"), signer: f.OperatorSigner);
        (RecordEnvelope secondDevice, _) = f.Admitted(second.Id, "The second operator's device");
        f.Require();
        f.Adopt();
        RecordEnvelope author = second;
        RecordEnvelope authorDevice = secondDevice;
        for (int i = 0; i < count; i++)
        {
            string name = "Appointee " + i.ToString(CultureInfo.InvariantCulture);
            RecordEnvelope next = byRoot
                ? f.Estate.Add("principal", f.Estate.OperatorId, Person(name, "operator"), signer: f.OperatorSigner)
                : f.Estate.Add("principal", author.Id, Person(name, "operator"), signer: f.Signer(authorDevice));
            if (!chain) continue;
            (authorDevice, _) = f.Admitted(next.Id, name + "'s device");
            author = next;
        }

        RecordEnvelope bySecond = f.Close("device", f.BuilderDevice.Id, second.Id, f.Signer(secondDevice));
        RecordEnvelope byLast = f.Close("device", f.BuilderDevice.Id, author.Id, f.Signer(authorDevice));
        TimeSpan standing = Fastest(f, model =>
        {
            Assert.Contains(model.Trust.Closings, c => c.Record.Id == bySecond.Id);
            Assert.Contains(model.Trust.Closings, c => c.Record.Id == byLast.Id);
        });
        f.Estate.Add("principal", f.Estate.OperatorId, Person("Second operator", "reviewer"), supersedes: second.Id, signer: f.OperatorSigner);
        TimeSpan removed = Fastest(f, model =>
        {
            Assert.DoesNotContain(model.Trust.Closings, c => c.Record.Id == bySecond.Id);
            Assert.Equal(byRoot && chain, model.Trust.Closings.Any(c => c.Record.Id == byLast.Id));
        });
        return (standing, removed);

        static TimeSpan Fastest(HistoryFixture f, Action<EstateModel> check)
        {
            TimeSpan best = TimeSpan.MaxValue;
            for (int run = 0; run < 2; run++)
            {
                var watch = Stopwatch.StartNew();
                EstateModel model = f.Load();
                watch.Stop();
                check(model);
                if (watch.Elapsed < best) best = watch.Elapsed;
            }

            return best;
        }
    }

    public void Dispose()
    {
        if (Directory.Exists(keyHome)) Directory.Delete(keyHome, recursive: true);
    }

    private static Func<string, byte[]?> Reader(Dictionary<string, byte[]> objects) => hash => objects.GetValueOrDefault(hash);

    private static EstateModel Load(string root, List<RecordFile> files, byte[] manifest, Dictionary<string, byte[]> objects, out TimeSpan elapsed)
    {
        var watch = Stopwatch.StartNew();
        EstateModel model = EstateLoader.Load(root, [.. files.OrderBy(f => f.Path, StringComparer.Ordinal)], null, manifest, Reader(objects));
        elapsed = watch.Elapsed;
        return model;
    }

    private static TimeSpan Time(Action action)
    {
        var watch = Stopwatch.StartNew();
        action();
        return watch.Elapsed;
    }

    private static byte[] Manifest(string estate)
    {
        var manifest = new JsonObject
        {
            ["estate"] = estate, ["format"] = EstateManifest.Format1, ["recordFormat"] = 3, ["profile"] = "repository-synced",
            ["vericairn"] = new JsonObject { ["commit"] = new string('0', 40), ["mode"] = "source", ["repository"] = "Vericairn/vericairn" },
        };
        using JsonDocument document = JsonDocument.Parse(manifest.ToJsonString());
        return CanonicalJson.SerializePrettyUtf8(document.RootElement);
    }
}
