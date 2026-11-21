// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.CommandLine;
using Vericairn.Protocol;

namespace Vericairn.Cli;

internal sealed class WorkNewCommand : Command
{
    public WorkNewCommand(CommandContext context)
        : base("new", "Create a work order from flags, or from 0.9.4 from a possibility's draft, validate the estate with it, and sign as the author.")
    {
        var byOption = new Option<string>("--by") { Description = "Authoring principal id (ULID).", Required = true };
        // Body requirements belong to the work-order validator, so refusals match records new work-order.
        var keyOption = new Option<string?>("--key") { Description = "Required short upper-case key, for example W-1." };
        var titleOption = new Option<string?>("--title") { Description = "Required short title." };
        var intentOption = new Option<string?>("--intent") { Description = "Required intent: why this work exists." };
        var scopeOption = new Option<string[]>("--scope") { Description = "What is in scope; repeat for each item. At least one is required." };
        var deliverableOption = new Option<string[]>("--deliverable") { Description = "What will exist when the work is done; repeat for each item. At least one is required." };
        var exitQuestionOption = new Option<string?>("--exit-question") { Description = "Required question whose honest yes closes the work." };
        var kindOption = new Option<string?>("--kind") { Description = "ordinary or milestone.", DefaultValueFactory = _ => "ordinary" };
        var afterOption = new Option<string[]>("--after") { Description = "Work-order record id this work starts after; repeat for each dependency." };
        var inputOption = new Option<string[]>("--input") { Description = "Input reference as type:id; repeat for each input." };
        var possibilityOption = new Option<string?>("--from-possibility") { Description = "From 0.9.4: adopt an open possibility, by any revision's id: its draft work order supplies every field left out, it joins the inputs, and its revision adopting it as the new work order is stored with it. A principal other than its author adopts it, whoever raised it, and no agent tied to its standing agent (named by an assignment of its work or by a revision of its role); a held finding's work order is held from disclosure too." };
        Options.Add(byOption);
        Options.Add(keyOption);
        Options.Add(titleOption);
        Options.Add(intentOption);
        Options.Add(scopeOption);
        Options.Add(deliverableOption);
        Options.Add(exitQuestionOption);
        Options.Add(kindOption);
        Options.Add(afterOption);
        Options.Add(inputOption);
        Options.Add(possibilityOption);
        SetAction(parseResult => context.WithApi(parseResult.GetValue(context.EstateOption)!, api =>
            context.ReportOutcome(api.WorkNew(new WorkNewRequest(
                parseResult.GetValue(byOption)!,
                parseResult.GetValue(keyOption),
                parseResult.GetValue(titleOption),
                parseResult.GetValue(intentOption),
                parseResult.GetValue(scopeOption),
                parseResult.GetValue(exitQuestionOption),
                parseResult.GetValue(deliverableOption),
                parseResult.GetValue(kindOption),
                parseResult.GetValue(afterOption) is { Length: > 0 } after ? after : null,
                parseResult.GetValue(inputOption))
            { FromPossibility = parseResult.GetValue(possibilityOption) }), parseResult.GetValue(context.JsonOption))));
    }
}
