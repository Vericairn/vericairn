// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Vericairn.Runtime.Guides;

/// <summary>Dependency changes between two exact private source manifests.</summary>
public sealed record GuideInputComparison(IReadOnlyList<string> Due, IReadOnlyList<GuideFinding> Findings);

internal sealed record GuideInputIdentity(string Key, string InputIdentity);
internal sealed record GuideSourceManifest(string Format, bool Complete, string GeneratorVersion, string TemplateVersion,
    string IndexIdentity, GuideSourcePin[] Sources, GuideBlockPin[] Blocks, GuideInputIdentity[] Guides);

/// <summary>Pure, bounded generation from explicit bytes. It reads no filesystem, clock, account, environment or network.</summary>
public static partial class GuideGenerator
{
    /// <summary>The implementation identity pinned in every output.</summary>
    public const string GeneratorVersion = "vericairn.guides/1";
    /// <summary>The built-in reviewed-template convention; selecting another version refuses.</summary>
    public const string TemplateVersion = "vericairn.extension-ecosystem/build-guide/1";
    /// <summary>Maximum bytes in one input file.</summary>
    public const int MaximumSourceBytes = 4 * 1024 * 1024;
    /// <summary>Maximum combined input bytes.</summary>
    public const int MaximumInputBytes = 32 * 1024 * 1024;
    /// <summary>Maximum combined rendered guide bytes, excluding the bounded inventory manifests.</summary>
    public const int MaximumOutputBytes = 128 * 1024 * 1024;

    /// <summary>Computes a portable SHA-256 identity.</summary>
    public static string Hash(ReadOnlySpan<byte> bytes) => "sha256:" + Convert.ToHexStringLower(SHA256.HashData(bytes));

    internal static byte[] Json<T>(T value) => GuideMarkdown.Utf8.GetBytes(JsonSerializer.Serialize(value, GuideJson.Options) + "\n");

