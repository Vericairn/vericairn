// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;

namespace Vericairn.Runtime.Guides;

/// <summary>Explicit local file boundaries for catalog preparation. Never consults an estate or performs a provider operation.</summary>
public static class CatalogWorkspace
{
    /// <summary>Loads only the reviewed allowlist after checking it against the source index. The optional map is a JSON array.</summary>
    public static CatalogBundle Load(string indexPath, string planPath, string catalogRoot, string contractsRoot, string? issueMapPath = null)
    {
        try
        {
            GuideSourceIndex index = Read<GuideSourceIndex>(indexPath);
            CatalogExportPlan plan = Read<CatalogExportPlan>(planPath);
            if (!CatalogExporter.ValidPlan(index, plan) || !CatalogExporter.Allowlisted(index, index.Sources.Select(s => s.Id), plan) ||
                plan.IndexIdentity != GuideGenerator.Hash(GuideGenerator.Json(index)))
                return CatalogExporter.Failed("export-allowlist", "The exact reviewed index and allowlist are required before source files can be read.");
            var sources = new Dictionary<string, byte[]>(StringComparer.Ordinal);
            long total = 0;
            foreach (GuideSourcePin source in index.Sources)
            {
                if (!GuideGenerator.SafeKey(source.Id) || !GuideGenerator.SafePath(source.Path) || source.Area is not ("catalog" or "contracts"))
                    return CatalogExporter.Failed("export-allowlist", "Only portable paths in the explicit catalog and contracts areas are admitted.");
                string path = Below(source.Area == "catalog" ? catalogRoot : contractsRoot, source.Path);
                byte[] bytes = ReadBytes(path, GuideGenerator.MaximumSourceBytes);
                total += bytes.Length;
                if (total > GuideGenerator.MaximumInputBytes) return CatalogExporter.Failed("export-limit", "The source set exceeds its byte bound.");
                if (!sources.TryAdd(source.Id, bytes)) return CatalogExporter.Failed("export-allowlist", "A source identity is duplicated.");
            }
            return CatalogExporter.Generate(index, sources, plan, issueMapPath is null ? [] : Read<CatalogTrackingIssue[]>(issueMapPath));
        }
        catch (Exception error) when (FileFailure(error))
        { return CatalogExporter.Failed("export-input", "A local input is missing, malformed, oversized, linked or inaccessible; no other source was read."); }
    }

