// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.CommandLine;

namespace Vericairn.Cli;

internal sealed class ProgressCommand : Command
{
    public ProgressCommand(CommandContext context)
        : base("progress", "Show the estate's recorded progress with latest local agent claims and worker observations labelled separately: where the estate stands, the work orders by state, each published release and what it contained, what comes next by release, the next eligible work and what waits on the operator. Required attention names only open obligations with exact references and inspection commands; ordinary after ordering is planning. --json includes complete attention references.")
    {
        var operations = new EstateViews(context);
        SetAction(parseResult => operations.Progress(parseResult.GetValue(context.EstateOption)!, parseResult.GetValue(context.JsonOption), context.Presentation.Read(parseResult)));
    }
}
