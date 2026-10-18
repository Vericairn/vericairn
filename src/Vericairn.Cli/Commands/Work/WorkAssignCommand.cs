// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.CommandLine;

namespace Vericairn.Cli;

internal sealed class WorkAssignCommand : Command
{
    public WorkAssignCommand(CommandContext context)
        : base("assign", "Delegate a work order to a contributor under a brief, pinned to the exact work-order revision.")
    {
        var operations = new WorkOperations(context);
        var keyArgument = new Argument<string>("work-order") { Description = "Work-order key (for example M1-WORK) or record id." };
        var byOption = new Option<string>("--by") { Description = "Authoring principal id (ULID).", Required = true };
        var contributorOption = new Option<string>("--contributor") { Description = "Principal id of the contributor the work is delegated to.", Required = true };
        var briefOption = new Option<string>("--brief") { Description = "Path to a JSON file holding the brief (outcome, sources, permittedChanges, prohibitedEffects, workspace, limits, expectedOutputs, reviewCriteria, recovery), or '-' for standard input.", Required = true };
        var noteOption = new Option<string?>("--note") { Description = "Free text." };
        Arguments.Add(keyArgument);
        Options.Add(contributorOption);
        Options.Add(byOption);
        Options.Add(briefOption);
        Options.Add(noteOption);
        SetAction(parseResult => operations.WorkAssign(
            parseResult.GetValue(context.EstateOption)!,
            parseResult.GetValue(keyArgument)!,
            parseResult.GetValue(contributorOption)!,
            parseResult.GetValue(byOption)!,
            parseResult.GetValue(briefOption)!,
            parseResult.GetValue(noteOption),
            parseResult.GetValue(context.JsonOption)));
    }
}
