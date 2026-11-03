// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Vericairn.Protocol;
using Vericairn.Records.Signing;
using Vericairn.Runtime.Providers;

namespace Vericairn.Runtime.Workers;

/// <summary>
/// The local checkpoint between a prepare-only request and the run that takes its workspace up: where the workspace is, its commit and
/// its inventory as prepared. It is owner-only under the Vericairn home, outside every workspace, and never a record. The run checks
/// what changed since against the assignment's permitted changes, and removes it when the harness is dispatched.
/// </summary>
internal sealed class WorkspacePreparation
{
    private const long Limit = 64 * 1024 * 1024;
    private const int Entries = 100_000;
    private static readonly TimeSpan Bound = TimeSpan.FromSeconds(30);
    private const UnixFileMode Shared = UnixFileMode.GroupRead | UnixFileMode.GroupWrite | UnixFileMode.GroupExecute
        | UnixFileMode.OtherRead | UnixFileMode.OtherWrite | UnixFileMode.OtherExecute;

    private static readonly JsonSerializerOptions Json = new(ProtocolJson.Options) { AllowDuplicateProperties = false };

    /// <summary>The checkpoint of one repository's branch in one estate, wherever its workspace root is.</summary>
    internal WorkspacePreparation(string estate, string repository, string branch) =>
        Location = Path.Combine(VericairnHome.Path(), "estates", estate, "preparations",
            Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(repository + "\n" + branch))) + ".json");

    /// <summary>Where the checkpoint is kept on this machine; never recorded.</summary>
    internal string Location { get; }

    /// <summary>The workspace as it was prepared: its directory, the commit it was at, and its inventory then.</summary>
    internal sealed record Checkpoint(string Workspace, string Commit, WorkspaceSnapshot Snapshot);

    /// <summary>The checkpoint, or null when there is none. One that is linked, shared, unreadable or altered refuses.</summary>
    internal Checkpoint? Read()
    {
        try
        {
            if (Linked()) throw new IOException("it or its directory is a symbolic link or reparse point");
            if (!File.Exists(Location)) return null;
            if (!OperatingSystem.IsWindows() && (File.GetUnixFileMode(Location) & Shared) != 0)
                throw new IOException("it is readable or writable by other users");
            if (new FileInfo(Location).Length > Limit) throw new JsonException();
            Checkpoint checkpoint = JsonSerializer.Deserialize<Checkpoint>(File.ReadAllBytes(Location), Json) ?? throw new JsonException();
            if (string.IsNullOrEmpty(checkpoint.Workspace) || !Path.IsPathFullyQualified(checkpoint.Workspace)
                || checkpoint.Commit is not { Length: 40 or 64 } || !checkpoint.Commit.All(char.IsAsciiHexDigit)
                || checkpoint.Snapshot?.Files is null || checkpoint.Snapshot.Problem is not null)
                throw new JsonException();
            return checkpoint;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException or ArgumentException or NotSupportedException)
        {
            string why = exception is IOException { InnerException: null } ? exception.Message : "it is unreadable or was altered";
            throw new ProviderRefusedException($"The workspace preparation checkpoint '{Location}' cannot be trusted ({why}), so what changed in the workspace since it was prepared cannot be checked. Remove that file, then prepare the workspace again with vericairn worker run --prepare-only.", exception);
        }
    }

    /// <summary>Saves the workspace as prepared, replacing an earlier checkpoint, owner-only and without following links.</summary>
    internal void Save(PreparedWorkspace workspace, WorkspaceSnapshot snapshot)
    {
        RefuseLinks();
        string folder = Path.GetDirectoryName(Location)!;
        if (OperatingSystem.IsWindows()) Directory.CreateDirectory(folder);
        else Directory.CreateDirectory(folder, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        string temporary = Location + "." + Guid.NewGuid().ToString("N");
        try
        {
            var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write, Share = FileShare.None };
            if (!OperatingSystem.IsWindows()) options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
            using (var stream = new FileStream(temporary, options))
            {
                JsonSerializer.Serialize(stream, new Checkpoint(workspace.Path, workspace.Commit, snapshot), Json);
                stream.Flush(flushToDisk: true);
            }

            File.Move(temporary, Location, overwrite: true);
        }
        finally
        {
            File.Delete(temporary);
        }
    }

    /// <summary>Removes the checkpoint once its run is dispatched; nothing when there is none.</summary>
    internal void Remove()
    {
        RefuseLinks();
        File.Delete(Location);
    }

    /// <summary>Why a checkpoint no longer describes this branch's workspace, or null when it does.</summary>
    internal static string? Stale(Checkpoint checkpoint, PreparedWorkspace workspace, WorkspaceSnapshot current)
    {
        if (!string.Equals(Path.TrimEndingDirectorySeparator(checkpoint.Workspace), Path.TrimEndingDirectorySeparator(workspace.Path),
                OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
            return $"it was made for a workspace at '{checkpoint.Workspace}', and the branch's workspace is now '{workspace.Path}'";
        if (!workspace.Reused || (checkpoint.Snapshot.Identity is not null && checkpoint.Snapshot.Identity != current.Identity))
            return $"the workspace it was made for was removed or replaced, and '{workspace.Path}' is a different directory";
        return null;
    }

    /// <summary>
    /// Refuses a workspace that holds a link the reference inventory did not, or that changed since the reference outside the
    /// assignment's permitted paths, naming what it found; ignored files are not changes. Throws <see cref="ProviderRefusedException"/>.
    /// </summary>
    internal static void Check(IWorkspaceProvider provider, PreparedWorkspace workspace, string commit, WorkspaceSnapshot reference, IReadOnlyList<string> permitted, string since)
    {
        if (Link(workspace.Path, reference) is string link)
            throw new ProviderRefusedException($"The workspace at '{workspace.Path}' holds a symbolic link or reparse point at '{link}' {since}, and the agent starts in a workspace without new links. Install dependencies without links (npm, for example, with --no-bin-links).");

        // The provider compares from the reference's commit, so commits made since count as changes too.
        RunScope scope = provider.CheckScope(workspace with { Commit = commit }, reference, permitted);
        if (scope.Problem is not null)
            throw new ProviderRefusedException($"The workspace at '{workspace.Path}' could not be checked {since}: {scope.Problem}");
        if (scope.OutsideCount > 0)
            throw new ProviderRefusedException($"The workspace at '{workspace.Path}' changed outside the assignment's permitted changes {since}: "
                + string.Join(", ", scope.Outside) + (scope.OutsideCount > scope.Outside.Count ? ", and more" : string.Empty)
                + ". Dependencies belong in paths the repository ignores; restore the rest, then run again.");
    }

    /// <summary>The first link in the workspace that the reference inventory does not name, or null. Its own .git is not searched.</summary>
    private static string? Link(string workspace, WorkspaceSnapshot reference)
    {
        var pending = new Stack<(string Path, string Name)>();
        pending.Push((workspace, string.Empty));
        var watch = Stopwatch.StartNew();
        int count = 0;
        while (pending.TryPop(out (string Path, string Name) directory))
        {
            foreach (string entry in Directory.EnumerateFileSystemEntries(directory.Path))
            {
                if (++count > Entries || watch.Elapsed > Bound)
                    throw new ProviderRefusedException($"The workspace at '{workspace}' holds more than {Entries} entries or could not be searched for links within {Bound.TotalSeconds} seconds.");
                string name = directory.Name + Path.GetFileName(entry);
                if (name == ".git") continue;
                FileAttributes attributes = File.GetAttributes(entry);
                if ((attributes & FileAttributes.ReparsePoint) != 0)
                {
                    if (!reference.Files.ContainsKey(name)) return name;
                }
                else if ((attributes & FileAttributes.Directory) != 0)
                {
                    pending.Push((entry, name + "/"));
                }
            }
        }

        return null;
    }

    private bool Linked() => new[] { Location, Path.GetDirectoryName(Location)! }
        .Any(path => new FileInfo(path).LinkTarget is not null || new DirectoryInfo(path).LinkTarget is not null);

    private void RefuseLinks()
    {
        if (Linked()) throw new IOException($"The workspace preparation checkpoint '{Location}' or its directory is a symbolic link or reparse point; remove it.");
    }
}
