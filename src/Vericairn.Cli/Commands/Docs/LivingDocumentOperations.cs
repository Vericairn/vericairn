// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using Vericairn.Protocol;
using Vericairn.Records.Estate;
using Vericairn.Records.Model;
using static Vericairn.Cli.CommandContext;

namespace Vericairn.Cli;

internal sealed class LivingDocumentOperations(CommandContext context)
{
    public int DocsLiving(string root, bool json) => context.WithApi(root, api =>
    {
        IReadOnlyList<DocumentRow> rows = api.Documents();
        if (json)
        {
            context.Output.WriteLine(JsonSerializer.Serialize(rows, ProtocolJson.Options));
            return Clean;
        }

        if (rows.Count == 0)
        {
            context.Output.WriteLine("No living documents are declared; declare one with vericairn docs declare.");
        }

        foreach (DocumentRow row in rows)
        {
            context.Output.WriteLine($"{row.Path}  {row.State}{(row.Level is null ? string.Empty : " at " + row.Level)}  {row.Title}  declaration {row.Declaration}{(row.Clearance is null ? "  never cleared" : "  cleared by " + row.Clearance)}");
            foreach (DocumentReason reason in row.Reasons)
            {
                context.Output.WriteLine($"  {reason.On} ({reason.Level}): {reason.Summary}");
            }

            foreach (string missing in row.Unavailable)
            {
                context.Output.WriteLine($"  unavailable: {missing} is provided by no installed pack");
            }

            foreach (string note in row.Notes)
            {
                context.Output.WriteLine($"  note: {note}");
            }
        }

        return Clean;
    });

    public int DocsReview(string root, string path, string by, string outcome, string summary, string[] findings, string? commit, string? independence, bool json) => context.WithApi(root, api =>
    {
        // A document is named by its path; the estate's own file unless a commit says it is in another repository.
        DocumentRow? row = api.Documents().FirstOrDefault(d => d.Path == path && (commit is null ? d.Repository is null : d.Repository is not null));
        if (row is null)
        {
            return context.Fail($"No living document {path} is declared{(commit is null ? " in the estate's own repository" : " in another repository")}; declare it with vericairn docs declare.");
        }

        var parsed = new List<DocumentFinding>();
        foreach (string finding in findings)
        {
            int colon = finding.IndexOf(':', StringComparison.Ordinal);
            string severity = colon > 0 ? finding[..colon] : string.Empty;
            if (severity is not ("error" or "warning" or "info"))
            {
                return context.Fail($"A finding is severity:summary, with severity error, warning or info: '{finding}'.");
            }

            parsed.Add(new DocumentFinding(severity, finding[(colon + 1)..].Trim()));
        }

        return context.ReportOutcome(api.DocumentReview(row.Declaration, new DocumentReviewRequest(by, outcome, summary, parsed, commit, independence)), json);
    });

    public int DocsDeclare(string root, string by, string bodyPath, string? supersedes, bool json) =>
        context.WithBody(bodyPath, body => context.WithApi(root, api => context.ReportOutcome(api.NewRecord(new NewRecordRequest("living-document", by, body, null, supersedes)), json)));

    /// <summary>Rewrites an estate document's generated regions, and a generated document, from their installed generators; nothing outside the markers changes.</summary>
    public int DocsRegenerate(string root, string path, bool json)
    {
        try
        {
            EstateModel model = LoadCopy(root);
            RecordEnvelope? declaration = model.Current("living-document").FirstOrDefault(d => EstateModel.BodyString(d, "path") == path && EstateModel.BodyString(d, "repository") is null);
            if (declaration is null)
            {
                return context.Fail($"No living document {path} is declared in the estate's own repository.");
            }

            string file = Path.Combine(root, path.Replace('/', Path.DirectorySeparatorChar));
            string text = File.Exists(file) ? File.ReadAllText(file).Replace("\r\n", "\n", StringComparison.Ordinal) : string.Empty;
            var rewritten = new List<string>();
            var missing = new List<string>();
            foreach ((string id, string generator, IReadOnlyList<string> inputs) in DocumentChecks.Regions(declaration))
            {
                if (DocumentGenerators.Find(generator)?.Generate(model, id, inputs) is not string fresh)
                {
                    missing.Add($"{id} ({generator} is not installed here)");
                    continue;
                }

                if (GeneratedRegions.Replace(text, id, fresh) is not string replaced)
                {
                    missing.Add($"{id} (its markers {GeneratedRegions.Start(id)} and {GeneratedRegions.End(id)} are not in place)");
                    continue;
                }

                if (replaced != text)
                {
                    rewritten.Add(id);
                    text = replaced;
                }
            }

            if (EstateModel.BodyString(declaration, "kind") == "generated" && DocumentChecks.Generator(declaration) is (string whole, IReadOnlyList<string> wholeInputs)
                && DocumentGenerators.Find(whole)?.Generate(model, null, wholeInputs) is string document && document.Replace("\r\n", "\n", StringComparison.Ordinal) != text)
            {
                text = document.Replace("\r\n", "\n", StringComparison.Ordinal);
                rewritten.Add("the whole document");
            }

            if (rewritten.Count > 0)
            {
                File.WriteAllText(file, text);
            }

            if (json)
            {
                context.Output.WriteLine(JsonSerializer.Serialize(new { path, rewritten, missing }));
            }
            else
            {
                context.Output.WriteLine(rewritten.Count == 0 ? $"{path}: every generated region is current." : $"{path}: rewrote {string.Join(", ", rewritten)}.");
                foreach (string gap in missing)
                {
                    context.Error.WriteLine($"{path}: not regenerated: {gap}");
                }
            }

            return missing.Count == 0 ? Clean : Failed;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException or JsonException)
        {
            return context.Fail(exception.Message);
        }
    }
}
