// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jayson Furr

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
    public static EstateModel Load(string root, IReadOnlyList<RecordFile> files)
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

            RecordEnvelope? record = RecordParser.Parse(file.Bytes, location, findings);
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

        var supersededBy = new Dictionary<string, string>(StringComparer.Ordinal);
        var successors = new Dictionary<string, List<RecordEnvelope>>(StringComparer.Ordinal);
        foreach (RecordEnvelope record in records.Where(r => r.Supersedes is not null))
        {
            string previousId = record.Supersedes!;
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
            if (remaining.Count > 1)
            {
                findings.Error("reconcile.successors", byId[previousId].ExpectedPath, $"Divergent revisions: {string.Join(", ", remaining.Select(s => s.Id))} all supersede {previousId}. Record a reconciliation that keeps one and withdraws the others.");
            }

            supersededBy[previousId] = (remaining.Count > 0 ? remaining[0] : list.OrderBy(s => s.Id, StringComparer.Ordinal).First()).Id;
        }

        foreach ((string withdrawn, string kept) in withdrawnBy)
        {
            supersededBy.TryAdd(withdrawn, kept);
        }

        var model = new EstateModel(root, records, byId, supersededBy, findings);
        foreach (RecordEnvelope record in records)
        {
            IRecordType? type = RecordTypeRegistry.Find(record.Type);
            if (type is null)
            {
                string hint = Array.IndexOf(RecordTypeRegistry.Reserved, record.Type) >= 0 ? " It is reserved for a later release of record format 1." : string.Empty;
                findings.Error("record.unknown-type", record.ExpectedPath, $"Unknown record type '{record.Type}'.{hint}");
                continue;
            }

            ValidateSubject(model, record, type, findings);
            type.Validate(new ValidationContext(model, record, findings));
        }

        SignatureChecks.Run(model, findings);
        model.Manifest = EstateManifest.Load(root, findings);
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

        RecordEnvelope latest = model.Latest(target);
        if (record.Subject.Hash is not null && latest.Id != target.Id && !ReleaseType.IsPublicationOf(target, latest))
        {
            findings.Warning("record.subject-stale", record.ExpectedPath, $"The subject has been superseded by {latest.Id}; this record refers to an earlier revision and needs reconsideration.");
        }
    }
}
