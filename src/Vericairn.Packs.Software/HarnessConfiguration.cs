// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Diagnostics;
using Vericairn.Records.Hashing;
using Vericairn.Runtime.Providers;
using Vericairn.Runtime.Workers;

namespace Vericairn.Packs.Software;

/// <summary>Inspects configuration discovery roots without executing anything found in them.</summary>
internal static class HarnessConfiguration
{
    private static readonly string[] Instructions = ["AGENTS.md", "AGENTS.override.md", "CLAUDE.md", "CLAUDE.local.md"];
    private static readonly string[] CodexRoots = [".codex", ".agents"];
    private static readonly string[] ClaudeRoots = [".claude", ".mcp.json"];

    public static IReadOnlyList<RunComponent> Inspect(LaunchContext context, bool codex)
    {
        var instructions = new List<RunComponent>();
        var found = new SortedSet<string>(StringComparer.Ordinal);
        var clock = Stopwatch.StartNew();
        int entries = 0;
        try
        {
            string workspace = HarnessWorkspace.Canonical(context.Workspace);
            Scan(workspace, "workspace", recursive: true);
            int ancestor = 0;
            for (DirectoryInfo? parent = Directory.GetParent(workspace); parent is not null; parent = parent.Parent)
            {
                Scan(parent.FullName, "ancestor-" + (++ancestor).ToString(System.Globalization.CultureInfo.InvariantCulture), recursive: false);
            }
            if (CommonWorktree(workspace) is string common && common != workspace) Scan(common, "common-worktree", recursive: false);
            if (codex)
            {
                // CODEX_HOME does not relocate the separate user skills discovery root.
                foreach (string user in new[] { context.Environment.GetValueOrDefault("HOME"), context.Environment.GetValueOrDefault("USERPROFILE") }.OfType<string>().Distinct(StringComparer.Ordinal))
                {
                    Customization(Path.Combine(user, ".agents"), "user/.agents");
                }
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            throw new ProviderRefusedException("The harness configuration discovery roots could not be completely inspected; launch was refused.", exception);
        }
        if (found.Count > 0)
        {
            string fix = found.Any(f => f.StartsWith("ancestor-", StringComparison.Ordinal))
                ? " Move workspaces outside those ancestors with vericairn worker configure --workspaces <dir>." : string.Empty;
            throw new ProviderRefusedException("Unqualified harness configuration was found: " + string.Join(", ", found.Take(20)) + (found.Count > 20 ? ", and further entries" : "") + "." + fix + " No run was dispatched.");
        }
        return instructions.OrderBy(c => c.Id, StringComparer.Ordinal).ToArray();

        void Bound()
        {
            if (++entries > 100000 || clock.Elapsed > TimeSpan.FromSeconds(30) || instructions.Count > 128)
            {
                throw new ProviderRefusedException("The harness configuration inspection exceeded its file, instruction or time bound.");
            }
        }

        void Scan(string root, string label, bool recursive)
        {
            var pending = new Stack<(string Path, string Label)>();
            pending.Push((root, label));
            while (pending.TryPop(out var next))
            {
                IEnumerable<string> paths = recursive ? Directory.EnumerateFileSystemEntries(next.Path)
                    : (codex ? CodexRoots : ClaudeRoots).Concat(Instructions).Select(n => Path.Combine(next.Path, n)).Where(HarnessInventory.Exists);
                foreach (string path in paths)
                {
                    Bound();
                    string name = Path.GetFileName(path);
                    string logical = next.Label + "/" + name;
                    FileAttributes attributes = File.GetAttributes(path);
                    if (IsCustomization(name)) Customization(path, logical);
                    else if (!codex && name.Equals(".mcp.json", StringComparison.OrdinalIgnoreCase)) found.Add(logical);
                    else if (Instructions.Contains(name, StringComparer.OrdinalIgnoreCase)) Instruction(path, logical, name);
                    else if (recursive && attributes.HasFlag(FileAttributes.ReparsePoint)) found.Add(logical + " (uninspected link)");
                    else if (recursive && attributes.HasFlag(FileAttributes.Directory) && !attributes.HasFlag(FileAttributes.ReparsePoint)
                        && !name.Equals(".git", StringComparison.OrdinalIgnoreCase)) pending.Push((path, logical));
                }
            }
        }

        bool IsCustomization(string name) => codex ? name.Equals(".codex", StringComparison.OrdinalIgnoreCase) || name.Equals(".agents", StringComparison.OrdinalIgnoreCase)
            : name.Equals(".claude", StringComparison.OrdinalIgnoreCase);

        void Customization(string path, string label)
        {
            if (!HarnessInventory.Exists(path)) return;
            FileAttributes attributes = File.GetAttributes(path);
            if (!attributes.HasFlag(FileAttributes.Directory) || attributes.HasFlag(FileAttributes.ReparsePoint))
            {
                found.Add(label);
                return;
            }
            foreach (string child in Directory.EnumerateFileSystemEntries(path))
            {
                Bound();
                string name = Path.GetFileName(child);
                string logical = label + "/" + name;
                if (Instructions.Contains(name, StringComparer.OrdinalIgnoreCase)) Instruction(child, logical, name);
                else if (File.GetAttributes(child).HasFlag(FileAttributes.Directory) && !File.GetAttributes(child).HasFlag(FileAttributes.ReparsePoint))
                {
                    // Name the actual file, when available, as well as empty customization directories.
                    int before = found.Count;
                    Customization(child, logical);
                    if (before == found.Count) found.Add(logical);
                }
                else found.Add(logical);
            }
        }

        void Instruction(string path, string logical, string name)
        {
            if (!File.Exists(path) || Directory.Exists(path)) throw new ProviderRefusedException("An instruction file could not be read as content: " + logical + ".");
            string identity = HarnessInventory.Digest(logical);
            instructions.Add(new("instruction-" + name + "-" + identity[7..23], "configuration", "content", ContentHash.Sha256File(path),
                HarnessInventory.Digest(new { source = logical, role = "instruction-content-not-tools" }), [], [], []));
        }
    }

    private static string? CommonWorktree(string workspace)
    {
        string git = Path.Combine(workspace, ".git");
        if (!File.Exists(git)) return null;
        string reference = File.ReadAllText(git).Trim();
        if (!reference.StartsWith("gitdir: ", StringComparison.Ordinal)) throw new ProviderRefusedException("The workspace's Git discovery root could not be established.");
        string metadata = Path.GetFullPath(reference[8..], workspace);
        string commonFile = Path.Combine(metadata, "commondir");
        if (!File.Exists(commonFile)) return null;
        string common = Path.GetFullPath(File.ReadAllText(commonFile).Trim(), metadata);
        return Path.GetFileName(common).Equals(".git", StringComparison.OrdinalIgnoreCase) ? Directory.GetParent(common)?.FullName : null;
    }
}
