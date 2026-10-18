// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.CommandLine;

namespace Vericairn.Cli;

internal sealed class ConnectionsListCommand : Command
{
    public ConnectionsListCommand(CommandContext context)
        : base("list", "Check every connection now and show its state: current, partial, denied, cached (with when it was last current) or unavailable.")
    {
        var operations = new ConnectionOperations(context);
        SetAction(parseResult => operations.ConnectionsList(parseResult.GetValue(context.JsonOption)));
    }
}
