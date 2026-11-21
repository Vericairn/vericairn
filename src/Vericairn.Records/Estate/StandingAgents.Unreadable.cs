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
/// (<see cref="StandingOutputs.OpenCount"/>), it ties every agent it would tie (<see cref="StandingOutputs.TieOf"/>), and an agent any such
/// record names reads held (<see cref="Unreadable"/>).
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
    /// The first record the loader left out as unreadable that names a record of a standing agent, or null when none does: any revision of
    /// its goal or of its work order, an assignment of that work order, an attempt or result on one, a record of one of its runs, or a
    /// revision of one of its outputs or of an ask about its goal. A goal-status, review, answer or any record of such a line names one, and
    /// so does a copy of any of them, since one copy is the record written. While one does, the agent reads held: its status, gates and
    /// occurrences may rest on what cannot be read, so nothing of it is read in its place. Null at once for an estate with no such record.
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

        return model.Unreadable.FirstOrDefault(r => Names(r).Any(ids.Contains));
    }
}
