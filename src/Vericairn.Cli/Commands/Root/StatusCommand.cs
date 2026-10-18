// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.CommandLine;
using Vericairn.Sdk;

namespace Vericairn.Cli;

internal sealed class StatusCommand : Command
{
    public StatusCommand(CommandContext context, ReleaseUpdates updates)
        : base("status", "Show the estate's current state derived from its records, with compact required attention resolved from current evidence. Each item names its exact source and inspection command. --json is identical on every machine for the same records and includes complete record-derived attention references. Human status also shows labelled local worker concerns. " + UpdateNotices.Help)
    {
        var operations = new EstateViews(context);
        var observeStatus = new Option<bool>("--observe") { Description = "Explicitly record that a person observed status; requires --by. Ordinary reads record nothing." };
        var observer = new Option<string?>("--by") { Description = "The observing human principal; used only with --observe." };
        Options.Add(observeStatus);
        Options.Add(observer);
        SetAction(parseResult => UpdateNotices.Run(context, updates, () => operations.Status(parseResult.GetValue(context.EstateOption)!, parseResult.GetValue(context.JsonOption), parseResult.GetValue(observeStatus), parseResult.GetValue(observer), context.Presentation.Read(parseResult))));
    }
}
