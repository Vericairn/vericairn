// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.CommandLine;

namespace Vericairn.Cli;

internal sealed class GoalNextCommand : Command
{
    public GoalNextCommand(CommandContext context)
        : base("next", "Show a goal's next steps routed from this machine: each eligible step with the principal its role's roster reaches (an agent where a launcher for its harness, its admitted key and the goal's budget allow, a person where needed, a reserved action only to one who holds it) or why none is available, whether the goal's budget allows it, and what holds the others. It records and starts nothing; vericairn goal step takes the next one.")
    {
        var operations = new GoalOperations(context);
        var shownArgument = new Argument<string?>("goal") { Description = "Goal record id (any revision); omit for the current goal.", Arity = ArgumentArity.ZeroOrOne };
        Arguments.Add(shownArgument);
        SetAction(parseResult => operations.GoalNext(parseResult.GetValue(context.EstateOption)!, parseResult.GetValue(shownArgument), parseResult.GetValue(context.JsonOption)));
    }
}
