// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Diagnostics;
using System.Globalization;
using Vericairn.Records.Model;
using Vericairn.Runtime.Providers;
using Vericairn.Runtime.Workers;

namespace Vericairn.Adapters.Git;

/// <summary>The git adapter over the git command on the PATH, with read-only observations and isolated worker workspaces.</summary>
public sealed partial class GitCommandAdapter : ISourceAdapter
{
    private readonly Func<ProcessStartInfo, ProcessResult> run;

    /// <summary>Observes git with a thirty-second deadline and bounded output for each command.</summary>
    public GitCommandAdapter() : this(info => BoundedProcess.Run(info, TimeSpan.FromSeconds(30)))
    {
    }

    internal GitCommandAdapter(Func<ProcessStartInfo, ProcessResult> run, IWorkspaceProvider? workspaces = null)
    {
        this.run = run;
        Workspaces = workspaces ?? new GitWorktreeWorkspaces();
    }

    /// <inheritdoc />
    public ProviderDescriptor Descriptor { get; } = new(
        "git-command",
        ISourceAdapter.Contract,
        "implemented",
        ["inspect", "read-file-at-hash", "workspaces"],
        ["Requires git on the PATH.", "Observations are read-only. Worker workspaces have independent administration and retain audited branches locally after settlement; the adapter never publishes."]);

    /// <inheritdoc />
    public IWorkspaceProvider Workspaces { get; }

    /// <inheritdoc />
    public byte[]? ReadFileAtHash(string root, string relativePath, string hash) => GitDocumentHistory.Read(root, relativePath, hash);

    /// <inheritdoc />
    public byte[]? ReadFileAtHash(string root, string relativePath, string hash, string? hashMode) => GitDocumentHistory.Read(root, relativePath, hash, hashMode);

    /// <inheritdoc />
    public bool IsRepository(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (!Directory.Exists(path)) return false;
        using GitHardening hardening = Harden();
        AcquisitionGitView view;
        try { (view, _) = Acquire(hardening, path, null); }
        catch (ProviderRefusedException) { return false; }
        return Git(hardening, view.Directory, out _, "rev-parse", "--verify", "HEAD^{commit}") == 0;
    }

    /// <inheritdoc />
    public RepositoryObservation Inspect(string path) => Inspect(path, null);

    /// <inheritdoc />
    public RepositoryObservation Inspect(string path, string? boundary)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (!Directory.Exists(path)) throw new ProviderRefusedException("The selected repository directory does not exist.");

        using GitHardening hardening = Harden();
        (AcquisitionGitView view, AcquisitionConfiguration settings) = Acquire(hardening, path, boundary);
        string git = view.Directory;
        if (Git(hardening, git, out string head, "rev-parse", "--verify", "HEAD^{commit}") != 0)
        {
            throw new ProviderRefusedException("The repository has no commits yet; make the first commit, then adopt it.");
        }

        string commit = head.Trim();
        string? branch = Git(hardening, git, out string branchText, "symbolic-ref", "--short", "-q", "HEAD") == 0 ? branchText.Trim() : null;
        string? remote = settings.Remote;
        Git(hardening, git, out string roots, "rev-list", "--max-parents=0", "HEAD");
        string rootCommit = roots.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(r => r.Trim()).Order(StringComparer.Ordinal).FirstOrDefault() ?? commit;
        string status = view.Bare ? string.Empty : Status(hardening, view, settings)
            ?? throw new ProviderRefusedException("git could not read the working tree completely; repository observation is incomplete.");
        int modified = 0;
        int untracked = 0;
        string[] entries = status.Split('\0', StringSplitOptions.RemoveEmptyEntries);
        for (int i = 0; i < entries.Length; i++)
        {
            string line = entries[i];
            if (line.StartsWith("??", StringComparison.Ordinal))
            {
                untracked++;
            }
            else
            {
                modified++;
                if (line.Length > 2 && (line[0] is 'R' or 'C' || line[1] is 'R' or 'C')) i++;
            }
        }

