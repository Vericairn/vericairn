// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.CommandLine;

namespace Vericairn.Cli;

internal sealed class SourcesObserveCommand : Command
{
    public SourcesObserveCommand(CommandContext context)
        : base("observe", "Record a fresh observation of an adopted repository.")
    {
        var operations = new SourceOperations(context);
        var pathArgument = new Argument<string>("path") { Description = "Path of a git working tree on this machine; never recorded." };
        var byOption = new Option<string>("--by") { Description = "Authoring principal id (ULID).", Required = true };
        var resourceOption = new Option<string>("--resource") { Description = "Resource record id of the adopted repository.", Required = true };
        Arguments.Add(pathArgument);
        Options.Add(resourceOption);
        Options.Add(byOption);
        SetAction(parseResult => operations.SourcesObserve(parseResult.GetValue(context.EstateOption)!, parseResult.GetValue(pathArgument)!, parseResult.GetValue(resourceOption)!, parseResult.GetValue(byOption)!, parseResult.GetValue(context.JsonOption)));
    }
}
