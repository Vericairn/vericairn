// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Vericairn.Records.Model;
using Vericairn.Records.Types;

namespace Vericairn.Records.Estate;

/// <summary>
/// Which living documents are due and why, derived from the records and, for the covered paths of the estate's own files, from its
/// working tree with uncommitted changes (the estate's design/living-documents.md §§ 5 and 6). Every trigger but covers reads
/// records only, so every machine derives the same due documents from the same records.
/// </summary>
public static class LivingDocuments
{
    /// <summary>The foundation's own contributions that are named namespace/name: its generators.</summary>
    public static readonly ImmutableArray<string> FoundationGenerators = ["vericairn.mcp/orientation", "vericairn.docs/notes", "vericairn.records/releases", "vericairn.records/progress"];

    private static readonly HashSet<string> Contributions = new(FoundationGenerators, StringComparer.Ordinal);

    /// <summary>Makes a pack's source, trigger, check or generator available, so that declarations naming it are evaluated.</summary>
    public static void Register(string contribution)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(contribution);
        lock (Contributions)
        {
            Contributions.Add(contribution);
        }
    }

    /// <summary>True for the foundation's built-ins and for contributions a pack registered.</summary>
    public static bool IsAvailable(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        if (!Patterns.Contribution.IsMatch(name))
        {
            return true;
        }

        lock (Contributions)
        {
            return Contributions.Contains(name);
        }
    }

    /// <summary>One reason a document is due: the trigger, its level, what fired it, and the record that did.</summary>
    /// <param name="On">The trigger.</param>
    /// <param name="Level">remind, warn or refuse.</param>
    /// <param name="Summary">What fired it, for a person.</param>
    /// <param name="Record">The record that fired it; null for covered paths.</param>
    public sealed record Reason(string On, string Level, string Summary, string? Record);

    /// <summary>A declared document and its derived state.</summary>
    /// <param name="Declaration">The current declaration.</param>
    /// <param name="Level">The highest level among the reasons; null when the document is current.</param>
    /// <param name="Reasons">Every trigger newer than the latest clearance.</param>
    /// <param name="Clearance">The latest clearance: a revision of the estate file or a clearing review; null when there is none.</param>
    /// <param name="Unavailable">The pack contributions it names that are not installed here.</param>
    /// <param name="Notes">What could not be evaluated here, and why.</param>
    public sealed record State(RecordEnvelope Declaration, string? Level, IReadOnlyList<Reason> Reasons, RecordEnvelope? Clearance, IReadOnlyList<string> Unavailable, IReadOnlyList<string> Notes)
    {
        /// <summary>The document's repository-relative path.</summary>
        public string Path => DocumentPatterns.Destination(Declaration) ?? "?";

        /// <summary>The resource record of the repository that holds it; null for the estate's own repository.</summary>
        public string? Repository => EstateModel.BodyString(Declaration, "repository");

        /// <summary>The repository commit selected for this release comparison; null when no commit is available or needed.</summary>
        public string? ComparisonCommit { get; init; }

        /// <summary>The fresh strict generation comparison, when the declaration has adopted that contract.</summary>
        public GeneratedDocumentPlan? Generation { get; init; }

        /// <summary>True when a trigger is newer than the latest clearance.</summary>
        public bool Due => Level is not null;

        /// <summary>True when a trigger at level refuse is declared, so an unavailable contribution closes the gates too.</summary>
        public bool DeclaresRefuse => Generation is not null || Triggers(Declaration).Any(t => t.Level == "refuse");

        /// <summary>Whether it closes the gates: due at level refuse, or unavailable while it declares refuse.</summary>
        public bool Closes => Level == "refuse" || (Unavailable.Count > 0 && DeclaresRefuse);

        /// <summary>The document, its state and its reasons, in one line for a person.</summary>
        public string Describe()
        {
            var parts = new List<string>();
            if (Unavailable.Count > 0)
            {
                parts.Add($"unavailable, since it names {string.Join(", ", Unavailable)}, which no installed pack provides");
            }

            if (Due)
            {
                parts.Add($"due at {Level}: {string.Join("; ", Reasons.Select(r => r.Summary))}");
            }

            return $"{Path}: {(parts.Count == 0 ? "current" : string.Join("; and ", parts))}";
        }
    }

    /// <summary>Every current declaration with its state, in path order.</summary>
    public static IReadOnlyList<State> Derive(EstateModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        return model.Current("living-document")
            .OrderBy(d => EstateModel.BodyString(d, "path"), StringComparer.Ordinal).ThenBy(d => d.Id, StringComparer.Ordinal)
            .Select(d => Evaluate(model, d)).ToList();
    }

    /// <summary>
    /// The declarations of a product at commits of their own repositories. Repositories in <paramref name="releaseRepositories"/>
    /// use the exact release commit; others use their own HEAD, or the reviewed commit when HEAD is its ancestor.
    /// Omitting the set applies the release commit to all repositories.
    /// The selected commit is retained for the content comparison; estate files and record triggers retain their usual rules.
    /// </summary>
    public static IReadOnlyList<State> AtReleaseCommit(EstateModel model, string product, string? commit, IReadOnlySet<string>? releaseRepositories = null)
    {
        ArgumentNullException.ThrowIfNull(model);
        if (releaseRepositories is null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(commit);
        }

        return model.Current("living-document").Where(d => NamesProduct(model, d, product)
                || (GeneratedDocumentContract.IsStrict(model, d) && (EstateModel.BodyString(d, "repository") is not { } repository
                    || releaseRepositories is null || releaseRepositories.Contains(repository))))
            .OrderBy(d => EstateModel.BodyString(d, "path"), StringComparer.Ordinal).ThenBy(d => d.Id, StringComparer.Ordinal)
            .Select(d =>
            {
                string? repository = EstateModel.BodyString(d, "repository");
                IDocumentSource? source = DocumentSources.Find(EstateModel.BodyString(d, "source"));
                bool releaseRepository = releaseRepositories is null || (repository is not null && releaseRepositories.Contains(repository));
                string? at = repository is null ? null
                    : releaseRepository ? commit : GeneratedDocumentContract.IsStrict(model, d) ? null : source?.Head(model, d);
                State state = Evaluate(model, d, at);
                if (!releaseRepository && at is not null && state.Clearance is { Type: "document-review" } review &&
                    review.Body.TryGetProperty("content", out JsonElement content) && content.ValueKind == JsonValueKind.Object &&
                    content.TryGetProperty("commit", out JsonElement reviewed) && reviewed.ValueKind == JsonValueKind.String &&
                    reviewed.GetString() is string reviewedCommit && reviewedCommit != at && source!.IsAncestor(model, d, at, reviewedCommit))
                {
                    at = reviewedCommit;
                    state = Evaluate(model, d, at);
                }

                return state with { ComparisonCommit = at };
            }).ToList();
    }

    /// <summary>
    /// The documents of a product that close its release gate: those whose release trigger names it, by any revision of the product
    /// record, and that are due at level refuse or unavailable while they declare refuse.
    /// </summary>
    public static IReadOnlyList<State> Gate(EstateModel model, string product)
    {
        ArgumentNullException.ThrowIfNull(model);
        return Derive(model).Where(s => NamesProduct(model, s.Declaration, product) && s.Closes).ToList();
    }

    /// <summary>True when one of the declaration's release triggers names the product, by any revision of the product record.</summary>
    public static bool NamesProduct(EstateModel model, RecordEnvelope declaration, string product)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(declaration);
        ArgumentNullException.ThrowIfNull(product);
        string identity = ProductIdentity(model, product);
        return Triggers(declaration).Any(t => t.On == "release" && t.Product is not null && ProductIdentity(model, t.Product) == identity);
    }

    /// <summary>
    /// A product's identity across the revisions of its record: the first record of the chain the named revision belongs to, or the
    /// id itself when the estate holds no such record. A trigger, a release or a request may name any revision of the product, so
    /// revising the product record neither silences a trigger nor needs every declaration revised with it.
    /// </summary>
    public static string ProductIdentity(EstateModel model, string product)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(product);
        return model.ById.TryGetValue(product, out RecordEnvelope? record) ? model.Chain(record)[0] : product;
    }

    /// <summary>
    /// The content identity of covered paths in a working tree: sha256 over each file's path and content hash, in path order, with
    /// a missing path recorded as absent. The estate's version-control and working directories are never part of it.
    /// </summary>
    public static string CoveredIdentity(string root, IEnumerable<string> paths)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        ArgumentNullException.ThrowIfNull(paths);
        var lines = new SortedSet<string>(StringComparer.Ordinal);
        foreach (string covered in paths)
        {
            string full = System.IO.Path.Combine(root, covered.Replace('/', System.IO.Path.DirectorySeparatorChar));
            if (File.Exists(full))
            {
                lines.Add(Line(root, full));
            }
            else if (Directory.Exists(full))
            {
                foreach (string file in Directory.EnumerateFiles(full, "*", SearchOption.AllDirectories).Where(f => !Hidden(root, f)))
                {
                    lines.Add(Line(root, file));
                }
            }
            else
            {
                lines.Add(covered + "\tabsent");
            }
        }

        return "sha256:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Concat(lines.Select(l => l + "\n"))))).ToLowerInvariant();
    }

    /// <summary>Every path a declaration's covers triggers name, in order and once each.</summary>
    public static IReadOnlyList<string> CoveredPaths(RecordEnvelope declaration)
    {
        ArgumentNullException.ThrowIfNull(declaration);
        return Triggers(declaration).Where(t => t.On == "covers").SelectMany(t => t.Values).Distinct(StringComparer.Ordinal).OrderBy(p => p, StringComparer.Ordinal).ToList();
    }

    /// <summary>A declaration's triggers with their parameters.</summary>
    public static IReadOnlyList<(string On, string Level, string? Product, IReadOnlyList<string> Values)> Triggers(RecordEnvelope declaration)
    {
        ArgumentNullException.ThrowIfNull(declaration);
        if (!declaration.Body.TryGetProperty("triggers", out JsonElement triggers) || triggers.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return triggers.EnumerateArray().Select(t =>
        {
            string on = t.TryGetProperty("on", out JsonElement o) ? o.GetString() ?? string.Empty : string.Empty;
            string level = t.TryGetProperty("level", out JsonElement l) ? l.GetString() ?? "remind" : "remind";
            string? product = t.TryGetProperty("product", out JsonElement p) ? p.GetString() : null;
            string values = on == "covers" ? "paths" : "keys";
            IReadOnlyList<string> list = t.TryGetProperty(values, out JsonElement v) && v.ValueKind == JsonValueKind.Array ? v.EnumerateArray().Select(e => e.GetString() ?? string.Empty).ToList() : [];
            return (on, level, product, list);
        }).ToList();
    }

    private static State Evaluate(EstateModel model, RecordEnvelope declaration, string? releaseCommit = null)
    {
        if (GeneratedDocumentContract.IsStrict(model, declaration))
        {
            GeneratedDocumentPlan plan = GeneratedDocumentVerification.Plan(model, declaration, releaseCommit);
            return new State(declaration, plan.Current ? null : "refuse",
                plan.Problems.Select(p => new Reason("generation", "refuse", p, null)).ToArray(), null,
                plan.Status == DocumentGenerationStatus.Unavailable ? plan.Problems : [], []) { Generation = plan };
        }

        IReadOnlyList<string> chain = model.Chain(declaration);
        string path = EstateModel.BodyString(declaration, "path") ?? string.Empty;
        bool elsewhere = EstateModel.BodyString(declaration, "repository") is not null;
        List<RecordEnvelope> reviews = model.OfType("document-review")
            .Where(r => r.Subject is not null && chain.Contains(r.Subject.Id) && EstateModel.BodyString(r, "outcome") is "current" or "revised")
            .OrderBy(r => r.At, StringComparer.Ordinal).ThenBy(r => r.Id, StringComparer.Ordinal).ToList();
        RecordEnvelope? revision = elsewhere ? null : model.OfType("document-revision").Where(r => EstateModel.BodyString(r, "path") == path)
            .OrderBy(r => r.At, StringComparer.Ordinal).ThenBy(r => r.Id, StringComparer.Ordinal).LastOrDefault();
        RecordEnvelope? clearance = Newer(reviews.LastOrDefault(), revision);

        var unavailable = new SortedSet<string>(StringComparer.Ordinal);
        foreach (string name in Named(declaration).Where(n => !IsAvailable(n)))
        {
            unavailable.Add(name);
        }

        var reasons = new List<Reason>();
        var notes = new List<string>();

        // A review records one identity for all the covered paths, so every covers trigger compares the identity of them all. The
        // estate's own files are read from its working tree; another repository's at its selected commit when given, otherwise HEAD.
        IReadOnlyList<string> coveredPaths = CoveredPaths(declaration);
        RecordEnvelope? coveredReview = reviews.LastOrDefault(r => EstateModel.BodyString(r, "covered") is not null);
        string? at = null;
        string? identity = null;
        if (coveredPaths.Count > 0 && !elsewhere)
        {
            identity = CoveredIdentity(model.Root, coveredPaths);
        }
        else if (coveredPaths.Count > 0 && DocumentSources.Find(EstateModel.BodyString(declaration, "source")) is IDocumentSource source &&
                 (releaseCommit is null ? source.Head(model, declaration) : source.Resolve(model, declaration, releaseCommit)) is string head)
        {
            at = head;
            identity = source.Covered(model, declaration, coveredPaths, head);
        }

        bool coveredChanged = identity is not null && (coveredReview is null || EstateModel.BodyString(coveredReview, "covered") != identity);
        foreach ((string on, string level, string? product, IReadOnlyList<string> values) in Triggers(declaration))
        {
            if (on == "covers")
            {
                if (identity is null)
                {
                    notes.Add("its covered paths are compared where its repository is: no clone of it is registered here (vericairn sources clone), so they are compared at the commit reviewed or released");
                }
                else if (coveredChanged)
                {
                    string where = at is null ? string.Empty : $"at {at[..Math.Min(12, at.Length)]}, ";
                    reasons.Add(new Reason(on, level, coveredReview is null ? $"{where}no review has recorded its covered paths ({string.Join(", ", values)})" : $"{where}its covered paths ({string.Join(", ", values)}) changed after the review {coveredReview.Id}", null));
                }

                continue;
            }

            // A release of any revision of the product fires a trigger that names any revision of it.
            string? named = product is null ? null : ProductIdentity(model, product);
            RecordEnvelope? fired = on switch
            {
                "release" => Newest(model.OfType("release").Where(r => named is not null && r.Subject is not null && ProductIdentity(model, r.Subject.Id) == named && EstateModel.BodyString(r, "status") == "published")),
                "upgrade" => Newest(model.OfType("migration")),
                "decision" => Newest(model.Current("decision").Where(d => Documents(d).Any(name => name == path || chain.Contains(name)))),
                "accepted" => Newest(model.OfType("acceptance").Where(a => a.Subject?.Type == "work-order" && model.ById.TryGetValue(a.Subject.Id, out RecordEnvelope? order) && values.Contains(EstateModel.BodyString(order, "key") ?? string.Empty))),
                _ => null,
            };
            if (fired is not null && (clearance is null || Later(fired, clearance)))
            {
                reasons.Add(new Reason(on, level, Fired(on, fired), fired.Id));
            }
        }

        string? due = reasons.Count == 0 ? null : reasons.Select(r => r.Level).OrderBy(l => LivingDocumentType.Levels.IndexOf(l)).Last();
        return new State(declaration, due, reasons, clearance, unavailable.ToList(), notes);
    }

    private static string Fired(string on, RecordEnvelope record) => on switch
    {
        "release" => $"release {EstateModel.BodyString(record, "version")} was published ({record.Id}) after its latest clearance",
        "upgrade" => $"the estate was upgraded ({record.Id}) after its latest clearance",
        "decision" => $"decision {record.Id}, \"{EstateModel.BodyString(record, "title")}\", names it and is newer than its latest clearance",
        _ => $"work order acceptance {record.Id} is newer than its latest clearance",
    };

    /// <summary>The contributions a declaration names: its source, triggers, checks and generators.</summary>
    private static IEnumerable<string> Named(RecordEnvelope declaration)
    {
        if (EstateModel.BodyString(declaration, "source") is string source && source != LivingDocumentType.EstateFile)
        {
            yield return source;
        }

        foreach ((string on, _, _, _) in Triggers(declaration))
        {
            yield return on;
        }

        JsonElement body = declaration.Body;
        if (body.TryGetProperty("checks", out JsonElement checks) && checks.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement check in checks.EnumerateArray())
            {
                yield return check.GetString() ?? string.Empty;
            }
        }

        if (body.TryGetProperty("generator", out JsonElement generator) && generator.TryGetProperty("id", out JsonElement id))
        {
            yield return id.GetString() ?? string.Empty;
        }

        if (body.TryGetProperty("regions", out JsonElement regions) && regions.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement region in regions.EnumerateArray())
            {
                yield return region.TryGetProperty("generator", out JsonElement g) ? g.GetString() ?? string.Empty : string.Empty;
            }
        }
    }

    private static IEnumerable<string> Documents(RecordEnvelope decision) =>
        decision.Body.TryGetProperty("documents", out JsonElement documents) && documents.ValueKind == JsonValueKind.Array
            ? documents.EnumerateArray().Select(d => d.GetString() ?? string.Empty)
            : [];

    private static RecordEnvelope? Newest(IEnumerable<RecordEnvelope> records) =>
        records.OrderBy(r => r.At, StringComparer.Ordinal).ThenBy(r => r.Id, StringComparer.Ordinal).LastOrDefault();

    private static RecordEnvelope? Newer(RecordEnvelope? a, RecordEnvelope? b) => a is null ? b : b is null ? a : Later(a, b) ? a : b;

    /// <summary>Record order: by authoring time, and by identifier within the same second.</summary>
    private static bool Later(RecordEnvelope a, RecordEnvelope b)
    {
        int byTime = string.CompareOrdinal(a.At, b.At);
        return byTime > 0 || (byTime == 0 && string.CompareOrdinal(a.Id, b.Id) > 0);
    }

    private static string Line(string root, string file) =>
        EstateLayout.Relative(root, file) + "\tsha256:" + Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(file))).ToLowerInvariant();

    private static bool Hidden(string root, string file) =>
        EstateLayout.Relative(root, file).Split('/').Any(segment => segment is ".git" or ".vericairn");
}
