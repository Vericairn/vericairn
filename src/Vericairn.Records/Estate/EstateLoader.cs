// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using Vericairn.Records.Model;
using Vericairn.Records.Types;

namespace Vericairn.Records.Estate;

/// <summary>One record file as the loader sees it: its repository-relative path and bytes, wherever they are held.</summary>
/// <param name="Path">Repository-relative path with forward slashes.</param>
/// <param name="Bytes">File bytes.</param>
public sealed record RecordFile(string Path, byte[] Bytes);

/// <summary>Loads every record of an estate and validates them individually and together.</summary>
public static class EstateLoader
{
    /// <summary>Loads the estate at <paramref name="root"/> from its records directory.</summary>
    public static EstateModel Load(string root)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        root = Path.GetFullPath(root);
        return Load(root, EnumerateRecordFiles(root));
    }

    /// <summary>Record files under records/, in path order, with residue reported as findings by the loader.</summary>
    public static IReadOnlyList<RecordFile> EnumerateRecordFiles(string root)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        string recordsRoot = Path.Combine(root, EstateLayout.RecordsDirectory);
        if (!Directory.Exists(recordsRoot))
        {
            return [];
        }

        return Directory.EnumerateFiles(recordsRoot, "*", SearchOption.AllDirectories)
            .Select(file => new RecordFile(EstateLayout.Relative(root, file), File.ReadAllBytes(file)))
            .OrderBy(f => f.Path, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>Loads an estate whose records are supplied by the caller (from files, a live store or an export).</summary>
    public static EstateModel Load(string root, IReadOnlyList<RecordFile> files) => Load(root, files, (RecordParseCache?)null);

    /// <summary>
    /// Loads an estate whose records and objects are supplied by the caller: <paramref name="objects"/> reads an object by its content
    /// hash, or returns null when it is not held, as a store that keeps objects outside the repository does.
    /// </summary>
    public static EstateModel Load(string root, IReadOnlyList<RecordFile> files, Func<string, byte[]?> objects) => Load(root, files, null, null, objects);

    /// <summary>
    /// The objects of a repository's objects/ directory, read by content hash and verified against it; null when absent, unreadable or
    /// not those bytes.
    /// </summary>
    public static Func<string, byte[]?> FileObjects(string root)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        string directory = Path.Combine(Path.GetFullPath(root), EstateLayout.ObjectsDirectory);
        return hash =>
        {
            if (!Hashing.ContentHash.IsValid(hash)) return null;
            string path = Path.Combine(directory, hash[Hashing.ContentHash.Prefix.Length..]);
            try
            {
                var info = new FileInfo(path);
                if (!info.Exists || info.LinkTarget is not null || info.Length > EstateLayout.MaxObjectBytes) return null;
                byte[] bytes = File.ReadAllBytes(path);
                return Hashing.ContentHash.Sha256(bytes) == hash ? bytes : null;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                return null;
            }
        };
    }

    /// <summary>Validates prepared records and manifest bytes before initialization writes any files.</summary>
    public static EstateModel LoadPrepared(string root, IReadOnlyList<RecordFile> files, byte[] manifestBytes)
    {
        ArgumentNullException.ThrowIfNull(manifestBytes);
        return Load(root, files, null, manifestBytes);
    }

    /// <summary>
    /// The records of a file set as identities only: each parsed record with its id, hash and body, supersession and the manifest,
    /// with parse findings alone. No record type, reference, authority, signature or admission is judged, no clock is read, and no
    /// file is read but the record files given and the manifest. A caller that needs any judgment loads the estate with <see cref="Load(string, IReadOnlyList{RecordFile})"/>.
    /// </summary>
    internal static EstateModel LoadIdentities(string root, IReadOnlyList<RecordFile> files, RecordParseCache? cache) => Load(root, files, cache, judge: false);

    /// <summary>
    /// The records of a file set judged with every record's roles and grants read at the record's own time, as an estate that has adopted
    /// history checkpoints reads the records its adoption does not witness: what adoption's check compares with the reading of today
    /// (design/signed-history.md § 10). Such a model serves that comparison alone.
    /// </summary>
    internal static EstateModel LoadAtRecordTime(string root, IReadOnlyList<RecordFile> files, Func<string, byte[]?>? objects) =>
        Load(root, files, null, null, objects, authorityAtRecordTime: true);

    // Only parsing is reusable. References, authority, signatures, manifest and output-file findings are rejudged on every load.
    internal static EstateModel Load(string root, IReadOnlyList<RecordFile> files, RecordParseCache? cache, byte[]? manifestBytes = null, Func<string, byte[]?>? objects = null, bool judge = true,
        bool authorityAtRecordTime = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        ArgumentNullException.ThrowIfNull(files);
        root = Path.GetFullPath(root);
        var findings = new FindingList();
        var records = new List<RecordEnvelope>();
        var byId = new Dictionary<string, RecordEnvelope>(StringComparer.Ordinal);
        if (files.Count == 0)
        {
            findings.Info("records.empty", EstateLayout.RecordsDirectory, "No records yet.");
        }

        foreach (RecordFile file in files.OrderBy(f => f.Path, StringComparer.Ordinal))
        {
            string location = file.Path;
            string name = location[(location.LastIndexOf('/') + 1)..];
            if (name == ".DS_Store")
            {
                findings.Warning("records.residue", location, "Operating-system residue inside records/; delete it.");
                continue;
            }

            if (!location.EndsWith(".json", StringComparison.Ordinal))
            {
                findings.Error("records.unexpected-file", location, "Only record files (*.json) belong under records/.");
                continue;
            }

            RecordEnvelope? record = cache is null ? RecordParser.Parse(file.Bytes, location, findings) : cache.Parse(file, findings);
            if (record is null)
            {
                continue;
            }

            if (!string.Equals(record.ExpectedPath, location, StringComparison.Ordinal))
            {
                findings.Error("record.path", location, $"This record must live at {record.ExpectedPath} (records/<type>/<yyyy-MM of at>/<id>.json).");
            }

            if (!byId.TryAdd(record.Id, record))
            {
                findings.Error("record.duplicate-id", location, $"Another record already has id {record.Id}.");
                continue;
            }

            records.Add(record);
        }

        var supersession = new FindingList();
        Dictionary<string, string> supersededBy = Successors(records, byId, supersession);
        var model = new EstateModel(root, records, byId, supersededBy, findings)
        {
            ObjectReader = objects ?? FileObjects(root),
            FilePaths = files.Select(f => f.Path).ToHashSet(StringComparer.Ordinal),
            AuthorityAtRecordTime = authorityAtRecordTime,
        };
        if (!judge)
        {
            // Identities only: no trust, counting, type, signature or manifest judgment; just the manifest as it reads.
            model.Manifest = manifestBytes is null ? EstateManifest.Load(root, new FindingList()) : EstateManifest.Parse(manifestBytes, new FindingList());
            return model;
        }


        // The manifest is read before anything is judged, because which unsigned admissions count depends on the estate's record format;
        // its findings keep their place after the signatures'. Signatures, anchors and history checkpoints are judged from every record
        // read, and then the model's lookups keep only the records that count, before any rule reads them (design/signed-history.md § 6).
        var manifestFindings = new FindingList();
        EstateManifest? manifest = manifestBytes is null ? EstateManifest.Load(root, manifestFindings) : EstateManifest.Parse(manifestBytes, manifestFindings);
        EstateTrust trust = EstateTrust.Build(model, manifest, model.ObjectReader, model.FilePaths);
        IReadOnlySet<string> excluded = trust.NotCounting();
        if (excluded.Count == 0)
        {
            foreach (Finding finding in supersession) findings.Add(finding);
            model.ApplyCounting(trust, excluded, supersededBy);
        }
        else
        {
            // Divergence is judged among the records that count: a successor that does not count revises nothing, so it neither
            // unseats a record nor makes a reconcile task. A grant or principal revision on top of one that does not count follows the
            // nearest record below it that counts, so its line reads one current record.
            var countingSupersession = new FindingList();
            Dictionary<string, string> countingSupersededBy = Successors(records.Where(r => !excluded.Contains(r.Id)).ToList(), byId, countingSupersession,
                r => trust.AttachedTo(r)?.Id);
            foreach (Finding finding in supersession.Where(f => f.Code != "reconcile.successors").Concat(countingSupersession.Where(f => f.Code == "reconcile.successors"))) findings.Add(finding);
            model.ApplyCounting(trust, excluded, countingSupersededBy);
        }
        foreach (RecordEnvelope record in records)
        {
            IRecordType? type = RecordTypeRegistry.Find(record.Type);
            if (type is null)
            {
                string hint = RecordTypeRegistry.Reserved.IndexOf(record.Type) >= 0 ? " It is reserved for a later release." : string.Empty;
                findings.Error("record.unknown-type", record.ExpectedPath, $"Unknown record type '{record.Type}'.{hint}");
                continue;
            }

            if (record.Format < type.SinceFormat)
            {
                findings.Error("record.type-format", record.ExpectedPath, $"Type '{record.Type}' belongs to record format {type.SinceFormat}, and this record states format {record.Format}.");
                continue;
            }

            ValidateSubject(model, record, type, findings);
            type.Validate(new ValidationContext(model, record, findings));
            // Existing records keep their reading. The write boundary promotes this finding only for proposed records.
            if (RecordAuthorship.Unresolved(model, record) is Finding author) findings.Add(author);
        }

        model.Manifest = manifest;
        SignatureChecks.Run(model, findings);
        foreach (Finding finding in trust.History.Findings.Concat(trust.ClosingFindings))
        {
            findings.Add(finding);
        }

        foreach (Finding finding in manifestFindings)
        {
            findings.Add(finding);
        }

        // A record is never ahead of its estate: an estate at format 1 holds only format-1 records, and moves to a later format by
        // the recorded upgrade. Records written before an upgrade keep the format, and the rules, they were written in.
        if (model.Manifest is not null)
        {
            Updates.VericairnReleaseObservation.ValidatePin(model);
            // An upgrade stopped between its migration record and the manifest is reported, not refused: rerunning it finishes the move.
            RecordEnvelope? pending = UpgradeProgress.Pending(model.Manifest, records);
            if (pending is not null)
            {
                findings.Warning("upgrade.incomplete", pending.ExpectedPath, $"Migration {pending.Id} moves the estate from {model.Manifest.Describe()} to {EstateModel.BodyString(pending, "toPin")}, and the manifest has not moved yet; `vericairn estate upgrade` to that release finishes it.");
            }

            foreach (RecordEnvelope record in records.Where(r => r.Format > model.Manifest.RecordFormat))
            {
                if (ReferenceEquals(record, pending) && UpgradeProgress.MovesToItsOwnFormat(model.Manifest, record)) continue;
                findings.Error("record.format-ahead", record.ExpectedPath, $"This record states record format {record.Format}, and the estate is at format {model.Manifest.RecordFormat}; `vericairn estate upgrade` to a release that writes format {record.Format} moves the estate first.");
            }
        }
        if (model.Manifest is not null && model.CurrentEstate is not null && model.Manifest.Estate != model.Chain(model.CurrentEstate)[0])
        {
            findings.Error("manifest.estate", EstateLayout.ManifestPath, $"The manifest names estate {model.Manifest.Estate} but this estate's charter began as {model.Chain(model.CurrentEstate)[0]}.");
        }

        if (records.Count > 0 && model.CurrentEstate is null)
        {
            findings.Error("estate.missing", EstateLayout.RecordsDirectory, "No current estate record exists; every estate starts with its charter.");
        }

        return model;
    }

    /// <summary>
    /// Which record each superseded record is superseded by, from the records given: divergent successors are a reconcile task unless a
    /// reconciliation settles them, and an authored document's revisions form one line ordered by time and id. A record follows the one it
    /// supersedes, or the one <paramref name="attachedTo"/> names for it.
    /// </summary>
    private static Dictionary<string, string> Successors(IReadOnlyList<RecordEnvelope> records, Dictionary<string, RecordEnvelope> byId, FindingList findings,
        Func<RecordEnvelope, string?>? attachedTo = null)
    {
        var supersededBy = new Dictionary<string, string>(StringComparer.Ordinal);
        var successors = new Dictionary<string, List<RecordEnvelope>>(StringComparer.Ordinal);
        foreach (RecordEnvelope record in records.Where(r => r.Supersedes is not null))
        {
            string previousId = attachedTo?.Invoke(record) ?? record.Supersedes!;
            if (!byId.TryGetValue(previousId, out RecordEnvelope? previous))
            {
                findings.Error("record.supersedes", record.ExpectedPath, $"Supersedes {previousId}, which does not exist.");
                continue;
            }

            if (previous.Type != record.Type)
            {
                findings.Error("record.supersedes", record.ExpectedPath, $"A {record.Type} cannot supersede a {previous.Type}.");
                continue;
            }

            if (record.AtTime < previous.AtTime)
            {
                findings.Error("record.supersedes", record.ExpectedPath, "A successor cannot be older than the record it supersedes.");
            }

            if (!successors.TryGetValue(previousId, out List<RecordEnvelope>? list))
            {
                list = [];
                successors[previousId] = list;
            }

            list.Add(record);
        }

        // Divergent successors (two clones revising the same record) are a reconcile task, settled by a reconciliation record.
        var withdrawnBy = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (RecordEnvelope reconciliation in records.Where(r => r.Type == "reconciliation" && r.Subject is not null))
        {
            string? kept = EstateModel.BodyString(reconciliation, "kept");
            if (kept is null || !byId.ContainsKey(kept))
            {
                continue;
            }

            if (reconciliation.Body.TryGetProperty("withdrawn", out System.Text.Json.JsonElement withdrawn) && withdrawn.ValueKind == System.Text.Json.JsonValueKind.Array)
            {
                foreach (string id in withdrawn.EnumerateArray().Where(w => w.ValueKind == System.Text.Json.JsonValueKind.String).Select(w => w.GetString()!))
                {
                    if (byId.ContainsKey(id) && id != kept)
                    {
                        withdrawnBy.TryAdd(id, kept);
                    }
                }
            }
        }

        foreach ((string previousId, List<RecordEnvelope> list) in successors)
        {
            List<RecordEnvelope> remaining = list.Where(s => !withdrawnBy.ContainsKey(s.Id)).OrderBy(s => s.Id, StringComparer.Ordinal).ToList();
            bool revisionSet = remaining.All(RevisionLines.IsAuthored)
                && remaining.All(r => EstateModel.BodyString(r, "path") == EstateModel.BodyString(byId[previousId], "path"));
            if (remaining.Count > 1 && !revisionSet)
            {
                findings.Error("reconcile.successors", byId[previousId].ExpectedPath, $"Divergent revisions: {string.Join(", ", remaining.Select(s => s.Id))} all supersede {previousId}. Record a reconciliation that keeps one and withdraws the others.");
            }

            supersededBy[previousId] = (remaining.Count > 0 ? remaining[0] : list.OrderBy(s => s.Id, StringComparer.Ordinal).First()).Id;
        }

        foreach ((string withdrawn, string kept) in withdrawnBy)
        {
            supersededBy.TryAdd(withdrawn, kept);
        }

        // Authored-content revision pins retain their recorded predecessor, while their view is a mergeable,
        // deterministically ordered history. Legacy whole-file forks retain their reconciliation contract.
        foreach (var group in records.Where(RevisionLines.IsAuthored).GroupBy(r => EstateModel.BodyString(r, "path"), StringComparer.Ordinal))
        {
            RecordEnvelope[] ordered = group.OrderBy(r => r.At, StringComparer.Ordinal).ThenBy(r => r.Id, StringComparer.Ordinal).ToArray();
            foreach (RecordEnvelope revision in ordered)
                if (supersededBy.TryGetValue(revision.Id, out string? next) && RevisionLines.IsAuthored(byId[next])
                    && EstateModel.BodyString(byId[next], "path") == group.Key) supersededBy.Remove(revision.Id);
            for (int i = 0; i + 1 < ordered.Length; i++) supersededBy[ordered[i].Id] = ordered[i + 1].Id;
        }

        return supersededBy;
    }

    private static void ValidateSubject(EstateModel model, RecordEnvelope record, IRecordType type, FindingList findings)
    {
        if (record.Subject is null)
        {
            if (type.SubjectType is not null || type.SubjectHashRequired)
            {
                findings.Error("record.subject", record.ExpectedPath, $"A {record.Type} record needs a subject.");
            }

            return;
        }

        if (type.SubjectType is not null && record.Subject.Type != type.SubjectType)
        {
            findings.Error("record.subject", record.ExpectedPath, $"A {record.Type} record's subject must be a {type.SubjectType}.");
        }

        if (type.SubjectHashRequired && record.Subject.Hash is null)
        {
            findings.Error("record.subject", record.ExpectedPath, $"A {record.Type} record pins its subject to an exact hash.");
        }

        if (!model.ById.TryGetValue(record.Subject.Id, out RecordEnvelope? target))
        {
            findings.Error("record.subject", record.ExpectedPath, $"Subject {record.Subject.Id} does not exist.");
            return;
        }

        if (target.Type != record.Subject.Type)
        {
            findings.Error("record.subject", record.ExpectedPath, $"Subject {record.Subject.Id} is a {target.Type}, not a {record.Subject.Type}.");
        }

        if (record.Subject.Hash is not null && record.Subject.Hash != target.ComputedHash)
        {
            findings.Error("record.subject-hash", record.ExpectedPath, $"Subject {record.Subject.Id} does not have the pinned content hash; the referenced record is not the one this record relied on.");
        }

        if (!model.Counts(target))
        {
            findings.Error("record.subject-not-counting", record.ExpectedPath, $"Subject {record.Subject.Id} does not count: it is kept as evidence and grants nothing, so this record cannot rest on it.");
        }

        RecordEnvelope latest = model.Latest(target);
        // An admission follows its device's identity, so a rename or a rotation of the device leaves it in force.
        // A Vericairn dependency observation uses the estate's charter only as an identity anchor, and a reserved package version's
        // records use the product declaration only as theirs.
        // Only a current record needs reconsideration; its own successor has already answered a historical judgment.
        if (model.IsCurrent(record) && record.Subject.Hash is not null && latest.Id != target.Id &&
            !WorkOrderRevisions.IsCurrentSubject(model, record.Subject, latest) &&
            !(Updates.VericairnReleaseObservation.Is(record) && target.Type == "estate" && latest.Id == model.CurrentEstate?.Id) &&
            !(target.Type == "product" && PackageRecordRules.PinsReserved(record)) &&
            !ReleaseType.IsPublicationOf(target, latest) && !(record.Type == "admission" && target.Type == "device"))
        {
            // A release withdrawn since answers what was said of its earlier revisions, so the withdrawal is named instead.
            if (ReleaseType.IsWithdrawalOf(target, latest))
            {
                findings.Info("record.subject-withdrawn", record.ExpectedPath, $"The subject, release {EstateModel.BodyString(latest, "version")}, was withdrawn by {latest.Id}; this record refers to an earlier revision, and the withdrawal answers it.");
            }
            else if (record.Type == "ask" && target.Type == "operation" && EstateModel.BodyString(latest, "action") == "agent-run" &&
                     EstateModel.BodyString(latest, "status") is "observed-success" or "known-failure" or "canceled" && !Asks.IsWaiting(Asks.State(model, record).State))
            {
                findings.Info("ask.run-settled", record.ExpectedPath, $"The run settled in operation {latest.Id}; this ask is already {Asks.State(model, record).State} and needs no reconsideration because of that settlement.");
            }
            else
            {
                findings.Warning("record.subject-stale", record.ExpectedPath, $"The subject has been superseded by {latest.Id}; this record refers to an earlier revision and needs reconsideration.");
            }
        }
    }
}
