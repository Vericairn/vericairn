// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Vericairn.Records.Hashing;
using Vericairn.Records.Signing;
using Vericairn.Runtime.Providers;

namespace Vericairn.Runtime.Workers;

/// <summary>A local checkpoint across prepare-only and run. It is outside the workspace and creates no estate record.</summary>
internal sealed class WorkspacePreparation(string estate, string assignment, string repository, string branch)
{
    private readonly string path = Path.Combine(VericairnHome.Path(), "preparations", ContentHash.Sha256(Encoding.UTF8.GetBytes(
        estate + "\n" + assignment + "\n" + repository + "\n" + branch))[7..] + ".json");

    internal sealed record Checkpoint(string Commit, WorkspaceSnapshot Snapshot);

    internal Checkpoint? Read()
    {
        try
        {
            RefuseLink(path);
            if (!File.Exists(path)) return null;
            if (new FileInfo(path).Length > 64 * 1024 * 1024) throw new JsonException();
            Checkpoint checkpoint = JsonSerializer.Deserialize<Checkpoint>(File.ReadAllBytes(path)) ?? throw new JsonException();
            if (string.IsNullOrEmpty(checkpoint.Commit) || checkpoint.Snapshot?.Files is null) throw new JsonException();
            return checkpoint;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            throw new ProviderRefusedException("The prepared workspace checkpoint is unreadable; preserve the workspace and repair its local preparation state before retrying.", exception);
        }
    }

    internal void Save(PreparedWorkspace workspace, WorkspaceSnapshot snapshot)
    {
        if (Read() is not null) return;
        RefuseLink(Path.GetDirectoryName(path)!);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string temporary = path + "." + Guid.NewGuid().ToString("N");
        try
        {
            using (var stream = new FileStream(temporary, new FileStreamOptions
            {
                Mode = FileMode.CreateNew, Access = FileAccess.Write,
                UnixCreateMode = OperatingSystem.IsWindows() ? null : UnixFileMode.UserRead | UnixFileMode.UserWrite,
            })) JsonSerializer.Serialize(stream, new Checkpoint(workspace.Commit, snapshot));
            File.Move(temporary, path, overwrite: false);
        }
        finally { File.Delete(temporary); }
    }

    internal void Complete() => File.Delete(path);

    internal static void Check(IWorkspaceProvider provider, PreparedWorkspace workspace, Checkpoint checkpoint, IReadOnlyList<string> permitted)
    {
        CheckLinks(workspace.Path);
        RunScope scope = provider.CheckScope(workspace with { Commit = checkpoint.Commit }, checkpoint.Snapshot, permitted);
        if (scope.Problem is not null || scope.OutsideCount != 0)
            throw new ProviderRefusedException($"Workspace '{workspace.Path}' failed preparation checks: "
                + (scope.Problem ?? "changes outside the assignment's permitted paths: " + string.Join(", ", scope.Outside)));
    }

    private static void CheckLinks(string workspace)
    {
        var pending = new Stack<string>();
        pending.Push(workspace);
        var watch = Stopwatch.StartNew();
        int count = 0;
        while (pending.TryPop(out string? directory))
        {
            RefuseLink(directory);
            foreach (string entry in Directory.EnumerateFileSystemEntries(directory))
            {
                if (++count > 100000 || watch.Elapsed > TimeSpan.FromSeconds(30))
                    throw new ProviderRefusedException($"Workspace '{workspace}' exceeded the preparation inspection bound.");
                RefuseLink(entry);
                if (Directory.Exists(entry) && Path.GetFileName(entry) != ".git") pending.Push(entry);
            }
        }
    }

    private static void RefuseLink(string path)
    {
        if (new FileInfo(path).LinkTarget is not null || new DirectoryInfo(path).LinkTarget is not null)
            throw new ProviderRefusedException($"Path '{path}' is a symbolic link or reparse point; workspace preparation requires ordinary entries.");
    }
}
