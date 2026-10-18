// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.CommandLine;

namespace Vericairn.Cli;

internal sealed class CheckCommand : Command
{
    public CheckCommand(CommandContext context)
        : base("check", "Run a check and record its evidence as a verification of the exact commit, or observe a workflow run.")
    {
        Subcommands.Add(new CheckRunCommand(context));
        Subcommands.Add(new CheckObserveCommand(context));
    }
}
