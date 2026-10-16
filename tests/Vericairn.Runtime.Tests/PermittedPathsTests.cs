// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.RegularExpressions;
using Vericairn.Runtime.Workers;

namespace Vericairn.Runtime.Tests;

public class PermittedPathsTests
{
    private static readonly (string Path, string Pattern, bool Expected)[] Cases =
    [
        ("src/a.cs", "src/**/*.cs", true),
        ("src/sub/a.cs", "src/**/*.cs", true),
        ("src/sub/a.cs", "src/*.cs", false),
        ("src/a.cs", "src/*.cs", true),
        ("src", "src/", true),
        ("src/a.cs", "src/", true),
        ("src-other/a.cs", "src/", false),
        ("SRC/a.cs", "src/**", false),
        ("a.cs", "**/*.cs", true),
        ("sub/a.cs", "**/*.cs", true),
        ("src/", "src/**", true),
        ("src", "src/**", false),
        ("src/a/b", "src/**", true),
        ("a/b.cs", "**.cs", true),
        ("src/a/b.cs", "src**.cs", true),
        ("a.cs", "***/*.cs", true),
        ("a.cs", "*****/*.cs", true),
        ("sub/a.cs", "****/*.cs", true),
        ("src/a.cs", "src/?.cs", true),
        ("src/ab.cs", "src/?.cs", false),
        ("src//.cs", "src/?.cs", false),
        ("", "", true),
        ("a", "", false),
        ("/a", "", true),
        ("", "/", true),
        ("", "*", true),
        ("", "?", false),
        ("", "**", true),
        ("a/b", "**", true),
        ("a/b", "*", false),
        ("/", "**/", true),
        ("a/b", "**/", true),
        ("a/", "a", true),
        ("a//b", "a/", true),
        ("a//b", "a/*/b", true),
        ("a/b", "a/*/b", false),
        ("a//b", "a/?/b", false),
        ("a//b", "a/**/b", true),
        ("a/b", "a/**/b", true),
        ("a/x/b", "a/**/b", true),
        ("a/x/y/b", "a/**/b", true),
        ("a/bc", "a/**/b", false),
        ("a/z", "a/**/b", false),
        ("src/", "src/*", true),
        ("src/a/", "src/*", false),
        ("src/a", "src/*/", true),
        ("src/a/", "src/*/", false),
        ("a", "**/a", true),
        ("/a", "**/a", true),
        ("//a", "**/a", true),
        ("ab", "a**/b", true),
        ("a/b", "a**b", true),
        ("a/.cs", "a/?.cs", false),
        ("A", "?", true),
        ("\U0001f600", "?", false),
        ("\U0001f600", "??", true),
        ("e\u0301", "\u00e9", false),
        ("a\nb", "*", true),
        ("a\nb", "**", false),
        ("a\nb", "a?b", true),
        ("a\n/b", "**/b", false),
        ("a\n/b", "*/b", true),
        ("a\r/b", "**/b", true),
        ("a.b", "a.b", true),
        ("a+b", "a+b", true),
        ("[a]", "[a]", true),
        ("a", "[a]", false),
        ("a[1].cs", "a[?].cs", true),
        ("a/b", "**?**", true),
        ("/", "**?**", false),
        ("a/", "**?", false),
        ("a/b", "?**", true),
        ("a/b", "**/?", true),
        ("abc", "**/?", false),
        ("a/b", "**/**", true),
        ("a", "**/**/a", true),
        ("a", "a***", true),
        ("a/b", "a***", true),
    ];

    public static TheoryData<string, string, bool> MatchingCases
    {
        get
        {
            var data = new TheoryData<string, string, bool>();
            foreach ((string path, string pattern, bool expected) in Cases)
            {
                data.Add(path, pattern, expected);
            }

            return data;
        }
    }