    /// <summary>Writes only successful bundles into a new or empty directory. Never overwrites or initializes Git.</summary>
    public static IReadOnlyList<GuideFinding> Write(CatalogBundle bundle, string output)
    {
        ArgumentNullException.ThrowIfNull(bundle);
        if (!bundle.Success) return bundle.Findings;
        IReadOnlyList<GuideFinding> scan = CatalogDisclosure.Check(bundle.Files);
        if (scan.Count > 0) return scan;
        try
        {
            Local(output);
            if (Directory.Exists(output) && Directory.EnumerateFileSystemEntries(output).Any())
                return [Finding("export-not-empty", "Choose a new or empty directory; existing output is never changed.")];
            if (bundle.Files.Count > 100000 || bundle.Files.Sum(f => (long)f.Value.Length) > 256L * 1024 * 1024 ||
                bundle.Files.Keys.Any(p => !GuideGenerator.SafePath(p) || p.Split('/').Any(c => c.Equals(".git", StringComparison.OrdinalIgnoreCase))) ||
                bundle.Files.Keys.Distinct(StringComparer.OrdinalIgnoreCase).Count() != bundle.Files.Count)
                return [Finding("export-output", "The complete output membership or byte bound is invalid.")];
            CreateDirectory(output);
            foreach ((string relative, byte[] bytes) in bundle.Files.OrderBy(f => f.Key, StringComparer.Ordinal))
            {
                string path = Below(output, relative);
                CreateDirectory(Path.GetDirectoryName(path)!);
                Local(path);
                var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write, Share = FileShare.None };
                if (!OperatingSystem.IsWindows()) options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
                using var file = new FileStream(path, options);
                file.Write(bytes);
            }
            return [];
        }
        catch (Exception error) when (FileFailure(error))
        { return [Finding("export-output", "The empty destination could not be written completely. Inspect it and retry in a fresh directory; existing files were not replaced.")]; }
    }

    /// <summary>Reports additions, changes and removals in a local checkout. Does not read Git metadata or unrelated file contents.</summary>
    public static CatalogComparison Compare(CatalogBundle bundle, string destination)
    {
        ArgumentNullException.ThrowIfNull(bundle);
        if (!bundle.Success) return new([], bundle.Findings);
        var changes = new List<CatalogChange>();
        var findings = new List<GuideFinding>(CatalogDisclosure.Check(bundle.Files));
        if (findings.Count > 0) return new([], findings);
        try
        {
            Local(destination);
            if (!Directory.Exists(destination)) return new([], [Finding("destination-missing", "Select an existing local destination checkout or empty comparison directory.")]);
            var seen = new HashSet<string>(StringComparer.Ordinal);
            int items = 0;
            Read(destination, "");
            changes.AddRange(bundle.Files.Keys.Where(k => !seen.Contains(k)).Select(k => new CatalogChange(k, "add")));
            return new(changes.OrderBy(c => c.Path, StringComparer.Ordinal).ThenBy(c => c.Action, StringComparer.Ordinal).ToArray(), findings);

            void Read(string directory, string prefix)
            {
                foreach (string path in Directory.EnumerateFileSystemEntries(directory).Order(StringComparer.Ordinal))
                {
                    if (++items > 120000) throw new IOException("The destination exceeds the membership bound.");
                    string relative = prefix + Path.GetFileName(path);
                    // A checkout may have a .git directory or a worktree .git file. Neither is opened or traversed.
                    if (relative.Equals(".git", StringComparison.OrdinalIgnoreCase)) continue;
                    Local(path);
                    if (!GuideGenerator.SafePath(relative)) throw new IOException("A destination name is not portable.");
                    if (Directory.Exists(path))
                    {
                        if (prefix.Count(c => c == '/') > 16) throw new IOException("The destination exceeds the depth bound.");
                        if (bundle.Files.ContainsKey(relative)) findings.Add(Finding("destination-type", "An output file collides with a destination directory."));
                        Read(path, relative + "/");
                    }
                    else if (!bundle.Files.TryGetValue(relative, out byte[]? expected))
                    {
                        changes.Add(new(relative, "remove"));
                        if (relative.StartsWith("guides/", StringComparison.Ordinal) && relative.EndsWith(".md", StringComparison.Ordinal))
                            findings.Add(Finding("retained-page-required", "A previous guide address would disappear. Supply its retained retirement page before publication."));
                    }
                    else
                    {
                        seen.Add(relative);
                        if (new FileInfo(path).Length != expected.Length || !ReadBytes(path, expected.Length).AsSpan().SequenceEqual(expected))
                            changes.Add(new(relative, "change"));
                    }
                }
            }
        }
        catch (Exception error) when (FileFailure(error))
        { return new([], [Finding("destination-unavailable", "The destination cannot be completely compared: inspect linked, inaccessible, nonportable or oversized membership.")]); }
    }

    /// <summary>Reads one bounded explicit local JSON input with duplicate and unknown properties refused.</summary>
    public static T Read<T>(string path) where T : class =>
        JsonSerializer.Deserialize<T>(ReadBytes(path, GuideGenerator.MaximumSourceBytes), GuideJson.Options) ?? throw new JsonException("A JSON object is required.");

    /// <summary>Prevents output from containing or overlapping source trees and explicit input files.</summary>
    public static bool SeparateOutput(string output, string catalog, string contracts, params string[] files)
    {
        foreach (string path in new[] { output, catalog, contracts }.Concat(files)) Local(path);
        StringComparison comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        string Prefix(string path) => Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        string target = Prefix(output);
        return !new[] { catalog, contracts }.Any(p => Prefix(p).StartsWith(target, comparison) || target.StartsWith(Prefix(p), comparison)) &&
            files.All(p => !Prefix(p).StartsWith(target, comparison));
    }

    private static byte[] ReadBytes(string path, long limit)
    {
        Local(path);
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length > limit) throw new IOException("The local input exceeds its byte bound.");
        byte[] bytes = new byte[checked((int)stream.Length)];
        stream.ReadExactly(bytes);
        if (stream.ReadByte() != -1) throw new IOException("The input changed while being read.");
        return bytes;
    }

    private static string Below(string root, string relative)
    {
        Local(root);
        if (!GuideGenerator.SafePath(relative)) throw new IOException("A portable relative path is required.");
        string current = Path.GetFullPath(root);
        foreach (string part in relative.Split('/')) { current = Path.Combine(current, part); NoLink(current); }
        return current;
    }

    // The chosen path itself and everything below it must be link-free, as GUIDE-01's preview requires. Its ancestors are the
    // person's own environment and may be aliases, such as macOS's /tmp and /var, so they are not refused.
    private static void Local(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || path.StartsWith("//", StringComparison.Ordinal) || path.StartsWith("\\\\", StringComparison.Ordinal) || path.Contains("://", StringComparison.Ordinal))
            throw new IOException("An explicit local path is required.");
        NoLink(Path.GetFullPath(path));
    }

    private static void NoLink(string path)
    {
        if (new FileInfo(path).LinkTarget is not null || new DirectoryInfo(path).LinkTarget is not null ||
            (File.Exists(path) || Directory.Exists(path)) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw new IOException("Linked inputs and destinations are not admitted.");
    }

    private static void CreateDirectory(string path)
    {
        if (OperatingSystem.IsWindows()) Directory.CreateDirectory(path);
        else Directory.CreateDirectory(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
    }

    private static bool FileFailure(Exception error) => error is IOException or UnauthorizedAccessException or JsonException or ArgumentException or NotSupportedException;
    private static GuideFinding Finding(string code, string message) => new("inventory", code, message);
}
