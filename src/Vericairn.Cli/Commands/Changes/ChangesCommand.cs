// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.CommandLine;

namespace Vericairn.Cli;

internal sealed class ChangesCommand : Command
{
    public ChangesCommand(CommandContext context) : base("changes", "Record checked pushes, pull requests, pinned merges and their observations.")
    {
        Subcommands.Add(new ChangesPushCommand(context));
        Subcommands.Add(new ChangesOpenCommand(context));
        Subcommands.Add(new ChangesMergeCommand(context));
        Subcommands.Add(new ChangesObserveCommand(context));
    }
}
