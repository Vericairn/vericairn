// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.ComponentModel;
using System.Diagnostics;

namespace Vericairn.Records.Processes;

/// <summary>
/// From 0.9.4 (FIX-161), how every program the product starts is chosen: by one absolute path, decided here before the start. A bare
/// name is looked up only in the absolute directories of PATH, in order; empty and relative entries and the current directory take no
/// part, and on Windows the name gains the program extensions PATHEXT lists. An absolute path, such as a configured harness path, is
/// used as given. A relative path, or a name no absolute directory of PATH holds, is refused before anything starts. The start then
/// names that absolute path, so neither the platform's own search nor the directory the process happens to run in chooses what runs.
/// The program receives PATH with its empty and relative entries removed, so what it starts by name in turn is found the same way.
/// </summary>
internal static class ProgramPath
{
    // What PATHEXT holds on a default Windows installation, used when it is unset.
    private const string DefaultExtensions = ".COM;.EXE;.BAT;.CMD";

    // A bare name resolves only to an image the system loads directly, as a start by name always did: .exe, which CreateProcess appends
    // to a name without an extension, and .com. A script (.bat, .cmd) runs through the command interpreter under other quoting rules,
    // so it is started only when it is named with its extension.
    private static readonly string[] Images = [".COM", ".EXE"];

    // Extensions a name is taken to carry already, whatever PATHEXT lists.
    private static readonly string[] Named = [".COM", ".EXE", ".BAT", ".CMD"];

    private static readonly char[] WindowsSeparators = ['/', '\\'];
    private static readonly char[] UnixSeparators = ['/'];

    /// <summary>The absolute path <paramref name="program"/> starts as on this machine, by this process's PATH (and PATHEXT on Windows).</summary>
    /// <exception cref="ProgramNotFoundException">The name is empty, a relative path, or held by no absolute directory of PATH.</exception>
    internal static string Resolve(string program) => Resolve(program, Environment.GetEnvironmentVariable("PATH"));

    /// <summary>The absolute path <paramref name="program"/> starts as on this machine with <paramref name="path"/> as PATH.</summary>
    internal static string Resolve(string program, string? path) =>
        Resolve(program, OperatingSystem.IsWindows(), path, Environment.GetEnvironmentVariable("PATHEXT"), IsProgram);

    /// <summary>The absolute path <paramref name="program"/> starts as, or null when <see cref="Resolve(string)"/> would refuse it.</summary>
    internal static string? TryResolve(string program)
    {
        try
        {
            return Resolve(program);
        }
        catch (ProgramNotFoundException)
        {
            return null;
        }
    }

    /// <summary>
    /// Starts <paramref name="info"/> by its program's absolute path: <see cref="ProcessStartInfo.FileName"/> is replaced by what
    /// <see cref="Resolve(string)"/> finds for it, and nothing else about the start changes.
    /// </summary>
    /// <exception cref="ProgramNotFoundException">The program cannot be resolved; nothing was started.</exception>
    internal static Process? Start(ProcessStartInfo info)
    {
        ArgumentNullException.ThrowIfNull(info);
        info.FileName = Resolve(info.FileName);
        NarrowSearchPath(info);
        return Process.Start(info);
    }

    /// <summary>
    /// <paramref name="program"/> read against <paramref name="directory"/>: a relative path with a directory part, such as
    /// ./scripts/setup.sh or scripts\setup.cmd, becomes the absolute path it names there; a bare name and an absolute path are returned as
    /// given, for <see cref="Resolve(string)"/> to look up or use. So is a Windows path rooted only on the current drive (\tools\x.exe) or
    /// relative to a drive's current directory (C:x.exe), which names no one file and is refused there.
    /// </summary>
    internal static string InDirectory(string program, string directory)
    {
        ArgumentNullException.ThrowIfNull(directory);
        bool windows = OperatingSystem.IsWindows();
        string name = windows ? Unquote(program ?? string.Empty) : program ?? string.Empty;
        return RelativeWithDirectory(name, windows) ? Path.GetFullPath(Path.Combine(directory, name)) : program ?? string.Empty;
    }

    /// <summary>
    /// Why <paramref name="program"/> can never be started however it is read, or null when it can: it names nothing, names a directory, or
    /// on Windows is rooted only on the current drive or relative to a drive's current directory.
    /// </summary>
    internal static string? NeverStarts(string program) => NeverStarts(program, OperatingSystem.IsWindows());

