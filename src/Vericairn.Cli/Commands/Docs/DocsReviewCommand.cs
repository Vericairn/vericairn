// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.CommandLine;

namespace Vericairn.Cli;

internal sealed class DocsReviewCommand : Command
{
    public DocsReviewCommand(CommandContext context)
        : base("review", "Record a review of a living document: the runtime reads its content, records the covered paths' identity and the triggers it answers, and runs its declared checks; a failed check refuses the clearance.")
    {
        var operations = new LivingDocumentOperations(context);
        var pathArgument = new Argument<string>("path") { Description = "The document's repository-relative path." };
        var byOption = new Option<string>("--by") { Description = "The reviewing principal (ULID).", Required = true };
        var outcomeOption = new Option<string>("--outcome") { Description = "current (it holds), revised (it was changed for the trigger) or needs-change (it is stale and stays due).", Required = true };
        var summaryOption = new Option<string>("--summary") { Description = "What was checked.", Required = true };
        var findingOption = new Option<string[]>("--finding") { Description = "A finding, severity:summary with severity error, warning or info; repeatable.", AllowMultipleArgumentsPerToken = false };
        var commitOption = new Option<string?>("--commit") { Description = "For a document of another repository, the commit reviewed." };
        var independenceOption = new Option<string?>("--independence") { Description = "For a document of another repository, whose author the estate's records do not show, the reviewer's independence of its content: self (the reviewer wrote or revised it), independent-agent or operator. Default: self for a principal that holds the builder role, else judged from the estate's records. An estate file's revisions decide its reviewer's independence, and a statement that differs is refused." };
        Arguments.Add(pathArgument);
        Options.Add(byOption);
        Options.Add(outcomeOption);
        Options.Add(summaryOption);
        Options.Add(findingOption);
        Options.Add(commitOption);
        Options.Add(independenceOption);
        SetAction(parseResult => operations.DocsReview(parseResult.GetValue(context.EstateOption)!, parseResult.GetValue(pathArgument)!, parseResult.GetValue(byOption)!, parseResult.GetValue(outcomeOption)!, parseResult.GetValue(summaryOption)!, parseResult.GetValue(findingOption) ?? [], parseResult.GetValue(commitOption), parseResult.GetValue(independenceOption), parseResult.GetValue(context.JsonOption)));
    }
}
