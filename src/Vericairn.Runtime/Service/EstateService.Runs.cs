// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Vericairn.Protocol;
using Vericairn.Records.Authoring;
using Vericairn.Records.Estate;
using Vericairn.Records.Hashing;
using Vericairn.Records.Identity;
using Vericairn.Records.Model;
using Vericairn.Records.Signing;
using Vericairn.Records.Types;
using Vericairn.Runtime.Providers;
using Vericairn.Runtime.Workers;

namespace Vericairn.Runtime.Service;

/// <summary>
/// The worker contract, vericairn.worker/1 (the estate's design/goals.md § 7): one assignment carried by a coding agent through a
/// launcher. Prepare a workspace on a work branch, never the shared clone, with the brief exported into it, the MCP server
/// configured for the agent's principal, an environment from the allowlist and the run's cap from its goal's budget; record the
/// intent, an operation of action agent-run, before anything starts; launch; observe heartbeats, usage and the exit, a lost
/// connection never counting as a stop; and settle the operation with the observed effect and usage. The worker accepts nothing:
/// the agent's result is a claim another principal reviews.
/// </summary>
public sealed partial class EstateService
{
    private static readonly JsonSerializerOptions RunRecordJson = new(ProtocolJson.Options) { DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull };

    private readonly ConcurrentDictionary<string, (Task Task, CancellationTokenSource Abandon)> supervisions = new(StringComparer.Ordinal);

    /// <summary>How the worker paces and places the runs this service supervises.</summary>
    public WorkerOptions WorkerOptions { get; set; } = WorkerOptions.Default;

    /// <inheritdoc />
    public IReadOnlyList<ProviderRow> Launchers() => ProviderRows().Where(r => r.Kind == "launcher").ToList();

    /// <inheritdoc />
    public RunResponse RunStart(RunStartRequest request) => Start(request, null, out _);

