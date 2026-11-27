// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Data.Sqlite;
using Vericairn.Protocol;
using Vericairn.Records.Estate;
using Vericairn.Records.Hashing;
using Vericairn.Records.History;
using Vericairn.Records.Identity;
using Vericairn.Records.Model;
using Vericairn.Records.Signing;
using Vericairn.Runtime.Events;

namespace Vericairn.Runtime.Service;

internal sealed record DeviceReachReport(string Device, string Since, IReadOnlyList<DeviceSignatureReach> Records,
    IReadOnlyList<PresenceSessionEntry> Sessions, IReadOnlyList<DeviceRevocationReach> Revocations,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? PresenceProblem,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? JournalProblem);

internal sealed record DeviceSignatureReach(string Id, string Hash, string? Type, string? Path, string At, string Source, string Evidence,
    IReadOnlyList<string> Sessions, IReadOnlyList<string> LedgerSignedAt,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] DeviceCheckpointEvidence? FirstWitness = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] DeviceCheckpointEvidence? OutsideClosing = null)
{
    internal const string WitnessedAfterCutoff = "witnessed-after-cutoff";
    internal const string Unwitnessed = "unwitnessed";
    internal const string ClaimedTime = "claimed-time";
}

internal sealed record DeviceCheckpointEvidence(string Id, string Hash, string At)
{
    internal static DeviceCheckpointEvidence? From(RecordEnvelope? record) => record is null ? null : new(record.Id, record.ComputedHash, record.At);
}

public sealed partial class EstateService
{
    private static readonly string RevocationObserverIdentity = Guid.NewGuid().ToString("N");

    internal string? RevocationJournalIdentity => journal?.Epoch;

    private static RevocationObserver ObserveRevocation(string evidence) =>
        new("runtime", RevocationObserverIdentity, evidence, RecordParser.FormatTimestamp(DateTimeOffset.UtcNow));

    /// <summary>Read the local records and diagnostics for one device, without judging a signature's right to authorize a write.</summary>
    internal DeviceReachReport? DeviceReach(string device, DateTimeOffset since, bool includeRevocations = true)
    {
        EstateModel model = session.Load();
        if (!model.ById.TryGetValue(device, out RecordEnvelope? named) || named.Type != "device") return null;
        string identity = model.Trust.IdentityOf(named);
        HashSet<string> revisionIds = DeviceRevisionIds(model, identity);
        IReadOnlyList<RecordEnvelope> keyHistory = model.Trust.DeviceChain(identity);
        string directory = PresenceSessionLedger.DirectoryOf(session.Root, session.Manifest.Estate, session.Profile, session.Home);
        IReadOnlyList<PresenceSessionEntry> sessions = [];
        string? presenceProblem = null;
        try
        {
            sessions = PresenceSessionLedger.Read(directory).Where(s => Ulid.IsValid(s.Session) && s.Device is { } id && revisionIds.Contains(id)).ToArray();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            presenceProblem = exception.Message;
        }

        var signedBySession = sessions.SelectMany(s => s.Signed.Where(r => Ulid.IsValid(r.Record) && ContentHash.IsValid(r.Hash)).Select(r => (Signature: r, s.Session)))
            .ToLookup(r => (r.Signature.Record, r.Signature.Hash));
        var records = new Dictionary<(string, string), DeviceSignatureReach>();
        string[] publicKeys = [.. keyHistory.Select(r => EstateModel.BodyString(r, "publicKey")).OfType<string>().Distinct(StringComparer.Ordinal)];
        HistoryState history = SignedHistory.Of(model);
        Dictionary<string, RecordEnvelope> witnesses = IndependentWitnesses(model, history, revisionIds, publicKeys);
        Closing[] closings = [.. model.Trust.Closings.Where(c => c.Kind == "device" && c.Identity == identity && c.Record.AtTime >= since)];
        foreach (RecordEnvelope record in model.Records.Concat(model.Unreadable))
        {
            if (!record.Signatures.Any(s => revisionIds.Contains(s.Device))) continue;
            var key = (record.Id, record.ComputedHash);
            RecordEnvelope? witness = FirstWitness(history, witnesses, key);
            if (witness?.AtTime < since || !VerifiesDeviceSignature(record, revisionIds, publicKeys)) continue;
            var entries = signedBySession[key];
            // A closing is positive evidence of a boundary, but absence from it could also be a late merge of older work.
            RecordEnvelope? outside = closings.FirstOrDefault(c => !model.Trust.Witnesses(c, record))?.Record;
            string evidence = witness is not null ? DeviceSignatureReach.WitnessedAfterCutoff
                : record.AtTime >= since ? DeviceSignatureReach.ClaimedTime : DeviceSignatureReach.Unwitnessed;
            records.TryAdd(key, new(record.Id, record.ComputedHash, record.Type, record.ExpectedPath, record.At,
                "record-signature", evidence, [.. entries.Select(e => e.Session).Distinct(StringComparer.Ordinal)],
                LedgerTimes(entries.Select(e => e.Signature)), DeviceCheckpointEvidence.From(witness), DeviceCheckpointEvidence.From(outside)));
        }

        foreach (var entries in signedBySession)
        {
            var local = entries.Where(e => AtOrAfter(e.Signature.At, since)).MinBy(e => e.Signature.At, StringComparer.Ordinal);
            if (local.Signature is not { Hash: string hash } signature) continue;
            var key = (signature.Record, hash);
            RecordEnvelope? witness = FirstWitness(history, witnesses, key);
            if (witness?.AtTime < since) continue;
            records.TryAdd(key, new(signature.Record, hash, signature.Type, null, signature.At, "presence-ledger",
                witness is null ? DeviceSignatureReach.Unwitnessed : DeviceSignatureReach.WitnessedAfterCutoff,
                [.. entries.Select(e => e.Session).Distinct(StringComparer.Ordinal)], LedgerTimes(entries.Select(e => e.Signature)), DeviceCheckpointEvidence.From(witness)));
        }

        var referenced = records.Values.SelectMany(r => r.Sessions).ToHashSet(StringComparer.Ordinal);
        PresenceSessionEntry[] announced = [.. sessions.Where(s => referenced.Contains(s.Session) || AtOrAfter(s.AnnouncedAt, since))];

        DeviceRevocationReachReport? revocationReport = includeRevocations ? ReadRevocationReachLocked(model, identity, revisionIds) : null;
        return new(identity, RecordParser.FormatTimestamp(since), [.. records.Values.OrderBy(r => r.At, StringComparer.Ordinal).ThenBy(r => r.Id, StringComparer.Ordinal)
            .ThenBy(r => r.Hash, StringComparer.Ordinal)], announced, revocationReport?.Revocations ?? [], presenceProblem, revocationReport?.JournalProblem);
    }

