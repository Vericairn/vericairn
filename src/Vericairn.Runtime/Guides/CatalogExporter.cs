// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Vericairn.Runtime.Guides;

/// <summary>Pure public rendition of an explicit selection. Private preview files are never copied into the bundle.</summary>
public static class CatalogExporter
{
    private static readonly string[] ExportedRoles = ["adapter", "domain", "foundry", "provider", "shared-capability"];
    /// <summary>The exporter convention, independent of the underlying guide generator.</summary>
    public const string Version = "vericairn.catalog-bundle/1";

    /// <summary>Builds a proposed repository from exact allowlisted bytes. Reads no filesystem, environment, clock or network.</summary>
    public static CatalogBundle Generate(GuideSourceIndex index, IReadOnlyDictionary<string, byte[]> sources,
        CatalogExportPlan plan, IReadOnlyList<CatalogTrackingIssue>? trackingIssues = null)
    {
        ArgumentNullException.ThrowIfNull(index);
        ArgumentNullException.ThrowIfNull(sources);
        ArgumentNullException.ThrowIfNull(plan);
        trackingIssues ??= [];
        if (!ValidPlan(index, plan) || !Allowlisted(index, sources.Keys, plan))
            return Failed("export-allowlist", "The exact index, source allowlist and bounded public selection are required.");
        if (plan.IndexIdentity != GuideGenerator.Hash(GuideGenerator.Json(index)))
            return Failed("export-index", "The reviewed index identity differs; review the selection again.");

        var selected = plan.Entries.ToHashSet(StringComparer.Ordinal);
        var entries = index.Entries.Where(e => selected.Contains(e.Key)).OrderBy(e => e.Key, StringComparer.Ordinal).ToArray();
        if (entries.Length != selected.Count || entries.Any(e => e.State is "withheld" or "reference"))
            return Failed("export-selection", "Only explicit owning public entries may be selected; withheld rows and aliases are not a publication selection.");
        // GUIDE-01 checks the entire inventory, including rows outside this public selection.
        GuideBundle checkedInputs = GuideGenerator.Generate(index, sources, entries.Where(e => e.State is "planned" or "remainder").Select(e => e.Key).ToArray());
        if (!checkedInputs.Success)
            return Failed("export-inputs", "Guide generation failed. Inspect the inputs with the private guide check; no private findings were exported.");
        var owners = index.Entries.ToDictionary(e => e.Key, StringComparer.Ordinal);
        var aliases = index.Entries.SelectMany(e => e.Aliases.Select(a => (Alias: a, e.Key))).ToDictionary(a => a.Alias, a => a.Key, StringComparer.Ordinal);
        string Resolve(string key) => owners.ContainsKey(key) ? key : aliases[key];
        if (entries.SelectMany(e => e.References).Any(r => !selected.Contains(Resolve(r))))
            return Failed("export-reference", "A selected entry depends on an entry outside the public selection. Review the public dependency scope.");
        if (plan.Retirements.Any(r => !selected.Contains(r.Key)) || entries.Any(e =>
            (e.State is "retired" or "available") != plan.Retirements.Any(r => r.Key == e.Key)))
            return Failed("export-retirement", "Each selected retired or available entry requires exactly one reviewed retirement page, and active entries require none.");
        if (plan.Retirements.Any(r => !ValidRetirement(r)))
            return Failed("export-retirement", "Retirement requires a public date, revision, replacement and explicit migration notes.");

        var tables = index.Tables.ToDictionary(t => (t.Source, t.Heading));
        var sourceAreas = index.Sources.ToDictionary(s => s.Id, s => s.Area, StringComparer.Ordinal);
        var used = new HashSet<string>(StringComparer.Ordinal);
        foreach (GuideEntryBinding entry in entries)
        {
            GuideTableBinding table = tables[(entry.Source, entry.Heading)];
            used.UnionWith(entry.Status.Concat(entry.Boundaries).Concat(entry.Context).Concat(table.Boundaries).Concat(table.Context));
            used.UnionWith(index.Roles.Single(r => r.Kind == table.Kind).Blocks);
            if (table.Family is not null) used.UnionWith(index.Families.Single(f => f.Name == table.Family).Boundaries);
        }
        GuideBlockPin[] blocks = index.Blocks.Where(b => used.Contains(b.Id)).OrderBy(b => b.Id, StringComparer.Ordinal).ToArray();
        var publicIds = blocks.Select((b, i) => (b.Id, Public: sourceAreas[b.Source] + ".block-" + (i + 1).ToString("D6", CultureInfo.InvariantCulture)))
            .ToDictionary(b => b.Id, b => b.Public, StringComparer.Ordinal);
        string[] Mapped(IEnumerable<string> names) => names.Select(n => publicIds.GetValueOrDefault(n, n)).Order(StringComparer.Ordinal).ToArray();
        // Rebind source labels before invoking the original template, including its cross-section quote references.
        GuideSourceIndex publicLabels = index with
        {
            Blocks = index.Blocks.Select(b => b with { Id = publicIds.GetValueOrDefault(b.Id, b.Id) }).ToArray(),
            Roles = index.Roles.Select(r => r with { Blocks = Mapped(r.Blocks) }).ToArray(),
            Families = index.Families.Select(f => f with { Boundaries = Mapped(f.Boundaries) }).ToArray(),
            Tables = index.Tables.Select(t => t with { Boundaries = Mapped(t.Boundaries), Context = Mapped(t.Context) }).ToArray(),
            Entries = index.Entries.Select(e => e with { Status = Mapped(e.Status), Boundaries = Mapped(e.Boundaries), Context = Mapped(e.Context) }).ToArray(),
        };
        GuideBundle generated = GuideGenerator.Generate(publicLabels, sources, entries.Where(e => e.State is "planned" or "remainder").Select(e => e.Key).ToArray());
        if (!generated.Success) return Failed("export-inputs", "The public source labels could not be resolved unambiguously.");
        var inventory = new CatalogInventory("vericairn.catalog-inventory/1", entries.Select(e =>
        {
            GuideTableBinding table = tables[(e.Source, e.Heading)];
            return new CatalogEntry(e.Key, e.Name, table.Kind, table.Family, e.State,
                "guides/" + table.Kind + "/" + e.Key + ".md", e.State is "planned" or "remainder",
                plan.Retirements.SingleOrDefault(r => r.Key == e.Key)?.Replacement);
        }).ToArray());
        if (trackingIssues.Count > entries.Length || trackingIssues.Any(i => i is null || !selected.Contains(i.Key) || i.Number <= 0) ||
            trackingIssues.Select(i => i.Key).Distinct(StringComparer.Ordinal).Count() != trackingIssues.Count ||
            trackingIssues.Select(i => i.Number).Distinct().Count() != trackingIssues.Count)
            return Failed("export-tracking", "The explicit issue map must contain distinct selected keys and positive, distinct public issue numbers.");

        var files = new SortedDictionary<string, byte[]>(StringComparer.Ordinal);
        void Text(string path, string value) => files.Add(path, GuideMarkdown.Utf8.GetBytes(value.Replace("\r\n", "\n", StringComparison.Ordinal).TrimEnd() + "\n"));
        void Json<T>(string path, T value) => files.Add(path, GuideGenerator.Json(value));
        Text("README.md", CatalogText.Readme);
        Text("LICENSE.md", CatalogText.License);
        Text("NOTICE.md", CatalogText.Notice);
        Text("CONTRIBUTING.md", CatalogText.Contributing);
        Text("templates/README.md", CatalogText.Template);
        Json("templates/role-recipes.json", new { template = GuideGenerator.TemplateVersion,
            roles = ExportedRoles
                .Select(kind => new { kind, build = GuideTemplate.Build(kind), checks = GuideTemplate.Checks(kind) }).ToArray() });
        var rows = index.Sources.Where(s => s.Area == "catalog").SelectMany(s => GuideMarkdown.Read(s.Id, sources[s.Id]).Rows)
            .ToDictionary(r => (r.Source, r.Heading, r.Name));
        Json("sources/catalog/entries.json", entries.Select(e => new
        {
            e.Key, e.Name, e.State,
            kind = tables[(e.Source, e.Heading)].Kind,
            family = tables[(e.Source, e.Heading)].Family,
            aliases = e.Aliases.Order(StringComparer.Ordinal).ToArray(),
            references = e.References.Select(Resolve).Order(StringComparer.Ordinal).ToArray(),
            status = Mapped(e.Status), boundaries = Mapped(e.Boundaries), context = Mapped(e.Context),
            tableBoundaries = Mapped(tables[(e.Source, e.Heading)].Boundaries),
            tableContext = Mapped(tables[(e.Source, e.Heading)].Context),
            familyBoundaries = tables[(e.Source, e.Heading)].Family is string family ? Mapped(index.Families.Single(f => f.Name == family).Boundaries) : [],
            role = Mapped(index.Roles.Single(r => r.Kind == tables[(e.Source, e.Heading)].Kind).Blocks),
            cells = new SortedDictionary<string, string>(rows[(e.Source, e.Heading, e.Name)].Cells.Where(c => c.Key != "#").ToDictionary(c => c.Key, c => c.Value), StringComparer.Ordinal),
        }).ToArray());
        foreach (string area in new[] { "catalog", "contracts" })
            Json("sources/" + area + "/blocks.json", blocks.Where(b => sourceAreas[b.Source] == area).Select(b => new
            {
                key = publicIds[b.Id], text = string.Concat(GuideMarkdown.Lines(GuideMarkdown.Utf8.GetString(sources[b.Source]))[(b.StartLine - 1)..b.EndLine]),
            }).ToArray());
        Json("sources/catalog/retirements.json", plan.Retirements.OrderBy(r => r.Key, StringComparer.Ordinal).ToArray());
        Json("indexes/entries.json", inventory);
        Json("indexes/tracking-issues.json", trackingIssues.OrderBy(i => i.Key, StringComparer.Ordinal).ToArray());
        Text("indexes/entries.md", Navigation(inventory.Entries, "Entries"));
        Text("indexes/families.md", GroupNavigation(inventory.Entries, e => e.Family ?? "Shared", "Families"));
        Text("indexes/roles.md", GroupNavigation(inventory.Entries, e => e.Kind, "Roles"));
        Text(".github/ISSUE_TEMPLATE/request-implementation.yml", CatalogRequests.Form(inventory));
        foreach (CatalogEntry entry in inventory.Entries)
        {
            if (entry.Requestable)
            {
                GuidePage page = JsonSerializer.Deserialize<GuidePage>(generated.Files["guides/" + entry.Key + ".json"], GuideJson.Options)!;
                Text(entry.Guide, PublicGuide(page));
            }
            else Text(entry.Guide, Retirement(entry, plan.Retirements.Single(r => r.Key == entry.Key)));
        }
        Json("manifests/inputs.json", new { format = Version, generator = GuideGenerator.GeneratorVersion,
            template = GuideGenerator.TemplateVersion, publicationState = "local-preparation",
            inputs = files.Where(f => f.Key.StartsWith("sources/", StringComparison.Ordinal) || f.Key.StartsWith("templates/", StringComparison.Ordinal) || f.Key == "indexes/tracking-issues.json")
                .Select(f => new { path = f.Key, sha256 = GuideGenerator.Hash(f.Value) }).ToArray() });
        // The output manifest excludes itself; its exact bytes are part of the reviewed directory comparison.
        Json("manifests/outputs.json", new { format = Version, files = files.Select(f => new { path = f.Key, sha256 = GuideGenerator.Hash(f.Value) }).ToArray() });
        string[] forbidden = [.. plan.RestrictedText, plan.IndexIdentity, .. index.Sources.Select(s => s.Sha256), .. index.Blocks.Select(b => b.Sha256)];
        IReadOnlyList<GuideFinding> scan = CatalogDisclosure.Check(files, forbidden);
        return scan.Count == 0 ? new(files, inventory, []) : new(new SortedDictionary<string, byte[]>(StringComparer.Ordinal),
            new("vericairn.catalog-inventory/1", []), scan);
    }

