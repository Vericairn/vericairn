// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.CommandLine;
using Vericairn.Runtime.Portability;
using static Vericairn.Cli.CommandContext;

namespace Vericairn.Cli;

internal sealed class RecoverCompareCommand : Command
{
    public RecoverCompareCommand(CommandContext context)
        : base("compare", "Records only in this copy, only in the other, or (never expected) differing in content.")
    {
        var withOption = new Option<string>("--with") { Description = "Root of the other copy.", Required = true };
        Options.Add(withOption);
        SetAction(parseResult => Execute(context, parseResult.GetValue(context.EstateOption)!, parseResult.GetValue(withOption)!));
    }

    private static int Execute(CommandContext context, string root, string other)
    {
        try
        {
            context.Output.Write(Recovery.Compare(CommandContext.LoadCopy(root), "this copy", CommandContext.LoadCopy(other), "the other copy"));
            return Clean;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            return context.Fail(exception.Message);
        }
    }
}
