// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace Vericairn.Runtime.Tests;

internal static class ProcessHarness
{
    private const string Marker = "--vericairn-process-test";

    internal static ProcessStartInfo Command(string scenario)
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
        return info;
    }

    [ModuleInitializer]
#pragma warning disable CA2255 // The test application hosts a synthetic child before the test platform starts.
    internal static void Intercept()
#pragma warning restore CA2255
    {
        string[] arguments = Environment.GetCommandLineArgs();
        int at = Array.IndexOf(arguments, Marker);
        if (at < 0)
        {
            return;
        }

        // Even the broken sequential reader has a finite test lifetime.
        new Thread(() => { Thread.Sleep(10_000); Environment.Exit(98); }) { IsBackground = true }.Start();
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
                Thread.Sleep(5000);
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
            default:
                return 99;
        }
    }
}
