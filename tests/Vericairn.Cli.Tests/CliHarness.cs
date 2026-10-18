// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using Vericairn.Cli;
using Vericairn.Protocol;
using Vericairn.Records.Identity;
using Vericairn.Records.Signing;
using Vericairn.Records.Updates;
using Vericairn.Runtime.Service;
using Vericairn.Runtime.Workers;
using Vericairn.Sdk;

namespace Vericairn.Cli.Tests;

/// <summary>Runs the command tree in-process with captured output, exactly as the binary would run it.</summary>
internal static class CliHarness
{
    static CliHarness()
    {
        // Live stores and keys of test estates never touch the developer's real per-user state.
        Environment.SetEnvironmentVariable(VericairnHome.EnvironmentVariable, TempDirectory("cli-tests-home"));
        new ReleaseUpdateStore().Configure(false);
    }

    public sealed record Outcome(int Code, string Out, string Err)
    {
        public JsonElement Json => JsonDocument.Parse(Out).RootElement.Clone();

        public string Id => Json.GetProperty("id").GetString()!;

        public string Hash => Json.GetProperty("hash").GetString()!;
    }

    // Some journeys switch homes while running. Always inject a stand-in as well as disabling the
    // default test home's setting, so even a fresh home can never initiate an external request.
    public static Outcome Run(params string[] args) => RunWithUpdates(new(new(), new NoReleaseSource(), TimeProvider.System), args);

    private sealed class NoReleaseSource : IReleaseSource
    {
        public Task<PublishedRelease?> LatestAsync(CancellationToken cancellationToken) => Task.FromResult<PublishedRelease?>(null);
    }

    internal static Outcome RunWithUpdates(ReleaseUpdates? updates, params string[] args)
    {
        var standardOut = new StringWriter { NewLine = "\n" };
        var standardError = new StringWriter { NewLine = "\n" };
        TextWriter previousOut = Console.Out;
        TextWriter previousError = Console.Error;
        Console.SetOut(standardOut);
        Console.SetError(standardError);
        try
        {
            int code = CliApp.Build(updates).Parse(args).Invoke();
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

    /// <summary>Revoke after this journey's signed history using an explicit record time, without a wall-clock sleep.</summary>
    public static RecordWritten RevokeAfterRecords(string root, RevokeRequest request)
    {
        using EstateService service = EstateService.Open(root);
        DateTimeOffset at = service.Session.Load().Records.Max(r => r.AtTime).AddSeconds(1);
        WriteOutcome revoked = service.DeviceRevoke(request, at);
        Assert.True(revoked.Ok, revoked.Problem?.Message);
        return revoked.Written!;
    }

    /// <summary>Runs a command with the text as its standard input.</summary>
    public static Outcome RunWithInput(string input, params string[] args)
    {
        TextReader previousIn = Console.In;
        Console.SetIn(new StringReader(input));
        try
        {
            return Run(args);
        }
        finally
        {
            Console.SetIn(previousIn);
        }
    }

    /// <summary>Runs a command with the text as its standard input; it must succeed.</summary>
    public static Outcome OkWithInput(string input, params string[] args)
    {
        Outcome outcome = RunWithInput(input, args);
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

    /// <summary>Deletes test directories, clearing read-only attributes first so git object files delete on Windows too.</summary>
    public static void Delete(params string[] directories)
    {
        foreach (string directory in directories.Where(d => !string.IsNullOrEmpty(d) && Directory.Exists(d)))
        {
            try
            {
                WorkspaceCleanup.Delete(directory);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // Best effort.
            }
        }
    }
}
