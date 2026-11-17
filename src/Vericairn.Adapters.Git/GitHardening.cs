// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Collections.Immutable;
using System.Diagnostics;
using System.Globalization;
using Vericairn.Runtime.Workers;

namespace Vericairn.Adapters.Git;

/// <summary>
/// The configuration isolation of every git process the adapter starts on a repository Vericairn did not create, shared with the
/// worker's own git calls. The system and global configuration are ignored; a fresh directory Vericairn owns supplies the empty
/// global configuration and the hooks directory; and the settings below override any entry of the repository's own configuration
/// that names a program, a pager, a hooks directory or a transport. An instance serves the git processes of one operation on one
/// repository and removes its directory when disposed.
/// </summary>
internal sealed class GitHardening : IDisposable
{
    /// <summary>The settings every hardened git process runs with, after the hooks directory.</summary>
    internal static readonly ImmutableArray<string> Settings =
    [
        "core.fsmonitor=false", "core.untrackedCache=false", "core.pager=", "core.sshCommand=", "core.alternateRefsCommand=",
        "diff.external=", "diff.trustExitCode=false", "log.showSignature=false", "protocol.allow=never", "gc.auto=0",
        "maintenance.auto=false", "fetch.writeCommitGraph=false", "submodule.recurse=false",
        // Configuration isolation cannot inherit a Windows installation's long-path opt-in. Staging refs can exceed MAX_PATH even
        // when the published workspace fits; keep this non-executing option explicit.
        "core.longpaths=true",
    ];

    /// <summary>
    /// Lists the filter drivers the repository's own configuration defines; git answers 1 when it defines none. Run it with this
    /// instance before a command that reads working-tree files through the index, and pass the output to
    /// <see cref="NeutralizeFilters"/>.
    /// </summary>
    internal static readonly ImmutableArray<string> FilterScan = ["config", "--name-only", "-z", "--get-regexp", @"^filter\."];

    // A repository needs very few filter drivers; a configuration naming more, longer names or a name that no -c option can state
    // (one holding '=') is refused rather than partly carried.
    private const int MaximumFilters = 32;
    private const int MaximumFilterName = 200;

    // Ambient variables that could route a process to another repository, object store, index or configuration.
    private static readonly string[] Routing =
    [
        "GIT_DIR", "GIT_WORK_TREE", "GIT_COMMON_DIR", "GIT_OBJECT_DIRECTORY", "GIT_ALTERNATE_OBJECT_DIRECTORIES", "GIT_INDEX_FILE",
        "GIT_NAMESPACE", "GIT_SHALLOW_FILE", "GIT_GRAFT_FILE", "GIT_REPLACE_REF_BASE", "GIT_CONFIG", "GIT_CONFIG_PARAMETERS",
        "GIT_CONFIG_COUNT", "GIT_GLOB_PATHSPECS", "GIT_NOGLOB_PATHSPECS", "GIT_LITERAL_PATHSPECS", "GIT_ICASE_PATHSPECS",
    ];

    private readonly List<string> filters = [];

    private GitHardening(string control) => Control = control;

    /// <summary>The directory Vericairn owns for this operation: an empty global configuration and a hooks path with no hooks.</summary>
    internal string Control { get; }

    private string Hooks => Path.Combine(Control, "hooks");

    private string GlobalConfiguration => Path.Combine(Control, "config");

