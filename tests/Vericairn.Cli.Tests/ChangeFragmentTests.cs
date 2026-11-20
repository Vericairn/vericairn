// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using System.Text.RegularExpressions;

namespace Vericairn.Cli.Tests;

/// <summary>
/// From 0.9.4 (OPS-17; the estate's design/measured-work.md § 12): a change hands its text for a held guide to its release's
/// documentation-set change as a fragment at <c>docs/changes/&lt;version&gt;/&lt;KEY&gt;.md</c>, laid out as docs/changes/README.md
/// says, and a version whose documentation set is frozen has no fragment left. The layout is checked over the repository's own
/// fragments and over constructed ones, so that the check is exercised whether or not a fragment is waiting.
/// </summary>
public sealed partial class ChangeFragmentTests
{
    /// <summary>The held guides other than the compatibility notes, in the order a fragment names them.</summary>
    internal static readonly string[] HeldGuides = ["docs/operator-guide.md", "docs/limitations-and-platforms.md", "docs/api-v1.md"];

    [Fact]
    public void TheRepositorysFragmentsFollowTheLayoutAndNoneOutlivesItsFrozenSet()
    {
        string root = RepositoryRoot();
        Assert.True(File.Exists(Path.Combine(root, "docs", "changes", "README.md")), "docs/changes/README.md describes the fragment layout.");
        IReadOnlyList<string> problems = Problems(root);
        Assert.True(problems.Count == 0, string.Join("\n", problems));
    }

    [Fact]
    public void TheLayoutGuideNamesEveryHeldGuideAndTheCompatibilityNotes()
    {
        string guide = File.ReadAllText(Path.Combine(RepositoryRoot(), "docs", "changes", "README.md"));
        foreach (string held in HeldGuides) Assert.Contains($"`{held}`", guide, StringComparison.Ordinal);
        Assert.Contains("`docs/api-compatibility-<version>.md`", guide, StringComparison.Ordinal);
        Assert.Contains("`docs/changes/<version>/<KEY>.md`", guide, StringComparison.Ordinal);
    }

