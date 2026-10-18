// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.CommandLine;

namespace Vericairn.Cli;

internal sealed class GoalCommand : Command
{
    public GoalCommand(CommandContext context)
        : base("goal", "Goals: objectives the estate carries out, done when their record predicates hold, an evaluator who did none of the work finds every condition met, and they are accepted. Without a subcommand, the current goal condition by condition: the one goal neither achieved nor ended, or with none or several, the list.")
    {
        var operations = new GoalOperations(context);
        SetAction(parseResult => operations.Goal(parseResult.GetValue(context.EstateOption)!, null, parseResult.GetValue(context.JsonOption)));
        Subcommands.Add(new GoalShowCommand(context));
        Subcommands.Add(new GoalNextCommand(context));
        Subcommands.Add(new GoalProposeCommand(context));
        Subcommands.Add(new GoalActivateCommand(context));
        Subcommands.Add(new GoalPauseCommand(context));
        Subcommands.Add(new GoalResumeCommand(context));
        Subcommands.Add(new GoalStopCommand(context));
        Subcommands.Add(new GoalCustodyExceptionCommand(context));
        Subcommands.Add(new GoalStepCommand(context));
        Subcommands.Add(new GoalEvaluateCommand(context));
        Subcommands.Add(new GoalAcceptCommand(context));
    }
}
