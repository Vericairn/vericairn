// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Diagnostics;
using Vericairn.Adapters.Git;
using Vericairn.Runtime.Providers;
using Vericairn.Runtime.Service;

namespace Vericairn.Daemon.Tests;

/// <summary>Makes a test estate's root a git repository with one commit, and lets its runtime observe it, as the estate's clone is.</summary>
internal static class EstateGit
{
    private static readonly string[] Options = ["-c", "core.autocrlf=false", "-c", "user.name=Test", "-c", "user.email=test@example.invalid", "-c", "commit.gpgsign=false"];

    /// <summary>Commits everything under <paramref name="root"/> and registers the git adapter with the service; returns the head.</summary>
    internal static string Init(string root, EstateService service)
    {
        Run(root, "init", "-q");
        Run(root, "config", "core.autocrlf", "false");
        string head = Commit(root, "Initial fixture");
        var providers = new ProviderRegistry();
        providers.Register(new GitCommandAdapter());
        service.Providers = providers;
        return head;
    }

    /// <summary>Commits every change and returns the new head.</summary>
    internal static string Commit(string root, string message)
    {
        Run(root, "add", "-A");
        Run(root, "commit", "-qm", message, "--allow-empty");
        return Run(root, "rev-parse", "HEAD").Trim();
    }

    internal static string Run(string root, params string[] arguments)
    {
        var start = new ProcessStartInfo("git") { WorkingDirectory = root, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (string argument in Options.Concat(arguments)) start.ArgumentList.Add(argument);
        using Process process = Process.Start(start)!;
        string output = process.StandardOutput.ReadToEnd();
        string error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        Assert.True(process.ExitCode == 0, output + error);
        return output;
    }
}
