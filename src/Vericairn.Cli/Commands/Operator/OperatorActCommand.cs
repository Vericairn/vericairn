// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.CommandLine;
using System.Text.Json;
using Vericairn.Protocol;

namespace Vericairn.Cli;

/// <summary>
/// From 0.9.4 (OPS-24): vericairn operator act --ready lists every operator item whose command is ready, each with what its signature
/// records; with a choice, it signs the chosen items in one reviewed act on the operator's own device, leaving the others waiting or
/// dismissing a request with a reason.
/// </summary>
internal sealed class OperatorActCommand : Command
{
    public OperatorActCommand(CommandContext context)
        : base("act", "With --ready, list every operator item whose command is ready (acceptances of reviewed results, answers with their recommendations, a goal's, a release candidate's or a milestone's acceptance, and request closes), each with what its signature records. Choose with --all or --select, leave items waiting with --skip and dismiss a request with --dismiss; the rest sign in one reviewed act on your own admitted device, under one presence check where presence sessions apply. A changed item refuses the whole act. Not exposed through MCP.")
    {
        var ready = new Option<bool>("--ready") { Description = "List, or act on, every operator item whose command is ready. Required." };
        var all = new Option<bool>("--all") { Description = "Sign every listed item except those skipped or dismissed; refuses if the list changes." };
        var select = new Option<string[]>("--select") { Description = "Item id to sign; repeat to select more than one. Unselected items wait.", AllowMultipleArgumentsPerToken = true };
        var skip = new Option<string[]>("--skip") { Description = "With --all, item id to leave waiting; repeatable.", AllowMultipleArgumentsPerToken = true };
        var dismiss = new Option<string[]>("--dismiss") { Description = "A request to dismiss, as <item id>=<your reason>; repeatable. Only a request is dismissed." };
        var by = new Option<string?>("--by") { Description = "Operator acting on their own admitted device; required to act. The listing is theirs; without it, the root operator's." };
        var stake = new Option<string>("--stake") { Description = "Each answer's decision stake: low, medium, high or critical.", DefaultValueFactory = _ => "low" };
        stake.AcceptOnlyFromAmong("low", "medium", "high", "critical");
        var from = new Option<string?>("--from") { Description = "Saved operator act --ready --json listing, or '-' for standard input. Reuses its exact pins." };
        foreach (Option option in new Option[] { ready, all, select, skip, dismiss, by, stake, from }) Options.Add(option);
        SetAction(parse =>
        {
            if (!parse.GetValue(ready)) return context.Fail("Use --ready: the act lists, and signs, the operator items whose command is ready.");
            bool every = parse.GetValue(all);
            string[] selected = parse.GetValue(select) ?? [];
            string[] skipped = parse.GetValue(skip) ?? [];
            string?[] dismissedRaw = parse.GetValue(dismiss) ?? [];
            var dismissed = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (string? raw in dismissedRaw)
            {
                int equals = raw?.IndexOf('=', StringComparison.Ordinal) ?? -1;
                if (raw is null || equals <= 0 || string.IsNullOrWhiteSpace(raw[(equals + 1)..])) return context.Fail("Dismiss a request as --dismiss <item id>=<your reason>.");
                if (!dismissed.TryAdd(raw[..equals], raw[(equals + 1)..])) return context.Fail("Dismiss each item once.");
            }

            bool acting = every || selected.Length > 0 || dismissed.Count > 0;
            string? actor = parse.GetValue(by);
            if (every && selected.Length > 0) return context.Fail("Use either --select or --all.");
            if (skipped.Length > 0 && !every) return context.Fail("--skip leaves items waiting from --all; without --all, an unselected item waits.");
            string[] named = [.. selected, .. skipped, .. dismissed.Keys];
            if (named.Distinct(StringComparer.Ordinal).Count() != named.Length) return context.Fail("Name each item once, in one of --select, --skip or --dismiss.");
            if (acting && string.IsNullOrWhiteSpace(actor)) return context.Fail("An act requires --by <operator>.");
            if (!acting && parse.GetValue(from) is not null) return context.Fail("Use --from with a choice.");
            return context.WithApi(parse.GetValue(context.EstateOption)!, api =>
            {
                if (parse.GetValue(from) is string saved)
                    return context.WithBody(saved, json => Run(json.Deserialize<OperatorActListResponse>(ProtocolJson.Options)
                        ?? throw new JsonException("Supply a saved operator act --ready listing.")));
                return Run(api.OperatorActItems(actor));

                int Run(OperatorActListResponse listing)
                {
                    if (!acting)
                    {
                        if (parse.GetValue(context.JsonOption)) WriteJson(listing);
                        else Show(context.Output, listing, null);
                        if (listing.Problem is not null) CommandContext.WriteRuntime(context.Error, listing.Problem.Message);
                        return listing.Ok ? CommandContext.Clean : CommandContext.HasErrors;
                    }

                    if (!listing.Ok) return Refused(listing.Problem!, true);
                    if (listing.Items is null || listing.Items.Any(i => i?.Subject is null || i.Basis is null || i.Basis.Any(b => b is null))
                        || listing.Items.Select(i => i.Id).Distinct(StringComparer.Ordinal).Count() != listing.Items.Count || listing.By != actor)
                        return context.Fail("Supply an intact operator act --ready --json listing of --by's items.");
                    if (named.FirstOrDefault(id => !listing.Items.Any(i => i.Id == id)) is string unknown)
                        return Refused(new("refused", $"Item {unknown} is not in this listing of ready items; nothing was written.", []));
                    if (dismissed.Keys.FirstOrDefault(id => !listing.Items.Single(i => i.Id == id).Dismissable) is string notRequest)
                        return Refused(new("refused", $"Item {notRequest} is not a request; only a request is dismissed, and any other item is left waiting. Nothing was written.", []));
                    OperatorActChoice[] choices = [.. listing.Items.Select(i => dismissed.TryGetValue(i.Id, out string? reason) ? i.Choose("dismiss", reason)
                        : (every ? !skipped.Contains(i.Id, StringComparer.Ordinal) : selected.Contains(i.Id, StringComparer.Ordinal)) ? i.Choose() : i.Choose("wait"))
                        .Where(c => every || c.Choice != "wait")];
                    if (parse.GetValue(from) is not null)
                    {
                        OperatorActListResponse current = api.OperatorActItems(actor);
                        if (!current.Ok) return Refused(current.Problem!, true);
                        // A saved file supplies pins, not trusted prose: what each signature records must be the current words too.
                        foreach (OperatorActItem item in listing.Items.Where(i => choices.Any(c => c.Item == i.Id)))
                        {
                            OperatorActItem? actual = current.Items.SingleOrDefault(i => i.Id == item.Id);
                            if (actual is null || JsonSerializer.Serialize(item, ProtocolJson.Options) != JsonSerializer.Serialize(actual, ProtocolJson.Options))
                                return Refused(new("conflict", "The saved listing differs from the current ready items. Read operator act --ready again; nothing was written.", []));
                        }
                    }

                    // Keep machine output one JSON response; the reviewed act is still shown before signing on standard error.
                    TextWriter display = parse.GetValue(context.JsonOption) ? context.Error : context.Output;
                    Show(display, listing, choices);
                    display.Flush();
                    OperatorActResponse response = api.OperatorAct(new(actor!, choices, every, parse.GetValue(stake)!));
                    if (parse.GetValue(context.JsonOption)) WriteJson(response);
                    else
                    {
                        if (response.Act is not null) context.Output.WriteLine($"Act {response.Act}: {response.Written.Count} {(response.Written.Count == 1 ? "record" : "records")}");
                        foreach (OperatorActReceipt receipt in response.Written)
                            context.Output.WriteLine($"{(receipt.Choice == "dismiss" ? "Dismissed" : "Signed")}: {receipt.Item} as {receipt.Record.Id} at {receipt.Record.Hash}");
                        if (response.Problem is not null) CommandContext.WriteRuntime(context.Error, response.Problem.Message);
                    }

                    return response.Ok ? CommandContext.Clean : CommandContext.HasErrors;
                }

                int Refused(Problem problem, bool fromRuntime = false)
                {
                    if (parse.GetValue(context.JsonOption)) WriteJson(new OperatorActResponse(null, [], problem));
                    else if (fromRuntime) CommandContext.WriteRuntime(context.Error, problem.Message);
                    else context.Error.WriteLine(problem.Message);
                    return CommandContext.HasErrors;
                }

                void WriteJson<T>(T response) => context.Output.WriteLine(JsonSerializer.Serialize(response, ProtocolJson.Options));
            }, readOnly: !acting);
        });
    }

