// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using Vericairn.Records.Identity;
using Vericairn.Runtime.Workers;

namespace Vericairn.Runtime.Tests;

/// <summary>
/// A writer that does not take a working tree's write lock, as a repository sync bringing in another clone's records does: it
/// writes in a copy of the tree, and the copy's new and changed files are then synced into the tree. From 0.9.2 a runtime writing the
/// tree itself waits for the lock instead (FIX-99), so tests of changes arriving during a write use this.
/// </summary>
internal static class UnlockedWriter
{
    internal static T Sync<T>(string root, Func<string, T> write)
    {
        string clone = Path.Combine(Path.GetTempPath(), "vericairn-unlocked-" + Ulid.NewUlid().Value);
        try
        {
            foreach (string file in Files(root))
            {
                string target = Path.Combine(clone, Path.GetRelativePath(root, file));
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.Copy(file, target);
            }

            T result = write(clone);
            foreach (string file in Files(clone))
            {
                string target = Path.Combine(root, Path.GetRelativePath(clone, file));
                byte[] bytes = File.ReadAllBytes(file);
                if (File.Exists(target) && File.ReadAllBytes(target).AsSpan().SequenceEqual(bytes)) continue;
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.WriteAllBytes(target, bytes);
            }

            return result;
        }
        finally
        {
            try { WorkspaceCleanup.Delete(clone); }
            catch (IOException) { /* Best effort: a store still closing keeps its files a moment longer. */ }
        }
    }

    // The tree's tracked content: never its machine-local .vericairn directory or a repository's .git.
    private static IEnumerable<string> Files(string root) =>
        Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .Where(f => Path.GetRelativePath(root, f).Split(Path.DirectorySeparatorChar)[0] is not (".vericairn" or ".git"));
}
