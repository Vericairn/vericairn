// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Diagnostics;
using Vericairn.Records.Types;
using Vericairn.Runtime.Providers;

namespace Vericairn.Adapters.Git;

/// <summary>Explicit, single-ref pushes through git, pinned to a scanned commit and the observed remote head.</summary>
public sealed class GitPushAdapter : IPushAdapter
{
    private readonly Func<ProcessStartInfo, ProcessResult> run;

    /// <summary>Each git process has bounded output and a two-minute deadline.</summary>
    public GitPushAdapter() : this(info => BoundedProcess.Run(info, TimeSpan.FromMinutes(2))) { }

    internal GitPushAdapter(Func<ProcessStartInfo, ProcessResult> run) => this.run = run;

    /// <inheritdoc />
    public PushPlan Prepare(string directory, string remote, string branch, string target, string head)
    {
        if (!Patterns.Commit.IsMatch(head)) throw new ProviderRefusedException("A push needs an exact head.");
        if (Read(directory, "rev-parse", "--is-shallow-repository").Trim() != "false")
            throw new ProviderRefusedException("A checked push requires complete local history; a shallow clone cannot establish every commit in its scan range.");
        Branch(directory, branch);
        Branch(directory, target);
        string local = Read(directory, "rev-parse", "--verify", "refs/heads/" + branch + "^{commit}").Trim();
        if (local != head || Read(directory, "rev-parse", "--verify", "HEAD").Trim() != head)
            throw new ProviderRefusedException("The local branch or checked-out head moved.");
        string? remoteHead = Observe(directory, remote, branch);
        string targetHead = Observe(directory, remote, target) ?? throw new ProviderRefusedException("The remote target branch does not exist.");
        string basis;
        if (remoteHead is not null)
        {
            if (Invoke(directory, ["merge-base", "--is-ancestor", remoteHead, head]) is not { Outcome: ProcessOutcome.Exited, ExitCode: 0 })
                throw new ProviderRefusedException("The remote branch is not a known ancestor of this head; fetch and reconcile outside this operation.");
            basis = remoteHead;
        }
        else
        {
            basis = Read(directory, "merge-base", targetHead, head).Trim();
        }
        if (!Patterns.Commit.IsMatch(basis)) throw new ProviderRefusedException("The exact scan base could not be established.");
        return new(branch, target, head, basis, remoteHead, targetHead);
    }

    /// <inheritdoc />
    public void Push(string directory, string remote, PushPlan plan, SecretScan scan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(scan);
        if (!scan.Passed(plan)) throw new ProviderRefusedException("A push requires a successful scan of exactly its commits.");
        if (Prepare(directory, remote, plan.Branch, plan.Target, plan.Head) != plan)
            throw new ProviderRefusedException("The push's remote expectation or scan range moved; prepare and scan again.");
        ProcessResult result = Invoke(directory, ["-c", "push.followTags=false", "push", "--porcelain", "--no-follow-tags", "--recurse-submodules=no",
            "--force-with-lease=refs/heads/" + plan.Branch + ":" + (plan.RemoteHead ?? string.Empty), "--", remote, plan.Head + ":refs/heads/" + plan.Branch]);
        if (!result.Started) throw new ProviderRefusedException("git could not be started; nothing was pushed.");
        if (result.Outcome != ProcessOutcome.Exited || result.ExitCode != 0)
            throw new ProviderUncertainException("The push may have taken effect; observe its remote head before any retry.");
    }

    /// <inheritdoc />
    public string? Observe(string directory, string remote, string branch)
    {
        Branch(directory, branch);
        string text = Read(directory, "ls-remote", "--heads", "--", remote, "refs/heads/" + branch).Trim();
        if (text.Length == 0) return null;
        string[] fields = text.Split(['\t', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries);
        if (fields.Length != 2 || fields[1] != "refs/heads/" + branch || !Patterns.Commit.IsMatch(fields[0]))
            throw new ProviderRefusedException("The remote branch observation was incomplete.");
        return fields[0];
    }

    private void Branch(string directory, string branch)
    {
        if (string.IsNullOrWhiteSpace(branch) || branch.StartsWith('-') || branch.Length > 200
            || Invoke(directory, ["check-ref-format", "refs/heads/" + branch]) is not { Outcome: ProcessOutcome.Exited, ExitCode: 0 })
            throw new ProviderRefusedException("A valid explicit branch name is required.");
    }

    private string Read(string directory, params string[] arguments)
    {
        ProcessResult result = Invoke(directory, arguments);
        if (result.Outcome != ProcessOutcome.Exited || result.ExitCode != 0)
            throw new ProviderRefusedException("git could not establish the exact push range or remote head.");
        return result.StandardOutput;
    }

    private ProcessResult Invoke(string directory, string[] arguments)
    {
        var info = new ProcessStartInfo("git") { WorkingDirectory = directory, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
        info.ArgumentList.Add("--no-replace-objects");
        foreach (string argument in arguments) info.ArgumentList.Add(argument);
        info.Environment["GIT_TERMINAL_PROMPT"] = "0";
        info.Environment["GIT_OPTIONAL_LOCKS"] = "0";
        return run(info);
    }
}
