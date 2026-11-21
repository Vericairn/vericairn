// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

namespace Vericairn.Runtime.Providers;

/// <summary>The exact commit range and remote expectation admitted for a push; contains no machine path.</summary>
public sealed record PushPlan(string Branch, string Target, string Head, string Base, string? RemoteHead, string TargetHead)
{
    /// <summary>The commits the scanner must examine, excluding the base.</summary>
    public string Range => Base + ".." + Head;
}

/// <summary>A scan's portable facts. There is deliberately no place for findings, matched text or process output.</summary>
public sealed record SecretScan(string Tool, string Version, string Range, string Verdict, int? FindingCount)
{
    /// <summary>Only an exact, successful, zero-finding scan authorizes this plan.</summary>
    public bool Passed(PushPlan plan) => plan is not null && Range == plan.Range && Verdict == "passed" && FindingCount == 0
        && !string.IsNullOrWhiteSpace(Tool) && !string.IsNullOrWhiteSpace(Version) && Version != "unknown";
}

/// <summary>One commit a push carries, by its exact id, with its whole message, trailers included (CODE-81).</summary>
public sealed record PushedCommit(string Commit, string Message);

/// <summary>The secret scan contract, version 1. Implementations scan exactly the plan's commits and return only aggregate facts.</summary>
public interface ISecretScanner
{
    /// <summary>The contract version.</summary>
    const string Contract = "vericairn.scanner.secrets/1";

    /// <summary>Scans an exact range in the local clone. A missing scanner, timeout or incomplete scan never passes.</summary>
    SecretScan Scan(string directory, PushPlan plan);
}

/// <summary>The push adapter contract, version 1. Preparation reads the remote; the push is pinned to its head and a remote lease.</summary>
public interface IPushAdapter
{
    /// <summary>The contract version.</summary>
    const string Contract = "vericairn.adapter.push/1";

    /// <summary>Resolves the branch, remote expectation and scan base; refuses non-fast-forward updates or unavailable objects.</summary>
    /// <remarks>
    /// From 0.9.4 (FIX-154) the runtime calls the overload that takes the records' heads instead; an implementation of this one alone
    /// takes no checked push. The git adapter's form without them scans from the merge base with the target's observed head.
    /// </remarks>
    PushPlan Prepare(string directory, string remote, string branch, string target, string head);

    /// <summary>Rechecks the plan and scan, then pushes only the pinned commit under the remote expectation.</summary>
    /// <remarks>From 0.9.4 (FIX-154) the runtime calls the overload that takes the records' heads instead.</remarks>
    void Push(string directory, string remote, PushPlan plan, SecretScan scan);

    /// <summary>
    /// Resolves the branch, remote expectation and scan base from the records' heads (FIX-154): <paramref name="recorded"/> holds the
    /// heads of the branch's complete recorded pushes, newest first, at most sixteen. The base is the first of them that the remote
    /// branch, as observed now, still contains and the new head descends from, otherwise the merge base of the new head with the target
    /// branch's observed head, including when a recorded head is missing from the clone or its ancestry cannot be read; never the remote
    /// branch's head as such. A push to the target branch itself, a non-fast-forward update or unavailable objects refuse.
    /// </summary>
    /// <remarks>
    /// The default refuses, so an adapter written before 0.9.4 takes no checked push rather than choosing a base of its own: a checked
    /// push covers every commit its target does not hold.
    /// </remarks>
    PushPlan Prepare(string directory, string remote, string branch, string target, string head, IReadOnlyList<string> recorded) =>
        throw new ProviderRefusedException("The push adapter cannot take the scan base the records give, which a checked push needs from 0.9.4; nothing was pushed.");

    /// <summary>
    /// Rechecks the plan, prepared again from the same <paramref name="recorded"/> heads, and the scan, then pushes only the pinned commit
    /// under the remote expectation (FIX-154). The default refuses, as <see cref="Prepare(string, string, string, string, string, IReadOnlyList{string})"/> does.
    /// </summary>
    void Push(string directory, string remote, PushPlan plan, SecretScan scan, IReadOnlyList<string> recorded) =>
        throw new ProviderRefusedException("The push adapter cannot take the scan base the records give, which a checked push needs from 0.9.4; nothing was pushed.");

    /// <summary>The remote branch's current head, or null when it does not exist.</summary>
    string? Observe(string directory, string remote, string branch);

    /// <summary>
    /// The text the plan's commits would publish: their messages and the lines they add. Null when the adapter cannot read it,
    /// which refuses a push while a disclosure hold is in force.
    /// </summary>
    string? PublishedText(string directory, PushPlan plan) => null;

    /// <summary>
    /// The text of one file, by its repository-relative path, at a commit the plan names: its head or its target branch's head. Null
    /// when that commit holds no file at the path, or when the adapter reads no files, which never lets a push pass a disclosure hold;
    /// a read that fails refuses. From 0.9.4 (FIX-137) a checked push reads whether it carries a release's documentation set this way.
    /// </summary>
    string? FileText(string directory, PushPlan plan, string commit, string path) => null;

    /// <summary>
    /// Every commit the plan's range carries that its target branch's head does not already hold, merge commits included, each with
    /// its whole message: a commit the target already holds is in the repository already, so taking the target into a branch adds
    /// none of its commits. Null when the adapter cannot read them, which refuses a push while the estate checks commit messages for
    /// blocked terms or required trailers (CODE-81, from 0.9.4).
    /// </summary>
    IReadOnlyList<PushedCommit>? CommitMessages(string directory, PushPlan plan) => null;

    /// <summary>
    /// The repository-relative paths each commit of <see cref="CommitMessages"/> changes, by its exact id, exactly as git names them; for
    /// a merge commit, only those that differ from every one of its parents. Read on its own, so a list of paths too long to read never
    /// keeps the messages from being checked. A checked push reads them only to tell a release's documentation set from other changes
    /// (FIX-137); null when the adapter cannot read them, which never lets a set pass.
    /// </summary>
    IReadOnlyDictionary<string, IReadOnlyList<string>>? ChangedPaths(string directory, PushPlan plan) => null;
}
