// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using System.Text.RegularExpressions;
using Vericairn.Records.Model;
using Vericairn.Records.Types;

namespace Vericairn.Records.Estate;

/// <summary>
/// The foundation's checks of a living document at its clearance (the estate's design/living-documents.md § 8): names-version,
/// matches-generator and links-resolve. A failed check refuses the clearance; a passing one never clears authored content alone.
/// </summary>
public static partial class DocumentChecks
{
    /// <summary>A check's outcome as a document review records it.</summary>
    /// <param name="Name">The check.</param>
    /// <param name="Result">passed, failed, skipped or not-performed.</param>
    /// <param name="Summary">What it found.</param>
    public sealed record Outcome(string Name, string Result, string Summary);

    /// <summary>Runs every check a declaration names against the document's text, answering the given trigger records.</summary>
    public static IReadOnlyList<Outcome> Run(EstateModel model, RecordEnvelope declaration, string text, string documentPath, string? root, IReadOnlyList<RecordEnvelope> answered)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(declaration);
        ArgumentNullException.ThrowIfNull(text);
        var outcomes = new List<Outcome>();
        IEnumerable<string> names = declaration.Body.TryGetProperty("checks", out JsonElement checks) && checks.ValueKind == JsonValueKind.Array
            ? checks.EnumerateArray().Select(c => c.GetString() ?? string.Empty)
            : [];
        foreach (string name in names)
        {
            outcomes.Add(name switch
            {
                "names-version" => NamesVersion(text, answered),
                "matches-generator" => MatchesGenerator(model, declaration, text),
                "links-resolve" => root is null ? new Outcome(name, "not-performed", "The links are resolved where the repository is.") : LinksResolve(text, documentPath, root),
                _ => new Outcome(name, "not-performed", LivingDocuments.IsAvailable(name) ? "A pack's check; the pack runs it." : "No installed pack provides this check."),
            });
        }

