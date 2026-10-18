// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.CommandLine;

namespace Vericairn.Cli;

internal sealed class RecordsDescribeCommand : Command
{
    public RecordsDescribeCommand(CommandContext context)
        : base("describe", "Explain record types and their fields.")
    {
        var describeTypeArgument = new Argument<string?>("type") { Description = "Record type to describe; omit to list all types.", Arity = ArgumentArity.ZeroOrOne };
        Arguments.Add(describeTypeArgument);
        SetAction(result => RecordDescriptions.Show(context, result.GetValue(describeTypeArgument)));
    }
}
