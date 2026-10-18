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
        root.Subcommands.Add(new RuntimeCommand(context));
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














}
