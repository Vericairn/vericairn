// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using Vericairn.Records.Authoring;
using Vericairn.Records.Estate;
using Vericairn.Records.Model;
using Vericairn.Runtime.Policies;

namespace Vericairn.Runtime.Service;

/// <summary>
/// Assignment start (from 0.9.4, POLICY-08; the estate's design/estate-policies.md §§ 6 to 8): under an active refuse-level
/// <c>vericairn.estate/concurrent-paths</c> policy in force, the write boundary refuses a new assignment, a revision or a new attempt that
/// leaves an unmet or unavailable pair, before writing, naming each pair and the remedies that apply to the write: sequencing with after or
/// narrowing with excludedChanges, and, for an attempt on recorded assignments, an exception for the exact pair of an unmet one. Record
/// validation never refuses; records already in the estate keep their reading, and an overlap among them is held only when work on it
/// starts.
/// </summary>
public sealed partial class EstateService
{
    /// <summary>
    /// The write boundary's concurrent-paths refusals for proposed records. A new assignment is refused for every pair it leaves unresolved;
    /// a revision of one only for a pair that its previous revision did not already leave unmet, unavailable or excepted, so that a
    /// corrective revision is never held by a pair it inherited, while that pair still holds its attempts and runs. A new attempt is refused
    /// for every pair of its assignment's current revision. The pairs read the proposed records, and this machine's registered clones for
    /// what they show of each assignment's release and the witnesses, as the overlap check's waits do. The instant is the later of the
    /// record's stated time and this runtime's clock, so that a record dated back before a policy's activation or an exception's expiry
    /// is held as one written now.
    /// </summary>
    private void ConcurrentPathsStart(EstateModel proposed, IReadOnlyList<AuthoredRecord> records, FindingList relevant)
    {
        if (session.Manifest.RecordFormat < 3 || !AssignmentOverlaps.Policies(proposed).Any(p => p.Level == "refuse")) return;
        RepositoryView? view = null;
        EstateModel? current = null;
        DateTimeOffset now = (AuthoringClock ?? WorkerOptions.Clock).GetUtcNow();
        foreach (AuthoredRecord record in records)
        {
            RecordEnvelope envelope = record.Envelope;
            DateTimeOffset at = envelope.AtTime > now ? envelope.AtTime : now;
            if (envelope.Type == "assignment")
            {
                if (!proposed.ById.TryGetValue(envelope.Id, out RecordEnvelope? assignment) || !proposed.IsCurrent(assignment)) continue;
                List<ConcurrentPaths.Standing> standing = [.. ConcurrentPaths.AtStart(proposed, assignment, view ??= View(proposed), at).Where(s => s.Holds)];
                if (standing.Count > 0 && envelope.Supersedes is string previous)
                {
                    current ??= LoadWith([]);
                    HashSet<(string, string, string)> inherited = current.ById.TryGetValue(previous, out RecordEnvelope? earlier) && earlier.Type == "assignment"
                        ? [.. ConcurrentPaths.AtStart(current, earlier, View(current), at).Where(s => s.Pair.Verdict != "satisfied").Select(s => Key(current, s))]
                        : [];
                    standing.RemoveAll(s => inherited.Contains(Key(proposed, s)));
                }

                if (ConcurrentPaths.Refusal(proposed, standing, envelope.Supersedes is null ? "the new assignment" : "this revision of the assignment", recorded: false) is string refusal)
                {
                    relevant.Add(new Finding(Severity.Error, "assignment.concurrent-paths", record.RelativePath + "#body.brief", refusal));
                }
            }
            else if (envelope.Type == "attempt" && envelope.Subject is not null && proposed.ById.TryGetValue(envelope.Subject.Id, out RecordEnvelope? subject) && subject.Type == "assignment")
            {
                if (ConcurrentPaths.Refusal(proposed, ConcurrentPaths.AtStart(proposed, proposed.Latest(subject), view ??= View(proposed), at), "a new attempt") is string refusal)
                {
                    relevant.Add(new Finding(Severity.Error, "attempt.concurrent-paths", record.RelativePath + "#subject", refusal));
                }
            }
        }

        // A pair by its policy, rule and the other party's lineage, which the proposed revision and its predecessor share.
        static (string, string, string) Key(EstateModel model, ConcurrentPaths.Standing standing)
        {
            string identity = ConcurrentPaths.Identity(model, standing.Pair.Overlap);
            return (standing.Policy.Record.Id, standing.Rule, identity);
        }
    }
}
