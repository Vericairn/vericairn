// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.CommandLine;

namespace Vericairn.Cli;

internal sealed class SourcesAdoptCommand : Command
{
    public SourcesAdoptCommand(CommandContext context)
        : base("adopt", "Adopt a repository as a resource by observation: a resource record and a first observation; nothing in the repository changes.")
    {
        var operations = new SourceOperations(context);
        var pathArgument = new Argument<string>("path") { Description = "Path of a git working tree on this machine; never recorded." };
        var byOption = new Option<string>("--by") { Description = "Authoring principal id (ULID).", Required = true };
        var nameOption = new Option<string>("--name") { Description = "Display name of the resource.", Required = true };
        var roleOption = new Option<string>("--role") { Description = "What the estate uses the repository for.", Required = true };
        var visibilityOption = new Option<string>("--visibility") { Description = "public, private or n/a.", DefaultValueFactory = _ => "private" };
        var notesOption = new Option<string?>("--notes") { Description = "Free text." };
        Arguments.Add(pathArgument);
        Options.Add(byOption);
        Options.Add(nameOption);
        Options.Add(roleOption);
        Options.Add(visibilityOption);
        Options.Add(notesOption);
        SetAction(parseResult => operations.SourcesAdopt(
            parseResult.GetValue(context.EstateOption)!,
            parseResult.GetValue(pathArgument)!,
            parseResult.GetValue(byOption)!,
            parseResult.GetValue(nameOption)!,
            parseResult.GetValue(roleOption)!,
            parseResult.GetValue(visibilityOption)!,
            parseResult.GetValue(notesOption),
            parseResult.GetValue(context.JsonOption)));
    }
}