        Git(hardening, git, out string tagText, "tag", "--list");
        List<string> tags = tagText.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(t => t.Trim()).Order(StringComparer.Ordinal).ToList();
        Git(hardening, git, out string log, "log", "-n", "10", "--no-show-signature", "--format=%H%x1f%cI%x1f%s");
        var recent = new List<RecentCommit>();
        foreach (string line in log.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            string[] parts = line.Split('\x1f');
            if (parts.Length == 3 && DateTimeOffset.TryParse(parts[1], CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out DateTimeOffset at))
            {
                recent.Add(new RecentCommit(parts[0].Trim(), RecordParser.FormatTimestamp(at), parts[2].Trim()));
            }
        }

        return new RepositoryObservation(commit, branch, remote, rootCommit, modified == 0 && untracked == 0, modified, untracked, tags, recent, RecordParser.FormatTimestamp(DateTimeOffset.UtcNow))
        {
            IgnoredConfiguration = settings.Findings,
            ConfigurationHash = settings.ConfigurationHash,
            WorktreeConfigurationHash = settings.WorktreeConfigurationHash,
            GitRedirections = view.Redirections,
        };
    }

    private static GitHardening Harden()
    {
        try
        {
            return GitHardening.Create();
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            throw new ProviderRefusedException("git cannot run isolated: a temporary directory could not be created; repository observation is incomplete.", error);
        }
    }

    internal (AcquisitionGitView View, AcquisitionConfiguration Settings) Acquire(GitHardening hardening, string path, string? boundary, bool includeObjects = true)
    {
        try
        {
            var view = new AcquisitionGitView(path, boundary, Path.Combine(hardening.Control, "repository"), includeObjects);
            var settings = new AcquisitionConfiguration(view, configuration =>
                Git(hardening, view.Directory, out string entries, "config", "--file", configuration, "--no-includes", "--null", "--list") == 0
                    ? entries : throw new ProviderRefusedException("Git could not parse the repository's configuration as data."));
            view.SetObjectFormat(settings.ObjectFormat);
            return (view, settings);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            throw new ProviderRefusedException("Could not read the repository's Git metadata safely: " + error.Message, error);
        }
    }

    // Status only compares copies of the working tree and the index. There is no Git process in the source working tree.
    private string? Status(GitHardening hardening, AcquisitionGitView view, AcquisitionConfiguration settings)
    {
        GitWorkingTree tree;
        try { tree = WorkingTree(hardening, view, settings); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException)
        {
            throw new ProviderRefusedException("Git could not read the working tree completely: " + error.Message, error);
        }

        // --ignore-submodules=all keeps Git from finding or reading any submodule's repository.
        return Git(hardening, tree.TopLevel, tree, out string status, out string warnings, "status", "--porcelain=v1", "-z", "--untracked-files=all", "--ignore-submodules=all") == 0
            && !warnings.Contains("could not open directory", StringComparison.Ordinal) ? status : null;
    }

    internal GitWorkingTree WorkingTree(GitHardening hardening, AcquisitionGitView view, AcquisitionConfiguration settings)
    {
        string mirror = Path.Combine(hardening.Control, "working-tree");
        view.CopyIndex();
        // List cached entries in the bare administration before Git gets a working directory to compare them against.
        // --sparse keeps sparse-directory entries visible, even when their trees have no files.
        (HashSet<string> submodules, bool sparse, HashSet<string> tracked) = ReadIndexNames("--sparse");
        if (sparse)
        {
            (HashSet<string> gitlinks, _, HashSet<string> files) = ReadIndexNames();
            submodules.UnionWith(gitlinks);
            tracked.UnionWith(files);
        }
        Directory.CreateDirectory(mirror);
        List<string> options = ["--git-dir=" + view.Directory, "--work-tree=" + mirror, "-c", "core.bare=false"];
        if (File.Exists(Path.Combine(view.Directory, "info", "sparse-checkout")))
            options.AddRange(["-c", "core.sparseCheckout=true"]);
        foreach (string setting in settings.Comparison)
        {
            options.Add("-c");
            options.Add(setting);
        }
        // Untracked files keep their names, which is all status reads of them; their content is copied only within the limits.
        var withheld = new HashSet<string>(tracked.Comparer);
        view.CopyWorkingTree(mirror, submodules, tracked, withheld);
        var tree = new GitWorkingTree(mirror, Path.Combine(view.Directory, "index"), options, withheld);
        if (withheld.Count > 0)
        {
            // Git itself must count every withheld name as untracked (ignored ones included); a name it takes for a tracked path,
            // through a spelling the comparison above did not fold, refuses the read instead of hiding that path's change.
            if (Git(hardening, mirror, tree, out string others, out _, "ls-files", "--others", "-z") != 0)
                throw new ProviderRefusedException("Git could not read the working tree completely: its untracked files could not be listed.");
            var untracked = new HashSet<string>(others.Split('\0', StringSplitOptions.RemoveEmptyEntries), tracked.Comparer);
            if (withheld.FirstOrDefault(path => !untracked.Contains(path)) is string counted)
                throw new ProviderRefusedException($"Could not read the working tree at {System.Text.Json.JsonSerializer.Serialize(counted)}: Git counts it as tracked, and it is a link, a special or hard-linked file, or exceeds the acquisition limit.");
        }

        return tree;

        (HashSet<string> Gitlinks, bool Sparse, HashSet<string> Tracked) ReadIndexNames(params string[] extra)
        {
            var info = new ProcessStartInfo("git") { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true,
                CreateNoWindow = true, WorkingDirectory = view.Directory };
            hardening.Apply(info, ["--git-dir=" + view.Directory, "ls-files", "--cached", "--stage", "-z", .. extra]);
            ProcessResult index = run(info);
            if (index.Outcome != ProcessOutcome.Exited || index.ExitCode != 0
                || index.StandardOutput.Length != 0 && index.StandardOutput[^1] != '\0')
                throw new ProviderRefusedException("Git could not read the working tree completely: its copied index is unavailable.");

            bool insensitive = OperatingSystem.IsWindows() || settings.Comparison.Contains("core.ignorecase=true", StringComparer.OrdinalIgnoreCase);
            // Git on macOS precomposes names unless the repository turns it off.
            bool precompose = OperatingSystem.IsMacOS() && !settings.Comparison.Contains("core.precomposeunicode=false", StringComparer.OrdinalIgnoreCase)
                || settings.Comparison.Contains("core.precomposeunicode=true", StringComparer.OrdinalIgnoreCase);
            var names = new GitPathComparer(precompose, insensitive);
            var gitlinks = new HashSet<string>(names);
            var files = new HashSet<string>(names);
            bool sparseIndex = false;
            foreach (string entry in index.StandardOutput.Split('\0', StringSplitOptions.RemoveEmptyEntries))
            {
                int tab = entry.IndexOf('\t', StringComparison.Ordinal);
                if (tab < 0) throw new ProviderRefusedException("Git could not read the working tree completely: its copied index is malformed.");
                string path = entry[(tab + 1)..];
                bool directory = entry.StartsWith("040000 ", StringComparison.Ordinal) && path.EndsWith('/');
                string name = directory ? path[..^1] : path;
                if (!AcquisitionWorkingTree.IsIndexPath(name))
                    throw new ProviderRefusedException("The copied Git index contains an unsafe path " + System.Text.Json.JsonSerializer.Serialize(path) + ".");
                sparseIndex |= directory;
                if (entry.StartsWith("160000 ", StringComparison.Ordinal)) gitlinks.Add(name);
                else if (!directory) files.Add(name);
            }
            return (gitlinks, sparseIndex, files);
        }
    }

    private int Git(GitHardening hardening, string path, out string output, params string[] arguments) => Git(hardening, path, null, out output, out _, arguments);

    private int Git(GitHardening hardening, string path, GitWorkingTree? tree, out string output, out string error, params string[] arguments)
    {
        var info = new ProcessStartInfo("git") { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true, WorkingDirectory = path };
        if (tree is null) hardening.Apply(info, ["--git-dir=" + path, "-c", "core.quotepath=off", .. arguments]);
        else hardening.Apply(info, tree, ["-c", "core.quotepath=off", .. arguments]);
        ProcessResult result = run(info);
        if (result.Outcome != ProcessOutcome.Exited)
        {
            throw new ProviderRefusedException(result.Outcome == ProcessOutcome.NotStarted
                ? "git could not be started; install git on the PATH to adopt and observe repositories."
                : $"git {result.Description}; repository observation is incomplete.");
        }

        output = result.StandardOutput;
        error = result.StandardError;
        return result.ExitCode!.Value;
    }
}
