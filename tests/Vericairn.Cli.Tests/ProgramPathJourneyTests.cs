// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Vericairn.Cli.Tests;

/// <summary>
/// From 0.9.4 (FIX-161): the command line, and the runtime it hosts, start git by the absolute path an absolute directory of PATH gives.
/// A program of the same name in the current directory, in the estate's root or in an empty or relative PATH entry is not what runs.
/// </summary>
[Collection("cli")]
public sealed partial class ProgramPathJourneyTests
{
    [Fact]
    public void DoctorReportsTheGitOfAnAbsolutePathDirectoryWhateverTheCurrentDirectoryHolds()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "The stand-in programs are POSIX shell scripts; Windows is verified on a Windows machine.");
        TestPrerequisites.RequireGit();
        string directory = CliHarness.TempDirectory("program-path-doctor");
        string markers = CliHarness.TempDirectory("program-path-markers");
        string home = QuietHome();
        try
        {
            string marker = StandIn(directory, markers);
            (int _, string output, string error) = Cli(directory, null, home, "doctor", "--json");
            Assert.False(File.Exists(marker), "The git in the current directory ran.");
            using JsonDocument report = JsonDocument.Parse(output);
            JsonElement git = report.RootElement.GetProperty("git");
            Assert.True(git.GetProperty("state").GetString() == "present", error);
            Assert.StartsWith("git version ", git.GetProperty("value").GetString(), StringComparison.Ordinal);
            Assert.NotEqual(StandInVersion, git.GetProperty("value").GetString());
        }
        finally
        {
            CliHarness.Delete(directory, markers, home);
        }
    }

    // The runtime's repository checks start git through the containment primitive with the estate's root as its working directory.
    // Run from the root, with --estate left to default to it, and from elsewhere, with empty, "." and relative PATH entries ahead of
    // the absolute ones: the git of an absolute directory answers, so the check passes and no stand-in leaves its marker.
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void EstateCheckStartsTheGitOfAnAbsolutePathDirectoryWhateverTheRootAndPathEntriesHold(bool fromRoot)
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "The stand-in programs are POSIX shell scripts; Windows is verified on a Windows machine.");
        TestPrerequisites.RequireGit();
        string root = CliHarness.TempDirectory("program-path-estate");
        string elsewhere = CliHarness.TempDirectory("program-path-elsewhere");
        string markers = CliHarness.TempDirectory("program-path-markers");
        string home = QuietHome();
        try
        {
            CliHarness.Ok("--estate", root, "estate", "init", "--name", "Example", "--slug", "example",
                "--purpose", "Program paths.", "--operator-name", "Operator", "--organization", "Example",
                "--commit", new string('a', 40));
            ContextJourneyTests.Git(root, "init", "-q");
            File.WriteAllText(Path.Combine(root, ".gitignore"), "git\nbin/\n");
            string atRoot = StandIn(root, markers);
            string inEntry = StandIn(Directory.CreateDirectory(Path.Combine(root, "bin")).FullName, markers);
            string path = ":.:bin:" + Environment.GetEnvironmentVariable("PATH");
            (int code, string output, string error) = fromRoot
                ? Cli(root, path, home, "estate", "check")
                : Cli(elsewhere, path, home, "--estate", root, "estate", "check");
            Assert.False(File.Exists(atRoot), "The git in the estate's root ran.");
            Assert.False(File.Exists(inEntry), "The git of a relative PATH entry ran.");
            Assert.True(code == 0, output + error);
            Assert.DoesNotContain("layout.unexpected", output, StringComparison.Ordinal);
        }
        finally
        {
            CliHarness.Delete(root, elsewhere, markers, home);
        }
    }

    // Every process the product starts goes through the program resolver: Process.Start is called only by it, and the native starts
    // (the containment primitive and the presence helper) resolve their program with it before they create anything.
    [Fact]
    public void EveryProductProcessStartResolvesItsProgramFirst()
    {
        string root = RepositoryRoot();
        string resolver = Path.Combine("src", "Vericairn.Records", "Processes", "ProgramPath.cs");
        string[] native = [Path.Combine("src", "Vericairn.Runtime", "Workers", "ContainedProcess.cs"), Path.Combine("src", "Vericairn.Records", "Signing", "PresenceSessionHelper.cs")];
        var starts = new SortedSet<string>(StringComparer.Ordinal);
        var spawns = new SortedSet<string>(StringComparer.Ordinal);
        foreach (string file in Directory.EnumerateFiles(Path.Combine(root, "src"), "*.cs", SearchOption.AllDirectories))
        {
            string relative = Path.GetRelativePath(root, file);
            if (relative.Split(Path.DirectorySeparatorChar).Any(part => part is "bin" or "obj")) continue;
            string[] code = File.ReadAllLines(file).Where(line => !line.TrimStart().StartsWith("//", StringComparison.Ordinal)).ToArray();
            if (code.Any(line => ProcessStart().IsMatch(line) || line.Contains("new Process(", StringComparison.Ordinal) || line.Contains("StartInfo =", StringComparison.Ordinal)))
                starts.Add(relative);
            if (code.Any(line => line.Contains("posix_spawn(", StringComparison.Ordinal) || line.Contains("posix_spawnp(", StringComparison.Ordinal)
                || line.Contains("CreateProcess", StringComparison.Ordinal) || line.Contains("execv", StringComparison.Ordinal)))
            {
                spawns.Add(relative);
                Assert.True(code.Any(line => line.Contains("ProgramPath.Resolve(", StringComparison.Ordinal)), $"{relative} creates a process without resolving its program.");
            }
        }

        Assert.Equal([resolver], starts);
        Assert.Equal(native.Order(StringComparer.Ordinal), spawns);
    }

    [GeneratedRegex(@"(?<![\w.])(System\.Diagnostics\.)?Process\.Start\(")]
    private static partial Regex ProcessStart();

    private static string RepositoryRoot()
    {
        string? root = AppContext.BaseDirectory;
        while (root is not null && !File.Exists(Path.Combine(root, "Vericairn.slnx")))
        {
            root = Path.GetDirectoryName(root);
        }

        Assert.NotNull(root);
        return root;
    }

    private const string StandInVersion = "git version 0.0.0";

    // A stand-in git that leaves a marker when it runs and fails. Running it once by its own path confirms it can run and leave the
    // marker, so the marker's absence afterwards means it was not started.
    private static string StandIn(string directory, string markers)
    {
        if (OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        string marker = Path.Combine(markers, Path.GetFileName(directory) + ".ran");
        string program = Path.Combine(directory, "git");
        File.WriteAllText(program, $"#!/bin/sh\necho ran > '{marker}'\necho '{StandInVersion}'\nexit 1\n");
        File.SetUnixFileMode(program, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        using (Process confirm = Process.Start(new ProcessStartInfo(program) { UseShellExecute = false, RedirectStandardOutput = true })!)
        {
            Assert.Equal(StandInVersion, confirm.StandardOutput.ReadToEnd().Trim());
            Assert.True(confirm.WaitForExit((int)TestTiming.HangTimeout.TotalMilliseconds));
        }

        Assert.True(File.Exists(marker));
        File.Delete(marker);
        return marker;
    }

    // A Vericairn home of the command line's own with its release check off, so the commands these tests start make no request.
    private static string QuietHome()
    {
        string home = CliHarness.TempDirectory("program-path-home");
        (int code, string output, string error) = Cli(home, null, home, "updates", "configure", "--enabled", "false");
        Assert.True(code == 0, output + error);
        return home;
    }

    private static (int Code, string Output, string Error) Cli(string workingDirectory, string? path, string home, params string[] arguments)
    {
        var info = new ProcessStartInfo(CliHarness.BuiltExecutable)
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true, WorkingDirectory = workingDirectory,
        };
        foreach (string argument in arguments) info.ArgumentList.Add(argument);
        if (path is not null) info.Environment["PATH"] = path;
        info.Environment["VERICAIRN_HOME"] = home;
        using Process process = Process.Start(info)!;
        Task<string> output = process.StandardOutput.ReadToEndAsync(TestContext.Current.CancellationToken);
        Task<string> error = process.StandardError.ReadToEndAsync(TestContext.Current.CancellationToken);
        try
        {
            Assert.True(process.WaitForExit((int)TestTiming.HangTimeout.TotalMilliseconds), "The command did not finish.");
        }
        finally
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
        }

        return (process.ExitCode, output.GetAwaiter().GetResult(), error.GetAwaiter().GetResult());
    }
}
