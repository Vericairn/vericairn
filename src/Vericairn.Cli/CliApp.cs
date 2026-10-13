// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jayson Furr

using System.CommandLine;

namespace Vericairn.Cli;

/// <summary>The command tree of <c>vericairn</c>; built once per invocation so tests can drive it in-process.</summary>
internal static class CliApp
{
    /// <summary>Builds the root command with every family: records, estate, work, status, decisions, doctor, version.</summary>
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
        var init = new Command("init", "Create a new estate: operator principal, charter and manifest, validated together.");
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
            parseResult.GetValue(jsonOption)));
        estate.Subcommands.Add(init);

        var check = new Command("check", "Validate records and run repository cleanliness and portability checks.");
        check.SetAction(parseResult => Commands.Check(parseResult.GetValue(estateOption)!, parseResult.GetValue(jsonOption)));
        estate.Subcommands.Add(check);
        return estate;
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
