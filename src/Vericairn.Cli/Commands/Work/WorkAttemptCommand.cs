// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.CommandLine;

namespace Vericairn.Cli;

internal sealed class WorkAttemptCommand : Command
{
    public WorkAttemptCommand(CommandContext context)
        : base("attempt", "Record the start of an attempt by the assignment's contributor.")
    {
        var operations = new WorkOperations(context);
        var byOption = new Option<string>("--by") { Description = "Authoring principal id (ULID).", Required = true };
        var assignmentArgument = new Argument<string>("assignment") { Description = "Assignment record id." };
        var workspaceOption = new Option<string>("--workspace") { Description = "Where the work happens, named without machine paths (a repository and branch, a clone, a tool).", Required = true };
        var attemptNoteOption = new Option<string>("--note") { Description = "What the contributor intends to do.", Required = true };
        var atOption = new Option<string?>("--at") { Description = "Start time, for example 2026-10-13T14:00:00Z (default: now)." };
        Arguments.Add(assignmentArgument);
        Options.Add(byOption);
        Options.Add(workspaceOption);
        Options.Add(attemptNoteOption);
        Options.Add(atOption);
        SetAction(parseResult => operations.WorkAttempt(
            parseResult.GetValue(context.EstateOption)!,
            parseResult.GetValue(assignmentArgument)!,
            parseResult.GetValue(byOption)!,
            parseResult.GetValue(workspaceOption)!,
            parseResult.GetValue(attemptNoteOption)!,
            parseResult.GetValue(atOption),
            parseResult.GetValue(context.JsonOption)));
    }
}
