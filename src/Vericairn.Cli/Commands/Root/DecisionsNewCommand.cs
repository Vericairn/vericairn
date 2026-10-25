// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.CommandLine;
using System.Text.Json;
using Vericairn.Protocol;

namespace Vericairn.Cli;

internal sealed class DecisionsNewCommand : Command
{
    public DecisionsNewCommand(CommandContext context)
        : base("new", "Record an operator's supplied answer with its exact words, answer time and complete changed design document pins.")
    {
        var by = Required("--by", "Recording principal id.");
        var ask = Required("--ask", "Ask record id; its recipient is the deciding operator.");
        var statement = Required("--statement", "The operator's exact words, preserved verbatim.");
        var answeredAt = Required("--answered-at", "When the operator answered, as a UTC timestamp with second precision.");
        var baseCommit = Required("--base", "Full local commit before the design changes; includes changes through the current working tree.");
        var title = Required("--title", "Short decision title.");
        var summary = Required("--summary", "What was decided.");
        var synthesis = Required("--synthesis", "Context, decision, consequences and limits.");
        var effects = new Option<string[]>("--effect") { Description = "Concrete effect; repeat for each. At least one is required." };
        var documents = new Option<string[]>("--document") { Description = "Changed design file path; repeat for each. Must exactly match the change from --base." };
        var affects = new Option<string[]>("--affects") { Description = "Affected work-order id; repeat for each." };
        var notAuthorized = new Option<string[]>("--not-authorized") { Description = "What the answer does not authorize; repeat for each." };
        var stake = new Option<string>("--stake") { Description = "low, medium, high or critical.", DefaultValueFactory = _ => "low" };
        var decidedAt = new Option<string?>("--decided-at") { Description = "Defaults to the answer time; a later time is refused." };
        var expectedAsk = new Option<string?>("--expected-ask") { Description = "Expected hash of the current ask revision." };
        foreach (Option option in new Option[] { by, ask, statement, answeredAt, baseCommit, title, summary, synthesis,
            effects, documents, affects, notAuthorized, stake, decidedAt, expectedAsk }) Options.Add(option);
        SetAction(parse => context.WithApi(parse.GetValue(context.EstateOption)!, api =>
        {
            DecisionNewResponse response = api.DecisionFromAsk(new(parse.GetValue(by)!, parse.GetValue(ask)!, parse.GetValue(statement)!,
                parse.GetValue(answeredAt)!, parse.GetValue(baseCommit)!, parse.GetValue(title)!, parse.GetValue(summary)!, parse.GetValue(synthesis)!,
                parse.GetValue(effects) ?? [], parse.GetValue(documents) ?? [], parse.GetValue(stake)!, parse.GetValue(affects),
                parse.GetValue(notAuthorized), parse.GetValue(decidedAt), parse.GetValue(expectedAsk)));
            if (parse.GetValue(context.JsonOption))
            {
                context.Output.WriteLine(JsonSerializer.Serialize(response, ProtocolJson.Options));
                return response.Ok ? CommandContext.Clean : context.Fail(response.Problem?.Message ?? "The decision batch was not completed.");
            }
            foreach (RecordWritten pin in response.Pins) context.ReportOutcome(new(pin, null), false);
            return context.ReportOutcome(new(response.Written, response.Problem), false);
        }));
    }

    private static Option<string> Required(string name, string description) => new(name) { Description = description, Required = true };
}
