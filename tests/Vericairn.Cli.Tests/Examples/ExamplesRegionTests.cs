// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Vericairn.Cli.Tests;

public sealed class ExamplesRegionTests
{
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
    }

    private static string RepositoryRoot()
    {
        for (DirectoryInfo? parent = new(AppContext.BaseDirectory); parent is not null; parent = parent.Parent)
            if (File.Exists(Path.Combine(parent.FullName, "Vericairn.slnx"))) return parent.FullName;
        throw new DirectoryNotFoundException("The guide check needs a product checkout.");
    }
}
