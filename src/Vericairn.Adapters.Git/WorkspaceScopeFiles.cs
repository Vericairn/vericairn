// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Diagnostics;
using System.Security.Cryptography;

namespace Vericairn.Adapters.Git;

/// <summary>
/// Repository ignore rules and tracked names, without loading agent-controlled Git configuration. Before a run they are the
/// workspace's own. At settlement they are the run's starting rules, which the run could not change: the base commit's
/// committed ignore files and the info/exclude captured before the agent started, never the ignore files, info/exclude or
/// index the agent left.
/// </summary>
internal sealed class WorkspaceScopeFiles
{
    private const long Limit = 64 * 1024 * 1024;
    private readonly HashSet<string> ignored;
    private readonly HashSet<string> tracked;

    /// <summary>Before a run: the workspace's ignore rules and index.</summary>
    internal WorkspaceScopeFiles(string workspace, string directory, TimeSpan deadline)
    {
        var watch = Stopwatch.StartNew();
        string head = WorkspaceGitScope.Read(Path.Combine(directory, "HEAD"), 4096).Trim();
        if (head.StartsWith("ref: ", StringComparison.Ordinal)) head = WorkspaceGitScope.Head(directory, head[5..]);
        using var view = new WorkspaceGitView(Path.Combine(directory, "objects"), head);
        // Use a copy: even optional index refreshes must not write the workspace's administration.
        CopyIndex(directory, view.Path, deadline, watch);
        string? exclude = ReadExclude(directory);
        ExcludeHash = Hash(exclude);
        if (exclude is not null)
        {
            Directory.CreateDirectory(Path.Combine(view.Path, "info"));
            File.WriteAllText(Path.Combine(view.Path, "info", "exclude"), exclude);
        }
        tracked = Names(view.Path, workspace, deadline, watch, "--cached");
        ignored = Names(view.Path, workspace, deadline, watch, "--others", "--ignored", "--exclude-standard", "--directory");
    }

