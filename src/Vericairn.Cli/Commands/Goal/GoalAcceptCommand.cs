// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.CommandLine;

namespace Vericairn.Cli;

internal sealed class GoalAcceptCommand : Command
{
    public GoalAcceptCommand(CommandContext context)
        : base("accept", "Accept a goal at its current revision, relying on its current evaluation: by its sponsor when the sponsor is a person, or the principal the sponsor delegated an ordinary goal's acceptance to, and by an operator, only an operator, when an agent sponsors it; never by its evaluator or a step's author, and only once every condition is found met and every record predicate holds. From 0.9.4 --reason or --step-reason also close the goal's run with its unsettled steps, as goal close does, recorded with the acceptance.")
    {
        var operations = new GoalOperations(context);
        var byOption = new Option<string>("--by") { Description = "Acting principal id (ULID).", Required = true };
        var goalArgument = new Argument<string>("goal") { Description = "Goal record id (any revision)." };
        var statementOption = new Option<string>("--statement") { Description = "What is accepted and on what basis.", Required = true };
        Option<string?> reasonOption = GoalOperations.ReasonOption();
        Option<string[]> stepReasonOption = GoalOperations.StepReasonOption();
        Arguments.Add(goalArgument);
        Options.Add(byOption);
        Options.Add(statementOption);
        Options.Add(reasonOption);
        Options.Add(stepReasonOption);
        SetAction(parseResult => operations.GoalAccept(parseResult.GetValue(context.EstateOption)!, parseResult.GetValue(goalArgument)!, parseResult.GetValue(byOption)!, parseResult.GetValue(statementOption)!,
            parseResult.GetValue(reasonOption), parseResult.GetValue(stepReasonOption) ?? [], parseResult.GetValue(context.JsonOption)));
    }
}
