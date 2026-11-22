// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Diagnostics;

namespace Vericairn.Runtime.Tests;

internal static class ProcessHarness
{
    private const string Marker = "--vericairn-process-test";

    /// <summary>The variable naming the working directory of the onward-git scenario.</summary>
    internal const string DirectoryVariable = "VERICAIRN_PROCESS_TEST_DIRECTORY";

    /// <summary>The external git command the onward-git scenario asks for: git-vericairn-standin, which git looks up by its search path.</summary>
    internal const string OnwardCommand = "vericairn-standin";

    internal static ProcessStartInfo Command(string scenario, string? ready = null)
    {
        var info = new ProcessStartInfo(Environment.ProcessPath!)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = Environment.CurrentDirectory,
        };
        if (Path.GetFileNameWithoutExtension(info.FileName).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
        {
            info.ArgumentList.Add(typeof(ProcessHarness).Assembly.Location);
        }

        info.ArgumentList.Add(Marker);
        info.ArgumentList.Add(scenario);
        if (ready is not null) info.ArgumentList.Add(ready);
        return info;
    }

    internal sealed class Scenario(string scenario) : IDisposable
    {
        private readonly string directory = CreateDirectory();
        private string Ready => Path.Combine(directory, "ready");
        internal ProcessStartInfo Command => ProcessHarness.Command(scenario, Ready);

        internal void WhenReady(Action action)
        {
            using var changed = new FileSignal(directory);
            changed.Until(() => File.Exists(Ready), "the child process to enter its scenario");
            Assert.True(File.Exists(Ready));
            action();
        }

        public void Dispose() => Directory.Delete(directory, recursive: true);

        private static string CreateDirectory()
        {
            string path = Path.Combine(Path.GetTempPath(), "vericairn-process-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path);
            return path;
        }
    }

    internal static void Intercept()
    {
        string[] arguments = Environment.GetCommandLineArgs();
        int at = Array.IndexOf(arguments, Marker);
        if (at < 0)
        {
            return;
        }

        // Even the broken sequential reader has a finite test lifetime.
        using var guard = new Timer(_ => Environment.Exit(98), null, TestTiming.HangTimeout, Timeout.InfiniteTimeSpan);
        if (arguments.Length > at + 2) File.WriteAllText(arguments[at + 2], "ready");
        int code = Run(arguments[at + 1]);
        Console.Out.Flush();
        Console.Error.Flush();
        Environment.Exit(code);
    }

    private static int Run(string scenario)
    {
        switch (scenario)
        {
            case "silent":
                using (var held = new ManualResetEventSlim()) held.Wait();
                return 0;
            case "stderr":
                Console.Error.Write(new string('e', 256 * 1024));
                Console.Out.Write("done");
                return 0;
            case "output-limit":
                Console.Out.Write(new string('o', 256 * 1024));
                return 0;
            case "nonzero":
                Console.Out.Write("output");
                Console.Error.Write("error");
                return 7;
            case "descendant":
                ProcessStartInfo info = Command("silent");
                info.RedirectStandardOutput = false;
                info.RedirectStandardError = false;
                using (Process child = Process.Start(info)!)
                {
                    Console.Out.WriteLine(child.Id);
                    Console.Out.Flush();
                }

                // The root exits; its descendant retains both inherited pipe handles.
                return 0;
            case "onward-git":
                // Started with empty and relative entries in its own PATH, this process starts git through the containment primitive
                // and through the program resolver's start, each asking git for an external command it would find only through them.
                string directory = Environment.GetEnvironmentVariable(DirectoryVariable)!;
                var contained = new ProcessStartInfo("git") { WorkingDirectory = directory, ArgumentList = { OnwardCommand, "contained" } };
                Vericairn.Runtime.Providers.ProcessResult result = Vericairn.Runtime.Providers.BoundedProcess.Run(contained, TestTiming.HangTimeout);
                var started = new ProcessStartInfo("git")
                {
                    WorkingDirectory = directory, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true,
                    ArgumentList = { OnwardCommand, "started" },
                };
                using (Process process = Vericairn.Records.Processes.ProgramPath.Start(started)!)
                {
                    Task<string> output = process.StandardOutput.ReadToEndAsync();
                    Task<string> error = process.StandardError.ReadToEndAsync();
                    process.WaitForExit();
                    _ = output.GetAwaiter().GetResult() + error.GetAwaiter().GetResult();
                    Console.Out.Write($"{result.Outcome} {result.ExitCode} {process.ExitCode}");
                }

                return 0;
            default:
                return 99;
        }
    }
}
