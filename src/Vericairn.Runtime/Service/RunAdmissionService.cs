// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

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
using Vericairn.Runtime.Store;
using Vericairn.Runtime.Workers;
using static Vericairn.Runtime.Service.EstateServiceValues;

namespace Vericairn.Runtime.Service;

/// <summary>Owns run preparation, intent admission and the final checks that fence a harness launch.</summary>
internal sealed class RunAdmissionService(
    EstateRecordAccess recordAccess,
    Func<ProviderRegistry> providers,
    Func<WorkerOptions> workerOptions,
    Func<EstateModel, IReadOnlyList<RecordFile>, RecordEnvelope, long?, string?, string?, BriefResponse> assembleBrief)
{
    private readonly object gate = recordAccess.Gate;
    private readonly EstateSession session = recordAccess.Session;
    private ProviderRegistry Providers => providers();
    private WorkerOptions WorkerOptions => workerOptions();

    /// <summary>
    /// Prepares a run and admits its intent: the whole preflight first, the records last. With a pending assignment, routing's assignment of a step that no
    /// assignment held, signed and not yet stored, the run carries it, and it is stored with the run's intent, so that neither is left
    /// alone: every refusal before the intent records nothing, that assignment included.
    /// </summary>
    internal RunResponse? Prepare(RunStartRequest request, AuthoredRecord? routed, out AdmittedRun? admitted, out RecordWritten? assigned)
    {
        ArgumentNullException.ThrowIfNull(request);
        assigned = null;
        admitted = null;
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
        PreparedWorkspace? workspace = null;
        WorkspaceSnapshot? baseline = null;
        string brief;
        string? configuredRoot;
        RunResponse Refused(RunResponse response)
        {
            if (workspace is null) return response;
            string? retained;
            try { retained = workspaces.DiscardUnused(workspace, baseline, plan.Brief); }
            catch (Exception exception) when (exception is ProviderRefusedException or IOException or UnauthorizedAccessException)
            {
                retained = "Cleanup could not verify that this preparation is unused.";
            }
            if (retained is null) return response;
            string message = response.Message + $" Workspace kept at '{workspace.Path}'. {retained} Preserve its commits and files before removing it or choosing a root that contains it.";
            return response with { Message = message, Problem = response.Problem is null ? null : response.Problem with { Message = message } };
        }

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
            return Refused(RunProblem(pending, "not-started", "refused", $"The workspace could not be prepared: {exception.Message} Nothing was recorded."));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return Refused(RunProblem(pending, "not-started", "refused", $"The workspace could not be prepared: {exception.Message} Nothing was recorded."));
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
            return Refused(RunProblem(pending, "not-started", "refused", $"Execution preflight refused: {exception.Message} Nothing was recorded or dispatched."));
        }

        // Record the intent before anything starts, judged again against the records as they are now, after the preparation: the
        // step still unassigned when routing's assignment comes with the run, the goal at its current revision, the custody guard
        // over the keys held here and the exception in force now, which the intent names, and the cap. The run is its intent.
        RecordWritten intent;
        Excepted? excepted = null;
        lock (gate)
        {
            if (WorkspaceRootProblem(configuredRoot) is string rootProblem)
                return Refused(RunProblem(pending, "not-started", "refused", rootProblem + " Nothing was recorded."));
            EstateModel model = recordAccess.LoadWith(carried);
            RecordEnvelope? goal = AgentRuns.GoalAt(model, plan.Goal);
            if (AssignmentRefusal(model, plan.Assignment, plan.Key) is string assignmentRefusal)
            {
                return Refused(RunProblem(pending, "not-started", "refused", assignmentRefusal + " Nothing was recorded."));
            }

            DateTimeOffset now = DateTimeOffset.UtcNow;
            if (goal is not null)
            {
                IReadOnlyList<(string Device, string Reason)> exposed = CustodyGuard.Exposed(model, CustodyGuard.Guarded(model, goal, plan.Agent), DeviceKeyStores.All(), Environment.UserName);
                RecordEnvelope? exception = exposed.Count == 0 ? null : CustodyExceptions.InForce(model, goal, now);
                if (exposed.Count > 0 && exception is null)
                {
                    return Refused(RunProblem(pending, "not-started", "refused", CustodyRefusal(goal.Id, exposed) + " Nothing was recorded."));
                }

                excepted = exception is null ? null : new Excepted(exception.Id, CustodyExceptions.Describe(model, exception), EstateModel.BodyString(exception, "expires") ?? "?", exposed);
            }

            IReadOnlyList<(string Code, string Message)> refusals = goal is null ? [("operation.goal", $"Goal {plan.Goal} is gone.")] : AgentRuns.StartRefusals(model, goal, plan.Assignment.Id, Element(plan.Cap));
            if (refusals.Count > 0)
            {
                return Refused(RunProblem(pending, "not-started", "refused", string.Join(" ", refusals.Select(r => r.Message)) + " Nothing was recorded."));
            }

            if (!SelectionStillCurrent(model, goal!, plan, configuration, launcher, request))
            {
                return Refused(RunProblem(pending, "not-started", "refused", "Execution defaults or restrictions changed during preparation; start a newly admitted run. Nothing was recorded."));
            }

            if (!HarnessAdmission.StillCurrent(model, launcher.Launcher, preflight, plan.Selection["harnessAdmission"]))
            {
                return Refused(RunProblem(pending, "not-started", "refused", "Harness qualification changed during preparation; nothing was recorded or dispatched."));
            }

            RunWatchSettings watch = RunWatchSettings.Resolve(model, goal!, launcher);
            if (watch.Refusal is string watchRefusal)
            {
                return Refused(RunProblem(pending, "not-started", "refused", watchRefusal + " Nothing was recorded."));
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
                WriteOutcome written = recordAccess.WriteLocked(draft, model);
                if (!written.Ok)
                {
                    return Refused(new RunResponse(pending, "not-started", "Not recorded: " + written.Problem!.Message, false, Problem: written.Problem));
                }

                intent = written.Written!;
            }
            else
            {
                // Routing's assignment and the run's intent are stored together, or neither is.
                (AuthoredRecord? authored, string? unsigned) = recordAccess.AuthorLocked(draft, model);
                if (authored is null)
                {
                    return Refused(RunProblem(pending, "not-started", "refused", $"Not recorded: {unsigned} Nothing was recorded."));
                }

                (IReadOnlyList<RecordWritten> stored, Problem? problem) = recordAccess.StoreTogetherLocked([routed, authored], $"routing's assignment of work order {plan.Key} to {plan.AgentName} and the intent of its run {run}", routed.Envelope.Subject?.Id);
                if (problem is not null)
                {
                    assigned = stored.Count > 0 ? stored[0] : null;
                    string what = assigned is null
                        ? "Nothing was recorded."
                        : $"Routing's assignment {assigned.Id} was stored and the run's intent was not, so nothing was launched: the assignment holds nothing of its step without that intent, routing takes the step again, and this machine reports the batch as a reconcile task (batch.incomplete).";
                    return Refused(new RunResponse(pending, "not-started", $"{problem.Message} {what}", false, Problem: problem));
                }

                assigned = stored[0];
                intent = stored[1];
            }
        }

        admitted = new AdmittedRun(request, plan, launcher, configuration, workspaces, workspace, baseline, configuredRoot,
            directory, launch, environment, limits, environmentPolicy, admittedBinding, run, where, intent, excepted, Refused);
        return null;
    }

    /// <summary>
    /// Rechecks admission immediately before launch. The callback runs under the original estate gate, after the final probes
    /// and custody check; moving the launch outside this gate would leave an admission race.
    /// </summary>
    internal RunSupervisor? Launch(AdmittedRun admitted, Func<IReadOnlyList<string>, RunSupervisor> start,
        out HashSet<string> priorAttempts, out IReadOnlyList<(string Device, string Reason)> lapsed)
    {
        RunStartRequest request = admitted.Request;
        RunPlan plan = admitted.Plan;
        IAgentLauncher launcher = admitted.Launcher;
        RunConfiguration configuration = admitted.Configuration;
        LaunchContext launch = admitted.Launch;
        string? configuredRoot = admitted.ConfiguredRoot;
        RunEnvironmentPolicy? environmentPolicy = admitted.EnvironmentPolicy;
        Dictionary<string, string> environment = admitted.Environment;
        string? admittedBinding = admitted.AdmittedBinding;
        RunSupervisor? supervisor = null;
        priorAttempts = new(StringComparer.Ordinal);
        lapsed = [];
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
                supervisor = start(command);
            }
        }
        return supervisor;
    }

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
        BriefResponse contextBrief = assembleBrief(model, files, order, maxBytes, recipient, recipient is null ? null : assignment.Id);
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
    internal static string CustodyRefusal(string goal, IReadOnlyList<(string Device, string Reason)> exposed) =>
        "The worker launches nothing while a device of an operator or of the goal's sponsor can sign without its owner's presence on this machine (design/goals.md § 10): " + string.Join(" ", exposed.Select(e => e.Reason)) + $" Or the goal's sponsor records a custody exception for the goal, with its reason and its expiry: vericairn goal custody-exception {goal} --by <sponsor> --reason <why> --expires <time>.";

    /// <summary>The units in which the goal's settled runs alone reached its budget: a cap crossed, not one held by runs in flight.</summary>
    internal static List<string> Crossed(AgentRuns.Ledger ledger)
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
    internal void MarkBudgetLimited(EstateModel model, RecordEnvelope goal, string by, string why)
    {
        RecordEnvelope current = model.Latest(goal);
        if (GoalRules.Status(model, current) != "active")
        {
            return;
        }

        string note = $"Its agent runs reached its budget: {why} New runs are fenced until its sponsor raises the budget by a revision and activates it, or stops it.";
        var body = new JsonObject { ["status"] = "budget-limited", ["note"] = note.Length > 4000 ? note[..3999] + "…" : note };
        recordAccess.WriteLocked(new RecordDraft("goal-status", by, Element(body), current.Reference), model);
    }

    private static string Slug(string text) => Regex.Replace(text, "[^A-Za-z0-9._-]+", "-").Trim('-');

    private static RunResponse RunProblem(string run, string status, string code, string message) =>
        new(run, status, message, false, Problem: new Problem(code, message, []));

    /// <summary>What the preflight found: everything a run needs that the records give.</summary>
    internal sealed record RunPlan(string Estate, RecordEnvelope Assignment, string Key, string Goal, string Agent, string AgentName, JsonObject Cap, string ClonePath, string Resource, string Repository, string Branch, string Brief, JsonElement Context, string Condition, JsonObject Selection, int StallSeconds, RunWatchSettings? Watch = null, decimal? HistoryRate = null);

    /// <summary>The sponsor's custody exception a run is launched under, in force when its intent is recorded, and the devices that would otherwise refuse it here.</summary>
    internal sealed record Excepted(string Id, string Description, string Expires, IReadOnlyList<(string Device, string Reason)> Exposed);

    /// <summary>The complete handoff from a recorded intent to dispatch; all preparation state belongs to this admission.</summary>
    internal sealed record AdmittedRun(RunStartRequest Request, RunPlan Plan, IAgentLauncher Launcher, RunConfiguration Configuration,
        IWorkspaceProvider Workspaces, PreparedWorkspace Workspace, WorkspaceSnapshot Baseline, string? ConfiguredRoot,
        RunDirectory Directory, LaunchContext Launch, Dictionary<string, string> Environment, RunLimits Limits,
        RunEnvironmentPolicy? EnvironmentPolicy, string? AdmittedBinding, string Run, string Where, RecordWritten Intent,
        Excepted? Excepted, Func<RunResponse, RunResponse> Refused);
}
