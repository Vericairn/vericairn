// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using AskState = Vericairn.Records.Estate.Asks;
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

        RunConfiguration? configuration;
        try { configuration = Configuration(launcher, request.Configuration); }
        catch (ProviderRefusedException exception)
        {
            return RunProblem(pending, "not-started", "refused", exception.Message + " Nothing was recorded.");
        }
        if (configuration is null || configuration.Launcher != launcher.Launcher || (request.Configuration is not null && request.Configuration != configuration.Name))
        {
            string configure = launcher.Launcher is "codex" or "claude-code"
                ? $" Configure it on this machine with vericairn worker configure --launcher {launcher.Launcher} --model <model> --effort <effort>."
                : string.Empty;
            return RunProblem(pending, "not-started", "refused", "No matching explicit launcher configuration is available; ambient model and effort choices are not permitted." + configure + " Nothing was recorded.");
        }

        var settings = new Dictionary<string, string>(configuration.Settings ?? new Dictionary<string, string>(StringComparer.Ordinal), StringComparer.Ordinal);
        foreach ((string key, string value) in request.Settings ?? new Dictionary<string, string>(StringComparer.Ordinal)) settings[key] = value;
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
            List<RecordFile> files = session.Files().Concat(carried.Select(r => new RecordFile(r.RelativePath, r.Bytes))).OrderBy(f => f.Path, StringComparer.Ordinal).ToList();
            EstateModel model = EstateLoader.Load(session.Root, files);
            (RunPlan? planned, string? refusal, RecordEnvelope? limited) = Plan(model, files, request, launcher, configuration);
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

        if (!launcher.ExecutionCapabilities.ClosedEnvironment)
        {
            return RunProblem(pending, "not-started", "refused", "This launcher cannot establish a complete tool, configuration, dependency and route inventory; dispatch is unsupported. Nothing was recorded or executed.");
        }

        // Even feature probes need a launcher that can account for what it loads; unsupported implementations execute nothing.
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
        string? configuredRoot;
        try
        {
            configuredRoot = new WorkerConfigurationStore().ReadWorkspaceRoot();
            string root = configuredRoot ?? Path.Combine(VericairnHome.Path(), "estates", plan.Estate, "workspaces");
            string path = configuredRoot is null ? Path.Combine(root, Slug(plan.Branch)) : Path.Combine(root, plan.Estate, Slug(plan.Branch));
            workspace = workspaces.Prepare(plan.ClonePath, plan.Branch, request.Base, path, root);
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

        string run = Ulid.NewUlid().Value;
        var directory = new RunDirectory(plan.Estate, run);
        IReadOnlyList<string> server = [.. WorkerOptions.VericairnCommand(), "--estate", session.Root, "mcp", "serve", "--as", plan.Agent];
        var serverEnvironment = WorkerEnvironment.McpServer.Where(environment.ContainsKey).ToDictionary(n => n, n => environment[n], StringComparer.Ordinal);
        environment["VERICAIRN_RUN"] = run;
        environment["VERICAIRN_STOP_FILE"] = directory.StopFile;
        environment["VERICAIRN_BRIEF"] = brief;
        environment["VERICAIRN_MCP_CONFIG"] = directory.McpConfiguration;
        environment["VERICAIRN_CONDITION"] = plan.Condition;
        LaunchContext launch;
        RunPreflight preflight;
        RunEnvironmentPolicy? environmentPolicy = configuration.EnvironmentPolicy;
        string? admittedBinding;
        try
        {
            directory.Create();
            directory.RetainContext(plan.Context, plan.Brief);
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
            launch = launcher.Prepare(new LaunchContext(workspace.Path, plan.Condition, brief, directory.McpConfiguration, server, serverEnvironment, settings, environment, limits, Element(plan.Selection)));
            environment = new Dictionary<string, string>(launch.Environment, WorkerEnvironment.Names);
            preflight = launcher.Probe(launch);
            lock (gate)
            {
                if (HarnessAdmission.Admit(session.Load(), launcher.Launcher, preflight) is JsonObject admission)
                {
                    plan.Selection["harnessAdmission"] = admission;
                    environmentPolicy ??= new("qualified-" + launcher.Launcher, preflight.Components);
                }
            }
            admittedBinding = preflight.LocalBindingHash;
            RunSelection.ResolveRevision(plan.Selection, preflight.ModelRevision);
            plan.Selection["harnessVersion"] = preflight.HarnessVersion;
            plan.Selection["environment"] = RunEnvironment.Admit(environmentPolicy, preflight, environment, [plan.Selection["requested"]!["destination"]!.GetValue<string>(), "estate", "local"], launcher.EnvironmentNames);
            launch = launch with { Execution = Element(plan.Selection) };
        }
        catch (Exception exception) when (exception is ProviderRefusedException or IOException or UnauthorizedAccessException)
        {
            return RunProblem(pending, "not-started", "refused", $"Execution preflight refused: {exception.Message} Nothing was recorded or dispatched.");
        }

        // Record the intent before anything starts, judged again against the records as they are now, after the preparation: the
        // step still unassigned when routing's assignment comes with the run, the goal at its current revision, the custody guard
        // over the keys held here and the exception in force now, which the intent names, and the cap. The run is its intent.
        RecordWritten intent;
        Excepted? excepted = null;
        lock (gate)
        {
            if (WorkspaceRootProblem(configuredRoot) is string rootProblem)
                return RunProblem(pending, "not-started", "refused", rootProblem + " Nothing was recorded.");
            EstateModel model = LoadWith(carried);
            RecordEnvelope? goal = AgentRuns.GoalAt(model, plan.Goal);
            if (AssignmentRefusal(model, plan.Assignment, plan.Key) is string assignmentRefusal)
            {
                return RunProblem(pending, "not-started", "refused", assignmentRefusal + " Nothing was recorded.");
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

            if (!SelectionStillCurrent(model, goal!, plan, configuration, launcher, request))
            {
                return RunProblem(pending, "not-started", "refused", "Execution defaults or restrictions changed during preparation; start a newly admitted run. Nothing was recorded.");
            }

            if (!HarnessAdmission.StillCurrent(model, launcher.Launcher, preflight, plan.Selection["harnessAdmission"]))
            {
                return RunProblem(pending, "not-started", "refused", "Harness qualification changed during preparation; nothing was recorded or dispatched.");
            }

            RunWatchSettings watch = RunWatchSettings.Resolve(model, goal!, launcher);
            if (watch.Refusal is string watchRefusal)
            {
                return RunProblem(pending, "not-started", "refused", watchRefusal + " Nothing was recorded.");
            }

            plan = plan with { StallSeconds = StallSeconds(model, goal!, settings, launcher), Watch = watch,
                HistoryRate = RunWatch.History(AgentRuns.Of(model, goal!).Runs, launcher.Launcher) };
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
            target["watch"] = JsonSerializer.Serialize(plan.Watch, ProtocolJson.Options);
            if (RunWatchSettings.Roster(model, goal!) is RecordEnvelope watchRoster)
            {
                target["watchRoster"] = watchRoster.Id;
            }

            target["watchHistoryTokensPerMinute"] = plan.HistoryRate?.ToString(CultureInfo.InvariantCulture) ?? "unavailable";
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
                ["execution"] = plan.Selection.DeepClone(),
            };
            ((JsonArray)body["evidence"]!).Add(new JsonObject { ["summary"] = limits.Describe(Element(plan.Cap)) });
            ((JsonArray)body["evidence"]!).Add(new JsonObject { ["summary"] = launcher.Limitations });
            if (excepted is not null)
            {
                ((JsonArray)body["evidence"]!).Add(new JsonObject { ["summary"] = $"Launched unattended under the sponsor's custody exception {excepted.Id}, in force until {excepted.Expires}, while {(excepted.Exposed.Count == 1 ? "device" : "devices")} {string.Join(", ", excepted.Exposed.Select(e => e.Device))} of an operator or of the goal's sponsor {(excepted.Exposed.Count == 1 ? "signs" : "sign")} without {(excepted.Exposed.Count == 1 ? "its" : "their")} owner's presence on the machine that ran the worker." });
            }

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
        RunSupervisor? supervisor = null;
        HashSet<string> priorAttempts = new(StringComparer.Ordinal);
        IReadOnlyList<(string Device, string Reason)> lapsed = [];
        try
        {
            IReadOnlyList<string> command = launcher.Command(launch);

            // The custody precondition once more, immediately before the launch and under the gate, so that no exception is revised
            // or expires between this judgment and the start: one that lapsed since the intent, or a key that arrived, launches nothing.
            lock (gate)
            {
                if (WorkspaceRootProblem(configuredRoot) is string rootProblem) throw new ProviderRefusedException(rootProblem);
                EstateModel dispatchModel = session.Load();
                RecordEnvelope? dispatchGoal = AgentRuns.GoalAt(dispatchModel, plan.Goal);
                if (dispatchGoal is null || !SelectionStillCurrent(dispatchModel, dispatchGoal, plan, configuration, launcher, request))
                {
                    throw new ProviderRefusedException("Execution defaults or restrictions changed after the intent; no harness was dispatched.");
                }
                RunPreflight dispatchPreflight = launcher.Probe(launch);
                // Probes start subprocesses and can take time. Judge authority again from the records after they finish.
                if (WorkspaceRootProblem(configuredRoot) is string changedRoot) throw new ProviderRefusedException(changedRoot);
                dispatchModel = session.Load();
                dispatchGoal = AgentRuns.GoalAt(dispatchModel, plan.Goal);
                if (dispatchGoal is null || !SelectionStillCurrent(dispatchModel, dispatchGoal, plan, configuration, launcher, request))
                {
                    throw new ProviderRefusedException("Execution defaults or restrictions changed during the final probes; no harness was dispatched.");
                }
                JsonObject? dispatchAdmission = HarnessAdmission.Admit(dispatchModel, launcher.Launcher, dispatchPreflight);
                JsonObject dispatchEnvironment = RunEnvironment.Admit(environmentPolicy, dispatchPreflight, environment,
                    [plan.Selection["requested"]!["destination"]!.GetValue<string>(), "estate", "local"], launcher.EnvironmentNames);
                if (!JsonNode.DeepEquals(dispatchEnvironment, plan.Selection["environment"])
                    || !JsonNode.DeepEquals(dispatchAdmission, plan.Selection["harnessAdmission"])
                    || (dispatchPreflight.ModelRevision ?? "unknown") != plan.Selection["resolvedRevision"]!.GetValue<string>()
                    || dispatchPreflight.HarnessVersion != plan.Selection["harnessVersion"]!.GetValue<string>()
                    || dispatchPreflight.LocalBindingHash != admittedBinding)
                {
                    throw new ProviderRefusedException("The effective inventory or model resolution changed after admission; no harness was dispatched.");
                }
                lapsed = Lapsed(dispatchModel, plan.Goal, plan.Agent);
                if (lapsed.Count == 0)
                {
                    // Record timestamps have second precision. Keep identities too, so an earlier attempt in the same second cannot count.
                    priorAttempts = dispatchModel.OfType("attempt").Select(a => a.Id).ToHashSet(StringComparer.Ordinal);
                    supervisor = RunSupervisor.Start(run, command, workspace.Path, environment, launcher, directory, WorkerOptions, AgentRuns.Amount(Element(plan.Cap), "wallMinutes") ?? 1, AgentRuns.Amount(Element(plan.Cap), "tokens"), limits, TimeSpan.FromSeconds(plan.StallSeconds), liveness => RecordStall(run, request.By, plan.Goal, liveness), Strings(plan.Assignment.Body.GetProperty("brief"), "permittedChanges"), plan.Watch, plan.HistoryRate, signal => RecordSignal(run, request.By, plan.Goal, signal), Element(plan.Selection));
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
                previous["executionObserved"] = new RunExecutionObservation(Element(plan.Selection)).Snapshot();
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
        supervisions[run] = (Task.Run(() => Supervise(run, request.By, plan.Goal, supervisor, workspaces, workspace, baseline, Strings(plan.Assignment.Body.GetProperty("brief"), "permittedChanges"), priorAttempts, abandon.Token)), abandon);
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
            return Runs(model);
        }
    }

    // Caller holds the service gate; progress uses its already loaded model and still reads current worker observations.
    private RunsResponse Runs(EstateModel model)
    {
        string estate = session.Manifest.Estate;
        IReadOnlyList<AgentRuns.Run> runs = AgentRuns.All(model);
        var rows = new List<RunRow>();
        foreach (AgentRuns.Run run in runs)
        {
            var directory = new RunDirectory(estate, run.Id);
            RunObservedState? observed = directory.Read();
            RunStop? stop = RecordedStop(run.Latest) ?? (run.InFlight ? observed?.Stop ?? Requested(directory.ReadStop()) : null);
            IReadOnlyList<RunSignal>? signals = run.Latest.Body.TryGetProperty("signals", out JsonElement recordedSignals)
                ? JsonSerializer.Deserialize<RunSignal[]>(recordedSignals, ProtocolJson.Options) : observed?.Signals;
            if (!run.InFlight && observed is { Settled: not null, Problem: string problem })
            {
                // A post-settlement local transfer failure is visible without rewriting the immutable settlement or adding
                // machine bindings to its records. The provider's refusal is path-free.
                signals = [.. signals ?? [], new RunSignal("audit-unverified", "warning", observed.ExitedAt ?? run.Latest.At, problem)];
            }
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
                run.Latest.Body.TryGetProperty("scope", out JsonElement scope) ? JsonSerializer.Deserialize<RunScope>(scope, ProtocolJson.Options) : null,
                signals,
                run.Intent.Body.TryGetProperty("execution", out JsonElement execution) ? execution.Clone() : null,
                run.Latest.Body.TryGetProperty("executionObserved", out JsonElement executionObserved) ? executionObserved.Clone() : null));
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
            if (found is not null && StopRefusal(model, found, request.By) is string refused)
            {
                return StopProblem(run, "refused", refused);
            }
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
        StopRequest asked;
        lock (gate)
        {
            if (RequestDeviceRefusal(session.Load()) is Problem denied)
            {
                return StopProblem(run, denied.Code, denied.Message);
            }

            asked = directory.RequestStop(new StopRequest(RecordParser.FormatTimestamp(DateTimeOffset.UtcNow), request.By, string.IsNullOrWhiteSpace(request.Reason) ? null : request.Reason.Trim(), request.GraceSeconds));
        }
        RunObservedState? observed = ReadSteadily(directory);
        bool supervised = Fresh(observed);
        if (supervised)
        {
            // The supervising worker transmits the stop at its next heartbeat and escalates it after the grace period; this waits for
            // what it observes, never longer than the stop wait, whatever grace the stop names.
            TimeSpan grace = asked.GraceSeconds is int seconds ? TimeSpan.FromSeconds(seconds) : WorkerOptions.Grace;
            TimeSpan wait = grace + (WorkerOptions.Heartbeat * 4) + TimeSpan.FromSeconds(10);
            DateTimeOffset deadline = DateTimeOffset.UtcNow + (wait < WorkerOptions.StopWait ? wait : WorkerOptions.StopWait);
            DateTimeOffset until = deadline;
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

            // Once the exit is observed, the settlement follows; it records the stop's facts as observed. Within the same deadline.
            DateTimeOffset settling = DateTimeOffset.UtcNow + (WorkerOptions.Heartbeat * 2) + TimeSpan.FromSeconds(10);
            settling = settling < deadline + (WorkerOptions.Heartbeat * 2) ? settling : deadline + (WorkerOptions.Heartbeat * 2);
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
    /// Who may stop a run, on every profile: an operator; who decides for its goal, its sponsor or, for a goal an agent sponsors, an
    /// operator; and the principal who started it, who recorded its intent. Anyone else is refused before anything is asked of the run.
    /// </summary>
    private static string? StopRefusal(EstateModel model, AgentRuns.Run run, string by)
    {
        if (model.HasRole(by, Roles.Operator) || string.Equals(run.Intent.By, by, StringComparison.Ordinal))
        {
            return null;
        }

        if (run.Goal is string goal && model.ById.TryGetValue(goal, out RecordEnvelope? revision) && GoalRules.MayEnd(model, model.Latest(revision), by))
        {
            return null;
        }

        return $"Principal {by} may not stop run {run.Id}: an operator stops a run, as do whoever decides for its goal and {run.Intent.By}, who started it.";
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

    private static string? WorkspaceRootProblem(string? expected)
    {
        try
        {
            return new WorkerConfigurationStore().ReadWorkspaceRoot() == expected ? null
                : "The workspace root changed during preparation; start a newly prepared run. No harness was dispatched.";
        }
        catch (ProviderRefusedException exception)
        {
            return exception.Message;
        }
    }

    private static string? AssignmentRefusal(EstateModel model, RecordEnvelope assignment, string key)
    {
        RecordEnvelope current = model.Latest(assignment);
        IReadOnlyList<RecordEnvelope> holders = WorkView.OrderOfAssignment(model, current) is RecordEnvelope order
            ? Routing.Assignments(model, model.Latest(order)) : [];
        return holders.Count > 0 && holders[^1].Id != current.Id
            ? $"Work order {key} is held by assignment {holders[^1].Id}; its step goes on from that assignment."
            : null;
    }

    /// <summary>The preflight of a run, before anything is prepared or recorded: every refusal that needs only the records and this machine's keys.</summary>
    private (RunPlan? Plan, string? Refusal, RecordEnvelope? Limited) Plan(EstateModel model, IReadOnlyList<RecordFile> files, RunStartRequest request, IAgentLauncher launcher, RunConfiguration configuration)
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
        if (AssignmentRefusal(model, assignment, key) is string assignmentRefusal)
        {
            return (null, assignmentRefusal, null);
        }

        if (!Strings(assignment.Body.GetProperty("brief"), "permittedChanges").Any(entry => PermittedPaths.Pattern(entry) is not null))
        {
            return (null, "The assignment brief's permittedChanges contain no interpretable repository-relative path pattern. Use a pattern such as path:NOTES.md or path:src/**; prose may accompany at least one pattern.", null);
        }

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

        RunWatchSettings watch = RunWatchSettings.Resolve(model, goal, launcher);
        if (watch.Refusal is string watchRefusal)
        {
            return (null, watchRefusal, null);
        }

        JsonObject selection;
        long? maxBytes;
        try
        {
            selection = RunSelection.Resolve(configuration, launcher.ExecutionCapabilities, RunSelection.Sources(model, goal, assignment), request.Execution, Element(cap));
            selection["role"] = Routing.RoleOf(order);
            selection["allocation"] = RunExecutionObservation.Allocation(request.Allocation, Element(cap), out maxBytes);
            if (request.ContextPackage is not null && !ContentHash.IsValid(request.ContextPackage))
            {
                return (null, "A context-package identity must be its exact content hash.", null);
            }
            selection["adapter"] = "disabled";
        }
        catch (ProviderRefusedException exception)
        {
            return (null, exception.Message, null);
        }

        // This snapshot includes the signed pending assignment. The run's existing grouped-write preflight
        // must admit it with the intent before launch. A stranded older batch is reconciled by that write;
        // ordinary briefs and hand-overs still refuse while the batch remains incomplete.
        string? recipient = CommunicationRecipient(model, goal);
        BriefResponse contextBrief = BriefLocked(model, files, order, checkBatches: false, maxBytes: maxBytes, profile: Communication(recipient, recipient is null ? null : assignment.Id));
        if (contextBrief.Problem is not null)
        {
            return (null, contextBrief.Problem.Message, null);
        }

        JsonElement package = contextBrief.Json.GetProperty("context");
        string packageHash = package.GetProperty("hash").GetString()!;
        if (request.ContextPackage is not null && request.ContextPackage != packageHash)
        {
            return (null, "The supplied context-package identity differs from the package assembled for this run.", null);
        }

        selection["contextPackage"] = packageHash;
        JsonElement accounting = package.GetProperty("payload").GetProperty("accounting");
        selection["allocation"]!["context"] = new JsonObject
        {
            ["units"] = accounting.GetProperty("units").GetString(),
            ["bytes"] = accounting.GetProperty("bytes").GetInt64(),
            ["maxBytes"] = JsonNode.Parse(accounting.GetProperty("maxBytes").GetRawText()),
        };

        return (new RunPlan(estate, assignment, key, goal.Id, request.Agent, PrincipalName(model, request.Agent), cap, clone, resource.Id, repository, branch, contextBrief.Markdown, contextBrief.Json, Condition(order, assignment, goal), selection, StallSeconds(model, goal, request.Settings, launcher), watch), null, null);
    }

    private RunConfiguration? Configuration(IAgentLauncher launcher, string? name) =>
        WorkerOptions.Configurations.GetValueOrDefault(name ?? launcher.Launcher) ?? launcher.Configuration ?? new WorkerConfigurationStore().Resolve(launcher, name);

    private bool SelectionStillCurrent(EstateModel model, RecordEnvelope goal, RunPlan plan, RunConfiguration configuration, IAgentLauncher launcher, RunStartRequest request)
    {
        try
        {
            RunConfiguration? latest = Configuration(launcher, request.Configuration);
            if (latest is null || latest.Launcher != configuration.Launcher || latest.Name != configuration.Name) return false;
            JsonObject current = RunSelection.Resolve(latest, launcher.ExecutionCapabilities, RunSelection.Sources(model, goal, model.Latest(plan.Assignment)), request.Execution, Element(plan.Cap));
            string role = WorkView.OrderOfAssignment(model, model.Latest(plan.Assignment)) is RecordEnvelope order ? Routing.RoleOf(model.Latest(order)) : Routing.DefaultRole;
            return JsonNode.DeepEquals(current["requested"], plan.Selection["requested"])
                && role == plan.Selection["role"]!.GetValue<string>()
                && JsonNode.DeepEquals(current["provenance"], plan.Selection["provenance"])
                && JsonNode.DeepEquals(current["configurationHash"], plan.Selection["configurationHash"]);
        }
        catch (ProviderRefusedException)
        {
            return false;
        }
    }

    private static string? CommunicationRecipient(EstateModel model, RecordEnvelope goal)
    {
        string? sponsor = EstateModel.BodyString(goal, "sponsor");
        return model.CurrentPrincipal(sponsor) is { } person && EstateModel.BodyString(person, "kind") == "human" ? sponsor : null;
    }

    private static int StallSeconds(EstateModel model, RecordEnvelope goal, IReadOnlyDictionary<string, string>? settings, IAgentLauncher launcher)
    {
        if (goal.Body.TryGetProperty("limits", out JsonElement limits) && AgentRuns.Amount(limits, "stallSeconds") is long goalStall)
        {
            return (int)goalStall;
        }

        if (RunWatchSettings.Roster(model, goal) is RecordEnvelope roster && roster.Body.TryGetProperty("limits", out JsonElement rosterLimits)
            && AgentRuns.Amount(rosterLimits, "stallSeconds") is long rosterStall)
        {
            return (int)rosterStall;
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
            RecordEnvelope? existing = model.Records.FirstOrDefault(a => a.Type == "ask" && a.Subject?.Type == "operation" && a.Subject.Id == run
                && EstateModel.BodyString(a, "question")?.Contains("no stdout event for", StringComparison.Ordinal) == true);
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

    private string? RecordSignal(string run, string by, string goalId, RunSignal signal)
    {
        try
        {
            WorkerOptions.BeforeAttentionRecord?.Invoke();
            lock (gate)
            {
                EstateModel model = session.Load();
                string prefix = $"Run {run} [{signal.Code}/{signal.Severity}]";
                RecordEnvelope? existing = model.Records.FirstOrDefault(a => a.Type == "ask" && a.Subject?.Type == "operation" && a.Subject.Id == run
                    && EstateModel.BodyString(a, "question")?.StartsWith(prefix, StringComparison.Ordinal) == true);
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

                JsonObject body = SignalAskBody(model, goal, run, by, signal, settled: false);
                WriteOutcome written = WriteLocked(new RecordDraft("ask", by, Element(body), found.Intent.Reference), model);
                return written.Ok ? written.Written!.Id : null;
            }
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static JsonObject SignalAskBody(EstateModel model, RecordEnvelope goal, string run, string by, RunSignal signal, bool settled) => new()
    {
        ["to"] = GoalRules.AccepterOf(model, goal).Principal ?? by,
        ["goal"] = goal.Id,
        ["state"] = "open",
        ["question"] = $"Run {run} [{signal.Code}/{signal.Severity}] needs attention at {signal.At}: {signal.Summary} Inspect the run?",
        ["recommendation"] = "Inspect the worker's observation, the locally retained output and the assignment before deciding on another attempt.",
        ["consequence"] = settled
            ? "The run has exited. Its observation remains for review; another run needs fresh admission."
            : signal.Severity == "critical"
            ? "The worker asks the existing stop path to contain this run. Its allowance remains reserved until the whole boundary is observed exited."
            : "This warning does not stop the run. The observation remains for review; continued looping or abnormal spending may reach a critical threshold.",
    };

    /// <summary>Observes a run until its exit, then records its settlement, and marks its goal budget-limited when it crossed a cap.</summary>
    private void Supervise(string run, string by, string goalId, RunSupervisor supervisor, IWorkspaceProvider workspaces, PreparedWorkspace workspace, WorkspaceSnapshot baseline, IReadOnlyList<string> permitted, HashSet<string> priorAttempts, CancellationToken abandon)
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

        if (scope.OutsideCount > 0 || scope.Problem is not null)
        {
            var signal = new RunSignal("scope-settlement", "warning", RecordParser.FormatTimestamp(outcome.ExitedAt),
                $"The worker's final scope audit found {scope.OutsideCount.ToString(CultureInfo.InvariantCulture)} paths outside the brief{(scope.Problem is null ? "." : "; the audit is incomplete.")} The run's boundary has already exited; review its changes.");
            outcome = outcome with { Signals = [.. outcome.Signals ?? [], signal] };
        }

        try
        {
            lock (gate)
            {
                EstateModel model = session.Load();
                AgentRuns.Run? found = AgentRuns.Find(model, run);
                if (found is not null && !model.OfType("attempt").Any(a => a.Subject?.Id == found.Assignment && a.By == found.Principal
                    && !priorAttempts.Contains(a.Id) && string.CompareOrdinal(a.At, RecordParser.FormatTimestamp(outcome.LaunchedAt)) >= 0
                    && string.CompareOrdinal(a.At, RecordParser.FormatTimestamp(outcome.ExitedAt)) <= 0))
                {
                    var signal = new RunSignal("attempt-missing", "warning", RecordParser.FormatTimestamp(outcome.ExitedAt),
                        "The run settled without the agent's attempt on its assignment during the run. A clean harness exit does not establish that the agent did the work.");
                    outcome = outcome with { Signals = [.. outcome.Signals ?? [], signal] };
                }
                if (found is { Status: "not-started" })
                {
                    WriteOutcome recovered = RecordDispatch(run, by, outcome.LaunchedAt);
                    if (!recovered.Ok)
                    {
                        supervisor.Settled(null, "The observed dispatch could not be recorded; the allowance remains reserved.");
                        return;
                    }
                }
            }

            lock (gate)
            {
                string? status = null;
                WriteOutcome settled = Revise(run, by, previous => status = Settle(previous, outcome, scope)["status"]?.GetValue<string>());
                if (!settled.Ok)
                {
                    supervisor.Settled(null, settled.Problem?.Message);
                    return;
                }

                string? transferProblem;
                try
                {
                    transferProblem = workspaces.RetainForReview(workspace);
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ProviderRefusedException)
                {
                    transferProblem = "The run settled, but its workspace branch could not be retained in the registered clone. Preserve the workspace for review.";
                }
                supervisor.Settled(status, transferProblem);
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

        if (body["execution"] is JsonObject execution)
        {
            body["executionObserved"] = outcome.Execution is JsonElement facts ? JsonNode.Parse(facts.GetRawText()) : new RunExecutionObservation(Element(execution)).Snapshot();
        }
        body["scope"] = JsonSerializer.SerializeToNode(scope, RunRecordJson);
        body["signals"] = JsonSerializer.SerializeToNode(outcome.Signals ?? [], RunRecordJson);
        body["observed"] = new JsonObject { ["summary"] = summary, ["at"] = RecordParser.FormatTimestamp(outcome.ExitedAt) };
        body["dispatchedAt"] ??= RecordParser.FormatTimestamp(outcome.LaunchedAt);
        body["usage"] = usage;
        if (outcome.Liveness is not null)
        {
            body["liveness"] = JsonSerializer.SerializeToNode(outcome.Liveness, RunRecordJson);
        }

        JsonArray evidence = body["evidence"] as JsonArray ?? [];
        foreach (RunSignal signal in outcome.Signals?.Where(s => s.Code == "attempt-missing") ?? [])
        {
            evidence.Add(new JsonObject { ["summary"] = signal.Summary });
        }
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
            var draft = new RecordDraft("operation", by, Element(body), found.Latest.Subject, found.Latest.Id);
            if (body["status"]?.GetValue<string>() is not ("observed-success" or "known-failure" or "canceled"))
            {
                return WriteLocked(draft, model);
            }

            return SettleAttentionLocked(model, found, draft);
        }
    }

    // Process attention ends with the run; settlement review starts there. Reserve new ask ids before hashing the operation,
    // then pin those asks to that hash. The operation, new asks and old ask withdrawals are judged and journaled together.
    // A person may already have answered a process ask; only still-waiting asks named by this run are withdrawn.
    private WriteOutcome SettleAttentionLocked(EstateModel model, AgentRuns.Run run, RecordDraft draft)
    {
        var attention = new HashSet<string>(StringComparer.Ordinal);
        if (draft.Body.TryGetProperty("liveness", out JsonElement liveness) && liveness.TryGetProperty("attention", out JsonElement stall) && stall.ValueKind == JsonValueKind.String)
        {
            attention.Add(stall.GetString()!);
        }

        if (draft.Body.TryGetProperty("signals", out JsonElement signals))
        {
            foreach (JsonElement signal in signals.EnumerateArray())
            {
                if (signal.TryGetProperty("attention", out JsonElement asked) && asked.ValueKind == JsonValueKind.String) attention.Add(asked.GetString()!);
            }
        }

        List<RecordEnvelope> waiting = attention.Select(id => model.ById.GetValueOrDefault(id)).OfType<RecordEnvelope>()
            .Select(model.Latest).DistinctBy(a => a.Id)
            .Where(a => a.Type == "ask" && a.By == draft.By && a.Subject?.Id == run.Id && AskState.IsWaiting(AskState.State(model, a).State))
            .OrderBy(a => a.Id, StringComparer.Ordinal).ToList();
        List<RecordDraft> reviews = SettlementReviewAsks(model, run, ref draft);
        if (waiting.Count == 0 && reviews.Count == 0)
        {
            return WriteLocked(draft, model);
        }

        (AuthoredRecord? settlement, string? refusal) = AuthorLocked(draft, model);
        if (settlement is null) return Refuse(refusal!);
        var records = new List<AuthoredRecord> { settlement };
        foreach (RecordDraft review in reviews)
        {
            (AuthoredRecord? asked, string? problem) = AuthorLocked(review with { Subject = settlement.Envelope.Reference }, model);
            if (asked is null) return Refuse(problem!);
            records.Add(asked);
        }
        foreach (RecordEnvelope ask in waiting)
        {
            JsonObject body = JsonNode.Parse(ask.Body.GetRawText())!.AsObject();
            body["state"] = "withdrawn";
            body["recommendation"] = $"No longer needed: run {run.Id} settled as {EstateModel.BodyString(settlement.Envelope, "status")} in operation {settlement.Envelope.Id}. Its worker observations remain available for review.";
            (AuthoredRecord? withdrawn, string? problem) = AuthorLocked(new RecordDraft("ask", ask.By, Element(body), settlement.Envelope.Reference, ask.Id), model);
            if (withdrawn is null) return Refuse(problem!);
            records.Add(withdrawn);
        }

        (IReadOnlyList<RecordWritten> written, Problem? rejected) = StoreTogetherLocked(records, "Settle a run, open its settlement review asks and withdraw its process attention asks.", null);
        return rejected is null ? WriteOutcome.Success(written[0]) : WriteOutcome.Fail(rejected);
    }

    private List<RecordDraft> SettlementReviewAsks(EstateModel model, AgentRuns.Run run, ref RecordDraft draft)
    {
        var reviews = new List<RecordDraft>();
        JsonObject operation = JsonNode.Parse(draft.Body.GetRawText())!.AsObject();
        if (operation["signals"] is not JsonArray signals || run.Goal is not string goalId
            || AgentRuns.GoalAt(model, goalId) is not RecordEnvelope goal) return reviews;
        foreach (JsonObject node in signals.OfType<JsonObject>())
        {
            if (node["code"]?.GetValue<string>() is not ("attempt-missing" or "scope-settlement") || node["attention"] is not null) continue;
            try
            {
                WorkerOptions.BeforeAttentionRecord?.Invoke();
                RunSignal signal = node.Deserialize<RunSignal>(RunRecordJson)!;
                string id = Ulid.NewUlid().Value;
                reviews.Add(new RecordDraft("ask", draft.By, Element(SignalAskBody(model, goal, run.Id, draft.By, signal, settled: true)), Id: id));
                node["attention"] = id;
            }
            catch (Exception exception) when (exception is IOException or InvalidOperationException or UnauthorizedAccessException)
            {
                // Keep the observation without an attention id, as with failed attention recording during supervision.
            }
        }
        draft = draft with { Body = Element(operation) };
        return reviews;
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
    private sealed record RunPlan(string Estate, RecordEnvelope Assignment, string Key, string Goal, string Agent, string AgentName, JsonObject Cap, string ClonePath, string Resource, string Repository, string Branch, string Brief, JsonElement Context, string Condition, JsonObject Selection, int StallSeconds, RunWatchSettings? Watch = null, decimal? HistoryRate = null);

    /// <summary>The sponsor's custody exception a run is launched under, in force when its intent is recorded, and the devices that would otherwise refuse it here.</summary>
    private sealed record Excepted(string Id, string Description, string Expires, IReadOnlyList<(string Device, string Reason)> Exposed);
}
