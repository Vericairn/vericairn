// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jayson Furr

using Vericairn.Records.Model;
using Vericairn.Records.Types;

namespace Vericairn.Records.Estate;

/// <summary>Loads every record of an estate and validates them individually and together.</summary>
public static class EstateLoader
{
    /// <summary>Loads the estate at <paramref name="root"/>.</summary>
    public static EstateModel Load(string root)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        root = Path.GetFullPath(root);
        var findings = new FindingList();
        var records = new List<RecordEnvelope>();
        var byId = new Dictionary<string, RecordEnvelope>(StringComparer.Ordinal);
        string recordsRoot = Path.Combine(root, EstateLayout.RecordsDirectory);
        if (Directory.Exists(recordsRoot))
        {
            foreach (string file in Directory.EnumerateFiles(recordsRoot, "*", SearchOption.AllDirectories).OrderBy(f => EstateLayout.Relative(root, f), StringComparer.Ordinal))
            {
                string location = EstateLayout.Relative(root, file);
                if (Path.GetFileName(file) == ".DS_Store")
                {
                    findings.Warning("records.residue", location, "Operating-system residue inside records/; delete it.");
                    continue;
                }

                if (!file.EndsWith(".json", StringComparison.Ordinal))
                {
                    findings.Error("records.unexpected-file", location, "Only record files (*.json) belong under records/.");
                    continue;
                }

                RecordEnvelope? record = RecordParser.Parse(File.ReadAllBytes(file), location, findings);
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
        }
        else
        {
            findings.Info("records.empty", EstateLayout.RecordsDirectory, "No records directory yet.");
        }

        var supersededBy = new Dictionary<string, string>(StringComparer.Ordinal);
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

            if (!supersededBy.TryAdd(previousId, record.Id))
            {
                findings.Error("record.supersedes", record.ExpectedPath, $"{previousId} is already superseded by {supersededBy[previousId]}; two successors conflict.");
            }
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

        model.Manifest = EstateManifest.Load(root, findings);
        if (model.Manifest is not null && model.CurrentEstate is not null && model.Manifest.Estate != model.CurrentEstate.Id)
        {
            findings.Error("manifest.estate", EstateLayout.ManifestPath, $"The manifest names estate {model.Manifest.Estate} but the current estate record is {model.CurrentEstate.Id}.");
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
        if (record.Subject.Hash is not null && latest.Id != target.Id)
        {
            findings.Warning("record.subject-stale", record.ExpectedPath, $"The subject has been superseded by {latest.Id}; this record refers to an earlier revision and needs reconsideration.");
        }
    }
}
