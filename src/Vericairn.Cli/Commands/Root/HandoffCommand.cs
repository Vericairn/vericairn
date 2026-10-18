// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.CommandLine;

namespace Vericairn.Cli;

internal sealed class HandoffCommand : Command
{
    public HandoffCommand(CommandContext context)
        : base("handoff", "Prepare a complete record-derived hand-over; refuse missing sources or overflow. Remote is not built.")
    {
        var operations = new HandoffFiles(context);
        var handoffOut = new Option<string?>("--out") { Description = "Retain the portable package in a new file; never overwrite." };
        var handoffLimit = new Option<long?>("--max-bytes") { Description = "Optional payload byte limit; no truncation. No limit when omitted." };
        Options.Add(handoffOut);
        Options.Add(handoffLimit);
        SetAction(parseResult => operations.Handoff(parseResult.GetValue(context.EstateOption)!, parseResult.GetValue(handoffOut), parseResult.GetValue(handoffLimit), parseResult.GetValue(context.JsonOption), context.Presentation.Read(parseResult)));
    }
}