    /// <inheritdoc />
    public DeviceRevocationReachReport? DeviceRevocationReach(string device)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(device);
        lock (gate)
        {
            EstateModel model = session.Load();
            if (!model.ById.TryGetValue(device, out RecordEnvelope? named) || named.Type != "device") return null;
            string identity = model.Trust.IdentityOf(named);
            return ReadRevocationReachLocked(model, identity, DeviceRevisionIds(model, identity));
        }
    }

    private static HashSet<string> DeviceRevisionIds(EstateModel model, string identity) =>
        model.Records.Where(r => r.Type == "device" && model.Trust.IdentityOf(r) == identity)
            .Select(r => r.Id).ToHashSet(StringComparer.Ordinal);

    private DeviceRevocationReachReport ReadRevocationReachLocked(EstateModel model, string identity, HashSet<string> revisionIds)
    {
        // The cutoff applies to signatures. Preserve the full revocation history, including rows that did not count in this copy.
        RecordEnvelope[] revocations = [.. model.Records.Where(r => r.Type == "admission"
            && r.Subject is { Type: "device" } subject && revisionIds.Contains(subject.Id)
            && EstateModel.BodyString(r, "status") == "revoked")];
        if (revocations.Length == 0) return new(identity, []);

        string? currentAdmission = model.Trust.AdmissionOf(identity)?.Id;
        EventJournal? liveJournal = journal;
        string? journalIdentity = liveJournal?.Epoch;
        IReadOnlyList<JournalEntry> journalEntries = [];
        string? journalProblem = null;
        try
        {
            string[] admissionIds = [.. revocations.Select(r => r.Id)];
            if (liveJournal is not null)
                journalEntries = liveJournal.ReadAdmissionObservations(admissionIds);
            else
            {
                string directory = PresenceSessionLedger.DirectoryOf(session.Root, session.Manifest.Estate, session.Profile, session.Home);
                (journalIdentity, journalEntries) = EventJournal.ReadAdmissionObservations(directory, admissionIds);
            }
        }
        catch (Exception exception) when (exception is SqliteException or IOException or UnauthorizedAccessException or JsonException)
        {
            journalProblem = "The local daemon's event journal could not be read.";
        }

        var journalBySubject = journalEntries.Where(e => AtOrAfter(e.At, DateTimeOffset.MinValue)).ToLookup(e => (e.Subject.Id, e.Subject.Hash));
        string at = RecordParser.FormatTimestamp((liveJournal is null ? TimeProvider.System : eventClock).GetUtcNow());
        var observed = new RevocationObserver("runtime", RevocationObserverIdentity, "record-read", at);
        // A baseline or a pruned journal can have no event even when the serving daemon can read the exact record now.
        RevocationObserver[] readObservers = liveJournal is null ? [observed] : [observed, new("server", liveJournal.Epoch, "server-read", at)];
        DeviceRevocationReach[] reach = [.. revocations.OrderBy(r => r.At, StringComparer.Ordinal).ThenBy(r => r.Id, StringComparer.Ordinal)
            .Select(r => new DeviceRevocationReach(r.Id, r.ComputedHash, r.At, EstateModel.BodyString(r, "revokedAt"), model.Counts(r), r.Id == currentAdmission,
                [.. readObservers, .. journalBySubject[(r.Id, r.ComputedHash)].Select(e => new RevocationObserver("server", journalIdentity!, "journal-" + e.Change, e.At))]))];
        return new(identity, reach, journalProblem);
    }

    private static Dictionary<string, RecordEnvelope> IndependentWitnesses(EstateModel model, HistoryState history, HashSet<string> revisions, string[] publicKeys)
    {
        var witnesses = new Dictionary<string, RecordEnvelope>(StringComparer.Ordinal);
        var pending = new Stack<string>();
        foreach (RecordEnvelope checkpoint in history.CountingCheckpoints.Select(id => model.ById[id])
            .OrderBy(r => r.AtTime).ThenBy(r => r.Id, StringComparer.Ordinal))
        {
            if (witnesses.ContainsKey(checkpoint.Id) || VerifiesDeviceSignature(checkpoint, revisions, publicKeys)) continue;
            pending.Push(checkpoint.Id);
            // This checkpoint pins the exact predecessors it follows. An earlier witness has already covered any ancestor it reached.
            while (pending.TryPop(out string? id))
            {
                if (!witnesses.TryAdd(id, checkpoint)) continue;
                foreach (string predecessor in history.Predecessors[id]) pending.Push(predecessor);
            }
        }

        return witnesses;
    }

    private static RecordEnvelope? FirstWitness(HistoryState history, Dictionary<string, RecordEnvelope> witnesses, (string Id, string Hash) record)
    {
        IReadOnlyList<string> listings = history.ExactListings(record.Id, record.Hash);
        if (listings.Count == 0) return null;
        if (listings.Count == 1) return witnesses.GetValueOrDefault(listings[0]);
        return listings.Select(id => witnesses.GetValueOrDefault(id)).OfType<RecordEnvelope>()
            .OrderBy(r => r.AtTime).ThenBy(r => r.Id, StringComparer.Ordinal).FirstOrDefault();
    }

    private static string[] LedgerTimes(IEnumerable<PresenceSessionSigned> entries) =>
        [.. entries.Select(e => e.At).Where(RecordParser.IsTimestamp).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)];

    private static bool VerifiesDeviceSignature(RecordEnvelope record, HashSet<string> revisions, string[] publicKeys)
    {
        RecordSignature[] signatures = [.. record.Signatures.Where(s => s.Algorithm == RecordSigning.Algorithm && revisions.Contains(s.Device))];
        if (signatures.Length == 0) return false;
        byte[] digest = RecordSigning.Digest(record.ComputedHash);
        // All known revisions' keys are evidence of signing, including one used after rotation. Admission and key standing are
        // deliberately judged elsewhere. A claimed device id without its key's signature proves nothing here.
        return signatures.Any(s => publicKeys.Any(key => RecordSigning.Verify(key, digest, s.Signature)));
    }

    private static bool AtOrAfter(string value, DateTimeOffset since) =>
        DateTimeOffset.TryParseExact(value, "yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out DateTimeOffset at) && at >= since;
}
