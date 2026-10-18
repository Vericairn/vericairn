// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.RegularExpressions;

namespace Vericairn.Records.Updates;

// Only stable release tags are knowledge of an available upgrade. Links are derived from a validated tag,
// never taken from arbitrary response text, and build metadata does not affect precedence.
/// <summary>A stable published release observed by a release source, with a validated tag.</summary>
/// <param name="Tag">The release tag.</param>
public sealed partial record PublishedRelease(string Tag)
{
    internal const string Repository = "Vericairn/vericairn";
    internal string Version => Tag.StartsWith('v') ? Tag[1..] : Tag;
    internal string NotesUrl => $"https://github.com/{Repository}/releases/tag/{Uri.EscapeDataString(Tag)}";

    /// <summary>Accepts a stable release tag, or returns null for malformed or prerelease tags.</summary>
    public static PublishedRelease? FromTag(string? tag) => tag is { Length: <= 100 }
        && StableCore(tag.StartsWith('v') ? tag[1..] : tag) is not null ? new(tag) : null;

    internal bool IsNewerThan(string? installed)
    {
        if (installed is null) return false;
        string precedence = installed.Split('+')[0];
        string[] parts = precedence.Split('-', 2);
        System.Version? other = StableCore(parts[0]);
        System.Version? candidate = StableCore(Version);
        return other is not null && candidate is not null && (candidate > other || (candidate == other && parts.Length == 2));
    }

    private static System.Version? StableCore(string version) => StableVersion().IsMatch(version)
        && System.Version.TryParse(version.Split('+')[0], out System.Version? parsed) ? parsed : null;

    [GeneratedRegex(@"\A(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)(\+[0-9A-Za-z-]+(\.[0-9A-Za-z-]+)*)?\z")]
    private static partial Regex StableVersion();
}
