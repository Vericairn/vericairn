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

    // A clone's info/grafts rewrites parents as replace objects do, and turning replace objects off does not disable it: every git
    // process the adapter runs reads grafts from a file that never exists instead, so its range, fast-forward check and message read
    // follow the real objects alone.
    private static readonly string NoGrafts = Path.Combine(Path.GetTempPath(), "vericairn-no-grafts-" + Guid.NewGuid().ToString("N"), "grafts");

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
    /// <remarks>
    /// Every added line and every message of every commit the push carries that its target branch does not already hold is read,
    /// whatever the clone's settings and attributes, or the read returns null (FIX-137):
    /// <list type="bullet">
    /// <item>the messages in UTF-8 whatever the clone's output encoding, NUL-separated, with no signature verification, notes or mailmap;</item>
    /// <item>each commit's own diff against its parent, so text one commit adds and a later one removes is still read; for a merge, its
    /// dense combined diff, whose lines added against every parent are the merge's own text, while a line one parent already holds was
    /// added by a commit of that parent, read on its own or already in the repository;</item>
    /// <item>every file as text, so neither a NUL byte, a <c>-diff</c> or binary attribute, committed or the clone's own, nor a low
    /// <c>core.bigFileThreshold</c> turns its added lines into "Binary files differ", with no rename or copy detection, external diff or
    /// text conversion;</item>
    /// <item>a diff line is a file header only before the first hunk of its file, so an added line whose own text begins with "++" is read;</item>
    /// <item>and each added line holding NUL characters, as a UTF-16 or UTF-32 file's lines do, read a second time with them removed, after
    /// the rest, so that held text stored in such a file reads as its letters.</item>
    /// </list>
    /// </remarks>
    public string? PublishedText(string directory, PushPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (!Patterns.Commit.IsMatch(plan.Base) || !Patterns.Commit.IsMatch(plan.Head) || !Patterns.Commit.IsMatch(plan.TargetHead)) return null;
        string[] range = [plan.Head, "^" + plan.Base, "^" + plan.TargetHead];
        ProcessResult messages = Invoke(directory, ["-c", "i18n.logOutputEncoding=UTF-8", "log", "--no-show-signature", "--no-mailmap", "--no-notes", "-z",
            "--format=%H%n%B", .. range]);
        ProcessResult patches = Invoke(directory, ["-c", "core.bigFileThreshold=" + TextThreshold, "-c", "i18n.logOutputEncoding=UTF-8", "log", "--no-show-signature",
            "--no-mailmap", "--no-notes", "--format=%x01%H", "-p", "--cc", "--text", "--no-renames", "--no-ext-diff", "--no-textconv", "--unified=0", "--no-color", .. range]);
        if (messages is not { Outcome: ProcessOutcome.Exited, ExitCode: 0 } || patches is not { Outcome: ProcessOutcome.Exited, ExitCode: 0 }) return null;
        var text = new System.Text.StringBuilder();
        var commits = new HashSet<string>(StringComparer.Ordinal);
        foreach (string entry in messages.StandardOutput.Split('\0'))
        {
            if (entry.Length == 0) continue;
            int split = entry.IndexOf('\n', StringComparison.Ordinal);
            string id = split < 0 ? entry : entry[..split];
            if (!Patterns.Commit.IsMatch(id) || !commits.Add(id)) return null;
            text.Append(split < 0 ? string.Empty : entry[(split + 1)..]).Append('\n');
        }
        var stripped = new System.Text.StringBuilder();
        var patched = new HashSet<string>(StringComparer.Ordinal);
        // Outside a hunk the columns are zero: a file's header runs from its diff line to its first hunk. A hunk header's at signs give
        // the columns, one for each parent: a line is added when every column is a plus, and its text follows them.
        int columns = 0;
        foreach (string line in patches.StandardOutput.Split('\n'))
        {
            if (line.StartsWith('\u0001'))
            {
                if (!Patterns.Commit.IsMatch(line[1..]) || !patched.Add(line[1..])) return null;
                columns = 0;
            }
            else if (line.StartsWith("diff --git ", StringComparison.Ordinal) || line.StartsWith("diff --cc ", StringComparison.Ordinal)
                || line.StartsWith("diff --combined ", StringComparison.Ordinal)) columns = 0;
            else if (line.StartsWith("@@", StringComparison.Ordinal))
            {
                columns = line.TakeWhile(c => c == '@').Count() - 1;
                if (columns < 1) return null;
            }
            else if (columns == 0)
            {
                if (line.StartsWith("Binary files ", StringComparison.Ordinal)) return null;
            }
            else if (line.Length >= columns && line.Take(columns).All(c => c == '+'))
            {
                string added = line[columns..];
                text.Append('\n').Append(added);
                if (added.Contains('\0', StringComparison.Ordinal)) stripped.Append('\n').Append(added.Replace("\0", string.Empty, StringComparison.Ordinal));
            }
        }
        // Every commit's patch was read: a commit whose patch is missing could carry text nothing read.
        if (!patched.SetEquals(commits)) return null;
        return text.Append('\n').Append(stripped).ToString();
    }

    // The size up to which a diff reads a file as text, git's default; a larger file's diff exceeds the bounded output and is unreadable.
    private const string TextThreshold = "512m";

    /// <inheritdoc />
    public IReadOnlyList<PushedCommit>? CommitMessages(string directory, PushPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (!Patterns.Commit.IsMatch(plan.Base) || !Patterns.Commit.IsMatch(plan.Head) || !Patterns.Commit.IsMatch(plan.TargetHead)) return null;
        // Every commit of the range the target's head does not hold, merges included, NUL-separated: its id, a line break and its raw
        // message, in UTF-8 whatever the clone's own output encoding, with no signature verification, mailmap or notes.
        ProcessResult log = Invoke(directory, ["-c", "i18n.logOutputEncoding=UTF-8", "log", "--no-show-signature", "--no-mailmap", "--no-notes", "-z",
            "--format=%H%n%B", plan.Head, "^" + plan.Base, "^" + plan.TargetHead]);
        if (log is not { Outcome: ProcessOutcome.Exited, ExitCode: 0 }) return null;
        var commits = new List<PushedCommit>();
        foreach (string entry in log.StandardOutput.Split('\0'))
        {
            if (entry.Length == 0) continue;
            int split = entry.IndexOf('\n', StringComparison.Ordinal);
            string id = split < 0 ? entry : entry[..split];
            if (!Patterns.Commit.IsMatch(id)) return null;
            commits.Add(new(id, split < 0 ? string.Empty : entry[(split + 1)..]));
        }
        return commits;
    }

    /// <inheritdoc />
    public IReadOnlyDictionary<string, IReadOnlyList<string>>? ChangedPaths(string directory, PushPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (!Patterns.Commit.IsMatch(plan.Base) || !Patterns.Commit.IsMatch(plan.Head) || !Patterns.Commit.IsMatch(plan.TargetHead)) return null;
        // The commits CommitMessages reads, each as a header line of its id, then the paths it changes, NUL-separated and unquoted,
        // without renames, external diffs or text conversion; a merge commit names only the paths that differ from every one of its
        // parents, as a combined diff does. git separates the header from the first path by one line break, which is all that is removed.
        ProcessResult names = Invoke(directory, ["log", "--no-show-signature", "-z", "--no-renames", "--no-ext-diff", "--no-textconv", "--name-only", "--cc",
            "--format=%x01%H", plan.Head, "^" + plan.Base, "^" + plan.TargetHead]);
        if (names is not { Outcome: ProcessOutcome.Exited, ExitCode: 0 }) return null;
        var paths = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
        List<string>? current = null;
        bool header = false;
        foreach (string token in names.StandardOutput.Split('\0'))
        {
            if (token.StartsWith('\u0001'))
            {
                string id = token[1..];
                if (!Patterns.Commit.IsMatch(id) || paths.ContainsKey(id)) return null;
                paths[id] = current = [];
                header = true;
                continue;
            }
            if (token.Length == 0)
            {
                header = false;
                continue;
            }
            if (current is null || header && !token.StartsWith('\n')) return null;
            string path = header ? token[1..] : token;
            header = false;
            if (path.Length > 0) current.Add(path);
        }
        return paths;
    }

    /// <inheritdoc />
    public string? FileText(string directory, PushPlan plan, string commit, string path)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(path);
        if (!Patterns.Commit.IsMatch(commit) || commit != plan.Head && commit != plan.TargetHead)
            throw new ProviderRefusedException("A push reads files only at its head or its target branch's head.");
        if (!Patterns.RelativePath.IsMatch(path)) throw new ProviderRefusedException("A push reads a file by its normalized repository-relative path.");
        // The tree entry first, so that a missing path reads as absent and a failed read refuses. Only the object's own bytes are read:
        // no filter, text conversion or attribute of the clone applies.
        ProcessResult listed = Invoke(directory, ["ls-tree", "-z", "--full-tree", commit, "--", path]);
        if (listed is not { Outcome: ProcessOutcome.Exited, ExitCode: 0 }) throw new ProviderRefusedException("git could not read the push's files.");
        string entry = listed.StandardOutput.TrimEnd('\0');
        if (entry.Length == 0) return null;
        int tab = entry.IndexOf('\t', StringComparison.Ordinal);
        string[] fields = tab < 0 ? [] : entry[..tab].Split(' ');
        if (fields.Length != 3 || entry[(tab + 1)..] != path || entry.Contains('\0', StringComparison.Ordinal))
            throw new ProviderRefusedException("git could not read the push's files.");
        if (fields[1] != "blob") return null;
        ProcessResult blob = Invoke(directory, ["cat-file", "blob", fields[2]]);
        if (blob is not { Outcome: ProcessOutcome.Exited, ExitCode: 0 }) throw new ProviderRefusedException("git could not read the push's files.");
        return blob.StandardOutput;
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
        info.Environment["GIT_GRAFT_FILE"] = NoGrafts;
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
