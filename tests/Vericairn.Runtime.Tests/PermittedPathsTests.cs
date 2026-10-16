// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using Vericairn.Runtime.Workers;

namespace Vericairn.Runtime.Tests;

public class PermittedPathsTests
{
    [Theory]
    [InlineData("src/a.cs", "src/**/*.cs", true)]
    [InlineData("src/sub/a.cs", "src/**/*.cs", true)]
    [InlineData("src/sub/a.cs", "src/*.cs", false)]
    [InlineData("src/a.cs", "src/*.cs", true)]
    [InlineData("src", "src/", true)]
    [InlineData("src/a.cs", "src/", true)]
    [InlineData("src-other/a.cs", "src/", false)]
    [InlineData("SRC/a.cs", "src/**", false)]
    [InlineData("a.cs", "**/*.cs", true)]
    [InlineData("sub/a.cs", "**/*.cs", true)]
    [InlineData("src/", "src/**", true)]
    [InlineData("src", "src/**", false)]
    [InlineData("src/a/b", "src/**", true)]
    [InlineData("a/b.cs", "**.cs", true)]
    [InlineData("src/a/b.cs", "src**.cs", true)]
    [InlineData("a.cs", "***/*.cs", true)]
    [InlineData("a.cs", "*****/*.cs", true)]
    [InlineData("sub/a.cs", "****/*.cs", true)]
    [InlineData("src/a.cs", "src/?.cs", true)]
    [InlineData("src/ab.cs", "src/?.cs", false)]
    [InlineData("src//.cs", "src/?.cs", false)]
    public void GlobVerdictsPreserveSegmentBoundariesAndOrdinalComparison(string path, string pattern, bool expected) =>
        Assert.Equal(expected, PermittedPaths.Matches(path, pattern));

    [Fact]
    public void ManyWildcardsHaveAVerdictWithoutARegexComplexityLimit()
    {
        string pattern = string.Concat(Enumerable.Repeat("*a", 500));
        Assert.True(PermittedPaths.Matches(new string('a', 500), pattern));
        Assert.False(PermittedPaths.Matches(new string('a', 500) + "b", pattern));
    }
}
