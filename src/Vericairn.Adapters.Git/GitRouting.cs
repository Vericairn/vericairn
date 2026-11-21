// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Diagnostics;

namespace Vericairn.Adapters.Git;

/// <summary>
/// The inherited git variables that no process of a checked push passes on, whether the push adapter or the secret scan starts it
/// (FIX-150): each could point git at another repository, object store, history or configuration than the clone's own, or send what
/// git reads elsewhere. Both take the names from this one list, so the scan reads the history the push carries.
/// </summary>
internal static class GitRouting
{
    /// <summary>The names, and with them every indexed <c>GIT_CONFIG_KEY_</c> and <c>GIT_CONFIG_VALUE_</c> name (<see cref="Remove"/>).</summary>
    internal static readonly string[] Names =
    [
        // Which repository, work tree and index a process finds, and where its search for one stops.
        "GIT_DIR", "GIT_WORK_TREE", "GIT_COMMON_DIR", "GIT_INDEX_FILE", "GIT_NAMESPACE", "GIT_CEILING_DIRECTORIES", "GIT_DISCOVERY_ACROSS_FILESYSTEM",
        // Which objects it reads, and which parents a commit has: a shallow file names commits whose parents git then ignores.
        "GIT_OBJECT_DIRECTORY", "GIT_ALTERNATE_OBJECT_DIRECTORIES", "GIT_SHALLOW_FILE", "GIT_GRAFT_FILE", "GIT_REPLACE_REF_BASE", "GIT_NO_REPLACE_OBJECTS",
        // Configuration given through the environment.
        "GIT_CONFIG", "GIT_CONFIG_PARAMETERS", "GIT_CONFIG_COUNT",
        // On Windows, files that take the place of a process's standard streams, so that a reader of its output would read nothing.
        "GIT_REDIRECT_STDIN", "GIT_REDIRECT_STDOUT", "GIT_REDIRECT_STDERR",
    ];

    /// <summary>Removes every inherited routing name from the process's environment, before the caller sets its own values.</summary>
    internal static void Remove(ProcessStartInfo info)
    {
        foreach (string name in Names) info.Environment.Remove(name);
        foreach (string name in info.Environment.Keys.Where(Indexed).ToArray()) info.Environment.Remove(name);
    }

    private static bool Indexed(string name) =>
        name.StartsWith("GIT_CONFIG_KEY_", StringComparison.OrdinalIgnoreCase) || name.StartsWith("GIT_CONFIG_VALUE_", StringComparison.OrdinalIgnoreCase);
}
