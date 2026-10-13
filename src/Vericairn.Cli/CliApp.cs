// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.CommandLine;

namespace Vericairn.Cli;

/// <summary>The command tree of <c>vericairn</c>; built once per invocation so tests can drive it in-process.</summary>
internal static class CliApp
{
    /// <summary>Builds the root command with every family: records, estate, work, devices, release, sources, check, runtime, recover, objects, docs, status, decisions, doctor, version.</summary>
    public static RootCommand Build()
    {
        var estateOption = new Option<string>("--estate", "-e")
        {
            Description = "Path to the estate repository root.",
            DefaultValueFactory = _ => Directory.GetCurrentDirectory(),
            Recursive = true,
        };
        var jsonOption = new Option<bool>("--json")
        {
            Description = "Machine-readable output.",
            Recursive = true,
        };

        var root = new RootCommand("Vericairn: records, estates and the tools that keep them honest.");
        root.Options.Add(estateOption);
        root.Options.Add(jsonOption);
        root.Subcommands.Add(RecordsFamily(estateOption, jsonOption));
        root.Subcommands.Add(EstateFamily(estateOption, jsonOption));
        root.Subcommands.Add(WorkFamily(estateOption, jsonOption));
        root.Subcommands.Add(DevicesFamily(estateOption, jsonOption));
        root.Subcommands.Add(ReleaseFamily(estateOption, jsonOption));
        root.Subcommands.Add(SourcesFamily(estateOption, jsonOption));
        root.Subcommands.Add(CheckFamily(estateOption, jsonOption));
        root.Subcommands.Add(RuntimeFamily(estateOption, jsonOption));
        root.Subcommands.Add(RecoverFamily(estateOption));
        root.Subcommands.Add(ObjectsFamily(estateOption, jsonOption));
        root.Subcommands.Add(DocsFamily(estateOption, jsonOption));

        var status = new Command("status", "Show the estate's current state derived from its records.");
        status.SetAction(parseResult => Commands.Status(parseResult.GetValue(estateOption)!, parseResult.GetValue(jsonOption)));
        root.Subcommands.Add(status);

        var decisions = new Command("decisions", "List the estate's current decisions, or show one cleanly.");
        var decisionIdArgument = new Argument<string?>("id") { Description = "Decision record id (any revision); omit to list.", Arity = ArgumentArity.ZeroOrOne };
        decisions.Arguments.Add(decisionIdArgument);
        decisions.SetAction(parseResult => Commands.Decisions(parseResult.GetValue(estateOption)!, parseResult.GetValue(decisionIdArgument), parseResult.GetValue(jsonOption)));
        root.Subcommands.Add(decisions);

        var doctor = new Command("doctor", "Check this machine's prerequisites.");
        doctor.SetAction(parseResult => Commands.Doctor(parseResult.GetValue(estateOption)!));
        root.Subcommands.Add(doctor);

        var version = new Command("version", "Print the version.");
        version.SetAction(_ => Commands.Version());
        root.Subcommands.Add(version);
        return root;
    }

    private static Command DocsFamily(Option<string> estateOption, Option<bool> jsonOption)
    {
        var docs = new Command("docs", "A release's documentation set: generate the references from this build, the notes from an estate's records, and check a set for completeness.");

        var generate = new Command("generate", "Write the command-line, API and record references and the set's manifest from this build.");
        var generateOutOption = new Option<string>("--out") { Description = "Directory of the documentation set, for example docs/releases/0.3.0.", Required = true };
        generate.Options.Add(generateOutOption);
        generate.SetAction(parseResult => Docs.Generate(parseResult.GetValue(generateOutOption)!, parseResult.GetValue(jsonOption)));
        docs.Subcommands.Add(generate);

        var notes = new Command("notes", "Write release notes from the estate's records between two published releases.");
        var fromOption = new Option<string>("--from") { Description = "The earlier published release's version; the notes start after its publication.", Required = true };
        var toOption = new Option<string>("--to") { Description = "The release's version; its publication ends the notes.", Required = true };
        var notesOutOption = new Option<string>("--out") { Description = "Directory of the documentation set the notes belong to.", Required = true };
        notes.Options.Add(fromOption);
        notes.Options.Add(toOption);
        notes.Options.Add(notesOutOption);
        notes.SetAction(parseResult => Docs.Notes(parseResult.GetValue(estateOption)!, parseResult.GetValue(fromOption)!, parseResult.GetValue(toOption)!, parseResult.GetValue(notesOutOption)!, parseResult.GetValue(jsonOption)));
        docs.Subcommands.Add(notes);

        var check = new Command("check", "Refuse a documentation set that lacks a document, a command, an API operation or a record type; every gap is listed at once.");
        var directoryArgument = new Argument<string>("directory") { Description = "The documentation set to check." };
        check.Arguments.Add(directoryArgument);
        check.SetAction(parseResult => Docs.Check(parseResult.GetValue(directoryArgument)!, parseResult.GetValue(jsonOption)));
        docs.Subcommands.Add(check);
        return docs;
    }

