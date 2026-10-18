// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.CommandLine;

namespace Vericairn.Cli;

internal sealed class ContextCommand : Command
{
    public ContextCommand(CommandContext context)
        : base("context", "Prepare measured context and retrieve pinned originals under current permission.")
    {
        Subcommands.Add(new ContextCatalogueCommand(context));
        Subcommands.Add(new ContextPrepareCommand(context));
        Subcommands.Add(new ContextReadCommand(context));
    }
}
