// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using Vericairn.Protocol;
using Vericairn.Records.Estate;
using Vericairn.Runtime.Policies;

namespace Vericairn.Runtime.Service;

/// <summary>
/// What a release basis keeps in the estate's objects. A repository read at an exact commit is named by path and hash alone:
/// its bytes stay in that repository's git objects, where the commit fixes them, and a reader reads them there again. Only the
/// files read from a working tree, such as the estate's own documents, are retained as objects, within a fixed bound.
/// </summary>
internal static class ReleaseBasisRetention
{
    /// <summary>The most source files one basis retains as objects.</summary>
    internal const int MaximumFiles = 4096;

    /// <summary>The most source bytes one basis retains as objects, 64 MiB.</summary>
    internal const long MaximumBytes = 64L * 1024 * 1024;

    /// <summary>Codes of the objections that measure what a basis would write; a basis that draws one is never retained.</summary>
    internal static readonly IReadOnlySet<string> SizeCodes = new HashSet<string>(StringComparer.Ordinal)
    {
        "release.basis-size", "release.basis-file-size", "release.basis-retained",
    };

    /// <summary>True when a repository revision is an exact commit id, whose files a reader takes from git rather than from the estate.</summary>
    internal static bool AtExactCommit(string? revision) =>
        revision is { Length: 40 or 64 } && revision.All(c => char.IsAsciiDigit(c) || c is >= 'a' and <= 'f');

    /// <summary>The available files a basis retains as objects: those of repositories not read at an exact commit.</summary>
    internal static IEnumerable<(PolicyRepository Repository, PolicyFile File)> Retained(PolicyBasis basis) =>
        basis.Repositories.Where(r => !AtExactCommit(r.Revision)).SelectMany(r => r.Files.Where(f => f.Available).Select(f => (r, f)));

    /// <summary>The objections to what a basis would retain: an oversized file, or more than the bound in files or bytes.</summary>
    internal static IEnumerable<FindingDto> Objections(PolicyBasis basis)
    {
        int files = 0;
        long bytes = 0;
        foreach ((PolicyRepository repository, PolicyFile file) in Retained(basis))
        {
            files++;
            bytes += file.Bytes.Length;
            if (file.Bytes.Length > EstateLayout.MaxObjectBytes)
                yield return new("error", "release.basis-file-size", repository.Identity + ":" + file.Path, $"A retained source file exceeds the {EstateLayout.MaxObjectBytes}-byte object limit.");
        }
        if (files > MaximumFiles || bytes > MaximumBytes)
            yield return new("error", "release.basis-retained", "release-basis", Exceeded(files, bytes));
    }

    /// <summary>The refusal naming the bound, for the gate's objection and the retention's own check.</summary>
    internal static string Exceeded(int files, long bytes) =>
        $"The basis would retain {files} source files of {bytes} bytes in the estate's objects; a basis retains at most {MaximumFiles} files and {MaximumBytes} bytes, " +
        "the files read from a working tree. Files of a repository read at an exact commit are kept by path and hash and read from its git objects.";
}
