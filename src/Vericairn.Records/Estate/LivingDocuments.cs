// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Vericairn.Records.Hashing;
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

    // Retain the comparison used to derive due state so release admission never applies a second byte test.
    internal sealed record ContentComparison(bool Available, Reason? Reason, string Summary, bool Unregistered = false);

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

        /// <summary>The repository commit selected for this comparison; null when no commit is available or needed.</summary>
        public string? ComparisonCommit { get; init; }

        internal ContentComparison? Content { get; init; }

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
                || (GeneratedDocumentContract.RequiresFreshCheck(model, d) && (EstateModel.BodyString(d, "repository") is not { } repository
                    || releaseRepositories is null || releaseRepositories.Contains(repository))))
            .OrderBy(d => EstateModel.BodyString(d, "path"), StringComparer.Ordinal).ThenBy(d => d.Id, StringComparer.Ordinal)
            .Select(d =>
            {
                string? repository = EstateModel.BodyString(d, "repository");
                bool releaseRepository = releaseRepositories is null || (repository is not null && releaseRepositories.Contains(repository));
                return Evaluate(model, d, repository is not null && releaseRepository ? commit : null,
                    repository is not null && (releaseRepository || GeneratedDocumentContract.RequiresFreshCheck(model, d)));
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

    /// <summary>The document's own file and every path its covers triggers name, in order and once each.</summary>
    public static IReadOnlyList<string> CoveredPaths(RecordEnvelope declaration)
    {
        ArgumentNullException.ThrowIfNull(declaration);
        IEnumerable<string> paths = CoveredTriggerPaths(declaration);
        if (EstateModel.BodyString(declaration, "path") is { } path) paths = paths.Append(path);
        return paths.Distinct(StringComparer.Ordinal).OrderBy(p => p, StringComparer.Ordinal).ToList();
    }

    // The content pin already covers the document itself. Keep the separate covered pin's existing meaning so reviews
    // written before implicit own-file coverage remain comparable, including sources with their own identity algorithm.
    internal static IReadOnlyList<string> CoveredTriggerPaths(RecordEnvelope declaration) =>
        Triggers(declaration).Where(t => t.On == "covers").SelectMany(t => t.Values).Distinct(StringComparer.Ordinal).OrderBy(p => p, StringComparer.Ordinal).ToList();

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

    private static State Evaluate(EstateModel model, RecordEnvelope declaration, string? releaseCommit = null, bool exactCommit = false)
    {
        if (GeneratedDocumentContract.IsStrict(model, declaration))
        {
            GeneratedDocumentPlan plan = GeneratedDocumentVerification.Plan(model, declaration, releaseCommit);
            return new State(declaration, plan.Current ? null : "refuse",
                plan.Problems.Select(p => new Reason("generation", "refuse", p, null)).ToArray(), null,
                plan.Status == DocumentGenerationStatus.Unavailable ? plan.Problems : [], []) { Generation = plan, ComparisonCommit = releaseCommit };
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

        IDocumentSource? source = elsewhere ? DocumentSources.Find(EstateModel.BodyString(declaration, "source")) : null;
        string? at = elsewhere ? exactCommit ? releaseCommit : source?.Head(model, declaration) : null;
        if (!exactCommit && at is not null && clearance is { Type: "document-review" } review &&
            review.Body.TryGetProperty("content", out JsonElement content) && content.ValueKind == JsonValueKind.Object &&
            content.TryGetProperty("commit", out JsonElement reviewed) && reviewed.ValueKind == JsonValueKind.String && reviewed.GetString() is { } reviewedCommit &&
            reviewedCommit != at && source!.IsAncestor(model, declaration, at, reviewedCommit))
            at = reviewedCommit;

        ContentComparison? comparison = CompareContent(model, declaration, clearance, source, at);

        // A review records one identity for all the covered paths, so every covers trigger compares the identity of them all. The
        // estate's own files are read from its working tree; another repository's at its selected commit when given, otherwise HEAD.
        IReadOnlyList<string> coveredPaths = CoveredTriggerPaths(declaration);
        RecordEnvelope? coveredReview = reviews.LastOrDefault(r => EstateModel.BodyString(r, "covered") is not null);
        string? identity = null;
        if (coveredPaths.Count > 0 && !elsewhere)
        {
            identity = CoveredIdentity(model.Root, coveredPaths);
        }
        else if (coveredPaths.Count > 0 && comparison?.Available == true && at is not null)
        {
            identity = source!.Covered(model, declaration, coveredPaths, at);
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

        // Keep declared triggers in their established order, including the answers recorded by a review.
        if (comparison?.Reason is { } reason) reasons.Add(reason);
        string? due = reasons.Count == 0 ? null : reasons.Select(r => r.Level).OrderBy(l => LivingDocumentType.Levels.IndexOf(l)).Last();
        return new State(declaration, due, reasons, clearance, unavailable.ToList(), notes) { ComparisonCommit = at, Content = comparison };
    }

    private static ContentComparison? CompareContent(EstateModel model, RecordEnvelope declaration, RecordEnvelope? clearance, IDocumentSource? source, string? commit)
    {
        string path = EstateModel.BodyString(declaration, "path") ?? string.Empty;
        string? repository = EstateModel.BodyString(declaration, "repository");
        // Estate revisions also clear the own-file pin. A declaration alone is not a content clearance.
        if (repository is null && clearance is null) return null;
        string level = Triggers(declaration).Select(t => t.Level).OrderBy(l => LivingDocumentType.Levels.IndexOf(l)).LastOrDefault() ?? "remind";
        JsonElement pin = clearance is { Type: "document-review" } && clearance.Body.TryGetProperty("content", out JsonElement content)
            ? content : clearance?.Body ?? default;
        string? clearedHash = pin.ValueKind == JsonValueKind.Object && pin.TryGetProperty("hash", out JsonElement hash) && hash.ValueKind == JsonValueKind.String ? hash.GetString() : null;
        string clearedAt = pin.ValueKind == JsonValueKind.Object && pin.TryGetProperty("commit", out JsonElement at) && at.ValueKind == JsonValueKind.String ? at.GetString() ?? "?" : "?";
        bool available = repository is null || (commit is not null && source?.Resolve(model, declaration, commit) == commit);
        ContentComparison Due(string summary) => new(available, new Reason("covers", level, summary, null), summary);
        if (clearedHash is null) return Due($"{path}: no review has cleared its content");
        if (!available)
        {
            RecordEnvelope? resource = model.ById.GetValueOrDefault(repository!);
            string name = resource is null ? repository! : EstateModel.BodyString(resource, "name") ?? repository!;
            bool registered = resource is not null && model.Manifest is not null &&
                model.Chain(model.Latest(resource)).Any(id => RepositoryClones.Find(model.Manifest.Estate, id) is not null);
            bool unregistered = !registered && source?.Head(model, declaration) is null;
            return Due(!unregistered
                ? $"{path}: the registered clone of {name} ({repository}) {(commit is null ? "has no readable HEAD commit" : $"does not contain repository commit {commit}")}; fetch that repository's clone, then retry"
                : $"{path}: no clone of {name} ({repository}) is registered on this machine, so its content comparison cannot run; register one with `vericairn sources clone <path> --resource {repository}`") with { Unregistered = unregistered };
        }

        string file = System.IO.Path.Combine(model.Root, path.Replace('/', System.IO.Path.DirectorySeparatorChar));
        byte[]? bytes = repository is null ? File.Exists(file) ? File.ReadAllBytes(file) : null : source!.Read(model, declaration, commit!);
        string? currentHash = bytes is null ? null : ContentHash.Sha256(bytes);
        string where = commit is null ? "in the estate's working tree" : $"at {Short(commit)}";
        if (currentHash == clearedHash)
            return new(true, null, $"{path} {where} is the content its {(clearance!.Type == "document-review" ? "review" : "revision")} {clearance.Id} cleared");
        if (currentHash is null) return Due($"{path} is not {(repository is null ? "in the estate's working tree" : $"in its repository {where}")}");
        string cleared = clearance!.Type == "document-review" ? $"review{(commit is null ? " " + clearance.Id : " at " + Short(clearedAt))}" : $"revision {clearance.Id}";
        return Due($"{path} changed after its {cleared}: {where} it is {currentHash}, and the {(clearance.Type == "document-review" ? "review" : "revision")} cleared {clearedHash}");
    }

    private static string Short(string commit) => commit[..Math.Min(12, commit.Length)];

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
