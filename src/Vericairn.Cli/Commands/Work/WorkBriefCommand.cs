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
        var agentContext = new Option<bool>("--agent-context") { Description = "Use the measured agent brief served by MCP; named optional bodies stay withheld without a byte allocation." };
        var maxBytes = new Option<long?>("--max-bytes") { Description = "Context byte allocation; implies --agent-context. Mandatory overflow refuses instead of truncating." };
        Arguments.Add(keyArgument);
        Options.Add(agentContext);
        Options.Add(maxBytes);
        SetAction(parseResult => operations.WorkBrief(parseResult.GetValue(context.EstateOption)!, parseResult.GetValue(keyArgument)!, parseResult.GetValue(context.JsonOption), context.Presentation.Read(parseResult), parseResult.GetValue(agentContext), parseResult.GetValue(maxBytes)));
    }
}
