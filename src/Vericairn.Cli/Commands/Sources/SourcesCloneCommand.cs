// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.CommandLine;

namespace Vericairn.Cli;

internal sealed class SourcesCloneCommand : Command
{
    public SourcesCloneCommand(CommandContext context)
        : base("clone", "Register this machine's clone of an adopted repository, so that living documents in it can be read at a commit; the path is machine configuration, never a record.")
    {
        var operations = new SourceOperations(context);
        var pathArgument = new Argument<string>("path") { Description = "Path of a git working tree on this machine; never recorded." };
        var resourceOption = new Option<string>("--resource") { Description = "Resource record id of the adopted repository.", Required = true };
        Arguments.Add(pathArgument);
        Options.Add(resourceOption);
        SetAction(parseResult => operations.SourcesClone(parseResult.GetValue(context.EstateOption)!, parseResult.GetValue(resourceOption)!, parseResult.GetValue(pathArgument)!, parseResult.GetValue(context.JsonOption)));
    }
}
