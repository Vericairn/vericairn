// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jayson Furr

using System.CommandLine;
using Vericairn.Cli;

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

var records = new Command("records", "Create, validate, describe and format records.");
root.Subcommands.Add(records);

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

var estate = new Command("estate", "Create and check estates.");
root.Subcommands.Add(estate);

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

return root.Parse(args).Invoke();