    /// <summary>
    /// Starts a run: the whole preflight first, the records last. With a pending assignment, routing's assignment of a step that no
    /// assignment held, signed and not yet stored, the run carries it, and it is stored with the run's intent, so that neither is left
    /// alone: every refusal before the intent records nothing, that assignment included.
    /// </summary>
    private RunResponse Start(RunStartRequest request, AuthoredRecord? routed, out RecordWritten? assigned)
    {
        ArgumentNullException.ThrowIfNull(request);
        assigned = null;
        IReadOnlyList<AuthoredRecord> carried = routed is null ? [] : [routed];
        string pending = $"run of assignment {request.Assignment}";
        if (!Ulid.IsValid(request.Assignment) || !Ulid.IsValid(request.Agent) || !Ulid.IsValid(request.By) || !Ulid.IsValid(request.Repository) || (request.Goal is not null && !Ulid.IsValid(request.Goal)))
        {
            return RunProblem(pending, "not-started", "refused", "The assignment, the agent, the recording principal, the repository and the goal are named by ULIDs; nothing was recorded.");
        }

        if (request.WallMinutes is <= 0)
        {
            return RunProblem(pending, "not-started", "refused", "A run's cap is at least one wall minute; nothing was recorded.");
        }

        IAgentLauncher? launcher = string.IsNullOrWhiteSpace(request.Launcher) ? null : Providers.Launcher(request.Launcher.Trim());
        if (launcher is null)
        {
            return RunProblem(pending, "not-started", "refused", $"The launcher '{request.Launcher}' has no implementation on the runtime's machine; nothing selected another and nothing was recorded. Available: {string.Join(", ", Providers.Launchers.Select(l => l.Launcher).DefaultIfEmpty("none"))}.");
        }

        IWorkspaceProvider? workspaces = Providers.Workspace("git");
        if (workspaces is null)
        {
            return RunProblem(pending, "not-started", "refused", "No workspace provider for git repositories is available on the runtime's machine, so no isolated workspace can be prepared; nothing was recorded.");
        }

        var settings = new Dictionary<string, string>(request.Settings ?? new Dictionary<string, string>(StringComparer.Ordinal), StringComparer.Ordinal);
        List<string> unknown = settings.Keys.Where(k => launcher.Descriptor.DeclaredSettings.All(s => s.Name != k)).Order(StringComparer.Ordinal).ToList();
        List<string> missing = launcher.Descriptor.DeclaredSettings.Where(s => s.Required && !settings.ContainsKey(s.Name)).Select(s => s.Name).ToList();
        if (unknown.Count > 0 || missing.Count > 0)
        {
            string takes = string.Join(", ", launcher.Descriptor.DeclaredSettings.Select(s => s.Name + (s.Required ? " (required)" : string.Empty)).DefaultIfEmpty("none"));
            return RunProblem(pending, "not-started", "refused", $"The launcher '{launcher.Launcher}' takes the settings {takes}{(unknown.Count > 0 ? "; it takes no " + string.Join(", ", unknown) : string.Empty)}{(missing.Count > 0 ? "; this run names no " + string.Join(", ", missing) : string.Empty)}. Nothing was recorded.");
        }

        if (settings.TryGetValue("stall-seconds", out string? stallSetting) && (!int.TryParse(stallSetting, NumberStyles.None, CultureInfo.InvariantCulture, out int stallValue) || stallValue is < 1 or > 86400))
        {
            return RunProblem(pending, "not-started", "refused", "The stall-seconds setting is an integer from 1 through 86400. Nothing was recorded.");
        }

        RunPlan plan;
        lock (gate)
        {
            EstateModel model = LoadWith(carried);
            (RunPlan? planned, string? refusal, RecordEnvelope? limited) = Plan(model, request, launcher);
            if (planned is null)
            {
                if (limited is not null)
                {
                    MarkBudgetLimited(model, limited, request.By, refusal!);
                }

                return RunProblem(pending, "not-started", "refused", refusal + " Nothing was recorded.");
            }

            plan = planned;
        }

        RunLimits limits = RunLimits.For(Element(plan.Cap), WorkerOptions);
        if (limits.Tokens is <= 0 || limits.Currency is <= 0)
        {
            return RunProblem(pending, "not-started", "refused", "The remaining allowance cannot cover one unit plus in-flight headroom. Nothing was recorded.");
        }

        // The launcher's own check comes before anything is recorded or prepared: Codex's goals feature, for one.
        Dictionary<string, string> environment = WorkerEnvironment.Build(WorkerOptions.Environment ?? WorkerEnvironment.Current(), launcher.EnvironmentNames);
        string? unverified;
        try
        {
            unverified = launcher.Verify(settings, environment);
        }
        catch (ProviderRefusedException exception)
        {
            unverified = exception.Message;
        }

        if (unverified is not null)
        {
            return RunProblem(pending, "not-started", "refused", $"The {launcher.Launcher} launcher cannot run here: {unverified} Nothing was recorded.");
        }

        // Prepare: the workspace on its work branch, never the shared clone, with the brief exported into it.
        PreparedWorkspace workspace;
        WorkspaceSnapshot baseline;
        string brief;
        try
        {
            workspace = workspaces.Prepare(plan.ClonePath, plan.Branch, request.Base, Path.Combine(VericairnHome.Path(), "estates", plan.Estate, "workspaces", Slug(plan.Branch)));
            brief = WorkspaceMetadata.Write(workspace.Path, plan.Brief);
            baseline = workspaces.Capture(workspace);
        }
        catch (ProviderRefusedException exception)
        {
            return RunProblem(pending, "not-started", "refused", $"The workspace could not be prepared: {exception.Message} Nothing was recorded.");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return RunProblem(pending, "not-started", "refused", $"The workspace could not be prepared: {exception.Message} Nothing was recorded.");
        }

        string where = $"{plan.Repository}, branch {workspace.Branch}";

        // Record the intent before anything starts, judged again against the records as they are now, after the preparation: the
        // step still unassigned when routing's assignment comes with the run, the goal at its current revision, the custody guard
        // over the keys held here and the exception in force now, which the intent names, and the cap. The run is its intent.
        string run;
        RecordWritten intent;
        Excepted? excepted = null;
        lock (gate)
        {
            EstateModel model = LoadWith(carried);
            RecordEnvelope? goal = AgentRuns.GoalAt(model, plan.Goal);
            if (routed is not null && WorkView.OrderOfAssignment(model, routed.Envelope) is RecordEnvelope routedOrder
                && Routing.Assignments(model, model.Latest(routedOrder)).FirstOrDefault(a => a.Id != routed.Envelope.Id) is RecordEnvelope meanwhile)
            {
                return RunProblem(pending, "not-started", "refused", $"Work order {plan.Key} was assigned meanwhile, by assignment {meanwhile.Id}; its step goes on from that assignment. Nothing was recorded.");
            }

            DateTimeOffset now = DateTimeOffset.UtcNow;
            if (goal is not null)
            {
                IReadOnlyList<(string Device, string Reason)> exposed = CustodyGuard.Exposed(model, CustodyGuard.Guarded(model, goal, plan.Agent), DeviceKeyStores.All(), Environment.UserName);
                RecordEnvelope? exception = exposed.Count == 0 ? null : CustodyExceptions.InForce(model, goal, now);
                if (exposed.Count > 0 && exception is null)
                {
                    return RunProblem(pending, "not-started", "refused", CustodyRefusal(goal.Id, exposed) + " Nothing was recorded.");
                }

                excepted = exception is null ? null : new Excepted(exception.Id, CustodyExceptions.Describe(model, exception), EstateModel.BodyString(exception, "expires") ?? "?", exposed);
            }

            IReadOnlyList<(string Code, string Message)> refusals = goal is null ? [("operation.goal", $"Goal {plan.Goal} is gone.")] : AgentRuns.StartRefusals(model, goal, plan.Assignment.Id, Element(plan.Cap));
            if (refusals.Count > 0)
            {
                return RunProblem(pending, "not-started", "refused", string.Join(" ", refusals.Select(r => r.Message)) + " Nothing was recorded.");
            }

            plan = plan with { StallSeconds = StallSeconds(goal!, settings, launcher) };
            var target = new JsonObject
            {
                ["assignment"] = plan.Assignment.Id,
                ["principal"] = plan.Agent,
                ["workspace"] = where,
                ["repository"] = plan.Resource,
                ["branch"] = workspace.Branch,
                ["goal"] = goal!.Id,
                ["workOrder"] = plan.Key,
                ["worker"] = AgentRuns.WorkerContract,
                ["stallSeconds"] = plan.StallSeconds.ToString(CultureInfo.InvariantCulture),
            };
            target["capMode"] = "soft";
            if (excepted is not null)
            {
                target["custodyException"] = excepted.Id;
            }

            var body = new JsonObject
            {
                ["provider"] = launcher.Launcher,
                ["action"] = OperationType.AgentRun,
                ["intent"] = $"Run {launcher.Launcher} as {plan.AgentName} on assignment {plan.Assignment.Id} of work order {plan.Key}, toward its exit question, within its cap of {AgentRuns.Amount(Element(plan.Cap), "wallMinutes")?.ToString(CultureInfo.InvariantCulture)} wall minutes and {plan.Cap["attemptsPerStep"]} attempts per step.",
                ["target"] = target,
                ["status"] = "not-started",
                ["evidence"] = new JsonArray(new JsonObject { ["summary"] = $"The brief of work order {plan.Key}, exported into the workspace as .vericairn/brief.md.", ["hash"] = ContentHash.Sha256(Encoding.UTF8.GetBytes(plan.Brief)) }),
                ["cap"] = plan.Cap.DeepClone(),
            };
            ((JsonArray)body["evidence"]!).Add(new JsonObject { ["summary"] = limits.Describe(Element(plan.Cap)) });
            ((JsonArray)body["evidence"]!).Add(new JsonObject { ["summary"] = launcher.Limitations });
            if (excepted is not null)
            {
                ((JsonArray)body["evidence"]!).Add(new JsonObject { ["summary"] = $"Launched unattended under the sponsor's custody exception {excepted.Id}, in force until {excepted.Expires}, while {(excepted.Exposed.Count == 1 ? "device" : "devices")} {string.Join(", ", excepted.Exposed.Select(e => e.Device))} of an operator or of the goal's sponsor {(excepted.Exposed.Count == 1 ? "signs" : "sign")} without {(excepted.Exposed.Count == 1 ? "its" : "their")} owner's presence on the machine that ran the worker." });
            }

            run = Ulid.NewUlid(now).Value;
            body["identity"] = "agent-run:" + run;
            var draft = new RecordDraft("operation", request.By, Element(body), plan.Assignment.Reference, At: now, Id: run);
            if (routed is null)
            {
                WriteOutcome written = WriteLocked(draft, model);
                if (!written.Ok)
                {
                    return new RunResponse(pending, "not-started", "Not recorded: " + written.Problem!.Message, false, Problem: written.Problem);
                }

                intent = written.Written!;
            }
            else
            {
                // Routing's assignment and the run's intent are stored together, or neither is.
                (AuthoredRecord? authored, string? unsigned) = AuthorLocked(draft, model);
                if (authored is null)
                {
                    return RunProblem(pending, "not-started", "refused", $"Not recorded: {unsigned} Nothing was recorded.");
                }

                (IReadOnlyList<RecordWritten> stored, Problem? problem) = StoreTogetherLocked([routed, authored], $"routing's assignment of work order {plan.Key} to {plan.AgentName} and the intent of its run {run}", routed.Envelope.Subject?.Id);
                if (problem is not null)
                {
                    assigned = stored.Count > 0 ? stored[0] : null;
                    string what = assigned is null
                        ? "Nothing was recorded."
                        : $"Routing's assignment {assigned.Id} was stored and the run's intent was not, so nothing was launched: the assignment holds nothing of its step without that intent, routing takes the step again, and this machine reports the batch as a reconcile task (batch.incomplete).";
                    return new RunResponse(pending, "not-started", $"{problem.Message} {what}", false, Problem: problem);
                }

                assigned = stored[0];
                intent = stored[1];
            }
        }

        // The MCP server configured for the agent's principal, and the run's own files, outside the workspace; then the launch, and the
        // dispatch recorded. A harness that cannot start is a known failure that spent nothing.
        var directory = new RunDirectory(plan.Estate, run);
        IReadOnlyList<string> server = [.. WorkerOptions.VericairnCommand(), "--estate", session.Root, "mcp", "serve", "--as", plan.Agent];
        var serverEnvironment = WorkerEnvironment.McpServer.Where(environment.ContainsKey).ToDictionary(n => n, n => environment[n], StringComparer.Ordinal);
        environment["VERICAIRN_RUN"] = run;
        environment["VERICAIRN_STOP_FILE"] = directory.StopFile;
        environment["VERICAIRN_BRIEF"] = brief;
        environment["VERICAIRN_MCP_CONFIG"] = directory.McpConfiguration;
        environment["VERICAIRN_CONDITION"] = plan.Condition;
        RunSupervisor? supervisor = null;
        IReadOnlyList<(string Device, string Reason)> lapsed = [];
        try
        {
            directory.Create();
            var mcp = new JsonObject
            {
                ["mcpServers"] = new JsonObject
                {
                    ["vericairn"] = new JsonObject
                    {
                        ["command"] = server[0],
                        ["args"] = new JsonArray(server.Skip(1).Select(a => (JsonNode)a).ToArray()),
                        ["env"] = new JsonObject(serverEnvironment.OrderBy(e => e.Key, StringComparer.Ordinal).Select(e => KeyValuePair.Create(e.Key, (JsonNode?)e.Value))),
                    },
                },
            };
            File.WriteAllText(directory.McpConfiguration, mcp.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + "\n");
            LaunchContext launch = launcher.Prepare(new LaunchContext(workspace.Path, plan.Condition, brief, directory.McpConfiguration, server, serverEnvironment, settings, environment, limits));
            environment = new Dictionary<string, string>(launch.Environment, WorkerEnvironment.Names);
            IReadOnlyList<string> command = launcher.Command(launch);

            // The custody precondition once more, immediately before the launch and under the gate, so that no exception is revised
            // or expires between this judgment and the start: one that lapsed since the intent, or a key that arrived, launches nothing.
            lock (gate)
            {
                lapsed = Lapsed(session.Load(), plan.Goal, plan.Agent);
                if (lapsed.Count == 0)
                {
                    supervisor = RunSupervisor.Start(run, command, workspace.Path, environment, launcher, directory, WorkerOptions, AgentRuns.Amount(Element(plan.Cap), "wallMinutes") ?? 1, AgentRuns.Amount(Element(plan.Cap), "tokens"), limits, TimeSpan.FromSeconds(plan.StallSeconds), liveness => RecordStall(run, request.By, plan.Goal, liveness));
                }
            }
        }
        catch (RunLaunchException exception)
        {
            WriteOutcome uncertain = Revise(run, request.By, previous =>
            {
                previous["status"] = "uncertain";
                previous["dispatchedAt"] = RecordParser.FormatTimestamp(exception.LaunchedAt);
            });
            return new RunResponse(run, uncertain.Ok ? "uncertain" : "not-started", "The harness was created but launch observation failed; its full allowance remains reserved pending reconciliation.", false, uncertain.Written ?? intent, Element(plan.Cap), where);
        }
        catch (Exception exception) when (exception is ProviderRefusedException or IOException or UnauthorizedAccessException)
        {
            // The record says what happened without the reason's words, which can name paths on this machine.
            WriteOutcome failed = Revise(run, request.By, previous =>
            {
                previous["status"] = "known-failure";
                previous["observed"] = new JsonObject { ["summary"] = $"The {launcher.Launcher} harness could not be started on the machine that ran the worker, so nothing ran and nothing is charged.", ["at"] = RecordParser.FormatTimestamp(DateTimeOffset.UtcNow) };
                previous["usage"] = ZeroUsage(plan.Cap);
            });
            return new RunResponse(run, failed.Ok ? "known-failure" : "not-started", $"The {launcher.Launcher} harness could not be started: {exception.Message} The run is recorded as a known failure; nothing ran and nothing is charged.", false, failed.Written ?? intent, Element(plan.Cap), where, new Problem("provider", exception.Message, []));
        }

        if (supervisor is null)
        {
            // Cancelled before dispatch: the record names the devices and the exception, never the reasons' words, which name this machine's user.
            string ids = string.Join(", ", lapsed.Select(e => e.Device));
            string devices = lapsed.Count == 1 ? $"device {ids} of an operator or of the goal's sponsor signed without its owner's presence" : $"devices {ids} of an operator or of the goal's sponsor signed without their owners' presence";
            string named = excepted is null ? string.Empty : $", and the custody exception {excepted.Id} its intent named was no longer in force";
            WriteOutcome canceled = Revise(run, request.By, previous =>
            {
                previous["status"] = "canceled";
                previous["observed"] = new JsonObject { ["summary"] = $"Cancelled before dispatch: when the harness was about to start, {devices} on the machine that ran the worker, with no custody exception in force for the goal{named}. Nothing was launched and nothing is charged.", ["at"] = RecordParser.FormatTimestamp(DateTimeOffset.UtcNow) };
            });
            string refusal = CustodyRefusal(plan.Goal, lapsed);
            return new RunResponse(run, canceled.Ok ? "canceled" : "not-started", $"Run {run} was cancelled before dispatch: {refusal} Its intent is recorded as canceled; nothing was launched and nothing is charged.", false, canceled.Written ?? intent, Element(plan.Cap), where, new Problem("refused", refusal, []));
        }

        WriteOutcome dispatched;
        try
        {
            WorkerOptions.BeforeDispatchRecord?.Invoke();
            dispatched = RecordDispatch(run, request.By, supervisor.LaunchedAt);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            dispatched = Refuse("The launch was observed but its dispatch could not be persisted; its intent keeps the allowance reserved.");
        }

        var abandon = new CancellationTokenSource();
        supervisions[run] = (Task.Run(() => Supervise(run, request.By, plan.Goal, supervisor, workspaces, workspace, baseline, Strings(plan.Assignment.Body.GetProperty("brief"), "permittedChanges"), abandon.Token)), abandon);
        string generic = launcher.Generic ? $" The {launcher.Launcher} launcher is labeled: {IAgentLauncher.GenericLabel}." : string.Empty;
        if (excepted is not null)
        {
            generic += $" It was launched under {excepted.Description}; without it the worker would refuse: {string.Join(" ", excepted.Exposed.Select(e => e.Reason))}";
        }

        string recorded = dispatched.Ok ? string.Empty : $" Its dispatch could not be recorded ({dispatched.Problem?.Message}); the worker records what it observes when the run ends.";
        return new RunResponse(run, dispatched.Ok ? "dispatched" : "not-started", $"Run {run}: the intent was recorded with its cap before {launcher.Launcher} started as {plan.AgentName} in {where}; the worker observes it until it exits and records its usage. Its result is a claim another principal reviews; nothing is accepted.{generic}{recorded}", true, dispatched.Written ?? intent, Element(plan.Cap), where);
    }

