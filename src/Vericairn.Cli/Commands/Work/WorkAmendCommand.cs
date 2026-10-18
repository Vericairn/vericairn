// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.CommandLine;

namespace Vericairn.Cli;

internal sealed class WorkAmendCommand : Command
{
    public WorkAmendCommand(CommandContext context)
        : base("amend", "Request a change to an assignment's outcome, scope, target, budget or permitted effects; it authorizes nothing.")
    {
        var operations = new WorkOperations(context);
        var byOption = new Option<string>("--by") { Description = "Authoring principal id (ULID).", Required = true };
        var assignmentArgument = new Argument<string>("assignment") { Description = "Assignment record id." };
        var bodyOption = new Option<string>("--body") { Description = "Path to a JSON file holding the body, or '-' for standard input.", Required = true };
        Arguments.Add(assignmentArgument);
        Options.Add(byOption);
        Options.Add(bodyOption);
        SetAction(parseResult => operations.WorkAmend(
            parseResult.GetValue(context.EstateOption)!,
            parseResult.GetValue(assignmentArgument)!,
            parseResult.GetValue(byOption)!,
            parseResult.GetValue(bodyOption)!,
            parseResult.GetValue(context.JsonOption)));
    }
}
