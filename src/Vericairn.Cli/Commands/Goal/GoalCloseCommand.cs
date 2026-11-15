// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.CommandLine;

namespace Vericairn.Cli;

internal sealed class GoalCloseCommand : Command
{
    public GoalCloseCommand(CommandContext context)
        : base("close", "Close a goal's run that will not complete, from 0.9.4: each step it has not settled, neither done nor off, is recorded with its reason, and none of them counts as done. By who decides for the goal, a person: its sponsor, or an operator for a goal an agent sponsors; an agent's close is refused. Closing neither accepts nor stops the goal; accept it with goal accept, which takes the same reasons to close and accept at once.")
    {
        var operations = new GoalOperations(context);
        var byOption = new Option<string>("--by") { Description = "Acting principal id (ULID).", Required = true };
        var goalArgument = new Argument<string>("goal") { Description = "Goal record id (any revision)." };
        Option<string?> reasonOption = GoalOperations.ReasonOption();
        Option<string[]> stepReasonOption = GoalOperations.StepReasonOption();
        Arguments.Add(goalArgument);
        Options.Add(byOption);
        Options.Add(reasonOption);
        Options.Add(stepReasonOption);
        SetAction(parseResult => operations.GoalClose(parseResult.GetValue(context.EstateOption)!, parseResult.GetValue(goalArgument)!, parseResult.GetValue(byOption)!,
            parseResult.GetValue(reasonOption), parseResult.GetValue(stepReasonOption) ?? [], parseResult.GetValue(context.JsonOption)));
    }
}
