// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Globalization;
using System.Text;
using Vericairn.Records.Model;

namespace Vericairn.Records.Estate;

/// <summary>A generator of living documents or of their generated regions, from the estate's records and its declared inputs.</summary>
public interface IDocumentGenerator
{
    /// <summary>The generator's name, namespace/name, as declarations name it.</summary>
    string Id { get; }

    /// <summary>The versioned contract, or null for a retained legacy implementation.</summary>
    DocumentGeneratorDescriptor? Descriptor => null;

    /// <summary>Renders only the immutable, authorized sources supplied by the caller.</summary>
    DocumentRenderResult Generate(DocumentGenerationContext context) => DocumentRenderResult.Unavailable("This implementation supplies only the legacy contract.");

    /// <summary>The whole document (region null) or one region's content; null when this generator makes neither.</summary>
    string? Generate(EstateModel model, string? region, IReadOnlyList<string> inputs) => null;
}

/// <summary>The generators installed here: the foundation's, and those packs register. Registering one makes its name available.</summary>
public static class DocumentGenerators
{
    private static readonly Dictionary<string, IDocumentGenerator> Installed = new(StringComparer.Ordinal)
    {
        [ReleasesTable.Name] = new ReleasesTable(), [ProgressRegion.Name] = new ProgressRegion(),
        [RecordCompanionGenerator.Name] = new RecordCompanionGenerator(),
        [ManifestCompanionGenerator.Name] = new ManifestCompanionGenerator(),
    };

    static DocumentGenerators()
    {
        foreach (var register in RevisionCompanionGenerator.Registers.Where(r => r.Id != ReleasesTable.Name)) Register(new RevisionCompanionGenerator(register.Id));
    }

    /// <summary>Installs a generator; a pack registers its own.</summary>
    public static void Register(IDocumentGenerator generator)
    {
        ArgumentNullException.ThrowIfNull(generator);
        generator.Descriptor?.Validate(generator.Id);
        lock (Installed)
        {
            if (!Installed.TryAdd(generator.Id, generator))
            {
                throw new InvalidOperationException("A document generator with this identity is already registered.");
            }
        }

        LivingDocuments.Register(generator.Id);
    }

    /// <summary>The installed generator of that name, or null.</summary>
    public static IDocumentGenerator? Find(string id)
    {
        ArgumentNullException.ThrowIfNull(id);
        lock (Installed)
        {
            return Installed.GetValueOrDefault(id);
        }
    }

    /// <summary>Installed implementations, ordered by identity; enumeration performs no generation or source discovery.</summary>
    public static IReadOnlyList<IDocumentGenerator> All
    {
        get { lock (Installed) return Installed.Values.OrderBy(g => g.Id, StringComparer.Ordinal).ToArray(); }
    }

    /// <summary>
    /// vericairn.records/releases: a table of the published releases of the products its inputs name, by any revision of a product's
    /// record (all products when none), newest first, from release records only. It restates the releases in a document that must
    /// name them, held to the records.
    /// </summary>
    private sealed class ReleasesTable : IDocumentGenerator
    {
        public const string Name = "vericairn.records/releases";
        private readonly RevisionCompanionGenerator revisions = new(Name);

        public string Id => Name;

        public DocumentGeneratorDescriptor Descriptor => revisions.Descriptor;

        public DocumentRenderResult Generate(DocumentGenerationContext context) => revisions.Generate(context);

        public string? Generate(EstateModel model, string? region, IReadOnlyList<string> inputs)
        {
            if (region is null)
            {
                return null;
            }

            HashSet<string> products = inputs.Select(i => LivingDocuments.ProductIdentity(model, i)).ToHashSet(StringComparer.Ordinal);
            List<RecordEnvelope> published = model.Current("release")
                .Where(r => EstateModel.BodyString(r, "status") == "published" && (inputs.Count == 0 || (r.Subject is not null && products.Contains(LivingDocuments.ProductIdentity(model, r.Subject.Id)))))
                .OrderByDescending(r => EstateModel.BodyString(r, "publishedAt"), StringComparer.Ordinal).ThenByDescending(r => r.Id, StringComparer.Ordinal).ToList();
            var text = new StringBuilder("| Version | Published | Release |\n|---|---|---|\n");
            foreach (RecordEnvelope release in published)
            {
                string version = EstateModel.BodyString(release, "version") ?? "?";
                string date = (EstateModel.BodyString(release, "publishedAt") ?? string.Empty) is { Length: >= 10 } at ? at[..10] : "?";
                string? url = EstateModel.BodyString(release, "url");
                text.Append(CultureInfo.InvariantCulture, $"| {version} | {date} | {(url is null ? "v" + version : $"[v{version}]({url})")} |\n");
            }

            return text.ToString();
        }
    }

    /// <summary>
    /// vericairn.records/progress: the progress report (<see cref="ProgressReport"/>) in a region, as a fenced text block of what
    /// <c>vericairn progress</c> prints, from the records alone; it takes no inputs. Its check shows the records' own validation and
    /// says that it leaves out the repository checks: those read the document that carries the region, so a region that showed them
    /// could never equal a fresh generation, and generating them inside the estate check would run the check within itself.
    /// </summary>
    private sealed class ProgressRegion : IDocumentGenerator
    {
        public const string Name = "vericairn.records/progress";

        public string Id => Name;

        public string? Generate(EstateModel model, string? region, IReadOnlyList<string> inputs)
        {
            if (region is null)
            {
                return null;
            }

            string report = ProgressReport.Render(model, null);

            // The fence is longer than any run of backticks in the report, so no title or question can close it.
            int longest = 0;
            int run = 0;
            foreach (char c in report)
            {
                run = c == '`' ? run + 1 : 0;
                longest = Math.Max(longest, run);
            }

            string fence = new('`', Math.Max(3, longest + 1));
            return fence + "text\n" + report + fence + "\n";
        }
    }
}
