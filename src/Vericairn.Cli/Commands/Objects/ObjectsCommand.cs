// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.CommandLine;

namespace Vericairn.Cli;

internal sealed class ObjectsCommand : Command
{
    public ObjectsCommand(CommandContext context)
        : base("objects", "Content-addressed objects: put, list, verify.")
    {
        Subcommands.Add(new ObjectsPutCommand(context));
        Subcommands.Add(new ObjectsListCommand(context));
        Subcommands.Add(new ObjectsVerifyCommand(context));
    }
}
