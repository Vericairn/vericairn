// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Security.Cryptography;

namespace Vericairn.Records.Hashing;

/// <summary>SHA-256 content hashes in the textual form <c>sha256:&lt;64 lower-case hex&gt;</c>.</summary>
public static class ContentHash
{
    /// <summary>Textual prefix.</summary>
    public const string Prefix = "sha256:";

    /// <summary>Total textual length.</summary>
    public const int TextLength = 71;

    /// <summary>Hashes bytes.</summary>
    public static string Sha256(ReadOnlySpan<byte> bytes) => Prefix + Convert.ToHexStringLower(SHA256.HashData(bytes));

    /// <summary>Hashes a file's contents.</summary>
    public static string Sha256File(string path)
    {
        using FileStream stream = File.OpenRead(path);
        return Prefix + Convert.ToHexStringLower(SHA256.HashData(stream));
    }

    /// <summary>True when the text is a well-formed hash.</summary>
    public static bool IsValid(string? text)
    {
        if (text is null || text.Length != TextLength || !text.StartsWith(Prefix, StringComparison.Ordinal))
        {
            return false;
        }

        for (int i = Prefix.Length; i < text.Length; i++)
        {
            char c = text[i];
            if (c is not ((>= '0' and <= '9') or (>= 'a' and <= 'f')))
            {
                return false;
            }
        }

        return true;
    }
}
