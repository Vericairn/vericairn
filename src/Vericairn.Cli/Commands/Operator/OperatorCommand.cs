// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.CommandLine;

namespace Vericairn.Cli;

internal sealed class OperatorCommand : Command
{
    public OperatorCommand(CommandContext context) : base("operator", "Inspect the actions waiting on the estate's operators.")
    {
        Subcommands.Add(new OperatorListCommand(context));
        Subcommands.Add(new OperatorRecordCommand(context));
        Subcommands.Add(new OperatorCloseCommand(context));
        Subcommands.Add(new OperatorRequestCommand(context));
        Subcommands.Add(new OperatorRequestCommand(context, carry: true));
        Subcommands.Add(new OperatorRequestCloseCommand(context));
        Subcommands.Add(new OperatorRequestCloseCommand(context, dismiss: true));
    }
}