    /// <summary>The listing, or the reviewed act: each item with what its signature records, its exact subject and basis, and the choice made.</summary>
    internal static void Show(TextWriter output, OperatorActListResponse listing, IReadOnlyList<OperatorActChoice>? choices)
    {
        IReadOnlyList<OperatorActItem> items = listing.Items ?? [];
        if (choices is null) output.WriteLine($"Ready for {listing.By}: {items.Count} {(items.Count == 1 ? "item" : "items")}");
        else
        {
            int signing = choices.Count(c => c.Choice == "sign"), dismissing = choices.Count(c => c.Choice == "dismiss");
            output.WriteLine($"One act as {listing.By} signs {signing} and dismisses {dismissing} of {items.Count} ready items; the rest wait, and nothing else is signed:");
        }

        foreach (OperatorActItem item in items)
        {
            string choice = choices?.FirstOrDefault(c => c.Item == item.Id)?.Choice ?? (choices is null ? string.Empty : "wait");
            output.WriteLine($"{item.Id}  [{item.Kind}] {item.Title}{(choice.Length == 0 ? string.Empty : "  -> " + choice)}");
            output.WriteLine($"  Signs: {item.Records}");
            output.WriteLine($"  Subject: {item.SubjectType} {item.Subject.Id} at {item.Subject.Hash}");
            foreach (OperatorClosePin basis in item.Basis) output.WriteLine($"  Basis: {basis.Id} at {basis.Hash}");
            if (item.Recommendation is not null) output.WriteLine($"  Recommendation {item.RecommendationHash}: {item.Recommendation}");
            if (item.Dismissable) output.WriteLine("  May be dismissed with a reason.");
        }
    }
}