    /// <summary>Generates all planned rows, or an explicit set of keys/aliases, with complete owning-row coverage.</summary>
    public static GuideBundle Generate(GuideSourceIndex index, IReadOnlyDictionary<string, byte[]> sources,
        IReadOnlyCollection<string>? selection = null)
    {
        ArgumentNullException.ThrowIfNull(index);
        ArgumentNullException.ThrowIfNull(sources);
        var findings = new List<GuideFinding>();
        var coverage = new List<GuideCoverage>();
        var pages = new List<GuidePage>();
        long outputBytes = 0;
        void Fail(string entry, string code, string message) => findings.Add(new(entry, code, message));
        if (!ValidShape(index))
        {
            Fail("inventory", "invalid-index", "Index members must be non-null and bounded; every named item needs its required fields.");
            return Finish(new GuideSourceIndex(), pages, coverage, findings);
        }
        if (index.Format != "vericairn.guide-inputs/1" || index.TemplateVersion != TemplateVersion)
            Fail("inventory", "unsupported-template", "The input format and explicit template version must match this generator.");

        Duplicate(index.Sources.Select(s => s.Id), "duplicate-source");
        Duplicate(index.Blocks.Select(b => b.Id), "duplicate-block");
        Duplicate(index.Entries.Select(e => e.Key), "duplicate-key");
        Duplicate(index.Roles.Select(r => r.Kind), "duplicate-role");
        Duplicate(index.Families.Select(f => f.Name), "duplicate-family");
        Duplicate(index.Tables.Select(t => t.Source + ":" + t.Heading), "duplicate-table");
        foreach (var duplicate in index.Sources.GroupBy(s => s.Area + ":" + s.Path, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1))
            Fail(duplicate.First().Id, "duplicate-source-path", "A source path has more than one identity or spelling.");
        var sourceText = new Dictionary<string, string>(StringComparer.Ordinal);
        long total = 0;
        foreach (GuideSourcePin source in index.Sources.OrderBy(s => s.Id, StringComparer.Ordinal))
        {
            if (!SafeKey(source.Id) || source.Area is not ("catalog" or "contracts") || !SafePath(source.Path))
            { Fail(source.Id, "invalid-source", "Source ids, areas and relative paths must be portable and explicit."); continue; }
            if (!sources.TryGetValue(source.Id, out byte[]? bytes) || bytes is null)
            { Fail(source.Id, "missing-source", "An allowlisted source is unavailable."); continue; }
            total += bytes.Length;
            if (bytes.Length > MaximumSourceBytes || total > MaximumInputBytes)
            { Fail(source.Id, "input-limit", "The input exceeds the declared byte bound."); continue; }
            if (source.Sha256 != Hash(bytes))
            { Fail(source.Id, "source-hash-mismatch", "The source differs from its reviewed exact pin."); continue; }
            try
            {
                string value = GuideMarkdown.Utf8.GetString(bytes);
                if (UnsafeMarkup(value)) Fail(source.Id, "malicious-markup", "Active markup, embeds, unsafe link schemes or traversal are not admitted.");
                sourceText.TryAdd(source.Id, value);
            }
            catch (DecoderFallbackException) { Fail(source.Id, "invalid-utf8", "The source must be valid UTF-8."); }
        }
        var blocks = new Dictionary<string, GuideQuote>(StringComparer.Ordinal);
        foreach (GuideBlockPin pin in index.Blocks.OrderBy(b => b.Id, StringComparer.Ordinal))
        {
            if (!SafeKey(pin.Id)) { Fail(pin.Id, "invalid-block", "Block ids must be portable keys."); continue; }
            if (!sourceText.TryGetValue(pin.Source, out string? text))
            { Fail(pin.Id, "missing-source", "The block's exact source is unavailable."); continue; }
            string[] lines = GuideMarkdown.Lines(text);
            if (pin.StartLine < 1 || pin.EndLine < pin.StartLine || pin.EndLine > lines.Length)
            { Fail(pin.Id, "invalid-span", "The inclusive source span is out of range."); continue; }
            string payload = string.Concat(lines[(pin.StartLine - 1)..pin.EndLine]);
            if (string.IsNullOrWhiteSpace(payload) || pin.Sha256 != Hash(GuideMarkdown.Utf8.GetBytes(payload)))
            { Fail(pin.Id, "block-hash-mismatch", "A required block is empty or differs from its exact pin."); continue; }
            blocks.TryAdd(pin.Id, new(pin.Id, pin.Source, pin.Sha256, payload));
        }

        var rows = new List<GuideCatalogRow>();
        foreach (GuideSourcePin source in index.Sources.Where(s => s.Area == "catalog").OrderBy(s => s.Id, StringComparer.Ordinal))
        {
            if (!sourceText.ContainsKey(source.Id)) continue;
            GuideInventory inventory = GuideMarkdown.Read(source.Id, sources[source.Id]);
            rows.AddRange(inventory.Rows);
            findings.AddRange(inventory.Findings);
            // Catalog family links are inventory dependencies, never an invitation to scan the estate.
            foreach (Match link in CatalogLink().Matches(sourceText[source.Id]))
            {
                string path = source.Path.Contains('/') ? source.Path[..(source.Path.LastIndexOf('/') + 1)] + link.Groups[1].Value : link.Groups[1].Value;
                if (!index.Sources.Any(s => s.Area == "catalog" && s.Path == path))
                    Fail(source.Id, "missing-family-source", "A linked catalog family is not in the explicit source allowlist: " + link.Groups[1].Value);
            }
        }
        if (rows.Count == 0) Fail("inventory", "empty-inventory", "No owning catalog rows were found.");
        if (rows.Count > 20000) { Fail("inventory", "inventory-limit", "The inventory exceeds the row bound."); return Finish(index, pages, coverage, findings); }

        var entries = index.Entries.GroupBy(e => e.Key, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        var aliases = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (GuideEntryBinding entry in index.Entries)
        {
            if (!SafeKey(entry.Key)) Fail(entry.Key, "invalid-key", "Entry keys must be portable identifiers, not filenames or row numbers.");
            if (entry.State is not ("planned" or "remainder" or "available" or "retired" or "reference" or "withheld"))
                Fail(entry.Key, "unknown-state", "Readiness must be explicitly mapped to a supported state.");
            foreach (string alias in entry.Aliases)
                if (string.IsNullOrWhiteSpace(alias) || entries.ContainsKey(alias) || !aliases.TryAdd(alias, entry.Key))
                    Fail(entry.Key, "duplicate-alias", "An alias is empty or has more than one owner.");
        }
        string? Resolve(string name) => entries.ContainsKey(name) ? name : aliases.GetValueOrDefault(name);
        var selected = new HashSet<string>(StringComparer.Ordinal);
        if (selection is { Count: > 20000 } || selection?.Any(k => string.IsNullOrWhiteSpace(k)) == true)
            Fail("inventory", "invalid-selection", "Selections must be nonempty keys or aliases within the inventory bound.");
        else if (selection is null || selection.Count == 0)
            selected.UnionWith(index.Entries.Where(e => e.State is "planned" or "remainder").Select(e => e.Key));
        else
            foreach (string key in selection)
            {
                string? resolved = Resolve(key);
                if (resolved is null) Fail(key, "unresolved-selection", "The selected key or alias has no owner.");
                else if (entries[resolved].State is not ("planned" or "remainder")) Fail(key, "not-planned", "Only planned scope can be selected for generation.");
                else selected.Add(resolved);
            }

        foreach (GuideTableBinding table in index.Tables)
        {
            if (!rows.Any(r => r.Source == table.Source && r.Heading == table.Heading))
                Fail(table.Source, "missing-table", "A mapped owning table is missing: " + table.Heading);
        }
        var matched = new HashSet<string>(StringComparer.Ordinal);
        foreach (GuideCatalogRow row in rows.OrderBy(r => r.Source, StringComparer.Ordinal).ThenBy(r => r.Heading, StringComparer.Ordinal).ThenBy(r => r.Name, StringComparer.Ordinal))
        {
            GuideEntryBinding[] matches = index.Entries.Where(e => e.Source == row.Source && e.Heading == row.Heading && e.Name == row.Name).ToArray();
            if (matches.Length != 1)
            {
                Fail(row.Name, "row-mapping", "Each owning row needs exactly one reviewed identity mapping.");
                coverage.Add(new("", row.Source, row.Heading, row.Name, "blocked", "Missing or ambiguous identity mapping."));
                continue;
            }
            GuideEntryBinding entry = matches[0];
            int before = findings.Count;
            if (!matched.Add(entry.Key)) Fail(entry.Key, "duplicate-key", "More than one owning row resolves to this key.");
            GuideTableBinding[] tableMatches = index.Tables.Where(t => t.Source == row.Source && t.Heading == row.Heading).ToArray();
            if (tableMatches.Length != 1)
            {
                Fail(entry.Key, "table-mapping", "Each owning table needs exactly one kind and family mapping.");
                coverage.Add(new(entry.Key, row.Source, row.Heading, row.Name, "blocked", "Missing or ambiguous table context."));
                continue;
            }
            GuideTableBinding table = tableMatches[0];
            if (!GuideTemplate.IsRole(table.Kind) || !ColumnsAgree(table.Kind, row)) Fail(entry.Key, "column-contract", "The owning columns do not satisfy the mapped role.");
            if (row.NameColumn == "Identifier" && entry.Key != row.Name)
                Fail(entry.Key, "identity-changed", "An existing explicit identifier must remain the stable key.");
            if (row.NameColumn != "Identifier" && !entry.Key.StartsWith(table.Kind + ".", StringComparison.Ordinal))
                Fail(entry.Key, "unqualified-key", "A name-only row needs a reviewed kind-and-family-qualified key.");
            if (entry.Status.Length == 0) Fail(entry.Key, "missing-status", "Every row needs a pinned readiness/subset disclosure.");
            GuideQuote[] status = SelectBlocks(entry.Key, entry.Status);
            if (entry.State == "remainder" && !status.Any(q => q.Text.Contains("planned", StringComparison.OrdinalIgnoreCase)))
                Fail(entry.Key, "missing-remainder", "A planned remainder needs an explicit planned-scope disclosure.");
            var references = new SortedSet<string>(StringComparer.Ordinal);
            foreach (string reference in entry.References)
            {
                string? resolved = Resolve(reference);
                if (resolved is null) Fail(entry.Key, "unresolved-reference", "A required catalog reference or alias has no owner: " + reference);
                else references.Add(resolved);
            }
            if (entry.State == "reference")
            {
                string? owner = entry.Owner is null ? null : Resolve(entry.Owner);
                if (owner is null || owner == entry.Key || entries[owner].State == "reference")
                    Fail(entry.Key, "unresolved-reference", "A reused row must name a distinct owning entry directly.");
                else references.Add(owner);
            }
            else if (entry.Owner is not null) Fail(entry.Key, "unexpected-owner", "Only reference rows can redirect to an owner.");
            if (entry.State is "withheld" or "retired" && string.IsNullOrWhiteSpace(entry.Explanation))
                Fail(entry.Key, "missing-disposition", "Withheld and retired entries need their reason or replacement and migration explanation.");

            var required = new List<string>(table.Boundaries);
            if (table.Boundaries.Length == 0) Fail(entry.Key, "missing-boundary", "A kind/group preamble is mandatory.");
            if (table.Family is not null)
            {
                GuideFamilyBinding[] families = index.Families.Where(f => f.Name == table.Family).ToArray();
                if (families.Length != 1 || families[0].Boundaries.Length == 0)
                    Fail(entry.Key, "missing-boundary", "The industry family's complete common boundaries are mandatory.");
                else required.AddRange(families[0].Boundaries);
            }
            GuideRoleBinding[] roles = index.Roles.Where(r => r.Kind == table.Kind).ToArray();
            GuideQuote[] role = [];
            if (roles.Length != 1 || roles[0].Blocks.Length == 0)
                Fail(entry.Key, "missing-role", "The role's complete pinned contract is mandatory.");
            else
            {
                foreach (string id in roles[0].Blocks)
                    if (blocks.TryGetValue(id, out GuideQuote? block) && !index.Sources.Any(s => s.Id == block.Source && s.Area == "contracts"))
                        Fail(entry.Key, "role-source", "Role contracts must use the explicit contracts source area.");
                required.AddRange(roles[0].Blocks);
                role = SelectBlocks(entry.Key, roles[0].Blocks);
            }
            required.AddRange(entry.Boundaries);
            var boundaries = new List<GuideQuote>(SelectBlocks(entry.Key, required));
            string? caveat = row.Cells.GetValueOrDefault("Caveat") ?? row.Cells.GetValueOrDefault("Boundary");
            if (string.IsNullOrWhiteSpace(caveat) && row.NameColumn != "Entry") Fail(entry.Key, "missing-boundary", "The row caveat/boundary must not be empty.");
            else if (caveat is not null) boundaries.Add(new("row-boundary", row.Source, Hash(GuideMarkdown.Utf8.GetBytes(caveat)), caveat));
            GuideQuote[] context = SelectBlocks(entry.Key, table.Context.Concat(entry.Context));
            bool blocked = before != findings.Count;
            string disposition = blocked ? "blocked" : entry.State switch
            {
                "planned" when selected.Contains(entry.Key) => "active-guide",
                "remainder" when selected.Contains(entry.Key) => "planned-remainder-guide",
                "planned" or "remainder" => "withheld",
                "available" => "fully-available-documentation",
                "retired" => "retired-guide",
                _ => entry.State,
            };
            string reason = blocked ? "See per-entry findings." : entry.State == "reference" ? "Reuses " + string.Join(", ", references) :
                disposition == "withheld" ? entry.Explanation ?? "Outside the explicit selection." : entry.Explanation ?? "";
            coverage.Add(new(entry.Key, row.Source, row.Heading, row.Name, disposition, reason));
            if (selected.Contains(entry.Key) && !blocked && outputBytes <= MaximumOutputBytes)
            {
                GuidePage page = GuideTemplate.Page(entry, table, row, status, [.. boundaries], context, role, [.. references]);
                outputBytes += Json(page).LongLength + GuideTemplate.Markdown(page).LongLength + GuideTemplate.Html(page).LongLength;
                if (outputBytes > MaximumOutputBytes)
                    Fail(entry.Key, "output-limit", "The selected guide set exceeds the output byte bound; select a smaller exact batch.");
                else pages.Add(page);
            }
        }
        foreach (GuideEntryBinding entry in index.Entries.Where(e => !matched.Contains(e.Key)))
        {
            Fail(entry.Key, "missing-row", "The mapped owning row or its exact source is unavailable.");
            coverage.Add(new(entry.Key, entry.Source, entry.Heading, entry.Name, "blocked", "Owning row unavailable."));
        }
        foreach (string key in selected.Where(k => pages.Count(p => p.Key == k) != 1))
            Fail(key, "coverage", "The selected planned entry does not have exactly one complete guide.");
        return Finish(index, pages, coverage, findings);

        void Duplicate(IEnumerable<string> values, string code)
        {
            foreach (var duplicate in values.GroupBy(v => v, StringComparer.Ordinal).Where(g => g.Count() > 1))
                Fail(duplicate.Key, code, "A declared identity is duplicated.");
        }
        GuideQuote[] SelectBlocks(string key, IEnumerable<string> names)
        {
            var result = new List<GuideQuote>();
            foreach (string name in names.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal))
                if (blocks.TryGetValue(name, out GuideQuote? block)) result.Add(block);
                else Fail(key, "missing-block", "A required source block is unresolved: " + name);
            return [.. result];
        }
    }

