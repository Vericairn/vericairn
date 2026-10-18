// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.CommandLine;

namespace Vericairn.Cli;

internal sealed class GoalEvaluateCommand : Command
{
    public GoalEvaluateCommand(CommandContext context)
        : base("evaluate", "Record the evaluation of a goal's current revision, condition by condition, with the records each verdict read; by a principal other than any step's author.")
    {
        var operations = new GoalOperations(context);
        var byOption = new Option<string>("--by") { Description = "Acting principal id (ULID).", Required = true };
        var goalArgument = new Argument<string>("goal") { Description = "Goal record id (any revision)." };
        var summaryOption = new Option<string>("--summary") { Description = "What was reviewed and how.", Required = true };
        var verdictOption = new Option<string[]>("--verdict") { Description = "A condition's verdict, n:verdict:reason with n from 1 and the verdict met, not-yet or impossible; repeatable. Every evidence statement needs one; a record predicate gets the runtime's, unless the evaluator judges one the runtime does not find met impossible." };
        var readOption = new Option<string[]>("--read") { Description = "A record the evaluator read for a condition, n:record-id; repeatable. An evidence statement found met names at least one." };
        Arguments.Add(goalArgument);
        Options.Add(byOption);
        Options.Add(summaryOption);
        Options.Add(verdictOption);
        Options.Add(readOption);
        SetAction(parseResult => operations.GoalEvaluate(parseResult.GetValue(context.EstateOption)!, parseResult.GetValue(goalArgument)!, parseResult.GetValue(byOption)!, parseResult.GetValue(summaryOption)!, parseResult.GetValue(verdictOption) ?? [], parseResult.GetValue(readOption) ?? [], parseResult.GetValue(context.JsonOption)));
    }
}