    private static Command RecordsFamily(Option<string> estateOption, Option<bool> jsonOption)
    {
        var records = new Command("records", "Create, validate, describe and format records.");

        var validate = new Command("validate", "Parse and validate every record and the manifest of an estate.");
        validate.SetAction(parseResult => Commands.Validate(parseResult.GetValue(estateOption)!, parseResult.GetValue(jsonOption)));
        records.Subcommands.Add(validate);

        var typeArgument = new Argument<string>("type") { Description = "Record type, for example work-order." };
        var byOption = new Option<string>("--by") { Description = "Authoring principal id (ULID).", Required = true };
        var bodyOption = new Option<string>("--body") { Description = "Path to a JSON file holding the body, or '-' for standard input.", Required = true };
        var subjectOption = new Option<string?>("--subject") { Description = "Subject as type:id or type:id:hash." };
        var supersedesOption = new Option<string?>("--supersedes") { Description = "Id of the record this one replaces." };
        var atOption = new Option<string?>("--at") { Description = "Authoring time, for example 2026-10-13T14:00:00Z (default: now)." };
        var idOption = new Option<string?>("--id") { Description = "Explicit record id; only the root operator principal needs this." };
        var newCommand = new Command("new", "Create a record in canonical form, validate the estate with it, and write it.");
        newCommand.Arguments.Add(typeArgument);
        newCommand.Options.Add(byOption);
        newCommand.Options.Add(bodyOption);
        newCommand.Options.Add(subjectOption);
        newCommand.Options.Add(supersedesOption);
        newCommand.Options.Add(atOption);
        newCommand.Options.Add(idOption);
        newCommand.SetAction(parseResult => Commands.New(
            parseResult.GetValue(estateOption)!,
            parseResult.GetValue(typeArgument)!,
            parseResult.GetValue(byOption)!,
            parseResult.GetValue(bodyOption)!,
            parseResult.GetValue(subjectOption),
            parseResult.GetValue(supersedesOption),
            parseResult.GetValue(atOption),
            parseResult.GetValue(idOption),
            parseResult.GetValue(jsonOption)));
        records.Subcommands.Add(newCommand);

        var describeTypeArgument = new Argument<string?>("type") { Description = "Record type to describe; omit to list all types.", Arity = ArgumentArity.ZeroOrOne };
        var describe = new Command("describe", "Explain record types and their fields.");
        describe.Arguments.Add(describeTypeArgument);
        describe.SetAction(parseResult => Commands.Describe(parseResult.GetValue(describeTypeArgument)));
        records.Subcommands.Add(describe);

        var filesArgument = new Argument<string[]>("files") { Description = "Record files to rewrite in canonical pretty form.", Arity = ArgumentArity.OneOrMore };
        var format = new Command("format", "Rewrite record files in canonical pretty form without changing their content or hash.");
        format.Arguments.Add(filesArgument);
        format.SetAction(parseResult => Commands.Format(parseResult.GetValue(filesArgument)!));
        records.Subcommands.Add(format);

        var hashFileArgument = new Argument<string>("file") { Description = "Record file." };
        var hash = new Command("hash", "Print the computed content hash of a record file.");
        hash.Arguments.Add(hashFileArgument);
        hash.SetAction(parseResult => Commands.Hash(parseResult.GetValue(hashFileArgument)!));
        records.Subcommands.Add(hash);
        return records;
    }

