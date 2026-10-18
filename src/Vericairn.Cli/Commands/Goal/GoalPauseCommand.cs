// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.CommandLine;

namespace Vericairn.Cli;

internal sealed class GoalPauseCommand : Command
{
    public GoalPauseCommand(CommandContext context)
        : base("pause", "Pause an active goal; its sponsor's, or an operator's for a goal an agent sponsors. Prevents new runs but does not stop runs already in flight; use 'vericairn worker stop' for each run that must stop.")
    {
        var operations = new GoalOperations(context);
        var byOption = new Option<string>("--by") { Description = "Acting principal id (ULID).", Required = true };
        var goalArgument = new Argument<string>("goal") { Description = "Goal record id (any revision)." };
        var noteOption = new Option<string?>("--note") { Description = "Why, and what remains; a sentence naming the action when omitted." };
        Arguments.Add(goalArgument);
        Options.Add(byOption);
        Options.Add(noteOption);
        SetAction(parseResult => operations.GoalAction(parseResult.GetValue(context.EstateOption)!, "pause", parseResult.GetValue(goalArgument)!, parseResult.GetValue(byOption)!, parseResult.GetValue(noteOption), parseResult.GetValue(context.JsonOption)));
    }
}
