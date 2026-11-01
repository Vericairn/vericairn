// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

namespace Vericairn.Cli;

internal sealed record BootstrapReceipt(string Path, string Status);

/// <summary>The first-party scripts carried by this command line, copied without text conversion.</summary>
internal sealed class EstateBootstrap
{
    private sealed record Copy(string Name, string Destination, byte[] Bytes, bool Exists, bool Identical);

    private readonly Copy[] copies;
    private readonly Copy[] gitFiles;

    private EstateBootstrap(Copy[] copies, Copy[] gitFiles) => (this.copies, this.gitFiles) = (copies, gitFiles);

    // Git for Windows converts text to CRLF on checkout by default, which breaks the POSIX script and changes the canonical
    // bytes of records, objects and documents, so their hashes no longer match. The estate's .gitattributes keeps every text
    // file LF on every checkout, and its .gitignore keeps the per-clone working directory out of commits; only missing lines
    // are appended to either.
    private static readonly string[] AttributeLines = ["* text=auto eol=lf", "*.json text eol=lf", "*.md text eol=lf", "bootstrap text eol=lf", "bootstrap.ps1 text eol=lf"];
    private static readonly string[] IgnoreLines = [".vericairn/"];

    // Check every destination before initialization or refresh writes anything.
    internal static EstateBootstrap Prepare(string root, bool force = false)
    {
        Copy[] copies = [Read(root, "bootstrap", force), Read(root, "bootstrap.ps1", force)];
        return new(copies, [Lines(root, ".gitattributes", AttributeLines), Lines(root, ".gitignore", IgnoreLines)]);
    }

    internal BootstrapReceipt[] Write()
    {
        foreach (Copy file in gitFiles)
            if (!file.Identical) Replace(file);
        foreach (Copy copy in copies)
        {
            if (copy.Identical)
            {
                MakeExecutable(copy.Destination, copy.Name);
                continue;
            }

            string temporary = copy.Destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                    file.Write(copy.Bytes);
                MakeExecutable(temporary, copy.Name);
                File.Move(temporary, copy.Destination, overwrite: copy.Exists);
            }
            finally { File.Delete(temporary); }
        }

        return [.. copies.Concat(gitFiles).Select(copy => new BootstrapReceipt(copy.Name, copy.Identical ? "unchanged" : "written"))];
    }

    private static Copy Lines(string root, string name, string[] lines)
    {
        string destination = Path.Combine(root, name);
        if (new FileInfo(destination).LinkTarget is not null || Directory.Exists(destination))
            throw new InvalidOperationException($"{name} is a link or directory. Move it aside before writing the first-party bootstrap. Nothing changed.");
        bool exists = File.Exists(destination);
        string text = exists ? File.ReadAllText(destination) : string.Empty;
        HashSet<string> present = text.Split('\n').Select(line => line.Trim()).ToHashSet(StringComparer.Ordinal);
        string[] missing = lines.Where(line => !present.Contains(line)).ToArray();
        if (missing.Length == 0) return new(name, destination, [], exists, true);
        string separator = text.Length == 0 || text.EndsWith('\n') ? string.Empty : "\n";
        byte[] bytes = System.Text.Encoding.UTF8.GetBytes(text + separator + string.Concat(missing.Select(line => line + "\n")));
        return new(name, destination, bytes, exists, false);
    }

    private static void Replace(Copy copy)
    {
        string temporary = copy.Destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                file.Write(copy.Bytes);
            File.Move(temporary, copy.Destination, overwrite: copy.Exists);
        }
        finally { File.Delete(temporary); }
    }

    private static Copy Read(string root, string name, bool force)
    {
        using Stream resource = typeof(EstateBootstrap).Assembly.GetManifestResourceStream("Vericairn.Cli.Bootstrap." + name)
            ?? throw new InvalidOperationException($"The command line is missing its first-party {name} resource.");
        using var buffer = new MemoryStream();
        resource.CopyTo(buffer);
        byte[] bytes = buffer.ToArray();
        string destination = Path.Combine(root, name);
        if (new FileInfo(destination).LinkTarget is not null || Directory.Exists(destination))
            throw new InvalidOperationException($"{name} is a link or directory. Move it aside before writing the first-party bootstrap. Nothing changed.");
        bool exists = File.Exists(destination);
        bool identical = exists && File.ReadAllBytes(destination).AsSpan().SequenceEqual(bytes);
        if (exists && !identical && !force)
            throw new InvalidOperationException($"{name} differs from the first-party copy. Keep a copy of any changes; move it aside before initialization or use estate bootstrap --force in an existing estate. Nothing changed.");
        return new(name, destination, bytes, exists, identical);
    }

    private static void MakeExecutable(string path, string name)
    {
        if (name == "bootstrap" && !OperatingSystem.IsWindows())
        {
            UnixFileMode mode = File.GetUnixFileMode(path);
            File.SetUnixFileMode(path, mode | UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute);
        }
    }
}
