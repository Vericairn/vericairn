// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.CommandLine;

namespace Vericairn.Cli;

internal sealed class WorkReviewCommand : Command
{
    public WorkReviewCommand(CommandContext context)
        : base("review", "Review an exact result with its evidence read, signed as the acting principal; records no acceptance.")
    {
        var operations = new WorkOperations(context);
        var result = new Argument<string>("result") { Description = "Exact current result id." };
        var by = new Option<string>("--by") { Description = "Reviewing principal id.", Required = true };
        var expected = new Option<string>("--expected") { Description = "Expected current result hash.", Required = true };
        var body = new Option<string>("--body") { Description = "Review JSON with independence, outcome, summary, findings and exact read references; '-' reads standard input.", Required = true };
        Arguments.Add(result);
        Options.Add(by);
        Options.Add(expected);
        Options.Add(body);
        SetAction(parse => operations.WorkReview(parse.GetValue(context.EstateOption)!, parse.GetValue(result)!,
            parse.GetValue(by)!, parse.GetValue(body)!, parse.GetValue(expected)!, parse.GetValue(context.JsonOption)));
    }
}