    private static Command EstateFamily(Option<string> estateOption, Option<bool> jsonOption)
    {
        var estate = new Command("estate", "Create and check estates.");

        var nameOption = new Option<string>("--name") { Description = "Estate display name.", Required = true };
        var slugOption = new Option<string>("--slug") { Description = "Estate slug (lower-case kebab-case).", Required = true };
        var purposeOption = new Option<string>("--purpose") { Description = "Why the estate exists.", Required = true };
        var operatorNameOption = new Option<string>("--operator-name") { Description = "Display name of the root operator (a human).", Required = true };
        var organizationOption = new Option<string>("--organization") { Description = "Organization display name.", Required = true };
        var githubOption = new Option<string?>("--github") { Description = "GitHub organization login, if any." };
        var repositoryOption = new Option<string>("--repository") { Description = "GitHub owner/name of the Vericairn source to pin.", DefaultValueFactory = _ => "Vericairn/vericairn" };
        var commitOption = new Option<string>("--commit") { Description = "Commit of the Vericairn source to pin (40 hex digits).", Required = true };
        var profileOption = new Option<string>("--profile") { Description = "Storage profile: repository-synced (records as files in a git repository) or local-only (records in this machine's live store).", DefaultValueFactory = _ => "repository-synced" };
        var init = new Command("init", "Create a new estate: operator principal, charter and manifest, validated together.");
        init.Options.Add(profileOption);
        init.Options.Add(nameOption);
        init.Options.Add(slugOption);
        init.Options.Add(purposeOption);
        init.Options.Add(operatorNameOption);
        init.Options.Add(organizationOption);
        init.Options.Add(githubOption);
        init.Options.Add(repositoryOption);
        init.Options.Add(commitOption);
        init.SetAction(parseResult => Commands.Init(
            parseResult.GetValue(estateOption)!,
            parseResult.GetValue(nameOption)!,
            parseResult.GetValue(slugOption)!,
            parseResult.GetValue(purposeOption)!,
            parseResult.GetValue(operatorNameOption)!,
            parseResult.GetValue(organizationOption)!,
            parseResult.GetValue(githubOption),
            parseResult.GetValue(repositoryOption)!,
            parseResult.GetValue(commitOption)!,
            parseResult.GetValue(profileOption)!,
            parseResult.GetValue(jsonOption)));
        estate.Subcommands.Add(init);

        var check = new Command("check", "Validate records and run repository cleanliness and portability checks.");
        check.SetAction(parseResult => Commands.Check(parseResult.GetValue(estateOption)!, parseResult.GetValue(jsonOption)));
        estate.Subcommands.Add(check);

        var outOption = new Option<string>("--out") { Description = "Path of the export file to create (never overwritten).", Required = true };
        var exportByOption = new Option<string?>("--by") { Description = "Principal id recorded as the exporter." };
        var export = new Command("export", "Write a verified export: records, objects, manifest and documents with an inventory; no keys or authority.");
        export.Options.Add(outOption);
        export.Options.Add(exportByOption);
        export.SetAction(parseResult => Commands.Export(parseResult.GetValue(estateOption)!, parseResult.GetValue(outOption)!, parseResult.GetValue(exportByOption), parseResult.GetValue(jsonOption)));
        estate.Subcommands.Add(export);

        var fromOption = new Option<string>("--from") { Description = "Export file.", Required = true };
        var toOption = new Option<string>("--to") { Description = "Empty destination directory.", Required = true };
        var restore = new Command("restore", "Verify an export against its inventory and restore it into an empty directory as a repository-synced estate; authority is not restored.");
        restore.Options.Add(fromOption);
        restore.Options.Add(toOption);
        restore.SetAction(parseResult => Commands.Restore(parseResult.GetValue(fromOption)!, parseResult.GetValue(toOption)!, parseResult.GetValue(jsonOption)));
        estate.Subcommands.Add(restore);

        var toVersionOption = new Option<string>("--to") { Description = "Version of a published release recorded in this estate.", Required = true };
        var upgradeByOption = new Option<string?>("--by") { Description = "Principal id applying the upgrade (required unless --check)." };
        var checkOnlyOption = new Option<bool>("--check") { Description = "Show the plan without changing anything." };
        var upgrade = new Command("upgrade", "Pin the estate to a published release and apply record-format migrations as one reviewable change with a migration record.");
        upgrade.Options.Add(toVersionOption);
        upgrade.Options.Add(upgradeByOption);
        upgrade.Options.Add(checkOnlyOption);
        upgrade.SetAction(parseResult => Commands.Upgrade(parseResult.GetValue(estateOption)!, parseResult.GetValue(toVersionOption)!, parseResult.GetValue(upgradeByOption), parseResult.GetValue(checkOnlyOption), parseResult.GetValue(jsonOption)));
        estate.Subcommands.Add(upgrade);

        var reconcile = new Command("reconcile", "List what two diverged copies left to settle: divergent revisions, duplicate keys, duplicate results.");
        reconcile.SetAction(parseResult => Commands.Reconcile(parseResult.GetValue(estateOption)!, parseResult.GetValue(jsonOption)));
        estate.Subcommands.Add(reconcile);
        return estate;
    }

