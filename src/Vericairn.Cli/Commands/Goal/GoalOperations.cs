// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using Json = System.Text.Json.Nodes;
using Vericairn.Protocol;
using Vericairn.Records.Types;
using Vericairn.Records.Canonical;
using Vericairn.Records.Model;
using Vericairn.Sdk;
using static Vericairn.Cli.CommandContext;

namespace Vericairn.Cli;

internal sealed class GoalOperations(CommandContext context)
{
    /// <summary>The current goal condition by condition: the one named, or the one goal neither achieved nor ended; with none or several, the list.</summary>
    public int Goal(string root, string? id, bool json) => context.WithApi(root, api =>
    {
        id ??= OpenGoal(api, json);
        if (id is null)
        {
            return Clean;
        }

        GoalResponse? goal = api.Goal(id);
        if (goal is null)
        {
            return context.Fail($"No goal has id {id}.");
        }

        context.Output.Write(json ? CanonicalJson.SerializePretty(goal.Json) : goal.Text);
        return Clean;
    });

    /// <summary>
    /// A goal's next steps routed from the runtime's machine: the one named, or the one goal neither achieved nor ended; with none or
    /// several, the list. It records and starts nothing.
    /// </summary>
    public int GoalNext(string root, string? id, bool json) => context.WithApi(root, api =>
    {
        id ??= OpenGoal(api, json);
        if (id is null)
        {
            return Clean;
        }

        NextResponse? next = api.GoalNext(id);
        if (next is null)
        {
            return context.Fail($"No goal has id {id}.");
        }

        context.Output.Write(json ? CanonicalJson.SerializePretty(next.Json) : next.Text);
        return Clean;
    });

    /// <summary>Proposes a goal from its options: the conditions as JSON, the caps as unit=value, the inputs as type:id.</summary>
    public int GoalPropose(string root, GoalProposal proposal, bool json)
    {
        ArgumentNullException.ThrowIfNull(proposal);
        var completion = new Json.JsonArray();
        foreach (string condition in proposal.Done)
        {
            Json.JsonNode? node;
            try
            {
                node = Json.JsonNode.Parse(condition);
            }
            catch (JsonException exception)
            {
                return context.Fail($"--done '{condition}' is not JSON: {exception.Message}");
            }

            if (node is not Json.JsonObject entry)
            {
                return context.Fail($"--done '{condition}' is not a JSON object.");
            }

            completion.Add(entry.ContainsKey("record") || entry.ContainsKey("evidence") ? entry : new Json.JsonObject { ["record"] = entry });
        }

        var caps = new Json.JsonObject();
        foreach (string cap in proposal.Budget)
        {
            int equals = cap.IndexOf('=', StringComparison.Ordinal);
            string unit = equals > 0 ? cap[..equals] : string.Empty;
            string value = equals > 0 ? cap[(equals + 1)..] : string.Empty;
            string[] currency = value.Split(':');
            if (unit == "currency" && currency.Length == 2 && long.TryParse(currency[0], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out long amount))
            {
                caps[unit] = new Json.JsonObject { ["amount"] = amount, ["unit"] = currency[1] };
            }
            else if (unit != "currency" && Caps.UnitNames.Contains(unit) && long.TryParse(value, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out long number))
            {
                caps[unit] = number;
            }
            else
            {
                return context.Fail($"A cap is unit=value with the unit one of {string.Join(", ", Caps.UnitNames)}, and currency=<amount in minor units>:<unit>: '{cap}'.");
            }
        }

        var inputs = new Json.JsonArray();
        foreach (string input in proposal.Inputs)
        {
            int colon = input.IndexOf(':', StringComparison.Ordinal);
            if (colon <= 0)
            {
                return context.Fail($"An input is type:id: '{input}'.");
            }

            inputs.Add(new Json.JsonObject { ["type"] = input[..colon], ["id"] = input[(colon + 1)..] });
        }

        var body = new Json.JsonObject
        {
            ["title"] = proposal.Title,
            ["objective"] = proposal.Objective,
            ["completion"] = completion,
            ["constraints"] = new Json.JsonArray(proposal.Constraints.Select(c => (Json.JsonNode)c).ToArray()),
            ["kind"] = proposal.Kind,
            ["sponsor"] = proposal.Sponsor ?? proposal.By,
            ["budget"] = caps,
            ["plan"] = proposal.WorkOrders.Length == 0
                ? new Json.JsonObject { ["policy"] = "proposed-by-planner", ["approval"] = "sponsor-approves" }
                : new Json.JsonObject { ["policy"] = "given", ["approval"] = "sponsor-approves", ["workOrders"] = new Json.JsonArray(proposal.WorkOrders.Select(w => (Json.JsonNode)w).ToArray()) },
            ["inputs"] = inputs,
        };
        if (proposal.Delegate is not null)
        {
            body["delegate"] = proposal.Delegate;
        }

        if (proposal.Roster is not null)
        {
            body["roster"] = proposal.Roster;
        }

        if (proposal.Deadline is not null)
        {
            body["limits"] = new Json.JsonObject { ["deadline"] = proposal.Deadline };
        }

        return context.WithApi(root, api => context.ReportOutcome(api.GoalPropose(new GoalProposeRequest(proposal.By, CommandJson.Element(body), proposal.Supersedes)), json));
    }

