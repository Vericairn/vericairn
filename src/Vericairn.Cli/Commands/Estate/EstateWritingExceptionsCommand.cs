// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.CommandLine;

namespace Vericairn.Cli;

internal sealed class EstateWritingExceptionsCommand : Command
{
    public EstateWritingExceptionsCommand(CommandContext context)
        : base("writing-exceptions", "Inspect the signed writing registry and every current match.")
    {
        Subcommands.Add(new EstateWritingExceptionsAddCommand(context));
        Subcommands.Add(new EstateWritingExceptionsRemoveCommand(context));
        Subcommands.Add(new EstateWritingExceptionsListCommand(context));
        Subcommands.Add(new EstateWritingExceptionsShowCommand(context));
    }
}
