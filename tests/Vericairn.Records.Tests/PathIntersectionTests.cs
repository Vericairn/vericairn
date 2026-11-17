// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using Vericairn.Records.Estate;

namespace Vericairn.Records.Tests;

/// <summary>
/// From 0.9.4 (CODE-73): the exact overlap of two parties' effective permitted paths against brute-force enumeration over generated
/// grammars, with exclusions, selections, the valid-path language, the state bound and the length limit.
/// </summary>
public class PathIntersectionTests
{
    // Path characters the enumeration uses: literals of the generated patterns, a slash, a dot, a space and one other character.
    private const string PathChars = "./ abz";
    private const int Longest = 5;

    private static readonly string[] Enumerated = Enumerate().ToArray();

    [Fact]
    public void GeneratedWorkerGrammarsAgreeWithEnumeration()
    {
        var random = new Random(7301);
        int overlapping = 0;
        for (int round = 0; round < 400; round++)
        {
            var first = new PathIntersection.Party(Patterns(random, 1 + random.Next(2)), Patterns(random, random.Next(2)));
            var second = new PathIntersection.Party(Patterns(random, 1 + random.Next(2)), Patterns(random, random.Next(2)));
            overlapping += Agree(first, second, null, $"round {round}") ? 1 : 0;
        }

        // The generated grammars exercise both answers.
        Assert.InRange(overlapping, 40, 360);
    }

    [Fact]
    public void GeneratedSelectionsAgreeWithEnumeration()
    {
        var random = new Random(7302);
        int overlapping = 0;
        for (int round = 0; round < 300; round++)
        {
            var first = new PathIntersection.Party(Patterns(random, 1 + random.Next(2)), Patterns(random, random.Next(2)));
            var second = new PathIntersection.Party(Patterns(random, 1 + random.Next(2)), Patterns(random, random.Next(2)));
            string[] selectors = [.. Enumerable.Range(0, 1 + random.Next(2)).Select(_ => Selector(random))];
            overlapping += Agree(first, second, selectors, $"round {round} with selectors {string.Join(", ", selectors)}") ? 1 : 0;
        }

        Assert.InRange(overlapping, 20, 280);
    }

    [Theory]
    [InlineData("src/**", "tests/**", false, null)]
    [InlineData("src/**", "src/a.cs", true, "src/a.cs")]
    [InlineData("docs/", "docs/guide.md", true, "docs/guide.md")]
    [InlineData("design/**", "design/**", true, "design/.")]
    [InlineData("a*", "a/b", false, null)]
    [InlineData("a", "a", true, "a")]
    [InlineData("***/x", "y/x", true, "y/x")]
    [InlineData("path:my notes", "my*", true, "my notes")]
    public void PairsOverlapAsTheWorkerReadsThem(string first, string second, bool overlap, string? witness)
    {
        string a = PathPatterns.Pattern(first) ?? first;
        string b = PathPatterns.Pattern(second) ?? second;
        PathIntersection.Outcome outcome = PathIntersection.Search(new([a], []), new([b], []));
        Assert.True(outcome.Determined);
        Assert.Equal(overlap, outcome.Overlap);
        if (witness == "design/.")
        {
            // The shortest valid path under design/: a component that is neither empty, "." nor "..".
            Assert.Equal(8, outcome.Witness!.Length);
            Assert.StartsWith("design/", outcome.Witness, StringComparison.Ordinal);
            Assert.True(PathPatterns.Valid(outcome.Witness));
        }
        else
        {
            Assert.Equal(witness, outcome.Witness);
        }
    }

    [Fact]
    public void AnExclusionOfTheSharedTreeLeavesTheTreesDisjoint()
    {
        var owner = new PathIntersection.Party(["docs/**"], []);
        var other = new PathIntersection.Party(["docs/**"], ["docs/operator-guide.md"]);
        PathIntersection.Outcome both = PathIntersection.Search(owner, other);
        Assert.True(both.Overlap);
        Assert.NotEqual("docs/operator-guide.md", both.Witness);
        var narrowed = new PathIntersection.Party(["docs/**"], ["docs/**"]);
        Assert.False(PathIntersection.Search(owner, narrowed).Overlap);
        // Within a selection over the guide alone, the exclusion resolves the overlap.
        Assert.False(PathIntersection.Search(owner, other, ["docs/operator-guide.md"]).Overlap);
        Assert.True(PathIntersection.Search(owner, owner, ["docs/operator-guide.md"]).Overlap);
    }

