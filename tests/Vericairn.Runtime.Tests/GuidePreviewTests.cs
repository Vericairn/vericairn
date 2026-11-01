// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using Vericairn.Runtime.Guides;

namespace Vericairn.Runtime.Tests;

public sealed class GuidePreviewTests
{
    [Fact]
    public void IndependentLocalPreviewsMatchWithoutChangingTheirSources()
    {
        using var first = new Preview();
        using var second = new Preview();
        Dictionary<string, byte[]> original = first.Inputs();
        GuideBundle a = first.Load();
        GuideBundle b = second.Load();
        Assert.True(a.Success);
        Assert.True(b.Success);
        Assert.Empty(GuidePreview.Write(a, first.Output));
        Assert.Empty(GuidePreview.Write(b, second.Output));
        Assert.Empty(GuidePreview.Check(a, first.Output));
        Assert.Empty(GuidePreview.Check(a, second.Output));
        foreach ((string name, byte[] bytes) in a.Files)
            Assert.Equal(bytes, File.ReadAllBytes(Path.Combine(second.Output, name)));
        Dictionary<string, byte[]> current = first.Inputs();
        Assert.Equal(original.Keys, current.Keys);
        foreach (string name in original.Keys) Assert.Equal(original[name], current[name]);
        Assert.Empty(Directory.EnumerateFileSystemEntries(Path.Combine(first.Root, "home")));
        if (!OperatingSystem.IsWindows())
        {
            const UnixFileMode others = UnixFileMode.GroupRead | UnixFileMode.GroupWrite | UnixFileMode.GroupExecute |
                UnixFileMode.OtherRead | UnixFileMode.OtherWrite | UnixFileMode.OtherExecute;
            Assert.Equal((UnixFileMode)0, File.GetUnixFileMode(first.Output) & others);
            foreach (string path in Directory.EnumerateFiles(first.Output, "*", SearchOption.AllDirectories))
                Assert.Equal((UnixFileMode)0, File.GetUnixFileMode(path) & others);
        }
    }

    [Theory]
    [InlineData("edit")]
    [InlineData("missing")]
    [InlineData("extra")]
    [InlineData("directory")]
    public void ExistingPreviewChangesFailAndAreNeverOverwritten(string fault)
    {
        using var preview = new Preview();
        GuideBundle bundle = preview.Load();
        Assert.Empty(GuidePreview.Write(bundle, preview.Output));
        string guide = Path.Combine(preview.Output, "guides", "provider.workshop00.group.md");
        switch (fault)
        {
            case "edit": File.WriteAllText(guide, "An edited guide."); break;
            case "missing": File.Delete(guide); break;
            case "extra": File.WriteAllText(Path.Combine(preview.Output, "unrelated.txt"), "Unrelated material."); break;
            case "directory": Directory.CreateDirectory(Path.Combine(preview.Output, "unrelated")); break;
        }
        string[] files = Directory.GetFiles(preview.Output, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal).ToArray();
        byte[][] bytes = files.Select(File.ReadAllBytes).ToArray();
        Assert.NotEmpty(GuidePreview.Check(bundle, preview.Output));
        Assert.NotEmpty(GuidePreview.Write(bundle, preview.Output));
        Assert.Equal(files, Directory.GetFiles(preview.Output, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal));
        for (int i = 0; i < files.Length; i++) Assert.Equal(bytes[i], File.ReadAllBytes(files[i]));
    }

