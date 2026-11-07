// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.CommandLine;

namespace Vericairn.Cli;

internal sealed class EstateCacheCommand : Command
{
    public EstateCacheCommand(CommandContext context)
        : base("cache", "Recover this machine's derived cache of a repository-synced estate.")
    {
        Subcommands.Add(new EstateCacheRebuildCommand(context));
    }
}
