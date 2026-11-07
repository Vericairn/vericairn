// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.CommandLine;
using Vericairn.Protocol;

namespace Vericairn.Cli;

internal sealed class EventsWatchCommand : Command
{
    public EventsWatchCommand(CommandContext context)
        : base("watch", "Follow the event stream of the daemon serving this estate, one line per event, reconnecting from the last cursor until interrupted. Start it before reading the state it complements, or resume with --after from a cursor, so no change is missed. A reset line means re-read that state, then continue. --json prints the opening cursor, each envelope and each reset as one JSON object per line.")
    {
        var after = new Option<string?>("--after") { Description = "Resume after this cursor: an event's, a reset's or a state read's eventsCursor. From now when omitted." };
        var kind = new Option<string[]>("--kind") { Description = "Only events of this kind: record, attention, ask, step, request, due, run or presence; repeatable.", AllowMultipleArgumentsPerToken = false };
        var type = new Option<string[]>("--type") { Description = "Only events whose subject has this type, such as a record type; repeatable.", AllowMultipleArgumentsPerToken = false };
        var subject = new Option<string[]>("--subject") { Description = "Only events about this record id; a goal matches its work orders, steps and runs; repeatable.", AllowMultipleArgumentsPerToken = false };
        var to = new Option<string[]>("--to") { Description = "Only events concerning this principal id; repeatable.", AllowMultipleArgumentsPerToken = false };
        var category = new Option<string[]>("--category") { Description = "Only attention of this category: decision-needed, blocked, uncertain-effect, failed-check, incident or information; repeatable.", AllowMultipleArgumentsPerToken = false };
        var principal = new Option<string?>("--principal") { Description = "The principal to read for, whose audience every event must pass. The operator when omitted; the runtime machine's user may name any principal." };
        var count = new Option<int?>("--count") { Description = "Stop after this many events. Follows until interrupted when omitted." };
        foreach (Option option in new Option[] { after, kind, type, subject, to, category, principal, count })
        {
            Options.Add(option);
        }

        var operations = new EventOperations(context);
        SetAction(p => operations.Watch(p.GetValue(context.EstateOption)!, p.GetValue(after),
            new EventFilter(Values(p.GetValue(kind)), Values(p.GetValue(type)), Values(p.GetValue(subject)), Values(p.GetValue(to)), Values(p.GetValue(category))),
            p.GetValue(principal), p.GetValue(count), p.GetValue(context.JsonOption)));
    }

    private static string[]? Values(string[]? values) => values is { Length: > 0 } ? values : null;
}
