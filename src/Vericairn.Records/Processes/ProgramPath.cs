// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.ComponentModel;
using System.Diagnostics;
using System.Text;

namespace Vericairn.Records.Processes;

/// <summary>
/// From 0.9.4 (FIX-161), how every program the product starts is chosen: by one absolute path, decided here before the start. A bare
/// name is looked up only in the absolute directories of PATH, in order; empty and relative entries and the current directory take no
/// part, and on Windows the name gains the program extensions PATHEXT lists. An absolute path, such as a configured harness path, is
/// used as given. A relative path, or a name no absolute directory of PATH holds, is refused before anything starts. The start then
/// names that absolute path, so neither the platform's own search nor the directory the process happens to run in chooses what runs.
/// The program receives PATH with its empty and relative entries removed, so what it starts by name in turn is found the same way.
/// From 0.9.4 (FIX-165), on Windows an absolute path without an extension names the .exe, else the .com, of that name in that same
/// folder, and a batch program (.cmd, .bat) starts through the system directory's cmd.exe, its path and arguments written so that
/// cmd.exe reads each as data (<see cref="BatchArguments"/>); one it cannot be given that way is refused before anything starts.
/// </summary>
internal static class ProgramPath
{
    /// <summary>
    /// The longest command cmd.exe reads, in characters: a longer one is refused before the start, since cmd.exe would run none of it.
    /// </summary>
    internal const int CommandLineLimit = 8191;

    // What PATHEXT holds on a default Windows installation, used when it is unset.
    private const string DefaultExtensions = ".COM;.EXE;.BAT;.CMD";

    // How cmd.exe is told to run a batch program, before the quoted command itself. /d: no AutoRun command from the registry runs first.
    // /e:on: command extensions on whatever the registry says, so the reading below, and the %~1 forms batch programs rely on, are
    // the same on every machine. /v:off: delayed expansion off whatever the registry says, so ! is an ordinary character. /s: the
    // first and the last quote of the command are removed and nothing else, so the command is exactly the text between them. /c: run
    // the command, then exit with its exit code.
    private const string InterpreterSwitches = "/d /e:on /v:off /s /c ";

    // A bare name resolves only to an image the system loads directly, as a start by name always did: .exe, which CreateProcess appends
    // to a name without an extension, and .com. A script (.bat, .cmd) runs through the command interpreter under other quoting rules,
    // so it is started only when it is named with its extension.
    private static readonly string[] Images = [".COM", ".EXE"];

    // From 0.9.4 (FIX-165): what an absolute Windows path without an extension names, in this order, in its own folder only. CreateProcess
    // appended .exe to such a path when the start named it on its command line; .com follows, as a bare name may be either.
    private static readonly string[] AbsoluteImages = [".exe", ".com"];

    // Extensions a name is taken to carry already, whatever PATHEXT lists.
    private static readonly string[] Named = [".COM", ".EXE", ".BAT", ".CMD"];

    // The characters a batch argument may hold unquoted: none of them means anything to cmd.exe in an argument.
    private const string PlainSymbols = "#$*+-./:?@\\_";

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
    /// <see cref="Resolve(string)"/> finds for it, and nothing else about the start changes. From 0.9.4 (FIX-165), on Windows a batch
    /// program starts through the system directory's cmd.exe instead (<see cref="ThroughCommandInterpreter"/>).
    /// </summary>
    /// <exception cref="ProgramNotFoundException">The program cannot be resolved; nothing was started.</exception>
    /// <exception cref="BatchArgumentRefusedException">A batch program's path or arguments cannot be given to cmd.exe as data; nothing was started.</exception>
    internal static Process? Start(ProcessStartInfo info)
    {
        ArgumentNullException.ThrowIfNull(info);
        string program = Resolve(info.FileName);
        if (IsBatch(program, OperatingSystem.IsWindows()))
        {
            ThroughCommandInterpreter(info, program, CommandInterpreter);
        }
        else
        {
            info.FileName = program;
        }

        NarrowSearchPath(info);
        return Process.Start(info);
    }

    /// <summary>
    /// From 0.9.4 (FIX-165): makes <paramref name="info"/> start the batch program <paramref name="script"/> through the command
    /// interpreter <paramref name="interpreter"/> gives: its file name becomes the interpreter's absolute path and its arguments the
    /// command <see cref="BatchArguments"/> writes from the script and the argument list. Arguments given as one string cannot be
    /// checked one by one, so they are refused. Nothing about <paramref name="info"/> changes when it is refused.
    /// </summary>
    internal static void ThroughCommandInterpreter(ProcessStartInfo info, string script, Func<string> interpreter)
    {
        ArgumentNullException.ThrowIfNull(info);
        ArgumentNullException.ThrowIfNull(interpreter);
        if (!string.IsNullOrEmpty(info.Arguments))
        {
            throw new BatchArgumentRefusedException("A batch program was not started: its arguments were given as one string, which cannot be checked argument by argument; give them as a list.");
        }

        string arguments = BatchArguments(script, info.ArgumentList);
        string program = interpreter();
        info.FileName = program;
        info.ArgumentList.Clear();
        info.Arguments = arguments;
    }