    /// <inheritdoc />
    public RunsResponse Runs()
    {
        lock (gate)
        {
            EstateModel model = session.Load();
            string estate = session.Manifest.Estate;
            IReadOnlyList<AgentRuns.Run> runs = AgentRuns.All(model);
            var rows = new List<RunRow>();
            foreach (AgentRuns.Run run in runs)
            {
                var directory = new RunDirectory(estate, run.Id);
                RunObservedState? observed = directory.Read();
                RunStop? stop = RecordedStop(run.Latest) ?? (run.InFlight ? observed?.Stop ?? Requested(directory.ReadStop()) : null);
                rows.Add(new RunRow(
                    run.Id,
                    EstateModel.BodyString(run.Intent, "identity") ?? run.Id,
                    run.Launcher,
                    run.Principal,
                    run.Assignment,
                    run.Goal,
                    run.Workspace,
                    run.Status,
                    run.Cap.Clone(),
                    run.Usage.ValueKind == JsonValueKind.Object ? run.Usage.Clone() : null,
                    Element(Charges(AgentRuns.ChargeOf(run), run.Cap)),
                    stop,
                    run.InFlight ? observed?.Heartbeat : null,
                    run.InFlight && Fresh(observed),
                    run.Launcher == "generic" || Providers.Launcher(run.Launcher)?.Generic == true,
                    run.Latest.Body.TryGetProperty("liveness", out JsonElement liveness) ? JsonSerializer.Deserialize<RunLiveness>(liveness, ProtocolJson.Options) : observed?.Liveness,
                    run.Latest.Body.TryGetProperty("scope", out JsonElement scope) ? JsonSerializer.Deserialize<RunScope>(scope, ProtocolJson.Options) : null));
            }

            var ledgers = new List<LedgerRow>();
            foreach (RecordEnvelope goal in model.Current("goal").OrderBy(g => g.At, StringComparer.Ordinal).ThenBy(g => g.Id, StringComparer.Ordinal))
            {
                AgentRuns.Ledger ledger = AgentRuns.Of(model, goal);
                string status = GoalRules.Status(model, goal);
                if (ledger.Runs.Count == 0 && status != "active")
                {
                    continue;
                }

                List<LedgerUnit> units = AgentRuns.ChargedUnits.Where(ledger.Budget.ContainsKey)
                    .Select(u => new LedgerUnit(u, ledger.Budget[u], ledger.Spent.GetValueOrDefault(u), ledger.Remaining(u)!.Value, u == "currency" ? ledger.Currency : null)).ToList();
                List<string> fenced = AgentRuns.StartRefusals(model, goal, null, Element(AgentRuns.CapFor(ledger))).Select(r => r.Message).ToList();
                ledgers.Add(new LedgerRow(goal.Id, EstateModel.BodyString(goal, "title"), status, units, ledger.InFlight, ledger.Concurrency, ledger.AttemptsPerStep, fenced));
            }

            return new RunsResponse(rows, ledgers);
        }
    }

