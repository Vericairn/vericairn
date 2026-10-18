// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.CommandLine;

namespace Vericairn.Cli;

internal sealed class ConnectionsCommand : Command
{
    public ConnectionsCommand(CommandContext context)
        : base("connections", "The runtimes this machine reaches over the local network: add one by pairing with a code, list them with their state, and remove one. Kept for this user on this machine, never in a repository.")
    {
        Subcommands.Add(new ConnectionsAddCommand(context));
        Subcommands.Add(new ConnectionsListCommand(context));
        Subcommands.Add(new ConnectionsRemoveCommand(context));
    }
}
