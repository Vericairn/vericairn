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

    /// <summary>
    /// Runs every check a declaration names against the document's text, answering the given trigger records. Links resolve in the
    /// estate's working tree when a root is given, else through <paramref name="exists"/>, which answers for a repository-relative path.
    /// </summary>
    public static IReadOnlyList<Outcome> Run(EstateModel model, RecordEnvelope declaration, string text, string documentPath, string? root, IReadOnlyList<RecordEnvelope> answered, Func<string, bool>? exists = null)
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
                "links-resolve" => root is not null ? LinksResolve(text, documentPath, relative => File.Exists(Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar))) || Directory.Exists(Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar))))
                    : exists is not null ? LinksResolve(text, documentPath, exists)
                    : new Outcome(name, "not-performed", "The links are resolved where the repository is."),
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
        if (GeneratedDocumentContract.IsStrict(model, declaration))
            return GeneratedDocumentVerification.Plan(model, declaration).Problems;
        bool strictRegions = declaration.Format >= 3 && EstateModel.BodyString(declaration, "kind") == "authored";
        var problems = new List<string>();
        foreach ((string id, string generator, IReadOnlyList<string> inputs) in Regions(declaration))
        {
            string? fresh = DocumentGenerators.Find(generator)?.Generate(model, id, inputs);
            if (fresh is null)
            {
                if (strictRegions) problems.Add($"region {id}: generator {generator} is unavailable");
                continue;
            }

            string? content = GeneratedRegions.Content(strictRegions ? text : text.Replace("\r\n", "\n", StringComparison.Ordinal), id);
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
        bool strict = GeneratedDocumentContract.IsStrict(model, declaration) || (declaration.Format >= 3 && EstateModel.BodyString(declaration, "kind") == "authored");
        return !installed && !strict ? new Outcome("matches-generator", "not-performed", "Its generator is not installed here; the product's own tests compare it.")
            : problems.Count == 0 ? new Outcome("matches-generator", "passed", "Equals a fresh generation.")
            : new Outcome("matches-generator", "failed", string.Join("; ", problems) + ".");
    }

    private static Outcome LinksResolve(string text, string documentPath, Func<string, bool> exists)
    {
        string directory = documentPath.Contains('/', StringComparison.Ordinal) ? documentPath[..documentPath.LastIndexOf('/')] : string.Empty;
        var broken = new List<string>();
        foreach (Match link in LinkRegex().Matches(text))
        {
            string target = link.Groups["target"].Value.Split('#')[0].Split('?')[0];
            if (target.Length == 0 || target.Contains("://", StringComparison.Ordinal) || target.StartsWith("mailto:", StringComparison.Ordinal) || target.StartsWith('/'))
            {
                continue;
            }

            // Resolved within the repository, by its segments: a link that climbs out of it never resolves.
            var segments = new List<string>(directory.Length == 0 ? [] : directory.Split('/'));
            bool outside = false;
            foreach (string segment in Uri.UnescapeDataString(target).Split('/'))
            {
                if (segment is "" or ".")
                {
                    continue;
                }

                if (segment == "..")
                {
                    outside |= segments.Count == 0;
                    if (segments.Count > 0)
                    {
                        segments.RemoveAt(segments.Count - 1);
                    }

                    continue;
                }

                segments.Add(segment);
            }

            if (outside || segments.Count == 0 || !exists(string.Join('/', segments)))
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