    [Fact]
    public void AWellFormedFragmentPassesAndEachDepartureFromTheLayoutIsNamed()
    {
        using var repository = new Scratch();
        repository.Write("docs/changes/README.md", "# Per-change fragments\n");
        repository.Write("docs/releases/frozen-sha256.json", """{"format":"vericairn.frozen-docs/1","sets":{"0.9.3":{"README.md":"sha256:00"}}}""");
        repository.Write("docs/changes/0.9.4/POLICY-08.md", Fragment("POLICY-08", "0.9.4"));
        Assert.Empty(Problems(repository.Root));

        repository.Write("docs/changes/0.9.4/lower-case.md", Fragment("lower-case", "0.9.4"));
        repository.Write("docs/changes/0.9.4/FIX-1.txt", "text");
        repository.Write("docs/changes/0.9.4/OPS-17.md", "# OPS-18: Another key\n\n## docs/cli.md\n\n### In \"x\"\n\n> Text.\n");
        repository.Write("docs/changes/0.9.4/FIX-2.md", "# FIX-2: Out of order\n\n## docs/api-v1.md\n\n### In \"x\"\n\n> Text.\n\n## docs/operator-guide.md\n\n### In \"y\"\n\n> Text.\n\n## docs/api-compatibility-0.9.3.md\n\n### In \"z\"\n\n> Text.\n");
        repository.Write("docs/changes/0.9.4/FIX-3.md", "# FIX-3: A machine path\n\n## docs/operator-guide.md\n\n### In \"x\"\n\n> Read /" + "Users/someone/clone first.\n");
        repository.Write("docs/changes/0.9.4/FIX-4.md", "# FIX-4: No placement\n\n## docs/operator-guide.md\n\n> Text.\n");
        repository.Write("docs/changes/0.9.3/FIX-5.md", Fragment("FIX-5", "0.9.3"));
        repository.Write("docs/changes/next/FIX-6.md", Fragment("FIX-6", "0.9.4"));
        repository.Write("docs/changes/stray.md", "# Stray\n");
        IReadOnlyList<string> problems = Problems(repository.Root);
        Assert.Contains("docs/changes/0.9.4/lower-case.md: a fragment is named for its change's work-order key, such as POLICY-08.md.", problems);
        Assert.Contains("docs/changes/0.9.4/FIX-1.txt: a fragment is named for its change's work-order key, such as POLICY-08.md.", problems);
        Assert.Contains("docs/changes/0.9.4/OPS-17.md: the first line is the title, \"# OPS-17: \" and a short description.", problems);
        Assert.Contains("docs/changes/0.9.4/OPS-17.md: \"## docs/cli.md\" names no held guide; a section names docs/operator-guide.md, docs/limitations-and-platforms.md, docs/api-v1.md or docs/api-compatibility-0.9.4.md.", problems);
        Assert.Contains("docs/changes/0.9.4/FIX-2.md: the held guides are named each at most once, in the layout's order.", problems);
        Assert.Contains("docs/changes/0.9.4/FIX-2.md: \"## docs/api-compatibility-0.9.3.md\" names no held guide; a section names docs/operator-guide.md, docs/limitations-and-platforms.md, docs/api-v1.md or docs/api-compatibility-0.9.4.md.", problems);
        Assert.Contains("docs/changes/0.9.4/FIX-3.md: a fragment names no machine path.", problems);
        Assert.Contains("docs/changes/0.9.4/FIX-4.md: each held guide's section places its text under a level-three heading saying where it goes.", problems);
        Assert.Contains("docs/changes/0.9.3: the documentation set of 0.9.3 is frozen, so its fragments were applied and removed with it; FIX-5.md remains.", problems);
        Assert.Contains("docs/changes/next: a directory under docs/changes is named for the release in preparation, such as 0.9.4.", problems);
        Assert.Contains("docs/changes/stray.md: only README.md lives directly under docs/changes.", problems);
        Assert.DoesNotContain(problems, p => p.StartsWith("docs/changes/0.9.4/POLICY-08.md", StringComparison.Ordinal));
    }

    /// <summary>Every departure of a repository's <c>docs/changes</c> from the fragment layout, in a stable order; empty when it follows it.</summary>
    internal static IReadOnlyList<string> Problems(string root)
    {
        var problems = new List<string>();
        string changes = Path.Combine(root, "docs", "changes");
        if (!Directory.Exists(changes)) return problems;
        HashSet<string> frozen = Frozen(root);
        foreach (string entry in Directory.EnumerateFileSystemEntries(changes).Order(StringComparer.Ordinal))
        {
            string name = Path.GetFileName(entry);
            if (File.Exists(entry))
            {
                if (name != "README.md") problems.Add($"docs/changes/{name}: only README.md lives directly under docs/changes.");
                continue;
            }

            if (!Version().IsMatch(name))
            {
                problems.Add($"docs/changes/{name}: a directory under docs/changes is named for the release in preparation, such as 0.9.4.");
                continue;
            }

            List<string> fragments = [.. Directory.EnumerateFileSystemEntries(entry).Select(Path.GetFileName).OfType<string>().Order(StringComparer.Ordinal)];
            if (frozen.Contains(name) && fragments.Count > 0)
            {
                problems.Add($"docs/changes/{name}: the documentation set of {name} is frozen, so its fragments were applied and removed with it; {string.Join(", ", fragments)} remains.");
            }

            foreach (string fragment in fragments)
            {
                string path = $"docs/changes/{name}/{fragment}";
                string full = Path.Combine(entry, fragment);
                if (!File.Exists(full) || !fragment.EndsWith(".md", StringComparison.Ordinal) || !Key().IsMatch(fragment[..^3]))
                {
                    problems.Add($"{path}: a fragment is named for its change's work-order key, such as POLICY-08.md.");
                    continue;
                }

                problems.AddRange(Layout(File.ReadAllText(full).ReplaceLineEndings("\n"), fragment[..^3], name).Select(p => $"{path}: {p}"));
            }
        }

        return problems;
    }