    private static bool ColumnsAgree(string kind, GuideCatalogRow row) => row.NameColumn switch
    {
        "Entry" => kind == "shared-capability" && row.Cells.ContainsKey("Where"),
        "Area" => kind == "shared-capability" && row.Cells.ContainsKey("Owns") && row.Cells.ContainsKey("Purpose"),
        "Capability" => kind == "shared-capability" && row.Cells.ContainsKey("Role"),
        "Foundry" => kind == "foundry" && row.Cells.ContainsKey("Initializes or adopts") && row.Cells.ContainsKey("Who needs it and why"),
        "Provider" => kind == "provider" && row.Cells.ContainsKey("Role") && row.Cells.ContainsKey("Works with"),
        "Identifier" => kind == "domain" && row.Cells.ContainsKey("Who needs it and why") ||
            kind == "adapter" && row.Cells.ContainsKey("Role") && row.Cells.ContainsKey("Consumes and produces"),
        _ => false,
    };

    private static GuideBundle Finish(GuideSourceIndex index, List<GuidePage> pages, List<GuideCoverage> coverage, List<GuideFinding> findings)
    {
        GuideFinding[] orderedFindings = findings.Distinct().OrderBy(f => f.Entry, StringComparer.Ordinal)
            .ThenBy(f => f.Code, StringComparer.Ordinal).ThenBy(f => f.Message, StringComparer.Ordinal).ToArray();
        GuideCoverage[] orderedCoverage = coverage.OrderBy(c => c.Key, StringComparer.Ordinal).ThenBy(c => c.Source, StringComparer.Ordinal)
            .ThenBy(c => c.Heading, StringComparer.Ordinal).ThenBy(c => c.Name, StringComparer.Ordinal).ToArray();
        bool complete = orderedFindings.Length == 0;
        if (!complete) orderedCoverage = orderedCoverage.Select(c => c.State is "active-guide" or "planned-remainder-guide"
            ? c with { State = "blocked", Reason = "The selected bundle failed coverage; no guides were emitted." } : c).ToArray();
        var files = new SortedDictionary<string, byte[]>(StringComparer.Ordinal)
        {
            ["source-index.json"] = Json(index),
            ["source-manifest.json"] = Json(new GuideSourceManifest("vericairn.guide-sources/1", complete, GeneratorVersion, TemplateVersion,
                Hash(Json(index)), index.Sources.OrderBy(s => s.Id, StringComparer.Ordinal).ToArray(),
                index.Blocks.OrderBy(b => b.Id, StringComparer.Ordinal).ToArray(),
                complete ? pages.OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => new GuideInputIdentity(p.Key, p.InputIdentity)).ToArray() : [])),
            ["coverage-manifest.json"] = Json(new { format = "vericairn.guide-coverage/1", publicationState = "private-preview", complete, entries = orderedCoverage, findings = orderedFindings }),
        };
        if (complete)
        {
            var md = new StringBuilder("# Community build guides — private preview\n\nGeneration grants no publication authority.\n\n");
            var html = new StringBuilder("<!doctype html>\n<html lang=\"en\"><head><meta charset=\"utf-8\"><meta http-equiv=\"Content-Security-Policy\" content=\"default-src 'none'; base-uri 'none'; form-action 'none'\"><title>Private build guide preview</title></head><body>\n<h1>Community build guides — private preview</h1>\n<p>Generation grants no publication authority.</p>\n<ul>\n");
            foreach (GuidePage page in pages.OrderBy(p => p.Key, StringComparer.Ordinal))
            {
                files.Add("guides/" + page.Key + ".json", Json(page));
                files.Add("guides/" + page.Key + ".md", GuideTemplate.Markdown(page));
                files.Add("guides/" + page.Key + ".html", GuideTemplate.Html(page));
                md.Append("- <a href=\"guides/").Append(page.Key).Append(".md\">").Append(GuideTemplate.Escape(page.Name)).Append("</a> (`").Append(page.Key).Append("`)\n");
                html.Append("<li><a href=\"guides/").Append(page.Key).Append(".html\">").Append(GuideTemplate.Escape(page.Name)).Append("</a></li>\n");
            }
            md.Append("\nFull owning-row dispositions: [coverage manifest](coverage-manifest.json). Exact inputs: [source manifest](source-manifest.json).\n");
            html.Append("</ul>\n<p><a href=\"coverage-manifest.json\">Coverage manifest</a> · <a href=\"source-manifest.json\">Source manifest</a></p>\n</body></html>\n");
            files.Add("index.md", GuideMarkdown.Utf8.GetBytes(md.ToString()));
            files.Add("index.html", GuideMarkdown.Utf8.GetBytes(html.ToString()));
        }
        return new(files, orderedCoverage, orderedFindings);
    }

    /// <summary>Checks complete membership and bytes against fresh generation, including both quote payloads and renditions.</summary>
    public static IReadOnlyList<GuideFinding> Check(GuideBundle expected, IReadOnlyDictionary<string, byte[]> actual)
    {
        ArgumentNullException.ThrowIfNull(expected);
        ArgumentNullException.ThrowIfNull(actual);
        var findings = new List<GuideFinding>(expected.Findings);
        foreach (string name in expected.Files.Keys.Union(actual.Keys, StringComparer.Ordinal).Order(StringComparer.Ordinal))
        {
            int dot = name.LastIndexOf('.');
            string key = name.StartsWith("guides/", StringComparison.Ordinal) && dot > 7 ? name[7..dot] : "inventory";
            if (!expected.Files.TryGetValue(name, out byte[]? wanted)) findings.Add(new(key, "unexpected-output", "An undeclared generated file is present: " + name));
            else if (!actual.TryGetValue(name, out byte[]? found)) findings.Add(new(key, "missing-output", "A generated file is missing: " + name));
            else if (!wanted.AsSpan().SequenceEqual(found))
            {
                findings.Add(new(key, "edited-output", "The generated file differs from fresh generation: " + name));
                if (name.StartsWith("guides/", StringComparison.Ordinal))
                    findings.Add(new(key, "clearance-failed", "Quote payloads, readable boundaries and surrounding advice must match the complete generated guide."));
            }
        }
        return findings;
    }

    /// <summary>Marks added, removed or changed guide input identities due without reading source files or a clock.</summary>
    public static GuideInputComparison CompareInputs(ReadOnlySpan<byte> before, ReadOnlySpan<byte> after)
    {
        try
        {
            var left = JsonSerializer.Deserialize<GuideSourceManifest>(before, GuideJson.Options);
            var right = JsonSerializer.Deserialize<GuideSourceManifest>(after, GuideJson.Options);
            if (!ValidManifest(left) || !ValidManifest(right))
                return new([], [new("inventory", "invalid-manifest", "Both manifests must describe a complete generation with valid exact identities.")]);
            Dictionary<string, string> a = left!.Guides.ToDictionary(g => g.Key, g => g.InputIdentity, StringComparer.Ordinal);
            Dictionary<string, string> b = right!.Guides.ToDictionary(g => g.Key, g => g.InputIdentity, StringComparer.Ordinal);
            bool conventionChanged = left.GeneratorVersion != right.GeneratorVersion || left.TemplateVersion != right.TemplateVersion;
            return new(a.Keys.Union(b.Keys, StringComparer.Ordinal).Where(k => conventionChanged || a.GetValueOrDefault(k) != b.GetValueOrDefault(k)).Order(StringComparer.Ordinal).ToArray(), []);
        }
        catch (Exception exception) when (exception is JsonException or ArgumentException)
        { return new([], [new("inventory", "invalid-manifest", "A source manifest is malformed or has duplicate identities.")]); }
    }

    private static bool ValidManifest(GuideSourceManifest? manifest) => manifest is not null &&
        manifest.Format == "vericairn.guide-sources/1" && manifest.Complete &&
        !string.IsNullOrWhiteSpace(manifest.GeneratorVersion) && !string.IsNullOrWhiteSpace(manifest.TemplateVersion) &&
        manifest.IndexIdentity is not null && HashPattern().IsMatch(manifest.IndexIdentity) &&
        manifest.Sources is not null && manifest.Blocks is not null && manifest.Guides is { Length: <= 20000 } &&
        manifest.Guides.All(g => g is not null && g.Key is not null && SafeKey(g.Key) &&
            g.InputIdentity is not null && HashPattern().IsMatch(g.InputIdentity));

    internal static bool SafeKey(string key) => KeyPattern().IsMatch(key) && !key.Contains("..", StringComparison.Ordinal) &&
        !ReservedName().IsMatch(key.Split('.')[0]);

    internal static bool SafePath(string path) => path.Length > 0 && path.Length < 240 && path.Split('/').All(part =>
        part.Length > 0 && part is not ("." or "..") && !part.EndsWith('.') && !part.EndsWith(' ') &&
        part.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '-' or '_') && !ReservedName().IsMatch(part.Split('.')[0]));

    internal static bool ValidShape(GuideSourceIndex index) =>
        index.Sources is { Length: > 0 and <= 512 } && index.Blocks is { Length: <= 20000 } && index.Entries is { Length: <= 20000 } &&
        index.Tables is { Length: <= 2000 } && index.Roles is { Length: <= 5 } && index.Families is { Length: <= 512 } &&
        index.Sources.All(s => s is not null && s.Id is not null && s.Path is not null && s.Area is not null && s.Sha256 is not null) &&
        index.Blocks.All(b => b is not null && b.Id is not null && b.Source is not null && b.Sha256 is not null) &&
        index.Tables.All(t => t is not null && t.Source is not null && t.Heading is not null && t.Kind is not null && Strings(t.Boundaries) && Strings(t.Context)) &&
        index.Roles.All(r => r is not null && r.Kind is not null && Strings(r.Blocks)) &&
        index.Families.All(f => f is not null && f.Name is not null && Strings(f.Boundaries)) &&
        index.Entries.All(e => e is not null && e.Key is not null && e.Source is not null && e.Heading is not null && e.Name is not null && e.State is not null &&
            Strings(e.Status) && Strings(e.Boundaries) && Strings(e.Context) && Strings(e.References) && Strings(e.Aliases));

    private static bool Strings(string[]? values) => values is { Length: <= 20000 } && values.All(s => s is not null);

    private static bool UnsafeMarkup(string text)
    {
        if (text.Any(c => char.IsControl(c) && c is not ('\n' or '\r' or '\t'))) return true;
        string decoded = text;
        for (int i = 0; i < 4; i++) decoded = Uri.UnescapeDataString(WebUtility.HtmlDecode(decoded));
        return ActiveMarkup().IsMatch(decoded) || UnsafeLink().IsMatch(decoded);
    }

    [GeneratedRegex(@"^[a-z0-9][a-z0-9.-]{0,119}$", RegexOptions.CultureInvariant)]
    private static partial Regex KeyPattern();
    [GeneratedRegex(@"^sha256:[0-9a-f]{64}$", RegexOptions.CultureInvariant)]
    private static partial Regex HashPattern();
    [GeneratedRegex(@"^(con|prn|aux|nul|com[0-9]|lpt[0-9])$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ReservedName();
    [GeneratedRegex(@"!\[|<!--|<\s*/?\s*(script|iframe|object|embed|style|link|meta|base|img|audio|video|svg|math|form|input|button)\b|<[^>]*\bon\w+\s*=", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ActiveMarkup();
    [GeneratedRegex(@"\b(?:java\s*script|vbscript|data|file)\s*:|\]\(\s*(?:[^)]*/)?\.\.(?:/|\\)|\]\(\s*//", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex UnsafeLink();
    [GeneratedRegex(@"\]\((catalog/[^)#\s]+\.md)(?:#[^)]*)?\)", RegexOptions.CultureInvariant)]
    private static partial Regex CatalogLink();
}
