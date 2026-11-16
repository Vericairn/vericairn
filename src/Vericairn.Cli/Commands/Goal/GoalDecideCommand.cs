// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.CommandLine;

namespace Vericairn.Cli;

internal sealed class GoalDecideCommand : Command
{
    public GoalDecideCommand(CommandContext context)
        : base("decide", "Decide a run's decision step, from 0.9.4: the principal holding the step's assignment, the person or the agent its role reached through goal step, records the path or paths its transition takes, with the reasons and the evidence by hash, as the result of that assignment, signed by its own device. A rule's decision is never recorded here: goal step records it. The first decision of a round is final, and only the step's current assignment decides. Choosing a path performs nothing reserved and dispatches nothing; goal step then takes the steps the path makes eligible, and a reserved step still waits on the operator. A decision the flow requires of a person, or one never delegated, is refused for an agent.")
    {
        var operations = new GoalOperations(context);
        var goalArgument = new Argument<string>("goal") { Description = "Goal record id (any revision)." };
        var byOption = new Option<string>("--by") { Description = "Deciding principal id (ULID): the contributor of the step's assignment.", Required = true };
        var stepOption = new Option<string>("--step") { Description = "The decision step: its key, or key[instance].", Required = true };
        var pathOption = new Option<string[]>("--path") { Description = "A path of the step's transition the decision takes; repeat it for several paths at once where the flow allows.", Required = true, AllowMultipleArgumentsPerToken = false };
        var reasonOption = new Option<string>("--reason") { Description = "Why this path, from the evidence named.", Required = true };
        var evidenceOption = new Option<string[]>("--evidence") { Description = "A record the decision rests on, as id or type:id, pinned at its current hash; repeatable.", AllowMultipleArgumentsPerToken = false };
        Arguments.Add(goalArgument);
        Options.Add(byOption);
        Options.Add(stepOption);
        Options.Add(pathOption);
        Options.Add(reasonOption);
        Options.Add(evidenceOption);
        SetAction(parseResult => operations.GoalDecide(parseResult.GetValue(context.EstateOption)!, parseResult.GetValue(goalArgument)!, parseResult.GetValue(byOption)!,
            parseResult.GetValue(stepOption)!, parseResult.GetValue(pathOption) ?? [], parseResult.GetValue(reasonOption)!, parseResult.GetValue(evidenceOption) ?? [], parseResult.GetValue(context.JsonOption)));
    }
}
