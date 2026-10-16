// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Json = System.Text.Json.Nodes;
using Vericairn.Daemon;
using Vericairn.Mcp;
using Vericairn.Protocol;
using Vericairn.Records.Authoring;
using Vericairn.Records.Canonical;
using Vericairn.Records.Estate;
using Vericairn.Records.Model;
using Vericairn.Records.Signing;
using Vericairn.Records.Types;
using Vericairn.Runtime.Portability;
using Vericairn.Runtime.Providers;
using Vericairn.Runtime.Service;
using Vericairn.Packs.Software;
using Vericairn.Runtime.Store;
using Vericairn.Sdk;

namespace Vericairn.Cli;

/// <summary>
/// Command implementations. Every operation on an open estate goes through the API: the daemon's when one serves the estate on
/// this machine, the embedded runtime otherwise, so that one owner holds the live state and one implementation evaluates
/// authority. Exit codes: 0 clean, 1 findings with errors, 2 usage or failure.
/// </summary>
internal static partial class Commands
{
    internal const int Clean = 0;
    internal const int HasErrors = 1;
    internal const int Failed = 2;

    public static int Validate(string root, bool json) =>
        IsEstate(root) ? WithApi(root, api => ReportFindings(api.Validate(), json)) : WithFiles(root, model => ReportFindings(ToFindings(model.Findings, $"{model.Records.Count} records"), json));

    public static int Check(string root, bool json) =>
        IsEstate(root) ? WithApi(root, api => ReportFindings(api.Check(), json)) : WithFiles(root, model => ReportFindings(ToFindings(Combined(model), $"{model.Records.Count} records"), json));

    public static int Status(string root, bool json, bool observe = false, string? by = null, CommunicationSelection? presentation = null)
    {
        if (observe || by is not null)
        {
            if (!observe || string.IsNullOrWhiteSpace(by))
            {
                return Fail("Recording an observation requires both --observe and --by with a human principal id.");
            }

            return WithApi(root, api =>
            {
                WriteOutcome written = api.ObserveEstate(new EstateObserveRequest(by));
                if (!written.Ok)
                {
                    return ReportOutcome(written, json);
                }

                StatusResponse observed = api.Status();
                Console.Out.Write(json ? CanonicalJson.SerializePretty(observed.Json) : StatusPresentation(api, observed, presentation));
                return observed.HasErrors ? HasErrors : Clean;
            });
        }

        if (!IsEstate(root))
        {
            return WithFiles(root, model =>
            {
                FindingList checks = EstateChecks.Run(model);
                Console.Out.Write(json ? EstateStatus.RenderJson(model, checks) : EstateStatus.Render(model, checks));
                return model.Findings.HasErrors || checks.HasErrors ? HasErrors : Clean;
            });
        }

        return WithApi(root, api =>
        {
            StatusResponse status = api.Status();
            Console.Out.Write(json ? CanonicalJson.SerializePretty(status.Json) : StatusPresentation(api, status, presentation));
            return status.HasErrors ? HasErrors : Clean;
        });
    }

    /// <summary>Recorded progress with labelled local claims and worker observations, through the API; exit 1 when the check it shows found errors, as status does.</summary>
    public static int Progress(string root, bool json, CommunicationSelection? presentation = null) => WithApi(root, api =>
    {
        ProgressResponse progress = api.Progress();
        Console.Out.Write(json ? CanonicalJson.SerializePretty(progress.Json) : ProtectedPresentation(api, "status", "estate progress", progress.Text, presentation) + CommunicationPresentation.RenderAttention(CommunicationPresentation.Attention(api, presentation?.Recipient)));
        return progress.HasErrors ? HasErrors : Clean;
    });

    public static int Resume(string root, string input, bool json) => WithBody(input, body => WithApi(root, api =>
    {
        HandoffPackage? package = body.Deserialize<HandoffPackage>(ProtocolJson.Options);
        if (package is null)
        {
            return Fail("A retained hand-over package is required.");
        }

        ResumeResponse response = api.ResumeHandoff(package);
        Console.Out.Write(json ? response.Json is JsonElement view ? CanonicalJson.SerializePretty(view) : JsonSerializer.Serialize(response.Problem, ProtocolJson.Options) : response.Text);
        return response.Problem is not null ? Failed : response.HasErrors ? HasErrors : Clean;
    }));

    /// <summary>List current document revision identities without reading their original bodies.</summary>
    public static int ContextCatalogue(string root, bool json) => WithApi(root, api =>
    {
        var rows = api.Records("document-revision").Select(r => new { r.Id, r.Hash, Path = r.Envelope.GetProperty("body").GetProperty("path").GetString() }).ToList();
        if (json)
        {
            Console.Out.WriteLine(JsonSerializer.Serialize(rows, ProtocolJson.Options));
        }
        else
        {
            foreach (var row in rows)
            {
                Console.Out.WriteLine($"{row.Path}  revision {row.Id}  {row.Hash}");
            }
        }

        return Clean;
    });

    /// <summary>Compute context without writing through the runtime's common contract.</summary>
    public static int ContextPrepare(string root, string workOrder, bool json, long? maxBytes = null) => WithApi(root, api =>
    {
        ContextResponse response = api.Context(workOrder, maxBytes);
        Console.Out.Write(json ? JsonSerializer.Serialize(response.Package is null ? (object)response.Problem! : response.Package, ProtocolJson.Options) : response.Text);
        return response.Problem is null ? Clean : Failed;
    });

    /// <summary>Retrieve verified original bytes, or the identity and base64 bytes as JSON.</summary>
    public static int ContextRead(string root, string manifest, string? source, bool original, string? output, bool json) => WithApi(root, api =>
    {
        if (!original)
        {
            return Fail("Context retrieval requires --original.");
        }

        ContextReadResponse response = source is null ? api.ContextRead(manifest) : api.ContextRead(manifest, source);
        if (response.Problem is not null)
        {
            Console.Out.Write(json ? JsonSerializer.Serialize(response, ProtocolJson.Options) : response.Problem.Message + "\n");
            return Failed;
        }

        if (output is not null)
        {
            using var file = new FileStream(output, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            file.Write(response.Content!);
        }

        if (json)
        {
            Console.Out.Write(JsonSerializer.Serialize(response, ProtocolJson.Options));
        }
        else if (output is null)
        {
            using Stream stream = Console.OpenStandardOutput();
            stream.Write(response.Content!);
        }

        return Clean;
    });

    public static int Handoff(string root, string? output, long? maxBytes, bool json, CommunicationSelection? presentation = null) => WithApi(root, api =>
    {
        HandoffResponse response = api.Handoff(maxBytes);
        if (response.Package is null)
        {
            Console.Out.Write(json ? JsonSerializer.Serialize(response.Problem, ProtocolJson.Options) : response.Text);
            return Failed;
        }

        string package = CanonicalJson.SerializePretty(JsonSerializer.SerializeToElement(response.Package, ProtocolJson.Options));
        if (output is not null)
        {
            using var file = new FileStream(output, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            file.Write(Encoding.UTF8.GetBytes(package));
        }

        Console.Out.Write(json ? package : ContextTextMeasurement.Presentation(ProtectedPresentation(api, "handoff", response.Package.Hash, response.Text, presentation)).Text);
        return Clean;
    });

    /// <summary>What waits on a principal, or on every principal, through the API: at most three asks in one delivery to each, the recommendation first.</summary>
    public static int Ask(string root, string? to, bool json) => WithApi(root, api =>
    {
        if (to is not null && !Records.Identity.Ulid.IsValid(to))
        {
            return Fail($"--to names a principal by its ULID, not '{to}'.");
        }

        AsksResponse asks = api.Asks(to);
        Console.Out.Write(json ? CanonicalJson.SerializePretty(asks.Json) : asks.Text);
        return Clean;
    });

    public static int New(string root, string type, string by, string bodyPath, string? subject, string? supersedes, string? at, string? id, bool json) =>
        WithBody(bodyPath, body => WithApi(root, api => ReportOutcome(api.NewRecord(new NewRecordRequest(type, by, body, subject, supersedes, at, id)), json)));

    public static int WorkBrief(string root, string key, bool json, CommunicationSelection? presentation = null) => WithApi(root, api =>
    {
        BriefResponse? brief = json ? api.Brief(key) : api.BriefFor(key, presentation?.Recipient, presentation?.Assignment, presentation?.Values("handoff"));
        if (brief is null)
        {
            return Fail($"No current work order has key or id {key}.");
        }

        Console.Out.Write(json ? CanonicalJson.SerializePretty(brief.Json) : brief.Markdown);
        return brief.Problem is null ? Clean : Failed;
    });

    public static int WorkObserve(string root, string key, bool json, CommunicationSelection? presentation = null) => WithApi(root, api =>
    {
        ObserveResponse? observe = api.Observe(key);
        if (observe is null)
        {
            return Fail($"No current work order has key or id {key}.");
        }

        Console.Out.Write(json ? CanonicalJson.SerializePretty(observe.Json) : ProtectedPresentation(api, "result", key, observe.Text, presentation));
        return Clean;
    });

    public static int WorkAssign(string root, string key, string contributor, string by, string briefPath, string? note, bool json) =>
        WithBody(briefPath, brief => WithApi(root, api => ReportOutcome(api.Assign(new AssignRequest(key, contributor, by, brief, note)), json)));

    public static int WorkAttempt(string root, string assignmentId, string by, string workspace, string note, string? at, bool json) =>
        WithApi(root, api => ReportOutcome(api.Attempt(new AttemptRequest(assignmentId, by, workspace, note, at)), json));

    public static int WorkResult(string root, string attemptId, string by, string bodyPath, bool json) =>
        WithBody(bodyPath, body => WithApi(root, api => ReportOutcome(api.Result(new ResultRequest(attemptId, by, body)), json)));

    public static int WorkAmend(string root, string assignmentId, string by, string bodyPath, bool json) =>
        WithBody(bodyPath, body => WithApi(root, api => ReportOutcome(api.Amend(new AmendRequest(assignmentId, by, body)), json)));

    /// <summary>The current goal condition by condition: the one named, or the one goal neither achieved nor ended; with none or several, the list.</summary>
    public static int Goal(string root, string? id, bool json) => WithApi(root, api =>
    {
        id ??= OpenGoal(api, json);
        if (id is null)
        {
            return Clean;
        }

        GoalResponse? goal = api.Goal(id);
        if (goal is null)
        {
            return Fail($"No goal has id {id}.");
        }

        Console.Out.Write(json ? CanonicalJson.SerializePretty(goal.Json) : goal.Text);
        return Clean;
    });

    /// <summary>
    /// A goal's next steps routed from the runtime's machine: the one named, or the one goal neither achieved nor ended; with none or
    /// several, the list. It records and starts nothing.
    /// </summary>
    public static int GoalNext(string root, string? id, bool json) => WithApi(root, api =>
    {
        id ??= OpenGoal(api, json);
        if (id is null)
        {
            return Clean;
        }

        NextResponse? next = api.GoalNext(id);
        if (next is null)
        {
            return Fail($"No goal has id {id}.");
        }

        Console.Out.Write(json ? CanonicalJson.SerializePretty(next.Json) : next.Text);
        return Clean;
    });

    /// <summary>The one goal neither achieved nor ended; with none or several, prints the goals and returns null.</summary>
    private static string? OpenGoal(IEstateApi api, bool json)
    {
        IReadOnlyList<GoalRow> rows = api.Goals();
        List<GoalRow> open = rows.Where(r => r.Status is not ("achieved" or "impossible" or "abandoned")).ToList();
        if (open.Count == 1)
        {
            return open[0].Id;
        }

        if (json)
        {
            Console.Out.WriteLine(JsonSerializer.Serialize(rows, ProtocolJson.Options));
            return null;
        }

        Console.Out.WriteLine(rows.Count == 0 ? "No goals; propose one with vericairn goal propose." : open.Count == 0 ? "No goal is open; the goals:" : $"{open.Count} goals are open; name one:");
        foreach (GoalRow row in rows)
        {
            Console.Out.WriteLine($"{row.Id}  {row.Status}  {row.Title}  sponsor {row.SponsorName ?? row.Sponsor}");
        }

        return null;
    }

    /// <summary>What <c>vericairn goal propose</c> was given.</summary>
    internal sealed record GoalProposal(string By, string Title, string Objective, string[] Done, string? Sponsor, string? Delegate, string[] Constraints, string[] Budget, string? Roster, string Kind, string[] WorkOrders, string[] Inputs, string? Deadline, string? Supersedes);

    /// <summary>Proposes a goal from its options: the conditions as JSON, the caps as unit=value, the inputs as type:id.</summary>
    public static int GoalPropose(string root, GoalProposal proposal, bool json)
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
                return Fail($"--done '{condition}' is not JSON: {exception.Message}");
            }

            if (node is not Json.JsonObject entry)
            {
                return Fail($"--done '{condition}' is not a JSON object.");
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
                return Fail($"A cap is unit=value with the unit one of {string.Join(", ", Caps.UnitNames)}, and currency=<amount in minor units>:<unit>: '{cap}'.");
            }
        }

