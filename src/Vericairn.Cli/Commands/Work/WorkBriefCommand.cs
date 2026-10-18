// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.CommandLine;

namespace Vericairn.Cli;

internal sealed class WorkBriefCommand : Command
{
    public WorkBriefCommand(CommandContext context)
        : base("brief", "Export the brief of a work order: everything a contributor needs, from the records alone.")
    {
        var operations = new WorkOperations(context);
        var keyArgument = new Argument<string>("work-order") { Description = "Work-order key (for example M1-WORK) or record id." };
        Arguments.Add(keyArgument);
        SetAction(parseResult => operations.WorkBrief(parseResult.GetValue(context.EstateOption)!, parseResult.GetValue(keyArgument)!, parseResult.GetValue(context.JsonOption), context.Presentation.Read(parseResult)));
    }
}
