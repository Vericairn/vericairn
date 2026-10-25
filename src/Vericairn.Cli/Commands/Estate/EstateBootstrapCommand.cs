// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.CommandLine;

namespace Vericairn.Cli;

internal sealed class EstateBootstrapCommand : Command
{
    public EstateBootstrapCommand(CommandContext context)
        : base("bootstrap", "Write or refresh this estate's first-party bootstrap scripts from the installed command line.")
    {
        var forceOption = new Option<bool>("--force") { Description = "Replace bootstrap files that differ from the shipped first-party copies." };
        Options.Add(forceOption);
        SetAction(parseResult => new EstateOperations(context).Bootstrap(
            parseResult.GetValue(context.EstateOption)!,
            parseResult.GetValue(forceOption),
            parseResult.GetValue(context.JsonOption)));
    }
}
