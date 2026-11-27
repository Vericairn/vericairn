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

internal sealed record DeviceSignatureReach(string Id, string Hash, string? Type, string? Path, string At, string Source,
    IReadOnlyList<string> Sessions, IReadOnlyList<string> LedgerSignedAt,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] DeviceCheckpointEvidence? FirstWitness = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] DeviceCheckpointEvidence? OutsideClosing = null);

internal sealed record DeviceCheckpointEvidence(string Id, string Hash, string At)
{
    internal static DeviceCheckpointEvidence? From(RecordEnvelope? record) => record is null ? null : new(record.Id, record.ComputedHash, record.At);
}

internal sealed record DeviceRevocationReach(string Id, string Hash, string At, string? RevokedAt, bool Counts, bool Current, IReadOnlyList<RevocationObserver> Observations);

public sealed partial class EstateService
{
    private static readonly string RevocationObserverIdentity = Guid.NewGuid().ToString("N");

    internal string? RevocationJournalIdentity => journal?.Epoch;

    private static RevocationObserver ObserveRevocation(string evidence) =>
        new("runtime", RevocationObserverIdentity, evidence, RecordParser.FormatTimestamp(DateTimeOffset.UtcNow));

    /// <summary>Read the local records and diagnostics for one device, without judging a signature's right to authorize a write.</summary>
    internal DeviceReachReport? DeviceReach(string device, DateTimeOffset since)
    {
        EstateModel model = session.Load();
        if (!model.ById.TryGetValue(device, out RecordEnvelope? named) || named.Type != "device") return null;
        string identity = model.Trust.IdentityOf(named);
        HashSet<string> revisionIds = model.Records.Where(r => r.Type == "device" && model.Trust.IdentityOf(r) == identity)
            .Select(r => r.Id).ToHashSet(StringComparer.Ordinal);
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
        Closing[] closings = [.. model.Trust.Closings.Where(c => c.Kind == "device" && c.Identity == identity && c.Record.AtTime >= since)];
        foreach (RecordEnvelope record in model.Records.Concat(model.Unreadable))
        {
            if (!record.Signatures.Any(s => revisionIds.Contains(s.Device))) continue;
            var key = (record.Id, record.ComputedHash);
            var entries = signedBySession[key];
            RecordEnvelope? witness = FirstWitness(model, history, record);
            // A closing is positive evidence of a boundary, but absence from it could also be a late merge of older work.
            RecordEnvelope? outside = closings.FirstOrDefault(c => !model.Trust.Witnesses(c, record))?.Record;
            if (record.AtTime < since && !(witness?.AtTime >= since) && outside is null && !entries.Any(e => AtOrAfter(e.Signature.At, since))) continue;
            if (!VerifiesDeviceSignature(record, revisionIds, publicKeys)) continue;
            records.TryAdd(key, new(record.Id, record.ComputedHash, record.Type, record.ExpectedPath, record.At,
                "record-signature", [.. entries.Select(e => e.Session).Distinct(StringComparer.Ordinal)],
                LedgerTimes(entries.Select(e => e.Signature)), DeviceCheckpointEvidence.From(witness), DeviceCheckpointEvidence.From(outside)));
        }

        foreach (var entries in signedBySession)
        {
            var local = entries.Where(e => AtOrAfter(e.Signature.At, since)).MinBy(e => e.Signature.At, StringComparer.Ordinal);
            if (local.Signature is not { Hash: string hash } signature) continue;
            records.TryAdd((signature.Record, hash), new(signature.Record, hash, signature.Type, null, signature.At,
                "presence-ledger", [.. entries.Select(e => e.Session).Distinct(StringComparer.Ordinal)], LedgerTimes(entries.Select(e => e.Signature))));
        }

        var referenced = records.Values.SelectMany(r => r.Sessions).ToHashSet(StringComparer.Ordinal);
        PresenceSessionEntry[] announced = [.. sessions.Where(s => referenced.Contains(s.Session) || AtOrAfter(s.AnnouncedAt, since))];

        // The date filter applies to signatures. Include the full revocation history for this device, and preserve exactly which
        // revisions count in this copy even when a later admission supersedes them.
        RecordEnvelope[] revocations = [.. model.Records.Where(r => r.Type == "admission"
            && r.Subject is { Type: "device" } subject && revisionIds.Contains(subject.Id)
            && EstateModel.BodyString(r, "status") == "revoked")];
        string? currentAdmission = model.Trust.AdmissionOf(identity)?.Id;
        IReadOnlyList<JournalEntry> journalEntries = [];
        string? journalIdentity = null;
        string? journalProblem = null;
        if (revocations.Length > 0)
        {
            try
            {
                (journalIdentity, journalEntries) = EventJournal.ReadAdmissionObservations(directory, [.. revocations.Select(r => r.Id)]);
            }
            catch (Exception exception) when (exception is SqliteException or IOException or UnauthorizedAccessException or JsonException)
            {
                journalProblem = "The local daemon's event journal could not be read.";
            }
        }

        var journalBySubject = journalEntries.Where(e => AtOrAfter(e.At, DateTimeOffset.MinValue)).ToLookup(e => (e.Subject.Id, e.Subject.Hash));
        RevocationObserver observed = ObserveRevocation("record-read");
        DeviceRevocationReach[] reach = [.. revocations.OrderBy(r => r.At, StringComparer.Ordinal).ThenBy(r => r.Id, StringComparer.Ordinal)
            .Select(r => new DeviceRevocationReach(r.Id, r.ComputedHash, r.At, EstateModel.BodyString(r, "revokedAt"), model.Counts(r), r.Id == currentAdmission,
                [observed, .. journalBySubject[(r.Id, r.ComputedHash)].Select(e => new RevocationObserver("server", journalIdentity!, "journal-" + e.Change, e.At))]))];
        return new(identity, RecordParser.FormatTimestamp(since), [.. records.Values.OrderBy(r => r.At, StringComparer.Ordinal).ThenBy(r => r.Id, StringComparer.Ordinal)
            .ThenBy(r => r.Hash, StringComparer.Ordinal)], announced, reach, presenceProblem, journalProblem);
    }

    private static RecordEnvelope? FirstWitness(EstateModel model, HistoryState history, RecordEnvelope record)
    {
        if (!history.Entries.TryGetValue(record.Id, out WitnessedEntry? entry) || entry.Entry.Hash != record.ComputedHash) return null;
        if (history.Listings.GetValueOrDefault(record.Id) is not { Count: > 1 } listings) return model.ById[entry.Checkpoint];
        return listings.Select(id => model.ById[id]).OrderBy(checkpoint => checkpoint.AtTime).ThenBy(checkpoint => checkpoint.Id, StringComparer.Ordinal).First();
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
