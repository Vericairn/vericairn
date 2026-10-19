// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.CommandLine;

namespace Vericairn.Cli;

internal sealed class EstateCommand : Command
{
    public EstateCommand(CommandContext context)
        : base("estate", "Create and check estates.")
    {
        Subcommands.Add(new EstateInitCommand(context));
        Subcommands.Add(new EstateWritingExceptionsCommand(context));
        Subcommands.Add(new EstateCheckCommand(context));
        Subcommands.Add(new EstateExportCommand(context));
        Subcommands.Add(new EstateRestoreCommand(context));
        Subcommands.Add(new EstateUpgradeCommand(context));
        Subcommands.Add(new EstateReconcileCommand(context));
    }
}
