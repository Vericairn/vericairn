// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Security.Cryptography;
using System.Text;
using Vericairn.Records.Authoring;
using Vericairn.Records.Estate;
using Vericairn.Records.Model;
using Vericairn.Records.Signing;
using Vericairn.Runtime.Objects;

namespace Vericairn.Runtime.Store;

/// <summary>
/// An opened estate on this machine: its root, its storage profile, its live store and its object store.
/// Under the repository-synced profile the record files in the repository are canonical and the live store is a
/// derived copy refreshed on every load; under the local-only profile the live store is canonical and no record
/// file exists in the root directory.
/// </summary>
public sealed class EstateSession : IDisposable
{
    private readonly RecordParseCache parseCache = new();
    private LiveStore.FileSnapshot? loadedFiles;

    internal long ParsedFiles => parseCache.ParsedFiles;

    // The response envelope must come from the very bytes whose model was just judged, not a second full store read/sync.
    internal RecordFile? LoadedFile(string path) => loadedFiles?.ByPath.GetValueOrDefault(path);

    private EstateSession(string root, EstateManifest manifest, LiveStore live, IObjectStore objects)
    {
        Root = root;
        Manifest = manifest;
        Live = live;
        Objects = objects;
    }

    /// <summary>Repository root (repository-synced) or estate directory holding the manifest (local-only).</summary>
    public string Root { get; }

    /// <summary>The manifest.</summary>
    public EstateManifest Manifest { get; }

    /// <summary>Storage profile.</summary>
    public string Profile => Manifest.Profile;

    /// <summary>The live store on this machine.</summary>
    public LiveStore Live { get; }

    /// <summary>The object store: the repository's objects/ directory, or the live database.</summary>
    public IObjectStore Objects { get; }

    /// <summary>Where the live store of an estate opened from a root lives: under the per-user state, keyed by estate id and root path.</summary>
    public static string LiveStorePath(string root, string estateId, string profile) => EstateLocations.LiveStorePath(root, estateId, profile);

    /// <summary>Opens the estate at a root; the manifest decides the profile.</summary>
    public static EstateSession Open(string root)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        root = Path.GetFullPath(root);
        var findings = new FindingList();
        EstateManifest? manifest = EstateManifest.Load(root, findings);
        if (manifest is null)
        {
            throw new InvalidOperationException(string.Join("; ", findings.Select(f => f.Message)));
        }

        var live = new LiveStore(LiveStorePath(root, manifest.Estate, manifest.Profile));
        live.SetMeta("estate", manifest.Estate);
        live.SetMeta("profile", manifest.Profile);
        live.SetMeta("root", root);
        IObjectStore objects = manifest.Profile == "local-only"
            ? new SqliteObjectStore(Path.Combine(Path.GetDirectoryName(live.Path)!, "objects.db"))
            : new FileObjectStore(Path.Combine(root, EstateLayout.ObjectsDirectory));
        return new EstateSession(root, manifest, live, objects);
    }

    /// <summary>Loads and validates the estate. Repository-synced: the live store is refreshed from the record files first.</summary>
    public EstateModel Load()
    {
        if (Profile == "repository-synced")
        {
            Live.SyncFromDirectory(Root);
        }

        LiveStore.FileSnapshot files = Live.Snapshot();
        if (!ReferenceEquals(files, loadedFiles))
        {
            parseCache.Retain(files.ByPath);
            loadedFiles = files;
        }
        EstateModel model = EstateLoader.Load(Root, files.Files, parseCache);
        Live.SetMeta("lastLoadedAt", RecordParser.FormatTimestamp(DateTimeOffset.UtcNow));
        return model;
    }

    /// <summary>Stores a new record where the profile keeps records; never overwrites. Returns the record's path.</summary>
    public string Write(AuthoredRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        if (Profile == "repository-synced")
        {
            RecordAuthor.Write(Root, record);
        }
        else
        {
            if (Live.Has(record.RelativePath))
            {
                throw new IOException($"{record.RelativePath} already exists; records are never overwritten.");
            }

            Live.Put(record.RelativePath, record.Bytes);
        }

        return record.RelativePath;
    }

    /// <summary>Removes a record that was just written and turned out to make the estate invalid.</summary>
    public void Discard(AuthoredRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        if (Profile == "repository-synced")
        {
            string full = Path.Combine(Root, record.RelativePath.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(full))
            {
                File.Delete(full);
            }
        }

        Live.Remove(record.RelativePath);
    }

    /// <summary>Every record file the estate holds, in path order (from the repository or the live store).</summary>
    public IReadOnlyList<RecordFile> Files()
    {
        if (Profile == "repository-synced")
        {
            Live.SyncFromDirectory(Root);
        }

        return Live.Files();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        Objects.Dispose();
        Live.Dispose();
    }
}
