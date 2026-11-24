// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.ComponentModel;
using System.Diagnostics;
using Vericairn.Records.Processes;

namespace Vericairn.Records.Tests;

/// <summary>From 0.9.4 (FIX-161): a program is started by the absolute path the absolute directories of PATH give, and by nothing else.</summary>
public sealed class ProgramPathTests
{
    [Fact]
    public void AnAbsolutePathEntryIsChosenAndEmptyCurrentAndRelativeEntriesAreNeverLookedIn()
    {
        // Every candidate exists, however it is named, so only what is looked at decides.
        var asked = new List<string>();
        string resolved = ProgramPath.Resolve("git", windows: false, ":.:bin:./tools:../up:~/bin::/opt/tools:/usr/bin", null, candidate =>
        {
            asked.Add(candidate);
            return true;
        });

        Assert.Equal("/opt/tools/git", resolved);
        Assert.Equal(["/opt/tools/git"], asked);
    }

    [Fact]
    public void ANameOnlyEmptyCurrentOrRelativeEntriesCouldHoldIsRefusedWithoutLookingThere()
    {
        var asked = new List<string>();
        ProgramNotFoundException refused = Assert.Throws<ProgramNotFoundException>(() =>
            ProgramPath.Resolve("git", windows: false, ":.:bin:./tools", null, candidate =>
            {
                asked.Add(candidate);
                return true;
            }));

        Assert.Empty(asked);
        Assert.Equal(2, refused.NativeErrorCode);
        Assert.IsAssignableFrom<Win32Exception>(refused);
        Assert.Contains("git was not found in an absolute directory of PATH", refused.Message, StringComparison.Ordinal);
        Assert.Throws<ProgramNotFoundException>(() => ProgramPath.Resolve("git", windows: false, null, null, _ => true));
        Assert.Throws<ProgramNotFoundException>(() => ProgramPath.Resolve("git", windows: true, ";.;bin;.\\tools;\\tools;C:tools", null, _ => true));
    }

    [Fact]
    public void AbsoluteEntriesAreLookedInInOrderAndOnlyAProgramThereIsChosen()
    {
        var present = new HashSet<string>(StringComparer.Ordinal) { "/second/git", "/third/git" };
        Assert.Equal("/second/git", ProgramPath.Resolve("git", windows: false, "/first:/second/:/third", null, present.Contains));
        Assert.Equal("/third/git", ProgramPath.Resolve("git", windows: false, "/first:/third:/second", null, present.Contains));
    }

    [Theory]
    [InlineData(false, "/usr/local/bin/git")]
    [InlineData(false, "/no/such/git")]
    [InlineData(true, @"C:\Program Files\Git\cmd\git.exe")]
    [InlineData(true, "C:/Tools/harness.exe")]
    [InlineData(true, @"\\server\share\tools\harness.exe")]
    public void AnAbsolutePathIsUsedAsGivenWithoutLookingAnywhere(bool windows, string program)
    {
        Assert.Equal(program, ProgramPath.Resolve(program, windows, "/usr/bin;C:\\Windows", null, _ => throw new InvalidOperationException("Nothing is looked up.")));
    }