    // What one fragment's text departs from: its title, its sections, their order and placements, and machine paths.
    private static IEnumerable<string> Layout(string text, string key, string version)
    {
        string[] lines = text.Split('\n');
        if (!lines[0].StartsWith($"# {key}: ", StringComparison.Ordinal) || lines[0].Length <= key.Length + 4) yield return $"the first line is the title, \"# {key}: \" and a short description.";
        string[] held = [.. HeldGuides, $"docs/api-compatibility-{version}.md"];
        var named = new List<int>();
        string? section = null;
        bool placed = true;
        foreach (string line in lines.Skip(1))
        {
            if (line.StartsWith("## ", StringComparison.Ordinal))
            {
                if (section is not null && !placed) yield return "each held guide's section places its text under a level-three heading saying where it goes.";
                section = line[3..].Trim();
                placed = false;
                int index = Array.IndexOf(held, section);
                if (index < 0) yield return $"\"{line}\" names no held guide; a section names {string.Join(", ", held[..^1])} or {held[^1]}.";
                else named.Add(index);
            }
            else if (line.StartsWith("### ", StringComparison.Ordinal))
            {
                placed = true;
            }
        }

        if (section is not null && !placed) yield return "each held guide's section places its text under a level-three heading saying where it goes.";
        if (named.Count == 0) yield return "a fragment names at least one held guide in a level-two heading.";
        for (int i = 1; i < named.Count; i++)
        {
            if (named[i] <= named[i - 1])
            {
                yield return "the held guides are named each at most once, in the layout's order.";
                break;
            }
        }

        if (MachinePath().IsMatch(text)) yield return "a fragment names no machine path.";
    }

    private static HashSet<string> Frozen(string root)
    {
        string index = Path.Combine(root, "docs", "releases", "frozen-sha256.json");
        if (!File.Exists(index)) return [];
        using JsonDocument document = JsonDocument.Parse(File.ReadAllBytes(index));
        return document.RootElement.TryGetProperty("sets", out JsonElement sets) ? [.. sets.EnumerateObject().Select(s => s.Name)] : [];
    }

    private static string Fragment(string key, string version) =>
        $"# {key}: A change\n\n## docs/operator-guide.md\n\n### In \"5. The work loop\", after the brief example\n\n> From {version}, a change.\n\n## docs/api-compatibility-{version}.md\n\n### Under the {version} changes\n\n> No public API change.\n";

    private static string RepositoryRoot()
    {
        string? root = AppContext.BaseDirectory;
        while (root is not null && !File.Exists(Path.Combine(root, "Vericairn.slnx")))
        {
            root = Path.GetDirectoryName(root);
        }

        Assert.NotNull(root);
        return root;
    }

    [GeneratedRegex(@"\A(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\z")]
    private static partial Regex Version();

    [GeneratedRegex(@"\A[A-Z][A-Z0-9]*(?:-[A-Z0-9]+)*-[0-9]+\z")]
    private static partial Regex Key();

    // A machine path, split so that the pattern itself is not one: a home or volume root, a temporary root or a drive letter.
    [GeneratedRegex(@"(?:/" + "Users/|/" + "Volumes/|/" + "home/|/private/" + "tmp|[A-Za-z]:\\\\)")]
    private static partial Regex MachinePath();

    private sealed class Scratch : IDisposable
    {
        public Scratch()
        {
            Root = Path.Combine(Path.GetTempPath(), "vericairn-fragments-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Root);
        }

        public string Root { get; }

        public void Write(string path, string text)
        {
            string full = Path.Combine(Root, path.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            File.WriteAllText(full, text);
        }

        public void Dispose()
        {
            try { Directory.Delete(Root, recursive: true); }
            catch (IOException) { }
        }
    }
}
