// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.CommandLine;

namespace Vericairn.Cli;

/// <summary>From 0.9.4 (CODE-73): the overlap check's view, and a draft brief's preview.</summary>
internal sealed class WorkOverlapCommand : Command
{
    public WorkOverlapCommand(CommandContext context)
        : base("overlap", "From 0.9.4: list the active assignments by repository and every overlap of their effective permitted paths (permitted patterns less exclusions), with the patterns, witnesses and resolution of each and the concurrent-paths policies that hold it; with --brief, preview a draft brief's overlaps before it is recorded. Reads only; witnesses and integration are read from this machine's registered clones and labeled so.")
    {
        var operations = new WorkOperations(context);
        var briefOption = new Option<string?>("--brief") { Description = "Path to a JSON file holding a draft brief, as work assign takes it, or '-' for standard input: its overlaps with the active assignments are previewed, and nothing is recorded." };
        var repositoryOption = new Option<string?>("--repository") { Description = "A repository resource's id or estate: only that repository's assignments and overlaps, or the repository a draft brief would change when it names none." };
        Options.Add(briefOption);
        Options.Add(repositoryOption);
        SetAction(parseResult => operations.WorkOverlap(parseResult.GetValue(context.EstateOption)!, parseResult.GetValue(briefOption), parseResult.GetValue(repositoryOption), parseResult.GetValue(context.JsonOption)));
    }
}
