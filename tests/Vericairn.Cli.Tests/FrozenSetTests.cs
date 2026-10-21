// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Security.Cryptography;
using System.Text.Json;

namespace Vericairn.Cli.Tests;

/// <summary>Published and withdrawn documentation sets retain every frozen byte; a future set remains editable until frozen.</summary>
[Trait("Journey", "D19-G")]
public class FrozenSetTests
{
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
        foreach (string version in new[] { "0.2.0", "0.3.0", "0.3.1", "0.4.0", "0.4.1", "0.4.2", "0.5.0", "0.5.1", "0.5.2", "0.6.0", "0.6.1", "0.7.0", "0.7.1", "0.7.2", "0.7.3", "0.8.0" })
        {
            Assert.True(sets.TryGetProperty(version, out _), $"Frozen set {version} must stay indexed, even if withdrawn.");
        }

        Assert.Empty(Differences(releases, sets));
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

    private static string Hash(string path)
    {
        using FileStream content = File.OpenRead(path);
        return "sha256:" + Convert.ToHexStringLower(SHA256.HashData(content));
    }
}
