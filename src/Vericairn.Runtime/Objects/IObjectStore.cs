// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jayson Furr

namespace Vericairn.Runtime.Objects;

/// <summary>Content-addressed custody of bytes: an object is named by the SHA-256 of its content and verified on every read.</summary>
public interface IObjectStore : IDisposable
{
    /// <summary>Which implementation this is, for status and qualification records.</summary>
    string Kind { get; }

    /// <summary>Stores bytes and returns their content hash; storing the same bytes twice is harmless.</summary>
    string Put(ReadOnlySpan<byte> bytes);

    /// <summary>True when the object is held.</summary>
    bool Has(string hash);

    /// <summary>The object's bytes, verified against its hash; null when absent. A held object whose bytes no longer match throws <see cref="ObjectCorruptedException"/>.</summary>
    byte[]? Read(string hash);

    /// <summary>Every hash held, in ordinal order.</summary>
    IReadOnlyList<string> List();

    /// <summary>Removes an object; true when it was held.</summary>
    bool Remove(string hash);
}

/// <summary>A held object's bytes do not match its name: custody was violated outside the store.</summary>
public sealed class ObjectCorruptedException : Exception
{
    /// <summary>Creates the exception.</summary>
    public ObjectCorruptedException()
    {
    }

    /// <summary>Creates the exception with a message.</summary>
    public ObjectCorruptedException(string message)
        : base(message)
    {
    }

    /// <summary>Creates the exception with a message and cause.</summary>
    public ObjectCorruptedException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>Creates the exception for a hash.</summary>
    public static ObjectCorruptedException For(string hash, string actual) => new($"Object {hash} holds bytes with hash {actual}; the custody was violated outside the store.");
}