    /// <inheritdoc />
    public RunStopResponse RunStop(string run, RunStopRequest request)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(run);
        ArgumentNullException.ThrowIfNull(request);
        if (!Ulid.IsValid(request.By))
        {
            return StopProblem(run, "refused", "The principal asking for the stop is named by a ULID.");
        }

        if (request.GraceSeconds is < 0 or > 3600)
        {
            return StopProblem(run, "refused", "A grace period is from 0 to 3600 seconds.");
        }

        AgentRuns.Run? found;
        string estate;
        lock (gate)
        {
            EstateModel model = session.Load();
            if (model.CurrentPrincipal(request.By) is null)
            {
                return StopProblem(run, "refused", $"Principal {request.By} has no principal record in this estate.");
            }

            found = AgentRuns.Find(model, run);
            estate = session.Manifest.Estate;
        }

        if (found is null)
        {
            return StopProblem(run, "not-found", $"No agent run has id or identity {run}.");
        }

        if (!found.InFlight)
        {
            return new RunStopResponse(found.Id, found.Status, $"Run {found.Id} ended as {found.Status}; there is nothing to stop.", false, RecordedStop(found.Latest), new Problem("refused", $"Run {found.Id} ended as {found.Status}; there is nothing to stop.", []));
        }

        var directory = new RunDirectory(estate, found.Id);
        StopRequest asked = directory.RequestStop(new StopRequest(RecordParser.FormatTimestamp(DateTimeOffset.UtcNow), request.By, string.IsNullOrWhiteSpace(request.Reason) ? null : request.Reason.Trim(), request.GraceSeconds));
        RunObservedState? observed = ReadSteadily(directory);
        bool supervised = Fresh(observed);
        if (supervised)
        {
            // The supervising worker transmits the stop at its next heartbeat and escalates it after the grace period; this waits for what it observes.
            TimeSpan grace = asked.GraceSeconds is int seconds ? TimeSpan.FromSeconds(seconds) : WorkerOptions.Grace;
            DateTimeOffset until = DateTimeOffset.UtcNow + grace + (WorkerOptions.Heartbeat * 4) + TimeSpan.FromSeconds(10);
            // A state being replaced as it is read reads as nothing for a moment, which is no news: the last state read stands.
            while (DateTimeOffset.UtcNow < until)
            {
                RunObservedState? read = directory.Read();
                observed = read ?? observed;
                if (observed?.Stop?.ContainedAt is not null || observed?.ExitedAt is not null || (read is not null && !Fresh(read)))
                {
                    break;
                }

                Thread.Sleep(100);
            }

            // Once the exit is observed, the settlement follows; it records the stop's facts as observed.
            DateTimeOffset settling = DateTimeOffset.UtcNow + (WorkerOptions.Heartbeat * 2) + TimeSpan.FromSeconds(10);
            while (observed?.ExitedAt is not null && observed.Settled is null && observed.Problem is null && DateTimeOffset.UtcNow < settling)
            {
                Thread.Sleep(100);
                observed = directory.Read() ?? observed;
            }
        }

        RunStop stop = observed?.Stop is { } seen && seen.RequestedAt == asked.RequestedAt ? seen : new RunStop(asked.RequestedAt, asked.RequestedBy, asked.Reason);
        lock (gate)
        {
            if (AgentRuns.Find(session.Load(), found.Id) is { } settled && RecordedStop(settled.Latest) is { } recorded)
            {
                stop = recorded;
            }
        }

