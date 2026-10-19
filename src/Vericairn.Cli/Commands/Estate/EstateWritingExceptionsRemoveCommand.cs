// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.CommandLine;

namespace Vericairn.Cli;

internal sealed class EstateWritingExceptionsRemoveCommand : Command
{
    public EstateWritingExceptionsRemoveCommand(CommandContext context) : base("remove", "Sign a terminal removal against the expected entry hash.")
    {
        var id = new Argument<string>("id");
        var by = new Option<string>("--by") { Required = true, Description = "Acting principal id." };
        var expected = new Option<string>("--expected") { Required = true, Description = "Expected current entry hash." };
        var reason = new Option<string>("--reason") { Required = true, Description = "Why the entry is removed." };
        var basis = new Option<string?>("--basis") { Description = "Optional JSON authority basis file, or '-' for standard input." };
        Arguments.Add(id);
        Options.Add(by);
        Options.Add(expected);
        Options.Add(reason);
        Options.Add(basis);
        var operations = new PolicyWriteOperations(context);
        SetAction(p => operations.WritingRemove(p.GetValue(context.EstateOption)!, p.GetValue(id)!, p.GetValue(by)!, p.GetValue(expected)!, p.GetValue(reason)!, p.GetValue(basis), p.GetValue(context.JsonOption)));
    }
}
