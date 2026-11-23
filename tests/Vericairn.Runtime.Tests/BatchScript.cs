// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Diagnostics;

namespace Vericairn.Runtime.Tests;

/// <summary>From 0.9.4 (FIX-165): Windows batch programs for the tests that start one, written as cmd.exe reads a batch file.</summary>
internal static class BatchScript
{
    /// <summary>Writes a batch program of <paramref name="lines"/>, with CRLF line ends, and returns its path.</summary>
    internal static string Write(string directory, string name, params string[] lines)
    {
        string path = Path.Combine(directory, name);
        File.WriteAllText(path, string.Join("\r\n", lines) + "\r\n");
        return path;
    }

    /// <summary>
    /// A batch line starting <paramref name="command"/>, each part quoted. A batch file reads % as the start of a variable, so no part
    /// may hold one; the paths the tests use do not.
    /// </summary>
    internal static string Line(IEnumerable<string> command)
    {
        string[] parts = command.ToArray();
        Assert.DoesNotContain(parts, part => part.Contains('%', StringComparison.Ordinal) || part.Contains('"', StringComparison.Ordinal));
        return string.Join(' ', parts.Select(part => "\"" + part + "\""));
    }

    /// <summary>
    /// The lines that start the stand-in harness as a descendant that runs until it is ended, and wait until it has published
    /// <paramref name="report"/>, which names its process: a signal, so the script exits only once its descendant runs.
    /// </summary>
    internal static string[] Descendant(string report) =>
    [
        "start \"\" /b " + Line(FakeHarness.Command("ignore-stop", report)),
        ":wait",
        $"if not exist \"{report}\" goto wait",
    ];

    /// <summary>The descendant's process, by the report it published; open it before anything is to end it.</summary>
    internal static Process DescendantProcess(string report) =>
        Process.GetProcessById(Assert.Single(FakeHarness.Reports(report))["process"]!.GetValue<int>());

    /// <summary>Whether the descendant that published <paramref name="report"/> has ended, waiting for it within the hang ceiling.</summary>
    internal static bool DescendantEnded(string report)
    {
        try
        {
            using Process process = DescendantProcess(report);
            return process.WaitForExit((int)TestTiming.HangTimeout.TotalMilliseconds);
        }
        catch (ArgumentException)
        {
            return true; // It was ended and is gone already.
        }
    }
}
