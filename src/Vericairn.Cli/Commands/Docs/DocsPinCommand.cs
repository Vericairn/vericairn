// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.CommandLine;
using Vericairn.Protocol;

namespace Vericairn.Cli;

internal sealed class DocsPinCommand : Command
{
    public DocsPinCommand(CommandContext context)
        : base("pin", "Record a document's next revision. Converted documents regenerate their revision history and hash authored content; existing whole-file pins keep their mode. --convert-revisions converts the opening history; --render-template also permits a refresh with unchanged authored bytes.")
    {
        var path = new Argument<string>("path") { Description = "Normalized repository-relative file path under design/ or docs/; --render-template also admits a root README.md instance." };
        var by = new Option<string>("--by") { Description = "Authoring principal id (ULID).", Required = true };
        var summary = new Option<string>("--summary") { Description = "What this revision is or changes.", Required = true };
        var title = new Option<string?>("--title") { Description = "Document title. Defaults to the current pin's title, or the file name without its extension for a first pin." };
        var expected = new Option<string?>("--expected") { Description = "Expected current pin record id (ULID). A stale or forked pin is refused without recording." };
        var render = new Option<bool>("--render-template") { Description = "Refresh existing template regions or a converted revision history, including when authored bytes are unchanged, in one recoverable batch." };
        var convert = new Option<bool>("--convert-revisions") { Description = "Convert the opening hand-written revision history to a generated region, requiring a record for every existing line." };
        var decision = new Option<string?>("--decision") { Description = "Exact decision record carried by this revision." };
        Arguments.Add(path);
        Options.Add(by);
        Options.Add(summary);
        Options.Add(title);
        Options.Add(expected);
        Options.Add(render);
        Options.Add(convert);
        Options.Add(decision);
        SetAction(parse => context.WithApi(parse.GetValue(context.EstateOption)!, api =>
        {
            string document = parse.GetValue(path)!;
            DocumentPinResponse response = api.DocumentPin(new(document, parse.GetValue(by)!, parse.GetValue(summary)!, parse.GetValue(title), parse.GetValue(expected))
            { RenderTemplate = parse.GetValue(render), ConvertRevisions = parse.GetValue(convert), Decision = parse.GetValue(decision) });
            bool json = parse.GetValue(context.JsonOption);
            if (!json && response.Written is not null) context.Output.WriteLine($"{document}  revision {response.Revision}");
            return context.ReportOutcome(new WriteOutcome(response.Written, response.Problem), json);
        }));
    }
}
