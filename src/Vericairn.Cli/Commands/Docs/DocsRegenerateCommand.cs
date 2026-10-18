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
        var pathArgument = new Argument<string>("path") { Description = "The document's repository-relative path." };
        Arguments.Add(pathArgument);
        SetAction(parseResult => operations.DocsRegenerate(parseResult.GetValue(context.EstateOption)!, parseResult.GetValue(pathArgument)!, parseResult.GetValue(context.JsonOption)));
    }
}
