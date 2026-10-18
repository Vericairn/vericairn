// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.CommandLine;

namespace Vericairn.Cli;

internal sealed class GoalResumeCommand : Command
{
    public GoalResumeCommand(CommandContext context)
        : base("resume", "Resume a paused, budget-limited or blocked goal, which grants its budget again; by whoever may activate it.")
    {
        var operations = new GoalOperations(context);
        var byOption = new Option<string>("--by") { Description = "Acting principal id (ULID).", Required = true };
        var goalArgument = new Argument<string>("goal") { Description = "Goal record id (any revision)." };
        var noteOption = new Option<string?>("--note") { Description = "Why, and what remains; a sentence naming the action when omitted." };
        Arguments.Add(goalArgument);
        Options.Add(byOption);
        Options.Add(noteOption);
        SetAction(parseResult => operations.GoalAction(parseResult.GetValue(context.EstateOption)!, "resume", parseResult.GetValue(goalArgument)!, parseResult.GetValue(byOption)!, parseResult.GetValue(noteOption), parseResult.GetValue(context.JsonOption)));
    }
}
