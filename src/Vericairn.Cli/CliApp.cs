// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.CommandLine;
using Vericairn.Sdk;

namespace Vericairn.Cli;

/// <summary>The command tree of <c>vericairn</c>; built once per invocation so tests can drive it in-process.</summary>
internal static partial class CliApp
{
    /// <summary>Builds the root command with every family: records, estate, work, goal, worker, devices, connections, release, sources, check, runtime, recover, objects, docs, mcp, status, progress, ask, decisions, doctor, version, updates.</summary>
    public static RootCommand Build(ReleaseUpdates? updates = null)
    {
        updates ??= new ReleaseUpdates();
        Composition.Register(Vericairn.Runtime.Providers.ProviderRegistry.Current);
        var context = new CommandContext();
        Option<string> estateOption = context.EstateOption;
        Option<bool> jsonOption = context.JsonOption;

        var root = new RootCommand("Vericairn: records, estates and the tools that keep them honest.");
        root.Options.Add(estateOption);
        root.Options.Add(jsonOption);
        PresentationOptions presentation = context.Presentation;
        root.Options.Add(presentation.Recipient);
        root.Options.Add(presentation.Assignment);
        root.Options.Add(presentation.Depth);
        root.Subcommands.Add(new CommunicationCommand(context));
        root.Subcommands.Add(new RecordsCommand(context));
        root.Subcommands.Add(new EstateCommand(context));
        root.Subcommands.Add(new WorkCommand(context));
        root.Subcommands.Add(new GoalCommand(context));
        root.Subcommands.Add(WorkerFamily(estateOption, jsonOption));
        root.Subcommands.Add(new DevicesCommand(context));
        root.Subcommands.Add(new ConnectionsCommand(context));
        root.Subcommands.Add(new ReleaseCommand(context));
        root.Subcommands.Add(new SourcesCommand(context));
        root.Subcommands.Add(new CheckCommand(context));
        root.Subcommands.Add(RuntimeFamily(estateOption, jsonOption));
        root.Subcommands.Add(new RecoverCommand(context));
        root.Subcommands.Add(new ObjectsCommand(context));
        root.Subcommands.Add(new DocsCommand(context));
        root.Subcommands.Add(new McpCommand(context));
        root.Subcommands.Add(new ContextCommand(context));
        root.Subcommands.Add(new UpdatesCommand(context));

        var status = new Command("status", "Show the estate's current state derived from its records, with compact required attention resolved from current evidence. Each item names its exact source and inspection command. --json is identical on every machine for the same records and includes complete record-derived attention references. Human status also shows labelled local worker concerns. " + UpdateNotices.Help);
        var observeStatus = new Option<bool>("--observe") { Description = "Explicitly record that a person observed status; requires --by. Ordinary reads record nothing." };
        var observer = new Option<string?>("--by") { Description = "The observing human principal; used only with --observe." };
        status.Options.Add(observeStatus);
        status.Options.Add(observer);
        status.SetAction(parseResult => UpdateNotices.Run(context, updates, () => Commands.Status(parseResult.GetValue(estateOption)!, parseResult.GetValue(jsonOption), parseResult.GetValue(observeStatus), parseResult.GetValue(observer), presentation.Read(parseResult))));
        root.Subcommands.Add(status);

        var progress = new Command("progress", "Show the estate's recorded progress with latest local agent claims and worker observations labelled separately: where the estate stands, the work orders by state, each published release and what it contained, what comes next by release, the next eligible work and what waits on the operator. Required attention names only open obligations with exact references and inspection commands; ordinary after ordering is planning. --json includes complete attention references.");
        progress.SetAction(parseResult => Commands.Progress(parseResult.GetValue(estateOption)!, parseResult.GetValue(jsonOption), presentation.Read(parseResult)));
        root.Subcommands.Add(progress);

        var resume = new Command("resume", "Show every record, pin and observed-state change since a retained hand-over. The world may have moved; this restarts nothing.");
        var resumePackage = new Argument<string>("package") { Description = "The retained hand-over JSON file, or - for standard input." };
        resume.Arguments.Add(resumePackage);
        resume.SetAction(parseResult => Commands.Resume(parseResult.GetValue(estateOption)!, parseResult.GetValue(resumePackage)!, parseResult.GetValue(jsonOption)));
        root.Subcommands.Add(resume);

        var handoff = new Command("handoff", "Prepare a complete record-derived hand-over; refuse missing sources or overflow. Remote is not built.");
        var handoffOut = new Option<string?>("--out") { Description = "Retain the portable package in a new file; never overwrite." };
        var handoffLimit = new Option<long?>("--max-bytes") { Description = "Optional payload byte limit; no truncation. No limit when omitted." };
        handoff.Options.Add(handoffOut);
        handoff.Options.Add(handoffLimit);
        handoff.SetAction(parseResult => Commands.Handoff(parseResult.GetValue(estateOption)!, parseResult.GetValue(handoffOut), parseResult.GetValue(handoffLimit), parseResult.GetValue(jsonOption), presentation.Read(parseResult)));
        root.Subcommands.Add(handoff);

        var ask = new Command("ask", "Show what waits on a principal, or on every principal: the asks still open or notified, at most three in one delivery with the recommendation first and the consequence of leaving each unanswered, the rest waiting in order. Reading them answers nothing; an ask is answered by a decision of the principal it is put to that names it in its answers.");
        var toOption = new Option<string?>("--to") { Description = "The principal asked (ULID); every principal when omitted." };
        ask.Options.Add(toOption);
        ask.SetAction(parseResult => Commands.Ask(parseResult.GetValue(estateOption)!, parseResult.GetValue(toOption), parseResult.GetValue(jsonOption)));
        root.Subcommands.Add(ask);

        var decisions = new Command("decisions", "List the estate's current decisions, or show one cleanly.");
        var decisionIdArgument = new Argument<string?>("id") { Description = "Decision record id (any revision); omit to list.", Arity = ArgumentArity.ZeroOrOne };
        decisions.Arguments.Add(decisionIdArgument);
        decisions.SetAction(parseResult => Commands.Decisions(parseResult.GetValue(estateOption)!, parseResult.GetValue(decisionIdArgument), parseResult.GetValue(jsonOption), presentation.Read(parseResult)));
        root.Subcommands.Add(decisions);

        var doctor = new Command("doctor", "Check this machine's prerequisites. " + UpdateNotices.Help);
        doctor.SetAction(parseResult => UpdateNotices.Run(context, updates, () => Commands.Doctor(parseResult.GetValue(estateOption)!)));
        root.Subcommands.Add(doctor);

        var version = new Command("version", "Print the version. " + UpdateNotices.Help);
        version.SetAction(_ => UpdateNotices.Run(context, updates, Commands.Version));
        root.Subcommands.Add(version);
        return root;
    }




