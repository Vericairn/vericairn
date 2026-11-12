// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using Vericairn.Records.Estate;
using Vericairn.Records.Model;

namespace Vericairn.Records.History;

/// <summary>
/// What the at-time reading changes (design/signed-history.md §§ 8 and 10; ID-06): once an estate adopts history checkpoints, roles
/// and grants are read as they stood at each record's own time, except for the records its adoption witnesses, which keep the reading
/// they have then. Adoption's check lists the records that read otherwise at their own time than today, so that whoever signs the
/// adoption reads, before signing, what it keeps.
/// </summary>
internal static class AtTimeReading
{
    /// <summary>
    /// The records whose findings differ between a copy read today and the same copy read with every record's roles and grants at its
    /// own time, by id, each with what differs: a finding that holds only at its own time, or one that holds today and not then. A record
    /// that counts for nothing in both readings, such as an admission its signer could not make at its own time, keeps no reading at
    /// adoption, so it is not listed.
    /// </summary>
    internal static IReadOnlyList<(RecordEnvelope Record, string Note)> Changes(EstateModel today, EstateModel atTime)
    {
        ArgumentNullException.ThrowIfNull(today);
        ArgumentNullException.ThrowIfNull(atTime);
        Dictionary<string, Finding> now = Keyed(today);
        Dictionary<string, Finding> then = Keyed(atTime);
        Dictionary<string, RecordEnvelope> byPath = today.Records.GroupBy(r => r.ExpectedPath, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        var notes = new SortedDictionary<string, (RecordEnvelope Record, List<string> Notes)>(StringComparer.Ordinal);
        foreach (Finding finding in then.Where(f => !now.ContainsKey(f.Key)).OrderBy(f => f.Key, StringComparer.Ordinal).Select(f => f.Value))
            Note(finding, $"read at its own time, {finding.Code}: {finding.Message}");
        foreach (Finding finding in now.Where(f => !then.ContainsKey(f.Key)).OrderBy(f => f.Key, StringComparer.Ordinal).Select(f => f.Value))
            Note(finding, $"read today, {finding.Code}: {finding.Message} At its own time this does not hold.");
        return [.. notes.Values.Select(n => (n.Record, string.Join(" ", n.Notes)))];

        void Note(Finding finding, string note)
        {
            int hash = finding.Location.IndexOf('#', StringComparison.Ordinal);
            string path = hash < 0 ? finding.Location : finding.Location[..hash];
            if (!byPath.TryGetValue(path, out RecordEnvelope? record)) return;
            if (!today.Counts(record) && !(atTime.ById.TryGetValue(record.Id, out RecordEnvelope? then) && atTime.Counts(then))) return;
            if (!notes.TryGetValue(record.Id, out (RecordEnvelope Record, List<string> Notes) entry)) notes[record.Id] = entry = (record, []);
            if (!entry.Notes.Contains(note, StringComparer.Ordinal)) entry.Notes.Add(note);
        }
    }

    // Errors and warnings by severity, code and location; information says nothing a signer must weigh.
    private static Dictionary<string, Finding> Keyed(EstateModel model)
    {
        var keyed = new Dictionary<string, Finding>(StringComparer.Ordinal);
        foreach (Finding finding in model.Findings.Where(f => f.Severity != Severity.Info))
            keyed.TryAdd($"{finding.Severity}\n{finding.Code}\n{finding.Location}", finding);
        return keyed;
    }
}
