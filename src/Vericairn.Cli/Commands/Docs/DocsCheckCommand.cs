// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.CommandLine;

namespace Vericairn.Cli;

internal sealed class DocsCheckCommand : Command
{
    public DocsCheckCommand(CommandContext context)
        : base("check", "Refuse a documentation set that lacks a document, or a heading for a command, an API operation or a record type its manifest lists; every gap is listed at once.")
    {
        var directoryArgument = new Argument<string>("directory") { Description = "The documentation set to check." };
        var documentsOption = new Option<string?>("--documents") { Description = "The product's declaration of the documents every set holds (a JSON array of file names); the set's manifest is checked against it." };
        Arguments.Add(directoryArgument);
        Options.Add(documentsOption);
        SetAction(parseResult => Docs.Check(context, parseResult.GetValue(directoryArgument)!, parseResult.GetValue(documentsOption), parseResult.GetValue(context.JsonOption)));
    }
}
