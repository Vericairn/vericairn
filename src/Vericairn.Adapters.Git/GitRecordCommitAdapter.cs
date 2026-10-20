// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Diagnostics;
using System.Text;
using Vericairn.Records.Estate;
using Vericairn.Records.Hashing;
using Vericairn.Records.Types;
using Vericairn.Runtime.Providers;

namespace Vericairn.Adapters.Git;

/// <summary>Creates a local branch and commits one verified record with its generated members. No remote, credential, hook or signing command is invoked.</summary>
public sealed class GitRecordCommitAdapter : IRecordCommitAdapter
{
    private readonly Func<ProcessStartInfo, ProcessResult> run;

    /// <summary>Each local git process has bounded output and a thirty-second deadline.</summary>
    public GitRecordCommitAdapter() : this(info => BoundedProcess.Run(info, TimeSpan.FromSeconds(30))) { }

    internal GitRecordCommitAdapter(Func<ProcessStartInfo, ProcessResult> run) => this.run = run;

    /// <inheritdoc />
    public RecordCommitBasis Prepare(string directory)
    {
        // A relative prefix avoids confusing filesystem aliases with a directory inside the repository.
        if (Read(directory, "rev-parse", "--show-prefix").Trim().Length != 0)
            throw new ProviderRefusedException("The estate must be the repository root, not a directory inside another repository.");
        if (Read(directory, "rev-parse", "--is-shallow-repository").Trim() != "false")
            throw new ProviderRefusedException("The estate needs complete local history before recording an operator action.");
        string branch = Read(directory, "symbolic-ref", "--short", "HEAD").Trim();
        string head = Head(directory);
        Check(directory, branch, head, new HashSet<string>(StringComparer.Ordinal));
        return new(branch, head);
    }

    /// <inheritdoc />
    public void Branch(string directory, RecordCommitBasis basis, string branch, IReadOnlyDictionary<string, string> files)
    {
        ArgumentNullException.ThrowIfNull(basis);
        ValidateFiles(files);
        ValidBranch(directory, branch);
        string current = Read(directory, "symbolic-ref", "--short", "HEAD").Trim();
        if (current == branch) return; // Commit verifies the base, record bytes and every changed path on recovery.
        RequireBranch(directory, basis.Branch, basis.Head);
        RequireChanges(directory, new HashSet<string>(StringComparer.Ordinal), files.Keys.ToHashSet(StringComparer.Ordinal));
        WithoutHooks(directory, "checkout", "-b", branch, basis.Head);
    }

    /// <inheritdoc />
    public string Commit(string directory, string branch, string basis, IReadOnlyDictionary<string, string> files, IReadOnlySet<string> receipts)
    {
        ArgumentNullException.ThrowIfNull(receipts);
        string record = ValidateFiles(files);
        string head = Head(directory);
        RequireBranch(directory, branch, head);
        RequireChanges(directory, receipts, head == basis ? files.Keys.ToHashSet(StringComparer.Ordinal) : null);
        if (head != basis)
        {
            Verify(directory, head, basis, files);
            return head;
        }

        foreach ((string path, string byteHash) in files)
        {
            string file = Path.Combine(directory, path.Replace('/', Path.DirectorySeparatorChar));
            for (FileSystemInfo? entry = new FileInfo(file); entry is not null && entry.FullName != Path.GetFullPath(directory); entry = new DirectoryInfo(Path.GetDirectoryName(entry.FullName)!))
                if ((entry.Attributes & FileAttributes.ReparsePoint) != 0)
                    throw new ProviderRefusedException("The record and member paths must not traverse a symbolic link.");
            if (ContentHash.Sha256(File.ReadAllBytes(file)) != byteHash)
                throw new ProviderRefusedException("The signed record or generated member bytes changed before the commit.");
        }
        string[] paths = files.Keys.Order(StringComparer.Ordinal).ToArray();
        // --only names precisely the prepared record and members. The record already carries its device signature.
        Read(directory, ["add", "--intent-to-add", "--", .. paths]);
        WithoutHooks(directory, ["commit", "--only", "--no-gpg-sign", "-m", "Record operator action " + Path.GetFileNameWithoutExtension(record), "--", .. paths]);
        head = Head(directory);
        Verify(directory, head, basis, files);
        Check(directory, branch, head, receipts);
        return head;
    }

    /// <inheritdoc />
    public void Check(string directory, string branch, string head, IReadOnlySet<string> receipts)
    {
        ArgumentNullException.ThrowIfNull(receipts);
        RequireBranch(directory, branch, head);
        RequireChanges(directory, receipts, null);
    }

