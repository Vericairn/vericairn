// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Diagnostics;
using Vericairn.Records.Types;
using Vericairn.Runtime.Providers;
using Vericairn.Runtime.Workers;

namespace Vericairn.Adapters.Git;

/// <summary>Explicit, single-ref pushes through git, pinned to a scanned commit and the observed remote head.</summary>
/// <remarks>
/// Every git process runs without the system and global configuration. Remote access runs from a private bare repository that reads
/// the clone's objects but never its configuration, so the clone's address rewrites, transport settings and hooks cannot change
/// where or how a checked push goes. Only the declared remote's protocol is allowed, and the user's own configuration contributes
/// only its credential helpers and the installation's TLS trust settings.
/// </remarks>
public sealed class GitPushAdapter : IPushAdapter
{
    // Canonical names, as git config --get-regexp matches them: section and variable names in lower case.
    private const string Carried = @"^credential\.|^http\.(sslbackend|sslcainfo|sslcapath|schannelcheckrevoke|schannelusesslcainfo)$";

    // Inherited routing must not redirect a read or a push to another repository, object store or configuration.
    private static readonly string[] Routing = ["GIT_DIR", "GIT_WORK_TREE", "GIT_COMMON_DIR", "GIT_OBJECT_DIRECTORY", "GIT_ALTERNATE_OBJECT_DIRECTORIES",
        "GIT_INDEX_FILE", "GIT_NAMESPACE", "GIT_SHALLOW_FILE", "GIT_GRAFT_FILE", "GIT_REPLACE_REF_BASE", "GIT_CONFIG", "GIT_CONFIG_PARAMETERS", "GIT_CONFIG_COUNT"];