    /// <summary>Activates, pauses, resumes or stops a goal.</summary>
    public int GoalAction(string root, string action, string goal, string by, string? note, bool json) => context.WithApi(root, api =>
    {
        var request = new GoalActionRequest(by, note);
        return context.ReportOutcome(action switch
        {
            "activate" => api.GoalActivate(goal, request),
            "pause" => api.GoalPause(goal, request),
            "resume" => api.GoalResume(goal, request),
            _ => api.GoalStop(goal, request),
        }, json);
    });

    /// <summary>Records the sponsor's custody exception for a goal's current revision, with its reason and its expiry.</summary>
    public int GoalCustodyException(string root, string goal, string by, string reason, string expires, bool json) => context.WithApi(root, api =>
    {
        if (!RecordParser.IsTimestamp(expires))
        {
            return context.Fail($"--expires is a time such as 2026-10-20T00:00:00Z, not '{expires}'.");
        }

        GoalResponse? shown = api.Goal(goal);
        if (shown is null)
        {
            return context.Fail($"No goal has id {goal}.");
        }

        JsonElement current = shown.Json.GetProperty("goal");
        var body = new Json.JsonObject { ["reason"] = reason, ["expires"] = expires };
        return context.ReportOutcome(api.NewRecord(new NewRecordRequest("custody-exception", by, CommandJson.Element(body), $"goal:{current.GetProperty("id").GetString()}:{current.GetProperty("hash").GetString()}")), json);
    });

    /// <summary>
    /// Takes a goal's next step, one at a time: an agent's run starts and, without --detach, is followed until it settles; a person's step
    /// is assigned and asked; a step no principal is available for is asked of the sponsor.
    /// </summary>
    public int GoalStep(string root, GoalStepOptions options, bool json)
    {
        ArgumentNullException.ThrowIfNull(options);
        Dictionary<string, string>? settings = CommandSettings.Parse(options.Settings, out string? problem);
        if (settings is null || !RunChoices.TryParse(options.Choices, out JsonElement? execution, out JsonElement? allocation, out problem))
        {
            return context.Fail(problem!);
        }

        return context.WithApi(root, api =>
        {
            if (options.Detach && api is not VericairnClient)
            {
                return context.Fail("--detach leaves an agent's run to the daemon that serves the estate, and no daemon serves it here: start one with vericairn runtime start, or take the step in the foreground.");
            }

            string? goal = options.Goal ?? OpenGoal(api, json);
            if (goal is null)
            {
                return Clean;
            }

            GoalStepResponse step = api.GoalStep(goal, new GoalStepRequest(options.By, options.Repository, settings.Count == 0 ? null : settings, options.WallMinutes, options.Branch, options.Base,
                options.Choices?.Configuration, execution, allocation, options.Choices?.ContextPackage));
            if (!step.Ok)
            {
                return step.Run?.Problem is { Findings.Count: > 0 } rejected ? context.ReportOutcome(WriteOutcome.Fail(rejected), json) : context.Fail(step.Message);
            }

            RunRow? row = step.Outcome == "run" && !options.Detach ? RunConsole.Follow(api, step.Run!.Run, options.By) : null;
            if (json)
            {
                context.Output.WriteLine(JsonSerializer.Serialize(new { step, settled = row }, ProtocolJson.Options));
            }
            else
            {
                context.Output.WriteLine(step.Step is null ? $"goal {step.Goal}  {step.Outcome}" : $"step {step.Step}  {step.Route}  {step.Outcome}{(step.Principal is null ? string.Empty : "  " + step.Principal)}");
                context.Output.WriteLine("  " + step.Message);
                RunConsole.PrintSettled(context, row);
            }

            return step.Outcome != "run" || options.Detach || row?.Status == "observed-success" ? Clean : HasErrors;
        });
    }