    private void RequireBranch(string directory, string branch, string head)
    {
        ValidBranch(directory, branch);
        if (Head(directory) != head || Read(directory, "symbolic-ref", "--short", "HEAD").Trim() != branch
            || Read(directory, "rev-parse", "--verify", "refs/heads/" + branch).Trim() != head)
            throw new ProviderRefusedException("The operator action's branch or pinned head moved; restore that branch and head before resuming.");
        foreach (string state in new[] { "MERGE_HEAD", "CHERRY_PICK_HEAD", "REVERT_HEAD", "rebase-merge", "rebase-apply", "BISECT_LOG" })
        {
            string location = Read(directory, "rev-parse", "--git-path", state).Trim();
            string full = Path.IsPathRooted(location) ? location : Path.Combine(directory, location);
            if (File.Exists(full) || Directory.Exists(full))
                throw new ProviderRefusedException("Finish the repository's in-progress operation before recording an operator action.");
        }
    }

    private void RequireChanges(string directory, IReadOnlySet<string> receipts, HashSet<string>? files)
    {
        string status = Read(directory, "status", "--porcelain=v1", "-z", "--untracked-files=all", "--ignore-submodules=none");
        foreach (string entry in status.Split('\0', StringSplitOptions.RemoveEmptyEntries))
        {
            if (entry.Length < 4) throw new ProviderRefusedException("The working tree could not be inspected completely.");
            string path = entry[3..];
            if (entry[..2] == "??" && receipts.Contains(path)) continue;
            if (files?.Contains(path) == true && entry[..2] is "??" or " A" or "A " or "AM") continue;
            throw new ProviderRefusedException("The estate working tree holds other changes; settle them separately before resuming this action.");
        }
    }

    /// <inheritdoc />
    public void Verify(string directory, string head, string basis, IReadOnlyDictionary<string, string> files)
    {
        ValidateFiles(files);
        if (!Patterns.Commit.IsMatch(head) || !Patterns.Commit.IsMatch(basis)
            || Read(directory, "rev-list", "--parents", "-n", "1", head).Trim() != head + " " + basis
            || !Read(directory, "diff-tree", "--no-commit-id", "--name-only", "-r", "-z", basis, head).Split('\0', StringSplitOptions.RemoveEmptyEntries)
                .Order(StringComparer.Ordinal).SequenceEqual(files.Keys.Order(StringComparer.Ordinal), StringComparer.Ordinal)
            || files.Any(file => Read(directory, "ls-tree", head, "--", file.Key).Split(' ')[0] != "100644"
                || ContentHash.Sha256(Encoding.UTF8.GetBytes(Read(directory, "show", head + ":" + file.Key))) != file.Value))
            throw new ProviderRefusedException("The branch does not contain exactly the original signed record and generated members on its pinned base; inspect it before resuming.");
    }

    private static string ValidateFiles(IReadOnlyDictionary<string, string> files)
    {
        ArgumentNullException.ThrowIfNull(files);
        string[] records = files.Keys.Where(path => path.StartsWith("records/", StringComparison.Ordinal) && path.EndsWith(".json", StringComparison.Ordinal)).ToArray();
        if (records.Length != 1 || files.Keys.Distinct(StringComparer.OrdinalIgnoreCase).Count() != files.Count
            || files.Any(file => !DocumentPatterns.IsSafePath(file.Key) || !ContentHash.IsValid(file.Value)
                || file.Key != records[0] && !Patterns.DocumentPath.IsMatch(file.Key)))
            throw new ProviderRefusedException("The commit requires one canonical record and its exact generated document paths and byte hashes.");
        return records[0];
    }

    private string Head(string directory)
    {
        string head = Read(directory, "rev-parse", "--verify", "HEAD").Trim();
        return Patterns.Commit.IsMatch(head) ? head : throw new ProviderRefusedException("The repository needs an exact committed head.");
    }

    private void ValidBranch(string directory, string branch)
    {
        if (string.IsNullOrWhiteSpace(branch) || branch.StartsWith('-')) throw new ProviderRefusedException("A named local branch is required.");
        Read(directory, "check-ref-format", "refs/heads/" + branch);
    }

    private void WithoutHooks(string directory, params string[] arguments)
    {
        // An empty temporary hooks directory also prevents checkout hooks, including on Windows.
        string hooks = Path.Combine(Path.GetTempPath(), "vericairn-record-hooks-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(hooks);
        try { Read(directory, ["-c", "core.hooksPath=" + hooks, .. arguments]); }
        finally { Directory.Delete(hooks); }
    }

    private string Read(string directory, params string[] arguments)
    {
        var info = new ProcessStartInfo("git") { WorkingDirectory = directory, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
        info.ArgumentList.Add("--no-replace-objects");
        foreach (string argument in arguments) info.ArgumentList.Add(argument);
        info.Environment["GIT_OPTIONAL_LOCKS"] = "0";
        info.Environment["GIT_TERMINAL_PROMPT"] = "0";
        ProcessResult result = run(info);
        if (result.Outcome != ProcessOutcome.Exited || result.ExitCode != 0)
            throw new ProviderRefusedException("The local git step did not complete. Inspect the retained branch and rerun the same operator command; its existing commit will be verified before any retry.");
        return result.StandardOutput;
    }
}
