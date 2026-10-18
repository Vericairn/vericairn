// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.CommandLine;

namespace Vericairn.Cli;

internal sealed class RecordsHashCommand : Command
{
    public RecordsHashCommand(CommandContext context)
        : base("hash", "Print the computed content hash of a record file.")
    {
        var hashFileArgument = new Argument<string>("file") { Description = "Record file." };
        Arguments.Add(hashFileArgument);
        SetAction(result => RecordFiles.Hash(context, result.GetValue(hashFileArgument)!));
    }
}
