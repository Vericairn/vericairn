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

    // A program file of this platform: an executable script on Linux and macOS, a file with the .exe extension on Windows.
    private static string Program(string directory, string name)
    {
        string path = Path.Combine(directory, OperatingSystem.IsWindows() ? name + ".exe" : name);
        File.WriteAllText(path, "#!/bin/sh\nexit 0\n");
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        return path;
    }
}
