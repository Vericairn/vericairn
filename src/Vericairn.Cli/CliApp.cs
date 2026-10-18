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
        var presentation = new PresentationOptions();
        root.Options.Add(presentation.Recipient);
        root.Options.Add(presentation.Assignment);
        root.Options.Add(presentation.Depth);
        root.Subcommands.Add(CommunicationFamily(estateOption, jsonOption, presentation));
        root.Subcommands.Add(new RecordsCommand(context));
        root.Subcommands.Add(new EstateCommand(context));
        root.Subcommands.Add(WorkFamily(estateOption, jsonOption, presentation));
        root.Subcommands.Add(GoalFamily(estateOption, jsonOption));
        root.Subcommands.Add(WorkerFamily(estateOption, jsonOption));
        root.Subcommands.Add(new DevicesCommand(context));
        root.Subcommands.Add(new ConnectionsCommand(context));
        root.Subcommands.Add(new ReleaseCommand(context));
        root.Subcommands.Add(new SourcesCommand(context));
        root.Subcommands.Add(new CheckCommand(context));
        root.Subcommands.Add(RuntimeFamily(estateOption, jsonOption));
        root.Subcommands.Add(new RecoverCommand(context));
        root.Subcommands.Add(new ObjectsCommand(context));
        root.Subcommands.Add(DocsFamily(estateOption, jsonOption));
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


    private static Command DocsFamily(Option<string> estateOption, Option<bool> jsonOption)
    {
        var docs = new Command("docs", "Documentation: a release's set (its notes from the estate's records, and the check that a set holds every document its manifest lists; the references are generated by the product's own build tooling), and the living documents that stay true as the estate changes.");

        var notes = new Command("notes", "Write release notes from the estate's records since the previous release's notes ended, and record their window in the set's manifest.");
        var fromOption = new Option<string>("--from") { Description = "The earlier published release's version; the notes begin where the notes of its documentation set, the directory it names beside --out, ended (at its publication when there is no such set).", Required = true };
        var toOption = new Option<string>("--to") { Description = "The release's version; its publication ends the notes once it is recorded, the second before they are generated until then.", Required = true };
        var notesOutOption = new Option<string>("--out") { Description = "Directory of the documentation set the notes belong to.", Required = true };
        var sinceOption = new Option<string?>("--since") { Description = "The time the notes begin, for example 2026-10-16T04:29:43Z, no later than the previous set's end; widens the window to include records the previous set missed and records the chosen start in the manifest." };
        notes.Options.Add(fromOption);
        notes.Options.Add(toOption);
        notes.Options.Add(notesOutOption);
        notes.Options.Add(sinceOption);
        notes.SetAction(parseResult => Docs.Notes(parseResult.GetValue(estateOption)!, parseResult.GetValue(fromOption)!, parseResult.GetValue(toOption)!, parseResult.GetValue(notesOutOption)!, parseResult.GetValue(sinceOption), parseResult.GetValue(jsonOption)));
        docs.Subcommands.Add(notes);

        var check = new Command("check", "Refuse a documentation set that lacks a document, or a heading for a command, an API operation or a record type its manifest lists; every gap is listed at once.");
        var directoryArgument = new Argument<string>("directory") { Description = "The documentation set to check." };
        var documentsOption = new Option<string?>("--documents") { Description = "The product's declaration of the documents every set holds (a JSON array of file names); the set's manifest is checked against it." };
        check.Arguments.Add(directoryArgument);
        check.Options.Add(documentsOption);
        check.SetAction(parseResult => Docs.Check(parseResult.GetValue(directoryArgument)!, parseResult.GetValue(documentsOption), parseResult.GetValue(jsonOption)));
        docs.Subcommands.Add(check);

        var living = new Command("living", "List the declared living documents: each one current, due with every trigger newer than its latest clearance, or unavailable.");
        living.SetAction(parseResult => Commands.DocsLiving(parseResult.GetValue(estateOption)!, parseResult.GetValue(jsonOption)));
        docs.Subcommands.Add(living);

        var pathArgument = new Argument<string>("path") { Description = "The document's repository-relative path." };
        var byOption = new Option<string>("--by") { Description = "The reviewing principal (ULID).", Required = true };
        var outcomeOption = new Option<string>("--outcome") { Description = "current (it holds), revised (it was changed for the trigger) or needs-change (it is stale and stays due).", Required = true };
        var summaryOption = new Option<string>("--summary") { Description = "What was checked.", Required = true };
        var findingOption = new Option<string[]>("--finding") { Description = "A finding, severity:summary with severity error, warning or info; repeatable.", AllowMultipleArgumentsPerToken = false };
        var commitOption = new Option<string?>("--commit") { Description = "For a document of another repository, the commit reviewed." };
        var independenceOption = new Option<string?>("--independence") { Description = "For a document of another repository, whose author the estate's records do not show, the reviewer's independence of its content: self (the reviewer wrote or revised it), independent-agent or operator. Default: self for a principal that holds the builder role, else judged from the estate's records. An estate file's revisions decide its reviewer's independence, and a statement that differs is refused." };
        var review = new Command("review", "Record a review of a living document: the runtime reads its content, records the covered paths' identity and the triggers it answers, and runs its declared checks; a failed check refuses the clearance.");
        review.Arguments.Add(pathArgument);
        review.Options.Add(byOption);
        review.Options.Add(outcomeOption);
        review.Options.Add(summaryOption);
        review.Options.Add(findingOption);
        review.Options.Add(commitOption);
        review.Options.Add(independenceOption);
        review.SetAction(parseResult => Commands.DocsReview(parseResult.GetValue(estateOption)!, parseResult.GetValue(pathArgument)!, parseResult.GetValue(byOption)!, parseResult.GetValue(outcomeOption)!, parseResult.GetValue(summaryOption)!, parseResult.GetValue(findingOption) ?? [], parseResult.GetValue(commitOption), parseResult.GetValue(independenceOption), parseResult.GetValue(jsonOption)));
        docs.Subcommands.Add(review);

        var bodyOption = new Option<string>("--body") { Description = "Path to a JSON file holding the living-document declaration, or '-' for standard input.", Required = true };
        var supersedesOption = new Option<string?>("--supersedes") { Description = "The declaration this one revises." };
        var declareByOption = new Option<string>("--by") { Description = "Authoring principal id (ULID).", Required = true };
        var declare = new Command("declare", "Record or revise the declaration of a living document: its triggers and levels, generated regions, checks and who may clear it.");
        declare.Options.Add(declareByOption);
        declare.Options.Add(bodyOption);
        declare.Options.Add(supersedesOption);
        declare.SetAction(parseResult => Commands.DocsDeclare(parseResult.GetValue(estateOption)!, parseResult.GetValue(declareByOption)!, parseResult.GetValue(bodyOption)!, parseResult.GetValue(supersedesOption), parseResult.GetValue(jsonOption)));
        docs.Subcommands.Add(declare);

        var regenerate = new Command("regenerate", "Rewrite an estate document's generated regions, or a generated document, from their generators; nothing outside the markers changes.");
        regenerate.Arguments.Add(pathArgument);
        regenerate.SetAction(parseResult => Commands.DocsRegenerate(parseResult.GetValue(estateOption)!, parseResult.GetValue(pathArgument)!, parseResult.GetValue(jsonOption)));
        docs.Subcommands.Add(regenerate);
        return docs;
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








    private static Command GoalFamily(Option<string> estateOption, Option<bool> jsonOption)
    {
        var goal = new Command("goal", "Goals: objectives the estate carries out, done when their record predicates hold, an evaluator who did none of the work finds every condition met, and they are accepted. Without a subcommand, the current goal condition by condition: the one goal neither achieved nor ended, or with none or several, the list.");
        goal.SetAction(parseResult => Commands.Goal(parseResult.GetValue(estateOption)!, null, parseResult.GetValue(jsonOption)));
        var shownArgument = new Argument<string?>("goal") { Description = "Goal record id (any revision); omit for the current goal.", Arity = ArgumentArity.ZeroOrOne };
        var show = new Command("show", "Show a goal condition by condition: its status, the budget its activation grants, each condition's verdict with the records it read beside its current evaluation's, its steps, and what stands between it and its acceptance.");
        show.Arguments.Add(shownArgument);
        show.SetAction(parseResult => Commands.Goal(parseResult.GetValue(estateOption)!, parseResult.GetValue(shownArgument), parseResult.GetValue(jsonOption)));
        goal.Subcommands.Add(show);
        var next = new Command("next", "Show a goal's next steps routed from this machine: each eligible step with the principal its role's roster reaches (an agent where a launcher for its harness, its admitted key and the goal's budget allow, a person where needed, a reserved action only to one who holds it) or why none is available, whether the goal's budget allows it, and what holds the others. It records and starts nothing; vericairn goal step takes the next one.");
        next.Arguments.Add(shownArgument);
        next.SetAction(parseResult => Commands.GoalNext(parseResult.GetValue(estateOption)!, parseResult.GetValue(shownArgument), parseResult.GetValue(jsonOption)));
        goal.Subcommands.Add(next);

        var byOption = new Option<string>("--by") { Description = "Acting principal id (ULID).", Required = true };
        var goalArgument = new Argument<string>("goal") { Description = "Goal record id (any revision)." };

        var titleOption = new Option<string>("--title") { Description = "Short title.", Required = true };
        var objectiveOption = new Option<string>("--objective") { Description = "What the sponsor wants, in the sponsor's words.", Required = true };
        var doneOption = new Option<string[]>("--done") { Description = "A completion condition as JSON; repeatable. A record predicate, {\"type\": ...} with optional subject, product, fields, state, min and max (or the same inside {\"record\": ...}), or an evidence statement, {\"evidence\": ..., \"check\": ...}.", Required = true };
        var sponsorOption = new Option<string?>("--sponsor") { Description = "The accountable principal; the proposer when omitted. A person who sponsors a goal activates and accepts it; a goal an agent sponsors is activated and accepted by an operator, and only an operator." };
        var delegateOption = new Option<string?>("--delegate") { Description = "A principal the sponsor, a person, delegates the acceptance of an ordinary goal to; it accepts while it holds the 'accept-work' scope, and never a goal that includes a reserved action." };
        var constraintOption = new Option<string[]>("--constraint") { Description = "What must not change or happen; repeatable." };
        var budgetOption = new Option<string[]>("--budget") { Description = "A cap its activation grants, unit=value; repeatable: wallMinutes and attemptsPerStep (both required), tokens, concurrency, or currency=<amount in minor units>:<unit> such as currency=5000:USD. A unit not given is not granted." };
        var rosterOption = new Option<string?>("--roster") { Description = "The roster record whose roles it uses; the estate's current roster when omitted." };
        var kindOption = new Option<string>("--kind") { Description = "finite (ends when done) or stewardship (holds a standing condition).", DefaultValueFactory = _ => "finite" };
        var workOrderOption = new Option<string[]>("--work-order") { Description = "A work order the sponsor gives as its plan; repeatable. Without one a planner proposes its work, as work orders that name the goal among their inputs." };
        var inputOption = new Option<string[]>("--input") { Description = "A record it builds on, as type:id; repeatable." };
        var deadlineOption = new Option<string?>("--deadline") { Description = "The goal's deadline, for example 2026-12-01T00:00:00Z; validated and recorded but not yet enforced." };
        var supersedesOption = new Option<string?>("--supersedes") { Description = "The goal this proposal revises, by its current revision's id; a revision needs a fresh activation, and only its sponsor, a person, or an operator for a goal an agent sponsors records it." };
        var propose = new Command("propose", "Propose a goal, or a revision of one: what done looks like, its constraints, its budget and its sponsor. A proposal authorizes and funds nothing. Anyone may propose a goal; a revision is its sponsor's when the sponsor is a person, and an operator's when an agent sponsors it, and anyone else proposes the change as an ask.");
        propose.Options.Add(byOption);
        propose.Options.Add(titleOption);
        propose.Options.Add(objectiveOption);
        propose.Options.Add(doneOption);
        propose.Options.Add(sponsorOption);
        propose.Options.Add(delegateOption);
        propose.Options.Add(constraintOption);
        propose.Options.Add(budgetOption);
        propose.Options.Add(rosterOption);
        propose.Options.Add(kindOption);
        propose.Options.Add(workOrderOption);
        propose.Options.Add(inputOption);
        propose.Options.Add(deadlineOption);
        propose.Options.Add(supersedesOption);
        propose.SetAction(parseResult => Commands.GoalPropose(
            parseResult.GetValue(estateOption)!,
            new Commands.GoalProposal(
                parseResult.GetValue(byOption)!,
                parseResult.GetValue(titleOption)!,
                parseResult.GetValue(objectiveOption)!,
                parseResult.GetValue(doneOption) ?? [],
                parseResult.GetValue(sponsorOption),
                parseResult.GetValue(delegateOption),
                parseResult.GetValue(constraintOption) ?? [],
                parseResult.GetValue(budgetOption) ?? [],
                parseResult.GetValue(rosterOption),
                parseResult.GetValue(kindOption)!,
                parseResult.GetValue(workOrderOption) ?? [],
                parseResult.GetValue(inputOption) ?? [],
                parseResult.GetValue(deadlineOption),
                parseResult.GetValue(supersedesOption)),
            parseResult.GetValue(jsonOption)));
        goal.Subcommands.Add(propose);

        var noteOption = new Option<string?>("--note") { Description = "Why, and what remains; a sentence naming the action when omitted." };
        foreach ((string name, string description) in new[]
        {
            ("activate", "Activate a proposed goal, which grants its budget: by its sponsor, a person who holds the operator role or a grant of the spending scope, or by an operator when an agent sponsors it. An agent never activates a goal."),
            ("pause", "Pause an active goal; its sponsor's, or an operator's for a goal an agent sponsors. Prevents new runs but does not stop runs already in flight; use 'vericairn worker stop' for each run that must stop."),
            ("resume", "Resume a paused, budget-limited or blocked goal, which grants its budget again; by whoever may activate it."),
            ("stop", "Stop a goal: its sponsor, or an operator for a goal an agent sponsors, ends it as abandoned and prevents new runs. Runs already in flight are not stopped; use 'vericairn worker stop' for each run that must stop."),
        })
        {
            var command = new Command(name, description);
            command.Arguments.Add(goalArgument);
            command.Options.Add(byOption);
            command.Options.Add(noteOption);
            command.SetAction(parseResult => Commands.GoalAction(parseResult.GetValue(estateOption)!, name, parseResult.GetValue(goalArgument)!, parseResult.GetValue(byOption)!, parseResult.GetValue(noteOption), parseResult.GetValue(jsonOption)));
            goal.Subcommands.Add(command);
        }

        var reasonOption = new Option<string>("--reason") { Description = "Why unattended runs of the goal may launch while such a key signs where they run.", Required = true };
        var expiresOption = new Option<string>("--expires") { Description = "When the exception stops being in force, for example 2026-10-20T00:00:00Z.", Required = true };
        var exception = new Command("custody-exception", "Record the sponsor's exception to the custody precondition for a goal's current revision: until it expires, a coding agent's run of the goal may launch unattended on a machine where a key of an operator or of the sponsor signs without its owner's presence, and the run names the exception. Recorded only by the goal's sponsor, or by an operator when an agent sponsors it; a revised goal needs a fresh one.");
        exception.Arguments.Add(goalArgument);
        exception.Options.Add(byOption);
        exception.Options.Add(reasonOption);
        exception.Options.Add(expiresOption);
        exception.SetAction(parseResult => Commands.GoalCustodyException(parseResult.GetValue(estateOption)!, parseResult.GetValue(goalArgument)!, parseResult.GetValue(byOption)!, parseResult.GetValue(reasonOption)!, parseResult.GetValue(expiresOption)!, parseResult.GetValue(jsonOption)));
        goal.Subcommands.Add(exception);

        var repositoryOption = new Option<string?>("--repository") { Description = "For an agent's step, the repository resource whose clone, registered on this machine (vericairn sources clone), its workspace is made from." };
        var settingOption = new Option<string[]>("--setting") { Description = "For an agent's step, a setting of the launcher routing chose, as name=value, for example command=<executable or JSON array>; repeatable.", AllowMultipleArgumentsPerToken = false };
        var wallMinutesOption = new Option<long?>("--wall-minutes") { Description = "For an agent's step, the run's cap in wall minutes; what remains of the goal's budget when omitted, and never more." };
        var branchOption = new Option<string?>("--branch") { Description = "For an agent's step, the work branch; work/ and the work order's key when omitted." };
        var baseOption = new Option<string?>("--base") { Description = "For an agent's step, what a new work branch starts from; the clone's HEAD when omitted." };
        var detachOption = new Option<bool>("--detach") { Description = "Return once an agent's run is launched, leaving it to the daemon that serves the estate; without it the command waits until the run settles, and Ctrl+C asks it to stop." };
        var step = new Command("step", "Take a goal's next step, one at a time: its first eligible step whose route acts, routed from this machine by the goal's roster, for an operator, the goal's sponsor when a person, or a principal holding local-engineering. An agent's step goes on with the assignment that holds it, or is assigned to the agent with the run's intent, after the worker's whole preflight, and its run starts through the worker; every refusal before the intent records nothing, the assignment included, as while a key of an operator or of the sponsor signs here without presence and no custody exception is in force. A person's step is assigned to the person and asked of them, the two recorded together; a step no principal is available for is asked of the sponsor, never given to another contributor. It activates, funds, accepts and answers nothing.");
        step.Arguments.Add(shownArgument);
        step.Options.Add(byOption);
        step.Options.Add(repositoryOption);
        step.Options.Add(settingOption);
        step.Options.Add(wallMinutesOption);
        step.Options.Add(branchOption);
        step.Options.Add(baseOption);
        step.Options.Add(detachOption);
        var executionOptions = new ExecutionOptions(step);
        step.SetAction(parseResult => Commands.GoalStep(
            parseResult.GetValue(estateOption)!,
            new Commands.GoalStepOptions(
                parseResult.GetValue(shownArgument),
                parseResult.GetValue(byOption)!,
                parseResult.GetValue(repositoryOption),
                parseResult.GetValue(settingOption) ?? [],
                parseResult.GetValue(wallMinutesOption),
                parseResult.GetValue(branchOption),
                parseResult.GetValue(baseOption),
                parseResult.GetValue(detachOption), executionOptions.Read(parseResult)),
            parseResult.GetValue(jsonOption)));
        goal.Subcommands.Add(step);

        var summaryOption = new Option<string>("--summary") { Description = "What was reviewed and how.", Required = true };
        var verdictOption = new Option<string[]>("--verdict") { Description = "A condition's verdict, n:verdict:reason with n from 1 and the verdict met, not-yet or impossible; repeatable. Every evidence statement needs one; a record predicate gets the runtime's, unless the evaluator judges one the runtime does not find met impossible." };
        var readOption = new Option<string[]>("--read") { Description = "A record the evaluator read for a condition, n:record-id; repeatable. An evidence statement found met names at least one." };
        var evaluate = new Command("evaluate", "Record the evaluation of a goal's current revision, condition by condition, with the records each verdict read; by a principal other than any step's author.");
        evaluate.Arguments.Add(goalArgument);
        evaluate.Options.Add(byOption);
        evaluate.Options.Add(summaryOption);
        evaluate.Options.Add(verdictOption);
        evaluate.Options.Add(readOption);
        evaluate.SetAction(parseResult => Commands.GoalEvaluate(parseResult.GetValue(estateOption)!, parseResult.GetValue(goalArgument)!, parseResult.GetValue(byOption)!, parseResult.GetValue(summaryOption)!, parseResult.GetValue(verdictOption) ?? [], parseResult.GetValue(readOption) ?? [], parseResult.GetValue(jsonOption)));
        goal.Subcommands.Add(evaluate);

        var statementOption = new Option<string>("--statement") { Description = "What is accepted and on what basis.", Required = true };
        var accept = new Command("accept", "Accept a goal at its current revision, relying on its current evaluation: by its sponsor when the sponsor is a person, or the principal the sponsor delegated an ordinary goal's acceptance to, and by an operator, only an operator, when an agent sponsors it; never by its evaluator or a step's author, and only once every condition is found met and every record predicate holds.");
        accept.Arguments.Add(goalArgument);
        accept.Options.Add(byOption);
        accept.Options.Add(statementOption);
        accept.SetAction(parseResult => Commands.GoalAccept(parseResult.GetValue(estateOption)!, parseResult.GetValue(goalArgument)!, parseResult.GetValue(byOption)!, parseResult.GetValue(statementOption)!, parseResult.GetValue(jsonOption)));
        goal.Subcommands.Add(accept);
        return goal;
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

    private static Command WorkFamily(Option<string> estateOption, Option<bool> jsonOption, PresentationOptions presentation)
    {
        var work = new Command("work", "Briefs, assignments, attempts, results and amendments of work orders.");
        var keyArgument = new Argument<string>("work-order") { Description = "Work-order key (for example M1-WORK) or record id." };
        var byOption = new Option<string>("--by") { Description = "Authoring principal id (ULID).", Required = true };

        var brief = new Command("brief", "Export the brief of a work order: everything a contributor needs, from the records alone.");
        brief.Arguments.Add(keyArgument);
        brief.SetAction(parseResult => Commands.WorkBrief(parseResult.GetValue(estateOption)!, parseResult.GetValue(keyArgument)!, parseResult.GetValue(jsonOption), presentation.Read(parseResult)));
        work.Subcommands.Add(brief);

        var contributorOption = new Option<string>("--contributor") { Description = "Principal id of the contributor the work is delegated to.", Required = true };
        var briefOption = new Option<string>("--brief") { Description = "Path to a JSON file holding the brief (outcome, sources, permittedChanges, prohibitedEffects, workspace, limits, expectedOutputs, reviewCriteria, recovery), or '-' for standard input.", Required = true };
        var noteOption = new Option<string?>("--note") { Description = "Free text." };
        var assign = new Command("assign", "Delegate a work order to a contributor under a brief, pinned to the exact work-order revision.");
        assign.Arguments.Add(keyArgument);
        assign.Options.Add(contributorOption);
        assign.Options.Add(byOption);
        assign.Options.Add(briefOption);
        assign.Options.Add(noteOption);
        assign.SetAction(parseResult => Commands.WorkAssign(
            parseResult.GetValue(estateOption)!,
            parseResult.GetValue(keyArgument)!,
            parseResult.GetValue(contributorOption)!,
            parseResult.GetValue(byOption)!,
            parseResult.GetValue(briefOption)!,
            parseResult.GetValue(noteOption),
            parseResult.GetValue(jsonOption)));
        work.Subcommands.Add(assign);

        var assignmentArgument = new Argument<string>("assignment") { Description = "Assignment record id." };
        var workspaceOption = new Option<string>("--workspace") { Description = "Where the work happens, named without machine paths (a repository and branch, a clone, a tool).", Required = true };
        var attemptNoteOption = new Option<string>("--note") { Description = "What the contributor intends to do.", Required = true };
        var atOption = new Option<string?>("--at") { Description = "Start time, for example 2026-10-13T14:00:00Z (default: now)." };
        var attempt = new Command("attempt", "Record the start of an attempt by the assignment's contributor.");
        attempt.Arguments.Add(assignmentArgument);
        attempt.Options.Add(byOption);
        attempt.Options.Add(workspaceOption);
        attempt.Options.Add(attemptNoteOption);
        attempt.Options.Add(atOption);
        attempt.SetAction(parseResult => Commands.WorkAttempt(
            parseResult.GetValue(estateOption)!,
            parseResult.GetValue(assignmentArgument)!,
            parseResult.GetValue(byOption)!,
            parseResult.GetValue(workspaceOption)!,
            parseResult.GetValue(attemptNoteOption)!,
            parseResult.GetValue(atOption),
            parseResult.GetValue(jsonOption)));
        work.Subcommands.Add(attempt);

        var attemptArgument = new Argument<string>("attempt") { Description = "Attempt record id." };
        var bodyOption = new Option<string>("--body") { Description = "Path to a JSON file holding the body, or '-' for standard input.", Required = true };
        var result = new Command("result", "Submit the result of an attempt: outcome, summary, outputs by hash, evidence, checks, limitations, unresolved effects, next step.");
        result.Arguments.Add(attemptArgument);
        result.Options.Add(byOption);
        result.Options.Add(bodyOption);
        result.SetAction(parseResult => Commands.WorkResult(
            parseResult.GetValue(estateOption)!,
            parseResult.GetValue(attemptArgument)!,
            parseResult.GetValue(byOption)!,
            parseResult.GetValue(bodyOption)!,
            parseResult.GetValue(jsonOption)));
        work.Subcommands.Add(result);

        var amend = new Command("amend", "Request a change to an assignment's outcome, scope, target, budget or permitted effects; it authorizes nothing.");
        amend.Arguments.Add(assignmentArgument);
        amend.Options.Add(byOption);
        amend.Options.Add(bodyOption);
        amend.SetAction(parseResult => Commands.WorkAmend(
            parseResult.GetValue(estateOption)!,
            parseResult.GetValue(assignmentArgument)!,
            parseResult.GetValue(byOption)!,
            parseResult.GetValue(bodyOption)!,
            parseResult.GetValue(jsonOption)));
        work.Subcommands.Add(amend);

        var takenOption = new Option<string?>("--contributor") { Description = "The person who takes the step over; the recording principal when omitted." };
        var takeoverNoteOption = new Option<string?>("--note") { Description = "Why; a sentence naming the hand-over when omitted." };
        var takeover = new Command("takeover", "Take over a step an agent holds: the hand-over is a new assignment of the step to a person, carrying the agent's brief and naming the agent's assignment, which holds the step from then on; every run of the agent's assignment still in flight is asked to stop. A person records it, the one taking the step over, the sponsor of its goal or an operator, never an agent.");
        takeover.Arguments.Add(assignmentArgument);
        takeover.Options.Add(byOption);
        takeover.Options.Add(takenOption);
        takeover.Options.Add(takeoverNoteOption);
        takeover.SetAction(parseResult => Commands.WorkTakeover(parseResult.GetValue(estateOption)!, parseResult.GetValue(assignmentArgument)!, parseResult.GetValue(byOption)!, parseResult.GetValue(takenOption), parseResult.GetValue(takeoverNoteOption), parseResult.GetValue(jsonOption)));
        work.Subcommands.Add(takeover);

        var observe = new Command("observe", "Show a work order's assignments, attempts, results, amendments, reviews and acceptances.");
        observe.Arguments.Add(keyArgument);
        observe.SetAction(parseResult => Commands.WorkObserve(parseResult.GetValue(estateOption)!, parseResult.GetValue(keyArgument)!, parseResult.GetValue(jsonOption), presentation.Read(parseResult)));
        work.Subcommands.Add(observe);
        return work;
    }
}
