// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.CommandLine;
using System.Globalization;
using System.Text.Json;
using Vericairn.Protocol;
using Vericairn.Records.Model;

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
        var saidAt = new Option<string?>("--said-at") { Description = "For an agent: when the operator spoke, with Z or an explicit offset (2026-11-24T10:00:00Z, 2026-11-24T04:00:00-06:00), written in UTC in whole seconds. Without it, the time of this recording." };
        Arguments.Add(id);
        foreach (Option option in new Option[] { by, expected, reason, link, evidence, op, statement, source, saidAt }) Options.Add(option);
        SetAction(parse =>
        {
            // A time that names no instant is refused here, before the estate is opened or anything is signed.
            if (SaidAt(parse.GetValue(saidAt), DateTimeOffset.UtcNow) is not string at) return context.Fail(SaidAtRefusal);
            OperatorRequestWords? words = new[] { parse.GetValue(op), parse.GetValue(statement), parse.GetValue(source), parse.GetValue(saidAt) }.Any(v => v is not null)
                ? new(parse.GetValue(op) ?? "", parse.GetValue(statement) ?? "", parse.GetValue(source) ?? "", at) : null;
            int Close(JsonElement? proof) => context.WithApi(parse.GetValue(context.EstateOption)!, api =>
            {
                var request = new OperatorRequestCloseRequest(parse.GetValue(by)!, parse.GetValue(expected), parse.GetValue(reason), parse.GetValue(link), proof, words);
                return context.ReportOutcome(dismiss ? api.OperatorRequestDismiss(parse.GetValue(id)!, request) : api.OperatorRequestDone(parse.GetValue(id)!, request), parse.GetValue(context.JsonOption));
            });
            return parse.GetValue(evidence) is string path ? context.WithBody(path, body => Close(body)) : Close(null);
        });
    }

    /// <summary>
    /// From 0.9.5 (FIX-171): the refusal of a <c>--said-at</c> without <c>Z</c> or an explicit offset, which names the form it takes.
    /// </summary>
    internal const string SaidAtRefusal = "--said-at takes the time the operator spoke with Z or an explicit offset, such as 2026-11-24T10:00:00Z or "
        + "2026-11-24T04:00:00-06:00, and it is written in UTC in whole seconds; a time without either names no instant. Nothing was signed or written.";

    /// <summary>
    /// From 0.9.5 (FIX-171): when the operator's cited words were said, in the record's timestamp form, a UTC time in whole seconds. A
    /// given time must carry <c>Z</c> or an explicit offset, and is written as the same instant in that form, without its fractional
    /// seconds; any other text, a time without an offset included, is null, which the command refuses (<see cref="SaidAtRefusal"/>)
    /// before anything is signed, since a local wall time read as UTC would be hours off. Without a given time, the time of this
    /// recording, which follows the request and precedes the record's own time, as the record requires. Up to 0.9.4 the command passed
    /// <c>--said-at</c> on as given and wrote an empty time without it, and the record refused any time but a whole-second UTC one.
    /// </summary>
    internal static string? SaidAt(string? given, DateTimeOffset now)
    {
        if (given is null) return RecordParser.FormatTimestamp(now);
        return DateTimeOffset.TryParseExact(given.Trim(), SaidAtForms, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out DateTimeOffset at)
            ? RecordParser.FormatTimestamp(at) : null;
    }

    // Each form names its offset: a literal Z, read as UTC, or zzz, an offset such as +02:00. None accepts a time without one.
    private static readonly string[] SaidAtForms =
        ["yyyy-MM-dd'T'HH:mm:ss'Z'", "yyyy-MM-dd'T'HH:mm:ss.FFFFFFF'Z'", "yyyy-MM-dd'T'HH:mm:sszzz", "yyyy-MM-dd'T'HH:mm:ss.FFFFFFFzzz"];
}
