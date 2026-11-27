// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Collections.Immutable;
using System.Diagnostics;
using System.Globalization;
using Vericairn.Runtime.Workers;

namespace Vericairn.Adapters.Git;

/// <summary>Isolates Git's process environment and owns its temporary configuration and read views.</summary>
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

    private string? acquisitionSource;
    private (AcquisitionGitView View, AcquisitionConfiguration Settings) acquisition;
    private GitWorkingTree? workingCopy;

    private GitHardening(string control) => Control = control;

    internal AcquisitionGitView Acquire(string source)
    {
        if (acquisitionSource is null)
        {
            acquisition = new GitCommandAdapter().Acquire(this, source, null);
            acquisitionSource = source;
        }
        else if (acquisitionSource != source)
        {
            throw new InvalidOperationException("One acquisition may inspect only one repository.");
        }

        return acquisition.View;
    }

    internal GitWorkingTree AcquireWorkingTree(string source)
    {
        AcquisitionGitView view = Acquire(source);
        return workingCopy ??= new GitCommandAdapter().WorkingTree(this, view, acquisition.Settings);
    }

    /// <summary>The directory Vericairn owns for this operation: an empty global configuration and a hooks path with no hooks.</summary>
    internal string Control { get; }

    private string Hooks => Path.Combine(Control, "hooks");

    private string GlobalConfiguration => Path.Combine(Control, "config");

    /// <summary>Creates the owned directory; an <see cref="IOException"/> or <see cref="UnauthorizedAccessException"/> means no git may start.</summary>
    internal static GitHardening Create()
    {
        string control = Path.Combine(Path.GetTempPath(), "vericairn-git-" + Guid.NewGuid().ToString("N"));
        if (OperatingSystem.IsWindows()) Directory.CreateDirectory(control);
        else Directory.CreateDirectory(control, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
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

    /// <summary>Adds the hardened options, then <paramref name="arguments"/>, and sets the hardened environment.</summary>
    internal void Apply(ProcessStartInfo info, IEnumerable<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(info);
        ArgumentNullException.ThrowIfNull(arguments);
        info.ArgumentList.Add("--no-pager");
        info.ArgumentList.Add("-c");
        info.ArgumentList.Add("core.attributesFile=" + GlobalConfiguration);
        info.ArgumentList.Add("-c");
        info.ArgumentList.Add("core.excludesFile=" + GlobalConfiguration);
        foreach (string setting in Configuration())
        {
            info.ArgumentList.Add("-c");
            info.ArgumentList.Add(setting);
        }

        foreach (string argument in arguments) info.ArgumentList.Add(argument);
        SetEnvironment(info, []);
    }

    /// <summary>Adds the hardened options and <paramref name="arguments"/> for a process that reads <paramref name="tree"/>'s working tree.</summary>
    internal void Apply(ProcessStartInfo info, GitWorkingTree tree, IEnumerable<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(info);
        ArgumentNullException.ThrowIfNull(tree);
        info.WorkingDirectory = tree.TopLevel;
        Apply(info, [.. tree.Options, .. arguments]);
        info.Environment["GIT_INDEX_FILE"] = tree.Index;
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

    /// <summary>
    /// Sets the hardened environment as <see cref="ApplyEnvironment(ProcessStartInfo)"/> does, with the caller's own settings (each
    /// key=value, none naming a program) carried after the hardening's.
    /// </summary>
    internal void ApplyEnvironment(ProcessStartInfo info, IEnumerable<string> additional)
    {
        ArgumentNullException.ThrowIfNull(info);
        ArgumentNullException.ThrowIfNull(additional);
        SetEnvironment(info, [.. Configuration(), .. additional]);
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
            // Best effort: the directory holds only files Vericairn wrote; its hooks path never existed.
        }
    }

    // The hooks directory and the shared settings, each as key=value.
    private IEnumerable<string> Configuration() => Settings.Prepend("core.hooksPath=" + Hooks);

    private void SetEnvironment(ProcessStartInfo info, List<string> configuration)
    {
        foreach (string name in info.Environment.Keys.Where(name => name.StartsWith("GIT_", StringComparison.OrdinalIgnoreCase)).ToArray())
            info.Environment.Remove(name);
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

}

/// <summary>A working tree and index copied into a Git read view Vericairn owns.</summary>
internal sealed record GitWorkingTree(string TopLevel, string Index, IReadOnlyList<string> Options);
