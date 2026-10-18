// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.CommandLine;

namespace Vericairn.Cli;

internal sealed class ConnectionsRemoveCommand : Command
{
    public ConnectionsRemoveCommand(CommandContext context)
        : base("remove", "Forget a connection on this machine. The device stays admitted until an administrator revokes it, since disconnection is never revocation, and its key stays in this machine's custody.")
    {
        var operations = new ConnectionOperations(context);
        var idArgument = new Argument<string>("id") { Description = "The connection's id, as listed." };
        Arguments.Add(idArgument);
        SetAction(parseResult => operations.ConnectionsRemove(parseResult.GetValue(idArgument)!, parseResult.GetValue(context.JsonOption)));
    }
}
