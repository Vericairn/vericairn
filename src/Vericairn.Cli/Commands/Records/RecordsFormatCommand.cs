// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.CommandLine;

namespace Vericairn.Cli;

internal sealed class RecordsFormatCommand : Command
{
    public RecordsFormatCommand(CommandContext context)
        : base("format", "Rewrite record files in canonical pretty form without changing their content or hash.")
    {
        var filesArgument = new Argument<string[]>("files") { Description = "Record files to rewrite in canonical pretty form.", Arity = ArgumentArity.OneOrMore };
        Arguments.Add(filesArgument);
        SetAction(result => RecordFiles.Format(context, result.GetValue(filesArgument)!));
    }
}
