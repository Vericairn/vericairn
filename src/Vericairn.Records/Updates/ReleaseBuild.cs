// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Runtime.InteropServices;
using Vericairn.Records.Model;

namespace Vericairn.Records.Updates;

/// <summary>The target build's own identity and record-format capabilities.</summary>
/// <param name="Version">Stable version without build metadata.</param>
/// <param name="Commit">Exact source commit embedded in that build.</param>
/// <param name="Reads">Record formats the target reads.</param>
/// <param name="Writes">Record format the target writes.</param>
public sealed record ReleaseBuild(string Version, string Commit, IReadOnlyList<long> Reads, long Writes)
{
    /// <summary>The full build identity recorded by an observer.</summary>
    public string Identity => Version + "+" + Commit;

    /// <summary>Reads a published build's informational version; development builds cannot observe a release pin.</summary>
    public static ReleaseBuild Parse(string identity)
    {
        ArgumentNullException.ThrowIfNull(identity);
        string[] parts = identity.Split('+');
        if (parts.Length != 2 || PublishedRelease.FromTag(parts[0]) is null || parts[0].StartsWith('v') || !IsCommit(parts[1]))
            throw new InvalidOperationException("A release pin requires a stable published build with its full source commit. Run the target release's command line; omit --release to initialize a source pin.");
        return new(parts[0], parts[1], RecordFormats.Reads, RecordFormats.Current);
    }

    /// <summary>The running process's distributable platform.</summary>
    public static string Platform => (OperatingSystem.IsWindows() ? "win" : OperatingSystem.IsMacOS() ? "osx" : OperatingSystem.IsLinux() ? "linux" : "unknown")
        + "-" + RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant();

    internal static bool IsCommit(string? value) => value is { Length: 40 } && value.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');
}
