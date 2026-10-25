// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using System.Text.Json.Nodes;
using Vericairn.Records.Model;

namespace Vericairn.Records.Estate;

/// <summary>
/// How a living document of another repository is read at a revision (the estate's design/living-documents.md § 8): a pack's
/// source, such as the software pack's git-file, reading from a clone of that repository registered on this machine.
/// </summary>
public interface IDocumentSource
{
    /// <summary>The source's name, namespace/name, as declarations name it.</summary>
    string Id { get; }

    /// <summary>The supported immutable snapshot interface version; legacy sources supply no snapshot interface.</summary>
    int SnapshotVersion => 0;

    /// <summary>Obtains a complete authorized input selection at the exact requested commit before rendering.</summary>
    DocumentSourceSelection ReadSelection(EstateModel model, RecordEnvelope declaration, string commit, DocumentGeneratorDescriptor generator) =>
        new(DocumentGenerationStatus.Unavailable, [], commit, "This source does not provide the versioned snapshot contract.");

    /// <summary>Reads the complete owned output inventory at that same exact commit, including unexpected files.</summary>
    DocumentFileInventory ReadInventory(EstateModel model, RecordEnvelope declaration, string commit) =>
        new(DocumentGenerationStatus.Unavailable, [], commit, "This source does not provide complete member inventories.");

    /// <summary>The commit at the head of the repository's clone here, or null when no clone is registered or it cannot be read.</summary>
    string? Head(EstateModel model, RecordEnvelope declaration);

    /// <summary>The commit a reference names in the clone, or null.</summary>
    string? Resolve(EstateModel model, RecordEnvelope declaration, string reference);

    /// <summary>True when the source can establish that one commit is an ancestor of another; false when unknown.</summary>
    bool IsAncestor(EstateModel model, RecordEnvelope declaration, string ancestor, string descendant) => false;

    /// <summary>The document's bytes at a commit, or null when that commit has no such file or no clone is registered here.</summary>
    byte[]? Read(EstateModel model, RecordEnvelope declaration, string commit);

    /// <summary>The content identity of covered paths at a commit, or null when it cannot be evaluated here.</summary>
    string? Covered(EstateModel model, RecordEnvelope declaration, IReadOnlyList<string> paths, string commit);

    /// <summary>True when a repository-relative path exists at a commit.</summary>
    bool Exists(EstateModel model, RecordEnvelope declaration, string commit, string path);

    /// <summary>Complete repository file changes between exact local publication commits; null before means the complete first publication.</summary>
    DocumentSourceChanges Changes(EstateModel model, RecordEnvelope declaration, string? before, string commit) =>
        new([], "This document source cannot compare publication commits.");
}

/// <summary>A repository-relative changed file and its added, modified or deleted status.</summary>
public sealed record DocumentSourceChange(string Path, string Kind);

/// <summary>A complete local comparison, or an explicit gap; an empty successful comparison means no changed files.</summary>
public sealed record DocumentSourceChanges(IReadOnlyList<DocumentSourceChange> Files, string? Problem = null);

/// <summary>The document sources installed here; registering one makes its name available to declarations.</summary>
public static class DocumentSources
{
    private static readonly Dictionary<string, IDocumentSource> Installed = new(StringComparer.Ordinal);

    /// <summary>Installs a source unless one of that name is installed; true when it was installed.</summary>
    public static bool Register(IDocumentSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        lock (Installed)
        {
            if (!Installed.TryAdd(source.Id, source))
            {
                return false;
            }
        }

        LivingDocuments.Register(source.Id);
        return true;
    }

    /// <summary>The installed source of that name, or null.</summary>
    public static IDocumentSource? Find(string? id)
    {
        lock (Installed)
        {
            return id is null ? null : Installed.GetValueOrDefault(id);
        }
    }
}

/// <summary>Which clone on this machine holds each repository resource of an estate; kept beside the estate's per-machine state, never in a record.</summary>
public static class RepositoryClones
{
    /// <summary>The registered clone of a repository resource, or null.</summary>
    public static string? Find(string estateId, string resourceId) => All(estateId).GetValueOrDefault(resourceId);

    /// <summary>Every registered clone of the estate's repositories, by resource id.</summary>
    public static IReadOnlyDictionary<string, string> All(string estateId)
    {
        string path = EstateLocations.ClonesPath(estateId);
        if (!File.Exists(path))
        {
            return new Dictionary<string, string>(StringComparer.Ordinal);
        }

        using JsonDocument document = JsonDocument.Parse(File.ReadAllBytes(path));
        return document.RootElement.EnumerateObject().Where(p => p.Value.ValueKind == JsonValueKind.String).ToDictionary(p => p.Name, p => p.Value.GetString()!, StringComparer.Ordinal);
    }

    /// <summary>Registers a clone of a repository resource, replacing an earlier registration of that resource.</summary>
    public static void Register(string estateId, string resourceId, string clone)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceId);
        ArgumentException.ThrowIfNullOrWhiteSpace(clone);
        var all = new JsonObject();
        foreach ((string resource, string path) in All(estateId).Append(new KeyValuePair<string, string>(resourceId, Path.GetFullPath(clone))).GroupBy(p => p.Key).Select(g => g.Last()).OrderBy(p => p.Key, StringComparer.Ordinal))
        {
            all[resource] = path;
        }

        string file = EstateLocations.ClonesPath(estateId);
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllText(file, all.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
    }
}