    private static Command RuntimeFamily(Option<string> estateOption, Option<bool> jsonOption)
    {
        var runtime = new Command("runtime", "The daemon that owns an estate's live state on this machine: serve, start, stop, status, install, uninstall.");
        var noLoopbackOption = new Option<bool>("--no-loopback") { Description = "Serve the local socket only, without the pinned loopback TLS endpoint." };
        var serve = new Command("serve", "Serve the estate in this process until stopped (Ctrl+C or 'runtime stop'); refuses when a daemon already serves it.");
        serve.Options.Add(noLoopbackOption);
        serve.SetAction(parseResult => Commands.RuntimeServe(parseResult.GetValue(estateOption)!, !parseResult.GetValue(noLoopbackOption)));
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
        return runtime;
    }

    private static Command ReleaseFamily(Option<string> estateOption, Option<bool> jsonOption)
    {
        var release = new Command("release", "Releases: list the records, submit a release through the GitHub Releases provider as intent before effect, observe the run, the draft and the publication.");
        var list = new Command("list", "List current release records: version, status, commit, id, signing.");
        list.SetAction(parseResult => Commands.ReleaseList(parseResult.GetValue(estateOption)!, parseResult.GetValue(jsonOption)));
        release.Subcommands.Add(list);

        var byOption = new Option<string>("--by") { Description = "Principal id recording the operation (ULID).", Required = true };
        var versionOption = new Option<string>("--version") { Description = "The version to release, for example 0.2.0.", Required = true };
        var repositoryOption = new Option<string>("--repository") { Description = "GitHub owner/name of the source repository.", Required = true };
        var productOption = new Option<string>("--product") { Description = "Product record id the release belongs to.", Required = true };
        var refOption = new Option<string>("--ref") { Description = "Branch or tag to build from.", DefaultValueFactory = _ => "main" };
        var workflowOption = new Option<string>("--workflow") { Description = "Release workflow file.", DefaultValueFactory = _ => "release.yml" };
        var reconciliationOption = new Option<string?>("--reconciliation") { Description = "How an earlier failed, canceled or uncertain attempt under the same identity was settled; required for a retry." };
        var submit = new Command("submit", "Record the intent, then dispatch the release workflow; the run id and outcome are recorded as they are observed. Never publishes.");
        submit.Options.Add(productOption);
        submit.Options.Add(versionOption);
        submit.Options.Add(repositoryOption);
        submit.Options.Add(byOption);
        submit.Options.Add(refOption);
        submit.Options.Add(workflowOption);
        submit.Options.Add(reconciliationOption);
        submit.SetAction(parseResult => Commands.ReleaseSubmit(
            parseResult.GetValue(estateOption)!,
            parseResult.GetValue(productOption)!,
            parseResult.GetValue(versionOption)!,
            parseResult.GetValue(repositoryOption)!,
            parseResult.GetValue(byOption)!,
            parseResult.GetValue(refOption)!,
            parseResult.GetValue(workflowOption)!,
            parseResult.GetValue(reconciliationOption),
            parseResult.GetValue(jsonOption)));
        release.Subcommands.Add(submit);

        var qualifiedOption = new Option<string[]>("--qualified") { Description = "Platform whose qualification is recorded for the candidate; repeatable.", AllowMultipleArgumentsPerToken = true };
        var limitationOption = new Option<string[]>("--limitation") { Description = "A known limitation of the candidate; repeatable.", AllowMultipleArgumentsPerToken = true };
        var signingOption = new Option<string>("--signing") { Description = "Signing state recorded on the candidate.", DefaultValueFactory = _ => "unsigned developer build" };
        var cancelOption = new Option<string?>("--cancel") { Description = "Settle an intent that was never dispatched, or an outcome reconciled as having had no effect, with this reason." };
        var observe = new Command("observe", "Observe the run and the draft; record the outcome, the candidate release, and later the operator's manual publication.");
        observe.Options.Add(versionOption);
        observe.Options.Add(repositoryOption);
        observe.Options.Add(byOption);
        observe.Options.Add(qualifiedOption);
        observe.Options.Add(limitationOption);
        observe.Options.Add(signingOption);
        observe.Options.Add(cancelOption);
        observe.SetAction(parseResult => Commands.ReleaseObserve(
            parseResult.GetValue(estateOption)!,
            parseResult.GetValue(versionOption)!,
            parseResult.GetValue(repositoryOption)!,
            parseResult.GetValue(byOption)!,
            parseResult.GetValue(qualifiedOption) ?? [],
            parseResult.GetValue(limitationOption) ?? [],
            parseResult.GetValue(signingOption)!,
            parseResult.GetValue(cancelOption),
            parseResult.GetValue(jsonOption)));
        release.Subcommands.Add(observe);
        return release;
    }

