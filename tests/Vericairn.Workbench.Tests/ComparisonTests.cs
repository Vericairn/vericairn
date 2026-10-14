// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using Vericairn.Presentation;

namespace Vericairn.Workbench.Tests;

/// <summary>The line comparison behind comparison views.</summary>
public class ComparisonTests
{
    [Fact]
    public void MarksRemovedAddedAndUnchangedLines()
    {
        IReadOnlyList<DiffLine> lines = Diff.Lines("a\nb\nc", "a\nc\nd");
        Assert.Equal("  a\n- b\n  c\n+ d", Diff.Render(lines));
    }

    [Fact]
    public void IdenticalTextsHaveNoMarks()
    {
        Assert.All(Diff.Lines("x\ny", "x\ny"), l => Assert.Equal(' ', l.Mark));
    }

    [Fact]
    public void EmptySidesAreAllOneMark()
    {
        Assert.All(Diff.Lines(string.Empty, "x\ny"), l => Assert.True(l.Mark == '+' || l.Text.Length == 0));
        Assert.Contains(Diff.Lines("x", string.Empty), l => l.Mark == '-' && l.Text == "x");
    }
}