    internal static bool Allowlisted(GuideSourceIndex index, IEnumerable<string> supplied, CatalogExportPlan plan) =>
        index.Sources.Length == plan.Allowlist.Length && index.Sources.All(s => plan.Allowlist.Count(a => a == s) == 1) &&
        supplied.Order(StringComparer.Ordinal).SequenceEqual(index.Sources.Select(s => s.Id).Order(StringComparer.Ordinal), StringComparer.Ordinal);

    internal static bool ValidPlan(GuideSourceIndex index, CatalogExportPlan plan) => GuideGenerator.ValidShape(index) &&
        plan.Format == "vericairn.catalog-export/1" && plan.IndexIdentity is not null &&
        plan.Allowlist is { Length: > 0 and <= 512 } && plan.Allowlist.All(s => s is not null) &&
        plan.Entries is { Length: > 0 and <= 20000 } && plan.Entries.All(k => k is not null && GuideGenerator.SafeKey(k)) &&
        plan.Entries.Distinct(StringComparer.Ordinal).Count() == plan.Entries.Length &&
        plan.Retirements is { Length: <= 20000 } && plan.Retirements.All(r => r is not null && r.Key is not null) &&
        plan.Retirements.Select(r => r.Key).Distinct(StringComparer.Ordinal).Count() == plan.Retirements.Length &&
        plan.RestrictedText is { Length: <= 20000 } && plan.RestrictedText.All(t => !string.IsNullOrWhiteSpace(t) && t.Length <= 4096);