        string message = stop.State switch
        {
            "contained" => $"Run {found.Id}: the stop requested by {asked.RequestedBy} at {stop.RequestedAt} was transmitted at {stop.TransmittedAt} ({stop.Transmission}), {(stop.AcknowledgedAt is null ? "not acknowledged by the harness" : $"acknowledged at {stop.AcknowledgedAt} ({stop.Acknowledgment})")}, {(stop.EscalatedAt is null ? "not escalated" : $"escalated at {stop.EscalatedAt} ({stop.Escalation})")}, and observed contained at {stop.ContainedAt}.",
            "requested" => $"Run {found.Id}: the stop is requested at {stop.RequestedAt}, and no worker on this machine transmitted it: {(supervised ? "the worker that observed the run stopped observing it" : "none observes the run")}{(observed?.Heartbeat is string beat ? $", its last heartbeat at {beat}" : string.Empty)}. A stop that is not observed stays requested, and the run stays in flight, charged at its cap.",
            _ => $"Run {found.Id}: the stop requested at {stop.RequestedAt} is {stop.State}{(stop.TransmittedAt is null ? string.Empty : $" (transmitted at {stop.TransmittedAt}: {stop.Transmission})")}; its containment was not observed in time, so it is reported as {stop.State}, never as stopped.",
        };
        return new RunStopResponse(found.Id, stop.State, message, true, stop);
    }

    /// <summary>
    /// What the worker observing a run wrote, read through a moment in which it is being replaced: a state that is there and cannot be
    /// read yet is read again for up to a second before it counts as nothing.
    /// </summary>
    private static RunObservedState? ReadSteadily(RunDirectory directory)
    {
        for (int attempt = 0; ; attempt++)
        {
            RunObservedState? state = directory.Read();
            if (state is not null || attempt >= 20 || !File.Exists(directory.StateFile))
            {
                return state;
            }

            Thread.Sleep(50);
        }
    }

    /// <summary>A stop asked of a run that no worker has transmitted: reported as requested, never as stopped.</summary>
    private static RunStop? Requested(StopRequest? asked) => asked is null ? null : new RunStop(asked.RequestedAt, asked.RequestedBy, asked.Reason);

    /// <summary>The facts of the stop a revision of a run records, or null.</summary>
    private static RunStop? RecordedStop(RecordEnvelope revision)
    {
        if (!revision.Body.TryGetProperty("stop", out JsonElement stop) || stop.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        string? Text(string name) => stop.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
        return new RunStop(Text("requestedAt") ?? "?", Text("requestedBy") ?? "?", Text("reason"), Text("transmittedAt"), Text("transmission"), Text("acknowledgedAt"), Text("acknowledgment"), Text("escalatedAt"), Text("escalation"), Text("containedAt"));
    }

    /// <summary>True when a worker on this machine observes the run now: its state shows it running, with a recent heartbeat.</summary>
    private static bool Fresh(RunObservedState? observed) =>
        observed is { ExitedAt: null, Settled: null, Heartbeat: string beat }
        && DateTimeOffset.TryParseExact(beat, "yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out DateTimeOffset last)
        && DateTimeOffset.UtcNow - last <= TimeSpan.FromSeconds(Math.Max(3 * observed.HeartbeatSeconds, 2) + 2);

    /// <summary>The preflight of a run, before anything is prepared or recorded: every refusal that needs only the records and this machine's keys.</summary>
    private (RunPlan? Plan, string? Refusal, RecordEnvelope? Limited) Plan(EstateModel model, RunStartRequest request, IAgentLauncher launcher)
    {
        RecordEnvelope? assignment = Latest(model, request.Assignment, "assignment");
        if (assignment is null)
        {
            return (null, $"No assignment has id {request.Assignment}.", null);
        }

        RecordEnvelope? order = WorkView.OrderOfAssignment(model, assignment) is RecordEnvelope pinned ? model.Latest(pinned) : null;
        if (order is null)
        {
            return (null, $"Assignment {assignment.Id} delegates no work order of this estate.", null);
        }

        string key = EstateModel.BodyString(order, "key") ?? order.Id;
        string? contributor = EstateModel.BodyString(assignment, "contributor");
        if (request.Agent != contributor)
        {
            return (null, $"Assignment {assignment.Id} is {PrincipalName(model, contributor)}'s ({contributor}); a run carries an assignment as its contributor, so its agent is {contributor}.", null);
        }

        RecordEnvelope? agent = model.CurrentPrincipal(request.Agent);
        string kind = agent is null ? "missing" : EstateModel.BodyString(agent, "kind") ?? "?";
        if (kind != "agent")
        {
            return (null, $"Principal {request.Agent} is {(agent is null ? "not a principal of this estate" : $"a {kind} principal")}; the worker launches coding agents, and a person carries their own assignments.", null);
        }

        if (model.CurrentPrincipal(request.By) is null)
        {
            return (null, $"Principal {request.By} has no principal record in this estate.", null);
        }

        IReadOnlyList<IDeviceKeyStore> stores = DeviceKeyStores.All();
        if (!model.Current("device").Any(d => EstateModel.BodyString(d, "principal") == request.Agent && SignatureChecks.IsAdmitted(model, d) && stores.Any(s => s.Has(model.Chain(d)[0]))))
        {
            return (null, $"Agent {PrincipalName(model, request.Agent)} ({request.Agent}) has no admitted device whose key this machine holds, so its MCP server would list only the read-only tools and it could record no attempt and no result. Create its device here and have it admitted first.", null);
        }

        // The goal the run is charged to: the active goal whose work includes the work order, or the one the request names.
        string identity = model.Chain(order)[0];
        List<RecordEnvelope> goals = model.Current("goal").Where(g => GoalRules.Work(model, g).Any(o => model.Chain(o)[0] == identity)).OrderBy(g => g.Id, StringComparer.Ordinal).ToList();
        RecordEnvelope? goal;
        if (request.Goal is not null)
        {
            goal = GoalView.Find(model, request.Goal);
            if (goal is null || !goals.Any(g => g.Id == goal.Id))
            {
                return (null, $"Goal {request.Goal} {(goal is null ? "is no goal of this estate" : $"does not include work order {key} in its work")}.", null);
            }
        }
        else
        {
            List<RecordEnvelope> active = goals.Where(g => GoalRules.Status(model, g) == "active").ToList();
            goal = active.Count == 1 ? active[0] : active.Count == 0 && goals.Count == 1 ? goals[0] : null;
            if (goal is null)
            {
                return (null, goals.Count == 0
                    ? $"Work order {key} belongs to no goal, whose budget would cap the run: a goal that names it among its inputs or in its plan, activated by its sponsor, grants one."
                    : $"Work order {key} belongs to {(active.Count > 1 ? "several active goals" : "several goals")} ({string.Join(", ", (active.Count > 1 ? active : goals).Select(g => g.Id))}); name the one the run is charged to.", null);
            }
        }

        // A run is an external operation: recorded by the operator, under 'local-engineering', or by the goal's sponsor when a person,
        // who takes a step of the goal under its activation.
        GoalRules.Accepter decider = GoalRules.AccepterOf(model, goal);
        if (!model.HasRole(request.By, Roles.Operator) && !model.HasScope(request.By, Scopes.LocalEngineering, DateTimeOffset.UtcNow) && (decider.Principal is null || decider.Principal != request.By))
        {
            return (null, $"A run is an external operation, recorded by the operator, under the 'local-engineering' scope, or by the sponsor of the goal it is charged to when the sponsor is a person, and {PrincipalName(model, request.By)} ({request.By}) is none of these.", null);
        }

        // The custody precondition, judged here before anything is prepared and again when the intent is recorded: no operator's or
        // sponsor's key on this machine that signs without its owner's presence, unless the goal's sponsor recorded an exception in
        // force for the goal, which the run then names.
        IReadOnlyList<(string Device, string Reason)> exposed = CustodyGuard.Exposed(model, CustodyGuard.Guarded(model, goal, request.Agent), stores, Environment.UserName);
        if (exposed.Count > 0 && CustodyExceptions.InForce(model, goal, DateTimeOffset.UtcNow) is null)
        {
            return (null, CustodyRefusal(goal.Id, exposed), null);
        }

        AgentRuns.Ledger ledger = AgentRuns.Of(model, goal);
        JsonObject cap = AgentRuns.CapFor(ledger, request.WallMinutes);
        IReadOnlyList<(string Code, string Message)> refusals = AgentRuns.StartRefusals(model, goal, assignment.Id, Element(cap));
        if (refusals.Count > 0)
        {
            IReadOnlyList<string> step = model.Chain(assignment);
            bool crossed = Crossed(ledger).Count > 0 || ledger.Attempts(step) >= ledger.AttemptsPerStep;
            return (null, string.Join(" ", refusals.Select(r => r.Message)), crossed && GoalRules.Status(model, goal) == "active" ? goal : null);
        }

        RecordEnvelope? resource = Latest(model, request.Repository, "resource");
        if (resource is null || EstateModel.BodyString(resource, "kind") != "repository")
        {
            return (null, $"No repository resource has id {request.Repository}.", null);
        }

        string estate = session.Manifest.Estate;
        string? clone = model.Chain(resource).Append(request.Repository).Select(id => RepositoryClones.Find(estate, id)).FirstOrDefault(c => c is not null);
        if (clone is null || !Directory.Exists(clone))
        {
            return (null, $"This machine has no registered clone of resource {resource.Id}; register one with vericairn sources clone <path> --resource {resource.Id}, and the workspace is made from it.", null);
        }

        string repository = resource.Body.TryGetProperty("location", out JsonElement location) && location.ValueKind == JsonValueKind.Object && location.TryGetProperty("github", out JsonElement github) && github.ValueKind == JsonValueKind.String
            ? github.GetString()!
            : EstateModel.BodyString(resource, "name") ?? resource.Id;
        string branch = string.IsNullOrWhiteSpace(request.Branch) ? "work/" + Slug(key).ToLowerInvariant() : request.Branch.Trim();
        if (AgentRuns.All(model).FirstOrDefault(r => r.InFlight && r.Target("repository") is string other && model.Chain(resource).Contains(other) && r.Target("branch") == branch) is { } busy)
        {
            return (null, $"Run {busy.Id} works on branch {branch} of {repository} and is in flight; one run works in a workspace at a time, so name another branch or wait for it to settle.", null);
        }

        return (new RunPlan(estate, assignment, key, goal.Id, request.Agent, PrincipalName(model, request.Agent), cap, clone, resource.Id, repository, branch, WorkView.RenderBrief(model, order), Condition(order, assignment, goal), StallSeconds(goal, request.Settings, launcher)), null, null);
    }

    private static int StallSeconds(RecordEnvelope goal, IReadOnlyDictionary<string, string>? settings, IAgentLauncher launcher)
    {
        if (goal.Body.TryGetProperty("limits", out JsonElement limits) && AgentRuns.Amount(limits, "stallSeconds") is long goalStall)
        {
            return (int)goalStall;
        }

        return settings?.TryGetValue("stall-seconds", out string? stall) == true
            ? int.Parse(stall, CultureInfo.InvariantCulture)
            : (int)launcher.StallLimit.TotalSeconds;
    }

    /// <summary>
    /// The custody precondition judged immediately before a launch: the devices of an operator or of the goal's sponsor, at the goal's
    /// current revision, whose keys sign here without their owners' presence, when no custody exception is in force for the goal now;
    /// empty when the launch may go on.
    /// </summary>
    private static IReadOnlyList<(string Device, string Reason)> Lapsed(EstateModel model, string goalId, string agent)
    {
        if (AgentRuns.GoalAt(model, goalId) is not RecordEnvelope goal)
        {
            return [];
        }

        IReadOnlyList<(string Device, string Reason)> exposed = CustodyGuard.Exposed(model, CustodyGuard.Guarded(model, goal, agent), DeviceKeyStores.All(), Environment.UserName);
        return exposed.Count > 0 && CustodyExceptions.InForce(model, goal, DateTimeOffset.UtcNow) is null ? exposed : [];
    }

    /// <summary>The custody guard's refusal of an unattended run of a goal, with each device it found exposed and the way to an exception.</summary>
    private static string CustodyRefusal(string goal, IReadOnlyList<(string Device, string Reason)> exposed) =>
        "The worker launches nothing while a device of an operator or of the goal's sponsor can sign without its owner's presence on this machine (design/goals.md § 10): " + string.Join(" ", exposed.Select(e => e.Reason)) + $" Or the goal's sponsor records a custody exception for the goal, with its reason and its expiry: vericairn goal custody-exception {goal} --by <sponsor> --reason <why> --expires <time>.";

    /// <summary>The units in which the goal's settled runs alone reached its budget: a cap crossed, not one held by runs in flight.</summary>
    private static List<string> Crossed(AgentRuns.Ledger ledger)
    {
        var settled = new Dictionary<string, long>(StringComparer.Ordinal);
        foreach (AgentRuns.Run run in ledger.Runs.Where(r => !r.InFlight))
        {
            foreach ((string unit, long value) in AgentRuns.ChargeOf(run))
            {
                settled[unit] = settled.GetValueOrDefault(unit) + value;
            }
        }

        return ledger.Budget.Where(b => settled.GetValueOrDefault(b.Key) >= b.Value).Select(b => b.Key).ToList();
    }

    /// <summary>
    /// The condition the harness's /goal runs with: the work order's exit question, with the review criteria of the assignment's brief as
    /// its checks and the brief's prohibited effects and the goal's constraints as its constraints, on one line.
    /// </summary>
    private static string Condition(RecordEnvelope order, RecordEnvelope assignment, RecordEnvelope goal)
    {
        JsonElement brief = assignment.Body.TryGetProperty("brief", out JsonElement b) ? b : default;
        List<string> checks = Strings(brief, "reviewCriteria");
        List<string> constraints = Strings(brief, "prohibitedEffects").Concat(Strings(goal.Body, "constraints")).Distinct(StringComparer.Ordinal).ToList();
        var text = new StringBuilder();
        text.Append(EstateModel.BodyString(order, "exitQuestion") ?? "Is the work order's outcome reached?");
        text.Append(" Checks: ");
        foreach (string check in checks)
        {
            text.Append(check.TrimEnd('.', ' ')).Append("; ");
        }

        text.Append("an attempt and its result recorded with the vericairn MCP server's work_attempt and work_result tools. Constraints: ");
        foreach (string constraint in constraints)
        {
            text.Append(constraint.TrimEnd('.', ' ')).Append("; ");
        }

        text.Append("only the changes the brief in .vericairn/brief.md permits.");
        string line = Regex.Replace(text.ToString(), @"\s+", " ").Trim();
        return line.Length <= 6000 ? line : line[..5999] + "…";
    }

    /// <summary>Records a goal budget-limited, by the run's recording principal, when it is active: crossing a cap fences new runs and asks the sponsor to raise it or stop.</summary>
    private void MarkBudgetLimited(EstateModel model, RecordEnvelope goal, string by, string why)
    {
        RecordEnvelope current = model.Latest(goal);
        if (GoalRules.Status(model, current) != "active")
        {
            return;
        }

        string note = $"Its agent runs reached its budget: {why} New runs are fenced until its sponsor raises the budget by a revision and activates it, or stops it.";
        var body = new JsonObject { ["status"] = "budget-limited", ["note"] = note.Length > 4000 ? note[..3999] + "…" : note };
        WriteLocked(new RecordDraft("goal-status", by, Element(body), current.Reference), model);
    }

    /// <summary>Records one attention ask for a worker-observed stall, without claiming the agent is blocked or notifying anyone.</summary>
    private string? RecordStall(string run, string by, string goalId, RunLiveness liveness)
    {
        lock (gate)
        {
            EstateModel model = session.Load();
            RecordEnvelope? existing = model.Current("ask").FirstOrDefault(a => a.Subject?.Type == "operation" && a.Subject.Id == run);
            if (existing is not null)
            {
                return existing.Id;
            }

            AgentRuns.Run? found = AgentRuns.Find(model, run);
            RecordEnvelope? goal = AgentRuns.GoalAt(model, goalId);
            if (found is null || goal is null)
            {
                return null;
            }

            string to = GoalRules.AccepterOf(model, goal).Principal ?? by;
            var body = new JsonObject
            {
                ["to"] = to,
                ["goal"] = goal.Id,
                ["state"] = "open",
                ["question"] = $"Run {run} needs attention: at {liveness.AttentionAt} the worker observed no stdout event for {liveness.StallSeconds.ToString(CultureInfo.InvariantCulture)} seconds. Inspect the run?",
                ["recommendation"] = "Inspect the run's locally retained output and its assignment before deciding whether another attempt is needed.",
                ["consequence"] = "If the event stream stays silent for twice the stall limit, the worker stops and contains the run. Its allowance stays reserved until observed settlement; silence is the worker's observation, never the agent's claim.",
            };
            WriteOutcome written = WriteLocked(new RecordDraft("ask", by, Element(body), found.Intent.Reference), model);
            return written.Ok ? written.Written!.Id : null;
        }
    }

    /// <summary>Observes a run until its exit, then records its settlement, and marks its goal budget-limited when it crossed a cap.</summary>
    private void Supervise(string run, string by, string goalId, RunSupervisor supervisor, IWorkspaceProvider workspaces, PreparedWorkspace workspace, WorkspaceSnapshot baseline, IReadOnlyList<string> permitted, CancellationToken abandon)
    {
        RunOutcome? outcome;
        try
        {
            outcome = supervisor.Supervise(abandon);
        }
        catch (Exception exception) when (exception is InvalidOperationException or IOException or System.ComponentModel.Win32Exception)
        {
            supervisor.Settled(null, $"The worker lost its observation of the run: {exception.Message}");
            return;
        }

        if (outcome is null)
        {
            return;
        }

        RunScope scope;
        try
        {
            scope = workspaces.CheckScope(workspace, baseline, permitted);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ProviderRefusedException or RegexMatchTimeoutException)
        {
            scope = new RunScope(0, 0, [], "The workspace scope audit could not complete; scope is unverified.");
        }

        try
        {
            lock (gate)
            {
                if (AgentRuns.Find(session.Load(), run) is { Status: "not-started" })
                {
                    WriteOutcome recovered = RecordDispatch(run, by, outcome.LaunchedAt);
                    if (!recovered.Ok)
                    {
                        supervisor.Settled(null, "The observed dispatch could not be recorded; the allowance remains reserved.");
                        return;
                    }
                }
            }

            string? status = null;
            WriteOutcome settled = Revise(run, by, previous => status = Settle(previous, outcome, scope)["status"]?.GetValue<string>());
            supervisor.Settled(settled.Ok ? status : null, settled.Ok ? null : settled.Problem?.Message);
            if (!settled.Ok)
            {
                return;
            }

            lock (gate)
            {
                EstateModel model = session.Load();
                if (AgentRuns.GoalAt(model, goalId) is RecordEnvelope goal && Crossed(AgentRuns.Of(model, goal)) is { Count: > 0 } crossed)
                {
                    AgentRuns.Ledger ledger = AgentRuns.Of(model, goal);
                    MarkBudgetLimited(model, goal, by, string.Join("; ", crossed.Select(ledger.Describe)) + ".");
                }
            }
        }
        catch (Exception exception) when (exception is ObjectDisposedException or IOException or InvalidOperationException)
        {
            supervisor.Settled(null, exception.Message);
        }
    }

    private WriteOutcome RecordDispatch(string run, string by, DateTimeOffset launchedAt) => Revise(run, by, previous =>
    {
        previous["status"] = "dispatched";
        previous["dispatchedAt"] = RecordParser.FormatTimestamp(launchedAt);
    });

    /// <summary>The settlement of a run: its observed effect, its usage unit by unit, the units not reported, and a stop's facts.</summary>
    private JsonObject Settle(JsonObject body, RunOutcome outcome, RunScope scope)
    {
        JsonElement cap = body["cap"] is JsonNode node ? Element(node.AsObject()) : default;
        TimeSpan elapsed = outcome.Elapsed ?? (outcome.ExitedAt - outcome.LaunchedAt);
        long minutes = (long)Math.Ceiling(Math.Max(elapsed.Ticks, 0) / (double)WorkerOptions.Minute.Ticks);
        var usage = new JsonObject { ["wallMinutes"] = minutes };
        var unreported = new JsonArray();
        if (outcome.Tokens is long tokens)
        {
            usage["tokens"] = tokens;
        }
        else if (AgentRuns.Amount(cap, "tokens") is not null)
        {
            unreported.Add("tokens");
        }

        if (outcome.Price is long price && outcome.PriceUnit is string unit && (AgentRuns.CurrencyUnit(cap) ?? unit) == unit)
        {
            usage["currency"] = new JsonObject { ["amount"] = price, ["unit"] = unit };
        }
        else if (AgentRuns.Amount(cap, "currency") is not null)
        {
            unreported.Add("currency");
        }

        usage["unreported"] = unreported;
        string took = elapsed.TotalSeconds < 120 ? $"{Math.Round(elapsed.TotalSeconds).ToString(CultureInfo.InvariantCulture)} seconds" : $"{Math.Round(elapsed.TotalMinutes).ToString(CultureInfo.InvariantCulture)} minutes";
        string summary;
        if (outcome.Stop is RunStop stop)
        {
            body["status"] = "known-failure";
            body["stop"] = Stop(stop);
            summary = $"Stopped at the request of {(stop.RequestedBy == "worker" ? "the worker" : stop.RequestedBy)}{(stop.Reason is null ? string.Empty : ": " + stop.Reason)}. The stop was transmitted at {stop.TransmittedAt ?? "no time"}, {(stop.AcknowledgedAt is null ? "not acknowledged" : "acknowledged at " + stop.AcknowledgedAt)}, {(stop.EscalatedAt is null ? "not escalated" : "escalated at " + stop.EscalatedAt)}, and observed contained at {stop.ContainedAt}; the harness exited with code {outcome.ExitCode.ToString(CultureInfo.InvariantCulture)} after {took}.";
        }
        else
        {
            body["status"] = outcome.ExitCode == 0 ? "observed-success" : "known-failure";
            summary = outcome.ExitCode == 0
                ? $"The harness exited with code 0 after {took}. That it ran to its end is all this records: its claim of done is its result, which another principal reviews."
                : $"The harness exited with code {outcome.ExitCode.ToString(CultureInfo.InvariantCulture)} after {took}.";
        }

        if (scope.OutsideCount > 0 || scope.Problem is not null)
        {
            summary += $" Scope attention: {scope.OutsideCount.ToString(CultureInfo.InvariantCulture)} changed paths outside the assignment's permitted paths. " + scope.Problem;
        }

        body["scope"] = JsonSerializer.SerializeToNode(scope, RunRecordJson);
        body["observed"] = new JsonObject { ["summary"] = summary, ["at"] = RecordParser.FormatTimestamp(outcome.ExitedAt) };
        body["dispatchedAt"] ??= RecordParser.FormatTimestamp(outcome.LaunchedAt);
        body["usage"] = usage;
        if (outcome.Liveness is not null)
        {
            body["liveness"] = JsonSerializer.SerializeToNode(outcome.Liveness, RunRecordJson);
        }

        JsonArray evidence = body["evidence"] as JsonArray ?? [];
        evidence.Add(new JsonObject { ["summary"] = $"Observed by the worker on the machine that ran it at {outcome.Heartbeats.ToString(CultureInfo.InvariantCulture)} heartbeats every {WorkerOptions.Heartbeat.TotalSeconds.ToString(CultureInfo.InvariantCulture)} seconds, from {RecordParser.FormatTimestamp(outcome.LaunchedAt)} to its exit at {RecordParser.FormatTimestamp(outcome.ExitedAt)}; its output is kept on that machine." });
        if (outcome.EscalationFailure is string failure)
        {
            evidence.Add(new JsonObject { ["summary"] = failure });
        }

        body["evidence"] = evidence;
        return body;
    }

    private static JsonObject Stop(RunStop stop)
    {
        var json = new JsonObject { ["requestedAt"] = stop.RequestedAt, ["requestedBy"] = stop.RequestedBy };
        void Add(string name, string? value)
        {
            if (value is not null)
            {
                json[name] = value.Length > 2000 ? value[..1999] + "…" : value;
            }
        }

        Add("reason", stop.Reason);
        Add("transmittedAt", stop.TransmittedAt);
        Add("transmission", stop.Transmission);
        Add("acknowledgedAt", stop.AcknowledgedAt);
        Add("acknowledgment", stop.Acknowledgment);
        Add("escalatedAt", stop.EscalatedAt);
        Add("escalation", stop.Escalation);
        Add("containedAt", stop.ContainedAt);
        return json;
    }

    /// <summary>Writes the next revision of a run's operation from its latest, as the change makes it.</summary>
    private WriteOutcome Revise(string run, string by, Action<JsonObject> change)
    {
        lock (gate)
        {
            EstateModel model = session.Load();
            AgentRuns.Run? found = AgentRuns.Find(model, run);
            if (found is null)
            {
                return Refuse($"No agent run has id {run}.");
            }

            JsonObject body = JsonNode.Parse(found.Latest.Body.GetRawText())!.AsObject();
            change(body);
            return WriteLocked(new RecordDraft("operation", by, Element(body), found.Latest.Subject, found.Latest.Id), model);
        }
    }

    /// <summary>The usage of a run that never started: nothing in each unit its cap states.</summary>
    private static JsonObject ZeroUsage(JsonObject cap)
    {
        var usage = new JsonObject { ["wallMinutes"] = 0 };
        if (cap.ContainsKey("tokens"))
        {
            usage["tokens"] = 0;
        }

        if (cap["currency"] is JsonObject currency)
        {
            usage["currency"] = new JsonObject { ["amount"] = 0, ["unit"] = currency["unit"]?.GetValue<string>() };
        }

        usage["unreported"] = new JsonArray();
        return usage;
    }

    /// <summary>A run's charge by unit, as JSON in the units of its cap.</summary>
    private static JsonObject Charges(IReadOnlyDictionary<string, long> charge, JsonElement cap)
    {
        var json = new JsonObject();
        foreach ((string unit, long value) in charge.OrderBy(c => Array.IndexOf(AgentRuns.ChargedUnits, c.Key)))
        {
            json[unit] = unit == "currency" ? new JsonObject { ["amount"] = value, ["unit"] = AgentRuns.CurrencyUnit(cap) } : value;
        }

        return json;
    }

    /// <summary>Leaves every run this service supervises running and unobserved, and waits until each supervision has let go.</summary>
    private void AbandonRuns()
    {
        var tasks = new List<Task>();
        foreach ((string _, (Task task, CancellationTokenSource abandon)) in supervisions)
        {
            abandon.Cancel();
            tasks.Add(task);
        }

        try
        {
            Task.WaitAll([.. tasks], WorkerOptions.Heartbeat * 2 + TimeSpan.FromSeconds(10));
        }
        catch (AggregateException)
        {
            // A supervision that failed has let go too.
        }

        foreach ((string _, (Task _, CancellationTokenSource abandon)) in supervisions)
        {
            abandon.Dispose();
        }

        supervisions.Clear();
    }

    private static string Slug(string text) => Regex.Replace(text, "[^A-Za-z0-9._-]+", "-").Trim('-');

    private static List<string> Strings(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out JsonElement values) && values.ValueKind == JsonValueKind.Array
            ? values.EnumerateArray().Where(v => v.ValueKind == JsonValueKind.String).Select(v => v.GetString()!).ToList()
            : [];

    private static string PrincipalName(EstateModel model, string? id) =>
        model.CurrentPrincipal(id) is RecordEnvelope principal ? EstateModel.BodyString(principal, "name") ?? id ?? "?" : id ?? "?";

    private static RunResponse RunProblem(string run, string status, string code, string message) =>
        new(run, status, message, false, Problem: new Problem(code, message, []));

    private static RunStopResponse StopProblem(string run, string code, string message) =>
        new(run, "none", message, false, Problem: new Problem(code, message, []));

    /// <summary>What the preflight found: everything a run needs that the records give.</summary>
    private sealed record RunPlan(string Estate, RecordEnvelope Assignment, string Key, string Goal, string Agent, string AgentName, JsonObject Cap, string ClonePath, string Resource, string Repository, string Branch, string Brief, string Condition, int StallSeconds);

    /// <summary>The sponsor's custody exception a run is launched under, in force when its intent is recorded, and the devices that would otherwise refuse it here.</summary>
    private sealed record Excepted(string Id, string Description, string Expires, IReadOnlyList<(string Device, string Reason)> Exposed);
}
