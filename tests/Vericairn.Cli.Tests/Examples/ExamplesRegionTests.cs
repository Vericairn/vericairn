// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Vericairn.Cli.Tests;

public sealed class ExamplesRegionTests
{
    private static readonly Regex Fence = new(@"^[ \t]*(?:>[ \t]*)*(`{3,}|~{3,})(.*)$");

    [Fact]
    public void J163_RegisteredGuideRegionsMatchTheirJourney()
    {
        string root = RepositoryRoot();
        string examples = Path.Combine(root, "tests", "examples");
        using JsonDocument manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(examples, "examples.json")));
        foreach (JsonElement entry in manifest.RootElement.GetProperty("examples").EnumerateArray())
        {
            if (entry.GetProperty("guideRegion").ValueKind == JsonValueKind.Null) continue;
            string key = entry.GetProperty("key").GetString()!;
            string path = entry.GetProperty("guideRegion").GetString()!;
            Assert.StartsWith("docs/", path, StringComparison.Ordinal);
            ExampleJourney journey = ExampleJourney.Read(Path.Combine(examples, entry.GetProperty("journey").GetString()!));
            string guide = File.ReadAllText(Path.Combine(root, path));
            string generated = ExampleJourney.Guide(journey);
            CheckRegion(key, guide, generated);
            if (entry.GetProperty("guidedSteps").ValueKind == JsonValueKind.String)
                Assert.Equal("sha256:" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(generated))),
                    entry.GetProperty("guidedSteps").GetString());
        }
    }

    [Fact]
    public void J163_AnEditedRegionAndAChangedJourneyBothFail()
    {
        string sample = Path.Combine(RepositoryRoot(), "tests", "examples", "sample.json");
        ExampleJourney journey = ExampleJourney.Read(sample);
        string content = ExampleJourney.Guide(journey);
        Assert.DoesNotContain("estate init", content, StringComparison.Ordinal);
        string guide = "<!-- example:E1 -->\n" + content + "<!-- /example:E1 -->\n";
        CheckRegion("E1", guide, content);
        Assert.Throws<InvalidDataException>(() => CheckRegion("E1", guide.Replace("vericairn status", "vericairn version", StringComparison.Ordinal), content));
        var changed = journey with { Steps = [.. journey.Steps[..^1], journey.Steps[^1] with { Arguments = ["doctor"] }] };
        Assert.Throws<InvalidDataException>(() => CheckRegion("E1", guide, ExampleJourney.Guide(changed)));
    }

    [Fact]
    public void J163_TheWorkedSectionCannotAddAnUntestedCommandBlock()
    {
        const string block = "```sh\nvericairn status\n```\n";
        string guide = "## Previous section\n" + block + "## Worked section\n<!-- example:E1 -->\n" + block + "<!-- /example:E1 -->\n";
        CheckRegion("E1", guide + "## Next section\n" + block, block);
        Assert.Throws<InvalidDataException>(() => CheckRegion("E1", guide + "### Continued\n" + block, block));
        string additional = "```sh\n# An ordinary shell comment\nvericairn doctor\n```\n";
        Assert.Throws<InvalidDataException>(() => CheckRegion("E1", guide.Replace("<!-- example:E1 -->", additional + "<!-- example:E1 -->", StringComparison.Ordinal), block));
    }

    private static void CheckRegion(string key, string guide, string expected)
    {
        guide = guide.ReplaceLineEndings("\n");
        string opening = $"<!-- example:{key} -->\n";
        string closing = $"<!-- /example:{key} -->\n";
        int first = guide.IndexOf(opening, StringComparison.Ordinal);
        int last = guide.IndexOf(closing, StringComparison.Ordinal);
        if (first < 0 || last < first + opening.Length || first != guide.LastIndexOf(opening, StringComparison.Ordinal)
            || last != guide.LastIndexOf(closing, StringComparison.Ordinal) || guide[(first + opening.Length)..last] != expected)
            throw new InvalidDataException($"The guide region for {key} differs from the journey. Regenerate the region with the journey change.");

        int afterRegion = last + closing.Length;
        int level = 0;
        bool extra = false;
        string? openFence = null;
        foreach (Match line in Regex.Matches(guide, "^.*$", RegexOptions.Multiline))
        {
            Match fence = Fence.Match(line.Value);
            if (fence.Success)
            {
                string delimiter = fence.Groups[1].Value;
                if (openFence is null)
                {
                    openFence = delimiter;
                    extra |= line.Index < first || line.Index >= afterRegion;
                }
                else if (delimiter[0] == openFence[0] && delimiter.Length >= openFence.Length && string.IsNullOrWhiteSpace(fence.Groups[2].Value))
                    openFence = null;
            }
            if (openFence is not null) continue;
            Match heading = Regex.Match(line.Value, @"^ {0,3}(#{1,6})[ \t]+\S");
            if (!heading.Success) continue;
            int depth = heading.Groups[1].Length;
            if (line.Index >= afterRegion && depth <= level) break;
            if (line.Index >= first) continue;
            level = depth;
            extra = false;
        }
        if (extra)
            throw new InvalidDataException($"The worked guide section for {key} has a fenced block outside its checked region.");
    }

    private static string RepositoryRoot()
    {
        for (DirectoryInfo? parent = new(AppContext.BaseDirectory); parent is not null; parent = parent.Parent)
            if (File.Exists(Path.Combine(parent.FullName, "Vericairn.slnx"))) return parent.FullName;
        throw new DirectoryNotFoundException("The guide check needs a product checkout.");
    }
}
