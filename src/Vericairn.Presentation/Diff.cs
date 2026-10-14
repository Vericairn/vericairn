// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

namespace Vericairn.Presentation;

/// <summary>A line of a comparison: unchanged, removed or added, with the text.</summary>
public sealed record DiffLine(char Mark, string Text);

/// <summary>A minimal line comparison (longest common subsequence) for comparison views; the raw sides stay available beside it.</summary>
public static class Diff
{
    /// <summary>Compares two texts line by line.</summary>
    public static IReadOnlyList<DiffLine> Lines(string left, string right)
    {
        string[] a = (left ?? string.Empty).Split('\n');
        string[] b = (right ?? string.Empty).Split('\n');
        int[,] table = new int[a.Length + 1, b.Length + 1];
        for (int i = a.Length - 1; i >= 0; i--)
        {
            for (int j = b.Length - 1; j >= 0; j--)
            {
                table[i, j] = a[i] == b[j] ? table[i + 1, j + 1] + 1 : Math.Max(table[i + 1, j], table[i, j + 1]);
            }
        }

        var lines = new List<DiffLine>();
        int x = 0, y = 0;
        while (x < a.Length && y < b.Length)
        {
            if (a[x] == b[y])
            {
                lines.Add(new DiffLine(' ', a[x]));
                x++;
                y++;
            }
            else if (table[x + 1, y] >= table[x, y + 1])
            {
                lines.Add(new DiffLine('-', a[x]));
                x++;
            }
            else
            {
                lines.Add(new DiffLine('+', b[y]));
                y++;
            }
        }

        while (x < a.Length)
        {
            lines.Add(new DiffLine('-', a[x++]));
        }

        while (y < b.Length)
        {
            lines.Add(new DiffLine('+', b[y++]));
        }

        return lines;
    }

    /// <summary>The comparison as text with one mark per line.</summary>
    public static string Render(IReadOnlyList<DiffLine> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);
        return string.Join("\n", lines.Select(l => l.Mark + " " + l.Text));
    }
}
