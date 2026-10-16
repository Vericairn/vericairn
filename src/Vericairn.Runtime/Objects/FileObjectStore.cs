// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using Vericairn.Records.Hashing;

namespace Vericairn.Runtime.Objects;

/// <summary>Objects as files named by their hash in one directory, written atomically; the repository's objects/ directory uses this layout.</summary>
public sealed class FileObjectStore : IObjectStore
{
    private readonly string directory;

    /// <summary>Creates a store over a directory (created on first write).</summary>
    public FileObjectStore(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        this.directory = Path.GetFullPath(directory);
    }

    /// <inheritdoc />
    public string Kind => "file";

    /// <inheritdoc />
    public string Put(ReadOnlySpan<byte> bytes)
    {
        string hash = ContentHash.Sha256(bytes);
        string path = PathOf(hash);
        if (File.Exists(path))
        {
            return hash;
        }

        Directory.CreateDirectory(directory);
        string temporary = Path.Combine(directory, "." + Hex(hash) + ".tmp-" + Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture));
        using (FileStream stream = new(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            stream.Write(bytes);
            stream.Flush(flushToDisk: true);
        }

        try
        {
            File.Move(temporary, path, overwrite: false);
        }
        catch (IOException) when (File.Exists(path))
        {
            File.Delete(temporary);
        }

        return hash;
    }

    /// <inheritdoc />
    public bool Has(string hash) => File.Exists(PathOf(hash));

    /// <inheritdoc />
    public byte[]? Read(string hash)
    {
        string path = PathOf(hash);
        byte[] bytes;
        try
        {
            bytes = File.ReadAllBytes(path);
        }
        catch (Exception exception) when (exception is FileNotFoundException or DirectoryNotFoundException)
        {
            return null;
        }

        // Access denial must not masquerade as absence (File.Exists suppresses that distinction).
        string actual = ContentHash.Sha256(bytes);
        return actual == hash ? bytes : throw ObjectCorruptedException.For(hash, actual);
    }

    /// <inheritdoc />
    public IReadOnlyList<string> List()
    {
        if (!Directory.Exists(directory))
        {
            return [];
        }

        return Directory.EnumerateFiles(directory).Select(Path.GetFileName).Where(n => n is not null && ContentHash.IsValid("sha256:" + n)).Select(n => "sha256:" + n).Order(StringComparer.Ordinal).ToList()!;
    }

    /// <inheritdoc />
    public bool Remove(string hash)
    {
        string path = PathOf(hash);
        if (!File.Exists(path))
        {
            return false;
        }

        File.Delete(path);
        return true;
    }

    /// <inheritdoc />
    public void Dispose()
    {
    }

    private static string Hex(string hash) => hash[ContentHash.Prefix.Length..];

    private string PathOf(string hash)
    {
        if (!ContentHash.IsValid(hash))
        {
            throw new ArgumentException("Not a content hash.", nameof(hash));
        }

        return Path.Combine(directory, Hex(hash));
    }
}
