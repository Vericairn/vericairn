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
        repository.Write("docs/changes/0.9.4/FIX-7.md", "# FIX-7: Unquoted\n\n## docs/operator-guide.md\n\n### In \"x\"\n\nPlain text, not quoted.\n");
        repository.Write("docs/changes/0.9.4/FIX-8.md", "# FIX-8: A preamble\n\nSome text before any section.\n\n## docs/operator-guide.md\n\n### In \"x\"\n\n> Text.\n");
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
        Assert.Contains("docs/changes/0.9.4/FIX-7.md: a placement's text is a block quote, every line of it beginning with >.", problems);
        Assert.Contains("docs/changes/0.9.4/FIX-8.md: nothing but blank lines comes between the title and the first held guide's section.", problems);
        Assert.Contains("docs/changes/0.9.3: the documentation set of 0.9.3 is frozen, so its fragments were applied and removed with it; FIX-5.md remains.", problems);
        Assert.Contains("docs/changes/next: a directory under docs/changes is named for the release in preparation, such as 0.9.4.", problems);
        Assert.Contains("docs/changes/stray.md: only README.md lives directly under docs/changes.", problems);
        Assert.DoesNotContain(problems, p => p.StartsWith("docs/changes/0.9.4/POLICY-08.md", StringComparison.Ordinal));
    }

    // Each form a fragment may quote: a home-relative path that names no user, as the held guides name a user's own directories, a
    // path below a variable, a root named alone, a relative path and a web address. The roots are split so that no literal is one.
    [Theory]
    [InlineData("> A run is refused while the user's own `~/.agents` supplies skills.")]
    [InlineData("> A launchd agent (`~/Library/LaunchAgents`) or a systemd user unit (`~/.config/systemd/user`).")]
    [InlineData(@"> On Windows, below `~\AppData\Local`, `%USERPROFILE%\AppData` or `$env:USERPROFILE\AppData`.")]
    [InlineData("> In `$HOME/.config/vericairn`, `${HOME}/.config` or *~/.config*, never [the home](~/).")]
    [InlineData("> In `$TMPDIR/vericairn`, else `$XDG_RUNTIME_DIR/vericairn`, or `~/" + "tmp/x`.")]
    [InlineData("> The system `/" + "tmp` is permitted, as is `/" + "tmp/`, and files in /" + "tmp/.")]
    [InlineData("> Homes live below `/" + "Users` and `/" + "home/`, volumes below **/" + "Volumes/** and globs such as `/" + "Users/*/Library` name no one.")]
    [InlineData("> See docs/" + "home/guide.md, src/" + "tmp/x, ./" + "tmp/x and ../" + "Users/x.")]
    [InlineData("> See https://example.invalid/" + "home/x, a bare `file://` scheme and file:///etc/hosts.")]
    [InlineData("> Strike ~~this~~ and ~that~; it took ~10/s.")]
    public void AHomeRelativePathThatNamesNoUserAndARootNamedAlonePass(string quoted) => Assert.Empty(Quoting(quoted));

    // Each form that names one machine: a user's home on macOS, Linux and Windows, a volume, a temporary folder and a host, in plain text,
    // code, emphasis, links, file URLs, a table and after any mark a path can follow.
    [Theory]
    [InlineData("> Read /" + "Users/alice/clone first.")]
    [InlineData("> Read `/" + "Users/alice` first.")]
    [InlineData("> Read */" + "Users/alice* first.")]
    [InlineData("> Read [the clone](/" + "Users/alice/clone).")]
    [InlineData("> Read file:///" + "Users/alice/clone first.")]
    [InlineData(">/" + "Users/alice/clone")]
    [InlineData("> See /" + "home/alice/.config here.")]
    [InlineData("> See `/" + "home/<user>/.config` here.")]
    [InlineData("> See /" + "root/.config here.")]
    [InlineData("> See ~alice/clone or ~alice\\clone here.")]
    [InlineData("> See C:" + @"\Users\alice here.")]
    [InlineData("> See C:/" + "Users/alice here.")]
    [InlineData("> See file:///C:/" + "Users/alice here.")]
    [InlineData("> See /" + "Volumes/Data/estate here.")]
    [InlineData("> See _/" + "Volumes/Data_ here.")]
    [InlineData("> See **/" + "Volumes/Data** here.")]
    [InlineData("> See /" + "mnt/c/estates here.")]
    [InlineData("> See /" + "media/alice/usb here.")]
    [InlineData(@"> See D:\estates here.")]
    [InlineData(@"> See \\server\share\estate or `\\.\pipe\vericairn` here.")]
    [InlineData("> See file://server/share/estate here.")]
    [InlineData("> See file://localhost/" + "Users/alice here.")]
    [InlineData("> See /" + "tmp/scratch here.")]
    [InlineData("> See /" + "tmp/_build here.")]
    [InlineData("> See /" + "private/" + "tmp/scratch here.")]
    [InlineData("> See /" + "var/" + "tmp/scratch here.")]
    [InlineData("> See /" + "var/folders/ab/cd/T/x here.")]
    [InlineData("> See /" + "private/var/folders/ab/cd/T/x here.")]
    [InlineData("> After a colon:/" + "tmp/x, a comma,/" + "tmp/y or a quote \"/" + "tmp/z\".")]
    [InlineData("> | /" + "tmp/x | a cell |")]
    [InlineData("> |/" + "tmp/x|")]
    [InlineData("> In (/" + "tmp/x) or with --dir=/" + "tmp/x.")]
    public void APathNamingOneMachinesHomeVolumeOrTemporaryFolderIsRefusedWhereverItBegins(string quoted) =>
        Assert.Equal(["docs/changes/0.9.4/OPS-17.md: a fragment names no machine path."], Quoting(quoted));

    // The problems of a fragment that follows the layout, its one placement quoting the given line.
    private static IReadOnlyList<string> Quoting(string quoted)
    {
        using var repository = new Scratch();
        repository.Write("docs/changes/README.md", "# Per-change fragments\n");
        repository.Write("docs/changes/0.9.4/OPS-17.md", $"# OPS-17: A quoted path\n\n## docs/operator-guide.md\n\n### In \"x\"\n\n{quoted}\n");
        return Problems(repository.Root);
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

    // What one fragment's text departs from: its title, what precedes the first section, its sections, their order, placements whose
    // text is a block quote, and machine paths. Each departure is named once.
    private static IEnumerable<string> Layout(string text, string key, string version)
    {
        const string Unplaced = "each held guide's section places its text under a level-three heading saying where it goes.";
        const string Quoted = "a placement's text is a block quote, every line of it beginning with >.";
        var problems = new List<string>();
        string[] lines = text.Split('\n');
        if (!lines[0].StartsWith($"# {key}: ", StringComparison.Ordinal) || lines[0].Length <= key.Length + 4) problems.Add($"the first line is the title, \"# {key}: \" and a short description.");
        string[] held = [.. HeldGuides, $"docs/api-compatibility-{version}.md"];
        var named = new List<int>();
        string? section = null;
        bool placement = false, quoted = false;
        foreach (string line in lines.Skip(1))
        {
            if (line.StartsWith("## ", StringComparison.Ordinal))
            {
                if (section is not null && !placement) problems.Add(Unplaced);
                if (placement && !quoted) problems.Add(Quoted);
                section = line[3..].Trim();
                placement = quoted = false;
                int index = Array.IndexOf(held, section);
                if (index < 0) problems.Add($"\"{line}\" names no held guide; a section names {string.Join(", ", held[..^1])} or {held[^1]}.");
                else named.Add(index);
            }
            else if (line.StartsWith("### ", StringComparison.Ordinal))
            {
                if (section is null) problems.Add("nothing but blank lines comes between the title and the first held guide's section.");
                if (placement && !quoted) problems.Add(Quoted);
                placement = true;
                quoted = false;
            }
            else if (line.Trim().Length > 0)
            {
                if (section is null) problems.Add("nothing but blank lines comes between the title and the first held guide's section.");
                else if (!placement) problems.Add(Unplaced);
                else if (line.StartsWith('>')) quoted = true;
                else problems.Add(Quoted);
            }
        }

        if (section is not null && !placement) problems.Add(Unplaced);
        if (placement && !quoted) problems.Add(Quoted);
        if (named.Count == 0) problems.Add("a fragment names at least one held guide in a level-two heading.");
        for (int i = 1; i < named.Count; i++)
        {
            if (named[i] <= named[i - 1])
            {
                problems.Add("the held guides are named each at most once, in the layout's order.");
                break;
            }
        }

        if (MachinePath().IsMatch(text)) problems.Add("a fragment names no machine path.");
        return problems.Distinct(StringComparer.Ordinal);
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

    // A machine path, as docs/changes/README.md states it: a name below a user's home root, a volume root or a temporary root (each
    // also below /private), a tilde followed by a user's name, a drive letter, a network share, or a file URL naming a host. It begins
    // where a path begins: at the start of a line or after any character but a letter, a digit, '.', '-', '~' or '}', so that a
    // relative path and one below a variable are not one. A name follows the root's slash, past any leading dots or underscores, so that
    // a root named alone, before white space, a closing mark or punctuation, is not one; neither is a home-relative path naming no user.
    [GeneratedRegex(@"(?<![A-Za-z0-9.~}-])(?:(?:(?:/private)?/(?:tmp|var/tmp|var/folders)|/(?:Users|home|root|Volumes|mnt|media))/[._]*[^\s/\\`'""()\[\]>*_|,;:.!?]" +
        @"|~[A-Za-z_][A-Za-z0-9_.-]*[/\\]|[A-Za-z]:[\\/]|\\\\[A-Za-z0-9.?$_-]+\\[A-Za-z0-9.$_-]|(?i:file)://[^\s/\\`'""()\[\]>*|,;!?])")]
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
