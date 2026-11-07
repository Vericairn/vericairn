// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.CommandLine;

namespace Vericairn.Cli;

internal sealed class GoalProposeCommand : Command
{
    public GoalProposeCommand(CommandContext context)
        : base("propose", "Propose a goal, or a revision of one: what done looks like, its constraints, its budget and its sponsor. A proposal authorizes and funds nothing. Anyone may propose a goal; a revision is its sponsor's when the sponsor is a person, and an operator's when an agent sponsors it, and anyone else proposes the change as an ask.")
    {
        var operations = new GoalOperations(context);
        var byOption = new Option<string>("--by") { Description = "Acting principal id (ULID).", Required = true };
        var titleOption = new Option<string>("--title") { Description = "Short title.", Required = true };
        var objectiveOption = new Option<string>("--objective") { Description = "What the sponsor wants, in the sponsor's words.", Required = true };
        var doneOption = new Option<string[]>("--done") { Description = "A completion condition as JSON; repeatable. A record predicate, {\"type\": ...} with optional subject, product, fields, state, min and max (or the same inside {\"record\": ...}), or an evidence statement, {\"evidence\": ..., \"check\": ...}.", Required = true };
        var sponsorOption = new Option<string?>("--sponsor") { Description = "The accountable principal; the proposer when omitted. A person who sponsors a goal activates and accepts it; a goal an agent sponsors is activated and accepted by an operator, and only an operator." };
        var delegateOption = new Option<string?>("--delegate") { Description = "A principal the sponsor, a person, delegates the acceptance of an ordinary goal to; it accepts while it holds the 'accept-work' scope, and never a goal that includes a reserved action." };
        var constraintOption = new Option<string[]>("--constraint") { Description = "What must not change or happen; repeatable." };
        var budgetOption = new Option<string[]>("--budget") { Description = "A cap its activation grants, unit=value; repeatable: wallMinutes and attemptsPerStep (both required), tokens, concurrency, or currency=<amount in minor units>:<unit> such as currency=5000:USD. A unit not given is not granted." };
        var rosterOption = new Option<string?>("--roster") { Description = "The roster record whose roles it uses; the estate's current roster when omitted." };
        var kindOption = new Option<string>("--kind") { Description = "finite (ends when done) or stewardship (holds a standing condition).", DefaultValueFactory = _ => "finite" };
        var workOrderOption = new Option<string[]>("--work-order") { Description = "A work order the sponsor gives as its plan; repeatable. Without one a planner proposes its work, as work orders that name the goal among their inputs." };
        var inputOption = new Option<string[]>("--input") { Description = "A record it builds on, as type:id; repeatable." };
        var deadlineOption = new Option<string?>("--deadline") { Description = "The goal's deadline, for example 2026-12-01T00:00:00Z; validated and recorded but not yet enforced." };
        var flowOption = new Option<string?>("--flow") { Description = "A flow to run, as id@version, a template of kind flow (vericairn templates list): the goal is a run of it, whose plan gives exactly one --work-order and whose every work and review step is an assignment of it naming the step." };
        var subjectOption = new Option<string[]>("--subject") { Description = "With --flow, a field of the flow's subject as name=value: a record field by its id (or type:id), a list field as comma-separated item names, a text field as its text; repeatable." };
        var flowOptionOption = new Option<string[]>("--flow-option") { Description = "With --flow, the goal's own configuration as name=value: a flow option, or step.<key>=on|off for an optional step, narrower than the estate's and the product's template-defaults; repeatable. No configuration turns off a required step, a reserved marking or a step an estate policy requires." };
        var untilOption = new Option<string?>("--until") { Description = "With --flow, the step the run ends at; the steps after it are not part of the run." };
        var supersedesOption = new Option<string?>("--supersedes") { Description = "The goal this proposal revises, by its current revision's id; a revision needs a fresh activation, and only its sponsor, a person, or an operator for a goal an agent sponsors records it." };
        Options.Add(byOption);
        Options.Add(titleOption);
        Options.Add(objectiveOption);
        Options.Add(doneOption);
        Options.Add(sponsorOption);
        Options.Add(delegateOption);
        Options.Add(constraintOption);
        Options.Add(budgetOption);
        Options.Add(rosterOption);
        Options.Add(kindOption);
        Options.Add(workOrderOption);
        Options.Add(inputOption);
        Options.Add(deadlineOption);
        Options.Add(flowOption);
        Options.Add(subjectOption);
        Options.Add(flowOptionOption);
        Options.Add(untilOption);
        Options.Add(supersedesOption);
        SetAction(parseResult => operations.GoalPropose(
            parseResult.GetValue(context.EstateOption)!,
            new GoalProposal(
                parseResult.GetValue(byOption)!,
                parseResult.GetValue(titleOption)!,
                parseResult.GetValue(objectiveOption)!,
                parseResult.GetValue(doneOption) ?? [],
                parseResult.GetValue(sponsorOption),
                parseResult.GetValue(delegateOption),
                parseResult.GetValue(constraintOption) ?? [],
                parseResult.GetValue(budgetOption) ?? [],
                parseResult.GetValue(rosterOption),
                parseResult.GetValue(kindOption)!,
                parseResult.GetValue(workOrderOption) ?? [],
                parseResult.GetValue(inputOption) ?? [],
                parseResult.GetValue(deadlineOption),
                parseResult.GetValue(supersedesOption),
                parseResult.GetValue(flowOption),
                parseResult.GetValue(subjectOption) ?? [],
                parseResult.GetValue(flowOptionOption) ?? [],
                parseResult.GetValue(untilOption)),
            parseResult.GetValue(context.JsonOption)));
    }
}
