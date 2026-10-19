// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.CommandLine;

namespace Vericairn.Cli;

internal sealed class DocsRegenerateCommand : Command
{
    public DocsRegenerateCommand(CommandContext context)
        : base("regenerate", "Rewrite an estate document's generated regions, or a generated document, from their generators; nothing outside the markers changes.")
    {
        var operations = new LivingDocumentOperations(context);
        var companions = new CompanionOperations(context);
        var pathArgument = new Argument<string>("path") { Description = "The document's repository-relative path." };
        var compare = new Option<bool>("--compare") { Description = "Compare fresh bytes and complete membership without writing output, pins or review." };
        var commit = new Option<string?>("--commit") { Description = "Exact external source commit for --compare; repair remains local." };
        Arguments.Add(pathArgument);
        Options.Add(compare);
        Options.Add(commit);
        SetAction(p => p.GetValue(compare)
            ? companions.Compare(p.GetValue(context.EstateOption)!, p.GetValue(pathArgument)!, p.GetValue(commit), p.GetValue(context.JsonOption))
            : p.GetValue(commit) is not null ? context.Fail("--commit requires --compare.")
            : operations.DocsRegenerate(p.GetValue(context.EstateOption)!, p.GetValue(pathArgument)!, p.GetValue(context.JsonOption)));
    }
}
