// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using Vericairn.Records.Hashing;

namespace Vericairn.Records.Estate;

/// <summary>
/// An estate's <c>objects/</c> directory read as the loader reads it: each object a regular file named by its hash, measured before it
/// is read, and a link or anything else not held.
/// </summary>
internal sealed class ObjectFiles(string directory) : ISplitObjectSource
{
    public long? Length(string hash, CancellationToken cancellationToken)
    {
        if (!ContentHash.IsValid(hash)) return null;
        try
        {
            var info = new FileInfo(Path.Combine(directory, hash[ContentHash.Prefix.Length..]));
            return info.Exists && info.LinkTarget is null ? info.Length : null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public bool Read(string hash, Span<byte> destination, CancellationToken cancellationToken)
    {
        if (!ContentHash.IsValid(hash)) return false;
        try
        {
            string path = Path.Combine(directory, hash[ContentHash.Prefix.Length..]);
            if (new FileInfo(path).LinkTarget is not null) return false;
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1, FileOptions.SequentialScan);
            if (stream.Length != destination.Length) return false;
            stream.ReadExactly(destination);
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