    /// <summary>Creates the owned directory; an <see cref="IOException"/> or <see cref="UnauthorizedAccessException"/> means no git may start.</summary>
    internal static GitHardening Create()
    {
        string control = Path.Combine(Path.GetTempPath(), "vericairn-git-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(control);
        var hardening = new GitHardening(control);
        try
        {
            File.WriteAllText(hardening.GlobalConfiguration, string.Empty);
            return hardening;
        }
        catch
        {
            hardening.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Empties every filter driver named in <paramref name="names"/> (the output of <see cref="FilterScan"/>) for the later processes
    /// of this instance, so reading the working tree runs no clean or long-running filter. False when the configuration names more
    /// drivers, longer names, or a name holding '=', than can be carried; the caller then starts no command that reads working-tree
    /// files.
    /// </summary>
    internal bool NeutralizeFilters(string names)
    {
        ArgumentNullException.ThrowIfNull(names);
        const string section = "filter.";
        var drivers = new SortedSet<string>(StringComparer.Ordinal);
        foreach (string name in names.Split('\0', StringSplitOptions.RemoveEmptyEntries))
        {
            // filter.<driver>.<key>: the driver's name may hold dots, or be empty; a two-level filter.<key> names no driver.
            int key = name.LastIndexOf('.');
            if (!name.StartsWith(section, StringComparison.Ordinal) || key < section.Length) continue;
            drivers.Add(name[section.Length..key]);
        }

        if (drivers.Count > MaximumFilters || drivers.Any(d => d.Length > MaximumFilterName || d.Contains('=', StringComparison.Ordinal))) return false;
        foreach (string driver in drivers.Where(d => !filters.Contains(section + d + ".required=false")))
        {
            // An empty command is no command, and a driver that is not required may be absent.
            filters.Add(section + driver + ".clean=");
            filters.Add(section + driver + ".smudge=");
            filters.Add(section + driver + ".process=");
            filters.Add(section + driver + ".required=false");
        }

        return true;
    }

    /// <summary>Adds the hardened options, then <paramref name="arguments"/>, and sets the hardened environment.</summary>
    internal void Apply(ProcessStartInfo info, IEnumerable<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(info);
        ArgumentNullException.ThrowIfNull(arguments);
        info.ArgumentList.Add("--no-pager");
        foreach (string setting in Configuration())
        {
            info.ArgumentList.Add("-c");
            info.ArgumentList.Add(setting);
        }

        foreach (string argument in arguments) info.ArgumentList.Add(argument);
        SetEnvironment(info, []);
    }

    /// <summary>
    /// Sets the hardened environment for a program that starts git itself, such as the secret scanner: the same settings travel in
    /// git's configuration environment, which every git process it starts reads.
    /// </summary>
    internal void ApplyEnvironment(ProcessStartInfo info)
    {
        ArgumentNullException.ThrowIfNull(info);
        SetEnvironment(info, [.. Configuration()]);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        try
        {
            if (Directory.Exists(Control)) WorkspaceCleanup.Delete(Control);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            // Best effort: the directory holds only an empty configuration file; its hooks path never existed.
        }
    }

    // The hooks directory, the shared settings and any emptied filter drivers, each as key=value.
    private IEnumerable<string> Configuration() => [.. Settings.Prepend("core.hooksPath=" + Hooks), .. filters];

    private void SetEnvironment(ProcessStartInfo info, List<string> configuration)
    {
        foreach (string name in Routing) info.Environment.Remove(name);
        foreach (string name in info.Environment.Keys.Where(Indexed).ToArray()) info.Environment.Remove(name);
        info.Environment["GIT_CONFIG_NOSYSTEM"] = "1";
        info.Environment["GIT_CONFIG_GLOBAL"] = GlobalConfiguration;
        info.Environment["GIT_ATTR_NOSYSTEM"] = "1";
        info.Environment["GIT_TERMINAL_PROMPT"] = "0";
        info.Environment["GIT_OPTIONAL_LOCKS"] = "0";
        info.Environment["GIT_NO_REPLACE_OBJECTS"] = "1";
        info.Environment["GIT_NO_LAZY_FETCH"] = "1";
        // Inherited configuration entries are dropped; a program that starts git itself receives the hardening here instead.
        info.Environment["GIT_CONFIG_COUNT"] = configuration.Count.ToString(CultureInfo.InvariantCulture);
        for (int i = 0; i < configuration.Count; i++)
        {
            string[] setting = configuration[i].Split('=', 2);
            info.Environment["GIT_CONFIG_KEY_" + i.ToString(CultureInfo.InvariantCulture)] = setting[0];
            info.Environment["GIT_CONFIG_VALUE_" + i.ToString(CultureInfo.InvariantCulture)] = setting[1];
        }
    }

    private static bool Indexed(string name) =>
        name.StartsWith("GIT_CONFIG_KEY_", StringComparison.OrdinalIgnoreCase) || name.StartsWith("GIT_CONFIG_VALUE_", StringComparison.OrdinalIgnoreCase);
}
