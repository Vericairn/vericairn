// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text;

namespace Vericairn.Adapters.Git;

/// <summary>
/// Compares working-tree names as Git does when it decides whether a name is in the index: Unicode composed and decomposed
/// spellings are one name where Git precomposes them, and letter case is folded where Git ignores it. It errs toward treating
/// two names as one, which counts more names as tracked and so copies or refuses more, never less.
/// </summary>
internal sealed class GitPathComparer(bool precompose, bool ignoreCase) : IEqualityComparer<string>
{
    public bool Equals(string? x, string? y) => string.Equals(Key(x), Key(y), StringComparison.Ordinal);

    public int GetHashCode(string obj) => StringComparer.Ordinal.GetHashCode(Key(obj)!);

    private string? Key(string? name)
    {
        if (name is null) return null;
        if (precompose && !name.IsNormalized(NormalizationForm.FormC)) name = name.Normalize(NormalizationForm.FormC);
        return ignoreCase ? name.ToUpperInvariant() : name;
    }
}
