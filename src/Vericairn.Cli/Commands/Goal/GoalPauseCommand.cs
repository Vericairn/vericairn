// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.CommandLine;

namespace Vericairn.Cli;

internal sealed class GoalPauseCommand : Command
{
    public GoalPauseCommand(CommandContext context)
        : base("pause", "Pause an active goal; its sponsor's, or an operator's for a goal an agent sponsors. Prevents new runs but does not stop runs already in flight unless --stop-runs asks them to; otherwise use 'vericairn worker stop' for each run that must stop.")
    {
        var operations = new GoalOperations(context);
        var byOption = new Option<string>("--by") { Description = "Acting principal id (ULID).", Required = true };
        var goalArgument = new Argument<string?>("goal") { Description = "Goal record id (any revision); omitted with --standing.", Arity = ArgumentArity.ZeroOrOne };
        var noteOption = new Option<string?>("--note") { Description = "Why, and what remains; a sentence naming the action when omitted." };
        var standingOption = new Option<bool>("--standing") { Description = "From 0.9.4: pause every active standing agent the acting principal may pause, instead of one goal, and say which it paused and which it may not." };
        var stopRunsOption = new Option<bool>("--stop-runs") { Description = "From 0.9.4: also ask each run of the paused goal in flight to stop." };
        Arguments.Add(goalArgument);
        Options.Add(byOption);
        Options.Add(noteOption);
        Options.Add(standingOption);
        Options.Add(stopRunsOption);
        SetAction(parseResult => operations.GoalPause(parseResult.GetValue(context.EstateOption)!, parseResult.GetValue(goalArgument), parseResult.GetValue(byOption)!, parseResult.GetValue(noteOption),
            parseResult.GetValue(standingOption), parseResult.GetValue(stopRunsOption), parseResult.GetValue(context.JsonOption)));
    }
}