    /// <summary><see cref="NeverStarts(string)"/> for a platform (<paramref name="windows"/>).</summary>
    internal static string? NeverStarts(string program, bool windows)
    {
        string name = windows ? Unquote(program ?? string.Empty) : program ?? string.Empty;
        if (string.IsNullOrWhiteSpace(name))
        {
            return "it names no program.";
        }

        static bool Separator(char c, bool windows) => c == '/' || (windows && c == '\\');
        string last = name.Split(windows ? WindowsSeparators : UnixSeparators)[^1];
        if (Separator(name[^1], windows) || last is "." or "..")
        {
            return "it names a directory, never a program.";
        }

        if (windows && !FullyQualified(name, windows) && (Separator(name[0], windows) || name.Contains(':', StringComparison.Ordinal)))
        {
            return "it is rooted only on the current drive or relative to a drive's current directory, so it names no one file; give an absolute path, a path relative to the workspace, or a bare name found in an absolute directory of PATH.";
        }

        return null;
    }

    // A relative path with a directory part: not absolute, holding a separator, and on Windows neither rooted on the current drive nor
    // naming a drive.
    internal static bool RelativeWithDirectory(string name, bool windows) =>
        !string.IsNullOrEmpty(name) && !FullyQualified(name, windows)
        && (windows ? name.IndexOfAny(['/', '\\']) > 0 && !name.Contains(':', StringComparison.Ordinal) : name.Contains('/', StringComparison.Ordinal));

    /// <summary>
    /// The search path a started program receives. When <paramref name="info"/>'s environment holds this process's own PATH, as it does
    /// unless its caller set another, that PATH loses its empty and relative entries, its absolute entries kept in their order, so a
    /// program that starts another by name finds it only in an absolute directory too. A PATH the caller set is kept as given. With no
    /// absolute entry left PATH is removed, since an empty PATH also names the current directory to a POSIX search.
    /// </summary>
    internal static void NarrowSearchPath(ProcessStartInfo info)
    {
        ArgumentNullException.ThrowIfNull(info);
        NarrowSearchPath(info.Environment, Environment.GetEnvironmentVariable("PATH"), OperatingSystem.IsWindows());
    }

    /// <summary><see cref="NarrowSearchPath(ProcessStartInfo)"/> for an environment, this process's own PATH and a platform.</summary>
    internal static void NarrowSearchPath(IDictionary<string, string?> environment, string? own, bool windows)
    {
        ArgumentNullException.ThrowIfNull(environment);
        string? key = environment.Keys.FirstOrDefault(name => string.Equals(name, "PATH", windows ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal));
        if (key is null || environment[key] is not string value || !string.Equals(value, own, StringComparison.Ordinal))
        {
            return;
        }

        string narrowed = string.Join(windows ? ';' : ':', Entries(value, windows).Where(entry => entry.Directory is not null).Select(entry => entry.Text));
        if (narrowed.Length == 0)
        {
            environment.Remove(key);
        }
        else
        {
            environment[key] = narrowed;
        }
    }

    /// <summary>
    /// The resolution itself, for a platform (<paramref name="windows"/>), a PATH value, a PATHEXT value and a test of whether a path holds
    /// a program this process may start. Only absolute candidates are ever tested.
    /// </summary>
    internal static string Resolve(string program, bool windows, string? path, string? extensions, Func<string, bool> isProgram)
    {
        ArgumentNullException.ThrowIfNull(isProgram);
        string name = windows ? Unquote(program ?? string.Empty) : program ?? string.Empty;
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ProgramNotFoundException("No program was named, so nothing was started.");
        }

        if (FullyQualified(name, windows))
        {
            return name;
        }

        if (name.Contains('/', StringComparison.Ordinal) || (windows && (name.Contains('\\', StringComparison.Ordinal) || name.Contains(':', StringComparison.Ordinal))))
        {
            throw new ProgramNotFoundException($"{name} is a relative path, so it was not started: a program is named by its absolute path, or by a bare name found in an absolute directory of PATH.");
        }

