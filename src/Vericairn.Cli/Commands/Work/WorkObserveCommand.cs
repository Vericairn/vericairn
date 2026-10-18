// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.CommandLine;

namespace Vericairn.Cli;

internal sealed class WorkObserveCommand : Command
{
    public WorkObserveCommand(CommandContext context)
        : base("observe", "Show a work order's assignments, attempts, results, amendments, reviews and acceptances.")
    {
        var operations = new WorkOperations(context);
        var keyArgument = new Argument<string>("work-order") { Description = "Work-order key (for example M1-WORK) or record id." };
        Arguments.Add(keyArgument);
        SetAction(parseResult => operations.WorkObserve(parseResult.GetValue(context.EstateOption)!, parseResult.GetValue(keyArgument)!, parseResult.GetValue(context.JsonOption), context.Presentation.Read(parseResult)));
    }
}
