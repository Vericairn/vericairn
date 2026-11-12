// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using Vericairn.Records.Model;

namespace Vericairn.Records.Estate;

/// <summary>
/// Who acts as the operator for policy authority, and when: the principal records that count, read in time order, ids breaking ties,
/// from the root operator. A record changes whether a line gives the operator role only when its author is the root operator, or held
/// the operator role at that point, or held it earlier and takes it away; no principal's own record, other than the root operator's,
/// changes anything. A record is read as written only when it may widen (<c>mayWiden</c>, a signed record) and its author stands now,
/// or, for an author who no longer stands, when <c>retain</c> places it before the revision that took the role away; any other record
/// is read only as a removal: from it on its line gives the operator role only if it gave it before and the record gives it too. So a
/// revision that only takes the role away keeps counting whoever wrote it and whatever became of its author, an unsigned revision can
/// take the role away but not give it, and a former operator's appointment, dated back into the time the role stood, gives nothing.
/// Records read only as removals are found by reading again, one level of appointment at a time, until nothing more is set aside.
/// </summary>
internal sealed class OperatorReplay
{
    private readonly Dictionary<string, List<(RecordEnvelope Record, bool Operator)>> lines;

    private OperatorReplay(Dictionary<string, List<(RecordEnvelope Record, bool Operator)>> lines, HashSet<string> standing, HashSet<string> written)
    {
        this.lines = lines;
        StandingNow = standing;
        Written = written;
    }

    /// <summary>The principals that give the operator role once every record is read, the root operator among them.</summary>
    internal IReadOnlySet<string> StandingNow { get; }

    /// <summary>The ids of the records read as written; every other record read changes a line only as a removal.</summary>
    internal IReadOnlySet<string> Written { get; }

    /// <summary>Whether a line gives the operator role at a time: as its latest record read dated at or before it left it.</summary>
    internal bool OperatorAt(string line, string at)
    {
        if (!lines.TryGetValue(line, out List<(RecordEnvelope Record, bool Operator)>? read)) return false;
        bool value = false;
        foreach ((RecordEnvelope record, bool holds) in read)
        {
            if (string.CompareOrdinal(record.At, at) > 0) break;
            value = holds;
        }

        return value;
    }

    /// <summary>The record that took the operator role away from a line after a time: the first read dated after it that leaves the line without it.</summary>
    internal RecordEnvelope? LossAfter(string line, string at) =>
        lines.TryGetValue(line, out List<(RecordEnvelope Record, bool Operator)>? read)
            ? read.FirstOrDefault(r => string.CompareOrdinal(r.Record.At, at) > 0 && !r.Operator).Record
            : null;

    /// <summary>
    /// The loss a line's operator role at a time rests on through its appointment: the record that took the role away, after the record
    /// that gave the line the role then, from that record's author, or, while that author still holds it, from the author of the record
    /// that gave the author theirs, and so on up to the root operator. Null when no appointer lost it.
    /// </summary>
    internal RecordEnvelope? AppointerLoss(string line, string at, string? root)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        while (seen.Add(line) && GivenAt(line, at) is { } given && given.By != root)
        {
            if (LossAfter(given.By, given.At) is { } loss) return loss;
            (line, at) = (given.By, given.At);
        }

        return null;
    }

    /// <summary>The record that gave a line the operator role it holds at a time: the first of the latest run of records read up to it that give it.</summary>
    private RecordEnvelope? GivenAt(string line, string at)
    {
        if (!lines.TryGetValue(line, out List<(RecordEnvelope Record, bool Operator)>? read)) return null;
        RecordEnvelope? given = null;
        foreach ((RecordEnvelope record, bool holds) in read)
        {
            if (string.CompareOrdinal(record.At, at) > 0) break;
            given = holds ? given ?? record : null;
        }

        return given;
    }

    /// <summary>Reads the principal records (see the class summary).</summary>
    internal static OperatorReplay Run(IEnumerable<RecordEnvelope> principals, string? root, Func<RecordEnvelope, string> lineOf, Func<RecordEnvelope, bool> mayWiden,
        Func<RecordEnvelope, RecordEnvelope, bool> retain)
    {
        List<RecordEnvelope> ordered = [.. principals.DistinctBy(r => r.Id).OrderBy(r => r.At, StringComparer.Ordinal).ThenBy(r => r.Id, StringComparer.Ordinal)];
        var setAside = new HashSet<string>(StringComparer.Ordinal);
        while (true)
        {
            var state = new Dictionary<string, bool>(StringComparer.Ordinal);
            var former = new HashSet<string>(StringComparer.Ordinal);
            var lines = new Dictionary<string, List<(RecordEnvelope Record, bool Operator)>>(StringComparer.Ordinal);
            var written = new HashSet<string>(StringComparer.Ordinal);
            foreach (RecordEnvelope record in ordered)
            {
                string line = lineOf(record);
                bool rootsOwnFirst = record.Id == root && record.By == root && record.Supersedes is null;
                bool before = state.GetValueOrDefault(line);
                bool gives = AuthorityReplay.IsOperator(record);
                bool holds;
                if (rootsOwnFirst)
                {
                    holds = gives;
                    written.Add(record.Id);
                }
                else if (line == record.By && record.By != root)
                {
                    continue;
                }
                else if (Operator(record.By))
                {
                    bool full = mayWiden(record) && !setAside.Contains(record.Id);
                    holds = full ? gives : before && gives;
                    if (full) written.Add(record.Id);
                }
                else if (former.Contains(record.By) && before && !gives)
                {
                    holds = false;
                }
                else
                {
                    continue;
                }

                state[line] = holds;
                if (holds) former.Add(line);
                if (!lines.TryGetValue(line, out List<(RecordEnvelope Record, bool Operator)>? read)) lines[line] = read = [];
                read.Add((record, holds));
            }

            var standing = new HashSet<string>(state.Where(s => s.Value).Select(s => s.Key), StringComparer.Ordinal);
            if (root is not null) standing.Add(root);
            List<string> dropped = [.. ordered.Where(r => written.Contains(r.Id) && r.By != root && !standing.Contains(r.By) && !Retained(r)).Select(r => r.Id)];
            if (dropped.Count == 0) return new OperatorReplay(lines, standing, written);
            setAside.UnionWith(dropped);

            // A record by an author who no longer stands is read as written only when the caller places it before the revision that took the role away.
            bool Retained(RecordEnvelope record) => lines.TryGetValue(record.By, out List<(RecordEnvelope Record, bool Operator)>? author) &&
                author.FirstOrDefault(r => string.CompareOrdinal(r.Record.At, record.At) > 0 && !r.Operator).Record is { } loss && retain(record, loss);

            bool Operator(string author) => author == root && root is not null || state.GetValueOrDefault(author);
        }
    }
}
