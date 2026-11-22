// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Collections.Frozen;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Globalization;
using Vericairn.Runtime.Workers;

namespace Vericairn.Adapters.Git;

/// <summary>
/// The configuration isolation of every git process the adapter starts on a repository Vericairn did not create, shared with the
/// worker's own git calls. The system and global configuration are ignored; a fresh directory Vericairn owns supplies the empty
/// global configuration and the hooks directory; and the settings below override any entry of the repository's own configuration
/// that names a program, a pager, a hooks directory or a transport. A process that reads the working tree goes further and reads it
/// through a git directory Vericairn writes (<see cref="WorkingTree"/>), so it never opens the repository's configuration at all. An
/// instance serves the git processes of one operation on one repository and removes its directory when disposed.
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
    /// Lists the working tree's top level, the repository's own git directory and its common directory, one per line: the input of
    /// <see cref="WorkingTree"/>.
    /// </summary>
    internal static readonly ImmutableArray<string> LayoutScan = ["rev-parse", "--show-toplevel", "--absolute-git-dir", "--git-common-dir"];

    /// <summary>
    /// Lists the settings of the repository's own configuration that a working-tree read carries: how files and names compare and
    /// whether the checkout is sparse, none naming a program. Git answers 1 when the repository sets none.
    /// </summary>
    internal static readonly ImmutableArray<string> CarriedScan =
        ["config", "-z", "--get-regexp", @"^core\.(ignorecase|precomposeunicode|filemode|symlinks|autocrlf|eol|checkstat|trustctime|sparsecheckout|sparsecheckoutcone)$"];

    private static readonly FrozenSet<string> CarriedNames = new[]
    {
        "core.ignorecase", "core.precomposeunicode", "core.filemode", "core.symlinks", "core.autocrlf", "core.eol", "core.checkstat", "core.trustctime",
        "core.sparsecheckout", "core.sparsecheckoutcone",
    }.ToFrozenSet(StringComparer.Ordinal);

    // The sparse-checkout settings are carried only as plain booleans.
    private static readonly FrozenSet<string> BooleanNames = new[] { "core.sparsecheckout", "core.sparsecheckoutcone" }.ToFrozenSet(StringComparer.Ordinal);

    private static readonly FrozenSet<string> Booleans = new[] { "true", "false", "yes", "no", "on", "off", "1", "0" }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    // The repository's exclude, attribute and sparse-checkout files are data copied into the working-tree read's git directory,
    // each read once within this bound and deadline.
    private const int MaximumInformationFile = 1024 * 1024;
    private static readonly TimeSpan InformationDeadline = TimeSpan.FromSeconds(10);

    // Ambient variables that could route a process to another repository, object store, index or configuration.
    private static readonly string[] Routing =
    [
        "GIT_DIR", "GIT_WORK_TREE", "GIT_COMMON_DIR", "GIT_OBJECT_DIRECTORY", "GIT_ALTERNATE_OBJECT_DIRECTORIES", "GIT_INDEX_FILE",
        "GIT_NAMESPACE", "GIT_SHALLOW_FILE", "GIT_GRAFT_FILE", "GIT_REPLACE_REF_BASE", "GIT_CONFIG", "GIT_CONFIG_PARAMETERS",
        "GIT_CONFIG_COUNT", "GIT_GLOB_PATHSPECS", "GIT_NOGLOB_PATHSPECS", "GIT_LITERAL_PATHSPECS", "GIT_ICASE_PATHSPECS",
    ];

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
    /// Writes a git directory inside this instance's directory for reading the repository's working tree: <paramref name="head"/> as
    /// a detached HEAD (an unborn branch when null), the repository's objects through an alternate, copies of its exclude, attribute
    /// and sparse-checkout files, and a configuration holding only the object format. The process that reads the working tree through it
    /// never opens the repository's own configuration, so no setting read by an earlier process, or changed since, decides what it
    /// runs; it reads the repository's index where it is and carries only the plain values <paramref name="carried"/> (the output of
    /// <see cref="CarriedScan"/>) names. Null when the layout (the output of <see cref="LayoutScan"/> run in
    /// <paramref name="start"/>) or one of those files cannot be read safely.
    /// </summary>
    internal GitWorkingTree? WorkingTree(string start, string layout, string? head, string carried)
    {
        ArgumentNullException.ThrowIfNull(layout);
        ArgumentNullException.ThrowIfNull(carried);
        string[] lines = [.. layout.Split('\n').Select(l => l.TrimEnd('\r')).Where(l => l.Length > 0)];
        if (lines.Length != 3 || !Path.IsPathFullyQualified(lines[0]) || !Path.IsPathFullyQualified(lines[1])
            || head is not null && !(head.Length is 40 or 64 && head.All(c => char.IsAsciiDigit(c) || c is >= 'a' and <= 'f'))) return null;
        try
        {
            string common = Path.GetFullPath(Path.Combine(start, lines[2]));
            string view = Path.Combine(Control, "view");
            Directory.CreateDirectory(Path.Combine(view, "objects", "info"));
            Directory.CreateDirectory(Path.Combine(view, "refs"));
            Directory.CreateDirectory(Path.Combine(view, "info"));
            // Where git itself reads them: exclude and attributes from the common directory, the sparse-checkout patterns from the
            // working tree's own git directory.
            foreach ((string directory, string name) in new[] { (common, "exclude"), (common, "attributes"), (Path.GetFullPath(lines[1]), "sparse-checkout") })
            {
                if (!CopyInformation(Path.Combine(directory, "info", name), Path.Combine(view, "info", name))) return null;
            }

            File.WriteAllText(Path.Combine(view, "HEAD"), head is null ? "ref: refs/heads/unborn\n" : head + "\n");
            File.WriteAllText(Path.Combine(view, "objects", "info", "alternates"), Path.Combine(common, "objects").Replace('\\', '/') + "\n");
            File.WriteAllText(Path.Combine(view, "config"), head?.Length == 64
                ? "[core]\n\trepositoryformatversion = 1\n\tbare = false\n[extensions]\n\tobjectformat = sha256\n"
                : "[core]\n\trepositoryformatversion = 0\n\tbare = false\n");
            List<string> options = ["--git-dir=" + view, "--work-tree=" + Path.GetFullPath(lines[0])];
            foreach (string setting in Carried(carried))
            {
                options.Add("-c");
                options.Add(setting);
            }

            return new(Path.GetFullPath(lines[0]), Path.Combine(Path.GetFullPath(lines[1]), "index"), options);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return null;
        }
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

    // An exclude, attribute or sparse-checkout file is data, copied only from a regular file within the bound. An absent or empty
    // file is no file; a pipe or device also reports no length and is not opened. The file is opened once and what was opened is
    // checked again, within a deadline, since the path may have changed after it was examined.
    private static bool CopyInformation(string source, string destination)
    {
        var file = new FileInfo(source);
        if (!file.Exists) return true;
        if (file.LinkTarget is not null || (file.Attributes & FileAttributes.ReparsePoint) != 0 || file.Length > MaximumInformationFile) return false;
        if (file.Length == 0) return true;
        Task<byte[]?> reading = Task.Run(() => ReadInformation(source));
        try
        {
            if (!reading.Wait(InformationDeadline) || reading.Result is not { } bytes) return false;
            File.WriteAllBytes(destination, bytes);
            return true;
        }
        catch (AggregateException)
        {
            return false;
        }
    }

    // The opened file's bytes, or null when it is not a regular file (one that cannot seek) or holds more than the bound.
    private static byte[]? ReadInformation(string source)
    {
        using var stream = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read, 1, FileOptions.None);
        if (!stream.CanSeek || stream.Length > MaximumInformationFile) return null;
        byte[] buffer = new byte[MaximumInformationFile + 1];
        int total = 0;
        for (int read; total < buffer.Length && (read = stream.Read(buffer, total, buffer.Length - total)) > 0;) total += read;
        return total > MaximumInformationFile ? null : buffer[..total];
    }

    // The carried settings whose value is a plain word; a name without a value is git's true.
    private static IEnumerable<string> Carried(string entries)
    {
        foreach (string entry in entries.Split('\0', StringSplitOptions.RemoveEmptyEntries))
        {
            int split = entry.IndexOf('\n', StringComparison.Ordinal);
            string name = split < 0 ? entry : entry[..split];
            string value = split < 0 ? "true" : entry[(split + 1)..];
            if (CarriedNames.Contains(name) && value.Length is > 0 and <= 16 && value.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_')
                && (!BooleanNames.Contains(name) || Booleans.Contains(value)))
                yield return name + "=" + value;
        }
    }

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

/// <summary>A working tree read through a git directory Vericairn writes (<see cref="GitHardening.WorkingTree"/>).</summary>
internal sealed record GitWorkingTree(string TopLevel, string Index, IReadOnlyList<string> Options);
