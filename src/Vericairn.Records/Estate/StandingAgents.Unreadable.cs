// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using Vericairn.Records.Model;
using Vericairn.Records.Templates;

namespace Vericairn.Records.Estate;

/// <summary>
/// From 0.9.4 (FIX-156): a standing agent read beside the records the loader left out because their line holds an identifier more than
/// one record file carries (<see cref="EstateModel.UnreadableLines"/>). No reading takes any of them, so a duplicate can only fail toward
/// the person: the failure count counts what it cannot read as failed (<see cref="Failures"/>), the open outputs count it as open
/// (<see cref="StandingOutputs.OpenCount"/>), it ties every agent it would tie (<see cref="StandingOutputs.TieOf"/>), and every standing
/// agent reads held while any record is unread (<see cref="Unreadable"/>). While one is, a record that does not count, which may be one
/// whose device, admission, principal or grant is unread, is read against the agent and never for it (<see cref="ReadAgainst"/>).
/// </summary>
internal static partial class StandingAgents
{
    /// <summary>
    /// Every identifier a record names: its subject, the record it supersedes and every string anywhere in its body, whatever field holds
    /// it, so no field name a writer chooses hides a reference.
    /// </summary>
    internal static IEnumerable<string> Names(RecordEnvelope record)
    {
        ArgumentNullException.ThrowIfNull(record);
        if (record.Subject is not null)
        {
            yield return record.Subject.Id;
        }

        if (record.Supersedes is not null)
        {
            yield return record.Supersedes;
        }

        var pending = new Stack<JsonElement>();
        pending.Push(record.Body);
        while (pending.Count > 0)
        {
            JsonElement element = pending.Pop();
            switch (element.ValueKind)
            {
                case JsonValueKind.String:
                    yield return element.GetString()!;
                    break;
                case JsonValueKind.Object:
                    foreach (JsonProperty property in element.EnumerateObject())
                    {
                        pending.Push(property.Value);
                    }

                    break;
                case JsonValueKind.Array:
                    foreach (JsonElement item in element.EnumerateArray())
                    {
                        pending.Push(item);
                    }

                    break;
            }
        }
    }

    /// <summary>
    /// From 0.9.4 (FIX-156): whether a record is read only against a standing agent: while the estate leaves any record unread, a record that
    /// does not count, since what makes it count (its device, admission, principal or grant) may be the unread one. Such a record is read as a
    /// record of an agent tied to the goal is: a blocked or abandoned result as a failure, a known failure as the run's failure where no other
    /// revision decides it, an output as open and an assignment or roster revision as a tie, and never as a success, a cancellation, a
    /// correction, a stop, an answer or a disposition. False for every record of an estate that leaves none unread, which reads as before.
    /// </summary>
    internal static bool ReadAgainst(EstateModel model, RecordEnvelope record) => model.UnreadableLines.Count > 0 && !model.Counts(record);

    /// <summary>
    /// Records of a type that do not count, read only against a standing agent while the estate leaves any record unread
    /// (<see cref="ReadAgainst"/>); none otherwise.
    /// </summary>
    internal static IEnumerable<RecordEnvelope> AgainstOnly(EstateModel model, string type) =>
        model.UnreadableLines.Count == 0 ? [] : model.Records.Where(r => r.Type == type && !model.Counts(r));

    /// <summary>
    /// The record the loader left out as unreadable that holds a standing agent, or null for an estate that leaves none unread. While any is,
    /// every standing agent reads held: a record of the agent rests not only on what it names but on the device, admission, principal and
    /// grants that make it count, and through them on records anywhere in the estate, so no list of what an agent rests on is complete. The
    /// record named is the first unread record that names one of the agent's records (any revision of its goal or of its work order, an
    /// assignment of that work order, an attempt or result on one, a record of one of its runs, or a revision of one of its outputs or of an
    /// ask about its goal), else the first record whose identifier more than one record carries.
    /// </summary>
    internal static RecordEnvelope? Unreadable(EstateModel model, RecordEnvelope goal, RecordEnvelope? order)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(goal);
        if (model.UnreadableLines.Count == 0)
        {
            return null;
        }

        IReadOnlyList<string> goals = model.Chain(model.Latest(goal));
        string identity = goals[0];
        var ids = new HashSet<string>(goals, StringComparer.Ordinal);
        IReadOnlyList<string> orders = order is null ? [] : model.Chain(model.Latest(order));
        ids.UnionWith(orders);
        HashSet<string> assignments = [.. model.OfType("assignment").Where(a => a.Subject is not null && orders.Contains(a.Subject.Id)).Select(a => a.Id)];
        HashSet<string> attempts = [.. model.OfType("attempt").Where(a => a.Subject is not null && assignments.Contains(a.Subject.Id)).Select(a => a.Id)];
        ids.UnionWith(assignments);
        ids.UnionWith(attempts);
        ids.UnionWith(model.OfType("result").Where(r => r.Subject is not null && attempts.Contains(r.Subject.Id)).Select(r => r.Id));
        ids.UnionWith(model.OfType("operation").Where(o => TJson.Object(o.Body, "target") is { ValueKind: JsonValueKind.Object } target
            && ((TJson.Text(target, "goal") is string named && goals.Contains(named)) || (TJson.Text(target, "assignment") is string carried && assignments.Contains(carried)))).Select(o => o.Id));
        foreach (string type in new[] { "possibility", "ask", "operator-request" })
        {
            ids.UnionWith(model.OfType(type).Where(r => StandingOutputs.Of(r)?.Goal == identity || (EstateModel.BodyString(r, "goal") is string named && goals.Contains(named))).Select(r => r.Id));
        }

        return model.Unreadable.FirstOrDefault(r => Names(r).Any(ids.Contains))
            ?? model.Unreadable.GroupBy(r => r.Id, StringComparer.Ordinal).Where(g => g.Count() > 1).Select(g => g.First()).FirstOrDefault()
            ?? model.Unreadable.First();
    }
}