        return outcomes;
    }

    /// <summary>Every generated region, and a generated document, compared with a fresh generation; the regions that differ or have lost their markers.</summary>
    public static IReadOnlyList<string> Generated(EstateModel model, RecordEnvelope declaration, string text)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(declaration);
        ArgumentNullException.ThrowIfNull(text);
        var problems = new List<string>();
        foreach ((string id, string generator, IReadOnlyList<string> inputs) in Regions(declaration))
        {
            string? fresh = DocumentGenerators.Find(generator)?.Generate(model, id, inputs);
            if (fresh is null)
            {
                continue;
            }

            string? content = GeneratedRegions.Content(text.Replace("\r\n", "\n", StringComparison.Ordinal), id);
            if (content is null)
            {
                problems.Add($"region {id} has lost its markers");
            }
            else if (content != GeneratedRegions.Normalize(fresh))
            {
                problems.Add($"region {id} differs from a fresh generation by {generator}");
            }
        }

        if (EstateModel.BodyString(declaration, "kind") == "generated" && Generator(declaration) is (string whole, IReadOnlyList<string> wholeInputs)
            && DocumentGenerators.Find(whole)?.Generate(model, null, wholeInputs) is string document && document.Replace("\r\n", "\n", StringComparison.Ordinal) != text.Replace("\r\n", "\n", StringComparison.Ordinal))
        {
            problems.Add($"the document differs from a fresh generation by {whole}");
        }

        return problems;
    }

    /// <summary>A declaration's generated regions: id, generator and inputs.</summary>
    public static IReadOnlyList<(string Id, string Generator, IReadOnlyList<string> Inputs)> Regions(RecordEnvelope declaration)
    {
        ArgumentNullException.ThrowIfNull(declaration);
        return declaration.Body.TryGetProperty("regions", out JsonElement regions) && regions.ValueKind == JsonValueKind.Array
            ? regions.EnumerateArray().Select(r => (
                r.GetProperty("id").GetString() ?? string.Empty,
                r.GetProperty("generator").GetString() ?? string.Empty,
                (IReadOnlyList<string>)(r.TryGetProperty("inputs", out JsonElement i) ? i.EnumerateArray().Select(e => e.GetString() ?? string.Empty).ToList() : []))).ToList()
            : [];
    }

    /// <summary>A generated document's generator and inputs, or null.</summary>
    public static (string Id, IReadOnlyList<string> Inputs)? Generator(RecordEnvelope declaration)
    {
        ArgumentNullException.ThrowIfNull(declaration);
        return declaration.Body.TryGetProperty("generator", out JsonElement generator) && generator.TryGetProperty("id", out JsonElement id)
            ? (id.GetString() ?? string.Empty, generator.TryGetProperty("inputs", out JsonElement i) ? i.EnumerateArray().Select(e => e.GetString() ?? string.Empty).ToList() : [])
            : null;
    }

    private static Outcome NamesVersion(string text, IReadOnlyList<RecordEnvelope> answered)
    {
        List<string> versions = answered.Where(r => r.Type == "release").Select(r => EstateModel.BodyString(r, "version")).OfType<string>().Distinct(StringComparer.Ordinal).ToList();
        if (versions.Count == 0)
        {
            return new Outcome("names-version", "skipped", "The review answers no release.");
        }

        List<string> missing = versions.Where(v => !text.Contains(v, StringComparison.Ordinal)).ToList();
        return missing.Count == 0
            ? new Outcome("names-version", "passed", $"Names {string.Join(", ", versions)}.")
            : new Outcome("names-version", "failed", $"Does not name {string.Join(", ", missing)}, the release this review answers.");
    }

    private static Outcome MatchesGenerator(EstateModel model, RecordEnvelope declaration, string text)
    {
        IReadOnlyList<string> problems = Generated(model, declaration, text);
        bool installed = Regions(declaration).Any(r => DocumentGenerators.Find(r.Generator) is not null) || (Generator(declaration) is (string id, _) && DocumentGenerators.Find(id) is not null);
        return !installed ? new Outcome("matches-generator", "not-performed", "Its generator is not installed here; the product's own tests compare it.")
            : problems.Count == 0 ? new Outcome("matches-generator", "passed", "Equals a fresh generation.")
            : new Outcome("matches-generator", "failed", string.Join("; ", problems) + ".");
    }

    private static Outcome LinksResolve(string text, string documentPath, string root)
    {
        string directory = documentPath.Contains('/', StringComparison.Ordinal) ? documentPath[..documentPath.LastIndexOf('/')] : string.Empty;
        string full = Path.GetFullPath(root);
        var broken = new List<string>();
        foreach (Match link in LinkRegex().Matches(text))
        {
            string target = link.Groups["target"].Value.Split('#')[0].Split('?')[0];
            if (target.Length == 0 || target.Contains("://", StringComparison.Ordinal) || target.StartsWith("mailto:", StringComparison.Ordinal) || target.StartsWith('/'))
            {
                continue;
            }

            string resolved = Path.GetFullPath(Path.Combine(full, directory.Replace('/', Path.DirectorySeparatorChar), Uri.UnescapeDataString(target).Replace('/', Path.DirectorySeparatorChar)));
            bool inside = resolved.StartsWith(Path.TrimEndingDirectorySeparator(full) + Path.DirectorySeparatorChar, StringComparison.Ordinal) || resolved == full;
            if (!inside || !(File.Exists(resolved) || Directory.Exists(resolved)))
            {
                broken.Add(target);
            }
        }

        return broken.Count == 0
            ? new Outcome("links-resolve", "passed", "Every relative link resolves within the repository.")
            : new Outcome("links-resolve", "failed", $"Links that do not resolve within the repository: {string.Join(", ", broken.Distinct(StringComparer.Ordinal))}.");
    }

    [GeneratedRegex(@"\]\((?<target>[^)\s]+)(\s+""[^""]*"")?\)")]
    private static partial Regex LinkRegex();
}
