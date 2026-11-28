// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using Vericairn.Records.Processes;
using Vericairn.Runtime.Providers;
using Vericairn.Runtime.Workers;

namespace Vericairn.Adapters.Git;

/// <summary>Worker-owned Git invocations. Never use this against agent-controlled repository configuration.</summary>
internal static class WorkspaceGitCommand
{
    internal static int Run(string directory, out string output, params string[] arguments)
        => Run(directory, out output, TimeSpan.FromMinutes(2), arguments);

    internal static int Run(string directory, out string output, TimeSpan deadline, params string[] arguments)
        => RunWithInput(directory, out output, deadline, null, arguments);

    /// <summary>Runs Git with isolated configuration (<see cref="GitHardening"/>), writing <paramref name="input"/> to its standard input when given.</summary>
    internal static int RunWithInput(string directory, out string output, TimeSpan deadline, string? input, params string[] arguments)
    {
        if (deadline <= TimeSpan.Zero) throw new ProviderRefusedException("Git's observation deadline expired.");
        GitHardening hardening = GitHardening.Create();
        try
        {
            var info = new ProcessStartInfo("git")
            {
                UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, RedirectStandardInput = input is not null,
                CreateNoWindow = true, WorkingDirectory = directory,
            };
            if (input is not null) info.StandardInputEncoding = new UTF8Encoding(false);
            info.Environment.Clear();
            foreach ((string name, string value) in WorkerEnvironment.Build(WorkerEnvironment.Current(), []))
                info.Environment[name] = value;
            // Worker transfers are local: only the file protocol is allowed back after the shared hardening refuses every transport.
            hardening.Apply(info, ["-c", "protocol.file.allow=always", .. arguments]);
            using Process process = ProgramPath.Start(info) ?? throw new ProviderRefusedException("Git did not start.");
            Task<string> standardOutput = WorkspaceGitScope.ReadAsync(process.StandardOutput);
            Task<string> standardError = WorkspaceGitScope.ReadAsync(process.StandardError);
            if (input is not null)
            {
                // Both outputs are already draining, so a large input cannot block on a full output pipe.
                Task writing = process.StandardInput.WriteAsync(input).ContinueWith(_ =>
                {
                    // Git may exit before reading everything; its exit code then reports the failure.
                    try { process.StandardInput.Close(); }
                    catch (IOException) { }
                }, TaskScheduler.Default);
                if (!writing.Wait(deadline))
                {
                    process.Kill(entireProcessTree: true);
                    _ = process.WaitForExit(1000);
                    throw new ProviderRefusedException("Git did not read its input within its deadline.");
                }
            }
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
            hardening.Dispose();
        }
    }

    internal static string Require(string directory, params string[] arguments) => Run(directory, out string output, arguments) == 0
        ? output.TrimEnd('\r', '\n') : throw new ProviderRefusedException("Git could not " + arguments[0] + ": " + output.Trim());
}

/// <summary>A disposable bare view of objects, with only worker-owned configuration and references.</summary>
internal sealed class WorkspaceGitView : IDisposable
{
    private readonly GitHardening hardening;

    internal WorkspaceGitView(string objects, string head)
    {
        if (!WorkspaceGitScope.ObjectId(head)) throw new IOException("The workspace branch head is unavailable.");
        hardening = GitHardening.Create();
        try
        {
            Path = hardening.Acquire(System.IO.Path.GetDirectoryName(objects)!).Directory;
            System.IO.Directory.CreateDirectory(System.IO.Path.Combine(Path, "refs", "heads"));
            File.WriteAllText(System.IO.Path.Combine(Path, "HEAD"), "ref: refs/heads/review\n");
            File.WriteAllText(System.IO.Path.Combine(Path, "refs", "heads", "review"), head + "\n");
        }
        catch
        {
            hardening.Dispose();
            throw;
        }
    }

    internal string Path { get; }

    public void Dispose() => hardening.Dispose();
}
