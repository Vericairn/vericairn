// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.CommandLine;
using Vericairn.Protocol;

namespace Vericairn.Cli;

internal sealed class RecordsNewCommand : Command
{
    public RecordsNewCommand(CommandContext context)
        : base("new", "Create a record in canonical form, validate the estate with it, and write it.")
    {
        var typeArgument = new Argument<string>("type") { Description = "Record type, for example work-order." };
        var byOption = new Option<string>("--by") { Description = "Authoring principal id (ULID).", Required = true };
        var bodyOption = new Option<string>("--body") { Description = "Path to a JSON file holding the body, or '-' for standard input.", Required = true };
        var subjectOption = new Option<string?>("--subject") { Description = "Subject as type:id or type:id:hash." };
        var supersedesOption = new Option<string?>("--supersedes") { Description = "Id of the record this one replaces." };
        var atOption = new Option<string?>("--at") { Description = "Authoring time, for example 2026-10-13T14:00:00Z (default: now)." };
        var idOption = new Option<string?>("--id") { Description = "Explicit record id; only the root operator principal needs this." };
        var expectedOption = new Option<string?>("--expected") { Description = "Required for policy or placement authoring: expected predecessor or policy hash, or the canonical body hash for a creation." };
        Arguments.Add(typeArgument);
        Options.Add(byOption);
        Options.Add(bodyOption);
        Options.Add(subjectOption);
        Options.Add(supersedesOption);
        Options.Add(atOption);
        Options.Add(idOption);
        Options.Add(expectedOption);
        SetAction(parseResult => Execute(context,
            parseResult.GetValue(context.EstateOption)!,
            parseResult.GetValue(typeArgument)!,
            parseResult.GetValue(byOption)!,
            parseResult.GetValue(bodyOption)!,
            parseResult.GetValue(subjectOption),
            parseResult.GetValue(supersedesOption),
            parseResult.GetValue(atOption),
            parseResult.GetValue(idOption),
            parseResult.GetValue(expectedOption),
            parseResult.GetValue(context.JsonOption)));
    }

    private static int Execute(CommandContext context, string root, string type, string by, string bodyPath, string? subject, string? supersedes, string? at, string? id, string? expected, bool json) =>
        context.WithBody(bodyPath, body => context.WithApi(root, api => context.ReportOutcome(api.NewRecord(new NewRecordRequest(type, by, body, subject, supersedes, at, id, expected)), json)));
}
