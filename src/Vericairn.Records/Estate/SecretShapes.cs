// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.RegularExpressions;

namespace Vericairn.Records.Estate;

/// <summary>
/// The shapes the estate check refuses in an estate: credentials by their known formats, property names that hold secrets, and
/// absolute machine paths. The support bundle redacts by the same shapes, so what one finds the other removes.
/// </summary>
internal static partial class SecretShapes
{
    /// <summary>The start of an absolute machine path on macOS, Linux or Windows.</summary>
    [GeneratedRegex(@"(?<![A-Za-z0-9_./-])(?:/Users/|/home/[A-Za-z0-9_-]+/|/Volumes/|/private/var/|/mnt/[a-z]/|[A-Za-z]:\\)")]
    internal static partial Regex AbsolutePath();

    /// <summary>A credential in one of the formats the estate check knows.</summary>
    [GeneratedRegex(@"gh[pousr]_[A-Za-z0-9]{36,}|github_pat_[A-Za-z0-9_]{22,}|AKIA[0-9A-Z]{16}|-----BEGIN [A-Z ]*PRIVATE KEY-----|xox[baprs]-[A-Za-z0-9-]{10,}|\bsk-[A-Za-z0-9_-]{32,}|AIza[0-9A-Za-z_-]{35}|eyJ[A-Za-z0-9_-]{20,}\.[A-Za-z0-9_-]{20,}\.[A-Za-z0-9_-]{20,}")]
    internal static partial Regex Secret();

    /// <summary>
    /// One element of inline data on a line, a data: URI from its scheme to the end of its data (FIX-172): a credential-shaped match is
    /// placed by the ordinal of the element it lies in, so that a reviewer finds it among several on one long line. The scheme counts only
    /// where no letter, digit, plus, dot or hyphen comes before it, so that "metadata:" is none; the data ends at whitespace, a quote, an
    /// angle bracket, a parenthesis or a backslash, as it does in an attribute, a style, a link or a JSON string.
    /// </summary>
    [GeneratedRegex(@"(?<![A-Za-z0-9+.-])(?i:data):[A-Za-z0-9!#$&^_.+-]*(?:/[A-Za-z0-9!#$&^_.+-]+)?(?:;[A-Za-z0-9!#$&^_.+=-]+)*,[^\s""'<>()\\]*")]
    internal static partial Regex InlineData();

    /// <summary>A property name that holds a secret value.</summary>
    [GeneratedRegex(@"^(?i)(secret|token|password|passphrase|api[_-]?key|private[_-]?key|access[_-]?key|client[_-]?secret)$")]
    internal static partial Regex SecretKeyName();
}
