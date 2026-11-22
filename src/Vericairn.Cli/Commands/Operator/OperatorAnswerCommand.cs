// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.CommandLine;
using System.Text.Json;
using Vericairn.Protocol;

namespace Vericairn.Cli;

/// <summary>
/// From 0.9.4 (OPS-16): lists the asks waiting on the operators, each by its exact revision with its recommendation and that recommendation's
/// hash first; with a selection, answers them with their recommendations in one act signed on the operator's own device.
/// </summary>
internal sealed class OperatorAnswerCommand : Command
{
    public OperatorAnswerCommand(CommandContext context)
        : base("answer", "List the open asks waiting on the operators, each by its exact revision with its recommendation and that recommendation's hash first. Select some, or all addressed to you, to answer each with its recommendation in one act signed on your own admitted device: one decision for each ask, naming the act. A changed ask or recommendation refuses the whole act. Not exposed through MCP.")
    {
        var select = new Option<string[]>("--select") { Description = "Ask id to answer with its recommendation; repeat to select more than one.", AllowMultipleArgumentsPerToken = true };
        var all = new Option<bool>("--all") { Description = "Answer every ask waiting on --by with its recommendation; refuses if that membership changes." };
        var by = new Option<string?>("--by") { Description = "Operator the asks are put to, acting on their own admitted device; required with a selection. Narrows the listing to that operator." };
        var stake = new Option<string>("--stake") { Description = "Each decision's stake: low, medium, high or critical.", DefaultValueFactory = _ => "low" };
        stake.AcceptOnlyFromAmong("low", "medium", "high", "critical");
        var from = new Option<string?>("--from") { Description = "Saved operator answer --json listing, or '-' for standard input. Reuses its exact ask and recommendation pins." };
        Options.Add(select);
        Options.Add(all);
        Options.Add(by);
        Options.Add(stake);
        Options.Add(from);
        SetAction(parse =>
        {
            string[] asks = parse.GetValue(select) ?? [];
            bool every = parse.GetValue(all);
            bool answering = every || asks.Length > 0;
            string? actor = parse.GetValue(by);
            if (every && asks.Length > 0) return context.Fail("Use either --select or --all.");
            if (asks.Distinct(StringComparer.Ordinal).Count() != asks.Length) return context.Fail("Select each ask once.");
            if (answering && string.IsNullOrWhiteSpace(actor)) return context.Fail("A selection requires --by <operator>.");
            if (!answering && parse.GetValue(from) is not null) return context.Fail("Use --from with --select or --all.");
            return context.WithApi(parse.GetValue(context.EstateOption)!, api =>
            {
                if (parse.GetValue(from) is string saved)
                    return context.WithBody(saved, json => Run(json.Deserialize<OperatorAnswerCandidatesResponse>(ProtocolJson.Options)
                        ?? throw new JsonException("Supply a saved operator answer listing.")));
                return Run(api.OperatorAnswerCandidates(actor));

                int Run(OperatorAnswerCandidatesResponse candidates)
                {
                    if (!answering)
                    {
                        if (parse.GetValue(context.JsonOption)) WriteJson(candidates);
                        else Show(context.Output, candidates.Items);
                        if (candidates.Problem is not null) CommandContext.WriteRuntime(context.Error, candidates.Problem.Message);
                        return candidates.Ok ? CommandContext.Clean : CommandContext.HasErrors;
                    }

                    if (!candidates.Ok) return Refused(candidates.Problem!, true);
                    if (candidates.Items is null || candidates.Items.Any(i => i?.Ask is null || i.Options is null)
                        || candidates.Items.Select(i => i.Ask.Id).Distinct(StringComparer.Ordinal).Count() != candidates.Items.Count)
                        return context.Fail("Supply an intact operator answer --json listing.");
                    // Only the asks put to the acting operator are theirs to answer.
                    OperatorAnswerItem[] theirs = [.. candidates.Items.Where(i => i.To == actor)];
                    if (asks.Any(id => !theirs.Any(i => i.Ask.Id == id)))
                        return Refused(new("refused", "A selected ask is not waiting on " + actor + " in this listing; nothing was written.", []));
                    OperatorAnswerItem[] chosen = [.. theirs.Where(i => every || asks.Contains(i.Ask.Id, StringComparer.Ordinal))];
                    if (parse.GetValue(from) is not null)
                    {
                        OperatorAnswerCandidatesResponse current = api.OperatorAnswerCandidates(actor);
                        if (!current.Ok) return Refused(current.Problem!, true);
                        // A saved file supplies pins, not trusted prose: the displayed words must be the current ones too.
                        foreach (OperatorAnswerItem item in chosen)
                        {
                            OperatorAnswerItem? actual = current.Items.SingleOrDefault(i => i.Ask.Id == item.Ask.Id);
                            if (actual is null || JsonSerializer.Serialize(item, ProtocolJson.Options) != JsonSerializer.Serialize(actual, ProtocolJson.Options))
                                return Refused(new("conflict", "The saved listing differs from the current asks. Read operator answer again; nothing was written.", []));
                        }
                    }

                    // Keep machine output one JSON response; the act is still shown before signing on standard error.
                    TextWriter display = parse.GetValue(context.JsonOption) ? context.Error : context.Output;
                    display.WriteLine($"One act answers {chosen.Length} {(chosen.Length == 1 ? "ask" : "asks")} with {(chosen.Length == 1 ? "its recommendation" : "their recommendations")} as {actor}, each decision of stake {parse.GetValue(stake)}:");
                    Show(display, chosen);
                    display.Flush();
                    OperatorAnswerResponse response = api.OperatorAnswer(new(actor!, [.. chosen.Select(i => i.ToSelection())], every, parse.GetValue(stake)!));
                    if (parse.GetValue(context.JsonOption)) WriteJson(response);
                    else
                    {
                        if (response.Act is not null) context.Output.WriteLine($"Act {response.Act}: {response.Written.Count} {(response.Written.Count == 1 ? "decision" : "decisions")}");
                        foreach (RecordWritten receipt in response.Written) context.Output.WriteLine($"Decided: {receipt.Id} at {receipt.Hash}");
                        if (response.Problem is not null) CommandContext.WriteRuntime(context.Error, response.Problem.Message);
                    }

                    return response.Ok ? CommandContext.Clean : CommandContext.HasErrors;
                }

                int Refused(Problem problem, bool fromRuntime = false)
                {
                    if (parse.GetValue(context.JsonOption)) WriteJson(new OperatorAnswerResponse(null, [], problem));
                    else if (fromRuntime) CommandContext.WriteRuntime(context.Error, problem.Message);
                    else context.Error.WriteLine(problem.Message);
                    return CommandContext.HasErrors;
                }

                void WriteJson<T>(T response) => context.Output.WriteLine(JsonSerializer.Serialize(response, ProtocolJson.Options));
            }, readOnly: !answering);
        });
    }

    private static void Show(TextWriter output, IReadOnlyList<OperatorAnswerItem> items)
    {
        if (items.Count == 0) output.WriteLine("No open asks wait on an operator.");
        foreach (OperatorAnswerItem item in items)
        {
            output.WriteLine($"Ask {item.Ask.Id} at {item.Ask.Hash}, to {item.To}, {item.State}");
            output.WriteLine($"  Recommendation {item.RecommendationHash}: {item.Recommendation}");
            output.WriteLine($"  Question: {item.Question}");
            output.WriteLine($"  If unanswered: {item.Consequence}");
            if (item.Options.Count > 0) output.WriteLine($"  Options: {string.Join("; ", item.Options)}");
        }
    }
}
