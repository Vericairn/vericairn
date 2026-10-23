// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

namespace Vericairn.Runtime.Store;

// SQLite can create or update WAL sidecars even for a read-only connection. Inspect a disposable copy,
// including committed WAL pages, so inspection never touches the original database or its sidecars.
// Like repository reads, this assumes the documented single-runtime ownership of canonical storage.
internal sealed class SqliteReadSnapshot : IDisposable
{
    private readonly string directory;
    private bool disposed;
    internal string Path { get; }

    internal SqliteReadSnapshot(string source)
    {
        directory = Directory.CreateTempSubdirectory("vericairn-sqlite-read-").FullName;
        Path = System.IO.Path.Combine(directory, "snapshot.db");
        try
        {
            File.Copy(source, Path);
            try { File.Copy(source + "-wal", Path + "-wal"); }
            catch (FileNotFoundException) { }
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    public void Dispose()
    {
        if (disposed) return;
        foreach (string file in Directory.EnumerateFiles(directory)) File.SetAttributes(file, FileAttributes.Normal);
        Directory.Delete(directory, recursive: true);
        disposed = true;
    }
}