    private static bool ValidRetirement(CatalogRetirement r) =>
        DateOnly.TryParseExact(r.Date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _) &&
        !string.IsNullOrWhiteSpace(r.Revision) && r.Revision.Length <= 120 &&
        CatalogDisclosure.PublicUrl(r.Replacement) && !string.IsNullOrWhiteSpace(r.Migration) && r.Migration.Length <= 16000;

    private static string PublicGuide(GuidePage page)
    {
        var text = new StringBuilder("# ").Append(GuideTemplate.Escape(page.Name)).Append('\n');
        foreach (GuideSection section in page.Sections)
        {
            string body = section.Title switch
            {
                "Publication and contribution route" => CatalogText.ContributionRoute,
                "Request implementation" => "Request target: " + page.Key + ".\nRequest implementation: unavailable until the repository and request route are authorized. Demand: not collected.\n" + CatalogRequests.Explanation + "\nSee CONTRIBUTING.md for the permitted contribution route and the tracking-issue index for supplied observations.",
                "Provenance, maintenance and corrections" => "Template: " + GuideGenerator.TemplateVersion + ". Generator: " + GuideGenerator.GeneratorVersion + ".\nPublic source renditions and their identities are in sources/ and manifests/. Change the owning sources, review, and regenerate. Publication is a separate operator decision.",
                _ => section.Text.Replace("Publication state: private preview.", "Publication state: local preparation; repository not authorized.", StringComparison.Ordinal),
            };
            text.Append("\n## ").Append(section.Title).Append("\n\nReview: ").Append(section.ReviewRole).Append(".\n\n<pre>").Append(GuideTemplate.Escape(body)).Append("</pre>\n");
            foreach (GuideQuote quote in section.Quotes)
                text.Append("\n<p>Public source: ").Append(GuideTemplate.Escape(quote.Id)).Append("</p>\n<pre>").Append(GuideTemplate.Escape(quote.Text)).Append("</pre>\n");
        }
        text.Append("\n[Catalog sources](../../sources/catalog/entries.json) · [Catalog blocks](../../sources/catalog/blocks.json) · [Role contracts](../../sources/contracts/blocks.json) · [Contribute](../../CONTRIBUTING.md)\n");
        return text.ToString();
    }

