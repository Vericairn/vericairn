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
    /// Checks the proposed record before any signer is asked: its body with the same type validator as the full estate judgment, the
    /// subject rules the record alone decides, and (FIX-158) its own bytes with the content scan the estate check runs on every file.
    /// </summary>
    internal static FindingList CheckRecordBody(EstateModel model, RecordEnvelope record, string? relativePath = null, byte[]? bytes = null)
    {
        var bodyFindings = new FindingList();
        if (FindType(record, bodyFindings) is IRecordType type)
        {
            type.Validate(new ValidationContext(model, record, new FindingList(), bodyFindings));
            ValidateSubjectShape(record, type, bodyFindings);
        }

        if (relativePath is not null && bytes is not null)
        {
            foreach (Finding finding in EstateChecks.Content(relativePath, bytes)) bodyFindings.Add(finding);
        }

        return bodyFindings;
    }

    /// <summary>
    /// The records of a file set as identities only: each parsed record with its id, hash and body, supersession and the manifest,
    /// with parse findings alone. No record type, reference, authority, signature or admission is judged, no clock is read, and no
    /// file is read but the record files given and the manifest. A caller that needs any judgment loads the estate with <see cref="Load(string, IReadOnlyList{RecordFile})"/>.
    /// </summary>
    internal static EstateModel LoadIdentities(string root, IReadOnlyList<RecordFile> files, RecordParseCache? cache, JudgmentContext? context = null) => Load(root, files, cache, judge: false, context: context);

    /// <summary>
    /// The records of a file set judged with every record's roles and grants read at the record's own time, as an estate that has adopted
    /// history checkpoints reads the records its adoption does not witness: what adoption's check compares with the reading of today
    /// (design/signed-history.md § 10). Such a model serves that comparison alone.
    /// </summary>
    internal static EstateModel LoadAtRecordTime(string root, IReadOnlyList<RecordFile> files, Func<string, byte[]?>? objects, JudgmentContext? context = null) =>
        Load(root, files, null, null, objects, authorityAtRecordTime: true, context: context);

    // Only parsing and, from 0.9.4 (FIX-148), the arithmetic of signatures that verified are reused. References, authority, admissions,
    // closings, manifest and output-file findings are rejudged on every load. A judgment and every parse are counted in the context given,
    // and its verifications read that context's signature tables; without one, the process's tables, uncounted.
    internal static EstateModel Load(string root, IReadOnlyList<RecordFile> files, RecordParseCache? cache, byte[]? manifestBytes = null, Func<string, byte[]?>? objects = null, bool judge = true,
        bool authorityAtRecordTime = false, JudgmentContext? context = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        ArgumentNullException.ThrowIfNull(files);
        root = Path.GetFullPath(root);
        var findings = new FindingList();
        var records = new List<RecordEnvelope>();
        var byId = new Dictionary<string, RecordEnvelope>(StringComparer.Ordinal);
        var read = new List<(RecordEnvelope Record, string Location)>();
        HashSet<string>? duplicated = null;
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

            RecordEnvelope? record;
            if (cache is null)
            {
                context?.Parsed();
                record = RecordParser.Parse(file.Bytes, location, findings);
            }
            else
            {
                record = cache.Parse(file, findings, context);
            }
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
                (duplicated ??= new HashSet<string>(StringComparer.Ordinal)).Add(record.Id);
            }

            read.Add((record, location));
        }

        List<List<RecordEnvelope>> unreadable = [];
        if (duplicated is null)
        {
            records.AddRange(read.Select(r => r.Record));
        }
        else
        {
            unreadable = LeaveOutDuplicates(read, duplicated, records, byId, findings);
        }

        var supersession = new FindingList();
        Dictionary<string, string> supersededBy = Successors(records, byId, supersession);
        // A judgment reserves room in the signature tables for every signature it may verify, by every key its device records name,
        // before it verifies any, so the bound follows the estate (design/performance.md § 3.1); so does an identities load given a
        // context, whose caller may judge its trust itself. From 0.9.4 (FIX-159) the records left unread are judged too, in each reading
        // of them (RefusedWhileUnread), so they are reserved for as well.
        using Signing.SignatureTables.Judgment? signatures = judge || context is not null
            ? (context?.Tables ?? Signing.SignatureTables.Process).Begin(read.Sum(r => r.Record.Signatures.Count), DistinctKeys(read.Select(r => r.Record).ToList()), context)
            : null;
        var model = new EstateModel(root, records, byId, supersededBy, findings)
        {
            ObjectReader = objects ?? FileObjects(root),
            FilePaths = files.Select(f => f.Path).ToHashSet(StringComparer.Ordinal),
            AuthorityAtRecordTime = authorityAtRecordTime,
            Signatures = signatures,
            UnreadableLines = unreadable,
        };
        if (!judge)
        {
            // Identities only: no trust, counting, type, signature or manifest judgment; just the manifest as it reads.
            model.Manifest = manifestBytes is null ? EstateManifest.Load(root, new FindingList()) : EstateManifest.Parse(manifestBytes, new FindingList());
            return model;
        }


        context?.Judged();
        // The manifest is read before anything is judged, because which unsigned admissions count depends on the estate's record format;
        // its findings keep their place after the signatures'. Signatures, anchors and history checkpoints are judged from every record
        // read, and then the model's lookups keep only the records that count, before any rule reads them (design/signed-history.md § 6).
        var manifestFindings = new FindingList();
        EstateManifest? manifest = manifestBytes is null ? EstateManifest.Load(root, manifestFindings) : EstateManifest.Parse(manifestBytes, manifestFindings);
        EstateTrust trust = EstateTrust.Build(model, manifest, model.ObjectReader, model.FilePaths);
        IReadOnlySet<string> excluded = trust.NotCounting();
        // From 0.9.4 (FIX-159): while records are unread, a record counts only when every reading of them counts it, so no record counts
        // because the record that would refuse it is unread. Judged here, once, where counting is judged, so that every reading, the write
        // boundary and every acting check inherit it.
        if (unreadable.Count > 0 && RefusedWhileUnread(model, manifest, read, unreadable, excluded, findings) is { Count: > 0 } refused)
        {
            excluded = new HashSet<string>(excluded.Concat(refused), StringComparer.Ordinal);
        }

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

        // A policy record whose approval or authority its own validation finds missing counts for nothing, so that no policy, exception,
        // writing entry, prompt choice or reconciliation takes effect through an act its author could not perform; nor does a policy
        // record that fails only because an operator role it rests on was taken away before history checkpoints could place it earlier,
        // which is reported as one warning instead of errors. Judged once the counting records are known, since authority reads them.
        // Policy records give no role, and the grants among the latter rest on a role already gone, so one pass settles it.
        IReadOnlySet<string> unauthorized = Types.PolicyAuthority.Unauthorized(model);
        IReadOnlyDictionary<string, IReadOnlyList<RecordEnvelope>> removedBeforeHistory = Types.PolicyAuthority.RemovedBeforeHistory(model);
        // From 0.9.4 (ID-07) a dispatcher's assignment outside its dispatch grant counts for nothing, and so does any step's work it records.
        IReadOnlySet<string> dispatched = Types.DispatchAuthority.Withheld(model);
        if (unauthorized.Count > 0 || removedBeforeHistory.Count > 0 || dispatched.Count > 0)
        {
            var withheld = new HashSet<string>(excluded, StringComparer.Ordinal);
            withheld.UnionWith(unauthorized);
            withheld.UnionWith(dispatched);
            withheld.UnionWith(removedBeforeHistory.Keys);
            model.ApplyCounting(trust, withheld, Successors(records.Where(r => !withheld.Contains(r.Id)).ToList(), byId, new FindingList(), r => trust.AttachedTo(r)?.Id));
        }

        foreach (RecordEnvelope record in records)
        {
            if (FindType(record, findings) is not IRecordType type) continue;

            if (removedBeforeHistory.TryGetValue(record.Id, out IReadOnlyList<RecordEnvelope>? losses))
            {
                // Its errors are the removal's: it is reported once, as counting for nothing, with whatever else its checks note.
                var own = new FindingList();
                ValidateSubject(model, record, type, own);
                type.Validate(new ValidationContext(model, record, own));
                foreach (Finding finding in own.Where(f => f.Severity != Severity.Error)) findings.Add(finding);
                findings.Add(Types.PolicyAuthority.RemovedWarning(model, record, losses));
                if (RecordAuthorship.Unresolved(model, record) is Finding unresolved) findings.Add(unresolved);
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

        foreach (Finding finding in trust.RootFindings)
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
    /// From 0.9.4 (FIX-156): an identifier more than one record file carries is unreadable, whatever their dates, paths or contents, and so
    /// is every record of its line: every record joined to one of those files through supersedes, either way, the record each copy says it
    /// supersedes among them. None of them enters the estate as read, so no reading takes one copy in another's place, takes an earlier
    /// revision of the line for its latest, or reads a later revision as a line of its own. Each copy is reported (record.duplicate-id, an
    /// error and a reconcile task) and each other record of its line noted (record.duplicate-line); the records left out are kept apart,
    /// line by line, for the readings that count what they cannot read against the principal it would favor
    /// (<see cref="EstateModel.UnreadableLines"/>). Removing all but one copy makes the line read again.
    /// </summary>
    private static List<List<RecordEnvelope>> LeaveOutDuplicates(List<(RecordEnvelope Record, string Location)> read, HashSet<string> duplicated,
        List<RecordEnvelope> records, Dictionary<string, RecordEnvelope> byId, FindingList findings)
    {
        var parent = new Dictionary<string, string>(StringComparer.Ordinal);
        string Find(string id)
        {
            string root = id;
            while (parent.TryGetValue(root, out string? above) && above != root) root = above;
            while (parent.TryGetValue(id, out string? above) && above != root)
            {
                parent[id] = root;
                id = above;
            }

            return root;
        }

        foreach ((RecordEnvelope record, _) in read)
        {
            if (record.Supersedes is string prior && Find(record.Id) is string a && Find(prior) is string b && a != b)
            {
                parent[a] = b;
            }
        }

        Dictionary<string, int> copies = read.Where(r => duplicated.Contains(r.Record.Id)).GroupBy(r => r.Record.Id, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);
        Dictionary<string, string> heldBy = duplicated.GroupBy(Find, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.Order(StringComparer.Ordinal).First(), StringComparer.Ordinal);
        var lines = new Dictionary<string, List<RecordEnvelope>>(StringComparer.Ordinal);
        var order = new List<List<RecordEnvelope>>();
        byId.Clear();
        foreach ((RecordEnvelope record, string location) in read)
        {
            string root = Find(record.Id);
            if (!heldBy.TryGetValue(root, out string? held))
            {
                records.Add(record);
                byId[record.Id] = record;
                continue;
            }

            if (!lines.TryGetValue(root, out List<RecordEnvelope>? line))
            {
                line = [];
                lines[root] = line;
                order.Add(line);
            }

            line.Add(record);
            if (copies.TryGetValue(record.Id, out int count))
            {
                findings.Error("record.duplicate-id", location, $"{count.ToString(System.Globalization.CultureInfo.InvariantCulture)} records carry id {record.Id}: none of them is read, nor any record of its line, until all but one is removed.");
            }
            else
            {
                findings.Warning("record.duplicate-line", location, $"Not read: its line holds id {held}, which {copies[held].ToString(System.Globalization.CultureInfo.InvariantCulture)} records carry (record.duplicate-id); it is read again once all but one of them is removed.");
            }
        }

        return order;
    }

    /// <summary>
    /// From 0.9.4 (FIX-159): the most readings of the unread records the loader judges, beyond the reading that leaves them out. Each reading
    /// judges the estate's trust again, and the readings multiply with every identifier more than one file carries, so they are bounded;
    /// past the bound, which records a reading would refuse is not established, and no record counts.
    /// </summary>
    internal const int MaxUnreadReadings = 16;

    /// <summary>
    /// From 0.9.4 (FIX-159): the records read that count for nothing while records are unread because a reading of the unread records refuses
    /// them. A record refuses others when read: a charter's signature requirement refuses unsigned records, its operator roots every
    /// admission, a revocation ends what its device signs, a history checkpoint's closing refuses what its device, key, grant or person signs
    /// and it does not witness, an adoption refuses unanchored unsigned records, and a witnessed listing refuses other bytes under its
    /// identifier. Left out, such a record refuses nothing, so what it refused would count. No list of refusers is complete, so none is
    /// kept: the estate is judged again with the unread records read each way they can be, each identifier more than one file carries
    /// read as one of its records, any copy with the same content and signatures once, or as none of them, with the rest of its line,
    /// and a record counts only when the reading that leaves them out and every such reading count it. One of those readings is the estate
    /// before the copies were written, so whatever that estate refused stays refused, and since the reading that leaves them out is among
    /// them, a reading of a copy can only take counting away, never give it. Past <see cref="MaxUnreadReadings"/> readings no record counts
    /// (record.duplicate-unsettled, an error); otherwise the records refused this way are reported once (record.duplicate-refused, a
    /// warning). Only counting is judged here: the records read keep the reading that leaves the unread records out in every other respect.
    /// </summary>
    private static HashSet<string> RefusedWhileUnread(EstateModel model, EstateManifest? manifest, List<(RecordEnvelope Record, string Location)> read,
        List<List<RecordEnvelope>> unreadable, IReadOnlySet<string> excluded, FindingList findings)
    {
        var refused = new HashSet<string>(StringComparer.Ordinal);
        var unread = new HashSet<RecordEnvelope>(unreadable.SelectMany(line => line), ReferenceEqualityComparer.Instance);
        string location = read.First(r => unread.Contains(r.Record)).Location;
        // Each line's readings: its records carried by one file each, with one choice for each identifier more than one file carries.
        var fixedRecords = new List<RecordEnvelope>();
        var choices = new List<List<RecordEnvelope?>>();
        foreach (List<RecordEnvelope> line in unreadable)
        {
            foreach (IGrouping<string, RecordEnvelope> group in line.GroupBy(r => r.Id, StringComparer.Ordinal))
            {
                if (group.Count() == 1)
                {
                    fixedRecords.Add(group.First());
                }
                else
                {
                    choices.Add([.. group.DistinctBy(Content, StringComparer.Ordinal), null]);
                }
            }
        }

        long readings = 1;
        foreach (List<RecordEnvelope?> choice in choices)
        {
            readings = Math.Min(readings * choice.Count, MaxUnreadReadings + 1L);
        }

        if (readings > MaxUnreadReadings)
        {
            foreach (RecordEnvelope record in model.Records) refused.Add(record.Id);
            findings.Error("record.duplicate-unsettled", location, $"{choices.Count.ToString(System.Globalization.CultureInfo.InvariantCulture)} identifiers are each carried by more than one record (record.duplicate-id), which can be read more than {MaxUnreadReadings.ToString(System.Globalization.CultureInfo.InvariantCulture)} ways: which records a reading of them would refuse is not established, so no record counts until all but one record carrying each identifier is removed.");
            return refused;
        }

        var reading = new int[choices.Count];
        while (true)
        {
            var chosen = new HashSet<RecordEnvelope>(fixedRecords, ReferenceEqualityComparer.Instance);
            for (int i = 0; i < choices.Count; i++)
            {
                if (choices[i][reading[i]] is RecordEnvelope record) chosen.Add(record);
            }

            foreach (string id in NotCountingIn(model, manifest, [.. read.Select(r => r.Record).Where(r => !unread.Contains(r) || chosen.Contains(r))]))
            {
                if (model.ById.ContainsKey(id)) refused.Add(id);
            }

            int next = 0;
            while (next < choices.Count && ++reading[next] == choices[next].Count)
            {
                reading[next++] = 0;
            }

            if (next == choices.Count) break;
        }

        refused.ExceptWith(excluded);
        if (refused.Count > 0)
        {
            string some = string.Join(", ", refused.Order(StringComparer.Ordinal).Take(5));
            findings.Warning("record.duplicate-refused", location, $"{refused.Count.ToString(System.Globalization.CultureInfo.InvariantCulture)} records count for nothing while records are unread (record.duplicate-id), because a reading of the unread records refuses them ({some}{(refused.Count > 5 ? ", ..." : string.Empty)}): a record counts only when it counts with the unread records left out and with them read each way they can be. They count again once all but one record carrying each identifier is removed.");
        }

        return refused;

        // Copies with the same content and signatures read alike, so each is one reading.
        static string Content(RecordEnvelope record) =>
            record.ComputedHash + " " + record.DeclaredHash + " " + string.Join(" ", record.Signatures.Select(s => s.Algorithm + ":" + s.Device + ":" + s.Signature));
    }

    /// <summary>
    /// From 0.9.4 (FIX-159): the records that count for nothing in one reading of an estate's records, each identifier carried once: judged as
    /// the loader judges counting, by signatures, anchors, history and closings, then the authority of policy records and of dispatchers.
    /// </summary>
    private static HashSet<string> NotCountingIn(EstateModel model, EstateManifest? manifest, List<RecordEnvelope> records)
    {
        var byId = new Dictionary<string, RecordEnvelope>(StringComparer.Ordinal);
        foreach (RecordEnvelope record in records) byId[record.Id] = record;
        var reading = new EstateModel(model.Root, records, byId, Successors(records, byId, new FindingList()), new FindingList())
        {
            ObjectReader = model.ObjectReader,
            FilePaths = model.FilePaths,
            AuthorityAtRecordTime = model.AuthorityAtRecordTime,
            Signatures = model.Signatures,
        };
        reading.Manifest = manifest;
        EstateTrust trust = EstateTrust.Build(reading, manifest, reading.ObjectReader, reading.FilePaths);
        var excluded = new HashSet<string>(trust.NotCounting(), StringComparer.Ordinal);
        reading.ApplyCounting(trust, excluded, Successors(records.Where(r => !excluded.Contains(r.Id)).ToList(), byId, new FindingList(), r => trust.AttachedTo(r)?.Id));
        excluded.UnionWith(Types.PolicyAuthority.Unauthorized(reading));
        excluded.UnionWith(Types.PolicyAuthority.RemovedBeforeHistory(reading).Keys);
        excluded.UnionWith(Types.DispatchAuthority.Withheld(reading));
        return excluded;
    }

    // The public keys the device records name: every key a signature may be verified with.
    private static int DistinctKeys(IReadOnlyList<RecordEnvelope> records) =>
        records.Where(r => r.Type == "device").Select(r => EstateModel.BodyString(r, "publicKey")).Where(k => k is not null).Distinct(StringComparer.Ordinal).Count();

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

    private static IRecordType? FindType(RecordEnvelope record, FindingList findings)
    {
        IRecordType? type = RecordTypeRegistry.Find(record.Type);
        if (type is null)
        {
            string hint = RecordTypeRegistry.Reserved.IndexOf(record.Type) >= 0 ? " It is reserved for a later release." : string.Empty;
            findings.Error("record.unknown-type", record.ExpectedPath, $"Unknown record type '{record.Type}'.{hint}");
            return null;
        }

        if (record.Format < type.SinceFormat)
        {
            findings.Error("record.type-format", record.ExpectedPath, $"Type '{record.Type}' belongs to record format {type.SinceFormat}, and this record states format {record.Format}.");
            return null;
        }

        return type;
    }

    // The subject rules the record alone decides: whether it has one, its type and whether it pins a hash. From 0.9.5 (FIX-158) they
    // also run before any signer is asked; false when the record has no subject to look up.
    private static bool ValidateSubjectShape(RecordEnvelope record, IRecordType type, FindingList findings)
    {
        if (record.Subject is null)
        {
            if (type.SubjectType is not null || type.SubjectHashRequired)
            {
                findings.Error("record.subject", record.ExpectedPath, $"A {record.Type} record needs a subject.");
            }

            return false;
        }

        if (type.SubjectType is not null && record.Subject.Type != type.SubjectType)
        {
            findings.Error("record.subject", record.ExpectedPath, $"A {record.Type} record's subject must be a {type.SubjectType}.");
        }

        if (type.SubjectHashRequired && record.Subject.Hash is null)
        {
            findings.Error("record.subject", record.ExpectedPath, $"A {record.Type} record pins its subject to an exact hash.");
        }

        return true;
    }

    private static void ValidateSubject(EstateModel model, RecordEnvelope record, IRecordType type, FindingList findings)
    {
        if (!ValidateSubjectShape(record, type, findings) || record.Subject is null)
        {
            return;
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
            else if (target.Type == record.Subject.Type && record.Subject.Hash == target.ComputedHash && model.Counts(target)
                && SubjectSettlement(model, record, target, latest) is { } settled)
            {
                findings.Info("record.subject-settled", record.ExpectedPath,
                    $"The subject has been superseded by {latest.Id}; {settled}.");
            }
            else
            {
                findings.Warning("record.subject-stale", record.ExpectedPath, $"The subject has been superseded by {latest.Id}; this record refers to an earlier revision and needs reconsideration.");
            }
        }
    }

    private static string? SubjectSettlement(EstateModel model, RecordEnvelope record, RecordEnvelope target, RecordEnvelope latest)
    {
        switch (record.Type)
        {
            case "ask":
                (string state, IReadOnlyList<RecordEnvelope> answers) = Asks.State(model, record);
                if (state == "answered" && answers.Count > 0)
                    return $"decision {answers[^1].Id} answers this ask";
                if (state == "withdrawn")
                    return $"ask {record.Id} records its withdrawal";
                break;
            case "operator-request":
                if (OperatorRequests.Closures(model, record).Where(r => SignatureChecks.IsAuthentic(model, r))
                    .OrderBy(r => r.At, StringComparer.Ordinal).ThenBy(r => r.Id, StringComparer.Ordinal).LastOrDefault() is { } closure)
                    return $"{closure.Type} {closure.Id} closes this operator request";
                break;
            case "operation" when EstateModel.BodyString(record, "action") == Types.OperationType.AgentRun:
                if (EstateModel.BodyString(record, "status") is "observed-success" or "known-failure" or "canceled")
                    return $"operation {record.Id} records the run's settlement";
                break;
            case "attempt" when target.Type == "assignment":
                IEnumerable<string> successors = model.Chain(latest).SkipWhile(id => id != target.Id).Skip(1);
                RecordEnvelope? later = successors.SelectMany(id => model.About(id, "attempt").Where(a => a.Subject == model.ById[id].Reference))
                    .Where(a => model.IsCurrent(a) && GoalRules.Before(record, a))
                    .OrderBy(a => a.At, StringComparer.Ordinal).ThenBy(a => a.Id, StringComparer.Ordinal).LastOrDefault();
                if (later is not null)
                    return $"attempt {later.Id} continues the work on successor assignment {later.Subject!.Id}";
                break;
            case "goal-status" when latest.Type == "goal":
                // Only a current acceptance settles it, as for a work order: a superseded one names nothing that stands.
                if (model.About(latest.Id, "acceptance").Where(a => model.IsCurrent(a) && a.Subject?.Hash == latest.ComputedHash)
                    .OrderBy(a => a.At, StringComparer.Ordinal).ThenBy(a => a.Id, StringComparer.Ordinal)
                    .FirstOrDefault(a => GoalRules.AcceptanceRefusals(model, latest, a.By, GoalRules.Basis(a), a).Count == 0) is { } achievement)
                    return $"acceptance {achievement.Id} achieves the goal's current revision";
                break;
            case "assignment" when latest.Type == "work-order":
                if (EstateStatus.CurrentAcceptance(model, latest) is { } acceptance)
                    return $"acceptance {acceptance.Id} accepts the work order";
                break;
        }

        return null;
    }
}
