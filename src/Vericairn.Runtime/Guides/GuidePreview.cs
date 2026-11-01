// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;

namespace Vericairn.Runtime.Guides;

/// <summary>Local file access for private previews. Only an explicit index and its allowlisted files are read.</summary>
public static class GuidePreview
{
    /// <summary>Loads exact sources from two caller-selected roots. Missing sources become coverage findings.</summary>
    public static GuideBundle Load(string indexPath, string catalogRoot, string contractsRoot, IReadOnlyCollection<string>? selection = null)
    {
        try
        {
            EnsureLocal(indexPath);
            EnsureLocal(catalogRoot);
            EnsureLocal(contractsRoot);
            NoLink(indexPath);
            if (new FileInfo(indexPath).Length > GuideGenerator.MaximumSourceBytes) return Failed("input-limit", "The source index exceeds the byte bound.");
            GuideSourceIndex? index = JsonSerializer.Deserialize<GuideSourceIndex>(File.ReadAllBytes(indexPath), GuideJson.Options);
            if (index is null || !GuideGenerator.ValidShape(index)) return Failed("invalid-index", "A complete, bounded source index is required.");
            var sources = new Dictionary<string, byte[]>(StringComparer.Ordinal);
            long total = 0;
            foreach (GuideSourcePin source in index.Sources)
            {
                if (source is null || source.Id is null || source.Path is null || !GuideGenerator.SafePath(source.Path) || source.Area is not ("catalog" or "contracts"))
                    return Failed("invalid-source", "Source paths and areas must be explicit and portable.");
                string root = source.Area == "catalog" ? catalogRoot : contractsRoot;
                string path = Below(root, source.Path);
                if (!File.Exists(path)) continue;
                long length = new FileInfo(path).Length;
                total += length;
                if (length > GuideGenerator.MaximumSourceBytes || total > GuideGenerator.MaximumInputBytes)
                    return Failed("input-limit", "The inputs exceed the byte bound.");
                sources.TryAdd(source.Id, File.ReadAllBytes(path));
            }
            return GuideGenerator.Generate(index, sources, selection);
        }
        catch (Exception exception) when (FileFailure(exception))
        { return Failed("input-unavailable", "An input is malformed, inaccessible or uses a symbolic link. No ambient source was substituted."); }
    }

    /// <summary>Compares two explicit local manifests without reading an estate or writing files.</summary>
    public static GuideInputComparison Compare(string before, string after)
    {
        try
        {
            return GuideGenerator.CompareInputs(ReadManifest(before), ReadManifest(after));
        }
        catch (Exception exception) when (FileFailure(exception))
        { return new([], [new("inventory", "input-unavailable", "A source manifest is inaccessible, oversized or uses a symbolic link.")]); }

        static byte[] ReadManifest(string path)
        {
            EnsureLocal(path);
            NoLink(path);
            if (new FileInfo(path).Length > GuideGenerator.MaximumInputBytes) throw new IOException("The manifest exceeds the byte bound.");
            return File.ReadAllBytes(path);
        }
    }