    [Theory]
    [MemberData(nameof(MatchingCases))]
    public void GlobVerdictsPreserveSegmentBoundariesAndOrdinalComparison(string path, string pattern, bool expected) =>
        Assert.Equal(expected, PermittedPaths.Matches(path, pattern));

    [Fact]
    public void ManyWildcardsHaveAVerdictWithoutARegexComplexityLimit()
    {
        string pattern = string.Concat(Enumerable.Repeat("*a", 500));
        Assert.True(PermittedPaths.Matches(new string('a', 500), pattern));
        Assert.False(PermittedPaths.Matches(new string('a', 500) + "b", pattern));
    }

    [Fact]
    public void MaximumLengthPatternsHaveAVerdictWithoutARegexComplexityLimit()
    {
        string pattern = new('?', 2000);
        Assert.True(PermittedPaths.Relative(pattern));
        Assert.True(PermittedPaths.Matches(new string('a', 2000), pattern));
        Assert.False(PermittedPaths.Matches(new string('a', 1999), pattern));
    }

    [Fact]
    public void PathInspectionFailuresPropagateWithoutAFalseVerdict() =>
        Assert.Throws<ArgumentException>(() => PermittedPaths.Allows("src/a.cs", "invalid\0workspace", ["src/"]));

    [Fact]
    public void SmallPatternsAgreeWithThePreviousDialectExhaustively()
    {
        string[] paths = Words("ab/\n", 4).ToArray();
        foreach (string pattern in Words("a/*?", 5))
        {
            string plain = pattern.TrimEnd('/');
            // The old translation is an independent oracle on this finite, tiny corpus. It has no time-based assertion.
            string expression = Regex.Escape(plain).Replace(@"\*\*/", "(?:.*/)?", StringComparison.Ordinal)
                .Replace(@"\*\*", ".*", StringComparison.Ordinal).Replace(@"\*", "[^/]*", StringComparison.Ordinal).Replace(@"\?", "[^/]", StringComparison.Ordinal);
            var previous = new Regex("\\A" + expression + "\\z", RegexOptions.CultureInvariant | RegexOptions.NonBacktracking, Regex.InfiniteMatchTimeout);
            foreach (string path in paths)
            {
                bool expected = !plain.Contains('*', StringComparison.Ordinal) && !plain.Contains('?', StringComparison.Ordinal)
                    ? path == plain || path.StartsWith(plain + "/", StringComparison.Ordinal)
                    : previous.IsMatch(path);
                Assert.True(expected == PermittedPaths.Matches(path, pattern), $"Path {System.Text.Json.JsonSerializer.Serialize(path)}, pattern {System.Text.Json.JsonSerializer.Serialize(pattern)}.");
            }
        }
    }

    [Fact]
    public void ConcurrentChecksKeepEveryVerdictIncludingNormalizedTraversal()
    {
        string workspace = Path.Combine(Path.GetTempPath(), "glob-concurrent-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(workspace, "src"));
        try
        {
            Parallel.For(0, 512, new ParallelOptions { MaxDegreeOfParallelism = 8, CancellationToken = TestContext.Current.CancellationToken }, _ =>
            {
                foreach ((string path, string pattern, bool expected) in Cases)
                {
                    Assert.Equal(expected, PermittedPaths.Matches(path, pattern));
                }

                Assert.True(PermittedPaths.Allows("src/sub/../a.cs", workspace, ["path: src/**/*.cs"]));
                Assert.False(PermittedPaths.Allows("src-other/a.cs", workspace, ["src/"]));
                Assert.False(PermittedPaths.Allows("../a.cs", workspace, ["**"]));
            });
        }
        finally
        {
            Directory.Delete(workspace, recursive: true);
        }
    }

    private static IEnumerable<string> Words(string alphabet, int length)
    {
        yield return "";
        if (length > 0)
        {
            foreach (string prefix in Words(alphabet, length - 1))
            {
                foreach (char character in alphabet)
                {
                    yield return prefix + character;
                }
            }
        }
    }
}
