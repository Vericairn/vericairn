// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Diagnostics;

namespace Vericairn.Adapters.Git;

/// <summary>Repository ignore rules and tracked names, without loading agent-controlled Git configuration.</summary>
internal sealed class WorkspaceScopeFiles
{
    private readonly HashSet<string> ignored;
    private readonly HashSet<string> tracked;

    internal WorkspaceScopeFiles(string workspace, string directory, TimeSpan deadline)
    {
        var watch = Stopwatch.StartNew();
        string head = WorkspaceGitScope.Read(Path.Combine(directory, "HEAD"), 4096).Trim();
        if (head.StartsWith("ref: ", StringComparison.Ordinal)) head = WorkspaceGitScope.Head(directory, head[5..]);
        using var view = new WorkspaceGitView(Path.Combine(directory, "objects"), head);
        // Use a copy: even optional index refreshes must not write the workspace's administration.
        string index = Path.Combine(directory, "index");
        {
            using var handle = WorkspaceScopeAudit.OpenRegular(index);
            using var source = new FileStream(handle, FileAccess.Read);
            if (source.Length > 64 * 1024 * 1024) throw new IOException("The scope index exceeded its limit.");
            using var target = File.Create(Path.Combine(view.Path, "index"));
            byte[] buffer = new byte[64 * 1024];
            int read;
            while ((read = source.Read(buffer)) != 0)
            {
                if (target.Length + read > 64 * 1024 * 1024 || watch.Elapsed >= deadline)
                    throw new IOException("The scope index exceeded its work or time limit.");
                target.Write(buffer, 0, read);
            }
        }
        string exclude = Path.Combine(directory, "info", "exclude");
        if (File.Exists(exclude))
        {
            Directory.CreateDirectory(Path.Combine(view.Path, "info"));
            File.WriteAllText(Path.Combine(view.Path, "info", "exclude"), WorkspaceGitScope.Read(exclude, 1024 * 1024));
        }
        tracked = Names("--cached");
        ignored = Names("--others", "--ignored", "--exclude-standard", "--directory");

        HashSet<string> Names(params string[] arguments)
        {
            if (WorkspaceGitCommand.Run(view.Path, out string output, deadline - watch.Elapsed,
                ["--work-tree=" + workspace, "-c", "core.bare=false", "ls-files", "-z", .. arguments]) != 0)
                throw new IOException("The scope ignore rules or index could not be read.");
            string[] names = output.Split('\0', StringSplitOptions.RemoveEmptyEntries);
            if (names.Length > 100_000 || names.Any(name => !WorkspaceScopeAudit.Relative(name.TrimEnd('/')) || name.Contains('\ufffd', StringComparison.Ordinal)))
                throw new IOException("The scope file names could not be read losslessly within the limit.");
            return new HashSet<string>(names, StringComparer.Ordinal);
        }
    }

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
}