    // Derived object data a clone can write for itself: the commit-graph, reachability bitmaps and the multi-pack-index. A checked
    // push establishes its range and fast-forward check from the real objects alone, so every git process the adapter runs ignores
    // these caches; a clone that misreports ancestry in them cannot change where a push goes or what it carries.
    private static readonly string[] IgnoreDerived = ["-c", "core.commitGraph=false", "-c", "pack.useBitmaps=false", "-c", "core.multiPackIndex=false"];

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
        string objects = Objects(directory);
        ProcessResult result = Remote(remote, objects, ["-c", "push.followTags=false", "push", "--porcelain", "--no-follow-tags", "--recurse-submodules=no",
            "--force-with-lease=refs/heads/" + plan.Branch + ":" + (plan.RemoteHead ?? string.Empty), "--", remote, plan.Head + ":refs/heads/" + plan.Branch]);
        if (!result.Started) throw new ProviderRefusedException("git could not be started; nothing was pushed.");
        if (result.Outcome != ProcessOutcome.Exited || result.ExitCode != 0)
            throw new ProviderUncertainException("The push may have taken effect; observe its remote head before any retry.");
    }

    /// <inheritdoc />
    public string? Observe(string directory, string remote, string branch)
    {
        Branch(directory, branch);
        ProcessResult result = Remote(remote, null, ["ls-remote", "--heads", "--", remote, "refs/heads/" + branch]);
        if (result.Outcome != ProcessOutcome.Exited || result.ExitCode != 0)
            throw new ProviderRefusedException("git could not establish the exact push range or remote head.");
        string text = result.StandardOutput.Trim();
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

    // The clone's shared object store, which the private repository reads for a push. Reading it writes nothing to the clone.
    private string Objects(string directory)
    {
        string common = Read(directory, "rev-parse", "--path-format=absolute", "--git-common-dir").Trim();
        if (!Path.IsPathFullyQualified(common)) throw new ProviderRefusedException("git could not establish the clone's object store.");
        return Path.Combine(common, "objects");
    }

    // Local reads in the clone: no system or global configuration, no hooks, and no transport for lazily fetched objects.
    private ProcessResult Invoke(string directory, string[] arguments)
    {
        ProcessStartInfo info = Start(directory, ["-c", "protocol.allow=never", .. arguments]);
        Isolate(info, []);
        return run(info);
    }

    // Remote access from a private bare repository with only the declared remote's protocol. It loads none of the clone's
    // configuration, attributes or hooks; a push reads the clone's objects through the object directory alone.
    private ProcessResult Remote(string remote, string? objects, string[] arguments)
    {
        string protocol = Protocol(remote);
        string control = Path.Combine(Path.GetTempPath(), "vericairn-push-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(Path.Combine(control, "objects"));
            Directory.CreateDirectory(Path.Combine(control, "refs"));
            File.WriteAllText(Path.Combine(control, "HEAD"), "ref: refs/heads/unused\n");
            File.WriteAllText(Path.Combine(control, "config"), "[core]\nrepositoryformatversion = 0\nbare = true\n");
            List<KeyValuePair<string, string>> settings = Settings(control);
            ProcessStartInfo info = Start(control, ["--git-dir=" + control, "-c", "core.hooksPath=" + Path.Combine(control, "hooks"),
                "-c", "protocol.allow=never", "-c", "protocol." + protocol + ".allow=always", .. arguments]);
            Isolate(info, settings);
            if (objects is not null) info.Environment["GIT_OBJECT_DIRECTORY"] = objects;
            return run(info);
        }
        finally
        {
            if (Directory.Exists(control)) WorkspaceCleanup.Delete(control);
        }
    }

    // A checked push goes to GitHub over https or, for local work and tests, to a repository on this machine.
    private static string Protocol(string remote)
    {
        if (remote.StartsWith("https://", StringComparison.Ordinal)) return "https";
        if (Path.IsPathFullyQualified(remote) && !remote.Contains("://", StringComparison.Ordinal)) return "file";
        throw new ProviderRefusedException("A checked push goes only to an https remote or a repository on this machine.");
    }

    // The user's credential helpers and TLS trust settings from the system and global levels (including a platform's own level,
    // which git reports as unknown), in git's reading order so that an empty helper still resets the list before it.
    private List<KeyValuePair<string, string>> Settings(string control)
    {
        ProcessStartInfo info = Start(control, ["--git-dir=" + control, "config", "--show-scope", "-z", "--get-regexp", Carried]);
        foreach (string name in Routing) info.Environment.Remove(name);
        foreach (string name in info.Environment.Keys.Where(Indexed).ToArray()) info.Environment.Remove(name);
        ProcessResult result = run(info);
        if (result is { Outcome: ProcessOutcome.Exited, ExitCode: 1 } && result.StandardOutput.Length == 0) return [];
        if (result.Outcome != ProcessOutcome.Exited || result.ExitCode != 0)
            throw new ProviderRefusedException("git could not read the user's credential settings; nothing was pushed.");
        var settings = new List<KeyValuePair<string, string>>();
        string[] fields = result.StandardOutput.Split('\0');
        for (int i = 0; i + 1 < fields.Length; i += 2)
        {
            if (fields[i] is not ("system" or "global" or "unknown")) continue;
            // A name without a value is git's implicit true, which an environment setting can only state explicitly.
            int split = fields[i + 1].IndexOf('\n', StringComparison.Ordinal);
            settings.Add(split < 0 ? new(fields[i + 1], "true") : new(fields[i + 1][..split], fields[i + 1][(split + 1)..]));
        }
        return settings;
    }

    private static ProcessStartInfo Start(string directory, string[] arguments)
    {
        var info = new ProcessStartInfo("git") { WorkingDirectory = directory, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
        info.ArgumentList.Add("--no-replace-objects");
        foreach (string setting in IgnoreDerived) info.ArgumentList.Add(setting);
        foreach (string argument in arguments) info.ArgumentList.Add(argument);
        return info;
    }

    private static void Isolate(ProcessStartInfo info, List<KeyValuePair<string, string>> settings)
    {
        foreach (string name in Routing) info.Environment.Remove(name);
        foreach (string name in info.Environment.Keys.Where(Indexed).ToArray()) info.Environment.Remove(name);
        info.Environment["GIT_CONFIG_NOSYSTEM"] = "1";
        info.Environment["GIT_CONFIG_GLOBAL"] = string.Empty;
        info.Environment["GIT_ATTR_NOSYSTEM"] = "1";
        info.Environment["GIT_NO_REPLACE_OBJECTS"] = "1";
        info.Environment["GIT_NO_LAZY_FETCH"] = "1";
        info.Environment["GIT_TERMINAL_PROMPT"] = "0";
        info.Environment["GIT_OPTIONAL_LOCKS"] = "0";
        if (settings.Count == 0) return;
        info.Environment["GIT_CONFIG_COUNT"] = settings.Count.ToString(System.Globalization.CultureInfo.InvariantCulture);
        for (int i = 0; i < settings.Count; i++)
        {
            info.Environment["GIT_CONFIG_KEY_" + i.ToString(System.Globalization.CultureInfo.InvariantCulture)] = settings[i].Key;
            info.Environment["GIT_CONFIG_VALUE_" + i.ToString(System.Globalization.CultureInfo.InvariantCulture)] = settings[i].Value;
        }
    }

    private static bool Indexed(string name) =>
        name.StartsWith("GIT_CONFIG_KEY_", StringComparison.OrdinalIgnoreCase) || name.StartsWith("GIT_CONFIG_VALUE_", StringComparison.OrdinalIgnoreCase);
}