    /// <summary>
    /// At settlement: only the starting rules decide what is excluded. An ignore file that differs from them is reported in
    /// <see cref="Changed"/> and never applied; a run cannot hide a new file by adding a rule or untracking a committed one.
    /// </summary>
    internal WorkspaceScopeFiles(string workspace, string directory, string start, string? startExclude, TimeSpan deadline)
    {
        var watch = Stopwatch.StartNew();
        using var view = new WorkspaceGitView(Path.Combine(directory, "objects"), start);
        string shadow = Path.Combine(view.Path, "rules");
        Directory.CreateDirectory(shadow);
        // Not the user's global ignore file, which is outside the workspace and the run could change.
        string noExcludes = Path.Combine(view.Path, "no-excludes");
        File.WriteAllText(noExcludes, string.Empty);
        CopyIndex(directory, view.Path, deadline, watch);
        tracked = Names(view.Path, workspace, deadline, watch, "--cached");

        // The base commit's tracked names stay tracked, and its regular ignore files are the rules, checked out byte for byte.
        var committed = new List<string>();
        if (WorkspaceGitCommand.Run(view.Path, out string tree, Remaining(deadline, watch), "--literal-pathspecs", "ls-tree", "-r", "-z", "--full-tree", "review") != 0)
            throw new IOException("The run's base tree could not be read.");
        foreach (string entry in tree.Split('\0', StringSplitOptions.RemoveEmptyEntries))
        {
            int tab = entry.IndexOf('\t', StringComparison.Ordinal);
            string[] fields = tab > 0 ? entry[..tab].Split(' ') : [];
            string name = tab > 0 ? entry[(tab + 1)..] : string.Empty;
            if (fields.Length != 3 || !WorkspaceScopeAudit.Relative(name) || name.Contains('�', StringComparison.Ordinal))
                throw new IOException("The run's base tree names could not be read losslessly.");
            tracked.Add(name);
            if (fields[1] == "blob" && fields[0] is "100644" or "100755" && IgnoreFile(name)) committed.Add(name);
        }
        if (tracked.Count > 200_000 || committed.Count > 10_000) throw new IOException("The run's base tree exceeded its limit.");
        if (committed.Count > 0 && WorkspaceGitCommand.RunWithInput(view.Path, out string checkout, Remaining(deadline, watch), string.Join('\0', committed) + "\0",
                "--literal-pathspecs", "--git-dir=" + view.Path, "--work-tree=" + shadow, "-c", "core.bare=false", "-c", "core.autocrlf=false",
                "checkout", "review", "--pathspec-from-file=-", "--pathspec-file-nul") != 0)
            throw new IOException("The run's starting ignore rules could not be read: " + checkout.Trim());

        var changed = new SortedSet<string>(StringComparer.Ordinal);
        string? exclude = ReadExclude(directory);
        if (startExclude is not null && Hash(exclude) == startExclude)
        {
            if (exclude is not null)
            {
                Directory.CreateDirectory(Path.Combine(view.Path, "info"));
                File.WriteAllText(Path.Combine(view.Path, "info", "exclude"), exclude);
            }
        }
        else if (startExclude is not null || exclude is not null) changed.Add(".git/info/exclude");

        ignored = new HashSet<string>(StringComparer.Ordinal);
        var trackedDirectories = new HashSet<string>(StringComparer.Ordinal);
        foreach (string name in tracked)
            for (int slash = name.IndexOf('/', StringComparison.Ordinal); slash > 0; slash = name.IndexOf('/', slash + 1)) trackedDirectories.Add(name[..slash]);
        var present = new HashSet<string>(StringComparer.Ordinal);
        List<(string Name, bool Directory)> level = Entries(workspace, "").ToList();
        int seen = 0;
        while (level.Count > 0)
        {
            seen += level.Count;
            if (seen > 100_000 || watch.Elapsed >= deadline) throw new IOException("The scope ignore inventory reached its work or time limit.");
            // check-ignore learns that a name is a directory from the rules tree, so directory-only rules apply as Git applies them.
            foreach ((string name, bool isDirectory) in level)
                if (isDirectory) Directory.CreateDirectory(Path.Combine(shadow, name));
            // check-ignore takes no literal pathspecs; a leading ./ keeps a name that begins with a colon from being read as magic.
            if (WorkspaceGitCommand.RunWithInput(shadow, out string output, Remaining(deadline, watch), string.Concat(level.Select(e => "./" + e.Name + "\0")),
                    "--git-dir=" + view.Path, "--work-tree=" + shadow, "-c", "core.bare=false", "-c", "core.excludesFile=" + noExcludes,
                    "check-ignore", "--no-index", "--stdin", "-z") is not (0 or 1))
                throw new IOException("The run's starting ignore rules could not be applied.");
            var matched = new HashSet<string>(output.Split('\0', StringSplitOptions.RemoveEmptyEntries)
                .Select(name => name.StartsWith("./", StringComparison.Ordinal) ? name[2..] : name), StringComparer.Ordinal);
            var next = new List<(string Name, bool Directory)>();
            foreach ((string name, bool isDirectory) in level)
            {
                if (IgnoreFile(name) && !matched.Contains(name) && name != ".vericairn/.gitignore") present.Add(name);
                // Git still lists tracked files below an ignored directory; descend there and decide each child.
                if (matched.Contains(name) && !tracked.Contains(name) && !(isDirectory && trackedDirectories.Contains(name)))
                {
                    ignored.Add(isDirectory ? name + "/" : name);
                    continue;
                }
                if (isDirectory) next.AddRange(Entries(Path.Combine(workspace, name), name + "/"));
            }
            level = next;
        }

        foreach (string name in committed.Union(present, StringComparer.Ordinal))
        {
            if (!present.Contains(name) || !File.Exists(Path.Combine(shadow, name)) || !Same(Path.Combine(workspace, name), Path.Combine(shadow, name)))
                changed.Add(name);
        }
        Changed = changed.ToArray();
    }

    /// <summary>The hash of info/exclude when these rules were read, so settlement can tell whether the run changed it.</summary>
    internal string? ExcludeHash { get; }

    /// <summary>At settlement, the ignore files that differ from the run's starting rules. Settlement reports them and never applies them.</summary>
    internal IReadOnlyList<string> Changed { get; } = [];

