// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.CommandLine;
using Vericairn.Runtime.Portability;
using static Vericairn.Cli.CommandContext;

namespace Vericairn.Cli;

internal sealed class RecoverCommand : Command
{
    public RecoverCommand(CommandContext context)
        : base("recover", "After an interruption or a move: inspect what this copy holds and compare it with another.")
    {
        Subcommands.Add(new RecoverInspectCommand(context));
        Subcommands.Add(new RecoverCompareCommand(context));
    }
}