    [Fact]
    public void ASearchThatReachesItsBoundIsUndeterminedAndCountsAsOverlap()
    {
        var first = new PathIntersection.Party(["**"], ["*a*b*", "*b*a*"]);
        var second = new PathIntersection.Party(["**"], ["*a*a*"]);
        PathIntersection.Outcome bounded = PathIntersection.Search(first, second, bound: 6);
        Assert.True(bounded.Overlap);
        Assert.False(bounded.Determined);
        Assert.Null(bounded.Witness);
        Assert.True(bounded.States >= 6);
        PathIntersection.Outcome full = PathIntersection.Search(first, second);
        Assert.True(full.Determined);
        Assert.True(full.States < PathIntersection.StateBound);
    }

    [Fact]
    public void AShortestWitnessOverTheLengthLimitMeansNoValidOverlap()
    {
        string tree = new('a', 1999);
        var first = new PathIntersection.Party([tree], []);
        var second = new PathIntersection.Party(["*/**"], []);
        PathIntersection.Outcome outcome = PathIntersection.Search(first, second);
        Assert.True(outcome.Determined);
        Assert.False(outcome.Overlap);
        // The same trees one character shorter overlap at exactly the limit.
        PathIntersection.Outcome shorter = PathIntersection.Search(new([tree[..^1]], []), second);
        Assert.True(shorter.Overlap);
        Assert.Equal(PathIntersection.LengthLimit, shorter.Witness!.Length);
    }

    [Fact]
    public void ACaseDifferenceIsNoOverlapUnderOrdinalMatching()
    {
        Assert.False(PathIntersection.Search(new(["Docs/**"], []), new(["docs/**"], [])).Overlap);
    }

    private static bool Agree(PathIntersection.Party first, PathIntersection.Party second, string[]? selectors, string label)
    {
        PathIntersection.Outcome outcome = PathIntersection.Search(first, second, selectors);
        string? brute = Enumerated.FirstOrDefault(p => first.Contains(p) && second.Contains(p) && PathIntersection.Selected(selectors, p));
        string described = $"{label}: {string.Join("|", first.Patterns)} less {string.Join("|", first.Exclusions)} and {string.Join("|", second.Patterns)} less {string.Join("|", second.Exclusions)}";
        Assert.True(outcome.Determined, described);
        if (outcome.Witness is string witness)
        {
            Assert.True(first.Contains(witness) && second.Contains(witness) && PathIntersection.Selected(selectors, witness), $"{described}: witness '{witness}' is not in both");
        }

        if (brute is not null)
        {
            Assert.True(outcome.Overlap, $"{described}: enumeration found '{brute}'");
            Assert.Equal(brute.Length, outcome.Witness!.Length);
        }
        else
        {
            Assert.True(!outcome.Overlap || outcome.Witness!.Length > Longest, $"{described}: the search found '{outcome.Witness}', enumeration nothing up to {Longest}");
        }

        return outcome.Overlap;
    }

    private static IEnumerable<string> Enumerate()
    {
        var level = new List<string> { string.Empty };
        for (int length = 1; length <= Longest; length++)
        {
            level = [.. level.SelectMany(prefix => PathChars.Select(c => prefix + c))];
            foreach (string path in level.Where(PathPatterns.Valid).Order(StringComparer.Ordinal)) yield return path;
        }
    }

    private static string[] Patterns(Random random, int count) => [.. Enumerable.Range(0, count).Select(_ => Pattern(random))];

    private static string Pattern(Random random)
    {
        string[] pieces = ["a", "b", ".", "/", "*", "**", "**/", "?", "a/", " ", "ab", "*a", "b*"];
        string pattern;
        do
        {
            pattern = string.Concat(Enumerable.Range(0, 1 + random.Next(4)).Select(_ => pieces[random.Next(pieces.Length)]));
        }
        while (PathPatterns.Pattern("path:" + pattern) is null);
        return PathPatterns.Pattern("path:" + pattern)!;
    }

    private static string Selector(Random random)
    {
        string[] components = ["a", "b", "*", "**", "a*", "*b", ".a", "ab", "a b"];
        return string.Join('/', Enumerable.Range(0, 1 + random.Next(3)).Select(_ => components[random.Next(components.Length)]));
    }
}
