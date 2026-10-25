// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using Vericairn.Records.Canonical;
using Vericairn.Records.Estate;
using Vericairn.Records.Model;

namespace Vericairn.Records.Templates;

/// <summary>A template's exact content identity, never a permission or a mutable latest-version alias.</summary>
public sealed record TemplatePin(string Id, string Version, string Hash);

/// <summary>Template content contributed by the host or retained under docs/templates in the estate.</summary>
public static class TemplateCatalog
{
    private static readonly Dictionary<(string Id, string Version), TemplateDefinition> Installed = [];
    private static readonly HashSet<(string Id, string Version)> Retired = [];

    static TemplateCatalog()
    {
        foreach (TemplateDefinition definition in PresentationTemplates.Definitions) Register(definition);
    }

    /// <summary>Registers immutable content. Retirement hides a version from new-instance selection but keeps it readable.</summary>
    public static void Register(TemplateDefinition definition, bool retired = false)
    {
        ArgumentNullException.ThrowIfNull(definition);
        lock (Installed)
        {
            var key = (definition.Id, definition.Version);
            if (Installed.TryGetValue(key, out TemplateDefinition? previous) && previous.Hash != definition.Hash)
                throw new InvalidOperationException("A template version cannot be replaced with different content.");
            Installed[key] = definition;
            if (retired) Retired.Add(key);
        }
    }

    /// <summary>Available versions, in identity and version order. Reading never upgrades an instance.</summary>
    public static IReadOnlyList<TemplateDefinition> All(EstateModel model, bool includeRetired = false)
    {
        ArgumentNullException.ThrowIfNull(model);
        Dictionary<(string Id, string Version), TemplateDefinition> definitions;
        lock (Installed) definitions = new(Installed);
        string directory = Path.Combine(model.Root, "docs", "templates");
        if (Directory.Exists(directory))
        {
            // Refuse redirected roots and members; templates are ordinary content, never executable loaders.
            GeneratedDocumentVerification.SafeFile(model.Root, "docs/templates");
            var options = new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = FileAttributes.ReparsePoint };
            foreach (string file in Directory.EnumerateFiles(directory, "*.json", options).Order(StringComparer.Ordinal))
            {
                string relative = Path.GetRelativePath(model.Root, file).Replace(Path.DirectorySeparatorChar, '/');
                TemplateDefinition definition;
                try
                {
                    using JsonDocument json = CanonicalJson.Parse(File.ReadAllBytes(GeneratedDocumentVerification.SafeFile(model.Root, relative)));
                    definition = TemplateDefinition.Read(json.RootElement);
                }
                catch (CanonicalJsonException error) { throw new InvalidOperationException("Template content must be valid canonical JSON.", error); }
                var key = (definition.Id, definition.Version);
                if (definitions.TryGetValue(key, out TemplateDefinition? other) && other.Hash != definition.Hash)
                    throw new InvalidOperationException("Two different contracts claim the same template version.");
                definitions[key] = definition;
            }
        }
        lock (Installed)
            return definitions.Values.Where(d => includeRetired || !Retired.Contains((d.Id, d.Version)))
                .OrderBy(d => d.Id, StringComparer.Ordinal).ThenBy(d => d.Version, StringComparer.Ordinal).ToArray();
    }

    /// <summary>Resolves an exact retained pin, including a retired version, or refuses a missing or changed contract.</summary>
    public static TemplateDefinition Resolve(EstateModel model, TemplatePin pin)
    {
        ArgumentNullException.ThrowIfNull(pin);
        TemplateDefinition? definition = All(model, includeRetired: true).SingleOrDefault(d => d.Id == pin.Id && d.Version == pin.Version);
        if (definition is null || definition.Hash != pin.Hash)
            throw new InvalidOperationException("The exact pinned template content is unavailable or has changed.");
        return definition;
    }

    /// <summary>Reads the optional template pin of a living-document declaration.</summary>
    public static TemplatePin? Pin(RecordEnvelope declaration)
    {
        ArgumentNullException.ThrowIfNull(declaration);
        JsonElement template = TJson.Object(declaration.Body, "template");
        return TJson.Text(template, "id") is { } id && TJson.Text(template, "version") is { } version && TJson.Text(template, "hash") is { } hash
            ? new(id, version, hash) : null;
    }
}
