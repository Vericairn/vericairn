// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.CommandLine;

namespace Vericairn.Cli;

internal sealed class GoalShowCommand : Command
{
    public GoalShowCommand(CommandContext context)
        : base("show", "Show a goal condition by condition: its status, the budget its activation grants, each condition's verdict with the records it read beside its current evaluation's, its steps, and what stands between it and its acceptance.")
    {
        var operations = new GoalOperations(context);
        var shownArgument = new Argument<string?>("goal") { Description = "Goal record id (any revision); omit for the current goal.", Arity = ArgumentArity.ZeroOrOne };
        Arguments.Add(shownArgument);
        SetAction(parseResult => operations.Goal(parseResult.GetValue(context.EstateOption)!, parseResult.GetValue(shownArgument), parseResult.GetValue(context.JsonOption)));
    }
}
