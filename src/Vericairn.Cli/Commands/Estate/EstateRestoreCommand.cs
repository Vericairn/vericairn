// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.CommandLine;

namespace Vericairn.Cli;

internal sealed class EstateRestoreCommand : Command
{
    public EstateRestoreCommand(CommandContext context)
        : base("restore", "Verify an export against its inventory and restore it into an empty directory as a repository-synced estate; authority is not restored.")
    {
        var operations = new EstateOperations(context);
        var fromOption = new Option<string>("--from") { Description = "Export file.", Required = true };
        var toOption = new Option<string>("--to") { Description = "Empty destination directory.", Required = true };
        Options.Add(fromOption);
        Options.Add(toOption);
        SetAction(parseResult => operations.Restore(parseResult.GetValue(fromOption)!, parseResult.GetValue(toOption)!, parseResult.GetValue(context.JsonOption)));
    }
}
