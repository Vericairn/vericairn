// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Security.Cryptography;

namespace Vericairn.TestSupport;

internal static class SharedFileHash
{
    // A test that proves a read left the estate unchanged hashes files while SQLite still holds a live store open for
    // writing. Windows refuses a reader that does not share writes, so the snapshot shares them; it never writes.
    internal static string Sha256(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        return "sha256:" + Convert.ToHexStringLower(SHA256.HashData(stream));
    }
}
