// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.CommandLine;

namespace Vericairn.Cli;

internal sealed class RecordsCommand : Command
{
    public RecordsCommand(CommandContext context)
        : base("records", "Create, validate, describe and format records.")
    {
        Subcommands.Add(new RecordsValidateCommand(context));
        Subcommands.Add(new RecordsNewCommand(context));
        Subcommands.Add(new RecordsShowCommand(context));
        Subcommands.Add(new RecordsDescribeCommand(context));
        Subcommands.Add(new RecordsFormatCommand(context));
        Subcommands.Add(new RecordsHashCommand(context));
    }
}
