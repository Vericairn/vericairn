// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.CommandLine;

namespace Vericairn.Cli;

internal sealed class DocsLivingCommand : Command
{
    public DocsLivingCommand(CommandContext context)
        : base("living", "List the declared living documents: each one current, due with every trigger newer than its latest clearance, or unavailable.")
    {
        var operations = new LivingDocumentOperations(context);
        SetAction(parseResult => operations.DocsLiving(parseResult.GetValue(context.EstateOption)!, parseResult.GetValue(context.JsonOption)));
    }
}
