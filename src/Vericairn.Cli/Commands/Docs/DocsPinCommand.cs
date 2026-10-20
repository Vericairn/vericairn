// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.CommandLine;
using Vericairn.Protocol;

namespace Vericairn.Cli;

internal sealed class DocsPinCommand : Command
{
    public DocsPinCommand(CommandContext context)
        : base("pin", "Record a document's next revision from its exact estate file bytes, superseding its current pin. Refuses unchanged bytes, generated documents, stale expected pins and forks. Does not edit the document.")
    {
        var path = new Argument<string>("path") { Description = "Normalized repository-relative file path under design/ or docs/." };
        var by = new Option<string>("--by") { Description = "Authoring principal id (ULID).", Required = true };
        var summary = new Option<string>("--summary") { Description = "What this revision is or changes.", Required = true };
        var title = new Option<string?>("--title") { Description = "Document title. Defaults to the current pin's title, or the file name without its extension for a first pin." };
        var expected = new Option<string?>("--expected") { Description = "Expected current pin record id (ULID). A stale or forked pin is refused without recording." };
        Arguments.Add(path);
        Options.Add(by);
        Options.Add(summary);
        Options.Add(title);
        Options.Add(expected);
        SetAction(parse => context.WithApi(parse.GetValue(context.EstateOption)!, api =>
        {
            string document = parse.GetValue(path)!;
            DocumentPinResponse response = api.DocumentPin(new(document, parse.GetValue(by)!, parse.GetValue(summary)!, parse.GetValue(title), parse.GetValue(expected)));
            bool json = parse.GetValue(context.JsonOption);
            if (!json && response.Written is not null) context.Output.WriteLine($"{document}  revision {response.Revision}");
            return context.ReportOutcome(new WriteOutcome(response.Written, response.Problem), json);
        }));
    }
}
