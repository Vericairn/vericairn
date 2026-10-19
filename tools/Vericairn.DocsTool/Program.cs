// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.CommandLine;
using Vericairn.DocsTool;

var root = new RootCommand("Vericairn's build tooling for a release's documentation set: generate the references from this build, and compare a set with it.");
var jsonOption = new Option<bool>("--json") { Description = "Machine-readable output.", Recursive = true };
root.Options.Add(jsonOption);

var generate = new Command("generate", "Write the command-line, API and record references and the set's manifest from this build.");
var outOption = new Option<string>("--out") { Description = "Directory of the documentation set, for example docs/releases/0.4.0.", Required = true };
var documentsOption = new Option<string>("--documents") { Description = "The product's declaration of the documents every set holds (a JSON array of file names).", Required = true };
var provenanceOption = new Option<string?>("--provenance-at") { Description = "Explicit UTC provenance instant; otherwise retain the set's instant or use the exact build commit's recorded time." };
generate.Options.Add(outOption);
generate.Options.Add(documentsOption);
generate.Options.Add(provenanceOption);
generate.SetAction(parseResult => ReferenceSet.Generate(parseResult.GetValue(outOption)!, parseResult.GetValue(documentsOption)!, parseResult.GetValue(jsonOption), parseResult.GetValue(provenanceOption)));
root.Subcommands.Add(generate);

var compare = new Command("compare", "Refuse a set whose manifest differs from this build: every command, operation and record type, both ways.");
var directoryArgument = new Argument<string>("directory") { Description = "The documentation set to compare." };
compare.Arguments.Add(directoryArgument);
compare.SetAction(parseResult => ReferenceSet.CompareWithBuild(parseResult.GetValue(directoryArgument)!, parseResult.GetValue(jsonOption)));
root.Subcommands.Add(compare);

return root.Parse(args).Invoke();