        var inputs = new Json.JsonArray();
        foreach (string input in proposal.Inputs)
        {
            int colon = input.IndexOf(':', StringComparison.Ordinal);
            if (colon <= 0)
            {
                return Fail($"An input is type:id: '{input}'.");
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

        return WithApi(root, api => ReportOutcome(api.GoalPropose(new GoalProposeRequest(proposal.By, Element(body), proposal.Supersedes)), json));
    }

    /// <summary>Activates, pauses, resumes or stops a goal.</summary>
    public static int GoalAction(string root, string action, string goal, string by, string? note, bool json) => WithApi(root, api =>
    {
        var request = new GoalActionRequest(by, note);
        return ReportOutcome(action switch
        {
            "activate" => api.GoalActivate(goal, request),
            "pause" => api.GoalPause(goal, request),
            "resume" => api.GoalResume(goal, request),
            _ => api.GoalStop(goal, request),
        }, json);
    });

    /// <summary>Records a goal's evaluation from the evaluator's verdicts, n:verdict:reason, and the records read, n:id.</summary>
    public static int GoalEvaluate(string root, string goal, string by, string summary, string[] verdicts, string[] reads, bool json)
    {
        var given = new SortedDictionary<int, (string Verdict, string Reason)>();
        foreach (string verdict in verdicts)
        {
            string[] parts = verdict.Split(':', 3);
            if (parts.Length != 3 || !int.TryParse(parts[0], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out int condition) || !given.TryAdd(condition, (parts[1], parts[2].Trim())))
            {
                return Fail($"A verdict is n:verdict:reason, once for each condition, with n from 1: '{verdict}'.");
            }
        }

        var read = new Dictionary<int, List<string>>();
        foreach (string entry in reads)
        {
            string[] parts = entry.Split(':', 2);
            if (parts.Length != 2 || !int.TryParse(parts[0], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out int condition) || !given.ContainsKey(condition))
            {
                return Fail($"A record read is n:record-id, for a condition given a verdict: '{entry}'.");
            }

            if (!read.TryGetValue(condition, out List<string>? ids))
            {
                read[condition] = ids = [];
            }

            ids.Add(parts[1]);
        }

        List<GoalVerdict> list = given.Select(g => new GoalVerdict(g.Key, g.Value.Verdict, g.Value.Reason, read.TryGetValue(g.Key, out List<string>? ids) ? ids : null)).ToList();
        return WithApi(root, api => ReportOutcome(api.GoalEvaluate(goal, new GoalEvaluateRequest(by, summary, list)), json));
    }

    /// <summary>Records the sponsor's custody exception for a goal's current revision, with its reason and its expiry.</summary>
    public static int GoalCustodyException(string root, string goal, string by, string reason, string expires, bool json) => WithApi(root, api =>
    {
        if (!RecordParser.IsTimestamp(expires))
        {
            return Fail($"--expires is a time such as 2026-10-20T00:00:00Z, not '{expires}'.");
        }

        GoalResponse? shown = api.Goal(goal);
        if (shown is null)
        {
            return Fail($"No goal has id {goal}.");
        }

        JsonElement current = shown.Json.GetProperty("goal");
        var body = new Json.JsonObject { ["reason"] = reason, ["expires"] = expires };
        return ReportOutcome(api.NewRecord(new NewRecordRequest("custody-exception", by, Element(body), $"goal:{current.GetProperty("id").GetString()}:{current.GetProperty("hash").GetString()}")), json);
    });

    /// <summary>Accepts a goal at its current revision, relying on its current evaluation.</summary>
    public static int GoalAccept(string root, string goal, string by, string statement, bool json) =>
        WithApi(root, api => ReportOutcome(api.GoalAccept(goal, new GoalAcceptRequest(by, statement)), json));

    /// <summary>What <c>vericairn worker run</c> was given.</summary>
    internal sealed record WorkerRunOptions(string Assignment, string Launcher, string Agent, string By, string Repository, string? Goal, string? Branch, string? Base, long? WallMinutes, string[] Settings, bool Detach);

    /// <summary>Lists the coding-agent launchers, labeling a generic one as not complete agent support.</summary>
    public static int WorkerLaunchers(string root, bool json) => WithApi(root, api =>
    {
        IReadOnlyList<ProviderRow> rows = api.Launchers();
        if (json)
        {
            Console.Out.WriteLine(JsonSerializer.Serialize(rows, ProtocolJson.Options));
            return Clean;
        }

        if (rows.Count == 0)
        {
            Console.Out.WriteLine("No coding-agent launcher is available to the runtime here.");
            return Clean;
        }

        foreach (ProviderRow row in rows)
        {
            Console.Out.WriteLine($"{row.Provider}  {row.Implementation}  {row.Contract}  {row.Status}{(row.Provider == "generic" ? "  (" + Vericairn.Runtime.Workers.IAgentLauncher.GenericLabel + ")" : string.Empty)}");
            foreach (ProviderSettingRow setting in row.Settings)
            {
                Console.Out.WriteLine($"  {setting.Name,-10} {(setting.Required ? "required" : setting.Default is null ? "optional" : "default " + setting.Default)}: {setting.Summary}");
            }

            foreach (string limitation in row.Limitations)
            {
                Console.Out.WriteLine($"  limitation: {limitation}");
            }
        }

        return Clean;
    });

    /// <summary>
    /// Runs a coding agent on an assignment: starts the run, then waits in the foreground until it settles and reports its usage, a
    /// Ctrl+C asking it to stop; with --detach, returns once the daemon serving the estate has launched it.
    /// </summary>
    public static int WorkerRun(string root, WorkerRunOptions options, bool json)
    {
        ArgumentNullException.ThrowIfNull(options);
        Dictionary<string, string>? settings = ParseSettings(options.Settings, out string? problem);
        if (settings is null)
        {
            return Fail(problem!);
        }

        return WithApi(root, api =>
        {
            if (options.Detach && api is not VericairnClient)
            {
                return Fail("--detach leaves the run to the daemon that serves the estate, and no daemon serves it here: start one with vericairn runtime start, or run in the foreground.");
            }

            RunResponse started = api.RunStart(new RunStartRequest(options.Assignment, options.Launcher, options.Agent, options.By, options.Repository, options.Goal, options.Branch, options.Base, options.WallMinutes, settings));
            if (!started.Ok)
            {
                return started.Problem is { Findings.Count: > 0 } rejected ? ReportOutcome(WriteOutcome.Fail(rejected), json) : Fail(started.Message);
            }

            if (!json)
            {
                Console.Out.WriteLine($"run {started.Run}  {started.Status}  {started.Workspace}");
                Console.Out.WriteLine($"  cap {Units(started.Cap)}");
                Console.Out.WriteLine("  " + started.Message);
            }

            if (options.Detach)
            {
                if (json)
                {
                    Console.Out.WriteLine(JsonSerializer.Serialize(started, ProtocolJson.Options));
                }

                return Clean;
            }

            RunRow? row = Follow(api, started.Run, options.By);
            if (json)
            {
                Console.Out.WriteLine(JsonSerializer.Serialize(row, ProtocolJson.Options));
            }
            else
            {
                PrintSettled(row);
            }

            return row?.Status == "observed-success" ? Clean : HasErrors;
        });
    }

    /// <summary>
    /// Waits in the foreground until a run settles, or no worker observes it any longer, and returns its row; a Ctrl+C asks for its stop
    /// rather than abandoning it. The run is observed by the runtime, never by this wait.
    /// </summary>
    private static RunRow? Follow(IEstateApi api, string run, string by)
    {
        int stopping = 0;
        void Interrupt(object? sender, ConsoleCancelEventArgs e)
        {
            e.Cancel = true;
            if (Interlocked.Exchange(ref stopping, 1) == 0)
            {
                ThreadPool.QueueUserWorkItem(_ => api.RunStop(run, new RunStopRequest(by, "interrupted at the command line")));
            }
        }

        Console.CancelKeyPress += Interrupt;
        try
        {
            RunRow? row = null;
            int unobserved = 0;
            while (unobserved < 5)
            {
                Thread.Sleep(1000);
                row = api.Runs().Runs.FirstOrDefault(r => r.Run == run);
                if (row is null || row.Status is "observed-success" or "known-failure" or "canceled")
                {
                    break;
                }

                unobserved = row.Observed ? 0 : unobserved + 1;
            }

            return row;
        }
        finally
        {
            Console.CancelKeyPress -= Interrupt;
        }
    }

    /// <summary>Prints a followed run as it ended, or as it was last observed.</summary>
    private static void PrintSettled(RunRow? row)
    {
        if (row is null)
        {
            return;
        }

        PrintRun(row);
        if (row.Status is "dispatched" or "not-started" or "uncertain")
        {
            Console.Out.WriteLine("  The run is no longer observed by a worker here; it stays in flight, charged at its cap, until a worker observes its end. A lost connection is not a stopped run.");
        }
    }

    /// <summary>What <c>vericairn goal step</c> was given.</summary>
    internal sealed record GoalStepOptions(string? Goal, string By, string? Repository, string[] Settings, long? WallMinutes, string? Branch, string? Base, bool Detach);

    /// <summary>
    /// Takes a goal's next step, one at a time: an agent's run starts and, without --detach, is followed until it settles; a person's step
    /// is assigned and asked; a step no principal is available for is asked of the sponsor.
    /// </summary>
    public static int GoalStep(string root, GoalStepOptions options, bool json)
    {
        ArgumentNullException.ThrowIfNull(options);
        Dictionary<string, string>? settings = ParseSettings(options.Settings, out string? problem);
        if (settings is null)
        {
            return Fail(problem!);
        }

        return WithApi(root, api =>
        {
            if (options.Detach && api is not VericairnClient)
            {
                return Fail("--detach leaves an agent's run to the daemon that serves the estate, and no daemon serves it here: start one with vericairn runtime start, or take the step in the foreground.");
            }

            string? goal = options.Goal ?? OpenGoal(api, json);
            if (goal is null)
            {
                return Clean;
            }

            GoalStepResponse step = api.GoalStep(goal, new GoalStepRequest(options.By, options.Repository, settings.Count == 0 ? null : settings, options.WallMinutes, options.Branch, options.Base));
            if (!step.Ok)
            {
                return step.Run?.Problem is { Findings.Count: > 0 } rejected ? ReportOutcome(WriteOutcome.Fail(rejected), json) : Fail(step.Message);
            }

            RunRow? row = step.Outcome == "run" && !options.Detach ? Follow(api, step.Run!.Run, options.By) : null;
            if (json)
            {
                Console.Out.WriteLine(JsonSerializer.Serialize(new { step, settled = row }, ProtocolJson.Options));
            }
            else
            {
                Console.Out.WriteLine(step.Step is null ? $"goal {step.Goal}  {step.Outcome}" : $"step {step.Step}  {step.Route}  {step.Outcome}{(step.Principal is null ? string.Empty : "  " + step.Principal)}");
                Console.Out.WriteLine("  " + step.Message);
                PrintSettled(row);
            }

            return step.Outcome != "run" || options.Detach || row?.Status == "observed-success" ? Clean : HasErrors;
        });
    }

    /// <summary>Hands a step an agent holds over to a person, and prints the stops asked of the agent's runs in flight.</summary>
    public static int WorkTakeover(string root, string assignment, string by, string? contributor, string? note, bool json) => WithApi(root, api =>
    {
        TakeoverResponse taken = api.WorkTakeover(new TakeoverRequest(assignment, by, contributor, note));
        if (!taken.Ok)
        {
            return taken.Problem is { Code: "rejected", Findings.Count: > 0 } rejected ? ReportOutcome(WriteOutcome.Fail(rejected), json) : Fail(taken.Message);
        }

        if (json)
        {
            Console.Out.WriteLine(JsonSerializer.Serialize(taken, ProtocolJson.Options));
            return Clean;
        }

        Console.Out.WriteLine($"{taken.Assignment!.Path}  {taken.Assignment.Id}  {taken.Assignment.Hash}");
        Console.Out.WriteLine("  " + taken.Message);
        return Clean;
    });

    /// <summary>Asks a run to stop and prints the facts observed; a stop that was not observed is printed as requested.</summary>
    public static int WorkerStop(string root, string run, string by, string? reason, int? grace, bool json) => WithApi(root, api =>
    {
        RunStopResponse response = api.RunStop(run, new RunStopRequest(by, reason, grace));
        if (response.Problem is not null && response.Stop is null)
        {
            return Fail(response.Message);
        }

        if (json)
        {
            Console.Out.WriteLine(JsonSerializer.Serialize(response, ProtocolJson.Options));
        }
        else
        {
            Console.Out.WriteLine($"run {response.Run}  stop {response.State}");
            if (response.Stop is not null)
            {
                PrintStop(response.Stop);
            }

            Console.Out.WriteLine("  " + response.Message);
        }

        return response.Ok ? Clean : HasErrors;
    });

    /// <summary>Lists the agent runs with their caps, usage, charges and stops, and the ledgers of their goals.</summary>
    public static int WorkerRuns(string root, bool json) => WithApi(root, api =>
    {
        RunsResponse response = api.Runs();
        if (json)
        {
            Console.Out.WriteLine(JsonSerializer.Serialize(response, ProtocolJson.Options));
            return Clean;
        }

        if (response.Runs.Count == 0)
        {
            Console.Out.WriteLine("No agent runs.");
        }

        foreach (RunRow row in response.Runs)
        {
            PrintRun(row);
        }

        foreach (LedgerRow ledger in response.Goals)
        {
            Console.Out.WriteLine($"goal {ledger.Goal}  {ledger.Status}  {ledger.Title}");
            Console.Out.WriteLine($"  budget {string.Join(", ", ledger.Units.Select(u => $"{u.Unit} {u.Spent} of {u.Budget}{(u.Currency is null ? string.Empty : " " + u.Currency)} charged, {u.Remaining} remain"))}; {ledger.InFlight} of {ledger.Concurrency} runs in flight; {ledger.AttemptsPerStep} attempts per step");
            foreach (string fence in ledger.Fenced)
            {
                Console.Out.WriteLine($"  fenced: {fence}");
            }
        }

        return Clean;
    });

    private static void PrintRun(RunRow row)
    {
        Console.Out.WriteLine($"run {row.Run}  {row.Status}  {row.Launcher} as {row.Agent}  assignment {row.Assignment}  goal {row.Goal}");
        Console.Out.WriteLine($"  workspace {row.Workspace}");
        Console.Out.WriteLine($"  cap {Units(row.Cap)}");
        Console.Out.WriteLine(row.Usage is JsonElement usage ? $"  usage {Units(usage)}" : "  usage: not settled yet, so charged at its cap");
        Console.Out.WriteLine($"  charged {Units(row.Charged)}");
        foreach (RunSignal signal in row.Signals ?? [])
        {
            Console.Out.WriteLine($"  worker {signal.Severity} {signal.Code}: {signal.Summary} ({(signal.Attention is null ? "attention recording pending" : "ask " + signal.Attention)})");
        }

        if (row.Stop is not null)
        {
            PrintStop(row.Stop);
        }

        if (row.Heartbeat is not null)
        {
            Console.Out.WriteLine(row.Observed ? $"  observed by a worker on this machine, its last heartbeat at {row.Heartbeat}" : $"  not observed by a worker now; its last heartbeat here was at {row.Heartbeat}");
        }

        if (row.Generic)
        {
            Console.Out.WriteLine("  " + Vericairn.Runtime.Workers.IAgentLauncher.GenericLabel);
        }
    }

    private static void PrintStop(RunStop stop)
    {
        Console.Out.WriteLine($"  stop requested at {stop.RequestedAt} by {stop.RequestedBy}{(stop.Reason is null ? string.Empty : ": " + stop.Reason)}");
        Console.Out.WriteLine(stop.TransmittedAt is null ? "    transmitted: not observed" : $"    transmitted at {stop.TransmittedAt}: {stop.Transmission}");
        Console.Out.WriteLine(stop.AcknowledgedAt is null ? "    acknowledged: not observed" : $"    acknowledged at {stop.AcknowledgedAt}: {stop.Acknowledgment}");
        if (stop.EscalatedAt is not null)
        {
            Console.Out.WriteLine($"    escalated at {stop.EscalatedAt}: {stop.Escalation}");
        }

        Console.Out.WriteLine(stop.ContainedAt is null ? "    contained: not observed" : $"    contained at {stop.ContainedAt}");
    }

    /// <summary>Caps, usage or charges in words: each unit and its amount, and the units not reported.</summary>
    private static string Units(JsonElement? caps)
    {
        if (caps is not JsonElement element || element.ValueKind != JsonValueKind.Object)
        {
            return "none";
        }

        var parts = new List<string>();
        foreach (JsonProperty unit in element.EnumerateObject().OrderBy(p => Array.IndexOf(Caps.UnitNames, p.Name) is int at and >= 0 ? at : Caps.UnitNames.Length))
        {
            if (unit.Name == "unreported")
            {
                List<string> names = unit.Value.ValueKind == JsonValueKind.Array ? unit.Value.EnumerateArray().Select(v => v.GetString() ?? "?").ToList() : [];
                if (names.Count > 0)
                {
                    parts.Add($"not reported: {string.Join(", ", names)} (charged at the cap)");
                }

                continue;
            }

            parts.Add(unit.Value.ValueKind == JsonValueKind.Object
                ? $"{unit.Name} {(unit.Value.TryGetProperty("amount", out JsonElement amount) ? amount.GetRawText() : "?")} {(unit.Value.TryGetProperty("unit", out JsonElement code) ? code.GetString() : string.Empty)}"
                : $"{unit.Name} {unit.Value.GetRawText()}");
        }

        return parts.Count == 0 ? "nothing" : string.Join(", ", parts);
    }

    public static int Init(string root, string name, string slug, string purpose, string operatorName, string organization, string? github, string repository, string commit, string profile, bool json)
    {
        try
        {
            var initialization = new EstateInitialization(name, slug, purpose, operatorName, organization, github, repository, commit, Profile: profile);
            RecordEnvelope operatorRecord;
            RecordEnvelope estateRecord;
            if (profile == "local-only")
            {
                PreparedEstate prepared = EstateInitializer.Prepare(root, initialization);
                string manifestPath = Path.Combine(root, EstateLayout.ManifestPath);
                Directory.CreateDirectory(Path.GetDirectoryName(manifestPath)!);
                File.WriteAllBytes(manifestPath, prepared.ManifestBytes);
                using EstateSession session = EstateSession.Open(root);
                session.Write(prepared.Operator);
                session.Write(prepared.Estate);
                EstateModel model = session.Load();
                if (model.Findings.HasErrors)
                {
                    session.Discard(prepared.Estate);
                    session.Discard(prepared.Operator);
                    File.Delete(manifestPath);
                    return ReportFindings(ToFindings(model.Findings, "rejected"), json);
                }

                (operatorRecord, estateRecord) = (prepared.Operator.Envelope, prepared.Estate.Envelope);
            }
            else
            {
                (operatorRecord, estateRecord) = EstateInitializer.Initialize(root, initialization);
            }

            if (json)
            {
                Console.Out.WriteLine(JsonSerializer.Serialize(new { @operator = operatorRecord.Id, estate = estateRecord.Id, manifest = EstateLayout.ManifestPath }));
            }
            else
            {
                Console.Out.WriteLine($"operator  {operatorRecord.Id}  {operatorRecord.ExpectedPath}");
                Console.Out.WriteLine($"estate    {estateRecord.Id}  {estateRecord.ExpectedPath}");
                Console.Out.WriteLine($"manifest  {EstateLayout.ManifestPath}");
            }

            return Clean;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException)
        {
            return Fail(exception.Message);
        }
    }

    public static int Describe(string? type)
    {
        if (type is null)
        {
            Console.Out.WriteLine($"Record types this build reads (record formats {string.Join(", ", RecordFormats.Reads)}; new records use the estate's format, at most {RecordFormats.Current}):");
            foreach (IRecordType recordType in RecordTypeRegistry.All)
            {
                Console.Out.WriteLine($"  {recordType.Name,-18} {(recordType.SinceFormat > RecordFormats.First ? $"[format {recordType.SinceFormat}] " : string.Empty)}{recordType.Summary}");
            }

            Console.Out.WriteLine();
            Console.Out.WriteLine("Reserved for later releases: " + string.Join(", ", RecordTypeRegistry.Reserved));
            Console.Out.WriteLine("Every record also has the envelope: format, type, id, at, by, body, hash, and optionally subject, supersedes, signatures.");
            return Clean;
        }

        IRecordType? found = RecordTypeRegistry.Find(type);
        if (found is null)
        {
            return Fail($"Unknown record type '{type}'.");
        }

        Console.Out.WriteLine($"{found.Name}: {found.Summary}");
        Console.Out.WriteLine(found.SubjectType is null ? (found.SubjectHashRequired ? "Subject: required, pinned to an exact hash." : "Subject: none.") : $"Subject: {found.SubjectType}{(found.SubjectHashRequired ? ", pinned to an exact hash" : string.Empty)}.");
        if (found.SinceFormat > RecordFormats.First)
        {
            Console.Out.WriteLine($"Record format: {found.SinceFormat} and later.");
        }

        Console.Out.WriteLine("Body fields:");
        foreach (FieldDescription field in found.Fields)
        {
            string since = field.SinceFormat > RecordFormats.First ? $"[format {field.SinceFormat}] " : string.Empty;
            Console.Out.WriteLine($"  {field.Name,-18} {field.Kind,-28} {(field.Required ? "required" : "optional"),-9} {since}{field.Description}");
        }

        return Clean;
    }

    public static int Format(string[] files)
    {
        int result = Clean;
        foreach (string file in files)
        {
            try
            {
                byte[] original = File.ReadAllBytes(file);
                byte[] formatted = RecordAuthor.Format(original);
                if (!formatted.AsSpan().SequenceEqual(original))
                {
                    File.WriteAllBytes(file, formatted);
                    Console.Out.WriteLine($"formatted {file}");
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or CanonicalJsonException)
            {
                Console.Error.WriteLine($"{file}: {exception.Message}");
                result = Failed;
            }
        }

        return result;
    }

    public static int Hash(string file)
    {
        try
        {
            var findings = new FindingList();
            RecordEnvelope? record = RecordParser.Parse(File.ReadAllBytes(file), file, findings, checkFormatting: false);
            if (record is null)
            {
                return ReportFindings(ToFindings(findings, file), false);
            }

            Console.Out.WriteLine(record.ComputedHash);
            return Clean;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return Fail(exception.Message);
        }
    }

    public static int Decisions(string root, string? id, bool json, CommunicationSelection? presentation = null) => WithApi(root, api =>
    {
        if (id is null)
        {
            DecisionsResponse decisions = api.Decisions();
            Console.Out.Write(json ? CanonicalJson.SerializePretty(decisions.Json) : ProtectedPresentation(api, "decision", "decision index", decisions.Text, presentation));
            return Clean;
        }

        DecisionResponse? decision = api.Decision(id);
        if (decision is null)
        {
            return Fail($"No decision has id {id}.");
        }

        Console.Out.Write(json ? CanonicalJson.SerializePretty(decision.Body) : ProtectedPresentation(api, "decision", id, decision.Text, presentation));
        return Clean;
    });

    public static int Doctor(string root)
    {
        Console.Out.WriteLine($"vericairn {VersionText()}");
        Console.Out.WriteLine($"runtime   .NET {Environment.Version} on {RuntimeInformation.OSDescription} ({RuntimeInformation.OSArchitecture})");
        Console.Out.WriteLine($"git       {Probe("git", "--version") ?? "not found on PATH"}");
        Console.Out.WriteLine($"dotnet    {Probe("dotnet", "--version") ?? "not found on PATH (needed only to build from source)"}");
        Console.Out.WriteLine(IsEstate(root) ? $"estate    {Path.GetFullPath(root)} (manifest present)" : $"estate    {Path.GetFullPath(root)} is not an estate root (no {EstateLayout.ManifestPath})");
        long? format = IsEstate(root) ? EstateManifest.Load(root, new FindingList())?.RecordFormat : null;
        Console.Out.WriteLine(format is < 2
            ? $"custody   {DeviceKeyStores.Default(enclave: false).Kind} (the default for this estate, at record format {format}, which cannot name the Secure Enclave; {DeviceKeyStores.Default().Kind} once it is at format 2); per-user state at {VericairnHome.Path()}"
            : $"custody   {DeviceKeyStores.Default().Kind} (default on this platform); per-user state at {VericairnHome.Path()}");
        if (OperatingSystem.IsMacOS())
        {
            Console.Out.WriteLine(SecureEnclaveKeyStore.Available()
                ? "enclave   Secure Enclave custody available (record format 2): a person's device asks for Touch ID or the login password at every signature unless it is created with --presence false; an agent's never asks"
                : $"enclave   Secure Enclave custody unavailable ({(SecureEnclaveCustody.HelperPath() is null ? $"no {SecureEnclaveCustody.HelperName} helper beside the command line" : "no Secure Enclave available to this process")})");
        }
        Console.Out.WriteLine($"records   reads record formats {string.Join(", ", RecordFormats.Reads)}; writes an estate's own format, at most {RecordFormats.Current}");
        if (IsEstate(root))
        {
            DaemonEndpoint? endpoint = DaemonHost.Discover(root);
            Console.Out.WriteLine(endpoint is not null && VericairnClient.IsAlive(endpoint)
                ? $"daemon    process {endpoint.ProcessId} serves this estate since {endpoint.StartedAt} ({endpoint.Kind}{(endpoint.Loopback is null ? string.Empty : ", loopback " + endpoint.Loopback.Port)})"
                : "daemon    none; commands run the embedded runtime");
        }

        return Clean;
    }

    public static int Version()
    {
        Console.Out.WriteLine(VersionText());
        return Clean;
    }

    public static int RuntimeServe(string root, bool loopback, bool detached = false)
    {
        try
        {
            if (!IsEstate(root))
            {
                return Fail(NotAnEstate(root));
            }

            if (detached)
            {
                // Started by 'runtime start': whoever started that command may be reading its output to the end, so the daemon
                // writes to its own log beside the live store and never to the streams it was given, which that command closes.
                // The log is opened before anything else can fail, so every failure is written where 'runtime start' reads it.
                string log = DaemonLogPath(root);
                Directory.CreateDirectory(Path.GetDirectoryName(log)!);
                var writer = new StreamWriter(new FileStream(log, FileMode.Append, FileAccess.Write, FileShare.ReadWrite)) { AutoFlush = true };
                Console.SetOut(writer);
                Console.SetError(writer);
            }

            DaemonEndpoint? existing = DaemonHost.Discover(root);
            if (existing is not null && VericairnClient.IsAlive(existing))
            {
                return Fail($"Daemon {existing.ProcessId} already serves this estate; one process owns an estate's live state.");
            }

            using var stop = new CancellationTokenSource();
            Console.CancelKeyPress += (_, e) =>
            {
                e.Cancel = true;
                stop.Cancel();
            };
            using PosixSignalRegistration? hangup = IgnoreHangup();
            var options = new DaemonOptions { Log = line => Console.Out.WriteLine($"{RecordParser.FormatTimestamp(DateTimeOffset.UtcNow)} {line}") };
            DaemonHost host = DaemonHost.StartAsync(root, loopback: loopback, options: options).GetAwaiter().GetResult();
            Console.Out.WriteLine($"vericairnd {VersionText()} serving {host.Service.Root}");
            Console.Out.WriteLine($"  {host.Endpoint.Kind}  {host.Endpoint.Address}");
            if (host.Endpoint.Loopback is LoopbackEndpoint published)
            {
                Console.Out.WriteLine($"  loopback  https://{published.Host}:{published.Port} pinned to {published.Certificate}");
            }

            if (host.Endpoint.LocalNetwork is LocalNetworkEndpoint network)
            {
                Console.Out.WriteLine($"  local network  {LocalNetworkAddress(network)}, mutual TLS with paired devices; runtime identity {network.Identity}");
            }

            Console.Out.WriteLine($"  process {host.Endpoint.ProcessId}; stop with Ctrl+C or 'vericairn runtime stop'");
            host.WaitForShutdownAsync(stop.Token).GetAwaiter().GetResult();
            host.DisposeAsync().AsTask().GetAwaiter().GetResult();
            Console.Out.WriteLine("vericairnd stopped");
            return Clean;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException or System.Security.Cryptography.CryptographicException)
        {
            return Fail(exception.Message);
        }
        catch (Exception exception) when (detached)
        {
            // Nobody reads a detached daemon's streams: an unexpected failure goes to its log, where 'runtime start' finds it.
            return Fail($"the daemon stopped on an unexpected {exception.GetType().Name}: {exception.Message}");
        }
    }

    /// <summary>The name of the daemon's log beside the estate's live store.</summary>
    internal const string DaemonLogName = "daemon.log";

    /// <summary>Where a daemon started by 'runtime start' writes its output and its failures for an estate on this machine.</summary>
    internal static string DaemonLogPath(string root) => Path.Combine(Path.GetDirectoryName(DaemonHost.EndpointFilePath(root))!, DaemonLogName);

    public static int RuntimeStart(string root, bool json)
    {
        try
        {
            if (!IsEstate(root))
            {
                return Fail(NotAnEstate(root));
            }

            DaemonEndpoint? existing = DaemonHost.Discover(root);
            if (existing is not null && VericairnClient.IsAlive(existing))
            {
                return RuntimeStatus(root, json);
            }

            // The daemon's log is opened here first: a log that cannot be written stops the start with its reason, and a daemon
            // that fails before serving is explained from what it wrote there since this point.
            string log = DaemonLogPath(root);
            long logStart;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(log)!);
                using var probe = new FileStream(log, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
                logStart = probe.Length;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                return Fail($"The daemon's log {log} cannot be written, so no daemon was started: {exception.Message}");
            }

            // The daemon gets streams of its own and writes to its log: it never inherits this command's output, which a caller
            // (Workbench, a script capturing the output) may be reading to the end while the daemon runs for as long as it serves.
            IReadOnlyList<string> launcher = ServiceInstaller.CurrentLauncher();
            var info = new ProcessStartInfo(launcher[0]) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true };
            foreach (string argument in launcher.Skip(1).Concat(["--estate", Path.GetFullPath(root), "runtime", "serve", "--detached"]))
            {
                info.ArgumentList.Add(argument);
            }

            NativeMethods.KeepStandardHandlesFromChildren();
            using Process? process = Process.Start(info);
            if (process is null)
            {
                return Fail("The daemon process did not start.");
            }

            process.StandardInput.Close();
            process.StandardOutput.Close();
            process.StandardError.Close();

            var deadline = DateTimeOffset.UtcNow.AddSeconds(30);
            while (DateTimeOffset.UtcNow < deadline)
            {
                DaemonEndpoint? endpoint = DaemonHost.Discover(root);
                if (endpoint is not null && endpoint.ProcessId == process.Id && VericairnClient.IsAlive(endpoint))
                {
                    return RuntimeStatus(root, json);
                }

                if (process.HasExited)
                {
                    return Fail($"The daemon exited with code {process.ExitCode} before serving{LogSince(log, logStart)}. Its log is {log}.");
                }

                Thread.Sleep(200);
            }

            return Fail($"The daemon did not start serving within 30 seconds{LogSince(log, logStart)}. Its log is {log}.");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return Fail(exception.Message);
        }
    }

