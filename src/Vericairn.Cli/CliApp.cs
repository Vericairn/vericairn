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
        root.Subcommands.Add(new WorkerCommand(context));
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










}
