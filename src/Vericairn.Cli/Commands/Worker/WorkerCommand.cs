// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.CommandLine;

namespace Vericairn.Cli;

internal sealed class WorkerCommand : Command
{
    public WorkerCommand(CommandContext context)
        : base("worker", "The worker (vericairn.worker/1): a coding agent carries one assignment toward its exit question through a launcher, in a workspace on a work branch, with its cap from its goal's budget recorded before it starts and its observed usage after. It accepts nothing; the agent's result is a claim another principal reviews.")
    {
        Subcommands.Add(new WorkerLaunchersCommand(context));
        Subcommands.Add(new WorkerConfigureCommand(context));
        Subcommands.Add(new WorkerQualifyCommand(context));
        Subcommands.Add(new WorkerRunCommand(context));
        Subcommands.Add(new WorkerStopCommand(context));
        Subcommands.Add(new WorkerRunsCommand(context));
    }
}
