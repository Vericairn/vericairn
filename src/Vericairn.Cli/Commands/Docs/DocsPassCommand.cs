// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.CommandLine;
using System.Text;
using System.Text.Json;
using Vericairn.Protocol;
using Vericairn.Records.Identity;
using static Vericairn.Cli.CommandContext;

namespace Vericairn.Cli;

internal sealed class DocsPassCommand : Command
{
    public DocsPassCommand(CommandContext context)
        : base("pass", "List the documents still due for a publication with covered paths and changes, prepare front-document patches, and optionally assign a selection for review. Never applies or commits patches.")
    {
        var version = new Argument<string>("version") { Description = "Published release version." };
        var product = new Option<string?>("--product") { Description = "Product id or slug, required when several products published this version." };
        var reviewer = new Option<string?>("--assign-to") { Description = "Reviewer principal id: a person, builder or agent. Requires --by and a new --key." };
        var by = new Option<string?>("--by") { Description = "Principal assigning the documents; needs operator or local-engineering authority." };
        var key = new Option<string?>("--key") { Description = "New work-order key for this review assignment." };
        var documents = new Option<string[]>("--document") { Description = "Due declaration id or unambiguous path; repeat to select a group. Omitted selects every due document.", AllowMultipleArgumentsPerToken = true };
        var output = new Option<string?>("--output") { Description = "Directory for proposed .patch files, one per selected front document. Existing files are never overwritten." };
        Arguments.Add(version);
        Options.Add(product);
        Options.Add(reviewer);
        Options.Add(by);
        Options.Add(key);
        Options.Add(documents);
        Options.Add(output);
        SetAction(parse =>
        {
            string? to = parse.GetValue(reviewer), author = parse.GetValue(by), workKey = parse.GetValue(key);
            if ((to is not null && (author is null || workKey is null)) || (to is null && (author is not null || workKey is not null)))
                return context.Fail("Use --assign-to, --by and --key together.");
            return context.WithApi(parse.GetValue(context.EstateOption)!, api =>
            {
                DocumentPassResponse pass = api.DocumentPass(parse.GetValue(version)!, parse.GetValue(product));
                bool json = parse.GetValue(context.JsonOption);
                if (!pass.Ok)
                {
                    if (json) context.Output.WriteLine(JsonSerializer.Serialize(pass, ProtocolJson.Options));
                    else context.Error.WriteLine(pass.Problem!.Message);
                    return Failed;
                }
                var selected = new List<DocumentPassItem>();
                string[] names = parse.GetValue(documents) ?? [];
                if (names.Length == 0) selected.AddRange(pass.Documents);
                foreach (string name in names)
                {
                    DocumentPassItem[] matches = [.. pass.Documents.Where(d => d.Declaration.Id == name || d.Path == name)];
                    if (matches.Length != 1) return context.Fail("Each --document must identify exactly one due declaration; use its id to distinguish repositories.");
                    if (!selected.Contains(matches[0])) selected.Add(matches[0]);
                }
                if (to is not null && selected.Count == 0) return context.Fail("No selected documents remain due for this publication.");
                if (parse.GetValue(output) is { } directory)
                {
                    try { WritePatches(directory, pass.Updates.Where(u => selected.Any(d => d.Declaration.Id == u.Declaration)).ToArray()); }
                    catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
                    { return context.Fail("Patch output could not be prepared. Use an output directory that is not a symbolic link and has no existing patch files; inspect any files already written before retrying."); }
                }
                DocumentPassAssignResponse? assignment = to is null ? null : api.DocumentPassAssign(pass.Version,
                    new(author!, to, workKey!, pass.Release!, selected.Select(d => d.Declaration).ToArray()));
                if (json)
                {
                    context.Output.WriteLine(assignment is null ? JsonSerializer.Serialize(pass, ProtocolJson.Options)
                        : JsonSerializer.Serialize(new { pass, assignment }, ProtocolJson.Options));
                }
                else
                {
                    context.Output.WriteLine($"{pass.Version}: {pass.Documents.Count} documents still due for publication {pass.Release!.Id}, commit {pass.Commit}.");
                    foreach (DocumentPassRecord record in pass.Records) context.Output.WriteLine($"  release context: {record.Type} {record.Record.Id}: {record.Summary}");
                    foreach (DocumentPassItem document in pass.Documents)
                    {
                        context.Output.WriteLine($"{document.Path}  due at {document.Level}  declaration {document.Declaration.Id}  repository {document.Repository ?? "estate"}");
                        context.Output.WriteLine("  covers: " + string.Join(", ", document.CoveredPaths));
                        foreach (DocumentPassChange change in document.Changes) context.Output.WriteLine($"  {change.Kind}: {change.Path}");
                        foreach (DocumentPassRecord record in document.Records) context.Output.WriteLine($"  {record.Type} {record.Record.Id}: {record.Summary}");
                        foreach (string note in document.Notes) context.Output.WriteLine("  note: " + note);
                        foreach (DocumentPassAssignment assigned in document.Assignments) context.Output.WriteLine($"  assigned to {assigned.Reviewer}: {assigned.Key}, assignment {assigned.Assignment}");
                    }
                    context.Output.WriteLine($"{pass.Updates.Count} proposed front-document patches; inspect with --json or write them with --output. Applying and reviewing them are separate actions.");
                    if (assignment is not null)
                    {
                        if (assignment.Ok) context.Output.WriteLine($"Assigned {selected.Count} documents to {to}: {workKey}.");
                        foreach (RecordWritten written in assignment.Written) context.Output.WriteLine($"recorded {written.Id} at {written.Hash}");
                        if (assignment.Problem is not null) context.Error.WriteLine(assignment.Problem.Message);
                    }
                }
                return assignment is null || assignment.Ok ? Clean : Failed;
            }, readOnly: to is null);
        });
    }

    internal static void WritePatches(string directory, IReadOnlyList<DocumentPassUpdate> updates)
    {
        string full = Path.GetFullPath(directory);
        if (new DirectoryInfo(full).LinkTarget is not null) throw new IOException("The patch directory cannot be a symbolic link.");
        if (updates.Any(u => !Ulid.IsValid(u.Declaration)) || updates.Select(u => u.Declaration).Distinct(StringComparer.Ordinal).Count() != updates.Count)
            throw new InvalidOperationException("Patch identities must be distinct declaration ids.");
        if (updates.Any(u => Path.Exists(Path.Combine(full, u.Declaration + ".patch")))) throw new IOException("A patch already exists.");
        if (updates.Count == 0) return;
        Directory.CreateDirectory(full);
        foreach (DocumentPassUpdate update in updates)
        {
            using var file = new FileStream(Path.Combine(full, update.Declaration + ".patch"), FileMode.CreateNew, FileAccess.Write, FileShare.None);
            file.Write(Encoding.UTF8.GetBytes(update.Patch));
        }
    }
}
