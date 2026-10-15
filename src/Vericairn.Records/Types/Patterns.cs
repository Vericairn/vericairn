// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.RegularExpressions;

namespace Vericairn.Records.Types;

/// <summary>Shared field patterns.</summary>
public static partial class Patterns
{
    /// <summary>Lower-case kebab-case slug.</summary>
    public static Regex Slug => SlugRegex();

    /// <summary>Upper-case work-order key such as M0 or DESIGN-01.</summary>
    public static Regex WorkKey => WorkKeyRegex();

    /// <summary>Repository-relative path under design/ or docs/ with forward slashes.</summary>
    public static Regex DocumentPath => DocumentPathRegex();

    /// <summary>GitHub owner/name.</summary>
    public static Regex GitHubRepository => GitHubRepositoryRegex();

    /// <summary>Semantic version.</summary>
    public static Regex SemanticVersion => SemanticVersionRegex();

    /// <summary>A 40-character lower-case hex git commit id.</summary>
    public static Regex Commit => CommitRegex();

    /// <summary>A currency unit: three upper-case letters, as ISO 4217 writes them.</summary>
    public static Regex CurrencyUnit => CurrencyUnitRegex();

    /// <summary>A normalized repository-relative path: forward slashes, no leading slash, no empty or parent segment.</summary>
    public static Regex RelativePath => RelativePathRegex();

    /// <summary>A pack's or the foundation's contribution: a namespace, a slash and a name, such as vericairn.software/git-file.</summary>
    public static Regex Contribution => ContributionRegex();

    [GeneratedRegex("^[a-z0-9]+(-[a-z0-9]+)*$")]
    private static partial Regex SlugRegex();

    [GeneratedRegex("^[A-Z][A-Z0-9]*(-[A-Z0-9]+)*$")]
    private static partial Regex WorkKeyRegex();

    [GeneratedRegex(@"^(design|docs)/([A-Za-z0-9._-]+/)*[A-Za-z0-9._-]+\.[A-Za-z0-9]+$")]
    private static partial Regex DocumentPathRegex();

    [GeneratedRegex(@"^[A-Za-z0-9](?:[A-Za-z0-9-]*[A-Za-z0-9])?/[A-Za-z0-9._-]+$")]
    private static partial Regex GitHubRepositoryRegex();

    [GeneratedRegex(@"^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)(-[0-9A-Za-z.-]+)?$")]
    private static partial Regex SemanticVersionRegex();

    [GeneratedRegex("^[A-Z]{3}$")]
    private static partial Regex CurrencyUnitRegex();

    [GeneratedRegex(@"^(?!.*(^|/)\.\.?(/|$))[A-Za-z0-9._-]+(/[A-Za-z0-9._-]+)*$")]
    private static partial Regex RelativePathRegex();

    [GeneratedRegex(@"^[a-z0-9]+(\.[a-z0-9-]+)*/[a-z0-9]+(-[a-z0-9]+)*$")]
    private static partial Regex ContributionRegex();

    [GeneratedRegex("^[0-9a-f]{40}$")]
    private static partial Regex CommitRegex();
}
