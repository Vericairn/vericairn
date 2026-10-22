// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.CommandLine;

namespace Vericairn.Cli;

internal sealed class WorkCommand : Command
{
    public WorkCommand(CommandContext context)
        : base("work", "Create work orders; read briefs and record assignments, attempts, results and amendments.")
    {
        Subcommands.Add(new WorkNewCommand(context));
        Subcommands.Add(new WorkPlaceCommand(context));
        Subcommands.Add(new WorkBriefCommand(context));
        Subcommands.Add(new WorkAssignCommand(context));
        Subcommands.Add(new WorkAttemptCommand(context));
        Subcommands.Add(new WorkResultCommand(context));
        Subcommands.Add(new WorkReviewCommand(context));
        Subcommands.Add(new WorkAmendCommand(context));
        Subcommands.Add(new WorkTakeoverCommand(context));
        Subcommands.Add(new WorkObserveCommand(context));
    }
}
