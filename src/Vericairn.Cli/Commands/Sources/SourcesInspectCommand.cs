// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.CommandLine;

namespace Vericairn.Cli;

internal sealed class SourcesInspectCommand : Command
{
    public SourcesInspectCommand(CommandContext context)
        : base("inspect", "Observe a repository read-only: commit, branch, remote, working-tree state, tags, recent history.")
    {
        var operations = new SourceOperations(context);
        var pathArgument = new Argument<string>("path") { Description = "Path of a git working tree on this machine; never recorded." };
        var boundaryOption = new Option<string?>("--boundary") { Description = "Operator-selected local directory that may contain the repository and its Git metadata. Defaults to the repository root." };
        Arguments.Add(pathArgument);
        Options.Add(boundaryOption);
        SetAction(parseResult => operations.SourcesInspect(parseResult.GetValue(pathArgument)!, parseResult.GetValue(boundaryOption), parseResult.GetValue(context.JsonOption)));
    }
}
