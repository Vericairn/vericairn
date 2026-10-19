// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.CommandLine;

namespace Vericairn.Cli;

internal sealed class EstateWritingExceptionsListCommand : Command
{
    public EstateWritingExceptionsListCommand(CommandContext context)
        : base("list", "List writing entries, removal history, counts and locations.")
    {
        var operations = new PolicyOperations(context);
        SetAction(p => operations.Writing(p.GetValue(context.EstateOption)!, null, p.GetValue(context.JsonOption)));
    }
}