    private static string Retirement(CatalogEntry entry, CatalogRetirement retirement) =>
        "# " + GuideTemplate.Escape(entry.Name) + " — retired guide\n\n" +
        "Stable entry: `" + entry.Key + "`. This address is retained. Request implementation: unavailable for this scope.\n\n" +
        "Retirement date: " + retirement.Date + ". Public revision: " + GuideTemplate.Escape(retirement.Revision) + ".\n\n" +
        "Replacement: <" + retirement.Replacement + ">\n\n## Migration\n\n<pre>" + GuideTemplate.Escape(retirement.Migration) + "</pre>\n\n" +
        "## History and community implementations\n\nRetain prior published revisions in this repository's history. No community implementation or migration compatibility is inferred. Community packages retain their publishers and obligations. No implementation observations were supplied.\n\n" +
        "[Public retirement source](../../sources/catalog/retirements.json) · [Contribution route](../../CONTRIBUTING.md)\n";

    private static string Navigation(IEnumerable<CatalogEntry> entries, string title) => "# " + GuideTemplate.Escape(title) + "\n\n" +
        string.Concat(entries.Select(e => "- [" + e.Key + "](../" + e.Guide + ") — " + GuideTemplate.Escape(e.Name) + " (" + e.State + ")\n"));

    private static string GroupNavigation(IEnumerable<CatalogEntry> entries, Func<CatalogEntry, string> group, string title) =>
        "# " + title + "\n\n" + string.Concat(entries.GroupBy(group, StringComparer.Ordinal).OrderBy(g => g.Key, StringComparer.Ordinal)
            .Select(g => "## " + GuideTemplate.Escape(g.Key) + "\n\n" + Navigation(g, "Entries").Split("\n\n", 2)[1] + "\n"));

    internal static CatalogBundle Failed(string code, string message) => new(new SortedDictionary<string, byte[]>(StringComparer.Ordinal),
        new("vericairn.catalog-inventory/1", []), [new("inventory", code, message)]);
}
