// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.CommandLine;

namespace Vericairn.Cli;

internal sealed class EstateWritingExceptionsShowCommand : Command
{
    public EstateWritingExceptionsShowCommand(CommandContext context)
        : base("show", "Show one writing entry and its history by record id.")
    {
        var id = new Argument<string>("id");
        Arguments.Add(id);
        var operations = new PolicyOperations(context);
        SetAction(p => operations.Writing(p.GetValue(context.EstateOption)!, p.GetValue(id), p.GetValue(context.JsonOption)));
    }
}
