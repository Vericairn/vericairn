// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jayson Furr

using System.Text.Json;
using Vericairn.Cli;
using Vericairn.Records.Identity;
using Vericairn.Records.Signing;

namespace Vericairn.Cli.Tests;

/// <summary>Runs the command tree in-process with captured output, exactly as the binary would run it.</summary>
internal static class CliHarness
{
    static CliHarness()
    {
        // Live stores and keys of test estates never touch the developer's real per-user state.
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable(VericairnHome.EnvironmentVariable)))
        {
            Environment.SetEnvironmentVariable(VericairnHome.EnvironmentVariable, TempDirectory("cli-tests-home"));
        }
    }

    public sealed record Outcome(int Code, string Out, string Err)
    {
        public JsonElement Json => JsonDocument.Parse(Out).RootElement.Clone();

        public string Id => Json.GetProperty("id").GetString()!;

        public string Hash => Json.GetProperty("hash").GetString()!;
    }

    public static Outcome Run(params string[] args)
    {
        var standardOut = new StringWriter { NewLine = "\n" };
        var standardError = new StringWriter { NewLine = "\n" };
        TextWriter previousOut = Console.Out;
        TextWriter previousError = Console.Error;
        Console.SetOut(standardOut);
        Console.SetError(standardError);
        try
        {
            int code = CliApp.Build().Parse(args).Invoke();
            return new Outcome(code, standardOut.ToString(), standardError.ToString());
        }
        finally
        {
            Console.SetOut(previousOut);
            Console.SetError(previousError);
        }
    }

    /// <summary>Runs a command that must succeed and prints JSON.</summary>
    public static Outcome Ok(params string[] args)
    {
        Outcome outcome = Run(args);
        Assert.True(outcome.Code == 0, $"vericairn {string.Join(' ', args)} exited {outcome.Code}\n{outcome.Out}\n{outcome.Err}");
        return outcome;
    }

    public static string TempDirectory(string purpose)
    {
        string path = Path.Combine(Path.GetTempPath(), $"vericairn-{purpose}-{Ulid.NewUlid().Value}");
        Directory.CreateDirectory(path);
        return path;
    }

    public static string WriteJson(string directory, string name, string json)
    {
        string path = Path.Combine(directory, name);
        File.WriteAllText(path, json);
        return path;
    }

    /// <summary>A fresh clone: the repository's tracked content copied to a new directory.</summary>
    public static string Clone(string source, string purpose)
    {
        string destination = TempDirectory(purpose);
        foreach (string file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            string relative = Path.GetRelativePath(source, file);
            string target = Path.Combine(destination, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target);
        }

        return destination;
    }

    /// <summary>A merge: record files that exist in the source and not in the destination are added; nothing is ever overwritten.</summary>
    public static int MergeRecords(string source, string destination)
    {
        int added = 0;
        foreach (string file in Directory.EnumerateFiles(Path.Combine(source, "records"), "*.json", SearchOption.AllDirectories))
        {
            string relative = Path.GetRelativePath(source, file);
            string target = Path.Combine(destination, relative);
            if (File.Exists(target))
            {
                continue;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target);
            added++;
        }

        return added;
    }

    public static void Delete(params string[] directories)
    {
        foreach (string directory in directories.Where(d => !string.IsNullOrEmpty(d)))
        {
            try
            {
                Directory.Delete(directory, recursive: true);
            }
            catch (IOException)
            {
                // Best effort.
            }
        }
    }
}