    private static Command SourcesFamily(Option<string> estateOption, Option<bool> jsonOption)
    {
        var sources = new Command("sources", "Repositories the estate uses: inspect read-only, adopt as a resource, observe again.");
        var pathArgument = new Argument<string>("path") { Description = "Path of a git working tree on this machine; never recorded." };
        var inspect = new Command("inspect", "Observe a repository read-only: commit, branch, remote, working-tree state, tags, recent history.");
        inspect.Arguments.Add(pathArgument);
        inspect.SetAction(parseResult => Commands.SourcesInspect(parseResult.GetValue(pathArgument)!, parseResult.GetValue(jsonOption)));
        sources.Subcommands.Add(inspect);

        var byOption = new Option<string>("--by") { Description = "Authoring principal id (ULID).", Required = true };
        var nameOption = new Option<string>("--name") { Description = "Display name of the resource.", Required = true };
        var roleOption = new Option<string>("--role") { Description = "What the estate uses the repository for.", Required = true };
        var visibilityOption = new Option<string>("--visibility") { Description = "public, private or n/a.", DefaultValueFactory = _ => "private" };
        var notesOption = new Option<string?>("--notes") { Description = "Free text." };
        var adopt = new Command("adopt", "Adopt a repository as a resource by observation: a resource record and a first observation; nothing in the repository changes.");
        adopt.Arguments.Add(pathArgument);
        adopt.Options.Add(byOption);
        adopt.Options.Add(nameOption);
        adopt.Options.Add(roleOption);
        adopt.Options.Add(visibilityOption);
        adopt.Options.Add(notesOption);
        adopt.SetAction(parseResult => Commands.SourcesAdopt(
            parseResult.GetValue(estateOption)!,
            parseResult.GetValue(pathArgument)!,
            parseResult.GetValue(byOption)!,
            parseResult.GetValue(nameOption)!,
            parseResult.GetValue(roleOption)!,
            parseResult.GetValue(visibilityOption)!,
            parseResult.GetValue(notesOption),
            parseResult.GetValue(jsonOption)));
        sources.Subcommands.Add(adopt);

        var resourceOption = new Option<string>("--resource") { Description = "Resource record id of the adopted repository.", Required = true };
        var observe = new Command("observe", "Record a fresh observation of an adopted repository.");
        observe.Arguments.Add(pathArgument);
        observe.Options.Add(resourceOption);
        observe.Options.Add(byOption);
        observe.SetAction(parseResult => Commands.SourcesObserve(parseResult.GetValue(estateOption)!, parseResult.GetValue(pathArgument)!, parseResult.GetValue(resourceOption)!, parseResult.GetValue(byOption)!, parseResult.GetValue(jsonOption)));
        sources.Subcommands.Add(observe);
        return sources;
    }