    /// <summary>
    /// From 0.9.4 (FIX-165): whether <paramref name="program"/>, a resolved path, is a batch program, which Windows runs only through
    /// cmd.exe: on Windows, one whose extension is .cmd or .bat, in any case. There is none on Linux and macOS.
    /// </summary>
    internal static bool IsBatch(string program, bool windows) => windows && program is not null
        && (program.EndsWith(".cmd", StringComparison.OrdinalIgnoreCase) || program.EndsWith(".bat", StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// From 0.9.4 (FIX-165): the command interpreter a batch program runs in, cmd.exe in the system directory Windows reports, by its
    /// absolute path. Neither ComSpec nor PATH takes part.
    /// </summary>
    /// <exception cref="ProgramNotFoundException">The system directory is not an absolute path, or holds no cmd.exe.</exception>
    internal static string CommandInterpreter() => CommandInterpreter(Environment.SystemDirectory, IsProgram);

    /// <summary><see cref="CommandInterpreter()"/> for a system directory and a test of whether a path holds a program.</summary>
    internal static string CommandInterpreter(string? systemDirectory, Func<string, bool> isProgram)
    {
        ArgumentNullException.ThrowIfNull(isProgram);
        string directory = systemDirectory ?? string.Empty;
        if (directory.Length == 0 || !FullyQualified(directory, windows: true) || NamesStream(directory))
        {
            throw new ProgramNotFoundException("The system directory is not known as an absolute path, so the command interpreter a batch program runs in was not started.");
        }

        string interpreter = Join(directory, "cmd.exe", windows: true);
        return isProgram(interpreter)
            ? interpreter
            : throw new ProgramNotFoundException("The system directory holds no cmd.exe, so the batch program it would run was not started.");
    }

    /// <summary>
    /// From 0.9.4 (FIX-165): the arguments cmd.exe is started with to run the batch program <paramref name="script"/>, an absolute path,
    /// with <paramref name="arguments"/>: <c>/d /e:on /v:off /s /c ""script" arguments"</c>. The script's path is always quoted. An
    /// argument is written as it is when it is not empty, does not end with a backslash and holds only ASCII letters, digits and
    /// <c># $ * + - . / : ? @ \ _</c>; any other is quoted, each double quote in it doubled, and the backslashes before a double quote
    /// or at its end doubled. cmd.exe toggles its quoting at each double quote, so a doubled quote leaves every other character of
    /// the argument inside quotes, where &amp;, |, &lt;, &gt;, ^, ( and ) mean nothing; /v:off keeps ! as it is. A program the script
    /// hands the arguments on to (%*, as package managers' shims do) and that reads its command line the C runtime's way reads each
    /// argument as given; the script's own %~1 reads a doubled quote as two. What cmd.exe cannot be given as data is refused:
    /// <see cref="BatchRefusal(string, IReadOnlyList{string})"/>, and a command longer than <see cref="CommandLineLimit"/>.
    /// </summary>
    /// <exception cref="BatchArgumentRefusedException">The script's path or an argument cannot be given to cmd.exe as data.</exception>
    internal static string BatchArguments(string script, IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(script);
        ArgumentNullException.ThrowIfNull(arguments);
        if (BatchRefusal(script, arguments) is string refusal)
        {
            throw new BatchArgumentRefusedException($"A batch program was not started: {refusal}");
        }

        var line = new StringBuilder(InterpreterSwitches).Append("\"\"").Append(script).Append('"');
        foreach (string argument in arguments)
        {
            AppendBatchArgument(line.Append(' '), argument ?? string.Empty);
        }

        line.Append('"');
        return line.Length <= CommandLineLimit
            ? line.ToString()
            : throw new BatchArgumentRefusedException($"A batch program was not started: the command cmd.exe would read is {line.Length} characters long, and cmd.exe reads at most {CommandLineLimit}.");
    }

    /// <summary>
    /// From 0.9.4 (FIX-165): why the batch program <paramref name="script"/> cannot be given <paramref name="arguments"/> through cmd.exe,
    /// or null when it can. Refused, in the script's path or in any argument: a percent sign, since cmd.exe expands %NAME% from the
    /// environment before it reads any quoting and nothing on its command line escapes it; a line break or another control character
    /// (a tab in an argument aside), since a line feed ends the command cmd.exe reads, a carriage return is dropped from it and NUL
    /// ends it. Refused in the path also: a double quote, which no Windows file name holds. An argument's value is never repeated.
    /// </summary>
    internal static string? BatchRefusal(string script, IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        if (Unreadable(script ?? string.Empty, path: true) is string path)
        {
            return "its path " + path;
        }

        for (int index = 0; index < arguments.Count; index++)
        {
            if (Unreadable(arguments[index] ?? string.Empty, path: false) is string reason)
            {
                return $"its argument {index + 1} {reason}";
            }
        }

        return null;
    }

    /// <summary>
    /// <see cref="BatchRefusal(string, IReadOnlyList{string})"/> for a configured <paramref name="program"/>, as given, on a platform
    /// (<paramref name="windows"/>): null unless it names a batch program on Windows.
    /// </summary>
    internal static string? BatchRefusal(string program, IReadOnlyList<string> arguments, bool windows)
    {
        string name = windows ? Unquote(program ?? string.Empty) : program ?? string.Empty;
        return IsBatch(name, windows) ? BatchRefusal(name, arguments) : null;
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
            return windows ? WindowsAbsolute(name, isProgram) : name;
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

    // From 0.9.4 (FIX-165): an absolute Windows path names one file, as written. Windows drops a trailing dot or space from a file name
    // and reads a colon after the drive as naming a stream of a file, so such a name is refused: the extension read here, which decides
    // whether cmd.exe runs it, would not be that of the file Windows opens. A name with an extension is used as given. One without is
    // the .exe, else the .com, of that name in the same folder, and nothing else is looked at.
    private static string WindowsAbsolute(string name, Func<string, bool> isProgram)
    {
        string file = name[(name.LastIndexOfAny(WindowsSeparators) + 1)..];
        if (file.Length == 0)
        {
            throw new ProgramNotFoundException($"{name} names a directory, never a program, so it was not started.");
        }

        if (file[^1] is '.' or ' ')
        {
            throw new ProgramNotFoundException($"{name} ends with a dot or a space, which Windows drops from a file name, so it was not started: name the program as its file is named.");
        }

        if (NamesStream(name))
        {
            throw new ProgramNotFoundException($"{name} names a stream of a file, so it was not started: name the program by its file's absolute path.");
        }

        if (file.Contains('.', StringComparison.Ordinal))
        {
            return name;
        }

        foreach (string extension in AbsoluteImages)
        {
            if (isProgram(name + extension))
            {
                return name + extension;
            }
        }

        throw new ProgramNotFoundException($"{name} has no extension, and neither its .exe nor its .com is a program in that folder, so it was not started: an absolute path without an extension starts the .exe or the .com of that name in that folder, and no other folder is looked in.");
    }

    // A colon anywhere but after the drive letter, at the start or after a device prefix (\\?\C:, \\.\C:), names a stream of a file.
    private static bool NamesStream(string path)
    {
        static bool Separator(char c) => c is '\\' or '/';
        for (int at = path.IndexOf(':', StringComparison.Ordinal); at >= 0; at = path.IndexOf(':', at + 1))
        {
            bool drive = (at == 1 && char.IsAsciiLetter(path[0]))
                || (at == 5 && char.IsAsciiLetter(path[4]) && Separator(path[0]) && (Separator(path[1]) || path[1] == '?') && (path[2] is '?' or '.') && Separator(path[3]));
            if (!drive)
            {
                return true;
            }
        }

        return false;
    }

    // Why text cannot be given to cmd.exe as data, or null. path: it is the batch program's path, which is quoted as it is.
    private static string? Unreadable(string text, bool path)
    {
        if (text.Contains('%', StringComparison.Ordinal))
        {
            return "holds a percent sign, which cmd.exe reads as the start of a variable to expand before it reads any quoting, and which nothing on its command line escapes.";
        }

        if (text.Any(c => char.IsControl(c) && (path || c != '\t')))
        {
            return "holds a line break or another control character, which cmd.exe cannot read as part of an argument: a line feed ends the command it reads, and a carriage return is dropped from it.";
        }

        return path && text.Contains('"', StringComparison.Ordinal) ? "holds a double quote, which no Windows file name holds." : null;
    }

    // An argument written bare when cmd.exe reads every character of it as itself; quoted otherwise, each double quote doubled and the
    // backslashes before a double quote or at the end doubled, so a program reading its command line the C runtime's way reads them
    // as given. Ending with a backslash quotes it, so a script that quotes its argument again ("%~1") does not end with \".
    private static void AppendBatchArgument(StringBuilder line, string argument)
    {
        if (argument.Length > 0 && argument[^1] != '\\' && argument.All(c => char.IsAsciiLetterOrDigit(c) || PlainSymbols.Contains(c, StringComparison.Ordinal)))
        {
            line.Append(argument);
            return;
        }

        line.Append('"');
        int backslashes = 0;
        foreach (char c in argument)
        {
            if (c == '"')
            {
                line.Append('\\', backslashes).Append('"');
            }

            backslashes = c == '\\' ? backslashes + 1 : 0;
            line.Append(c);
        }

        line.Append('\\', backslashes).Append('"');
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

/// <summary>
/// From 0.9.4 (FIX-165): a batch program whose path or arguments cannot be given to cmd.exe as data, so nothing was started. It is a
/// <see cref="Win32Exception"/> with native error 160 (bad arguments), so every caller that handles a start that fails handles it.
/// </summary>
internal sealed class BatchArgumentRefusedException(string message) : Win32Exception(160, message);
