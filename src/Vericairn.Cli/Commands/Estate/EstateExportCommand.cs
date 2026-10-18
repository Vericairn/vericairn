// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.CommandLine;

namespace Vericairn.Cli;

internal sealed class EstateExportCommand : Command
{
    public EstateExportCommand(CommandContext context)
        : base("export", "Write a verified export: records, objects, manifest and documents with an inventory; no keys or authority.")
    {
        var operations = new EstateOperations(context);
        var outOption = new Option<string>("--out") { Description = "Path of the export file to create (never overwritten).", Required = true };
        var exportByOption = new Option<string?>("--by") { Description = "Principal id recorded as the exporter." };
        Options.Add(outOption);
        Options.Add(exportByOption);
        SetAction(parseResult => operations.Export(parseResult.GetValue(context.EstateOption)!, parseResult.GetValue(outOption)!, parseResult.GetValue(exportByOption), parseResult.GetValue(context.JsonOption)));
    }
}
