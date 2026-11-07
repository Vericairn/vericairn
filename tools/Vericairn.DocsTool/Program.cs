// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.CommandLine;
using Vericairn.DocsTool;

var root = new RootCommand("Vericairn's build tooling for a release's documentation set: generate the references from this build, compare a set with it, and assemble, freeze and index the set.");
var jsonOption = new Option<bool>("--json") { Description = "Machine-readable output.", Recursive = true };
root.Options.Add(jsonOption);

var generate = new Command("generate", "Write the command-line, API and record references and the set's manifest from this build.");
var outOption = new Option<string>("--out") { Description = "Directory of the documentation set, for example docs/releases/0.4.0.", Required = true };
var documentsOption = new Option<string>("--documents") { Description = "The product's declaration of the documents every set holds (a JSON array of file names).", Required = true };
var provenanceOption = new Option<string?>("--provenance-at") { Description = "Explicit UTC provenance instant; otherwise retain the set's instant or use the exact build commit's recorded time." };
var registerOption = new Option<string?>("--register") { Description = "An explicitly selected vocabulary register snapshot for the readable reference index." };
var artifactsOption = new Option<string?>("--artifacts") { Description = "Retain companion reports for this serialized build's payload directory and package them there." };
generate.Options.Add(outOption);
generate.Options.Add(documentsOption);
generate.Options.Add(provenanceOption);
generate.Options.Add(registerOption);
generate.Options.Add(artifactsOption);
generate.SetAction(parseResult => ReferenceSet.Generate(parseResult.GetValue(outOption)!, parseResult.GetValue(documentsOption)!, parseResult.GetValue(jsonOption), parseResult.GetValue(provenanceOption), parseResult.GetValue(registerOption), parseResult.GetValue(artifactsOption)));
root.Subcommands.Add(generate);

var compare = new Command("compare", "Refuse a set whose manifest differs from this build: every command, operation and record type, both ways.");
var directoryArgument = new Argument<string>("directory") { Description = "The documentation set to compare." };
var compareArtifactsOption = new Option<string?>("--artifacts") { Description = "Also compare the retained payload observations with these exact local artifact bytes." };
compare.Arguments.Add(directoryArgument);
compare.Options.Add(compareArtifactsOption);
compare.SetAction(parseResult => ReferenceSet.CompareWithBuild(parseResult.GetValue(directoryArgument)!, parseResult.GetValue(jsonOption), parseResult.GetValue(compareArtifactsOption)));
root.Subcommands.Add(compare);

var assemble = new Command("assemble", "Assemble a set from the declared documents: the references and manifest from this build, every maintained guide copied byte for byte, api.md from api-v1.md, and the overview kept or taken once from a file.");
var assembleOut = new Option<string>("--out") { Description = "Directory of the documentation set, named for this build's version, for example docs/releases/0.9.2.", Required = true };
var assembleDocuments = new Option<string>("--documents") { Description = "The product's declaration of the documents every set holds (a JSON array of file names).", Required = true };
var overviewOption = new Option<string?>("--overview") { Description = "The set's overview, copied byte for byte to README.md when the set has none; a set's own README.md is never overwritten." };
var guidesOption = new Option<string?>("--guides") { Description = "Directory of the maintained guides the set copies; the directory holding the sets' directory when omitted, for example docs." };
var keepOption = new Option<bool>("--keep-references") { Description = "Keep the references and manifest the set already holds, generated earlier, instead of generating them from this build." };
assemble.Options.Add(assembleOut);
assemble.Options.Add(assembleDocuments);
assemble.Options.Add(overviewOption);
assemble.Options.Add(guidesOption);
assemble.Options.Add(keepOption);
assemble.Options.Add(provenanceOption);
assemble.Options.Add(registerOption);
assemble.SetAction(parseResult => ReferenceSet.Assemble(parseResult.GetValue(assembleOut)!, parseResult.GetValue(assembleDocuments)!, parseResult.GetValue(jsonOption), parseResult.GetValue(overviewOption),
    parseResult.GetValue(guidesOption), parseResult.GetValue(keepOption), parseResult.GetValue(provenanceOption), parseResult.GetValue(registerOption)));
root.Subcommands.Add(assemble);