    /// <summary>Directories excluded whole, which settlement still searches for nested repositories.</summary>
    internal IEnumerable<string> IgnoredDirectories => ignored.Where(name => name.EndsWith('/')).Select(name => name.TrimEnd('/'));

    internal bool RunFile(string name) => !tracked.Contains(name) && name is ".vericairn/brief.md" or ".vericairn/.gitignore";

    internal bool Excludes(string name)
    {
        if (tracked.Contains(name)) return false;
        if (RunFile(name) || ignored.Contains(name) || ignored.Contains(name + "/")) return true;
        for (int slash = name.LastIndexOf('/'); slash >= 0; slash = name.LastIndexOf('/', slash - 1))
        {
            if (ignored.Contains(name[..(slash + 1)])) return true;
            if (slash == 0) break;
        }
        return false;
    }

    private static bool IgnoreFile(string name) => name == ".gitignore" || name.EndsWith("/.gitignore", StringComparison.Ordinal);

    private static TimeSpan Remaining(TimeSpan deadline, Stopwatch watch) => deadline - watch.Elapsed;

    private static IEnumerable<(string Name, bool Directory)> Entries(string directory, string prefix)
    {
        foreach (string path in Directory.EnumerateFileSystemEntries(directory))
        {
            string leaf = Path.GetFileName(path);
            if (leaf == ".git") continue;
            string name = prefix + leaf;
            if (!WorkspaceScopeAudit.Relative(name) || name.Contains('�', StringComparison.Ordinal))
                throw new IOException("An inventory name could not be decoded losslessly or safely.");
            FileAttributes attributes = File.GetAttributes(path);
            yield return (name, (attributes & FileAttributes.Directory) != 0 && (attributes & FileAttributes.ReparsePoint) == 0);
        }
    }

    private static void CopyIndex(string directory, string view, TimeSpan deadline, Stopwatch watch)
    {
        using var handle = WorkspaceScopeAudit.OpenRegular(Path.Combine(directory, "index"));
        using var source = new FileStream(handle, FileAccess.Read);
        if (source.Length > Limit) throw new IOException("The scope index exceeded its limit.");
        using var target = File.Create(Path.Combine(view, "index"));
        byte[] buffer = new byte[64 * 1024];
        int read;
        while ((read = source.Read(buffer)) != 0)
        {
            if (target.Length + read > Limit || watch.Elapsed >= deadline)
                throw new IOException("The scope index exceeded its work or time limit.");
            target.Write(buffer, 0, read);
        }
    }

    private static string? ReadExclude(string directory)
    {
        string exclude = Path.Combine(directory, "info", "exclude");
        return File.Exists(exclude) ? WorkspaceGitScope.Read(exclude, 1024 * 1024) : null;
    }

    private static string? Hash(string? text) => text is null ? null : "sha256:" + Convert.ToHexStringLower(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(text)));

    // Line endings that a checkout may convert are not a change of rules.
    private static bool Same(string current, string starting)
    {
        try
        {
            using var handle = WorkspaceScopeAudit.OpenRegular(current);
            using var stream = new FileStream(handle, FileAccess.Read);
            if (stream.Length > 1024 * 1024) return false;
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd().Replace("\r\n", "\n", StringComparison.Ordinal) == File.ReadAllText(starting).Replace("\r\n", "\n", StringComparison.Ordinal);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // A link or an unreadable file in an ignore file's place is a change.
            return false;
        }
    }

    private static HashSet<string> Names(string view, string workspace, TimeSpan deadline, Stopwatch watch, params string[] arguments)
    {
        if (WorkspaceGitCommand.Run(view, out string output, Remaining(deadline, watch),
            ["--work-tree=" + workspace, "-c", "core.bare=false", "ls-files", "-z", .. arguments]) != 0)
            throw new IOException("The scope ignore rules or index could not be read.");
        string[] names = output.Split('\0', StringSplitOptions.RemoveEmptyEntries);
        if (names.Length > 100_000 || names.Any(name => !WorkspaceScopeAudit.Relative(name.TrimEnd('/')) || name.Contains('�', StringComparison.Ordinal)))
            throw new IOException("The scope file names could not be read losslessly within the limit.");
        return new HashSet<string>(names, StringComparer.Ordinal);
    }
}
