// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Collections.Immutable;
using System.Text;
using Vericairn.Records.Hashing;
using Vericairn.Records.Tests;
using Vericairn.Runtime.Policies;

namespace Vericairn.Runtime.Tests;

public sealed class PolicyBasisOrderTests
{
    [Fact]
    public void SourcesKeepTheirOrderWhenOnlyHashesBreakTies()
    {
        using var fixture = new PolicyFixture();
        // Files that tie on path, hash, availability and link differ only in their bytes, as a misreporting source's would.
        PolicyFile[] files =
        [
            File("b.md", "b"), File("a.md", "a"), File("a.md", "z", "sha256:00"), File("a.md", "y", "sha256:00"),
            File("a.md", "y", "sha256:00", available: false), File("a.md", "x", "sha256:00", link: true), File("a.md", "w", "sha256:00", available: false, link: true),
        ];
        PolicyTreeComparison tree = new(new string('1', 40), new string('2', 40), ["src"], true, "sha256:01", "sha256:02");
        PolicyTreeComparison other = tree with { SelectedTree = "sha256:03" };
        PolicyRepository[] repositories =
        [
            Repository("b", "1", true, false, files), Repository("a", "2", true, false, files), Repository("a", "1", true, false, files),
            Repository("a", "1", false, false, files), Repository("a", "1", true, true, files),
            Repository("a", "1", true, false, [File("c.md", "1")]), Repository("a", "1", true, false, [File("c.md", "2")]),
            Repository("a", "1", true, false, [File("c.md", "1")], tree), Repository("a", "1", true, false, [File("c.md", "1")], other),
            Repository("a", "1", true, false, [File("c.md", "1")], other, tree), Repository("a", "1", true, false, []), Repository("a", "1", true, false, [], tree),
        ];
        for (int rotation = 0; rotation < repositories.Length; rotation++)
        {
            PolicyRepository[] input = [.. repositories.Skip(rotation), .. repositories.Take(rotation).Reverse()];
            PolicyBasis basis = PolicyBasis.Recorded(fixture.Estate.Load(), input);
            Assert.Equal(Former(input).Select(Describe), basis.Repositories.Select(Describe));
        }
    }

    // The order the basis used before ties were broken lazily: every key computed for every source.
    private static IEnumerable<PolicyRepository> Former(IEnumerable<PolicyRepository> repositories) =>
        repositories.Select(r => r with { TreeComparisons = r.TreeComparisons.OrderBy(PolicyBasis.Canonical, StringComparer.Ordinal).ToImmutableArray(), Files = r.Files.OrderBy(f => f.Path, StringComparer.Ordinal).ThenBy(f => f.Hash, StringComparer.Ordinal)
                .ThenBy(f => f.Available).ThenBy(f => f.IsLink).ThenBy(f => ContentHash.Sha256(f.Bytes.AsSpan()), StringComparer.Ordinal).ToImmutableArray() })
            .OrderBy(r => r.Identity, StringComparer.Ordinal).ThenBy(r => r.Revision, StringComparer.Ordinal).ThenBy(r => r.Contract, StringComparer.Ordinal).ThenBy(r => r.Complete)
            .ThenBy(r => r.Evidence).ThenBy(r => PolicyBasis.Hash(r.Files), StringComparer.Ordinal).ThenBy(r => PolicyBasis.Hash(r.TreeComparisons), StringComparer.Ordinal);

    private static string Describe(PolicyRepository repository) => string.Join("|", repository.Identity, repository.Revision, repository.Contract, repository.Complete, repository.Evidence,
        string.Join(",", repository.Files.Select(f => $"{f.Path}:{f.Hash}:{f.Available}:{f.IsLink}:{Convert.ToHexString(f.Bytes.AsSpan())}")), PolicyBasis.Canonical(repository.TreeComparisons));

    private static PolicyFile File(string path, string text, string? hash = null, bool available = true, bool link = false)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(text);
        return new(path, hash ?? ContentHash.Sha256(bytes), [.. bytes], available, link);
    }

    private static PolicyRepository Repository(string identity, string revision, bool complete, bool evidence, PolicyFile[] files, params PolicyTreeComparison[] trees) =>
        new(identity, revision, PolicyBasis.RepositoryContract, complete, [.. files], evidence) { TreeComparisons = [.. trees] };
}