    /// <summary>What the daemon wrote to its log after an offset, quoted briefly for a failed start, or nothing.</summary>
    private static string LogSince(string log, long start)
    {
        try
        {
            using var stream = new FileStream(log, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            if (stream.Length <= start)
            {
                return string.Empty;
            }

            stream.Seek(start, SeekOrigin.Begin);
            using var reader = new StreamReader(stream);
            string[] lines = reader.ReadToEnd().Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            string said = string.Join(" ", lines.TakeLast(5).Select(line => line.StartsWith("error: ", StringComparison.Ordinal) ? line["error: ".Length..] : line)).TrimEnd('.', ' ');
            return said.Length == 0 ? string.Empty : ": " + (said.Length > 1000 ? "..." + said[^1000..] : said);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return string.Empty;
        }
    }

    public static int RuntimeStop(string root, bool json)
    {
        if (!IsEstate(root))
        {
            return Fail(NotAnEstate(root));
        }

        DaemonEndpoint? endpoint = DaemonHost.Discover(root);
        if (endpoint is null || !VericairnClient.IsAlive(endpoint))
        {
            Console.Out.WriteLine(json ? "{\"stopped\":null}" : "no daemon is serving this estate");
            return Clean;
        }

        try
        {
            using (var client = new VericairnClient(endpoint))
            {
                client.Shutdown();
            }

            var deadline = DateTimeOffset.UtcNow.AddSeconds(15);
            while (DateTimeOffset.UtcNow < deadline && VericairnClient.IsAlive(endpoint))
            {
                Thread.Sleep(200);
            }

            bool stopped = !VericairnClient.IsAlive(endpoint);
            Console.Out.WriteLine(json ? JsonSerializer.Serialize(new { stopped = stopped ? endpoint.ProcessId : (int?)null, process = endpoint.ProcessId }) : $"asked daemon {endpoint.ProcessId} to stop; {(stopped ? "stopped" : "still answering")}");
            return stopped ? Clean : HasErrors;
        }
        catch (Exception exception) when (exception is HttpRequestException or IOException or InvalidOperationException)
        {
            return Fail(exception.Message);
        }
    }

    public static int RuntimeStatus(string root, bool json)
    {
        if (!IsEstate(root))
        {
            return Fail(NotAnEstate(root));
        }

        DaemonEndpoint? endpoint = DaemonHost.Discover(root);
        bool alive = endpoint is not null && VericairnClient.IsAlive(endpoint);
        string? configured = null;
        try
        {
            configured = RuntimeSettings.Load(Path.GetDirectoryName(DaemonHost.EndpointFilePath(root))!).LocalNetwork;
        }
        catch (InvalidOperationException exception)
        {
            Console.Error.WriteLine("warning: " + exception.Message);
        }

        if (json)
        {
            Console.Out.WriteLine(JsonSerializer.Serialize(new { mode = alive ? "daemon" : "embedded", daemon = alive ? endpoint : null, localNetwork = configured }, ProtocolJson.Options));
        }
        else if (alive)
        {
            Console.Out.WriteLine($"daemon {endpoint!.ProcessId} serves {endpoint.Root} since {endpoint.StartedAt}");
            Console.Out.WriteLine($"  {endpoint.Kind}  {endpoint.Address}");
            if (endpoint.Loopback is LoopbackEndpoint published)
            {
                Console.Out.WriteLine($"  loopback  https://{published.Host}:{published.Port} pinned to {published.Certificate}");
            }

            if (endpoint.LocalNetwork is LocalNetworkEndpoint network)
            {
                Console.Out.WriteLine($"  local network  {LocalNetworkAddress(network)}, mutual TLS with paired devices; runtime identity {network.Identity}");
            }
        }
        else
        {
            Console.Out.WriteLine("no daemon is serving this estate; commands run the embedded runtime");
            if (configured is not null)
            {
                Console.Out.WriteLine($"  the local-network profile is configured at {configured} and served from the runtime's next start");
            }
        }

        return Clean;
    }

    public static int RuntimeInstall(string root, bool planOnly, bool json)
    {
        try
        {
            if (!IsEstate(root))
            {
                return Fail(NotAnEstate(root));
            }

            ServicePlan plan = ServiceInstaller.PlanHere(root);
            if (planOnly)
            {
                PrintPlan(plan, json);
                return Clean;
            }

            IReadOnlyList<string> log = ServiceInstaller.Install(plan);
            if (json)
            {
                Console.Out.WriteLine(JsonSerializer.Serialize(new { installed = plan.Label, unit = plan.UnitPath, log }));
            }
            else
            {
                foreach (string line in log)
                {
                    Console.Out.WriteLine(line);
                }

                Console.Out.WriteLine($"installed {plan.Label}: the runtime starts at login and restarts after failure; remove it with 'vericairn runtime uninstall'.");
            }

            return Clean;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException or PlatformNotSupportedException or System.ComponentModel.Win32Exception)
        {
            return Fail(exception.Message);
        }
    }

    public static int RuntimeUninstall(string root, bool json)
    {
        try
        {
            if (!IsEstate(root))
            {
                return Fail(NotAnEstate(root));
            }

            ServicePlan plan = ServiceInstaller.PlanHere(root);
            IReadOnlyList<string> log = ServiceInstaller.Uninstall(plan);
            if (json)
            {
                Console.Out.WriteLine(JsonSerializer.Serialize(new { uninstalled = plan.Label, log }));
            }
            else
            {
                foreach (string line in log)
                {
                    Console.Out.WriteLine(line);
                }

                Console.Out.WriteLine($"uninstalled {plan.Label}");
            }

            return Clean;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException or PlatformNotSupportedException or System.ComponentModel.Win32Exception)
        {
            return Fail(exception.Message);
        }
    }

    public static int Upgrade(string root, string version, string? by, bool check, bool json) => WithApi(root, api =>
    {
        (UpgradeResponse? plan, Problem? problem) = api.Upgrade(new UpgradeRequest(version, by, check));
        if (plan is null)
        {
            if (problem is not null && problem.Findings.Count > 0)
            {
                Console.Error.WriteLine(problem.Message);
                return ReportFindings(new FindingsResponse("not upgraded", problem.Findings.Count(f => f.Severity == "error"), problem.Findings.Count(f => f.Severity == "warning"), problem.Findings), json);
            }

            return Fail(problem?.Message ?? "refused");
        }

        if (json)
        {
            Console.Out.WriteLine(JsonSerializer.Serialize(plan, ProtocolJson.Options));
        }
        else
        {
            Console.Out.WriteLine($"{(plan.Applied ? "upgraded" : "would upgrade")} {plan.FromPin} -> {plan.ToPin}; record format {plan.FromFormat} -> {plan.ToFormat}; migrations: {(plan.Migrations.Count == 0 ? "none" : string.Join(", ", plan.Migrations))}; release {plan.Release}");
            foreach ((string platform, string hash) in plan.Artifacts.OrderBy(a => a.Key, StringComparer.Ordinal))
            {
                Console.Out.WriteLine($"  {platform}  {hash}");
            }

            if (plan.Anchored is { Count: > 0 } anchored)
            {
                Console.Out.WriteLine($"{(plan.Applied ? "anchored" : "anchors")} the admissions recorded without a signature, which count after the move only because the signed migration lists them: {string.Join(", ", anchored)}");
            }

            if (plan.Migration is not null)
            {
                Console.Out.WriteLine($"{plan.Migration.Path}  {plan.Migration.Id}  {plan.Migration.Hash}");
            }
        }

        return Clean;
    });

    public static int ReleaseList(string root, bool json) => WithApi(root, api =>
    {
        IReadOnlyList<ReleaseRow> rows = api.Releases();
        if (json)
        {
            Console.Out.WriteLine(JsonSerializer.Serialize(rows, ProtocolJson.Options));
        }
        else
        {
            foreach (ReleaseRow row in rows)
            {
                Console.Out.WriteLine($"{row.Version}  {row.Status}  {row.Commit}  {row.Id}  {row.Signing}");
            }
        }

        return Clean;
    });

    public static int Export(string root, string output, string? by, bool json) => WithApi(root, api =>
    {
        (ExportResponse? export, Problem? problem) = api.Export(new ExportRequest(Path.GetFullPath(output), by));
        if (export is null)
        {
            return Fail(problem?.Message ?? "refused");
        }

        if (export.HadErrors)
        {
            Console.Error.WriteLine("The estate has errors; exported anyway so that it can be inspected elsewhere.");
        }

        if (json)
        {
            Console.Out.WriteLine(JsonSerializer.Serialize(new { export.Id, export.Estate, export.CreatedAt, export.Profile, export.Records, export.Objects, export.Documents, export.InventoryHash, output = export.Output }));
        }
        else
        {
            Console.Out.WriteLine($"export {export.Id} of estate {export.Estate}: {export.Records} records, {export.Objects} objects, {export.Documents} documents; inventory {export.InventoryHash}");
            Console.Out.WriteLine("Carries records and bytes only: no keys, sessions, credentials or authority travel with it.");
        }

        return Clean;
    });

    public static int Restore(string from, string to, bool json)
    {
        try
        {
            (ExportSummary? verified, FindingList findings) = EstateExport.Verify(from);
            if (verified is null || findings.HasErrors)
            {
                Console.Error.WriteLine("Nothing restored: the export does not verify against its inventory.");
                return ReportFindings(ToFindings(findings, "rejected"), json);
            }

            ExportSummary summary = EstateExport.Restore(from, to);
            using EstateSession session = EstateSession.Open(to);
            EstateModel model = session.Load();
            FindingList checks = EstateChecks.Run(model);
            if (json)
            {
                Console.Out.WriteLine(JsonSerializer.Serialize(new { summary.Id, summary.Estate, summary.Records, summary.Objects, summary.Documents, restoredTo = Path.GetFullPath(to), errors = model.Findings.Errors + checks.Errors }));
            }
            else
            {
                Console.Out.WriteLine($"restored export {summary.Id} of estate {summary.Estate} into {Path.GetFullPath(to)}: {summary.Records} records, {summary.Objects} objects, {summary.Documents} documents");
                Console.Out.WriteLine("Restored records carry no authority: devices stay where their keys are, and a device held here must be admitted before this copy can author.");
                Console.Out.Write(Recovery.Inspect(model, checks));
            }

            return model.Findings.HasErrors || checks.HasErrors ? HasErrors : Clean;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException or JsonException)
        {
            return Fail(exception.Message);
        }
    }

    public static int Reconcile(string root, bool json) => WithApi(root, api =>
    {
        IReadOnlyList<ReconcileTask> tasks = api.Reconcile();
        if (json)
        {
            Console.Out.WriteLine(JsonSerializer.Serialize(tasks.Select(t => new { t.Code, t.Location, t.Message })));
        }
        else if (tasks.Count == 0)
        {
            Console.Out.WriteLine("Nothing to reconcile.");
        }
        else
        {
            foreach (ReconcileTask task in tasks)
            {
                Console.Out.WriteLine($"{task.Code}  {task.Location}: {task.Message}");
            }

            // A batch stopped part-way says in its own words what settles it; divergent revisions are settled by a reconciliation.
            if (tasks.Any(t => t.Code != "batch.incomplete"))
            {
                Console.Out.WriteLine("Settle divergent revisions with a reconciliation record (kept, withdrawn, note); nothing is merged silently and nothing is deleted.");
            }
        }

        return tasks.Count == 0 ? Clean : HasErrors;
    });

    public static int RecoverInspect(string root) => WithApi(root, api =>
    {
        Console.Out.Write(api.RecoverInspect());
        return Clean;
    });

    public static int RecoverCompare(string root, string other)
    {
        try
        {
            Console.Out.Write(Recovery.Compare(LoadCopy(root), "this copy", LoadCopy(other), "the other copy"));
            return Clean;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            return Fail(exception.Message);
        }
    }

    public static int ObjectsPut(string root, string file, bool json)
    {
        byte[] bytes;
        try
        {
            bytes = File.ReadAllBytes(file);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return Fail(exception.Message);
        }

        return WithApi(root, api =>
        {
            (ObjectStored? stored, Problem? problem) = api.ObjectPut(bytes);
            if (stored is null)
            {
                return Fail(problem?.Message ?? "refused");
            }

            Console.Out.WriteLine(json ? JsonSerializer.Serialize(new { hash = stored.Hash, bytes = stored.Bytes, store = stored.Store }) : $"{stored.Hash}  {stored.Bytes} bytes  ({stored.Store})");
            return Clean;
        });
    }

    public static int ObjectsList(string root, bool json) => WithApi(root, api =>
    {
        IReadOnlyList<ObjectRow> rows = api.Objects();
        if (json)
        {
            Console.Out.WriteLine(JsonSerializer.Serialize(rows.Select(r => r.Hash)));
        }
        else
        {
            foreach (ObjectRow row in rows)
            {
                Console.Out.WriteLine(row.Hash);
            }
        }

        return Clean;
    });

    public static int ObjectsVerify(string root, bool json) => WithApi(root, api => ReportFindings(api.ObjectsVerify(), json));

    public static int DevicesCreate(string root, string principal, string name, string? custody, bool? presence, string[] profiles, bool json) =>
        WithApi(root, api => ReportOutcome(api.DeviceCreate(new DeviceCreateRequest(principal, name, custody, profiles.Length == 0 ? null : profiles, presence)), json));

    public static int DevicesList(string root, bool json) => WithApi(root, api =>
    {
        IReadOnlyList<DeviceRow> rows = api.Devices();
        if (json)
        {
            Console.Out.WriteLine(JsonSerializer.Serialize(rows.Select(r => new { id = r.Id, principal = r.Principal, principalName = r.PrincipalName, name = r.Name, platform = r.Platform, custody = r.Custody, presence = r.Presence, purpose = r.Purpose, admission = r.Admission, heldHere = r.HeldHere, profiles = r.Profiles })));
        }
        else
        {
            foreach (DeviceRow row in rows)
            {
                string presence = row.Presence is null ? string.Empty : row.Presence == "required" ? "  presence required" : "  no presence";
                string purpose = row.Purpose == "recovery" ? "  recovery" : string.Empty;
                string profiles = row.Profiles is { Count: > 0 } ? "  profiles " + string.Join(", ", row.Profiles) : string.Empty;
                Console.Out.WriteLine($"{row.Id}  {row.PrincipalName}  {row.Name}  {row.Platform}  {row.Custody}{presence}{purpose}  {row.Admission}{(row.HeldHere ? "  key held on this machine" : string.Empty)}{profiles}");
            }
        }

        return Clean;
    });

    public static int DevicesAdmit(string root, string deviceId, string by, string note, string? recovery, bool passphraseStdin, bool json)
    {
        string? passphrase = null;
        if (recovery is not null && (passphrase = Passphrase(passphraseStdin, confirm: false)) is null)
        {
            return Failed;
        }

        return WithApi(root, api => ReportOutcome(api.DeviceAdmit(new AdmitRequest(deviceId, by, note, recovery is null ? null : Path.GetFullPath(recovery), passphrase)), json));
    }

    public static int DevicesRevoke(string root, string deviceId, string by, string reason, string? recovery, bool passphraseStdin, bool json)
    {
        string? passphrase = null;
        if (recovery is not null && (passphrase = Passphrase(passphraseStdin, confirm: false)) is null)
        {
            return Failed;
        }

        return WithApi(root, api => ReportOutcome(api.DeviceRevoke(new RevokeRequest(deviceId, by, reason, recovery is null ? null : Path.GetFullPath(recovery), passphrase)), json));
    }

    public static int DevicesRecovery(string root, string principal, string name, string output, bool passphraseStdin, bool json)
    {
        string? passphrase = Passphrase(passphraseStdin, confirm: true);
        if (passphrase is null)
        {
            return Failed;
        }

        string path = Path.GetFullPath(output);
        int code = WithApi(root, api => ReportOutcome(api.DeviceRecovery(new DeviceRecoveryRequest(principal, name, path, passphrase)), json));
        if (code == 0 && !json)
        {
            Console.Out.WriteLine($"The recovery key is in {path}, encrypted with your passphrase. Move it off this machine now and keep the passphrase apart from it.");
        }

        return code;
    }

    /// <summary>A passphrase from the first line of standard input, or typed at the terminal without echo; null, with the reason written, when there is none.</summary>
    private static string? Passphrase(bool fromStandardInput, bool confirm)
    {
        if (fromStandardInput)
        {
            string? line = Console.In.ReadLine();
            if (string.IsNullOrEmpty(line))
            {
                Console.Error.WriteLine("Standard input holds no passphrase.");
                return null;
            }

            return line;
        }

        if (Console.IsInputRedirected)
        {
            Console.Error.WriteLine("A passphrase is typed at a terminal, or given on standard input with --passphrase-stdin.");
            return null;
        }

        string first = ReadHidden("Passphrase: ");
        if (confirm && ReadHidden("Passphrase again: ") != first)
        {
            Console.Error.WriteLine("The two passphrases differ; nothing was made.");
            return null;
        }

        return first;
    }

    private static string ReadHidden(string prompt)
    {
        Console.Error.Write(prompt);
        var text = new StringBuilder();
        while (true)
        {
            ConsoleKeyInfo key = Console.ReadKey(intercept: true);
            if (key.Key == ConsoleKey.Enter)
            {
                break;
            }

            if (key.Key == ConsoleKey.Backspace)
            {
                text.Length = Math.Max(0, text.Length - 1);
            }
            else if (!char.IsControl(key.KeyChar))
            {
                text.Append(key.KeyChar);
            }
        }

        Console.Error.WriteLine();
        return text.ToString();
    }

    public static int DocsLiving(string root, bool json) => WithApi(root, api =>
    {
        IReadOnlyList<DocumentRow> rows = api.Documents();
        if (json)
        {
            Console.Out.WriteLine(JsonSerializer.Serialize(rows, ProtocolJson.Options));
            return Clean;
        }

        if (rows.Count == 0)
        {
            Console.Out.WriteLine("No living documents are declared; declare one with vericairn docs declare.");
        }

        foreach (DocumentRow row in rows)
        {
            Console.Out.WriteLine($"{row.Path}  {row.State}{(row.Level is null ? string.Empty : " at " + row.Level)}  {row.Title}  declaration {row.Declaration}{(row.Clearance is null ? "  never cleared" : "  cleared by " + row.Clearance)}");
            foreach (DocumentReason reason in row.Reasons)
            {
                Console.Out.WriteLine($"  {reason.On} ({reason.Level}): {reason.Summary}");
            }

            foreach (string missing in row.Unavailable)
            {
                Console.Out.WriteLine($"  unavailable: {missing} is provided by no installed pack");
            }

            foreach (string note in row.Notes)
            {
                Console.Out.WriteLine($"  note: {note}");
            }
        }

        return Clean;
    });

    public static int DocsReview(string root, string path, string by, string outcome, string summary, string[] findings, string? commit, string? independence, bool json) => WithApi(root, api =>
    {
        // A document is named by its path; the estate's own file unless a commit says it is in another repository.
        DocumentRow? row = api.Documents().FirstOrDefault(d => d.Path == path && (commit is null ? d.Repository is null : d.Repository is not null));
        if (row is null)
        {
            return Fail($"No living document {path} is declared{(commit is null ? " in the estate's own repository" : " in another repository")}; declare it with vericairn docs declare.");
        }

        var parsed = new List<DocumentFinding>();
        foreach (string finding in findings)
        {
            int colon = finding.IndexOf(':', StringComparison.Ordinal);
            string severity = colon > 0 ? finding[..colon] : string.Empty;
            if (severity is not ("error" or "warning" or "info"))
            {
                return Fail($"A finding is severity:summary, with severity error, warning or info: '{finding}'.");
            }

            parsed.Add(new DocumentFinding(severity, finding[(colon + 1)..].Trim()));
        }

        return ReportOutcome(api.DocumentReview(row.Declaration, new DocumentReviewRequest(by, outcome, summary, parsed, commit, independence)), json);
    });

    public static int DocsDeclare(string root, string by, string bodyPath, string? supersedes, bool json) =>
        WithBody(bodyPath, body => WithApi(root, api => ReportOutcome(api.NewRecord(new NewRecordRequest("living-document", by, body, null, supersedes)), json)));

    /// <summary>Rewrites an estate document's generated regions, and a generated document, from their installed generators; nothing outside the markers changes.</summary>
    public static int DocsRegenerate(string root, string path, bool json)
    {
        try
        {
            EstateModel model = LoadCopy(root);
            RecordEnvelope? declaration = model.Current("living-document").FirstOrDefault(d => EstateModel.BodyString(d, "path") == path && EstateModel.BodyString(d, "repository") is null);
            if (declaration is null)
            {
                return Fail($"No living document {path} is declared in the estate's own repository.");
            }

            string file = Path.Combine(root, path.Replace('/', Path.DirectorySeparatorChar));
            string text = File.Exists(file) ? File.ReadAllText(file).Replace("\r\n", "\n", StringComparison.Ordinal) : string.Empty;
            var rewritten = new List<string>();
            var missing = new List<string>();
            foreach ((string id, string generator, IReadOnlyList<string> inputs) in DocumentChecks.Regions(declaration))
            {
                if (DocumentGenerators.Find(generator)?.Generate(model, id, inputs) is not string fresh)
                {
                    missing.Add($"{id} ({generator} is not installed here)");
                    continue;
                }

                if (GeneratedRegions.Replace(text, id, fresh) is not string replaced)
                {
                    missing.Add($"{id} (its markers {GeneratedRegions.Start(id)} and {GeneratedRegions.End(id)} are not in place)");
                    continue;
                }

                if (replaced != text)
                {
                    rewritten.Add(id);
                    text = replaced;
                }
            }

            if (EstateModel.BodyString(declaration, "kind") == "generated" && DocumentChecks.Generator(declaration) is (string whole, IReadOnlyList<string> wholeInputs)
                && DocumentGenerators.Find(whole)?.Generate(model, null, wholeInputs) is string document && document.Replace("\r\n", "\n", StringComparison.Ordinal) != text)
            {
                text = document.Replace("\r\n", "\n", StringComparison.Ordinal);
                rewritten.Add("the whole document");
            }

            if (rewritten.Count > 0)
            {
                File.WriteAllText(file, text);
            }

            if (json)
            {
                Console.Out.WriteLine(JsonSerializer.Serialize(new { path, rewritten, missing }));
            }
            else
            {
                Console.Out.WriteLine(rewritten.Count == 0 ? $"{path}: every generated region is current." : $"{path}: rewrote {string.Join(", ", rewritten)}.");
                foreach (string gap in missing)
                {
                    Console.Error.WriteLine($"{path}: not regenerated: {gap}");
                }
            }

            return missing.Count == 0 ? Clean : Failed;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException or JsonException)
        {
            return Fail(exception.Message);
        }
    }

    public static int DevicesRename(string root, string deviceId, string name, bool json) =>
        WithApi(root, api => ReportOutcome(api.DeviceRename(new DeviceRenameRequest(deviceId, name)), json));

    public static int DevicesRotate(string root, string deviceId, string? custody, bool? presence, bool json) =>
        WithApi(root, api => ReportOutcome(api.DeviceRotate(new DeviceRotateRequest(deviceId, custody, presence)), json));

    /// <summary>Issues a pairing code through the daemon serving the estate, which holds pending codes in its memory and serves the exchange.</summary>
    public static int DevicesPair(string root, string principal, string by, string[] profiles, bool json) => WithDaemon(root, "pairing", client =>
    {
        (PairingIssued? issued, Problem? problem) = client.IssuePairing(new PairingIssueRequest(principal, by, profiles.Length == 0 ? null : profiles));
        if (issued is null)
        {
            return Fail(problem?.Message ?? "No pairing code was issued.");
        }

        if (json)
        {
            Console.Out.WriteLine(JsonSerializer.Serialize(issued, ProtocolJson.Options));
            return Clean;
        }

        // A runtime listening on every interface has no one address to give: the person pairing names this machine's.
        string address = System.Net.IPEndPoint.TryParse(issued.Address, out System.Net.IPEndPoint? at) && (at.Address.Equals(System.Net.IPAddress.Any) || at.Address.Equals(System.Net.IPAddress.IPv6Any))
            ? $"<this machine's address>:{at.Port}"
            : issued.Address;
        Console.Out.WriteLine($"pairing code  {issued.Code}");
        Console.Out.WriteLine($"  for {issued.PrincipalName ?? issued.Principal} ({issued.Principal}), permitted {string.Join(", ", issued.Profiles)}");
        Console.Out.WriteLine($"  valid until {issued.ExpiresAt}, for one device and {issued.Attempts} attempts; show it only to the person pairing");
        Console.Out.WriteLine($"  runtime identity {issued.Identity}");
        Console.Out.WriteLine($"on the new device: vericairn connections add {address} --code {issued.Code} --name <device name>");
        return Clean;
    });

    /// <summary>Lists the writes paired devices made over the local network, from the ledger beside the live store; no daemon is needed.</summary>
    public static int DevicesWrites(string root, string? device, bool json)
    {
        try
        {
            if (!IsEstate(root))
            {
                return Fail(NotAnEstate(root));
            }

            List<LocalNetworkWrite> writes = [.. LocalNetworkLedger.Read(Path.GetDirectoryName(DaemonHost.EndpointFilePath(root))!).Where(w => device is null || w.Device == device)];
            if (json)
            {
                Console.Out.WriteLine(JsonSerializer.Serialize(writes, ProtocolJson.Options));
                return Clean;
            }

            if (writes.Count == 0)
            {
                Console.Out.WriteLine(device is null ? "no paired device has written over the local network through this machine's runtime" : $"device {device} has written nothing over the local network through this machine's runtime");
            }

            foreach (LocalNetworkWrite write in writes)
            {
                string outcome = write.State switch
                {
                    "interrupted" => write.Candidates is { Count: > 0 } candidates ? $"interrupted, the runtime stopped before its outcome was kept; it may have written {string.Join(", ", candidates)}" : "interrupted, the runtime stopped before its outcome was kept; its principal wrote nothing since",
                    "pending" => "pending, no outcome kept yet",
                    "failed" => "failed, nothing written",
                    _ => "written",
                };
                Console.Out.WriteLine($"{write.At}  device {write.Device} for {write.Principal}  {write.Operation}  {outcome}{(write.Record is null ? string.Empty : $"  {write.Record}{(write.Hash is null ? string.Empty : "  " + write.Hash)}")}");
            }

            return Clean;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            return Fail(exception.Message);
        }
    }

    public static int RuntimeConfigure(string root, string? localNetwork, bool allowPublic, bool json)
    {
        try
        {
            if (!IsEstate(root))
            {
                return Fail(NotAnEstate(root));
            }

            if (allowPublic && localNetwork is null)
            {
                return Fail("--allow-public allows the address --local-network gives; give both.");
            }

            string directory = Path.GetDirectoryName(DaemonHost.EndpointFilePath(root))!;
            RuntimeSettings settings;
            string? exposure = null;
            if (localNetwork is null)
            {
                settings = RuntimeSettings.Load(directory);
            }
            else
            {
                string? address = null;
                if (!localNetwork.Equals("off", StringComparison.OrdinalIgnoreCase))
                {
                    if (!RuntimeSettings.TryParseAddress(localNetwork, out System.Net.IPEndPoint? endpoint, out string? problem))
                    {
                        return Fail(problem!);
                    }

                    // The profile is for a private network: a wildcard or public address is the operator's explicit choice.
                    exposure = RuntimeSettings.Exposure(endpoint!.Address);
                    if (exposure is not null && !allowPublic)
                    {
                        return Fail($"{endpoint} is {exposure}, and the local-network profile is for a private network: give this machine's address on it (in 10/8, 172.16/12, 192.168/16, link-local or fc00::/7), or add --allow-public to listen there anyway. Nothing was changed.");
                    }

                    address = endpoint.ToString();
                }

                settings = new RuntimeSettings(address, AllowPublic: exposure is not null);
                settings.Save(directory);
            }

            DaemonEndpoint? running = DaemonHost.Discover(root);
            bool alive = running is not null && VericairnClient.IsAlive(running);
            string? served = running?.LocalNetwork is LocalNetworkEndpoint lan ? new System.Net.IPEndPoint(System.Net.IPAddress.Parse(lan.Host), lan.Port).ToString() : null;
            if (json)
            {
                Console.Out.WriteLine(JsonSerializer.Serialize(new { localNetwork = settings.LocalNetwork, allowPublic = settings.AllowPublic, served = alive ? served : null, restart = alive && localNetwork is not null }, ProtocolJson.Options));
                return Clean;
            }

            Console.Out.WriteLine(settings.LocalNetwork is null
                ? "the local-network profile is off: the runtime listens on its local socket and loopback only"
                : $"the runtime serves the local-network profile at {settings.LocalNetwork}, to devices that pair with it, from its next start");
            if (settings.AllowPublic)
            {
                Console.Out.WriteLine("  allowed with --allow-public: a wildcard or public address, where every device that reaches it may try to pair");
            }
            if (alive)
            {
                Console.Out.WriteLine(served is null ? $"  daemon {running!.ProcessId} serves no local-network profile now" : $"  daemon {running!.ProcessId} serves it now at {served}");
                if (localNetwork is not null)
                {
                    Console.Out.WriteLine("  the running daemon keeps its listeners until it restarts: 'vericairn runtime stop', then 'vericairn runtime start'");
                }
            }

            return Clean;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            return Fail(exception.Message);
        }
    }

    public static int RuntimeRotate(string root, bool compromised, bool identity, string? reason, bool json)
    {
        try
        {
            if (!IsEstate(root))
            {
                return Fail(NotAnEstate(root));
            }

            DaemonEndpoint? endpoint = DaemonHost.Discover(root);
            if (endpoint is not null && VericairnClient.IsAlive(endpoint))
            {
                using var client = new VericairnClient(endpoint);
                (CertificateRotated? rotated, Problem? problem) = client.RotateCertificate(new CertificateRotateRequest(compromised, reason, identity));
                if (rotated is null)
                {
                    return Fail(problem?.Message ?? "The certificate was not rotated.");
                }

                if (json)
                {
                    Console.Out.WriteLine(JsonSerializer.Serialize(rotated, ProtocolJson.Options));
                    return Clean;
                }

                Console.Out.WriteLine($"runtime certificate {rotated.Certificate}, until {rotated.NotAfter}, replaced {rotated.Previous}");
                Console.Out.WriteLine(rotated.Identity is null
                    ? "  paired devices keep working: they pin the runtime identity, which signs the certificate in use"
                    : $"  runtime identity replaced by {rotated.Identity}: every paired device pairs again");
                if (rotated.Compromised)
                {
                    Console.Out.WriteLine($"  after a compromise: {rotated.Closed} open connections were closed, and the old certificate is refused by every client");
                }

                return Clean;
            }

            // No daemon: the files are replaced now and the next start serves them; nothing is open to close.
            string directory = Path.GetDirectoryName(DaemonHost.EndpointFilePath(root))!;
            string? replacedIdentity = null;
            if (identity)
            {
                if (!File.Exists(Path.Combine(directory, RuntimeIdentity.FileName)))
                {
                    return Fail("This runtime has no identity to replace: it is made when the local-network profile is first served.");
                }

                replacedIdentity = RuntimeIdentity.LoadOrCreate(directory).Replace(directory).Fingerprint;
            }

            using System.Security.Cryptography.X509Certificates.X509Certificate2 next = RuntimeCertificate.Replace(directory);
            string fingerprint = RuntimeCertificate.Fingerprint(next);
            if (json)
            {
                Console.Out.WriteLine(JsonSerializer.Serialize(new { certificate = fingerprint, identity = replacedIdentity, served = false }, ProtocolJson.Options));
                return Clean;
            }

            Console.Out.WriteLine($"no daemon is serving this estate: the runtime certificate is now {fingerprint}, served from the next start");
            if (replacedIdentity is not null)
            {
                Console.Out.WriteLine($"  runtime identity replaced by {replacedIdentity}: every paired device pairs again");
            }

            return Clean;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException or HttpRequestException or System.Security.Cryptography.CryptographicException)
        {
            return Fail(exception.Message);
        }
    }

    public static int ConnectionsAdd(string address, string code, string name, string? custody, bool? presence, bool json)
    {
        try
        {
            var connections = new RuntimeConnections();
            IReadOnlyList<RuntimeConnection> before = connections.List();
            RuntimeConnection connection = connections.Add(address, code, new PairingOptions(name) { Custody = custody, Presence = presence });
            List<RuntimeConnection> replaced = [.. before.Where(earlier => connections.Find(earlier.Id) is null)];
            ConnectionStatus status = connections.Probe(connection);
            if (json)
            {
                Json.JsonObject added = Json.JsonNode.Parse(JsonSerializer.Serialize(ConnectionJson(status), ProtocolJson.Options))!.AsObject();
                added["replaced"] = Json.JsonNode.Parse(JsonSerializer.Serialize(replaced.Select(r => new { id = r.Id, device = r.Device, principal = r.Principal }), ProtocolJson.Options));
                Console.Out.WriteLine(added.ToJsonString());
                return Clean;
            }

            Console.Out.WriteLine($"paired as device {connection.Device} of {connection.PrincipalName ?? connection.Principal} ({connection.Principal}) with {connection.EstateName ?? connection.Estate} at {connection.Address}, permitted {string.Join(", ", connection.Profiles)}");
            Console.Out.WriteLine($"  runtime identity {RuntimeIdentityScheme.Fingerprint(connection.Identity)}, pinned on this machine");
            foreach (RuntimeConnection earlier in replaced)
            {
                Console.Out.WriteLine($"  replaced connection {earlier.Id}: device {earlier.Device} stays admitted until an administrator revokes it");
            }

            Console.Out.WriteLine($"connection {connection.Id}  {status.State}: {status.Message}");
            return Clean;
        }
        catch (PairingException exception)
        {
            return Fail(exception.Message);
        }
        catch (ArgumentException exception)
        {
            return Fail(exception.ParamName is { } parameter ? exception.Message.Replace($" (Parameter '{parameter}')", string.Empty, StringComparison.Ordinal) : exception.Message);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException or System.Security.Cryptography.CryptographicException)
        {
            return Fail(exception.Message);
        }
    }

    public static int ConnectionsList(bool json)
    {
        try
        {
            var connections = new RuntimeConnections();
            List<ConnectionStatus> statuses = connections.List().Select(c => connections.Probe(c)).ToList();
            if (json)
            {
                Console.Out.WriteLine(JsonSerializer.Serialize(statuses.Select(ConnectionJson), ProtocolJson.Options));
                return Clean;
            }

            if (statuses.Count == 0)
            {
                Console.Out.WriteLine("no connections on this machine; add one with 'vericairn connections add <address> --code <code> --name <device name>'");
            }

            foreach (ConnectionStatus status in statuses)
            {
                RuntimeConnection connection = status.Connection;
                Console.Out.WriteLine($"{connection.Id}  {connection.EstateName ?? connection.Estate}  {connection.Address}  {status.State}  device {connection.Device} ({connection.PrincipalName ?? connection.Principal})");
                Console.Out.WriteLine($"  {status.Message}");
            }

            return Clean;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException)
        {
            return Fail(exception.Message);
        }
    }

    public static int ConnectionsRemove(string id, bool json)
    {
        try
        {
            RuntimeConnection? removed = new RuntimeConnections().Remove(id);
            if (removed is null)
            {
                return Fail($"No connection {id} on this machine; 'vericairn connections list' shows them.");
            }

            if (json)
            {
                Console.Out.WriteLine(JsonSerializer.Serialize(new { removed = removed.Id, address = removed.Address, device = removed.Device, principal = removed.Principal }, ProtocolJson.Options));
                return Clean;
            }

            Console.Out.WriteLine($"removed connection {removed.Id} to {removed.Address}");
            Console.Out.WriteLine($"  device {removed.Device} stays admitted until an administrator revokes it ('vericairn devices revoke {removed.Device} --by <administrator> --reason <why>'): disconnection is not revocation; its key stays in this machine's custody");
            return Clean;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return Fail(exception.Message);
        }
    }

    private static object ConnectionJson(ConnectionStatus status) => new
    {
        id = status.Connection.Id,
        address = status.Connection.Address,
        estate = status.Connection.Estate,
        estateName = status.Connection.EstateName,
        identity = RuntimeIdentityScheme.Fingerprint(status.Connection.Identity),
        device = status.Connection.Device,
        principal = status.Connection.Principal,
        principalName = status.Connection.PrincipalName,
        profiles = status.Connection.Profiles,
        pairedAt = status.Connection.PairedAt,
        state = status.State,
        message = status.Message,
        lastCurrent = status.Connection.LastCurrent?.At,
    };

    /// <summary>Runs an operation that only a running daemon serves, such as pairing; refused, with the way on, when none answers.</summary>
    private static int WithDaemon(string root, string purpose, Func<VericairnClient, int> operation)
    {
        try
        {
            if (!IsEstate(root))
            {
                return Fail(NotAnEstate(root));
            }

            DaemonEndpoint? endpoint = DaemonHost.Discover(root);
            if (endpoint is null || !VericairnClient.IsAlive(endpoint))
            {
                return Fail($"No daemon is serving this estate, and {purpose} needs one: configure the local-network profile with 'vericairn runtime configure --local-network <address:port>' and start the runtime with 'vericairn runtime start'.");
            }

            using var client = new VericairnClient(endpoint);
            return operation(client);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException or HttpRequestException or JsonException)
        {
            return Fail(exception.Message);
        }
    }

    /// <summary>The git adapter behind sources and check; tests substitute a recorded one.</summary>
    /// <summary>Serves the estate over the Model Context Protocol on standard input and output; diagnostics go to standard error.</summary>
    public static int McpServe(string root, string? actingAs) => WithApi(root, api =>
    {
        var utf8 = new System.Text.UTF8Encoding(false);
        Console.InputEncoding = utf8;
        Console.OutputEncoding = utf8;
        var server = new McpServer(new ToolRegistry(api, actingAs), VersionText());
        Console.Error.WriteLine($"vericairn mcp: serving {Path.GetFullPath(root)} ({(string.IsNullOrWhiteSpace(actingAs) ? "read-only" : "acting as " + actingAs)})");
        server.Run(Console.In, Console.Out, Console.Error);
        return Clean;
    });

    /// <summary>
    /// Writes the agent orientation files from the estate's identity, its qualifications of agent harnesses and the tool registry;
    /// with <paramref name="check"/>, compares the copies in the directory with a fresh generation instead and names every file
    /// that is missing or differs.
    /// </summary>
    public static int McpOrientation(string root, string output, string? actingAs, bool check, bool json) => WithApi(root, api =>
    {
        IReadOnlyList<(string Name, string Content)> files = Orientation.Generate(api, new ToolRegistry(api, actingAs));
        if (check)
        {
            var findings = new List<FindingDto>();
            foreach ((string name, string content) in files)
            {
                string path = Path.Combine(output, name);
                if (!File.Exists(path))
                {
                    findings.Add(new FindingDto("error", "orientation.missing", name, "Missing; write it with `vericairn mcp orientation --out <directory>`."));
                }
                else if (File.ReadAllText(path) != content)
                {
                    findings.Add(new FindingDto("error", "orientation.stale", name, "Differs from a fresh generation; regenerate it with `vericairn mcp orientation --out <directory>` instead of editing it."));
                }
            }

            return ReportFindings(new FindingsResponse(findings.Count == 0 ? $"{output}: current" : $"{output}: out of date", findings.Count, 0, findings), json);
        }

        Directory.CreateDirectory(output);
        var written = new List<string>();
        foreach ((string name, string content) in files)
        {
            File.WriteAllText(Path.Combine(output, name), content);
            written.Add(name);
        }

        Console.Out.WriteLine(json ? JsonSerializer.Serialize(new { output, files = written }) : $"{output}: {string.Join(", ", written)}");
        return Clean;
    });

    /// <summary>The git adapter registered in this process; tests register a recorded one.</summary>
    internal static ISourceAdapter? SourceAdapter
    {
        get
        {
            Composition.Register(ProviderRegistry.Current);
            return ProviderRegistry.Current.Source("git");
        }

        set
        {
            if (value is null)
            {
                ProviderRegistry.Current.RemoveSource("git");
            }
            else
            {
                ProviderRegistry.Current.Register(value);
            }
        }
    }

    /// <summary>The implementation registered for the github-releases provider in this process; tests register the in-memory one and restore the previous afterwards.</summary>
    internal static IReleaseProvider? ReleaseProvider
    {
        get
        {
            Composition.Register(ProviderRegistry.Current);
            return ProviderRegistry.Current.Release("github-releases");
        }

        set
        {
            if (value is null)
            {
                ProviderRegistry.Current.RemoveRelease("github-releases");
            }
            else
            {
                ProviderRegistry.Current.Register(value);
            }
        }
    }

    private static ISourceAdapter? GitAdapter(out int failure)
    {
        ISourceAdapter? adapter = SourceAdapter;
        failure = adapter is null ? Fail("No git adapter is available in this distribution; repositories cannot be observed here.") : Clean;
        return adapter;
    }

    public static int SourcesInspect(string path, bool json)
    {
        try
        {
            ISourceAdapter? adapter = GitAdapter(out int failure);
            if (adapter is null)
            {
                return failure;
            }

            RepositoryObservation observation = adapter.Inspect(path);
            if (json)
            {
                Console.Out.WriteLine(JsonSerializer.Serialize(observation, ProtocolJson.Options));
            }
            else
            {
                PrintObservation(observation);
                Console.Out.WriteLine("Read-only: nothing in the repository changed.");
            }

            return Clean;
        }
        catch (ProviderRefusedException exception)
        {
            return Fail(exception.Message);
        }
    }

    public static int SourcesAdopt(string root, string path, string by, string name, string role, string visibility, string? notes, bool json)
    {
        RepositoryObservation observation;
        try
        {
            observation = (GitAdapter(out int failure) ?? throw new ProviderRefusedException("No git adapter is available in this distribution; repositories cannot be observed here.")).Inspect(path);
        }
        catch (ProviderRefusedException exception)
        {
            return Fail(exception.Message);
        }

        return WithApi(root, api =>
        {
            WriteOutcome resource = api.NewRecord(new NewRecordRequest("resource", by, Element(SourceAdoption.ResourceBody(name, role, visibility, observation, notes))));
            if (!resource.Ok)
            {
                return ReportOutcome(resource, json);
            }

            WriteOutcome observed = api.NewRecord(new NewRecordRequest("observation", by, Element(SourceAdoption.ObservationBody(observation)), $"resource:{resource.Written!.Id}:{resource.Written.Hash}"));
            if (!observed.Ok)
            {
                return ReportOutcome(observed, json);
            }

            if (json)
            {
                Console.Out.WriteLine(JsonSerializer.Serialize(new { resource = resource.Written, observation = observed.Written, commit = observation.Commit, clean = observation.Clean }, ProtocolJson.Options));
            }
            else
            {
                Console.Out.WriteLine($"resource     {resource.Written.Id}  {resource.Written.Path}");
                Console.Out.WriteLine($"observation  {observed.Written!.Id}  {observed.Written.Path}");
                PrintObservation(observation);
                Console.Out.WriteLine("Adopted by observation only: the repository is a resource the estate uses and does not own; uncommitted work is preserved and nothing in it changed.");
            }

            return Clean;
        });
    }

    /// <summary>Registers this machine's clone of an adopted repository, for the sources that read it, such as living documents of other repositories; nothing is recorded.</summary>
    public static int SourcesClone(string root, string resourceId, string path, bool json)
    {
        RepositoryObservation observation;
        try
        {
            observation = (GitAdapter(out int failure) ?? throw new ProviderRefusedException("No git adapter is available in this distribution; repositories cannot be observed here.")).Inspect(path);
        }
        catch (ProviderRefusedException exception)
        {
            return Fail(exception.Message);
        }

        return WithApi(root, api =>
        {
            RecordResponse? resource = api.Record(resourceId);
            if (resource is null || resource.Type != "resource" || !resource.Current)
            {
                return Fail($"No current resource has id {resourceId}.");
            }

            // The clone is the repository the resource names: by its root commit where recorded, else by its GitHub remote.
            JsonElement location = resource.Envelope.GetProperty("body").GetProperty("location");
            string? rootCommit = location.TryGetProperty("rootCommit", out JsonElement r) ? r.GetString() : null;
            string? github = location.TryGetProperty("github", out JsonElement g) ? g.GetString() : null;
            bool matches = rootCommit is not null ? rootCommit == observation.RootCommit : github is not null && string.Equals(SourceAdoption.GitHubRepository(observation.Remote), github, StringComparison.OrdinalIgnoreCase);
            if (!matches)
            {
                return Fail($"That working tree is not the repository of resource {resource.Id}: its {(rootCommit is not null ? "root commit" : "GitHub remote")} differs.");
            }

            RepositoryClones.Register(api.Health().Estate, resource.Id, path);
            Console.Out.WriteLine(json ? JsonSerializer.Serialize(new { resource = resource.Id, commit = observation.Commit }) : $"Registered this machine's clone of resource {resource.Id} at {observation.Commit[..12]}; the path stays in this machine's configuration, never in a record.");
            return Clean;
        });
    }

    public static int SourcesObserve(string root, string path, string resourceId, string by, bool json)
    {
        RepositoryObservation observation;
        try
        {
            observation = (GitAdapter(out int failure) ?? throw new ProviderRefusedException("No git adapter is available in this distribution; repositories cannot be observed here.")).Inspect(path);
        }
        catch (ProviderRefusedException exception)
        {
            return Fail(exception.Message);
        }

        return WithApi(root, api =>
        {
            RecordResponse? resource = api.Record(resourceId);
            if (resource is null || resource.Type != "resource" || !resource.Current)
            {
                return Fail($"No current resource has id {resourceId}.");
            }

            return ReportOutcome(api.NewRecord(new NewRecordRequest("observation", by, Element(SourceAdoption.ObservationBody(observation)), $"resource:{resource.Id}:{resource.Hash}")), json);
        });
    }

    public static int CheckRun(string root, string resourceId, string by, string path, string role, string? summary, int timeoutSeconds, string[] command, bool json)
    {
        RepositoryObservation observation;
        CheckRun run;
        try
        {
            observation = (GitAdapter(out int failure) ?? throw new ProviderRefusedException("No git adapter is available in this distribution; repositories cannot be observed here.")).Inspect(path);
            run = Checks.Run(path, command, TimeSpan.FromSeconds(timeoutSeconds));
        }
        catch (ProviderRefusedException exception)
        {
            return Fail(exception.Message);
        }

        return WithApi(root, api =>
        {
            RecordResponse? resource = api.Record(resourceId);
            if (resource is null || resource.Type != "resource" || !resource.Current)
            {
                return Fail($"No current resource has id {resourceId}.");
            }

            var evidence = new List<Checks.Evidence>();
            if (run.Output.Length > 0)
            {
                (ObjectStored? stored, _) = api.ObjectPut(run.Output);
                evidence.Add(stored is null ? new Checks.Evidence($"Command output ({run.Output.Length:N0} bytes, above the object limit, not stored): {run.OutputTail()}") : new Checks.Evidence("Command output", stored.Hash));
            }

            var limitations = new List<string> { "A check that passed here establishes the result for this commit on this platform only." };
            if (!observation.Clean)
            {
                limitations.Add($"The working tree had {observation.Modified} modified and {observation.Untracked} untracked files; the result may not reproduce from the commit alone.");
            }

            string text = summary ?? run.Summary;
            Json.JsonObject body = Checks.VerificationBody("command", observation.Commit, run.Command, Checks.LocalEnvironment(), role, run.Outcome, text, run.ExitCode, run.DurationMs, run.StartedAt, run.FinishedAt, evidence, limitations);
            if (api.Health().RecordFormat >= 2)
            {
                // The command ran here and was seen to finish: an observation by a tool.
                body["evidenceClass"] = "observation";
                body["observer"] = "automation";
            }

            WriteOutcome outcome = api.NewRecord(new NewRecordRequest("verification", by, Element(body), $"resource:{resource.Id}:{resource.Hash}"));
            if (!outcome.Ok)
            {
                return ReportOutcome(outcome, json);
            }

            if (json)
            {
                Console.Out.WriteLine(JsonSerializer.Serialize(new { verification = outcome.Written, run.Outcome, run.ExitCode, run.DurationMs, commit = observation.Commit }, ProtocolJson.Options));
            }
            else
            {
                Console.Out.WriteLine($"{outcome.Written!.Path}  {outcome.Written.Id}  {outcome.Written.Hash}");
                Console.Out.WriteLine($"{run.Outcome}: {text} (commit {observation.Commit[..12]})");
            }

            return Clean;
        });
    }

    public static int CheckObserve(string root, string resourceId, string? repository, string? providerId, string runId, string workflow, string by, bool json)
    {
        return WithApi(root, api =>
        {
            RecordResponse? resource = api.Record(resourceId);
            if (resource is null || resource.Type != "resource" || !resource.Current)
            {
                return Fail($"No current resource has id {resourceId}.");
            }

            // The provider is the one named, else the one serving the resource's recorded location; the destination likewise.
            Composition.Register(ProviderRegistry.Current);
            JsonElement location = resource.Envelope.GetProperty("body").TryGetProperty("location", out JsonElement l) && l.ValueKind == JsonValueKind.Object ? l : default;
            IReleaseProvider? provider = null;
            string? destination = repository;
            if (!string.IsNullOrWhiteSpace(providerId))
            {
                provider = ProviderRegistry.Current.Release(providerId);
                if (provider is null)
                {
                    return Fail($"The provider '{providerId}' has no implementation here; nothing selected another.");
                }
            }

            if (location.ValueKind == JsonValueKind.Object)
            {
                foreach (JsonProperty key in location.EnumerateObject().Where(p => p.Value.ValueKind == JsonValueKind.String))
                {
                    IReleaseProvider? serving = ProviderRegistry.Current.ReleaseForLocation(key.Name);
                    if (serving is not null && (provider is null || ReferenceEquals(serving, provider)))
                    {
                        provider ??= serving;
                        destination ??= key.Value.GetString();
                        break;
                    }
                }
            }

            if (provider is null || string.IsNullOrWhiteSpace(destination))
            {
                return Fail($"No provider here serves resource {resourceId}'s recorded location; name the provider and the repository.");
            }

            RunObservation? run;
            try
            {
                run = provider.ObserveRun(destination, runId);
            }
            catch (Exception exception) when (exception is ProviderRefusedException or ProviderUncertainException)
            {
                return Fail(exception.Message);
            }

            if (run is null)
            {
                return Fail($"No run {runId} is observable in {destination}.");
            }

            Json.JsonObject observed = Checks.VerificationOfRun(run, workflow);
            if (api.Health().RecordFormat >= 2)
            {
                // A workflow run's conclusion is the host's report of it, not an observation made here.
                observed["evidenceClass"] = "report";
                observed["observer"] = "provider";
            }

            WriteOutcome outcome = api.NewRecord(new NewRecordRequest("verification", by, Element(observed), $"resource:{resource.Id}:{resource.Hash}"));
            if (!outcome.Ok)
            {
                return ReportOutcome(outcome, json);
            }

            if (json)
            {
                Console.Out.WriteLine(JsonSerializer.Serialize(new { verification = outcome.Written, run.Status, run.Conclusion, commit = run.HeadSha }, ProtocolJson.Options));
            }
            else
            {
                Console.Out.WriteLine($"{outcome.Written!.Path}  {outcome.Written.Id}  {outcome.Written.Hash}");
                Console.Out.WriteLine($"run {run.RunId}: {run.Status}{(run.Conclusion is null ? string.Empty : ", " + run.Conclusion)} at {run.HeadSha[..12]}");
            }

            return Clean;
        });
    }

    public static int ReleaseSubmit(string root, string product, string version, string by, string? provider, string? repository, string? reference, string? workflow, string[] settings, string? reconciliation, bool json)
    {
        Dictionary<string, string>? parsed = ParseSettings(settings, out string? problem);
        return parsed is null
            ? Fail(problem!)
            : WithApi(root, api => ReportRelease(api.ReleaseSubmit(new ReleaseSubmitRequest(product, version, repository, by, reference, workflow, reconciliation, provider, parsed)), json));
    }

    public static int ReleaseObserve(string root, string version, string by, string? product, string? provider, string? repository, string[] qualified, string[] limitations, string signing, string? cancel, bool json) =>
        WithApi(root, api => ReportRelease(api.ReleaseObserve(new ReleaseObserveRequest(version, repository, by, qualified, limitations, signing, cancel, product, provider)), json));

    public static int ReleaseProviders(string root, bool json) => WithApi(root, api =>
    {
        IReadOnlyList<ProviderRow> rows = api.Providers();
        if (json)
        {
            Console.Out.WriteLine(JsonSerializer.Serialize(rows, ProtocolJson.Options));
            return Clean;
        }

        if (rows.Count == 0)
        {
            Console.Out.WriteLine("No provider or adapter is available to the runtime here.");
            return Clean;
        }

        foreach (ProviderRow row in rows)
        {
            Console.Out.WriteLine($"{row.Provider}  {row.Kind}  {row.Implementation}  {row.Contract}  {row.Status}{(row.Locations.Count == 0 ? string.Empty : "  serves " + string.Join(", ", row.Locations))}");
            foreach (ProviderSettingRow setting in row.Settings)
            {
                string marks = (setting.Name == row.Destination ? "destination, " : string.Empty) + (setting.Required ? "required" : setting.Default is null ? "optional" : "default " + setting.Default);
                Console.Out.WriteLine($"  {setting.Name,-12} {marks}: {setting.Summary}");
            }

            foreach (string limitation in row.Limitations)
            {
                Console.Out.WriteLine($"  limitation: {limitation}");
            }
        }

        return Clean;
    });

    private static Dictionary<string, string>? ParseSettings(string[] settings, out string? problem)
    {
        var parsed = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (string setting in settings)
        {
            int equals = setting.IndexOf('=', StringComparison.Ordinal);
            if (equals <= 0)
            {
                problem = $"A setting is name=value; '{setting}' is not.";
                return null;
            }

            parsed[setting[..equals].Trim()] = setting[(equals + 1)..].Trim();
        }

        problem = null;
        return parsed;
    }

    /// <summary>Prints a release operation's outcome exactly as the runtime reported it: a problem fails, a rejected record lists its findings, and the exit code says whether the request advanced.</summary>
    private static int ReportRelease(ReleaseResponse response, bool json)
    {
        if (response.Problem is not null)
        {
            return response.Problem.Findings.Count > 0 ? ReportOutcome(WriteOutcome.Fail(response.Problem), json) : Fail(response.Message);
        }

        if (json)
        {
            Console.Out.WriteLine(JsonSerializer.Serialize(response, ProtocolJson.Options));
        }
        else
        {
            foreach (RecordWritten? written in new[] { response.Operation, response.Candidate, response.Release })
            {
                if (written is not null)
                {
                    Console.Out.WriteLine($"{written.Path}  {written.Id}  {written.Hash}");
                }
            }

            Console.Out.WriteLine(response.Message);
        }

        return response.Ok ? Clean : HasErrors;
    }

    private static void PrintObservation(RepositoryObservation observation)
    {
        Console.Out.WriteLine($"commit      {observation.Commit}{(observation.Branch is null ? "  (detached)" : "  on " + observation.Branch)}");
        Console.Out.WriteLine($"remote      {(SourceAdoption.IsNetworkRemote(observation.Remote) ? observation.Remote : observation.Remote is null ? "none" : "a path on this machine (not recorded)")}");
        Console.Out.WriteLine($"identity    root commit {observation.RootCommit}{(SourceAdoption.GitHubRepository(observation.Remote) is string github ? ", github " + github : string.Empty)}");
        Console.Out.WriteLine($"working     {(observation.Clean ? "clean" : $"{observation.Modified} modified, {observation.Untracked} untracked")}");
        Console.Out.WriteLine($"tags        {(observation.Tags.Count == 0 ? "none" : string.Join(", ", observation.Tags))}");
        foreach (RecentCommit commit in observation.Recent)
        {
            Console.Out.WriteLine($"  {commit.Commit[..12]}  {commit.At}  {commit.Summary}");
        }
    }

    private static JsonElement Element(Json.JsonObject body)
    {
        using JsonDocument document = JsonDocument.Parse(body.ToJsonString());
        return document.RootElement.Clone();
    }

    /// <summary>Runs an operation against the estate's API: the daemon's when one answers for this estate, the embedded runtime otherwise.</summary>
    private static int WithApi(string root, Func<IEstateApi, int> operation)
    {
        try
        {
            if (!IsEstate(root))
            {
                return Fail(NotAnEstate(root));
            }

            DaemonEndpoint? endpoint = DaemonHost.Discover(root);
            if (endpoint is not null && VericairnClient.IsAlive(endpoint))
            {
                using var client = new VericairnClient(endpoint);
                return operation(client);
            }

            using EstateService service = EstateService.Open(root);
            return operation(service);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException or HttpRequestException or JsonException)
        {
            return Fail(exception.Message);
        }
    }

    private static string LocalNetworkAddress(LocalNetworkEndpoint network) =>
        "https://" + new System.Net.IPEndPoint(System.Net.IPAddress.Parse(network.Host), network.Port);

    /// <summary>A bare records directory without a manifest is still validated from its files; no live state is involved.</summary>
    private static int WithFiles(string root, Func<EstateModel, int> operation)
    {
        try
        {
            return operation(EstateLoader.Load(root));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            return Fail(exception.Message);
        }
    }

    private static int WithBody(string bodyPath, Func<JsonElement, int> operation)
    {
        try
        {
            using JsonDocument body = CanonicalJson.Parse(bodyPath == "-" ? ReadStandardInput() : File.ReadAllBytes(bodyPath));
            return operation(body.RootElement.Clone());
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or CanonicalJsonException)
        {
            return Fail(exception.Message);
        }
    }

    internal static EstateModel LoadCopy(string root)
    {
        if (!IsEstate(root))
        {
            return EstateLoader.Load(root);
        }

        using EstateSession session = EstateSession.Open(root);
        return session.Load();
    }

    private static bool IsEstate(string root) => File.Exists(Path.Combine(root, EstateLayout.ManifestPath));

    private static string NotAnEstate(string root) => $"{Path.GetFullPath(root)} is not an estate root (no {EstateLayout.ManifestPath}).";

    private static PosixSignalRegistration? IgnoreHangup()
    {
        try
        {
            return PosixSignalRegistration.Create(PosixSignal.SIGHUP, context => context.Cancel = true);
        }
        catch (PlatformNotSupportedException)
        {
            return null;
        }
    }

    private static void PrintPlan(ServicePlan plan, bool json)
    {
        if (json)
        {
            Console.Out.WriteLine(JsonSerializer.Serialize(plan, ProtocolJson.Options));
            return;
        }

        Console.Out.WriteLine($"{plan.Platform}: {plan.Label}");
        Console.Out.WriteLine($"unit {plan.UnitPath}:");
        Console.Out.Write(plan.UnitContent);
        foreach (IReadOnlyList<string> command in plan.Install)
        {
            Console.Out.WriteLine("install: " + string.Join(' ', command));
        }

        foreach (IReadOnlyList<string> command in plan.Uninstall.Concat(plan.AfterUninstall))
        {
            Console.Out.WriteLine("uninstall: " + string.Join(' ', command));
        }
    }

    private static FindingList Combined(EstateModel model)
    {
        var all = new FindingList();
        foreach (Finding finding in model.Findings.Concat(EstateChecks.Run(model)))
        {
            all.Add(finding);
        }

        return all;
    }

    private static string VersionText() =>
        Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "unknown";

    private static string? Probe(string tool, string argument)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo(tool) { ArgumentList = { argument }, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false });
            if (process is null)
            {
                return null;
            }

            string output = process.StandardOutput.ReadToEnd().Trim();
            process.WaitForExit(10_000);
            return process.ExitCode == 0 ? output.Split('\n')[0] : null;
        }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or IOException or InvalidOperationException)
        {
            return null;
        }
    }

    private static byte[] ReadStandardInput()
    {
        using Stream input = Console.OpenStandardInput();
        using var buffer = new MemoryStream();
        input.CopyTo(buffer);
        return buffer.ToArray();
    }

    private static FindingsResponse ToFindings(FindingList findings, string summary) =>
        new(summary, findings.Errors, findings.Warnings, findings.Sorted().Select(f => new FindingDto(f.Severity.ToString().ToLowerInvariant(), f.Code, f.Location, f.Message)).ToList());

    private static int ReportOutcome(WriteOutcome outcome, bool json)
    {
        if (outcome.Written is RecordWritten written)
        {
            Console.Out.WriteLine(json ? JsonSerializer.Serialize(new { path = written.Path, id = written.Id, hash = written.Hash }) : $"{written.Path}  {written.Id}  {written.Hash}");
            return Clean;
        }

        Problem problem = outcome.Problem ?? new Problem("refused", "refused", []);
        if (problem.Code == "rejected")
        {
            Console.Error.WriteLine(problem.Message);
            return ReportFindings(new FindingsResponse("rejected", problem.Findings.Count(f => f.Severity == "error"), problem.Findings.Count(f => f.Severity == "warning"), problem.Findings), json);
        }

        return Fail(problem.Message);
    }

    internal static int ReportFindings(FindingsResponse findings, bool json)
    {
        if (json)
        {
            Console.Out.WriteLine(JsonSerializer.Serialize(new
            {
                summary = findings.Summary,
                errors = findings.Errors,
                warnings = findings.Warnings,
                findings = findings.Findings.Select(f => new { severity = f.Severity, f.Code, f.Location, f.Message }),
            }));
        }
        else
        {
            foreach (FindingDto finding in findings.Findings)
            {
                Console.Out.WriteLine($"{finding.Severity.ToUpperInvariant()} {finding.Code} {finding.Location}: {finding.Message}");
            }

            Console.Out.WriteLine($"{findings.Summary}: {findings.Errors} errors, {findings.Warnings} warnings");
        }

        return findings.Errors > 0 ? HasErrors : Clean;
    }

    internal static int Fail(string message)
    {
        Console.Error.WriteLine("error: " + message);
        return Failed;
    }
}