    private static Command CheckFamily(Option<string> estateOption, Option<bool> jsonOption)
    {
        var check = new Command("check", "Run a check and record its evidence as a verification of the exact commit, or observe a workflow run.");
        var byOption = new Option<string>("--by") { Description = "Authoring principal id (ULID).", Required = true };
        var resourceOption = new Option<string>("--resource") { Description = "Resource record id of the repository checked.", Required = true };
        var inOption = new Option<string>("--in") { Description = "Working tree the command runs in.", DefaultValueFactory = _ => Directory.GetCurrentDirectory() };
        var roleOption = new Option<string>("--role") { Description = "self, independent-agent, operator or automation.", DefaultValueFactory = _ => "self" };
        var summaryOption = new Option<string?>("--summary") { Description = "What the check establishes, in your words; the observed exit and duration are recorded regardless." };
        var timeoutOption = new Option<int>("--timeout") { Description = "Seconds after which the command is stopped and its result recorded as unknown.", DefaultValueFactory = _ => 3600 };
        var commandArgument = new Argument<string[]>("command") { Description = "The command and its arguments; put -- before it.", Arity = ArgumentArity.OneOrMore };
        var run = new Command("run", "Run a command in a repository and record a verification: commit, command, environment, exit code, duration and output as evidence.");
        run.Options.Add(resourceOption);
        run.Options.Add(byOption);
        run.Options.Add(inOption);
        run.Options.Add(roleOption);
        run.Options.Add(summaryOption);
        run.Options.Add(timeoutOption);
        run.Arguments.Add(commandArgument);
        run.SetAction(parseResult => Commands.CheckRun(
            parseResult.GetValue(estateOption)!,
            parseResult.GetValue(resourceOption)!,
            parseResult.GetValue(byOption)!,
            parseResult.GetValue(inOption)!,
            parseResult.GetValue(roleOption)!,
            parseResult.GetValue(summaryOption),
            parseResult.GetValue(timeoutOption),
            parseResult.GetValue(commandArgument)!,
            parseResult.GetValue(jsonOption)));
        check.Subcommands.Add(run);

        var repositoryOption = new Option<string>("--repository") { Description = "GitHub owner/name.", Required = true };
        var runOption = new Option<string>("--run") { Description = "Workflow run id.", Required = true };
        var workflowOption = new Option<string>("--workflow") { Description = "Workflow file the run belongs to.", DefaultValueFactory = _ => "ci.yml" };
        var observe = new Command("observe", "Observe a workflow run at its host and record it as automation's verification of the commit it built.");
        observe.Options.Add(resourceOption);
        observe.Options.Add(repositoryOption);
        observe.Options.Add(runOption);
        observe.Options.Add(workflowOption);
        observe.Options.Add(byOption);
        observe.SetAction(parseResult => Commands.CheckObserve(
            parseResult.GetValue(estateOption)!,
            parseResult.GetValue(resourceOption)!,
            parseResult.GetValue(repositoryOption)!,
            parseResult.GetValue(runOption)!,
            parseResult.GetValue(workflowOption)!,
            parseResult.GetValue(byOption)!,
            parseResult.GetValue(jsonOption)));
        check.Subcommands.Add(observe);
        return check;
    }

    private static Command RecoverFamily(Option<string> estateOption)
    {
        var recover = new Command("recover", "After an interruption or a move: inspect what this copy holds and compare it with another.");
        var inspect = new Command("inspect", "What this copy knows, which devices can author from here, work in progress, unresolved effects and reconcile tasks.");
        inspect.SetAction(parseResult => Commands.RecoverInspect(parseResult.GetValue(estateOption)!));
        recover.Subcommands.Add(inspect);
        var withOption = new Option<string>("--with") { Description = "Root of the other copy.", Required = true };
        var compare = new Command("compare", "Records only in this copy, only in the other, or (never expected) differing in content.");
        compare.Options.Add(withOption);
        compare.SetAction(parseResult => Commands.RecoverCompare(parseResult.GetValue(estateOption)!, parseResult.GetValue(withOption)!));
        recover.Subcommands.Add(compare);
        return recover;
    }

    private static Command ObjectsFamily(Option<string> estateOption, Option<bool> jsonOption)
    {
        var objects = new Command("objects", "Content-addressed objects: put, list, verify.");
        var fileArgument = new Argument<string>("file") { Description = "File whose bytes become an object." };
        var put = new Command("put", "Store a file's bytes as an object named by their hash.");
        put.Arguments.Add(fileArgument);
        put.SetAction(parseResult => Commands.ObjectsPut(parseResult.GetValue(estateOption)!, parseResult.GetValue(fileArgument)!, parseResult.GetValue(jsonOption)));
        objects.Subcommands.Add(put);
        var list = new Command("list", "List held objects.");
        list.SetAction(parseResult => Commands.ObjectsList(parseResult.GetValue(estateOption)!, parseResult.GetValue(jsonOption)));
        objects.Subcommands.Add(list);
        var verify = new Command("verify", "Read every object and check its bytes against its name.");
        verify.SetAction(parseResult => Commands.ObjectsVerify(parseResult.GetValue(estateOption)!, parseResult.GetValue(jsonOption)));
        objects.Subcommands.Add(verify);
        return objects;
    }

