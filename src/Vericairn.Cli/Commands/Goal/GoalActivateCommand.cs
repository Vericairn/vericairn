// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.CommandLine;

namespace Vericairn.Cli;

internal sealed class GoalActivateCommand : Command
{
    public GoalActivateCommand(CommandContext context)
        : base("activate", "Activate a proposed goal, which grants its budget: by its sponsor, a person who holds the operator role or a grant of the spending scope, or by an operator when an agent sponsors it. An agent never activates a goal.")
    {
        var operations = new GoalOperations(context);
        var byOption = new Option<string>("--by") { Description = "Acting principal id (ULID).", Required = true };
        var goalArgument = new Argument<string>("goal") { Description = "Goal record id (any revision)." };
        var noteOption = new Option<string?>("--note") { Description = "Why, and what remains; a sentence naming the action when omitted." };
        Arguments.Add(goalArgument);
        Options.Add(byOption);
        Options.Add(noteOption);
        SetAction(parseResult => operations.GoalAction(parseResult.GetValue(context.EstateOption)!, "activate", parseResult.GetValue(goalArgument)!, parseResult.GetValue(byOption)!, parseResult.GetValue(noteOption), parseResult.GetValue(context.JsonOption)));
    }
}
