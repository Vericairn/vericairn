// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using System.Text.Json.Nodes;
using Vericairn.Records.Authoring;
using Vericairn.Records.Canonical;
using Vericairn.Records.Estate;
using Vericairn.Records.Model;

namespace Vericairn.Records.Tests;

public class EstateManifestLineTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void ExistingManifestsDefaultWithoutChangingTheirBytes(long format)
    {
        using var estate = new TestEstate(format);
        string path = Path.Combine(estate.Root, EstateLayout.ManifestPath);
        byte[] before = File.ReadAllBytes(path);
        var findings = new FindingList();
        EstateManifest manifest = Assert.IsType<EstateManifest>(EstateManifest.Load(estate.Root, findings));
        Assert.Empty(findings);
        Assert.Equal("current", manifest.Line);
        Assert.Equal("stable", manifest.Channel);
        Assert.Equal(before, File.ReadAllBytes(path));
        using JsonDocument original = JsonDocument.Parse(before);
        Assert.False(original.RootElement.GetProperty("vericairn").TryGetProperty("line", out _));
        Assert.False(original.RootElement.GetProperty("vericairn").TryGetProperty("channel", out _));
    }

    [Theory]
    [InlineData("current", "stable")]
    [InlineData("1.x", "lts")]
    [InlineData("1.x", "prerelease")]
    [InlineData("daily", "nightly")]
    public void InitializationPreparesTheSelectedLineAndChannelWithoutWriting(string line, string channel)
    {
        string root = Path.Combine(Path.GetTempPath(), "estate-line-init-" + Guid.NewGuid().ToString("N"));
        var initialization = new EstateInitialization("Example", "example", "Exercise lines.", "Operator", "Example", null,
            "Example/example", new string('a', 40)) { Line = line, Channel = channel };
        PreparedEstate prepared = EstateInitializer.Prepare(root, initialization);
        var findings = new FindingList();
        EstateManifest manifest = Assert.IsType<EstateManifest>(EstateManifest.Parse(prepared.ManifestBytes, findings));
        Assert.Empty(findings);
        Assert.Equal(line, manifest.Line);
        Assert.Equal(channel, manifest.Channel);
        Assert.False(Directory.Exists(root));
    }

    [Theory]
    [InlineData("line", "../other")]
    [InlineData("line", "Uppercase")]
    [InlineData("line", "")]
    [InlineData("line", "abcdefghijklmnopqrstuvwxyz0123456789-too-long")]
    [InlineData("channel", "preview")]
    [InlineData("channel", "Stable")]
    public void MalformedFollowingFieldsAreRefused(string key, string value)
    {
        using var estate = new TestEstate();
        JsonObject manifest = JsonNode.Parse(File.ReadAllText(Path.Combine(estate.Root, EstateLayout.ManifestPath)))!.AsObject();
        manifest["vericairn"]![key] = value;
        var findings = new FindingList();
        Assert.Null(EstateManifest.Parse(CanonicalJson.SerializePrettyUtf8(JsonSerializer.SerializeToElement(manifest)), findings));
        Assert.True(findings.HasErrors);
        Assert.Contains(findings, f => f.Location.EndsWith("." + key, StringComparison.Ordinal));
    }
}