    private static Command DevicesFamily(Option<string> estateOption, Option<bool> jsonOption)
    {
        var devices = new Command("devices", "Device keys: create a device for a principal on this machine, list, admit and revoke.");
        var byOption = new Option<string>("--by") { Description = "Authoring principal id (ULID).", Required = true };

        var principalOption = new Option<string>("--principal") { Description = "The principal this device acts for.", Required = true };
        var nameOption = new Option<string>("--name") { Description = "Descriptive device name, never a machine path.", Required = true };
        var custodyOption = new Option<string?>("--custody") { Description = "Key custody: default (CNG on Windows, owner-only file elsewhere), file, or cng." };
        var profilesOption = new Option<string[]>("--profile") { Description = "Permitted connectivity profile; repeatable (default: embedded).", AllowMultipleArgumentsPerToken = true };
        var create = new Command("create", "Generate a key on this machine and record the device, signed by its own key.");
        create.Options.Add(principalOption);
        create.Options.Add(nameOption);
        create.Options.Add(custodyOption);
        create.Options.Add(profilesOption);
        create.SetAction(parseResult => Commands.DevicesCreate(
            parseResult.GetValue(estateOption)!,
            parseResult.GetValue(principalOption)!,
            parseResult.GetValue(nameOption)!,
            parseResult.GetValue(custodyOption),
            parseResult.GetValue(profilesOption) ?? [],
            parseResult.GetValue(jsonOption)));
        devices.Subcommands.Add(create);

        var list = new Command("list", "List device records with their admission state and whether their key is held here.");
        list.SetAction(parseResult => Commands.DevicesList(parseResult.GetValue(estateOption)!, parseResult.GetValue(jsonOption)));
        devices.Subcommands.Add(list);

        var deviceArgument = new Argument<string>("device") { Description = "Device record id." };
        var noteOption = new Option<string>("--note") { Description = "Why the device is admitted.", Required = true };
        var admit = new Command("admit", "Admit a device to author records in this estate (operator or 'administer' scope).");
        admit.Arguments.Add(deviceArgument);
        admit.Options.Add(byOption);
        admit.Options.Add(noteOption);
        admit.SetAction(parseResult => Commands.DevicesAdmit(parseResult.GetValue(estateOption)!, parseResult.GetValue(deviceArgument)!, parseResult.GetValue(byOption)!, parseResult.GetValue(noteOption)!, parseResult.GetValue(jsonOption)));
        devices.Subcommands.Add(admit);

        var reasonOption = new Option<string>("--reason") { Description = "Why the admission ends.", Required = true };
        var revoke = new Command("revoke", "Revoke a device's admission from now on; its earlier records stay attributable.");
        revoke.Arguments.Add(deviceArgument);
        revoke.Options.Add(byOption);
        revoke.Options.Add(reasonOption);
        revoke.SetAction(parseResult => Commands.DevicesRevoke(parseResult.GetValue(estateOption)!, parseResult.GetValue(deviceArgument)!, parseResult.GetValue(byOption)!, parseResult.GetValue(reasonOption)!, parseResult.GetValue(jsonOption)));
        devices.Subcommands.Add(revoke);
        return devices;
    }

    private static Command WorkFamily(Option<string> estateOption, Option<bool> jsonOption)
    {
        var work = new Command("work", "Briefs, assignments, attempts, results and amendments of work orders.");
        var keyArgument = new Argument<string>("work-order") { Description = "Work-order key (for example M1-WORK) or record id." };
        var byOption = new Option<string>("--by") { Description = "Authoring principal id (ULID).", Required = true };

        var brief = new Command("brief", "Export the brief of a work order: everything a contributor needs, from the records alone.");
        brief.Arguments.Add(keyArgument);
        brief.SetAction(parseResult => Commands.WorkBrief(parseResult.GetValue(estateOption)!, parseResult.GetValue(keyArgument)!, parseResult.GetValue(jsonOption)));
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

        var observe = new Command("observe", "Show a work order's assignments, attempts, results, amendments, reviews and acceptances.");
        observe.Arguments.Add(keyArgument);
        observe.SetAction(parseResult => Commands.WorkObserve(parseResult.GetValue(estateOption)!, parseResult.GetValue(keyArgument)!, parseResult.GetValue(jsonOption)));
        work.Subcommands.Add(observe);
        return work;
    }
}
