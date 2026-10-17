// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using Vericairn.Records.Model;

namespace Vericairn.Records.Estate;

/// <summary>
/// Reuses envelope parsing and content hashes for unchanged original bytes, never semantic validation or its findings.
/// Owned by one serialized runtime session; source bytes are private snapshots, not buffers returned to callers.
/// </summary>
internal sealed class RecordParseCache
{
    private readonly Dictionary<string, Entry> entries = new(StringComparer.Ordinal);

    internal long ParsedFiles { get; private set; }

    internal RecordEnvelope? Parse(RecordFile file, FindingList findings)
    {
        if (!entries.TryGetValue(file.Path, out Entry? entry) ||
            (!ReferenceEquals(entry.Bytes, file.Bytes) && !entry.Bytes.AsSpan().SequenceEqual(file.Bytes)))
        {
            var parsedFindings = new FindingList();
            RecordEnvelope? parsed = RecordParser.Parse(file.Bytes, file.Path, parsedFindings);
            // Cached envelopes may be returned by Session.Load. Their signature collection must not let a caller mutate a
            // later load's input. The body is already an independent, immutable JsonElement; every model/list stays fresh.
            RecordEnvelope? record = parsed is null ? null : new RecordEnvelope(parsed.Type, parsed.Id, parsed.At, parsed.By,
                parsed.Subject, parsed.Supersedes, parsed.Body, parsed.DeclaredHash, parsed.ComputedHash,
                Array.AsReadOnly(parsed.Signatures.ToArray()), parsed.Format);
            entry = new Entry(file.Bytes, record, parsedFindings.ToArray());
            entries[file.Path] = entry;
            ParsedFiles++;
        }
        else if (!ReferenceEquals(entry.Bytes, file.Bytes))
        {
            // A store snapshot changed, but these bytes did not. Retain the new private buffer so later loads need no scan.
            entry = entry with { Bytes = file.Bytes };
            entries[file.Path] = entry;
        }

        foreach (Finding finding in entry.Findings)
        {
            findings.Add(finding);
        }
        return entry.Record;
    }

    internal void Retain(IReadOnlyDictionary<string, RecordFile> files)
    {
        foreach (string path in entries.Keys.Where(path => !files.ContainsKey(path)).ToArray())
        {
            entries.Remove(path);
        }
    }

    private sealed record Entry(byte[] Bytes, RecordEnvelope? Record, IReadOnlyList<Finding> Findings);
}
