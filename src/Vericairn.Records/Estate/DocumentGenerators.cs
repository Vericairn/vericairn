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

    /// <summary>The whole document (region null) or one region's content; null when this generator makes neither.</summary>
    string? Generate(EstateModel model, string? region, IReadOnlyList<string> inputs);
}

/// <summary>The generators installed here: the foundation's, and those packs register. Registering one makes its name available.</summary>
public static class DocumentGenerators
{
    private static readonly Dictionary<string, IDocumentGenerator> Installed = new(StringComparer.Ordinal) { [ReleasesTable.Name] = new ReleasesTable() };

    /// <summary>Installs a generator; a pack registers its own.</summary>
    public static void Register(IDocumentGenerator generator)
    {
        ArgumentNullException.ThrowIfNull(generator);
        lock (Installed)
        {
            Installed[generator.Id] = generator;
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

    /// <summary>
    /// vericairn.records/releases: a table of the published releases of the products its inputs name, by any revision of a product's
    /// record (all products when none), newest first, from release records only. It restates the releases in a document that must
    /// name them, held to the records.
    /// </summary>
    private sealed class ReleasesTable : IDocumentGenerator
    {
        public const string Name = "vericairn.records/releases";

        public string Id => Name;

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
}
