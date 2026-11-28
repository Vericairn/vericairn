// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using Vericairn.Records.Estate;
using Vericairn.Records.Hashing;
using Vericairn.Records.Processes;
using Vericairn.Runtime.Providers;

namespace Vericairn.Adapters.Git;

/// <summary>Exact document bytes from the estate's own local refs, with no checkout, fetch or retained copy.</summary>
internal static class GitDocumentHistory
{
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(30);
    private const int OutputLimit = 64 * 1024 * 1024;

    internal static byte[]? Read(string root, string relativePath, string hash, string? hashMode = null)
    {
        string[] parts = relativePath.Split('/');
        if (hashMode is not (null or RevisionLines.HashMode) || !Directory.Exists(root) || !ContentHash.IsValid(hash) || parts.Length < 2 || parts[0] is not ("docs" or "design") ||
            parts.Any(p => p is "" or "." or ".." || p.Contains('\\', StringComparison.Ordinal)))
        {
            return null;
        }

        var watch = Stopwatch.StartNew();
        using GitHardening hardening = GitHardening.Create();
        AcquisitionGitView view;
        try { view = hardening.Acquire(root); }
        catch (ProviderRefusedException) { return null; }
        // Do not search a containing repository, or accept a shallow clone even when a blob happens to remain.
        if (view.Bare || view.Root != Vericairn.Runtime.Workers.WorkspacePaths.Canonical(root) ||
            Text("rev-parse", "--is-shallow-repository") != "false")
        {
            return null;
        }

        string literal = ":(literal)" + relativePath;
        string? commits = Text("log", "--all", "--full-history", "--no-show-signature", "--no-notes", "--no-decorate", "--no-color", "--format=%H", "--", literal);
        if (commits is null)
        {
            return null;
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (string commit in commits.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            string? entry = Text("ls-tree", "-z", commit, "--", literal);
            if (entry is null)
            {
                return null;
            }
            int tab = entry.IndexOf('\t');
            if (tab < 0)
            {
                continue; // The path was deleted at this commit.
            }
            string[] metadata = entry[..tab].Split(' ');
            if (metadata.Length != 3 || metadata[0] is not ("100644" or "100755") || metadata[1] != "blob" || !seen.Add(metadata[2]))
            {
                continue;
            }

            byte[]? bytes = Run(hardening, root, watch, "cat-file", "blob", metadata[2]);
            if (bytes is null)
            {
                return null;
            }
            if (hashMode == RevisionLines.HashMode)
            {
                try { bytes = RevisionLines.AuthoredContent(bytes); }
                catch (Exception error) when (error is InvalidOperationException or ArgumentException) { continue; }
            }
            if (ContentHash.Sha256(bytes) == hash)
            {
                return bytes;
            }
        }
        return null;

        string? Text(params string[] arguments) => Run(hardening, root, watch, arguments) is { } bytes
            ? Encoding.UTF8.GetString(bytes).TrimEnd('\r', '\n') : null;
    }

    private static byte[]? Run(GitHardening hardening, string root, Stopwatch watch, params string[] arguments)
    {
        TimeSpan remaining = Deadline - watch.Elapsed;
        if (remaining <= TimeSpan.Zero)
        {
            return null;
        }
        ProcessStartInfo info = GitFileSource.StartInfo(root, hardening, arguments);
        try
        {
            using Process process = ProgramPath.Start(info) ?? throw new IOException("Git did not start.");
            using var timeout = new CancellationTokenSource(remaining);
            try
            {
                Task<byte[]> output = Capture(process.StandardOutput.BaseStream, timeout.Token);
                Task<byte[]> error = Capture(process.StandardError.BaseStream, timeout.Token);
                Task.WhenAll(output, error, process.WaitForExitAsync(timeout.Token)).WaitAsync(timeout.Token).GetAwaiter().GetResult();
                return process.ExitCode == 0 ? output.Result : null;
            }
            catch (Exception exception) when (exception is IOException or OperationCanceledException)
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }
                return null;
            }
        }
        catch (Exception exception) when (exception is Win32Exception or IOException or InvalidOperationException)
        {
            return null;
        }
    }

    private static async Task<byte[]> Capture(Stream stream, CancellationToken cancellationToken)
    {
        using var bytes = new MemoryStream();
        byte[] buffer = new byte[8192];
        int read;
        while ((read = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) != 0)
        {
            if (bytes.Length + read > OutputLimit)
            {
                throw new IOException("Git history output exceeded its limit.");
            }
            bytes.Write(buffer, 0, read);
        }
        return bytes.ToArray();
    }
}
