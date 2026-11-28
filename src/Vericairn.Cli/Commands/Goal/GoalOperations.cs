// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.CommandLine;
using System.Text.Json;
using Json = System.Text.Json.Nodes;
using Vericairn.Protocol;
using Vericairn.Records.Types;
using Vericairn.Records.Canonical;
using Vericairn.Records.Estate;
using Vericairn.Records.Model;
using Vericairn.Records.Templates;
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

    /// <summary>
    /// From 0.9.4: a standing agent's occurrences as of an explicit time: the one named, or the one goal neither achieved nor ended; with
    /// none or several, the list. It records and starts nothing.
    /// </summary>
    public int GoalDue(string root, string? id, string? asOf, bool json) => context.WithApi(root, api =>
    {
        id ??= OpenGoal(api, json);
        if (id is null)
        {
            return Clean;
        }

        GoalDueResponse? due = api.GoalDue(id, asOf);
        if (due is null)
        {
            return context.Fail($"No goal has id {id}.");
        }

        context.Output.Write(json ? CanonicalJson.SerializePretty(due.Json) : due.Text);
        return due.Json.TryGetProperty("problem", out _) ? HasErrors : Clean;
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

        bool standing = proposal.Standing is not null;
        if (proposal.Flow is not null && standing)
        {
            return context.Fail("--flow proposes a run, and --standing a standing agent, which runs its flow once per occurrence: give one of them.");
        }

        if (proposal.Flow is null && !standing && ((proposal.Subject?.Length ?? 0) > 0 || (proposal.FlowOptions?.Length ?? 0) > 0 || proposal.Until is not null))
        {
            return context.Fail("--subject, --flow-option and --until configure a run, and go with --flow or --standing.");
        }

        if (proposal.Flow is null && !standing && proposal.FlowPaths is { Length: > 0 })
        {
            return context.Fail("--flow-path configures routed steps and goes with --flow or --standing.");
        }

        if (!standing && ((proposal.Triggers?.Length ?? 0) > 0 || (proposal.Period?.Length ?? 0) > 0 || proposal.RunsOn is not null || proposal.AwaitingReview is not null || (proposal.Outputs?.Length ?? 0) > 0))
        {
            return context.Fail("--trigger, --period, --awaiting-review, --outputs and --runs-on configure a standing agent, and go with --standing.");
        }

        return context.WithApi(root, api =>
        {
            if (proposal.Flow is not null || standing)
            {
                string? problem = RunPlan(api, proposal, body);
                if (problem is not null)
                {
                    return context.Fail(problem);
                }
            }

            WriteOutcome outcome = api.GoalPropose(new GoalProposeRequest(proposal.By, CommandJson.Element(body), proposal.Supersedes));
            int code = ReportGoalWrite(outcome, json);
            // A standing agent's preview: its triggers, what would be due and its caps, before its sponsor activates it.
            if (standing && outcome.Ok && !json && outcome.Written is RecordWritten written && api.GoalDue(written.Id) is GoalDueResponse due)
            {
                context.Output.Write(due.Text);
            }

            return code;
        });
    }

    /// <summary>
    /// The plan of a run (the estate's design/flow.md § 12): the flow's exact pin, read from the templates this runtime offers, the
    /// subject bound by the kinds of the flow's subject fields, the goal's own options and the step it ends at; null, or what is wrong.
    /// </summary>
    private static string? RunPlan(IEstateApi api, GoalProposal proposal, Json.JsonObject body)
    {
        bool standing = proposal.Standing is not null;
        string chosen = proposal.Standing ?? proposal.Flow!;
        string option = standing ? "--standing" : "--flow";
        int at = chosen.LastIndexOf('@');
        string id = at > 0 ? chosen[..at] : chosen;
        string? version = at > 0 ? chosen[(at + 1)..] : null;
        TemplatesResponse templates = api.Templates();
        if (templates.Problem is not null)
        {
            return templates.Problem.Message;
        }

        List<TemplateDto> flows = templates.Templates.Where(t => t.Pin.Id == id && (version is null || t.Pin.Version == version)
            && t.Definition.TryGetProperty("kind", out JsonElement kind) && kind.ValueKind == JsonValueKind.String && kind.GetString() == "flow").ToList();
        if (flows.Count != 1)
        {
            return flows.Count == 0
                ? $"{option} names no flow this runtime offers: {chosen}; vericairn templates list shows the templates, flows among them."
                : $"{option} {chosen} names several versions of the flow; name one as id@version.";
        }

        if (proposal.WorkOrders.Length != 1)
        {
            return standing ? "A standing agent's work is one work order: give exactly one --work-order with --standing." : "A run's work is one work order: give exactly one --work-order with --flow.";
        }

        TemplateDto flow = flows[0];
        JsonElement declared;
        try
        {
            declared = ResolvedSubject(templates.Templates, flow);
        }
        catch (Exception error) when (error is InvalidOperationException or JsonException)
        {
            return $"{option} {chosen} does not resolve with every base it extends: {error.Message}";
        }

        Dictionary<string, (string Type, string? RecordType)> fields = declared.ValueKind == JsonValueKind.Object && declared.TryGetProperty("fields", out JsonElement named) && named.ValueKind == JsonValueKind.Array
            ? named.EnumerateArray().ToDictionary(f => f.GetProperty("name").GetString()!, f => (f.GetProperty("type").GetString()!, f.TryGetProperty("recordType", out JsonElement type) ? type.GetString() : null), StringComparer.Ordinal)
            : new Dictionary<string, (string Type, string? RecordType)>(StringComparer.Ordinal);
        var subject = new Json.JsonObject();
        foreach (string entry in proposal.Subject ?? [])
        {
            int equals = entry.IndexOf('=', StringComparison.Ordinal);
            string name = equals > 0 ? entry[..equals] : entry;
            string value = equals > 0 ? entry[(equals + 1)..] : string.Empty;
            if (equals <= 0 || !fields.TryGetValue(name, out (string Type, string? RecordType) field))
            {
                return $"--subject is name=value with a field of the flow's subject ({string.Join(", ", fields.Keys)}): '{entry}'.";
            }

            int colon = value.IndexOf(':', StringComparison.Ordinal);
            subject[name] = field.Type switch
            {
                "record" => new Json.JsonObject { ["type"] = colon > 0 ? value[..colon] : field.RecordType, ["id"] = colon > 0 ? value[(colon + 1)..] : value },
                "list" => new Json.JsonArray(value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(i => (Json.JsonNode)i).ToArray()),
                _ => value,
            };
        }

        if (standing)
        {
            return StandingPlan(proposal, body, flow, subject);
        }

        var options = new Json.JsonObject();
        foreach (string entry in proposal.FlowOptions ?? [])
        {
            int equals = entry.IndexOf('=', StringComparison.Ordinal);
            if (equals <= 0)
            {
                return $"--flow-option is name=value, such as step.shorts=off: '{entry}'.";
            }

            options[entry[..equals]] = entry[(equals + 1)..];
        }

        var run = new Json.JsonObject
        {
            ["pin"] = new Json.JsonObject { ["id"] = flow.Pin.Id, ["version"] = flow.Pin.Version, ["hash"] = flow.Pin.Hash },
            ["subject"] = subject,
        };
        if (options.Count > 0)
        {
            run["options"] = options;
        }

        if (proposal.FlowPaths is { Length: > 0 } paths)
        {
            run["paths"] = new Json.JsonArray(paths.Select(path => (Json.JsonNode)path).ToArray());
        }

        if (proposal.Until is not null)
        {
            run["until"] = proposal.Until;
        }

        body["plan"]!["flow"] = run;
        return null;
    }

    /// <summary>
    /// The plan of a standing agent (from 0.9.4, GOAL-07; the estate's design/standing-agents.md § 5): the flow's exact pin, the subject,
    /// the triggers as JSON, the period's caps as unit=value, the until time (thirty days from now when omitted) and the dispatching
    /// runtime; null, or what is wrong.
    /// </summary>
    private static string? StandingPlan(GoalProposal proposal, Json.JsonObject body, TemplateDto flow, Json.JsonObject subject)
    {
        if (proposal.Kind != "stewardship")
        {
            return "A standing agent is a stewardship goal: give --kind stewardship with --standing.";
        }

        if ((proposal.FlowOptions?.Length ?? 0) > 0)
        {
            return "--flow-option configures a run; a standing agent takes --trigger, --period, --until and --runs-on.";
        }

        var triggers = new Json.JsonArray();
        foreach (string trigger in proposal.Triggers ?? [])
        {
            try
            {
                if (Json.JsonNode.Parse(trigger) is not Json.JsonObject entry)
                {
                    return $"--trigger '{trigger}' is not a JSON object.";
                }

                triggers.Add(entry);
            }
            catch (JsonException exception)
            {
                return $"--trigger '{trigger}' is not JSON: {exception.Message}";
            }
        }

        if (triggers.Count == 0)
        {
            return "A standing agent has at least one --trigger, as JSON with its own key and kind, such as a monthly schedule: key monthly, kind schedule, unit months, every 1 and firstAt a UTC time.";
        }

        var period = new Json.JsonObject();
        foreach (string cap in proposal.Period ?? [])
        {
            int equals = cap.IndexOf('=', StringComparison.Ordinal);
            string unit = equals > 0 ? cap[..equals] : string.Empty;
            string value = equals > 0 ? cap[(equals + 1)..] : string.Empty;
            string[] currency = value.Split(':');
            if (unit == "unit" && value is "days" or "months")
            {
                period[unit] = value;
            }
            else if (unit == "currency" && currency.Length == 2 && long.TryParse(currency[0], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out long amount))
            {
                period[unit] = new Json.JsonObject { ["amount"] = amount, ["unit"] = currency[1] };
            }
            else if (unit is "every" or "runs" or "wallMinutes" or "tokens" && long.TryParse(value, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out long number))
            {
                period[unit] = number;
            }
            else
            {
                return $"A period cap is unit=months|days, every, runs, wallMinutes or tokens as a number, or currency=<amount in minor units>:<unit>: '{cap}'.";
            }
        }

        if (!period.ContainsKey("unit"))
        {
            period["unit"] = "months";
        }

        var plan = new Json.JsonObject
        {
            ["pin"] = new Json.JsonObject { ["id"] = flow.Pin.Id, ["version"] = flow.Pin.Version, ["hash"] = flow.Pin.Hash },
            ["subject"] = subject,
            ["triggers"] = triggers,
            ["period"] = period,
            ["until"] = proposal.Until ?? RecordParser.FormatTimestamp(DateTimeOffset.UtcNow.AddDays(Vericairn.Records.Estate.StandingAgents.DefaultDays)),
        };
        if (proposal.FlowPaths is { Length: > 0 } paths)
        {
            plan["paths"] = new Json.JsonArray(paths.Select(path => (Json.JsonNode)path).ToArray());
        }

        if (proposal.RunsOn is not null)
        {
            plan["runsOn"] = proposal.RunsOn;
        }

        if (proposal.AwaitingReview is long awaiting)
        {
            plan["awaitingReview"] = awaiting;
        }

        if (proposal.Outputs is { Length: > 0 } outputs)
        {
            var caps = new Json.JsonObject();
            foreach (string cap in outputs)
            {
                string[] parts = cap.Split('=', 2);
                if (parts.Length != 2 || parts[0] is not ("possibilities" or "drafts" or "open") || !long.TryParse(parts[1], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out long value))
                {
                    return $"An output cap is possibilities, drafts or open as name=number: '{cap}'.";
                }

                caps[parts[0]] = value;
            }

            plan["outputs"] = caps;
        }

        body["plan"]!["standing"] = plan;
        return null;
    }

    /// <summary>
    /// The subject a run of a flow binds: the flow resolved with every base it extends, as the goal's validation and its view read it, so
    /// an extension binds the subject it inherits. Each base is read by its exact pin from this build's own templates or from those the
    /// runtime offers.
    /// </summary>
    private static JsonElement ResolvedSubject(IReadOnlyList<TemplateDto> offered, TemplateDto flow) =>
        FlowDefinition.Resolve(TemplateDefinition.Read(flow.Definition), pin => TemplateCatalog.Resolve(pin, offered
            .Where(t => t.Pin.Id == pin.Id && t.Pin.Version == pin.Version && t.Pin.Hash == pin.Hash).Select(t => TemplateDefinition.Read(t.Definition)))).Subject;

    /// <summary>
    /// Pauses one goal, or from 0.9.4 with <paramref name="standing"/> every active standing agent the acting principal may pause, each
    /// pause asking the goal's runs in flight to stop with <paramref name="stopRuns"/>.
    /// </summary>
    public int GoalPause(string root, string? goal, string by, string? note, bool standing, bool stopRuns, bool json)
    {
        if (standing == (goal is not null))
        {
            return context.Fail(standing ? "--standing pauses every standing agent you may pause; name no goal with it." : "Name the goal to pause, or pause every standing agent you may pause with --standing.");
        }

        if (!standing)
        {
            return context.WithApi(root, api => context.ReportOutcome(api.GoalPause(goal!, new GoalActionRequest(by, note) { StopRuns = stopRuns }), json));
        }

        return context.WithApi(root, api =>
        {
            var paused = new List<object>();
            foreach (GoalRow row in api.Goals().Where(g => g.Status == "active"))
            {
                if (api.Goal(row.Id) is not GoalResponse shown || !shown.Json.TryGetProperty("standing", out _))
                {
                    continue;
                }

                WriteOutcome outcome = api.GoalPause(row.Id, new GoalActionRequest(by, note) { StopRuns = stopRuns });
                paused.Add(new { goal = row.Id, title = row.Title, paused = outcome.Ok, record = outcome.Written?.Id, notices = outcome.Notices, problem = outcome.Problem?.Message });
                if (!json)
                {
                    context.Output.WriteLine(outcome.Ok ? $"paused  {row.Id}  {row.Title}  goal-status {outcome.Written!.Id}" : $"not paused  {row.Id}  {row.Title}: {outcome.Problem?.Message}");
                    foreach (string notice in outcome.Notices ?? []) context.Output.WriteLine("  " + notice);
                }
            }

            if (json)
            {
                context.Output.WriteLine(JsonSerializer.Serialize(new { standing = paused }, ProtocolJson.Options));
            }
            else if (paused.Count == 0)
            {
                context.Output.WriteLine("No standing agent is active.");
            }

            return Clean;
        });
    }

    /// <summary>Activates, pauses, resumes or stops a goal.</summary>
    public int GoalAction(string root, string action, string goal, string by, string? note, bool json) => context.WithApi(root, api =>
    {
        var request = new GoalActionRequest(by, note);
        return ReportGoalWrite(action switch
        {
            "activate" => api.GoalActivate(goal, request),
            "pause" => api.GoalPause(goal, request),
            "resume" => api.GoalResume(goal, request),
            _ => api.GoalStop(goal, request),
        }, json);
    });

    private int ReportGoalWrite(WriteOutcome outcome, bool json)
    {
        if (outcome.Problem is { Code: FlowPermissions.Code } problem)
        {
            if (json) context.Output.WriteLine(JsonSerializer.Serialize(new { problem }, ProtocolJson.Options));
            CommandContext.WriteRuntime(context.Error, $"{problem.Code} {problem.Message}");
            return Failed;
        }

        return context.ReportOutcome(outcome, json);
    }

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
            if (options.Detach && !options.Check && !context.UsingDaemon)
            {
                return context.Fail("--detach leaves an agent's run to the daemon that serves the estate, and no daemon serves it here: start one with vericairn runtime start, or take the step in the foreground.");
            }

            string? goal = options.Goal ?? OpenGoal(api, json);
            if (goal is null)
            {
                return Clean;
            }

            GoalStepResponse step = api.GoalStep(goal, new GoalStepRequest(options.By, options.Repository, settings.Count == 0 ? null : settings, options.WallMinutes, options.Branch, options.Base,
                options.Choices?.Configuration, execution, allocation, options.Choices?.ContextPackage) { Search = options.Choices?.Search, Occurrence = options.Occurrence, Now = options.Now, Check = options.Check });
            if (!step.Ok)
            {
                // From 0.9.4 (CODE-80) the runtime's problem is shown as its own words, marked and escaped.
                if (step.Run?.Problem is { Findings.Count: > 0 } rejected) return context.ReportOutcome(WriteOutcome.Fail(rejected), json, runtimeProblem: true);
                if (step.Problem is { } stepProblem && (options.Check || stepProblem.Code == FlowPermissions.Code))
                {
                    if (json) context.Output.WriteLine(JsonSerializer.Serialize(new { step }, ProtocolJson.Options));
                    CommandContext.WriteRuntime(context.Error, $"{stepProblem.Code} {step.Message}");
                    return Failed;
                }
                CommandContext.WriteRuntime(context.Error, step.Message);
                return Failed;
            }

            // From 0.9.4 (CODE-73): the overlaps the step's assignment has, and any after routing recorded, beside the step's output.
            foreach (string notice in (step.Notices ?? []).Concat(step.Run?.Notices ?? [])) context.Error.WriteLine(notice);
            RunRow? row = step.Outcome == "run" && !options.Detach ? RunConsole.Follow(context, api, step.Run!.Run, options.By) : null;
            if (json)
            {
                context.Output.WriteLine(JsonSerializer.Serialize(new { step, settled = row }, ProtocolJson.Options));
            }
            else
            {
                context.Output.WriteLine(step.Step is null ? $"goal {step.Goal}  {step.Outcome}" : $"step {step.Step}  {step.Route}  {step.Outcome}{(step.Principal is null ? string.Empty : "  " + step.Principal)}");
                context.Output.WriteLine("  " + step.Message);
                if (options.Check && step.PermittedChanges is { Count: > 0 } paths)
                {
                    context.Output.WriteLine("  Permitted changes");
                    foreach (string path in paths) context.Output.WriteLine("    " + path);
                }

                RunConsole.PrintSettled(context, row);
            }

            return step.Outcome != "run" || options.Detach || row?.Status == "observed-success" ? Clean : HasErrors;
        }, readOnly: options.Check);
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

    /// <summary>Accepts a goal at its current revision, relying on its current evaluation; with reasons, it closes the goal's run as well.</summary>
    public int GoalAccept(string root, string goal, string by, string statement, string? reason, string[] stepReasons, bool json)
    {
        if (!TryStepReasons(stepReasons, out List<GoalStepReason> steps, out string? problem))
        {
            return context.Fail(problem!);
        }

        return context.WithApi(root, api => context.ReportOutcome(api.GoalAccept(goal, new GoalAcceptRequest(by, statement)
        {
            Reason = reason, StepReasons = steps.Count == 0 ? null : steps,
        }), json));
    }

    /// <summary>Closes a goal's run with a reason for each unsettled step: one for all, or one for each.</summary>
    public int GoalClose(string root, string goal, string by, string? reason, string[] stepReasons, bool json)
    {
        if (!TryStepReasons(stepReasons, out List<GoalStepReason> steps, out string? problem))
        {
            return context.Fail(problem!);
        }

        if (reason is null && steps.Count == 0)
        {
            return context.Fail("A run is closed with a reason: --reason for every unsettled step, or --step-reason <step>=<reason> for each.");
        }

        return context.WithApi(root, api => context.ReportOutcome(api.GoalClose(goal, new GoalCloseRequest(by, reason, steps.Count == 0 ? null : steps)), json));
    }

    /// <summary>
    /// Decides a run's decision step (from 0.9.4, PROC-19): the path or paths taken, the reasons and the evidence, recorded as the result of
    /// the step's assignment by the principal holding it. It performs nothing reserved and dispatches nothing.
    /// </summary>
    public int GoalDecide(string root, string goal, string by, string step, string[] paths, string reason, string[] evidence, bool json)
    {
        if (paths.Length == 0)
        {
            return context.Fail("A decision names the path it takes: --path <name>, repeated for several paths at once where the flow allows.");
        }

        if (string.IsNullOrWhiteSpace(reason))
        {
            return context.Fail("A decision records its reasons: --reason <why this path>.");
        }

        return context.WithApi(root, api => context.ReportOutcome(api.GoalDecide(goal, new GoalDecideRequest(by, step, paths, reason, evidence.Length == 0 ? null : evidence)), json));
    }

    /// <summary>The --reason option of goal close and goal accept.</summary>
    internal static Option<string?> ReasonOption() => new("--reason")
    {
        Description = "From 0.9.4: closes the goal's run, with this reason for every step it has not settled that --step-reason gives none.",
    };

    /// <summary>The repeatable --step-reason option of goal close and goal accept.</summary>
    internal static Option<string[]> StepReasonOption() => new("--step-reason")
    {
        Description = "From 0.9.4: the reason for one unsettled step, as <step>=<reason>, the step as its key or key[instance]; repeatable.",
    };

    /// <summary>The step reasons as given: each step=reason, the step a key or key[instance].</summary>
    private static bool TryStepReasons(string[] given, out List<GoalStepReason> steps, out string? problem)
    {
        steps = [];
        problem = null;
        foreach (string entry in given)
        {
            int equals = entry.IndexOf('=', StringComparison.Ordinal);
            if (equals <= 0 || equals == entry.Length - 1)
            {
                problem = $"--step-reason is <step>=<reason>, such as announce-x=The X post is not made for this release: '{entry}'.";
                return false;
            }

            steps.Add(new GoalStepReason(entry[..equals].Trim(), entry[(equals + 1)..].Trim()));
        }

        return true;
    }

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
