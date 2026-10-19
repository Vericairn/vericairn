// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.CommandLine;

namespace Vericairn.Cli;

internal sealed class DocsCompanionsCommand : Command
{
    public DocsCompanionsCommand(CommandContext context) : base("companions", "List installed readable coverage, generator contracts, declared destinations and the exact collection snapshot.")
    {
        var operations = new CompanionOperations(context);
        SetAction(p => operations.Coverage(p.GetValue(context.EstateOption)!, p.GetValue(context.JsonOption)));
    }
}
