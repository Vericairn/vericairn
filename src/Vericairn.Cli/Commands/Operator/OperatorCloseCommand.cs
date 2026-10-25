// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.CommandLine;
using System.Text.Json;
using Vericairn.Protocol;

namespace Vericairn.Cli;

internal sealed class OperatorCloseCommand : Command
{
    public OperatorCloseCommand(CommandContext context)
        : base("close", "List finished, reviewed and unaccepted steps with their exact results, reviews and exit questions. Select some or all to sign their acceptances in one validated action on the operator's admitted device. Changed evidence refuses the whole selection. Not exposed through MCP.")
    {
        var select = new Option<string[]>("--select") { Description = "Work-order key to close; repeat to select more than one.", AllowMultipleArgumentsPerToken = true };
        var all = new Option<bool>("--all") { Description = "Close every displayed candidate; refuses if the complete membership changes." };
        var by = new Option<string?>("--by") { Description = "Operator principal acting on their own admitted device; required with a selection." };
        var from = new Option<string?>("--from") { Description = "Saved operator close --json listing, or '-' for standard input. Reuses its exact evidence pins." };
        Options.Add(select);
        Options.Add(all);
        Options.Add(by);
        Options.Add(from);
        SetAction(parse =>
        {
            string[] keys = parse.GetValue(select) ?? [];
            bool every = parse.GetValue(all);
            bool closing = every || keys.Length > 0;
            string? actor = parse.GetValue(by);
            if (every && keys.Length > 0) return context.Fail("Use either --select or --all.");
            if (keys.Distinct(StringComparer.Ordinal).Count() != keys.Length) return context.Fail("Select each work-order key once.");
            if (closing && string.IsNullOrWhiteSpace(actor)) return context.Fail("A selection requires --by <operator>.");
            if (!closing && parse.GetValue(from) is not null) return context.Fail("Use --from with --select or --all.");
            return context.WithApi(parse.GetValue(context.EstateOption)!, api =>
            {
                if (parse.GetValue(from) is string saved)
                    return context.WithBody(saved, json => Run(json.Deserialize<OperatorCloseCandidatesResponse>(ProtocolJson.Options)
                        ?? throw new JsonException("Supply a saved operator close listing.")));
                return Run(api.OperatorCloseCandidates());

                int Run(OperatorCloseCandidatesResponse candidates)
                {
                    if (!closing)
                    {
                        if (parse.GetValue(context.JsonOption)) WriteJson(candidates);
                        else Show(context.Output, candidates.Items);
                        if (candidates.Problem is not null) context.Error.WriteLine(candidates.Problem.Message);
                        return candidates.Ok ? CommandContext.Clean : CommandContext.HasErrors;
                    }
                    if (!candidates.Ok) return Refused(candidates.Problem!);
                    if (candidates.Items is null || candidates.Items.Any(i => i is null || i.WorkOrder is null || i.Result is null || i.Reviews is null || i.Reviews.Any(r => r is null))
                        || candidates.Items.Select(i => i.Key).Distinct(StringComparer.Ordinal).Count() != candidates.Items.Count)
                        return context.Fail("Supply an intact operator close --json listing.");
                    if (keys.Any(key => !candidates.Items.Any(i => i.Key == key)))
                        return Refused(new("refused", "A selected key is not a finished, reviewed and unaccepted step in this listing; nothing was written.", []));
                    OperatorCloseItem[] chosen = [.. candidates.Items.Where(i => every || keys.Contains(i.Key, StringComparer.Ordinal))];
                    if (parse.GetValue(from) is not null)
                    {
                        OperatorCloseCandidatesResponse current = api.OperatorCloseCandidates();
                        if (!current.Ok) return Refused(current.Problem!);
                        // A saved file supplies pins, not trusted prose. Verify the displayed descriptions as well.
                        foreach (OperatorCloseItem item in chosen)
                        {
                            OperatorCloseItem? actual = current.Items.SingleOrDefault(i => i.Key == item.Key);
                            if (actual is null || JsonSerializer.Serialize(item, ProtocolJson.Options) != JsonSerializer.Serialize(actual, ProtocolJson.Options))
                                return Refused(new("conflict", "The saved selection differs from the current evidence. Read operator close again; nothing was written.", []));
                        }
                    }
                    // Keep machine output one JSON response; the evidence is still shown before signing on standard error.
                    TextWriter display = parse.GetValue(context.JsonOption) ? context.Error : context.Output;
                    Show(display, chosen);
                    display.WriteLine("The signing device checks presence for each acceptance when required.");
                    display.Flush();
                    OperatorCloseResponse response = api.OperatorClose(new(actor!, [.. chosen.Select(i => i.ToSelection())], every));
                    if (parse.GetValue(context.JsonOption)) WriteJson(response);
                    else
                    {
                        foreach (RecordWritten receipt in response.Written) context.Output.WriteLine($"Accepted: {receipt.Id} at {receipt.Hash}");
                        if (response.Problem is not null) context.Error.WriteLine(response.Problem.Message);
                    }
                    return response.Ok ? CommandContext.Clean : CommandContext.HasErrors;
                }

                int Refused(Problem problem)
                {
                    if (parse.GetValue(context.JsonOption)) WriteJson(new OperatorCloseResponse([], problem));
                    else context.Error.WriteLine(problem.Message);
                    return CommandContext.HasErrors;
                }

                void WriteJson<T>(T response) => context.Output.WriteLine(JsonSerializer.Serialize(response, ProtocolJson.Options));
            }, readOnly: !closing);
        });
    }

    private static void Show(TextWriter output, IReadOnlyList<OperatorCloseItem> items)
    {
        if (items.Count == 0) output.WriteLine("No finished, reviewed and unaccepted steps.");
        foreach (OperatorCloseItem item in items)
        {
            output.WriteLine($"{item.Key}  work order {item.WorkOrder.Id} at {item.WorkOrder.Hash}");
            output.WriteLine($"  Exit question: {item.ExitQuestion}");
            output.WriteLine($"  Result {item.Result.Id} at {item.Result.Hash}: {item.Outcome}");
            output.WriteLine($"    {item.Summary}");
            foreach (OperatorCloseReview review in item.Reviews)
            {
                output.WriteLine($"  Passing review {review.Id} at {review.Hash}: {review.Independence}");
                output.WriteLine($"    {review.Summary}");
            }
        }
    }
}
