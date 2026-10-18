// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.CommandLine;

namespace Vericairn.Cli;

internal sealed class DocsNotesCommand : Command
{
    public DocsNotesCommand(CommandContext context)
        : base("notes", "Write release notes from the estate's records since the previous release's notes ended, and record their window in the set's manifest.")
    {
        var fromOption = new Option<string>("--from") { Description = "The earlier published release's version; the notes begin where the notes of its documentation set, the directory it names beside --out, ended (at its publication when there is no such set).", Required = true };
        var toOption = new Option<string>("--to") { Description = "The release's version; its publication ends the notes once it is recorded, the second before they are generated until then.", Required = true };
        var notesOutOption = new Option<string>("--out") { Description = "Directory of the documentation set the notes belong to.", Required = true };
        var sinceOption = new Option<string?>("--since") { Description = "The time the notes begin, for example 2026-10-16T04:29:43Z, no later than the previous set's end; widens the window to include records the previous set missed and records the chosen start in the manifest." };
        Options.Add(fromOption);
        Options.Add(toOption);
        Options.Add(notesOutOption);
        Options.Add(sinceOption);
        SetAction(parseResult => Docs.Notes(context, parseResult.GetValue(context.EstateOption)!, parseResult.GetValue(fromOption)!, parseResult.GetValue(toOption)!, parseResult.GetValue(notesOutOption)!, parseResult.GetValue(sinceOption), parseResult.GetValue(context.JsonOption)));
    }
}
