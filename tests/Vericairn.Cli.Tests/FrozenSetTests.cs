// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Vericairn.Cli.Tests;

/// <summary>Published and withdrawn documentation sets retain every frozen byte; a future set remains editable until frozen.</summary>
[Trait("Journey", "D19-G")]
public partial class FrozenSetTests
{
    // The sets frozen before the release index linked each set's overview. Every later set is held in the index by its link there and
    // by its directory, so freezing a new set never edits this list; removing a set's entry needs its link removed as well.
    private static readonly string[] Unlinked = ["0.2.0", "0.3.0", "0.3.1", "0.4.0", "0.4.1", "0.4.2", "0.5.0", "0.5.1", "0.5.2"];

    [Fact]
    public void EveryFrozenSetMatchesItsChecksumIndex()
    {
        string? root = AppContext.BaseDirectory;
        while (root is not null && !File.Exists(Path.Combine(root, "Vericairn.slnx")))
        {
            root = Path.GetDirectoryName(root);
        }

        Assert.NotNull(root);
        string releases = Path.Combine(root, "docs", "releases");
        using JsonDocument index = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(releases, "frozen-sha256.json")));
        Assert.Equal("vericairn.frozen-docs/1", index.RootElement.GetProperty("format").GetString());
        JsonElement sets = index.RootElement.GetProperty("sets");
        // A frozen set stays indexed, even if withdrawn.
        Assert.Empty(Unindexed(releases, sets, BuildVersion(root), Unlinked));
        Assert.Empty(Differences(releases, sets));
    }

    [Fact]
    public void AFrozenSetCannotLeaveTheIndexWhileItsLinkOrDirectoryRemains()
    {
        string root = CliHarness.TempDirectory("frozen-witnesses");
        try
        {
            foreach (string version in new[] { "0.1.0", "0.2.0", "0.3.0" }) Directory.CreateDirectory(Path.Combine(root, version));
            File.WriteAllText(Path.Combine(root, "README.md"), "# Sets\n\n## Documentation sets\n\nThe [0.2.0 set](0.2.0/README.md) is frozen.\n");
            JsonElement Index(params string[] versions) =>
                JsonDocument.Parse(JsonSerializer.Serialize(versions.ToDictionary(v => v, _ => new Dictionary<string, string>()))).RootElement;

            // 0.3.0 is the build's own set, editable until frozen; 0.1.0 predates the index's links and is held by name.
            Assert.Empty(Unindexed(root, Index("0.1.0", "0.2.0"), "0.3.0", ["0.1.0"]));
            Assert.Equal(["0.3.0: not indexed"], Unindexed(root, Index("0.1.0", "0.2.0"), "0.4.0", ["0.1.0"]));

            // An entry removed while the set's directory or its link in the release index remains is noticed.
            Assert.Equal(["0.2.0: not indexed"], Unindexed(root, Index("0.1.0"), "0.3.0", ["0.1.0"]));
            Directory.Delete(Path.Combine(root, "0.2.0"));
            Assert.Equal(["0.2.0: not indexed"], Unindexed(root, Index("0.1.0"), "0.3.0", ["0.1.0"]));
            Directory.Delete(Path.Combine(root, "0.1.0"));
            Assert.Equal(["0.1.0: not indexed"], Unindexed(root, Index("0.2.0"), "0.3.0", ["0.1.0"]));

            // The release index describes only frozen sets, so a linked set has no exception, even the build's own.
            File.AppendAllText(Path.Combine(root, "README.md"), "\nThe [0.3.0 set](0.3.0/README.md) is prepared.\n");
            Assert.Equal(["0.3.0: not indexed"], Unindexed(root, Index("0.1.0", "0.2.0"), "0.3.0", ["0.1.0"]));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void TheGuardDetectsEditsAdditionsAndRemovalsWhileAnUnreleasedSetStaysEditable()
    {
        string root = CliHarness.TempDirectory("frozen-sets");
        try
        {
            string frozen = Path.Combine(root, "0.1.0");
            string current = Path.Combine(root, "0.2.0");
            Directory.CreateDirectory(frozen);
            Directory.CreateDirectory(current);
            string document = Path.Combine(frozen, "notes.md");
            File.WriteAllText(document, "Frozen.\n");
            using JsonDocument index = JsonDocument.Parse(JsonSerializer.Serialize(new Dictionary<string, Dictionary<string, string>>
            {
                ["0.1.0"] = new() { ["notes.md"] = Hash(document) },
            }));

            Assert.Empty(Differences(root, index.RootElement));
            File.WriteAllText(Path.Combine(current, "notes.md"), "Draft.\n");
            File.WriteAllText(Path.Combine(current, "notes.md"), "Revised draft.\n");
            Assert.Empty(Differences(root, index.RootElement));

            File.WriteAllText(document, "Edited.\n");
            Assert.Equal(["0.1.0/notes.md: changed"], Differences(root, index.RootElement));
            File.WriteAllText(document, "Frozen.\n");
            string added = Path.Combine(frozen, "extra.md");
            File.WriteAllText(added, "Added.\n");
            Assert.Equal(["0.1.0/extra.md: added"], Differences(root, index.RootElement));
            File.Delete(added);
            File.Delete(document);
            Assert.Equal(["0.1.0/notes.md: missing"], Differences(root, index.RootElement));
            Directory.Delete(frozen);
            Assert.Equal(["0.1.0/notes.md: missing"], Differences(root, index.RootElement));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static List<string> Differences(string root, JsonElement sets)
    {
        var differences = new List<string>();
        foreach (JsonProperty set in sets.EnumerateObject())
        {
            string directory = Path.Combine(root, set.Name);
            Dictionary<string, string?> expected = set.Value.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetString(), StringComparer.Ordinal);
            foreach ((string relative, string? hash) in expected)
            {
                string path = Path.Combine(directory, relative);
                if (!File.Exists(path))
                {
                    differences.Add($"{set.Name}/{relative}: missing");
                }
                else if (Hash(path) != hash)
                {
                    differences.Add($"{set.Name}/{relative}: changed");
                }
            }

            if (Directory.Exists(directory))
            {
                foreach (string path in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories))
                {
                    string relative = Path.GetRelativePath(directory, path).Replace('\\', '/');
                    if (!expected.ContainsKey(relative))
                    {
                        differences.Add($"{set.Name}/{relative}: added");
                    }
                }
            }
        }

        return differences;
    }

    // Every set the release index links, every set directory but the build's own until it is frozen, and the sets named before
    // the index linked them: each must be indexed, so no frozen set leaves the index unless all three are changed together.
    private static List<string> Unindexed(string releases, JsonElement sets, string buildVersion, IEnumerable<string> unlinked)
    {
        var required = new SortedSet<string>(unlinked, StringComparer.Ordinal);
        string readme = Path.Combine(releases, "README.md");
        if (File.Exists(readme)) required.UnionWith(SetLink().Matches(File.ReadAllText(readme)).Select(m => m.Groups["version"].Value));
        required.UnionWith(Directory.EnumerateDirectories(releases).Select(Path.GetFileName).OfType<string>()
            .Where(name => SetVersion().IsMatch(name) && (name != buildVersion || sets.TryGetProperty(name, out _))));
        return [.. required.Where(v => !sets.TryGetProperty(v, out _)).Select(v => v + ": not indexed")];
    }

    private static string BuildVersion(string root)
    {
        Match prefix = VersionPrefix().Match(File.ReadAllText(Path.Combine(root, "Directory.Build.props")));
        Assert.True(prefix.Success, "Directory.Build.props names the version this build documents.");
        return prefix.Groups[1].Value;
    }

    [GeneratedRegex(@"\[(?<version>\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?) set\]\(\k<version>/README\.md\)")]
    private static partial Regex SetLink();

    [GeneratedRegex(@"^\d+\.\d+\.\d+(?:-[0-9A-Za-z][0-9A-Za-z.-]*)?$")]
    private static partial Regex SetVersion();

    [GeneratedRegex(@"<VersionPrefix>([^<]+)</VersionPrefix>")]
    private static partial Regex VersionPrefix();

    private static string Hash(string path)
    {
        using FileStream content = File.OpenRead(path);
        return "sha256:" + Convert.ToHexStringLower(SHA256.HashData(content));
    }
}
