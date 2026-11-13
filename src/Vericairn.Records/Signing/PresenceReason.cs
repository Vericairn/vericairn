// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text;

namespace Vericairn.Records.Signing;

/// <summary>
/// From 0.9.3 (FIX-124): the reason a presence prompt shows, such as the system dialog the macOS Secure Enclave helper opens. It is one
/// sentence naming which signature of how many it is and the record's type, key and purpose, completing "... is trying to"; it never
/// carries a secret or a machine path, and it is bounded in length.
/// </summary>
public static class PresenceReason
{
    /// <summary>The reason a prompt shows when nothing more is known, as before 0.9.3.</summary>
    public const string Default = "sign a Vericairn record";

    /// <summary>The most characters a reason holds; a longer one is cut with an ellipsis.</summary>
    public const int MaxLength = 200;

    /// <summary>
    /// The reason held to its rules: control characters become spaces, runs of white space one space, anything that looks like a path
    /// on a machine (a word starting with a slash or a tilde, a drive letter, or a backslash) is replaced by "[path]", and the sentence is
    /// cut to <see cref="MaxLength"/> characters. An empty reason is <see cref="Default"/>.
    /// </summary>
    public static string Text(string? reason)
    {
        if (string.IsNullOrWhiteSpace(reason)) return Default;
        var words = new List<string>();
        foreach (string word in Normalize(reason).Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            words.Add(LooksLikePath(word) ? "[path]" : word);
        }

        string text = string.Join(' ', words);
        if (text.Length == 0) return Default;
        return text.Length <= MaxLength ? text : text[..(MaxLength - 1)].TrimEnd() + "…";
    }

    private static string Normalize(string reason)
    {
        var builder = new StringBuilder(Math.Min(reason.Length, MaxLength * 4));
        foreach (char c in reason)
        {
            builder.Append(char.IsControl(c) || char.IsWhiteSpace(c) || char.GetUnicodeCategory(c) == System.Globalization.UnicodeCategory.Format ? ' ' : c);
            if (builder.Length >= MaxLength * 4) break;
        }

        return builder.ToString();
    }

    // A word that would name a place on a machine: an absolute or home-relative Unix path, a Windows drive or share, or any backslash.
    private static bool LooksLikePath(string word)
    {
        string bare = word.TrimStart('(', '[', '"', '\'');
        return bare.StartsWith('/') || bare.StartsWith('~') || bare.Contains('\\', StringComparison.Ordinal)
            || bare.Length >= 3 && char.IsAsciiLetter(bare[0]) && bare[1] == ':' && bare[2] is '/' or '\\';
    }
}