    /// <summary>Writes a private preview into a new/empty directory. Existing exact output is checked, never overwritten.</summary>
    public static IReadOnlyList<GuideFinding> Write(GuideBundle bundle, string output)
    {
        ArgumentNullException.ThrowIfNull(bundle);
        try
        {
            EnsureLocal(output);
            NoLink(output);
            if (Directory.Exists(output) && Directory.EnumerateFileSystemEntries(output).Any()) return Check(bundle, output);
            CreateDirectory(output);
            foreach ((string relative, byte[] bytes) in bundle.Files.OrderBy(p => p.Key, StringComparer.Ordinal))
            {
                if (!GuideGenerator.SafePath(relative)) return [new("inventory", "output-path", "A generated path is invalid.")];
                string path = Below(output, relative);
                CreateDirectory(Path.GetDirectoryName(path)!);
                NoLink(path);
                var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write, Share = FileShare.None };
                if (!OperatingSystem.IsWindows()) options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
                using var file = new FileStream(path, options);
                file.Write(bytes);
            }
            return bundle.Findings;
        }
        catch (Exception exception) when (FileFailure(exception))
        { return [.. bundle.Findings, new("inventory", "output-unavailable", "The preview directory could not be written completely. Inspect it and use a fresh directory; existing files were not replaced.")]; }
    }

    /// <summary>Checks membership and fresh bytes without editing anything; links and unexpected directories fail.</summary>
    public static IReadOnlyList<GuideFinding> Check(GuideBundle expected, string output)
    {
        ArgumentNullException.ThrowIfNull(expected);
        try
        {
            EnsureLocal(output);
            NoLink(output);
            if (!Directory.Exists(output)) return [.. expected.Findings, new("inventory", "missing-output", "The preview directory does not exist.")];
            var actual = new Dictionary<string, byte[]>(StringComparer.Ordinal);
            var findings = new List<GuideFinding>();
            long total = 0;
            Read(output, "");
            return [.. findings, .. GuideGenerator.Check(expected, actual)];

            void Read(string directory, string prefix)
            {
                foreach (string path in Directory.EnumerateFileSystemEntries(directory).Order(StringComparer.Ordinal))
                {
                    NoLink(path);
                    string name = prefix + Path.GetFileName(path);
                    if (Directory.Exists(path))
                    {
                        if (name != "guides") findings.Add(new("inventory", "unexpected-output", "An undeclared output directory is present."));
                        else Read(path, "guides/");
                    }
                    else
                    {
                        // Do not read unrelated files, even in a caller-selected preview directory.
                        if (!expected.Files.TryGetValue(name, out byte[]? wanted))
                        { findings.Add(new("inventory", "unexpected-output", "An undeclared output file is present.")); continue; }
                        long length = new FileInfo(path).Length;
                        total += length;
                        // Length alone proves a mismatch. A distinct sentinel gives the same per-file
                        // diagnostics without reading arbitrary oversized content into memory.
                        if (length != wanted.Length) actual.Add(name, wanted.Length == 0 ? [0] : []);
                        else if (total > 512L * 1024 * 1024)
                            findings.Add(new("inventory", "output-limit", "The preview exceeds the complete bundle byte bound."));
                        else actual.Add(name, File.ReadAllBytes(path));
                    }
                }
            }
        }
        catch (Exception exception) when (FileFailure(exception))
        { return [.. expected.Findings, new("inventory", "output-unavailable", "The preview is inaccessible or contains a symbolic link.")]; }
    }

    /// <summary>Refuses placing generated output inside an input tree or over the index.</summary>
    public static bool SeparateOutput(string output, string index, string catalog, string contracts)
    {
        foreach (string path in new[] { output, index, catalog, contracts }) EnsureLocal(path);
        string target = Path.GetFullPath(output).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        StringComparison comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        return !new[] { catalog, contracts }.Any(root =>
        {
            string input = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            return target.StartsWith(input, comparison) || input.StartsWith(target, comparison);
        }) &&
            !Path.GetFullPath(index).StartsWith(target, comparison);
    }

    private static void EnsureLocal(string path)
    {
        if (path.StartsWith("//", StringComparison.Ordinal) || path.StartsWith("\\\\", StringComparison.Ordinal) || path.Contains("://", StringComparison.Ordinal))
            throw new IOException("A local path is required.");
    }

    private static string Below(string root, string relative)
    {
        string current = Path.GetFullPath(root);
        NoLink(current);
        foreach (string part in relative.Split('/')) { current = Path.Combine(current, part); NoLink(current); }
        return current;
    }

    private static void NoLink(string path)
    {
        var file = new FileInfo(path);
        var directory = new DirectoryInfo(path);
        if (file.LinkTarget is not null || directory.LinkTarget is not null ||
            (File.Exists(path) || Directory.Exists(path)) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw new IOException("Symbolic links are not source or output files.");
    }

    private static void CreateDirectory(string path)
    {
        if (OperatingSystem.IsWindows()) Directory.CreateDirectory(path);
        else Directory.CreateDirectory(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
    }

    private static bool FileFailure(Exception exception) => exception is IOException or UnauthorizedAccessException or ArgumentException or JsonException or NotSupportedException;

    private static GuideBundle Failed(string code, string message) => new(new Dictionary<string, byte[]>(), [], [new("inventory", code, message)]);
}