    [Theory]
    [InlineData(false, "./git")]
    [InlineData(false, "bin/git")]
    [InlineData(false, "../git")]
    [InlineData(true, @".\git.exe")]
    [InlineData(true, @"bin\git.exe")]
    [InlineData(true, "bin/git.exe")]
    [InlineData(true, @"\tools\git.exe")]
    [InlineData(true, "C:git.exe")]
    public void ARelativePathIsRefusedWithoutLookingAnywhere(bool windows, string program)
    {
        ProgramNotFoundException refused = Assert.Throws<ProgramNotFoundException>(() =>
            ProgramPath.Resolve(program, windows, windows ? @"C:\Tools" : "/usr/bin", null, _ => throw new InvalidOperationException("Nothing is looked up.")));
        Assert.Contains("is a relative path", refused.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(false, "")]
    [InlineData(false, " ")]
    [InlineData(true, "\"\"")]
    public void NoNameIsRefused(bool windows, string program) =>
        Assert.Throws<ProgramNotFoundException>(() => ProgramPath.Resolve(program, windows, "/usr/bin", null, _ => true));

    [Fact]
    public void OnWindowsABareNameGainsThePathextImageExtensionsDirectoryByDirectory()
    {
        var present = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { @"C:\Scripts\git.cmd", @"C:\Scripts\git.bat", @"C:\Git\cmd\git.exe", @"C:\Git\cmd\git.com" };
        const string Extensions = ".COM;.EXE;.BAT;.CMD";
        // A script never answers a bare name; the first directory holding an image does, in PATHEXT's order within it.
        Assert.Equal(@"C:\Git\cmd\git.COM", ProgramPath.Resolve("git", true, @"C:\Scripts;C:\Git\cmd", Extensions, present.Contains));
        Assert.Equal(@"C:\Git\cmd\git.EXE", ProgramPath.Resolve("git", true, @"C:\Scripts;C:\Git\cmd", ".EXE;.COM;.CMD", present.Contains));
        present.Add(@"C:\First\git.exe");
        Assert.Equal(@"C:\First\git.EXE", ProgramPath.Resolve("git", true, @"C:\First;C:\Git\cmd", Extensions, present.Contains));
    }

    [Fact]
    public void OnWindowsANameWithItsExtensionIsLookedUpAsNamed()
    {
        var present = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { @"C:\npm\harness.cmd", @"C:\npm\harness.cmd.exe", @"C:\Tools\tool.exe", @"C:\Tools\tool.exe.exe" };
        Assert.Equal(@"C:\npm\harness.cmd", ProgramPath.Resolve("harness.cmd", true, @"C:\npm", ".COM;.EXE;.BAT;.CMD", present.Contains));
        Assert.Equal(@"C:\Tools\tool.exe", ProgramPath.Resolve("tool.exe", true, @"C:\Tools", ".COM;.EXE", present.Contains));
        // .cmd counts as an extension even when PATHEXT leaves it out.
        Assert.Equal(@"C:\npm\harness.cmd", ProgramPath.Resolve("harness.cmd", true, @"C:\npm", ".EXE", present.Contains));
        // An extension PATHEXT does not list is part of the name, which gains an image extension.
        present.Add(@"C:\Tools\python3.12.exe");
        Assert.Equal(@"C:\Tools\python3.12.EXE", ProgramPath.Resolve("python3.12", true, @"C:\Tools", ".COM;.EXE", present.Contains));
    }

    [Fact]
    public void OnWindowsAScriptAloneDoesNotAnswerABareName()
    {
        var present = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { @"C:\npm\harness.cmd", @"C:\npm\harness.bat", @"C:\npm\harness.ps1" };
        ProgramNotFoundException refused = Assert.Throws<ProgramNotFoundException>(() =>
            ProgramPath.Resolve("harness", true, @"C:\npm", ".COM;.EXE;.BAT;.CMD;.PS1", present.Contains));
        Assert.Contains("a script is named with its extension", refused.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(".COM")]
    [InlineData("EXE;.E.X;.\\;;.CMD")]
    public void OnWindowsAnExeIsAlwaysTriedWhateverPathextHolds(string? extensions)
    {
        var present = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { @"C:\Git\cmd\git.exe" };
        Assert.Equal(@"C:\Git\cmd\git.exe", ProgramPath.Resolve("git", true, @"C:\Git\cmd", extensions, present.Contains), StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void OnWindowsEntriesAreReadWithoutQuotesOrSpacesAndOnlyAbsoluteOnesAreLookedIn()
    {
        var asked = new List<string>();
        string resolved = ProgramPath.Resolve("\"git\"", true, @" ;.;bin;\tools;C:tools; ""C:\Program Files\Git\cmd"" ;D:/Other", ".EXE", candidate =>
        {
            asked.Add(candidate);
            return candidate.StartsWith(@"C:\Program Files", StringComparison.Ordinal);
        });

        Assert.Equal(@"C:\Program Files\Git\cmd\git.EXE", resolved);
        Assert.Equal([@"C:\Program Files\Git\cmd\git.EXE"], asked);
        Assert.Equal(@"\\server\tools\git.EXE", ProgramPath.Resolve("git", true, @"\\server\tools", ".EXE", _ => true));
    }

    [Fact]
    public void OnThisMachineAProgramReachableOnlyThroughARelativeEntryIsNotChosen()
    {
        string root = Directory.CreateTempSubdirectory("vericairn-program-path-").FullName;
        try
        {
            string name = "vericairn-program-path-" + Guid.NewGuid().ToString("N");
            string reached = Directory.CreateDirectory(Path.Combine(root, "reached")).FullName;
            string absolute = Directory.CreateDirectory(Path.Combine(root, "absolute")).FullName;
            Program(reached, name);
            string expected = Program(absolute, name);
            // The relative entry names a real directory that holds the program, read against this process's current directory.
            string relative = Path.GetRelativePath(Environment.CurrentDirectory, reached);
            Assert.SkipWhen(Path.IsPathFullyQualified(relative), "The temporary directory is on another volume than the current directory, so no relative entry reaches it.");
            Assert.True(File.Exists(Path.Combine(Environment.CurrentDirectory, relative, Path.GetFileName(expected))));
            string entries = string.Join(Path.PathSeparator, "", ".", relative);

            Assert.Throws<ProgramNotFoundException>(() => ProgramPath.Resolve(name, entries));
            Assert.Equal(expected, ProgramPath.Resolve(name, entries + Path.PathSeparator + absolute), StringComparer.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void OnLinuxAndMacOSOnlyAnExecutableFileIsChosenAndALinkToOneIs()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Windows has no execute permission; a file of a program extension is a program there.");
        if (OperatingSystem.IsWindows()) return;
        string root = Directory.CreateTempSubdirectory("vericairn-program-path-").FullName;
        try
        {
            string name = "vericairn-program-path-" + Guid.NewGuid().ToString("N");
            string plain = Directory.CreateDirectory(Path.Combine(root, "plain")).FullName;
            string folder = Directory.CreateDirectory(Path.Combine(root, "folder")).FullName;
            string linked = Directory.CreateDirectory(Path.Combine(root, "linked")).FullName;
            string target = Directory.CreateDirectory(Path.Combine(root, "target")).FullName;
            File.WriteAllText(Path.Combine(plain, name), "#!/bin/sh\n");
            File.SetUnixFileMode(Path.Combine(plain, name), UnixFileMode.UserRead | UnixFileMode.UserWrite);
            Directory.CreateDirectory(Path.Combine(folder, name));
            File.CreateSymbolicLink(Path.Combine(linked, name), Program(target, name));

            Assert.Equal(Path.Combine(linked, name), ProgramPath.Resolve(name, string.Join(':', plain, folder, linked, target)));
            Assert.Throws<ProgramNotFoundException>(() => ProgramPath.Resolve(name, string.Join(':', plain, folder)));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void AStartNamesTheResolvedPathAndAnUnresolvableProgramStartsNothing()
    {
        string absent = "vericairn-absent-" + Guid.NewGuid().ToString("N");
        var info = new ProcessStartInfo(absent) { UseShellExecute = false };
        ProgramNotFoundException refused = Assert.Throws<ProgramNotFoundException>(() => ProgramPath.Start(info));
        Assert.Equal(2, refused.NativeErrorCode);
        Assert.Null(ProgramPath.TryResolve(absent));
        Assert.Throws<ProgramNotFoundException>(() => ProgramPath.Start(new ProcessStartInfo(Path.Combine(".", absent))));

        var shell = new ProcessStartInfo(OperatingSystem.IsWindows() ? "cmd" : "sh") { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (string argument in OperatingSystem.IsWindows() ? new[] { "/d", "/c", "exit 0" } : ["-c", "exit 0"]) shell.ArgumentList.Add(argument);
        using Process process = ProgramPath.Start(shell)!;
        Assert.True(process.WaitForExit((int)TestTiming.HangTimeout.TotalMilliseconds));
        Assert.Equal(0, process.ExitCode);
        Assert.True(Path.IsPathFullyQualified(shell.FileName), shell.FileName);
    }

    [Fact]
    public void AStartedProgramReceivesThisProcesssSearchPathWithOnlyItsAbsoluteEntriesInOrder()
    {
        const string Own = ":/usr/local/bin:.:bin::./tools:/usr/bin:~/bin";
        var environment = new Dictionary<string, string?>(StringComparer.Ordinal) { ["PATH"] = Own, ["HOME"] = "/home/person" };
        ProgramPath.NarrowSearchPath(environment, Own, windows: false);
        Assert.Equal("/usr/local/bin:/usr/bin", environment["PATH"]);
        Assert.Equal("/home/person", environment["HOME"]);

        // No absolute entry left: PATH is removed, since an empty one also names the current directory.
        environment = new Dictionary<string, string?>(StringComparer.Ordinal) { ["PATH"] = ":.:bin" };
        ProgramPath.NarrowSearchPath(environment, ":.:bin", windows: false);
        Assert.False(environment.ContainsKey("PATH"));

        // On Windows the name is matched without case and an absolute entry keeps its own spelling.
        const string Windows = @" ;C:\Git\cmd;.;bin;\tools;C:tools; ""C:\Program Files\Tools"" ;\\server\share";
        environment = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase) { ["Path"] = Windows };
        ProgramPath.NarrowSearchPath(environment, Windows, windows: true);
        Assert.Equal(@"C:\Git\cmd; ""C:\Program Files\Tools"" ;\\server\share", environment["PATH"]);
        Assert.Equal(["Path"], environment.Keys);
    }

    [Fact]
    public void ASearchPathTheCallerSetIsKeptAsGivenAndNoneIsAdded()
    {
        var explicitly = new Dictionary<string, string?>(StringComparer.Ordinal) { ["PATH"] = "bin:/opt/tools" };
        ProgramPath.NarrowSearchPath(explicitly, ":/usr/bin", windows: false);
        Assert.Equal("bin:/opt/tools", explicitly["PATH"]);

        var without = new Dictionary<string, string?>(StringComparer.Ordinal) { ["HOME"] = "/home/person" };
        ProgramPath.NarrowSearchPath(without, ":/usr/bin", windows: false);
        Assert.False(without.ContainsKey("PATH"));

        var info = new ProcessStartInfo("git");
        info.Environment.Remove("PATH");
        info.Environment["PATH"] = "bin" + Path.PathSeparator + Environment.GetEnvironmentVariable("PATH") + "-explicit";
        string given = info.Environment["PATH"]!;
        ProgramPath.NarrowSearchPath(info);
        Assert.Equal(given, info.Environment["PATH"]);
    }

    [Theory]
    [InlineData(false, "./scripts/setup.sh", true)]
    [InlineData(false, "scripts/setup.sh", true)]
    [InlineData(false, "../setup.sh", true)]
    [InlineData(false, "setup.sh", false)]
    [InlineData(false, "/opt/tools/setup.sh", false)]
    [InlineData(true, @"scripts\setup.cmd", true)]
    [InlineData(true, @".\setup.cmd", true)]
    [InlineData(true, "scripts/setup.cmd", true)]
    [InlineData(true, "setup.cmd", false)]
    [InlineData(true, @"C:\tools\setup.cmd", false)]
    [InlineData(true, @"\\server\share\setup.cmd", false)]
    [InlineData(true, @"\tools\setup.cmd", false)]
    [InlineData(true, "C:setup.cmd", false)]
    public void OnlyARelativePathWithADirectoryPartIsReadAgainstADirectory(bool windows, string program, bool relative) =>
        Assert.Equal(relative, ProgramPath.RelativeWithDirectory(program, windows));

    [Fact]
    public void OnThisMachineARelativePathWithADirectoryPartBecomesTheAbsolutePathItNamesInTheDirectory()
    {
        string directory = Path.Combine(Path.GetTempPath(), "workspace");
        Assert.Equal(Path.Combine(directory, "scripts", "setup.sh"), ProgramPath.InDirectory(Path.Combine(".", "scripts", "setup.sh"), directory));
        Assert.Equal(Path.Combine(Path.GetTempPath(), "setup.sh"), ProgramPath.InDirectory(Path.Combine("..", "setup.sh"), directory));
        Assert.Equal("make", ProgramPath.InDirectory("make", directory));
        string absolute = Path.Combine(Path.GetTempPath(), "tools", "setup");
        Assert.Equal(absolute, ProgramPath.InDirectory(absolute, directory));
    }

    [Theory]
    [InlineData(false, "", "it names no program")]
    [InlineData(false, "scripts/", "it names a directory")]
    [InlineData(false, ".", "it names a directory")]
    [InlineData(false, "scripts/..", "it names a directory")]
    [InlineData(true, @"scripts\", "it names a directory")]
    [InlineData(true, @"\tools\setup.cmd", "rooted only on the current drive")]
    [InlineData(true, "C:setup.cmd", "rooted only on the current drive")]
    public void AnExecutableNoReadingCanStartSaysWhy(bool windows, string program, string why) =>
        Assert.Contains(why, ProgramPath.NeverStarts(program, windows) ?? string.Empty, StringComparison.Ordinal);

    [Theory]
    [InlineData(false, "make")]
    [InlineData(false, "./scripts/setup.sh")]
    [InlineData(false, "/opt/tools/setup")]
    [InlineData(true, "npm")]
    [InlineData(true, @"scripts\setup.cmd")]
    [InlineData(true, @"C:\tools\setup.exe")]
    [InlineData(true, @"\\server\share\setup.exe")]
    public void AnExecutableSomeReadingCanStartIsNotRefused(bool windows, string program) => Assert.Null(ProgramPath.NeverStarts(program, windows));

    // From 0.9.4 (FIX-165): what an absolute Windows path without an extension starts, and the batch programs that start through cmd.exe.

    [Fact]
    public void OnWindowsAnAbsolutePathWithoutAnExtensionIsItsExeElseItsComInThatFolderOnly()
    {
        var asked = new List<string>();
        var present = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { @"C:\tools\fix161argv.exe", @"C:\Other\fix161argv.exe" };
        bool Probe(string candidate)
        {
            asked.Add(candidate);
            return present.Contains(candidate);
        }

        Assert.Equal(@"C:\tools\fix161argv.exe", ProgramPath.Resolve(@"C:\tools\fix161argv", true, @"C:\Other", ".COM;.EXE", Probe));
        Assert.Equal([@"C:\tools\fix161argv.exe"], asked);

        // A .com when there is no .exe; the .exe first when both are there, whatever PATHEXT's order.
        asked.Clear();
        present = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { @"C:\tools\tool.com" };
        Assert.Equal(@"C:\tools\tool.com", ProgramPath.Resolve(@"C:\tools\tool", true, null, ".COM;.EXE", Probe));
        Assert.Equal([@"C:\tools\tool.exe", @"C:\tools\tool.com"], asked);
        present.Add(@"C:\tools\tool.exe");
        Assert.Equal(@"C:\tools\tool.exe", ProgramPath.Resolve(@"C:\tools\tool", true, null, ".COM;.EXE", Probe));
        Assert.Equal(@"\\server\share\tool.exe", ProgramPath.Resolve(@"\\server\share\tool", true, null, null, _ => true));

        // Neither: refused, though the file without an extension, a script of that name, and the program in another folder and in
        // PATH are there. Only that folder's .exe and .com were looked at.
        asked.Clear();
        present = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { @"C:\tools\probe", @"C:\tools\probe.cmd", @"C:\tools\probe.bat", @"C:\Other\probe.exe" };
        ProgramNotFoundException refused = Assert.Throws<ProgramNotFoundException>(() => ProgramPath.Resolve(@"C:\tools\probe", true, @"C:\Other", ".COM;.EXE;.BAT;.CMD", Probe));
        Assert.Equal([@"C:\tools\probe.exe", @"C:\tools\probe.com"], asked);
        Assert.Contains("has no extension", refused.Message, StringComparison.Ordinal);

        // A name with an extension is used as given, and on Linux and macOS a path without one is too.
        Assert.Equal(@"C:\tools\python3.12", ProgramPath.Resolve(@"C:\tools\python3.12", true, null, null, _ => throw new InvalidOperationException("Nothing is looked up.")));
        Assert.Equal("/opt/tools/fix161argv", ProgramPath.Resolve("/opt/tools/fix161argv", false, "/usr/bin", null, _ => throw new InvalidOperationException("Nothing is looked up.")));
    }

    [Theory]
    [InlineData(@"C:\tools\probe.cmd.")]
    [InlineData(@"C:\tools\probe.cmd .")]
    [InlineData(@"C:\tools\probe.cmd. .")]
    [InlineData(@"C:\tools\probe.cmd::$DATA")]
    [InlineData(@"C:\tools\probe.exe:hidden.cmd")]
    [InlineData(@"\\?\C:\tools\probe.cmd.")]
    [InlineData(@"\\server\share\probe:cmd")]
    [InlineData(@"C:\tools\")]
    public void OnWindowsAnAbsolutePathWindowsWouldOpenUnderAnotherNameIsRefused(string program)
    {
        Assert.Throws<ProgramNotFoundException>(() => ProgramPath.Resolve(program, true, @"C:\Windows", null, _ => throw new InvalidOperationException("Nothing is looked up.")));
        Assert.Equal(@"\\?\C:\tools\probe.cmd", ProgramPath.Resolve(@"\\?\C:\tools\probe.cmd", true, null, null, _ => throw new InvalidOperationException("Nothing is looked up.")));
    }

    [Theory]
    [InlineData(true, @"C:\npm\harness.cmd", true)]
    [InlineData(true, @"C:\npm\SETUP.BAT", true)]
    [InlineData(true, @"C:\npm\setup.Cmd", true)]
    [InlineData(true, @"C:\npm\harness.exe", false)]
    [InlineData(true, @"C:\npm\harness.cmd.exe", false)]
    [InlineData(true, @"C:\npm\harness.ps1", false)]
    [InlineData(false, "/opt/npm/harness.cmd", false)]
    public void ABatchProgramIsOneWindowsKnowsByItsExtension(bool windows, string program, bool batch) =>
        Assert.Equal(batch, ProgramPath.IsBatch(program, windows));

    [Fact]
    public void ABatchProgramsCommandQuotesItsPathAndEveryArgumentCmdWouldReadAsMoreThanData()
    {
        Assert.Equal("/d /e:on /v:off /s /c \"\"C:\\script dir\\probe.cmd\" \"a b\" \"x\"\"y\"\"",
            ProgramPath.BatchArguments(@"C:\script dir\probe.cmd", ["a b", "x\"y"]));
        Assert.Equal("/d /e:on /v:off /s /c \"\"C:\\tools\\probe.bat\"\"", ProgramPath.BatchArguments(@"C:\tools\probe.bat", []));

        // Bare: letters, digits and # $ * + - . / : ? @ \ _, not ending with a backslash. Quoted: anything else, the empty argument and
        // one ending with a backslash, whose trailing backslashes are doubled, as are those before a double quote.
        (string Given, string Written)[] arguments =
        [
            ("--version", "--version"),
            ("-c", "-c"),
            (@"C:\dir\file.txt", @"C:\dir\file.txt"),
            ("/goal", "/goal"),
            ("a@b:c?d*e#f$g+h_i", "a@b:c?d*e#f$g+h_i"),
            ("a=b", @"""a=b"""),
            ("a,b;c", @"""a,b;c"""),
            ("a b", @"""a b"""),
            ("tab\there", "\"tab\there\""),
            ("", @""""""),
            ("x&y|z<w>v^u(t)s!r", @"""x&y|z<w>v^u(t)s!r"""),
            ("it's", @"""it's"""),
            ("caf\u00e9", "\"caf\u00e9\""),
            (@"C:\dir\", @"""C:\dir\\"""),
            (@"C:\dir\\", @"""C:\dir\\\\"""),
            ("x\"y", @"""x""""y"""),
            ("\"", @""""""""""),
            (@"a\""b", @"""a\\""""b"""),
            (@"\\""", @"""\\\\"""""""),
        ];
        Assert.Equal("/d /e:on /v:off /s /c \"\"C:\\npm\\harness.cmd\"" + string.Concat(arguments.Select(a => " " + a.Written)) + "\"",
            ProgramPath.BatchArguments(@"C:\npm\harness.cmd", arguments.Select(a => a.Given).ToArray()));
    }

    // The arguments read back two ways from the command written for them: as cmd.exe splits a batch program's arguments, which must
    // leave every character but a plain one inside quotes and keep each argument one argument, and as a program the script hands them
    // to reads them the C runtime's way, which must give each argument as it was. A model of the documented rules, not Windows itself.
    [Fact]
    public void EveryArgumentStaysOneQuotedArgumentToCmdAndReadsBackAsGivenTheCRuntimesWay()
    {
        string[] arguments =
        [
            "a b", "x\"y", "\"", "\"\"", "\"start", "end\"", "a\\\"b", "a\\\\\"b", @"C:\dir\", @"C:\dir with space\", "\\", "\\\\", "", " ",
            "x\" & calc & \"y", "\" & calc", "a\"^&b", "&|<>^()!", "a!PATH!b", "=;,", "tab\there", "projects={\"C:\\\\work\"={trust_level=\"untrusted\"}}",
            "/goal Make it so & more!", "--flag=value", "caf\u00e9 \u4e16\u754c",
        ];
        string written = ProgramPath.BatchArguments(@"C:\script dir\probe.cmd", arguments);
        const string Switches = "/d /e:on /v:off /s /c ";
        Assert.StartsWith(Switches, written, StringComparison.Ordinal);

        // /s takes away the first and the last quote and nothing else.
        string command = written[Switches.Length..];
        Assert.True(command[0] == '"' && command[^1] == '"');
        command = command[1..^1];

        // cmd.exe: quoting toggles at every double quote; outside quotes only plain characters and the spaces between arguments.
        bool quoted = false;
        var tokens = new List<string>();
        var token = new System.Text.StringBuilder();
        foreach (char c in command)
        {
            if (c == '"') quoted = !quoted;
            if (!quoted && c == ' ')
            {
                if (token.Length > 0) tokens.Add(token.ToString());
                token.Clear();
                continue;
            }

            Assert.True(quoted || c == '"' || char.IsAsciiLetterOrDigit(c) || "#$*+-./:?@\\_".Contains(c, StringComparison.Ordinal), $"'{c}' is outside quotes.");
            token.Append(c);
        }

        Assert.False(quoted);
        if (token.Length > 0) tokens.Add(token.ToString());
        Assert.Equal(1 + arguments.Length, tokens.Count);
        Assert.Equal("\"C:\\script dir\\probe.cmd\"", tokens[0]);
        Assert.DoesNotContain('%', command);

        // The C runtime: what the script hands on with %* reads back as given.
        Assert.Equal(arguments, CRuntimeArguments(command[tokens[0].Length..]));
    }

    [Theory]
    [InlineData("100%", "its argument 1 holds a percent sign")]
    [InlineData("%PATH%", "its argument 1 holds a percent sign")]
    [InlineData("a\nb", "its argument 1 holds a line break or another control character")]
    [InlineData("a\r\nb", "its argument 1 holds a line break or another control character")]
    [InlineData("a\0b", "its argument 1 holds a line break or another control character")]
    [InlineData("a\u001Ab", "its argument 1 holds a line break or another control character")]
    [InlineData("a\u0085b", "its argument 1 holds a line break or another control character")]
    public void AnArgumentCmdCannotReadAsDataIsRefusedWithoutRepeatingIt(string argument, string why)
    {
        BatchArgumentRefusedException refused = Assert.Throws<BatchArgumentRefusedException>(() => ProgramPath.BatchArguments(@"C:\tools\probe.cmd", [argument]));
        Assert.Contains(why, refused.Message, StringComparison.Ordinal);
        Assert.Equal(160, refused.NativeErrorCode);
        Assert.IsAssignableFrom<Win32Exception>(refused);
        string second = Assert.Throws<BatchArgumentRefusedException>(() => ProgramPath.BatchArguments(@"C:\tools\probe.cmd", ["ok", "secret-" + argument])).Message;
        Assert.Contains("its argument 2 ", second, StringComparison.Ordinal);
        Assert.DoesNotContain("secret", second, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(@"C:\100%\probe.cmd", "its path holds a percent sign")]
    [InlineData("C:\\a\"b\\probe.cmd", "its path holds a double quote")]
    [InlineData("C:\\a\tb\\probe.cmd", "its path holds a line break or another control character")]
    public void ABatchPathCmdCannotReadAsDataIsRefused(string script, string why) =>
        Assert.Contains(why, Assert.Throws<BatchArgumentRefusedException>(() => ProgramPath.BatchArguments(script, ["a b"])).Message, StringComparison.Ordinal);

    [Fact]
    public void ACommandLongerThanCmdReadsIsRefused()
    {
        const string Script = @"C:\tools\probe.cmd";
        int room = ProgramPath.CommandLineLimit - ProgramPath.BatchArguments(Script, []).Length - 1;
        Assert.Equal(ProgramPath.CommandLineLimit, ProgramPath.BatchArguments(Script, [new string('a', room)]).Length);
        Assert.Contains("cmd.exe reads at most 8191", Assert.Throws<BatchArgumentRefusedException>(() => ProgramPath.BatchArguments(Script, [new string('a', room + 1)])).Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AConfiguredBatchProgramsArgumentsAreCheckedOnlyOnWindows()
    {
        Assert.Contains("its argument 2 holds a percent sign", ProgramPath.BatchRefusal(@"scripts\setup.cmd", ["a b", "100%"], windows: true), StringComparison.Ordinal);
        Assert.Contains("its argument 1 holds a percent sign", ProgramPath.BatchRefusal("\"C:\\tools\\setup.bat\"", ["%HOME%"], windows: true), StringComparison.Ordinal);
        Assert.Null(ProgramPath.BatchRefusal(@"scripts\setup.cmd", ["a b", "x\"y", "&|<>^!"], windows: true));
        Assert.Null(ProgramPath.BatchRefusal(@"scripts\setup.exe", ["100%"], windows: true));
        Assert.Null(ProgramPath.BatchRefusal("scripts/setup.cmd", ["100%"], windows: false));
    }

    [Fact]
    public void TheCommandInterpreterIsTheSystemDirectorysCmdByItsAbsolutePath()
    {
        Assert.Equal(@"C:\Windows\System32\cmd.exe", ProgramPath.CommandInterpreter(@"C:\Windows\System32", p => p == @"C:\Windows\System32\cmd.exe"));
        Assert.Equal(@"C:\Windows\System32\cmd.exe", ProgramPath.CommandInterpreter(@"C:\Windows\System32\", p => p == @"C:\Windows\System32\cmd.exe"));
        foreach (string? directory in new[] { null, "", "System32", @"\Windows\System32", @"C:Windows\System32" })
        {
            Assert.Throws<ProgramNotFoundException>(() => ProgramPath.CommandInterpreter(directory, _ => throw new InvalidOperationException("Nothing is looked up.")));
        }

        Assert.Contains("holds no cmd.exe", Assert.Throws<ProgramNotFoundException>(() => ProgramPath.CommandInterpreter(@"C:\Windows\System32", _ => false)).Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ABatchProgramsStartNamesTheInterpreterAndTheCommandAndARefusedOneChangesNothing()
    {
        const string Script = @"C:\script dir\probe.cmd";
        var info = new ProcessStartInfo(Script) { ArgumentList = { "a b", "x\"y" } };
        ProgramPath.ThroughCommandInterpreter(info, Script, () => @"C:\Windows\System32\cmd.exe");
        Assert.Equal(@"C:\Windows\System32\cmd.exe", info.FileName);
        Assert.Empty(info.ArgumentList);
        Assert.Equal("/d /e:on /v:off /s /c \"\"C:\\script dir\\probe.cmd\" \"a b\" \"x\"\"y\"\"", info.Arguments);
        Assert.Equal("1", info.Environment[ProgramPath.NoCurrentDirectorySearch]);

        var refused = new ProcessStartInfo("probe.cmd") { ArgumentList = { "a b", "100%" } };
        Assert.Throws<BatchArgumentRefusedException>(() => ProgramPath.ThroughCommandInterpreter(refused, Script, () => throw new InvalidOperationException("No interpreter is looked for.")));
        Assert.Equal("probe.cmd", refused.FileName);
        Assert.Equal(["a b", "100%"], refused.ArgumentList);

        var single = new ProcessStartInfo("probe.cmd", "a b");
        Assert.Contains("given as one string", Assert.Throws<BatchArgumentRefusedException>(() => ProgramPath.ThroughCommandInterpreter(single, Script, () => @"C:\Windows\System32\cmd.exe")).Message, StringComparison.Ordinal);
        Assert.Equal("probe.cmd", single.FileName);
    }

    // From 0.9.4 (FIX-165): each character cmd.exe reads as more than data, alone and inside an argument, is quoted, a double quote
    // doubled, and a percent sign refused; each character of the bare set stays bare. Any change to the bare set fails one of these.
    private const string Probe = @"C:\tools\probe.cmd";

    private static string Written(params string[] arguments) => "/d /e:on /v:off /s /c \"\"" + Probe + "\"" + string.Concat(arguments.Select(a => " " + a)) + "\"";

    [Theory]
    [InlineData('&')]
    [InlineData('|')]
    [InlineData('<')]
    [InlineData('>')]
    [InlineData('^')]
    [InlineData('(')]
    [InlineData(')')]
    [InlineData(',')]
    [InlineData(';')]
    [InlineData('=')]
    [InlineData('!')]
    [InlineData(' ')]
    [InlineData('\t')]
    [InlineData('\'')]
    [InlineData('`')]
    [InlineData('~')]
    [InlineData('[')]
    [InlineData(']')]
    [InlineData('{')]
    [InlineData('}')]
    [InlineData('\u00a0')]
    public void EachCharacterCmdReadsAsMoreThanDataIsQuoted(char special)
    {
        Assert.Equal(Written($"\"{special}\""), ProgramPath.BatchArguments(Probe, [special.ToString()]));
        Assert.Equal(Written($"\"a{special}b\""), ProgramPath.BatchArguments(Probe, [$"a{special}b"]));
    }

    [Fact]
    public void ADoubleQuoteIsDoubledInsideQuotesAndAPercentSignIsRefused()
    {
        Assert.Equal(Written("\"\"\"\""), ProgramPath.BatchArguments(Probe, ["\""]));
        Assert.Equal(Written("\"a\"\"b\""), ProgramPath.BatchArguments(Probe, ["a\"b"]));
        Assert.Throws<BatchArgumentRefusedException>(() => ProgramPath.BatchArguments(Probe, ["%"]));
        Assert.Throws<BatchArgumentRefusedException>(() => ProgramPath.BatchArguments(Probe, ["a%b"]));
    }

    [Theory]
    [InlineData('#')]
    [InlineData('$')]
    [InlineData('*')]
    [InlineData('+')]
    [InlineData('-')]
    [InlineData('.')]
    [InlineData('/')]
    [InlineData(':')]
    [InlineData('?')]
    [InlineData('@')]
    [InlineData('_')]
    [InlineData('\\')]
    [InlineData('a')]
    [InlineData('Z')]
    [InlineData('0')]
    public void EachCharacterOfTheBareSetStaysBareInsideAnArgument(char plain) =>
        Assert.Equal(Written($"a{plain}b"), ProgramPath.BatchArguments(Probe, [$"a{plain}b"]));

    // From 0.9.4 (FIX-165): every program started on Windows looks for what it names bare in PATH only.
    [Fact]
    public void TheVariableThatKeepsTheWorkingDirectoryOutOfTheSearchIsSetWhateverTheEnvironmentHeld()
    {
        Assert.Equal("NoDefaultCurrentDirectoryInExePath", ProgramPath.NoCurrentDirectorySearch);
        var windows = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase) { ["Path"] = @"C:\Windows", ["nodefaultcurrentdirectoryinexepath"] = "0" };
        ProgramPath.WithoutCurrentDirectorySearch(windows);
        Assert.Equal(["Path", "NoDefaultCurrentDirectoryInExePath"], windows.Keys);
        Assert.Equal("1", windows["NoDefaultCurrentDirectoryInExePath"]);

        var spelled = new Dictionary<string, string?>(StringComparer.Ordinal) { ["NODEFAULTCURRENTDIRECTORYINEXEPATH"] = null, ["NoDefaultCurrentDirectoryInExePath"] = "" };
        ProgramPath.WithoutCurrentDirectorySearch(spelled);
        Assert.Equal(new KeyValuePair<string, string?>("NoDefaultCurrentDirectoryInExePath", "1"), Assert.Single(spelled));
    }

    // On a Windows machine: the batch program starts through the system's cmd.exe, gets its arguments and gives its exit code.
    [Fact]
    public void OnWindowsABatchProgramStartsThroughTheSystemsCmdWithItsArgumentsAndExitCode()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "A batch program runs on Windows only; the command it is given is checked above on every platform.");
        string root = Directory.CreateTempSubdirectory("vericairn-batch-").FullName;
        try
        {
            string folder = Directory.CreateDirectory(Path.Combine(root, "script dir")).FullName;
            string script = Batch(folder, "probe.cmd", ">\"%~dp0one.txt\" echo(%~1", ">\"%~dp0two.txt\" echo(%2", "exit /b 3");
            var info = new ProcessStartInfo(script) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, ArgumentList = { "a b", "x\"y" } };

            Assert.Equal(3, Run(info));
            Assert.Equal(Path.Combine(Environment.SystemDirectory, "cmd.exe"), info.FileName, StringComparer.OrdinalIgnoreCase);
            Assert.Equal("a b", File.ReadAllText(Path.Combine(folder, "one.txt")).TrimEnd('\r', '\n'));
            // The script's own %2 is the argument as written, its quote doubled.
            Assert.Equal("\"x\"\"y\"", File.ReadAllText(Path.Combine(folder, "two.txt")).TrimEnd('\r', '\n'));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void OnWindowsABatchArgumentCmdCannotReadAsDataStartsNothing()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "A batch program runs on Windows only; the refusals are checked above on every platform.");
        string root = Directory.CreateTempSubdirectory("vericairn-batch-").FullName;
        try
        {
            string marker = Path.Combine(root, "ran.txt");
            string script = Batch(root, "probe.cmd", $">\"{marker}\" echo ran", "exit /b 0");
            foreach (string argument in new[] { "100%", "%PATH%", "a\nb", "a\rb", "a\u001Ab" })
            {
                var info = new ProcessStartInfo(script) { UseShellExecute = false, ArgumentList = { "a b", argument } };
                Assert.Throws<BatchArgumentRefusedException>(() => ProgramPath.Start(info));
                Assert.False(File.Exists(marker), "The batch program ran.");
            }

            // The same program with arguments cmd.exe reads as data runs, and leaves the marker.
            Assert.Equal(0, Run(new ProcessStartInfo(script) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, ArgumentList = { "a b", "&|<>^!" } }));
            Assert.True(File.Exists(marker));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void OnWindowsAnAbsolutePathWithoutAnExtensionStartsTheExeOrTheComOfThatFolder()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Windows reads a program's extension; the rule is checked above on every platform.");
        string cmd = Path.Combine(Environment.SystemDirectory, "cmd");
        Assert.Equal(cmd + ".exe", ProgramPath.Resolve(cmd), StringComparer.OrdinalIgnoreCase);
        Assert.Equal(5, Run(new ProcessStartInfo(cmd) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, ArgumentList = { "/d", "/c", "exit 5" } }));

        string chcp = Path.Combine(Environment.SystemDirectory, "chcp");
        Assert.SkipUnless(File.Exists(chcp + ".com") && !File.Exists(chcp + ".exe"), "This Windows has no chcp.com alone in its system directory.");
        Assert.Equal(chcp + ".com", ProgramPath.Resolve(chcp), StringComparer.OrdinalIgnoreCase);
        var com = new ProcessStartInfo(chcp) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        Run(com);
        Assert.Equal(chcp + ".com", com.FileName, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void OnWindowsABareNameOnlyABatchProgramOnPathHoldsIsNotFound()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "PATHEXT and batch programs are Windows'; the rule is checked above with Windows' names.");
        string root = Directory.CreateTempSubdirectory("vericairn-batch-").FullName;
        try
        {
            string name = "vericairn-batch-" + Guid.NewGuid().ToString("N");
            string script = Batch(root, name + ".cmd", "exit /b 0");
            Assert.Throws<ProgramNotFoundException>(() => ProgramPath.Resolve(name, root));
            Assert.Equal(script, ProgramPath.Resolve(name + ".cmd", root), StringComparer.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    // A batch program of the given lines, written as cmd.exe reads a batch file, with CRLF line ends.
    private static string Batch(string directory, string name, params string[] lines)
    {
        string path = Path.Combine(directory, name);
        File.WriteAllText(path, string.Join("\r\n", lines) + "\r\n");
        return path;
    }

    // Starts the program through the resolver and waits for it, its output read to the end; its exit code.
    private static int Run(ProcessStartInfo info)
    {
        using Process process = ProgramPath.Start(info)!;
        Task<string>? output = info.RedirectStandardOutput ? process.StandardOutput.ReadToEndAsync() : null;
        Task<string>? error = info.RedirectStandardError ? process.StandardError.ReadToEndAsync() : null;
        Assert.True(process.WaitForExit((int)TestTiming.HangTimeout.TotalMilliseconds), "The program did not finish.");
        output?.GetAwaiter().GetResult();
        error?.GetAwaiter().GetResult();
        return process.ExitCode;
    }

    // A command line read the C runtime's way (the rules Microsoft documents for parsing C++ command-line arguments): white space
    // separates arguments outside quotes; 2n backslashes before a quote are n backslashes and the quote toggles quoting, 2n+1 are n
    // and a literal quote; a doubled quote inside quotes is one literal quote; other backslashes are literal.
    private static List<string> CRuntimeArguments(string text)
    {
        var result = new List<string>();
        var current = new System.Text.StringBuilder();
        bool quoted = false, started = false;
        for (int i = 0; i < text.Length;)
        {
            char c = text[i];
            if (!quoted && c is ' ' or '\t')
            {
                if (started) result.Add(current.ToString());
                current.Clear();
                started = false;
                i++;
                continue;
            }

            started = true;
            if (c == '\\')
            {
                int count = 0;
                while (i < text.Length && text[i] == '\\')
                {
                    count++;
                    i++;
                }

                if (i < text.Length && text[i] == '"')
                {
                    current.Append('\\', count / 2);
                    if (count % 2 == 1)
                    {
                        current.Append('"');
                        i++;
                    }
                }
                else
                {
                    current.Append('\\', count);
                }

                continue;
            }

            if (c == '"')
            {
                if (quoted && i + 1 < text.Length && text[i + 1] == '"')
                {
                    current.Append('"');
                    i += 2;
                    continue;
                }

                quoted = !quoted;
                i++;
                continue;
            }

            current.Append(c);
            i++;
        }

        if (started) result.Add(current.ToString());
        return result;
    }

    // A program file of this platform: an executable script on Linux and macOS, a file with the .exe extension on Windows.
    private static string Program(string directory, string name)
    {
        string path = Path.Combine(directory, OperatingSystem.IsWindows() ? name + ".exe" : name);
        File.WriteAllText(path, "#!/bin/sh\nexit 0\n");
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        return path;
    }
}

/// <summary>
/// From 0.9.4 (FIX-165): cmd.exe is the system directory's, never the one ComSpec names. The test changes this process's ComSpec, so
/// nothing runs beside it.
/// </summary>
[CollectionDefinition("command interpreter", DisableParallelization = true)]
public sealed class CommandInterpreterScope;

[Collection("command interpreter")]
public sealed class CommandInterpreterTests
{
    [Fact]
    public void TheCommandInterpreterIsNeverTheOneComSpecNames()
    {
        string? comSpec = Environment.GetEnvironmentVariable("ComSpec");
        string root = Directory.CreateTempSubdirectory("vericairn-comspec-").FullName;
        string standIn = Path.Combine(root, "cmd.exe");
        File.WriteAllText(standIn, "not the system's command interpreter");
        try
        {
            Environment.SetEnvironmentVariable("ComSpec", standIn);
            if (OperatingSystem.IsWindows())
            {
                Assert.Equal(Path.Combine(Environment.SystemDirectory, "cmd.exe"), ProgramPath.CommandInterpreter(), StringComparer.OrdinalIgnoreCase);
            }
            else
            {
                // No system directory: refused, whatever ComSpec names.
                Assert.Throws<ProgramNotFoundException>(() => ProgramPath.CommandInterpreter());
            }
        }
        finally
        {
            Environment.SetEnvironmentVariable("ComSpec", comSpec);
            Directory.Delete(root, recursive: true);
        }
    }
}
