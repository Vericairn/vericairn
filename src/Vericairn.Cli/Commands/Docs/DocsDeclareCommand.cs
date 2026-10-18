// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.CommandLine;

namespace Vericairn.Cli;

internal sealed class DocsDeclareCommand : Command
{
    public DocsDeclareCommand(CommandContext context)
        : base("declare", "Record or revise the declaration of a living document: its triggers and levels, generated regions, checks and who may clear it.")
    {
        var operations = new LivingDocumentOperations(context);
        var bodyOption = new Option<string>("--body") { Description = "Path to a JSON file holding the living-document declaration, or '-' for standard input.", Required = true };
        var supersedesOption = new Option<string?>("--supersedes") { Description = "The declaration this one revises." };
        var declareByOption = new Option<string>("--by") { Description = "Authoring principal id (ULID).", Required = true };
        Options.Add(declareByOption);
        Options.Add(bodyOption);
        Options.Add(supersedesOption);
        SetAction(parseResult => operations.DocsDeclare(parseResult.GetValue(context.EstateOption)!, parseResult.GetValue(declareByOption)!, parseResult.GetValue(bodyOption)!, parseResult.GetValue(supersedesOption), parseResult.GetValue(context.JsonOption)));
    }
}
