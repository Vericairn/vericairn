// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

namespace Vericairn.Runtime.Objects;

/// <summary>
/// An object store that measures an object without reading it and reads one into a destination of exactly its length (FIX-163), so that
/// a reader of untrusted references never reads more than it has checked.
/// </summary>
internal interface IBoundedObjectReads
{
    /// <summary>The number of bytes held under a hash, found without reading them; null when no object is held there.</summary>
    long? Length(string hash);

    /// <summary>Reads the bytes held under a hash into a destination of exactly their length; false when they are not held at that length.</summary>
    bool ReadExactly(string hash, Span<byte> destination);
}