    private static Command RuntimeFamily(Option<string> estateOption, Option<bool> jsonOption)
    {
        var runtime = new Command("runtime", "The daemon that owns an estate's live state on this machine: serve, start, stop, status, install, uninstall, configure (the local-network profile) and rotate (its certificate).");
        var noLoopbackOption = new Option<bool>("--no-loopback") { Description = "Disable the pinned loopback TLS endpoint. The local socket and any configured local-network listener remain available." };
        var serve = new Command("serve", "Serve the estate in this process until stopped (Ctrl+C or 'runtime stop'); refuses when a daemon already serves it.");
        var detachedOption = new Option<bool>("--detached") { Description = "Write the daemon's output to its log beside the estate's live store instead of the standard streams; 'runtime start' serves this way." };
        serve.Options.Add(noLoopbackOption);
        serve.Options.Add(detachedOption);
        serve.SetAction(parseResult => Commands.RuntimeServe(parseResult.GetValue(estateOption)!, !parseResult.GetValue(noLoopbackOption), parseResult.GetValue(detachedOption)));
        runtime.Subcommands.Add(serve);
        var start = new Command("start", "Start a daemon for the estate in the background and wait until it answers.");
        start.SetAction(parseResult => Commands.RuntimeStart(parseResult.GetValue(estateOption)!, parseResult.GetValue(jsonOption)));
        runtime.Subcommands.Add(start);
        var stop = new Command("stop", "Ask the daemon serving the estate to stop; closing a client never stops it.");
        stop.SetAction(parseResult => Commands.RuntimeStop(parseResult.GetValue(estateOption)!, parseResult.GetValue(jsonOption)));
        runtime.Subcommands.Add(stop);
        var status = new Command("status", "Whether a daemon serves the estate and where, or whether commands run the embedded runtime.");
        status.SetAction(parseResult => Commands.RuntimeStatus(parseResult.GetValue(estateOption)!, parseResult.GetValue(jsonOption)));
        runtime.Subcommands.Add(status);
        var planOption = new Option<bool>("--plan") { Description = "Show the unit and the commands without installing anything." };
        var install = new Command("install", "Install the daemon as a per-user service that starts at login (launchd agent, systemd user unit or a logon task).");
        install.Options.Add(planOption);
        install.SetAction(parseResult => Commands.RuntimeInstall(parseResult.GetValue(estateOption)!, parseResult.GetValue(planOption), parseResult.GetValue(jsonOption)));
        runtime.Subcommands.Add(install);
        var uninstall = new Command("uninstall", "Remove the per-user service installed by 'runtime install'.");
        uninstall.SetAction(parseResult => Commands.RuntimeUninstall(parseResult.GetValue(estateOption)!, parseResult.GetValue(jsonOption)));
        runtime.Subcommands.Add(uninstall);
        var localNetworkOption = new Option<string?>("--local-network") { Description = "The address and port the local-network profile listens on from the runtime's next start, an IP address of this machine such as 192.168.1.20:7443; 'off' turns it off. Off unless set." };
        var configure = new Command("configure", "Configure the runtime of this estate on this machine; without options, show the configuration. Nothing listens beyond the local socket and loopback unless an address is set.");
        var allowPublicOption = new Option<bool>("--allow-public") { Description = "With --local-network, allow a wildcard address (every interface) or one other networks can reach; such an address is refused without it." };
        configure.Options.Add(localNetworkOption);
        configure.Options.Add(allowPublicOption);
        configure.SetAction(parseResult => Commands.RuntimeConfigure(parseResult.GetValue(estateOption)!, parseResult.GetValue(localNetworkOption), parseResult.GetValue(allowPublicOption), parseResult.GetValue(jsonOption)));
        runtime.Subcommands.Add(configure);
        var compromisedOption = new Option<bool>("--compromised") { Description = "The certificate's key may be known to someone else: also close every open connection, so nothing continues under it." };
        var identityOption = new Option<bool>("--identity") { Description = "The runtime identity's key may be known to someone else: replace the identity too; every paired device then pairs again." };
        var reasonOption = new Option<string?>("--reason") { Description = "Why, for the runtime's log." };
        var rotate = new Command("rotate", "Replace the runtime certificate now (it is also replaced on a schedule before it expires). Paired devices keep working, since they pin the runtime identity, which signs the certificate in use; loopback clients read the new one from the endpoint file.");
        rotate.Options.Add(compromisedOption);
        rotate.Options.Add(identityOption);
        rotate.Options.Add(reasonOption);
        rotate.SetAction(parseResult => Commands.RuntimeRotate(parseResult.GetValue(estateOption)!, parseResult.GetValue(compromisedOption), parseResult.GetValue(identityOption), parseResult.GetValue(reasonOption), parseResult.GetValue(jsonOption)));
        runtime.Subcommands.Add(rotate);
        return runtime;
    }