    [Fact]
    public void MissingSourceEmitsOnlyFailureManifestsAndNoPartialGuides()
    {
        using var preview = new Preview();
        File.Delete(Path.Combine(preview.Root, "catalog", "catalog", "workshop00.md"));
        GuideBundle bundle = preview.Load();
        Assert.False(bundle.Success);
        Assert.Contains(bundle.Findings, f => f.Entry == "provider.workshop00.group" && f.Code == "missing-row");
        Assert.NotEmpty(GuidePreview.Write(bundle, preview.Output));
        Assert.False(Directory.Exists(Path.Combine(preview.Output, "guides")));
        using JsonDocument coverage = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(preview.Output, "coverage-manifest.json")));
        Assert.False(coverage.RootElement.GetProperty("complete").GetBoolean());
        using JsonDocument sources = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(preview.Output, "source-manifest.json")));
        Assert.False(sources.RootElement.GetProperty("complete").GetBoolean());
        Assert.NotEmpty(GuidePreview.Compare(Path.Combine(preview.Output, "source-manifest.json"), Path.Combine(preview.Output, "source-manifest.json")).Findings);
    }

    [Theory]
    [InlineData("null")]
    [InlineData("{\"sources\":null}")]
    [InlineData("{\"unknown\":true}")]
    [InlineData("{\"format\":\"first\",\"format\":\"second\"}")]
    [InlineData("not JSON")]
    public void MalformedIndexesReturnFindings(string json)
    {
        using var preview = new Preview();
        File.WriteAllText(preview.Index, json);
        GuideBundle bundle = preview.Load();
        Assert.False(bundle.Success);
        Assert.NotEmpty(bundle.Findings);
    }

    [Theory]
    [InlineData("../outside.md")]
    [InlineData("nested/../../outside.md")]
    [InlineData("nested\\outside.md")]
    [InlineData("NUL.md")]
    [InlineData("file.md.")]
    public void NonportableOrEscapingSourcePathsFailBeforeReading(string path)
    {
        using var preview = new Preview();
        GuideFixture fixture = GuideFixture.Create();
        fixture.Index = fixture.Index with { Sources = fixture.Index.Sources.Select(s => s.Id == "family00" ? s with { Path = path } : s).ToArray() };
        File.WriteAllText(preview.Index, JsonSerializer.Serialize(fixture.Index, GuideJson.Options));
        Assert.Contains(preview.Load().Findings, f => f.Code == "invalid-source");
    }

    [Fact]
    public void OutputMustBeSeparateFromAllInputTreesAndTheIndex()
    {
        using var preview = new Preview();
        string catalog = Path.Combine(preview.Root, "catalog");
        string contracts = Path.Combine(preview.Root, "contracts");
        Assert.True(GuidePreview.SeparateOutput(preview.Output, preview.Index, catalog, contracts));
        foreach (string path in new[] { preview.Root, catalog, contracts, Path.Combine(catalog, "preview"), Path.Combine(contracts, "preview") })
            Assert.False(GuidePreview.SeparateOutput(path, preview.Index, catalog, contracts));
    }

    [Fact]
    public void LinkedSourcesAndOutputsAreRefusedWithoutTouchingTheTarget()
    {
        using var preview = new Preview();
        string target = Path.Combine(preview.Root, "catalog", "catalog", "workshop00.md");
        string original = File.ReadAllText(target);
        string link = Path.Combine(preview.Root, "catalog", "linked.md");
        try
        {
            File.CreateSymbolicLink(link, target);
            Directory.CreateSymbolicLink(preview.Output, Path.Combine(preview.Root, "contracts"));
        }
        catch (Exception error) when (OperatingSystem.IsWindows() && error is IOException or UnauthorizedAccessException)
        { Assert.Skip("Creating Windows symbolic links requires Developer Mode or the symlink privilege."); }
        GuideBundle valid = preview.Load();
        Assert.True(valid.Success);
        Assert.NotEmpty(GuidePreview.Write(valid, preview.Output));
        Assert.NotEmpty(GuidePreview.Check(valid, preview.Output));
        GuideFixture fixture = GuideFixture.Create();
        fixture.Index = fixture.Index with { Sources = fixture.Index.Sources.Select(s => s.Id == "family00" ? s with { Path = "linked.md" } : s).ToArray() };
        File.WriteAllText(preview.Index, JsonSerializer.Serialize(fixture.Index, GuideJson.Options));
        Assert.Contains(preview.Load().Findings, f => f.Code == "input-unavailable");
        Assert.Equal(original, File.ReadAllText(target));
        Assert.Single(Directory.GetFiles(Path.Combine(preview.Root, "contracts")));
    }

    private sealed class Preview : IDisposable
    {
        public string Root { get; } = Directory.CreateTempSubdirectory("guide-preview-").FullName;
        public string Index => Path.Combine(Root, "index.json");
        public string Output => Path.Combine(Root, "preview");
        public Preview() => GuideFixture.Create().WriteInputs(Root);
        public GuideBundle Load() => GuidePreview.Load(Index, Path.Combine(Root, "catalog"), Path.Combine(Root, "contracts"));
        public Dictionary<string, byte[]> Inputs() => Directory.GetFiles(Root, "*", SearchOption.AllDirectories)
            .Where(p => !p.StartsWith(Output + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            .Order(StringComparer.Ordinal).ToDictionary(p => Path.GetRelativePath(Root, p), File.ReadAllBytes, StringComparer.Ordinal);
        public void Dispose() => Directory.Delete(Root, recursive: true);
    }
}
