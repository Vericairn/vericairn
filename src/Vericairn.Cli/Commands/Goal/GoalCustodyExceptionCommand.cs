// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.CommandLine;

namespace Vericairn.Cli;

internal sealed class GoalCustodyExceptionCommand : Command
{
    public GoalCustodyExceptionCommand(CommandContext context)
        : base("custody-exception", "Record the sponsor's exception to the custody precondition for a goal's current revision: until it expires, a coding agent's run of the goal may launch unattended on a machine where a key of an operator or of the sponsor signs without its owner's presence, and the run names the exception. Recorded only by the goal's sponsor, or by an operator when an agent sponsors it; a revised goal needs a fresh one.")
    {
        var operations = new GoalOperations(context);
        var byOption = new Option<string>("--by") { Description = "Acting principal id (ULID).", Required = true };
        var goalArgument = new Argument<string>("goal") { Description = "Goal record id (any revision)." };
        var reasonOption = new Option<string>("--reason") { Description = "Why unattended runs of the goal may launch while such a key signs where they run.", Required = true };
        var expiresOption = new Option<string>("--expires") { Description = "When the exception stops being in force, for example 2026-10-20T00:00:00Z.", Required = true };
        Arguments.Add(goalArgument);
        Options.Add(byOption);
        Options.Add(reasonOption);
        Options.Add(expiresOption);
        SetAction(parseResult => operations.GoalCustodyException(parseResult.GetValue(context.EstateOption)!, parseResult.GetValue(goalArgument)!, parseResult.GetValue(byOption)!, parseResult.GetValue(reasonOption)!, parseResult.GetValue(expiresOption)!, parseResult.GetValue(context.JsonOption)));
    }
}