    private static Command WorkerFamily(Option<string> estateOption, Option<bool> jsonOption)
    {
        var worker = new Command("worker", "The worker (vericairn.worker/1): a coding agent carries one assignment toward its exit question through a launcher, in a workspace on a work branch, with its cap from its goal's budget recorded before it starts and its observed usage after. It accepts nothing; the agent's result is a claim another principal reviews.");

        var launchers = new Command("launchers", "List the coding-agent launchers available to the runtime, providers of vericairn.launcher.agent/1, with their settings and limitations.");
        launchers.SetAction(parseResult => Commands.WorkerLaunchers(parseResult.GetValue(estateOption)!, parseResult.GetValue(jsonOption)));
        worker.Subcommands.Add(launchers);
        worker.Subcommands.Add(WorkerConfigure(jsonOption));

        var qualify = new Command("qualify", "Record an authorized human's qualification of one exact installed harness version from local probe digests: version, help-confirmed flags, closed tools, isolated configuration, login boundary and effective MCP. It launches no agent, establishes no live inference or work acceptance, and grants no version range. Admission allows the shipped baseline or a current qualification whose repeated local probes have identical digests; a superseding record revokes the qualification. The existing inventory policy still applies. Remote is not built.");
        var qualifyLauncher = new Option<string>("--launcher") { Description = "The native launcher: codex or claude-code.", Required = true };
        var qualifyBy = new Option<string>("--by") { Description = "A human operator or human explicitly granted the qualify scope; never an agent.", Required = true };
        var qualifySettings = new Option<string[]>("--setting") { Description = "Local launcher setting as name=value, including command=<executable or JSON array>; repeatable. Values are never recorded.", AllowMultipleArgumentsPerToken = false };
        qualify.Options.Add(qualifyLauncher);
        qualify.Options.Add(qualifyBy);
        qualify.Options.Add(qualifySettings);
        qualify.SetAction(parseResult => Commands.WorkerQualify(parseResult.GetValue(estateOption)!, parseResult.GetValue(qualifyLauncher)!, parseResult.GetValue(qualifyBy)!, parseResult.GetValue(qualifySettings) ?? [], parseResult.GetValue(jsonOption)));
        worker.Subcommands.Add(qualify);

        var assignmentArgument = new Argument<string>("assignment") { Description = "The assignment the agent carries, whose contributor it is." };
        var launcherOption = new Option<string>("--launcher") { Description = "The launcher: claude-code, codex or generic (generic process launch is not complete agent support).", Required = true };
        var agentOption = new Option<string>("--agent") { Description = "The agent principal the harness acts for: the assignment's contributor, with an admitted device whose key this machine holds.", Required = true };
        var byOption = new Option<string>("--by") { Description = "The principal recording the run: the operator, the goal's sponsor when the sponsor is a person, or a principal with the local-engineering scope.", Required = true };
        var repositoryOption = new Option<string>("--repository") { Description = "The repository resource whose clone, registered on this machine (vericairn sources clone), the workspace is made from.", Required = true };
        var goalOption = new Option<string?>("--goal") { Description = "The goal the run is charged to, when the work belongs to more than one active goal." };
        var branchOption = new Option<string?>("--branch") { Description = "The work branch; work/ and the work order's key when omitted. A later run of the branch continues in its workspace." };
        var baseOption = new Option<string?>("--base") { Description = "What a new work branch starts from; the clone's HEAD when omitted." };
        var wallMinutesOption = new Option<long?>("--wall-minutes") { Description = "The run's cap in wall minutes; what remains of the goal's budget when omitted, and never more." };
        var settingOption = new Option<string[]>("--setting") { Description = "A launcher setting as name=value, for example command=<executable or JSON array>; repeatable.", AllowMultipleArgumentsPerToken = false };
        var detachOption = new Option<bool>("--detach") { Description = "Return once the harness is launched, leaving the run to the daemon that serves the estate; without it the command waits until the run settles, and Ctrl+C asks it to stop." };
        var run = new Command("run", "Run a coding agent on an assignment: refused while the goal is not active or its budget is spent, while a device of an operator or the goal's sponsor signs here without presence and the sponsor recorded no custody exception in force for the goal, or when the launcher cannot run; otherwise the workspace is prepared, the intent recorded with the cap, the harness launched with the brief and the MCP server, observed, and its usage recorded once it exits.");
        run.Arguments.Add(assignmentArgument);
        run.Options.Add(launcherOption);
        run.Options.Add(agentOption);
        run.Options.Add(byOption);
        run.Options.Add(repositoryOption);
        run.Options.Add(goalOption);
        run.Options.Add(branchOption);
        run.Options.Add(baseOption);
        run.Options.Add(wallMinutesOption);
        run.Options.Add(settingOption);
        run.Options.Add(detachOption);
        var executionOptions = new ExecutionOptions(run);
        run.SetAction(parseResult => Commands.WorkerRun(
            parseResult.GetValue(estateOption)!,
            new Commands.WorkerRunOptions(
                parseResult.GetValue(assignmentArgument)!,
                parseResult.GetValue(launcherOption)!,
                parseResult.GetValue(agentOption)!,
                parseResult.GetValue(byOption)!,
                parseResult.GetValue(repositoryOption)!,
                parseResult.GetValue(goalOption),
                parseResult.GetValue(branchOption),
                parseResult.GetValue(baseOption),
                parseResult.GetValue(wallMinutesOption),
                parseResult.GetValue(settingOption) ?? [],
                parseResult.GetValue(detachOption), executionOptions.Read(parseResult)),
            parseResult.GetValue(jsonOption)));
        worker.Subcommands.Add(run);

        var runArgument = new Argument<string>("run") { Description = "The run: its id, the id of any of its operation's revisions, or its identity." };
        var stopByOption = new Option<string>("--by") { Description = "The principal asking for the stop.", Required = true };
        var reasonOption = new Option<string?>("--reason") { Description = "Why." };
        var graceOption = new Option<int?>("--grace") { Description = "Seconds the harness has to stop once the stop is transmitted before its process tree is killed; the worker's default, ten, when omitted." };
        var stop = new Command("stop", "Ask a run to stop and report what was observed: the request, its transmission to the harness, the harness's acknowledgment, an escalation after the grace period and the containment, each only once observed. A stop that is not observed is reported as requested.");
        stop.Arguments.Add(runArgument);
        stop.Options.Add(stopByOption);
        stop.Options.Add(reasonOption);
        stop.Options.Add(graceOption);
        stop.SetAction(parseResult => Commands.WorkerStop(parseResult.GetValue(estateOption)!, parseResult.GetValue(runArgument)!, parseResult.GetValue(stopByOption)!, parseResult.GetValue(reasonOption), parseResult.GetValue(graceOption), parseResult.GetValue(jsonOption)));
        worker.Subcommands.Add(stop);

        var runs = new Command("runs", "List the agent runs with their caps, their observed usage, what each is charged (its cap for what was not reported, and while in flight) and their stops, and the ledgers of their goals.");
        runs.SetAction(parseResult => Commands.WorkerRuns(parseResult.GetValue(estateOption)!, parseResult.GetValue(jsonOption)));
        worker.Subcommands.Add(runs);
        return worker;
    }

}