var insert = new Command("insert", "Insert an approved section into a set's document byte for byte, before its anchor heading; refuse a missing anchor or a different section already there.");
var insertDirectory = new Argument<string>("directory") { Description = "The documentation set." };
var sectionOption = new Option<string>("--section") { Description = "The approved section, beginning with its heading line; inserted exactly as its bytes are.", Required = true };
var documentOption = new Option<string>("--document") { Description = "The set's document that receives the section.", DefaultValueFactory = _ => ReferenceSet.Notes };
var beforeOption = new Option<string>("--before") { Description = "The heading line the section is inserted before.", DefaultValueFactory = _ => ReferenceSet.NotesAnchor };
var expectedOption = new Option<string?>("--expected-hash") { Description = "The approved bytes' sha256:<hex>; a section with other bytes is refused." };
insert.Arguments.Add(insertDirectory);
insert.Options.Add(sectionOption);
insert.Options.Add(documentOption);
insert.Options.Add(beforeOption);
insert.Options.Add(expectedOption);
insert.SetAction(parseResult => ReferenceSet.Insert(parseResult.GetValue(insertDirectory)!, parseResult.GetValue(sectionOption)!, parseResult.GetValue(jsonOption), parseResult.GetValue(documentOption)!,
    parseResult.GetValue(beforeOption)!, parseResult.GetValue(expectedOption)));
root.Subcommands.Add(insert);

var freeze = new Command("freeze", "Freeze a complete set: add every file's SHA-256 to frozen-sha256.json beside it; refuse an incomplete set, an unexpected file and a frozen set that changed.");
var freezeDirectory = new Argument<string>("directory") { Description = "The documentation set." };
var freezeDocuments = new Option<string?>("--documents") { Description = "The product's declaration the set is checked against; documents.json beside the sets when omitted." };
freeze.Arguments.Add(freezeDirectory);
freeze.Options.Add(freezeDocuments);
freeze.SetAction(parseResult => ReferenceSet.Freeze(parseResult.GetValue(freezeDirectory)!, parseResult.GetValue(jsonOption), parseResult.GetValue(freezeDocuments)));
root.Subcommands.Add(freeze);

var index = new Command("index", "Add a frozen set's paragraph to the release index, README.md beside the sets, newest first under its documentation sets heading.");
var indexDirectory = new Argument<string>("directory") { Description = "The documentation set." };
var paragraphOption = new Option<string>("--paragraph") { Description = "The set's paragraph, linking its overview as [<version> set](<version>/README.md); added exactly as its bytes are.", Required = true };
index.Arguments.Add(indexDirectory);
index.Options.Add(paragraphOption);
index.SetAction(parseResult => ReferenceSet.IndexParagraph(parseResult.GetValue(indexDirectory)!, parseResult.GetValue(paragraphOption)!, parseResult.GetValue(jsonOption)));
root.Subcommands.Add(index);

var prepare = new Command("prepare", "Prepare a version's set in one command: assemble, render the notes when the set has none, insert the approved section, compare with this build, freeze and index.");
var prepareOut = new Option<string>("--out") { Description = "Directory of the documentation set, named for this build's version.", Required = true };
var prepareDocuments = new Option<string>("--documents") { Description = "The product's declaration of the documents every set holds.", Required = true };
var estateOption = new Option<string?>("--estate") { Description = "The estate whose records the notes render; needed while the set has no notes." };
var fromOption = new Option<string?>("--from") { Description = "The earlier published release the notes follow, as vericairn docs notes takes it; needed while the set has no notes." };
var sinceOption = new Option<string?>("--since") { Description = "An earlier start for the notes, as vericairn docs notes takes it." };
var securityOption = new Option<string?>("--security") { Description = "The approved section the notes open with, inserted byte for byte before their contents." };
var securityHashOption = new Option<string?>("--security-hash") { Description = "The approved section's sha256:<hex>; other bytes are refused." };
var prepareParagraph = new Option<string?>("--index-paragraph") { Description = "The set's paragraph for the release index." };
prepare.Options.Add(prepareOut);
prepare.Options.Add(prepareDocuments);
prepare.Options.Add(estateOption);
prepare.Options.Add(fromOption);
prepare.Options.Add(sinceOption);
prepare.Options.Add(overviewOption);
prepare.Options.Add(securityOption);
prepare.Options.Add(securityHashOption);
prepare.Options.Add(prepareParagraph);
prepare.Options.Add(guidesOption);
prepare.Options.Add(keepOption);
prepare.SetAction(parseResult => ReferenceSet.Prepare(parseResult.GetValue(prepareOut)!, parseResult.GetValue(prepareDocuments)!, parseResult.GetValue(jsonOption), parseResult.GetValue(estateOption),
    parseResult.GetValue(fromOption), parseResult.GetValue(sinceOption), parseResult.GetValue(overviewOption), parseResult.GetValue(securityOption), parseResult.GetValue(securityHashOption),
    parseResult.GetValue(prepareParagraph), parseResult.GetValue(guidesOption), parseResult.GetValue(keepOption)));
root.Subcommands.Add(prepare);

return root.Parse(args).Invoke();
