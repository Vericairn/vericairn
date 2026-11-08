// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.RegularExpressions;

namespace Vericairn.Cli;

/// <summary>
/// Text a record may carry: no machine's path, on any platform. A known path, such as the estate's root or the guides directory, is
/// replaced by the name the record uses for it; any other rooted path, a Windows drive or UNC path such as a named pipe or a Unix path
/// of more than one segment, by &lt;path&gt;. Repository-relative paths and web addresses are kept.
/// </summary>
internal static partial class PortableText
{
    /// <summary>The text with every machine path replaced; <paramref name="known"/> maps a local path to its portable name.</summary>
    internal static string Of(string text, params (string Path, string Name)[] known)
    {
        ArgumentNullException.ThrowIfNull(text);
        string result = text;
        StringComparison comparison = OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        foreach ((string path, string name) in known.Where(k => k.Path.Length > 1).OrderByDescending(k => k.Path.Length))
        {
            string trimmed = System.IO.Path.TrimEndingDirectorySeparator(path);
            result = result.Replace(trimmed + System.IO.Path.DirectorySeparatorChar, name + "/", comparison).Replace(trimmed, name, comparison);
        }

        return MachinePath().Replace(result, "<path>");
    }

    /// <summary>True when the text still names a machine path.</summary>
    internal static bool HasMachinePath(string text) => MachinePath().IsMatch(text) || Vericairn.Records.Estate.SecretShapes.AbsolutePath().IsMatch(text);

    // A whole rooted path token: a drive (C:\ or C:/), a UNC or device path (\\server\share, \\.\pipe\name), or a Unix path of at least
    // two segments (/home/op/estate, /tmp/x/y), not inside a word, a relative path, a name such as &lt;estate&gt;/records or a web address.
    [GeneratedRegex(@"(?<![A-Za-z0-9_.~:/\\>-])(?:[A-Za-z]:[\\/]|\\\\|/(?=[^\s/""'`<>|]+/))[^\s""'`<>|]*")]
    private static partial Regex MachinePath();
}
