// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.CommandLine;

namespace Vericairn.Cli;

internal sealed class EstateCacheRebuildCommand : Command
{
    public EstateCacheRebuildCommand(CommandContext context)
        : base("rebuild", "Rebuild this clone's derived cache of a repository-synced estate from the repository's canonical record files, keeping the failed live.db and its -wal and -shm files for diagnosis. Refused for a local-only estate, whose database is canonical, and while a daemon or another command has the estate open.")
    {
        var operations = new EstateOperations(context);
        SetAction(parseResult => operations.CacheRebuild(parseResult.GetValue(context.EstateOption)!, parseResult.GetValue(context.JsonOption)));
    }
}
