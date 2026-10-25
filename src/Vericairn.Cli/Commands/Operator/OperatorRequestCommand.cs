// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.CommandLine;
using System.Text.Json;
using Vericairn.Protocol;

namespace Vericairn.Cli;

internal sealed class OperatorRequestCommand : Command
{
    public OperatorRequestCommand(CommandContext context, bool carry = false)
        : base(carry ? "carry" : "request", carry
            ? "Carry an exact open ask into a durable operator request, retaining its words and original requester. The original stays readable; this does not answer a decision question."
            : "Record an operator request with exact content hashes and a destination. A changed draft supersedes its prior revision. Publication requests record intent and dispatch nothing.")
    {
        var ask = new Argument<string>("ask") { Description = "Exact open ask revision to carry over." };
        var by = new Option<string>("--by") { Required = true, Description = "Authoring principal id." };
        var body = new Option<string>("--body") { Required = true, Description = carry ? "JSON file or '-' supplying kind, destination and content; optionally why. The original ask supplies its wording." : "Complete request body JSON file, or '-' for standard input." };
        var subject = new Option<string>("--subject") { Required = true, Description = "Work order or release as type:id:hash." };
        var expected = new Option<string?>("--expected") { Required = carry, Description = "Expected ask hash for carry-over, or predecessor hash for a revision." };
        var supersedes = new Option<string?>("--supersedes") { Description = "Exact request revision replaced by this draft." };
        if (carry) Arguments.Add(ask);
        else Options.Add(supersedes);
        Options.Add(by);
        Options.Add(body);
        Options.Add(subject);
        Options.Add(expected);
        SetAction(parse => context.WithBody(parse.GetValue(body)!, input => context.WithApi(parse.GetValue(context.EstateOption)!, api =>
            context.ReportOutcome(carry
                ? api.OperatorRequestCarry(parse.GetValue(ask)!, new(parse.GetValue(by)!, parse.GetValue(expected)!, input, parse.GetValue(subject)!))
                : api.OperatorRequestWrite(new(parse.GetValue(by)!, input, parse.GetValue(subject)!, parse.GetValue(supersedes), parse.GetValue(expected))),
                parse.GetValue(context.JsonOption)))));
    }
}

internal sealed class OperatorRequestCloseCommand : Command
{
    public OperatorRequestCloseCommand(CommandContext context, bool dismiss = false)
        : base(dismiss ? "dismiss" : "done", dismiss
            ? "Dismiss one exact operator request with a reason in one signed step, keeping its record."
            : "Record one exact operator request as done in one signed step, keeping its record. Optional link or evidence; a publication requires its observed link. An agent cites the operator's own words.")
    {
        var id = new Argument<string>("request") { Description = "Exact current operator request revision." };
        var by = new Option<string>("--by") { Required = true, Description = "The operator or agent recording the close, using their admitted device." };
        var expected = new Option<string?>("--expected") { Description = "Optional expected hash of this exact request revision." };
        var reason = new Option<string?>("--reason") { Required = dismiss, Description = "Why the operator dismissed this request." };
        var link = new Option<string?>("--link") { Description = "Evidence link or the observed publication destination." };
        var evidence = new Option<string?>("--evidence") { Description = "JSON array file, or '-', of evidence objects with summary and optional hash and location." };
        var op = new Option<string?>("--operator") { Description = "For an agent: the operator whose words are cited." };
        var statement = new Option<string?>("--statement") { Description = "For an agent: the operator's exact words, verbatim." };
        var source = new Option<string?>("--source") { Description = "For an agent: the citation locating those words." };
        var saidAt = new Option<string?>("--said-at") { Description = "For an agent: when the operator spoke, as a UTC record timestamp." };
        Arguments.Add(id);
        foreach (Option option in new Option[] { by, expected, reason, link, evidence, op, statement, source, saidAt }) Options.Add(option);
        SetAction(parse =>
        {
            OperatorRequestWords? words = new[] { parse.GetValue(op), parse.GetValue(statement), parse.GetValue(source), parse.GetValue(saidAt) }.Any(v => v is not null)
                ? new(parse.GetValue(op) ?? "", parse.GetValue(statement) ?? "", parse.GetValue(source) ?? "", parse.GetValue(saidAt) ?? "") : null;
            int Close(JsonElement? proof) => context.WithApi(parse.GetValue(context.EstateOption)!, api =>
            {
                var request = new OperatorRequestCloseRequest(parse.GetValue(by)!, parse.GetValue(expected), parse.GetValue(reason), parse.GetValue(link), proof, words);
                return context.ReportOutcome(dismiss ? api.OperatorRequestDismiss(parse.GetValue(id)!, request) : api.OperatorRequestDone(parse.GetValue(id)!, request), parse.GetValue(context.JsonOption));
            });
            return parse.GetValue(evidence) is string path ? context.WithBody(path, body => Close(body)) : Close(null);
        });
    }
}
