// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.CommandLine;

namespace Vericairn.Cli;

internal sealed class SourcesCommand : Command
{
    public SourcesCommand(CommandContext context)
        : base("sources", "Repositories the estate uses: inspect read-only, adopt as a resource, observe again, and register this machine's clone.")
    {
        Subcommands.Add(new SourcesInspectCommand(context));
        Subcommands.Add(new SourcesAdoptCommand(context));
        Subcommands.Add(new SourcesObserveCommand(context));
        Subcommands.Add(new SourcesCloneCommand(context));
    }
}
