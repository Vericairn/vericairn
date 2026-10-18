// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.ComponentModel;
using System.Diagnostics;
using Vericairn.Runtime.Providers;
using Vericairn.Runtime.Workers;

namespace Vericairn.Packs.Software;

/// <summary>Worker-owned Git invocations. Never use this against agent-controlled repository configuration.</summary>
internal static class WorkspaceGitCommand
{
    internal static int Run(string directory, out string output, params string[] arguments)
        => Run(directory, out output, TimeSpan.FromMinutes(2), arguments);

    internal static int Run(string directory, out string output, TimeSpan deadline, params string[] arguments)
    {
        if (deadline <= TimeSpan.Zero) throw new ProviderRefusedException("Git's observation deadline expired.");
        string control = Path.Combine(Path.GetTempPath(), "vericairn-git-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(control);
        try
        {
            File.WriteAllText(Path.Combine(control, "config"), string.Empty);
            var info = new ProcessStartInfo("git")
            {
                UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true,
                CreateNoWindow = true, WorkingDirectory = directory,
            };
            info.ArgumentList.Add("--no-pager");
            foreach (string setting in new[]
            {
                "core.hooksPath=" + Path.Combine(control, "hooks"), "core.fsmonitor=false", "core.pager=",
                "core.sshCommand=", "core.alternateRefsCommand=", "diff.external=", "diff.trustExitCode=false",
                "protocol.allow=never", "protocol.file.allow=always", "gc.auto=0", "maintenance.auto=false",
                "fetch.writeCommitGraph=false", "submodule.recurse=false",
                // Configuration isolation cannot inherit a Windows installation's long-path opt-in. Staging refs can
                // exceed MAX_PATH even when the published workspace fits; keep this non-executing option explicit.
                "core.longpaths=true",
            })
            {
                info.ArgumentList.Add("-c");
                info.ArgumentList.Add(setting);
            }
            foreach (string argument in arguments) info.ArgumentList.Add(argument);
            info.Environment.Clear();
            foreach ((string name, string value) in WorkerEnvironment.Build(WorkerEnvironment.Current(), []))
                info.Environment[name] = value;
            info.Environment["GIT_CONFIG_NOSYSTEM"] = "1";
            info.Environment["GIT_CONFIG_GLOBAL"] = Path.Combine(control, "config");
            info.Environment["GIT_ATTR_NOSYSTEM"] = "1";
            info.Environment["GIT_TERMINAL_PROMPT"] = "0";
            info.Environment["GIT_OPTIONAL_LOCKS"] = "0";
            info.Environment["GIT_NO_REPLACE_OBJECTS"] = "1";
            info.Environment["GIT_NO_LAZY_FETCH"] = "1";
            using Process process = Process.Start(info) ?? throw new ProviderRefusedException("Git did not start.");
            Task<string> standardOutput = WorkspaceGitScope.ReadAsync(process.StandardOutput);
            Task<string> standardError = WorkspaceGitScope.ReadAsync(process.StandardError);
            if (!process.WaitForExit(deadline))
            {
                process.Kill(entireProcessTree: true);
                _ = process.WaitForExit(1000);
                throw new ProviderRefusedException("Git did not answer within its deadline.");
            }
            Task.WhenAll(standardOutput, standardError).WaitAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();
            output = process.ExitCode == 0 ? standardOutput.Result : standardError.Result + standardOutput.Result;
            return process.ExitCode;
        }
        catch (Win32Exception exception)
        {
            throw new ProviderRefusedException("Git is not on the PATH; install it to prepare workspaces.", exception);
        }
        catch (TimeoutException exception)
        {
            throw new ProviderRefusedException("Git output did not close within its deadline.", exception);
        }
        finally
        {
            WorkspaceCleanup.Delete(control);
        }
    }

    internal static string Require(string directory, params string[] arguments) => Run(directory, out string output, arguments) == 0
        ? output.TrimEnd('\r', '\n') : throw new ProviderRefusedException("Git could not " + arguments[0] + ": " + output.Trim());
}

/// <summary>A disposable bare view of objects, with only worker-owned configuration and references.</summary>
internal sealed class WorkspaceGitView : IDisposable
{
    internal WorkspaceGitView(string objects, string head)
    {
        if (!WorkspaceGitScope.ObjectId(head)) throw new IOException("The workspace branch head is unavailable.");
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "vericairn-git-view-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(System.IO.Path.Combine(Path, "objects", "info"));
        Directory.CreateDirectory(System.IO.Path.Combine(Path, "refs", "heads"));
        File.WriteAllText(System.IO.Path.Combine(Path, "HEAD"), "ref: refs/heads/review\n");
        File.WriteAllText(System.IO.Path.Combine(Path, "refs", "heads", "review"), head + "\n");
        File.WriteAllText(System.IO.Path.Combine(Path, "objects", "info", "alternates"), objects.Replace('\\', '/') + "\n");
        File.WriteAllText(System.IO.Path.Combine(Path, "config"), head.Length == 64
            ? "[core]\nrepositoryformatversion = 1\nbare = true\n[extensions]\nobjectformat = sha256\n"
            : "[core]\nrepositoryformatversion = 0\nbare = true\n");
    }

    internal string Path { get; }

    public void Dispose() => WorkspaceCleanup.Delete(Path);
}
