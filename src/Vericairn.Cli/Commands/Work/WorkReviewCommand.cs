// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.CommandLine;

namespace Vericairn.Cli;

internal sealed class WorkReviewCommand : Command
{
    public WorkReviewCommand(CommandContext context)
        : base("review", "Review an exact result with its evidence read, signed as the acting principal; records no acceptance. It always needs --expected and an admitted device whose key this runtime holds. A result with duties is reviewed this way or with records new review --expected; a review of a result without duties can be recorded with records new review, which needs neither.")
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