        string[] names = Candidates(name, windows, extensions);
        foreach (string directory in Directories(path, windows))
        {
            foreach (string candidate in names)
            {
                string full = Join(directory, candidate, windows);
                if (isProgram(full))
                {
                    return full;
                }
            }
        }

        throw new ProgramNotFoundException(windows
            ? $"{name} was not found in an absolute directory of PATH, so it was not started: a bare name is found as a program PATHEXT lists (.exe or .com) in an absolute directory of PATH, and a script is named with its extension or by its absolute path."
            : $"{name} was not found in an absolute directory of PATH, so it was not started: a bare name is found only in an absolute directory of PATH, or the program is named by its absolute path.");
    }

    // The absolute entries of PATH, in order. An empty entry means the current directory to a shell, and a relative entry is read
    // against it, so both are skipped, as is anything else that does not name one directory from a root.
    private static IEnumerable<string> Directories(string? path, bool windows) =>
        Entries(path, windows).Select(entry => entry.Directory).OfType<string>();

    // Each entry of PATH as written, with the directory it names when that is absolute, else null.
    private static IEnumerable<(string Text, string? Directory)> Entries(string? path, bool windows)
    {
        foreach (string entry in (path ?? string.Empty).Split(windows ? ';' : ':'))
        {
            string directory = windows ? Unquote(entry) : entry;
            yield return (entry, directory.Length > 0 && FullyQualified(directory, windows) ? directory : null);
        }
    }

    private static string[] Candidates(string name, bool windows, string? extensions)
    {
        if (!windows)
        {
            return [name];
        }

        string[] listed = (string.IsNullOrWhiteSpace(extensions) ? DefaultExtensions : extensions)
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(extension => extension.Length > 1 && extension[0] == '.' && extension.IndexOfAny(['.', '\\', '/', ':', '"'], 1) < 0)
            .ToArray();
        int dot = name.LastIndexOf('.');
        string extension = dot > 0 ? name[dot..] : string.Empty;
        if (extension.Length > 1 && (listed.Contains(extension, StringComparer.OrdinalIgnoreCase) || Named.Contains(extension, StringComparer.OrdinalIgnoreCase)))
        {
            return [name];
        }

        List<string> images = listed.Where(e => Images.Contains(e, StringComparer.OrdinalIgnoreCase)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (!images.Contains(".EXE", StringComparer.OrdinalIgnoreCase))
        {
            images.Add(".exe");
        }

        return images.Select(e => name + e).ToArray();
    }

    // Absolute from a root: on Windows a drive letter with its separator (C:\), or a UNC or device path (\\server\share, \\?\); a path
    // that is rooted only on the current drive (\tools) or relative to a drive's current directory (C:tools) is not.
    private static bool FullyQualified(string path, bool windows)
    {
        if (!windows)
        {
            return path.StartsWith('/');
        }

        static bool Separator(char c) => c is '\\' or '/';
        return (path.Length >= 3 && char.IsAsciiLetter(path[0]) && path[1] == ':' && Separator(path[2]))
            || (path.Length >= 2 && Separator(path[0]) && (Separator(path[1]) || path[1] == '?'));
    }

    private static string Join(string directory, string name, bool windows)
    {
        char separator = windows ? '\\' : '/';
        return directory.EndsWith(separator) || (windows && directory.EndsWith('/')) ? directory + name : directory + separator + name;
    }

    // Windows reads a name and a PATH entry without surrounding whitespace or quotes, as Process.Start and the command interpreter do.
    private static string Unquote(string value)
    {
        string trimmed = value.Trim();
        return trimmed.Length >= 2 && trimmed[0] == '"' && trimmed[^1] == '"' ? trimmed[1..^1].Trim() : trimmed;
    }

    // A regular file this process may start: on Linux and macOS one with an execute permission, as the platform's own search requires.
    private static bool IsProgram(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                return false;
            }

            return OperatingSystem.IsWindows()
                || (File.GetUnixFileMode(path) & (UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute)) != 0;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return false;
        }
    }
}

/// <summary>
/// From 0.9.4 (FIX-161): a program that cannot be resolved to an absolute path, so nothing was started. It is the "file not found" a
/// failed start reports, so every caller that handles a start that fails handles this one the same way.
/// </summary>
internal sealed class ProgramNotFoundException(string message) : Win32Exception(2, message);
