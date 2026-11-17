// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Diagnostics;

namespace Vericairn.TestSupport;

/// <summary>
/// A repository whose own configuration names a program that git can run while reading it: an fsmonitor hook, a clean filter, a
/// signature verifier, a hooks directory or a pager. Every program only writes a marker file outside the repository. Reading the
/// repository through Vericairn must leave the marker absent; <see cref="Control"/> shows that plain git does run the program.
/// </summary>
internal sealed class RepositoryProgramProbe : IDisposable
{
    internal const string Fsmonitor = "fsmonitor";
    internal const string Filter = "filter";
    internal const string Signature = "signature";
    internal const string Hooks = "hooks";
    internal const string Pager = "pager";

    private static readonly string[] Identity = ["-c", "user.name=Test", "-c", "user.email=test@example.invalid", "-c", "commit.gpgsign=false"];
    private static readonly DateTime Earlier = new(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    /// <summary>
    /// A committed repository with a tag, a commit carrying a signature header, a file its attributes send through a filter driver,
    /// one modified file and one untracked file. Nothing is configured yet: <see cref="Arm"/> adds the program.
    /// </summary>
    internal RepositoryProgramProbe(string kind)
    {
        Kind = kind;
        Root = Path.Combine(Path.GetTempPath(), "vericairn-probe-" + Guid.NewGuid().ToString("N"));
        Repository = Path.Combine(Root, "repository");
        Marker = Path.Combine(Root, "marker");
        Directory.CreateDirectory(Repository);
        Directory.CreateDirectory(Path.Combine(Root, "programs"));
        Git("init", "-q", "-b", "main");
        Git("config", "core.autocrlf", "false");
        Write("README.md", "widget\n");
        Write("widget.dat", "widget data\n");
        Write(".gitattributes", "*.dat filter=probe\n");
        Git("add", ".");
        Git("commit", "-q", "-m", "First commit");
        Git("tag", "v0.1.0");
        string commit = Path.Combine(Root, "signed-commit");
        File.WriteAllText(commit, "tree " + Git("rev-parse", "HEAD^{tree}") + "\nparent " + Git("rev-parse", "HEAD")
            + "\nauthor Test <test@example.invalid> 1700000000 +0000\ncommitter Test <test@example.invalid> 1700000000 +0000"
            + "\ngpgsig -----BEGIN PGP SIGNATURE-----\n \n iQ==\n -----END PGP SIGNATURE-----\n\nSigned commit\n");
        Git("update-ref", "refs/heads/main", Git("hash-object", "-t", "commit", "-w", "--no-filters", commit));
        File.AppendAllText(Path.Combine(Repository, "README.md"), "changed\n");
        Write("notes.txt", "untracked\n");
    }

    internal string Kind { get; }

    internal string Root { get; }

    internal string Repository { get; }

    internal string Marker { get; }

    /// <summary>True when a configured program ran.</summary>
    internal bool Ran => File.Exists(Marker);

    /// <summary>Configures the program, then makes tracked files look changed so that git reads them again.</summary>
    internal void Arm()
    {
        string command = "printf ran > '" + Marker.Replace('\\', '/').Replace("'", "'\\''", StringComparison.Ordinal) + "'";
        switch (Kind)
        {
            case Fsmonitor:
                Git("config", "core.fsmonitor", command);
                break;
            case Filter:
                Git("config", "filter.probe.clean", command);
                Git("config", "filter.probe.required", "true");
                break;
            case Signature:
                Git("config", "log.showSignature", "true");
                Git("config", "gpg.program", Program("verify", command));
                break;
            case Hooks:
                string hooks = Path.Combine(Root, "programs", "hooks");
                Directory.CreateDirectory(hooks);
                foreach (string hook in new[] { "post-index-change", "reference-transaction", "post-checkout", "pre-commit", "post-commit", "pre-auto-gc", "fsmonitor-watchman" })
                    Program(Path.Combine("hooks", hook), command);
                Git("config", "core.hooksPath", hooks.Replace('\\', '/'));
                break;
            case Pager:
                Git("config", "core.pager", command);
                foreach (string name in new[] { "status", "log", "tag", "config", "rev-list" }) Git("config", "pager." + name, command);
                break;
            default:
                throw new InvalidOperationException("Unknown probe " + Kind);
        }

        File.SetLastWriteTimeUtc(Path.Combine(Repository, "README.md"), Earlier);
        File.SetLastWriteTimeUtc(Path.Combine(Repository, "widget.dat"), Earlier);
    }

    /// <summary>
    /// Runs plain git in the way that reaches the program and reports whether it ran, removing the marker; null for the pager, which
    /// git starts only on a terminal.
    /// </summary>
    internal bool? Control()
    {
        if (Kind == Pager) return null;
        File.Delete(Marker);
        File.SetLastWriteTimeUtc(Path.Combine(Repository, "widget.dat"), Earlier);
        _ = Run(Kind == Signature ? ["log", "-1"] : ["status", "--porcelain"], out _, out _);
        bool ran = Ran;
        File.Delete(Marker);
        return ran;
    }

    public void Dispose()
    {
        try
        {
            if (!Directory.Exists(Root)) return;
            // Git writes its objects read-only, which Windows refuses to delete.
            foreach (string file in Directory.EnumerateFiles(Root, "*", SearchOption.AllDirectories))
                File.SetAttributes(file, FileAttributes.Normal);
            Directory.Delete(Root, recursive: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Best effort.
        }
    }

    private string Program(string name, string command)
    {
        string path = Path.Combine(Root, "programs", name);
        File.WriteAllText(path, "#!/bin/sh\n" + command + "\n");
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        return path.Replace('\\', '/');
    }

    private void Write(string name, string content) => File.WriteAllText(Path.Combine(Repository, name), content);

    private string Git(params string[] arguments)
    {
        Assert.True(Run(arguments, out string output, out string error) == 0, "git " + string.Join(' ', arguments) + " failed: " + error);
        return output.Trim();
    }

    private int Run(string[] arguments, out string output, out string error)
    {
        var info = new ProcessStartInfo("git") { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true, WorkingDirectory = Repository };
        foreach (string argument in Identity.Concat(arguments)) info.ArgumentList.Add(argument);
        info.Environment.Remove("GIT_OPTIONAL_LOCKS");
        using Process process = Process.Start(info) ?? throw new InvalidOperationException("git did not start.");
        Task<string> diagnostics = process.StandardError.ReadToEndAsync();
        output = process.StandardOutput.ReadToEnd();
        Assert.True(process.WaitForExit(TestTiming.HangTimeoutMilliseconds), "The fixture process did not exit.");
        error = diagnostics.Result;
        return process.ExitCode;
    }
}