    /// <summary>Records a goal's evaluation from the evaluator's verdicts, n:verdict:reason, and the records read, n:id.</summary>
    public int GoalEvaluate(string root, string goal, string by, string summary, string[] verdicts, string[] reads, bool json)
    {
        var given = new SortedDictionary<int, (string Verdict, string Reason)>();
        foreach (string verdict in verdicts)
        {
            string[] parts = verdict.Split(':', 3);
            if (parts.Length != 3 || !int.TryParse(parts[0], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out int condition) || !given.TryAdd(condition, (parts[1], parts[2].Trim())))
            {
                return context.Fail($"A verdict is n:verdict:reason, once for each condition, with n from 1: '{verdict}'.");
            }
        }

        var read = new Dictionary<int, List<string>>();
        foreach (string entry in reads)
        {
            string[] parts = entry.Split(':', 2);
            if (parts.Length != 2 || !int.TryParse(parts[0], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out int condition) || !given.ContainsKey(condition))
            {
                return context.Fail($"A record read is n:record-id, for a condition given a verdict: '{entry}'.");
            }

            if (!read.TryGetValue(condition, out List<string>? ids))
            {
                read[condition] = ids = [];
            }

            ids.Add(parts[1]);
        }

        List<GoalVerdict> list = given.Select(g => new GoalVerdict(g.Key, g.Value.Verdict, g.Value.Reason, read.TryGetValue(g.Key, out List<string>? ids) ? ids : null)).ToList();
        return context.WithApi(root, api => context.ReportOutcome(api.GoalEvaluate(goal, new GoalEvaluateRequest(by, summary, list)), json));
    }

    /// <summary>Accepts a goal at its current revision, relying on its current evaluation.</summary>
    public int GoalAccept(string root, string goal, string by, string statement, bool json) =>
        context.WithApi(root, api => context.ReportOutcome(api.GoalAccept(goal, new GoalAcceptRequest(by, statement)), json));

    /// <summary>The one goal neither achieved nor ended; with none or several, prints the goals and returns null.</summary>
    private string? OpenGoal(IEstateApi api, bool json)
    {
        IReadOnlyList<GoalRow> rows = api.Goals();
        List<GoalRow> open = rows.Where(r => r.Status is not ("achieved" or "impossible" or "abandoned")).ToList();
        if (open.Count == 1)
        {
            return open[0].Id;
        }

        if (json)
        {
            context.Output.WriteLine(JsonSerializer.Serialize(rows, ProtocolJson.Options));
            return null;
        }

        context.Output.WriteLine(rows.Count == 0 ? "No goals; propose one with vericairn goal propose." : open.Count == 0 ? "No goal is open; the goals:" : $"{open.Count} goals are open; name one:");
        foreach (GoalRow row in rows)
        {
            context.Output.WriteLine($"{row.Id}  {row.Status}  {row.Title}  sponsor {row.SponsorName ?? row.Sponsor}");
        }

        return null;
    }
}
