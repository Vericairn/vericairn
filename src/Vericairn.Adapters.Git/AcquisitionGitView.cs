// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Diagnostics;
using System.Text;
using Microsoft.Win32.SafeHandles;
using Vericairn.Runtime.Providers;
using Vericairn.Runtime.Workers;

namespace Vericairn.Adapters.Git;

/// <summary>Copies the repository data Git may open into an isolated administration directory.</summary>
internal sealed class AcquisitionGitView
{
    private const int MetadataLimit = 16 * 1024 * 1024;
    private static readonly UTF8Encoding Utf8 = new(false, true);
    private readonly List<string> redirections = [];
    private readonly HashSet<string> stores = new(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
    private readonly Stopwatch age = Stopwatch.StartNew();
    private readonly string boundary;
    private readonly string administration;
    private readonly string common;

    internal AcquisitionGitView(string source, string? allowedRoot, string directory)
    {
        Directory = directory;
        string full = Path.GetFullPath(source);
        if (TryAttributes(full, out FileAttributes selected) && (selected & FileAttributes.ReparsePoint) != 0)
            throw new ProviderRefusedException("The selected repository path is a link.");
        string? root = FindRoot(full);
        if (root is null) throw new ProviderRefusedException("The selected path is not inside a git working tree or a bare repository.");
        Root = WorkspacePaths.Canonical(root);
        boundary = allowedRoot is null ? Root : WorkspacePaths.Canonical(Path.GetFullPath(allowedRoot));
        if (!Contains(boundary, Root))
            throw new ProviderRefusedException("The repository root is outside the selected operator boundary.");

        string marker = Path.Combine(Root, ".git");
        if (!TryAttributes(marker, out FileAttributes attributes))
        {
            Bare = true;
            administration = Root;
        }
        else if ((attributes & FileAttributes.ReparsePoint) != 0)
        {
            throw new ProviderRefusedException("The repository's .git entry is a link.");
        }
        else if ((attributes & FileAttributes.Directory) != 0)
        {
            administration = marker;
        }
        else
        {
            string pointer = ReadText(marker, 4096)?.TrimEnd('\r', '\n') ?? string.Empty;
            if (!pointer.StartsWith("gitdir: ", StringComparison.Ordinal))
                throw new ProviderRefusedException("The repository's .git file has an invalid gitdir: entry.");
            administration = Redirect(Root, pointer[8..], ".git gitdir:");
        }

        string? commonPointer = ReadText(Path.Combine(administration, "commondir"), 4096)?.TrimEnd('\r', '\n');
        common = commonPointer is null ? administration : Redirect(administration, commonPointer, "commondir");

        System.IO.Directory.CreateDirectory(Directory);
        System.IO.Directory.CreateDirectory(Path.Combine(Directory, "refs"));
        CopyMetadata(administration, "HEAD", Directory, required: true);
        CopyMetadata(common, "packed-refs", Directory);
        CopyMetadata(common, "shallow", Directory);
        string refs = Path.Combine(common, "refs");
        if (TryAttributes(refs, out _))
            AcquisitionWorkingTree.CopyFrom(boundary, CheckedDirectory(refs, "refs"), Path.Combine(Directory, "refs"));

        CopyObjects(Path.Combine(common, "objects"), Path.Combine(Directory, "objects"), 0);
        Config = ReadBytes(Path.Combine(common, "config"), MetadataLimit) ?? [];
        WorktreeConfig = ReadBytes(Path.Combine(administration, "config.worktree"), MetadataLimit);
        File.WriteAllText(Path.Combine(Directory, "config"), "[core]\nrepositoryformatversion = 0\nbare = true\n");
    }

    internal string Directory { get; }

    internal string Root { get; }

    internal string Common => common;

    internal string Boundary => boundary;

    internal bool CheckedOut(string branch, out bool selected)
    {
        string reference = "ref: refs/heads/" + branch;
        selected = File.ReadAllText(Path.Combine(Directory, "HEAD")).TrimEnd('\r', '\n') == reference;
        if (selected) return true;
        string linked = Path.Combine(common, "worktrees");
        if (!TryAttributes(linked, out _)) return false;
        linked = CheckedDirectory(linked, "worktrees");
        int count = 0;
        foreach (string worktree in System.IO.Directory.EnumerateFileSystemEntries(linked))
        {
            if (++count > 2048) throw new ProviderRefusedException("The linked worktree inventory exceeded its limit.");
            if (ReadText(Path.Combine(worktree, "HEAD"), 4096)?.TrimEnd('\r', '\n') == reference) return true;
        }
        return false;
    }

    internal bool Bare { get; }

    internal byte[] Config { get; }

    internal byte[]? WorktreeConfig { get; }

    internal IReadOnlyList<string> Redirections => redirections;

    internal void SetObjectFormat(string format) => File.WriteAllText(Path.Combine(Directory, "config"), format == "sha256"
        ? "[core]\nrepositoryformatversion = 1\nbare = true\n[extensions]\nobjectformat = sha256\n"
        : "[core]\nrepositoryformatversion = 0\nbare = true\n");

    internal void CopyIndex()
    {
        if (Bare) throw new ProviderRefusedException("The selected repository has no working tree.");
        CopyMetadata(administration, "index", Directory);
        CopyMetadata(common, "info/exclude", Directory);
        CopyMetadata(common, "info/attributes", Directory);
        CopyMetadata(administration, "info/sparse-checkout", Directory);
        foreach (string file in System.IO.Directory.EnumerateFileSystemEntries(administration, "sharedindex.*").Take(129))
        {
            string name = Path.GetFileName(file);
            if (!WorkspaceGitScope.ObjectId(name["sharedindex.".Length..]))
                throw new ProviderRefusedException("The repository contains an invalid shared index name.");
            CopyMetadata(administration, name, Directory);
        }

        if (System.IO.Directory.EnumerateFileSystemEntries(Directory, "sharedindex.*").Skip(128).Any())
            throw new ProviderRefusedException("The repository has too many shared index files.");
    }

    internal void CopyWorkingTree(string destination, IReadOnlySet<string> submodules) =>
        AcquisitionWorkingTree.CopyFrom(boundary, Root, destination, path => !submodules.Contains(path), workingTree: true);

    internal string? LocalOrigin(string value)
    {
        if (!RepositoryAddress.IsLocal(value)) return null;
        string target = Path.GetFullPath(value, Root);
        if (!Contains(boundary, target)) return null;
        // A local origin is only an observed name. Do not open its repository.
        try { return Contains(boundary, WorkspacePaths.Canonical(target)) ? value : null; }
        catch (ProviderRefusedException) { return null; }
    }

    private void CopyObjects(string source, string target, int depth)
    {
        if (depth > 16 || stores.Count >= 64 || age.Elapsed > TimeSpan.FromSeconds(30))
            throw new ProviderRefusedException("The repository's object redirections exceed the acquisition limit.");
        string store = CheckedDirectory(source, "objects");
        if (!stores.Add(store)) return;
        AcquisitionWorkingTree.CopyFrom(boundary, store, target, ObjectMember);
        string? alternates = ReadText(Path.Combine(store, "info", "alternates"), 32768);
        if (alternates is null) return;
        var copies = new List<string>();
        foreach (string path in alternates.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            string name = path.TrimEnd('\r');
            string resolved = Redirect(store, name, "objects/info/alternates");
            if (stores.Contains(resolved)) continue;
            string copy = Path.Combine(Directory, "object-store-" + stores.Count.ToString(System.Globalization.CultureInfo.InvariantCulture));
            CopyObjects(resolved, copy, depth + 1);
            copies.Add(copy.Replace('\\', '/'));
        }

        if (copies.Count == 0) return;
        string info = Path.Combine(target, "info");
        System.IO.Directory.CreateDirectory(info);
        File.WriteAllLines(Path.Combine(info, "alternates"), copies);
    }

    private static bool ObjectMember(string relative)
    {
        if (relative == "pack") return true;
        string[] parts = relative.Split('/');
        if (parts.Length == 1) return Hex(parts[0], 2);
        if (parts.Length != 2) return false;
        if (parts[0] != "pack") return Hex(parts[0], 2) && (Hex(parts[1], 38) || Hex(parts[1], 62));
        string name = parts[1];
        string extension = Path.GetExtension(name);
        return name.StartsWith("pack-", StringComparison.Ordinal) && extension is ".pack" or ".idx"
            && WorkspaceGitScope.ObjectId(Path.GetFileNameWithoutExtension(name)[5..]);
    }

    private static bool Hex(string name, int length) => name.Length == length && name.All(char.IsAsciiHexDigit);

    private string Redirect(string from, string pointer, string member)
    {
        if (string.IsNullOrEmpty(pointer) || pointer.Any(char.IsControl) || pointer[0] == '"')
            throw new ProviderRefusedException($"The repository's {member} redirection has an unsupported path.");
        string path;
        try { path = Path.GetFullPath(pointer, from); }
        catch (ArgumentException error) { throw new ProviderRefusedException($"The repository's {member} redirection has an invalid path.", error); }
        string physical = CheckedDirectory(path, member);
        redirections.Add(member);
        return physical;
    }

    private string CheckedDirectory(string path, string member)
    {
        if (!Contains(boundary, path))
            throw new ProviderRefusedException($"The repository's {member} points outside the selected operator boundary.");
        string physical = WorkspacePaths.Canonical(path);
        if (!Contains(boundary, physical))
            throw new ProviderRefusedException($"The repository's {member} resolves outside the selected operator boundary.");
        return physical;
    }

    private void CopyMetadata(string source, string name, string target, bool required = false)
    {
        byte[]? bytes = ReadBytes(Path.Combine(source, name), MetadataLimit);
        if (bytes is null)
        {
            if (required) throw new ProviderRefusedException($"The repository's {name} is missing.");
            return;
        }
        string output = Path.Combine(target, name);
        System.IO.Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        File.WriteAllBytes(output, bytes);
    }

    private string? ReadText(string path, int limit)
        => ReadBytes(path, limit) is { } bytes ? Utf8.GetString(bytes) : null;

    private byte[]? ReadBytes(string path, int limit)
    {
        using SafeFileHandle? handle = AcquisitionWorkingTree.TryOpenRegular(boundary, path);
        if (handle is null) return null;
        using var stream = new FileStream(handle, FileAccess.Read);
        if (stream.Length > limit)
            throw new ProviderRefusedException($"The repository's {Path.GetFileName(path)} exceeds the acquisition byte limit.");
        byte[] bytes = new byte[stream.Length];
        stream.ReadExactly(bytes);
        if (stream.ReadByte() != -1 || age.Elapsed > TimeSpan.FromSeconds(30))
            throw new ProviderRefusedException($"The repository's {Path.GetFileName(path)} changed or exceeded the acquisition limit.");
        return bytes;
    }

    private static string? FindRoot(string path)
    {
        for (var current = new DirectoryInfo(path); current.Exists; current = current.Parent!)
        {
            if (TryAttributes(Path.Combine(current.FullName, ".git"), out _)
                || TryAttributes(Path.Combine(current.FullName, "HEAD"), out _) && System.IO.Directory.Exists(Path.Combine(current.FullName, "objects")))
                return current.FullName;
            if (current.Parent is null) break;
        }
        return null;
    }

    private static bool TryAttributes(string path, out FileAttributes attributes)
    {
        try { attributes = File.GetAttributes(path); return true; }
        catch (FileNotFoundException) { attributes = default; return false; }
        catch (DirectoryNotFoundException) { attributes = default; return false; }
    }

    private static bool Contains(string root, string path)
    {
        string directory = Path.TrimEndingDirectorySeparator(root);
        StringComparison comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        return path.Equals(directory, comparison) || path.StartsWith(Path.EndsInDirectorySeparator(directory)
            ? directory : directory + Path.DirectorySeparatorChar, comparison);
    }
}
