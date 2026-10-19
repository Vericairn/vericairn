// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.CommandLine;
using Vericairn.Protocol;

namespace Vericairn.Cli;

internal sealed class DocsRenderCommand : Command
{
    public DocsRenderCommand(CommandContext context) : base("render", "Read a registered item or collection at an explicit revision or snapshot; no output files or records are written.")
    {
        var operations = new CompanionOperations(context);
        var generator = new Argument<string>("generator") { Description = "The installed generator identity from docs companions." };
        var selection = new Option<string>("--selection") { Required = true, Description = "item or nested list at --revision; current or all-revisions at --snapshot." };
        var revision = new Option<string?>("--revision") { Description = "The exact immutable source record id; never resolved to its successor." };
        var snapshot = new Option<string?>("--snapshot") { Description = "The exact authorized snapshot from docs companions." };
        var page = new Option<int>("--page") { DefaultValueFactory = _ => 1, Description = "One-based page of the same exact selection." };
        var field = new Option<string>("--field") { DefaultValueFactory = _ => "body", Description = "body or a registered nested collection field." };
        var index = new Option<string>("--index") { DefaultValueFactory = _ => "all", Description = "all, or the one-based nested member index." };
        var audience = new Option<string>("--audience") { DefaultValueFactory = _ => "Internal", Description = "Requested audience; grants no source access." };
        var contract = new Option<int>("--contract") { DefaultValueFactory = _ => 1, Description = "Exact output contract version." };
        var declaration = new Option<string?>("--declaration") { Description = "Exact current declaration of a retained source item." };
        var commit = new Option<string?>("--commit") { Description = "Exact repository commit for that declared source; clone head is never substituted." };
        var sources = new Option<bool>("--sources") { Description = "Inspect the selected source basis without invoking the generator." };
        Arguments.Add(generator);
        foreach (Option option in new Option[] { selection, revision, snapshot, page, field, index, audience, contract, declaration, commit, sources }) Options.Add(option);
        SetAction(p => operations.Render(p.GetValue(context.EstateOption)!, new CompanionRequest(p.GetValue(generator)!, p.GetValue(selection)!,
            p.GetValue(revision), p.GetValue(snapshot), p.GetValue(page), p.GetValue(field)!, p.GetValue(index)!, p.GetValue(audience)!,
            p.GetValue(contract), p.GetValue(declaration), p.GetValue(commit)), p.GetValue(sources), p.GetValue(context.JsonOption)));
    }
}
