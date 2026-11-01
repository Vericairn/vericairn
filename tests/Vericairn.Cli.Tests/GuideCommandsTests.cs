// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.CommandLine;
using System.Text.Json;
using Vericairn.Cli;
using Vericairn.Runtime.Guides;
using Vericairn.Runtime.Tests;

namespace Vericairn.Cli.Tests;

[Collection("cli")]
public sealed class GuideCommandsTests
{
    [Fact]
    public void HelpExposesOnlyLocalPreparationAndChecksWithExplicitPaths()
    {
        Assert.Contains("private previews", CliHarness.Ok("guides", "--help").Out, StringComparison.Ordinal);
        RootCommand root = CliApp.Build(cancellationToken: TestContext.Current.CancellationToken);
        Command guides = root.Subcommands.Single(c => c.Name == "guides");
        Assert.Equal(["check", "compare", "generate"], guides.Subcommands.Select(c => c.Name).Order(StringComparer.Ordinal));
        foreach (string action in new[] { "generate", "check" })
        {
            CliHarness.Outcome help = CliHarness.Ok("guides", action, "--help");
            foreach (string option in new[] { "--catalog", "--contracts", "--index", "--out", "--select" })
                Assert.Contains(option, help.Out, StringComparison.Ordinal);
            Command command = guides.Subcommands.Single(c => c.Name == action);
            foreach (string option in new[] { "--catalog", "--contracts", "--index", "--out" })
                Assert.True(command.Options.Single(o => o.Name == option).Required);
            Assert.NotEqual(0, CliHarness.Run("guides", action).Code);
        }
        Assert.NotEqual(0, CliHarness.Run("guides", "publish").Code);
    }

    [Fact]
    public void GenerateAndCheckUseNoEstateAndProduceIdenticalPrivateBundles()
    {
        string root = CliHarness.TempDirectory("guide-commands");
        try
        {
            GuideFixture.Create().WriteInputs(root);
            string absentEstate = Path.Combine(root, "absent-estate");
            string first = Path.Combine(root, "first");
            string second = Path.Combine(root, "second");
            foreach (string output in new[] { first, second })
            {
                CliHarness.Outcome written = CliHarness.Ok(["--estate", absentEstate, "--json", .. Arguments("generate", root, output)]);
                Assert.True(written.Json.GetProperty("ok").GetBoolean());
                Assert.Equal("private-preview", written.Json.GetProperty("publicationState").GetString());
                Assert.Empty(written.Json.GetProperty("findings").EnumerateArray());
                Assert.Equal(0, CliHarness.Run(["--estate", absentEstate, .. Arguments("check", root, output)]).Code);
            }
            string[] files = Directory.GetFiles(first, "*", SearchOption.AllDirectories).Select(p => Path.GetRelativePath(first, p)).Order(StringComparer.Ordinal).ToArray();
            Assert.Equal(files, Directory.GetFiles(second, "*", SearchOption.AllDirectories).Select(p => Path.GetRelativePath(second, p)).Order(StringComparer.Ordinal));
            foreach (string file in files) Assert.Equal(File.ReadAllBytes(Path.Combine(first, file)), File.ReadAllBytes(Path.Combine(second, file)));
            Assert.False(Directory.Exists(absentEstate));
            Assert.Empty(Directory.EnumerateFileSystemEntries(Path.Combine(root, "home")));
            CliHarness.Outcome compared = CliHarness.Ok("--json", "guides", "compare", "--before", Path.Combine(first, "source-manifest.json"), "--after", Path.Combine(second, "source-manifest.json"));
            Assert.Empty(compared.Json.GetProperty("due").EnumerateArray());
            Assert.Empty(compared.Json.GetProperty("findings").EnumerateArray());
        }
        finally { CliHarness.Delete(root); }
    }

    [Fact]
    public void SelectionResolvesAliasesAndComparisonReportsAffectedKeys()
    {
        string root = CliHarness.TempDirectory("guide-selection");
        try
        {
            GuideFixture fixture = GuideFixture.Create();
            fixture.WriteInputs(root);
            string before = Path.Combine(root, "before");
            string after = Path.Combine(root, "after");
            string[] selection = ["--select", "prior.shared.adapter", "--select", "provider.workshop00.group"];
            Assert.Equal(0, CliHarness.Run([.. Arguments("generate", root, before), .. selection]).Code);
            Assert.Equal(2, Directory.GetFiles(Path.Combine(before, "guides"), "*.json").Length);
            fixture.Replace("family00", "never energize an actuator", "never energize any actuator");
            fixture.WriteInputs(root);
            Assert.Equal(0, CliHarness.Run([.. Arguments("generate", root, after), .. selection]).Code);
            CliHarness.Outcome comparison = CliHarness.Ok("--json", "guides", "compare", "--before", Path.Combine(before, "source-manifest.json"), "--after", Path.Combine(after, "source-manifest.json"));
            Assert.Equal(["fixture.shared.adapter", "provider.workshop00.group"], comparison.Json.GetProperty("due").EnumerateArray().Select(e => e.GetString()));
            Assert.Empty(comparison.Json.GetProperty("findings").EnumerateArray());
        }
        finally { CliHarness.Delete(root); }
    }

    [Fact]
    public void EditedGuideFailsCheckAndRegenerationWithoutRepairingIt()
    {
        string root = CliHarness.TempDirectory("guide-edits");
        try
        {
            GuideFixture.Create().WriteInputs(root);
            string output = Path.Combine(root, "preview");
            Assert.Equal(0, CliHarness.Run(Arguments("generate", root, output)).Code);
            string guide = Path.Combine(output, "guides", "provider.workshop00.group.md");
            string changed = File.ReadAllText(guide).Replace("never energize", "always energize", StringComparison.Ordinal);
            File.WriteAllText(guide, changed);
            foreach (string action in new[] { "check", "generate" })
            {
                CliHarness.Outcome refused = CliHarness.Run(["--json", .. Arguments(action, root, output)]);
                Assert.Equal(1, refused.Code);
                Assert.False(refused.Json.GetProperty("ok").GetBoolean());
                Assert.NotEmpty(refused.Json.GetProperty("findings").EnumerateArray());
                Assert.Equal(changed, File.ReadAllText(guide));
            }
        }
        finally { CliHarness.Delete(root); }
    }

    [Fact]
    public void MissingSourceReportsPerEntryCoverageAndNoGuides()
    {
        string root = CliHarness.TempDirectory("guide-missing-source");
        try
        {
            GuideFixture.Create().WriteInputs(root);
            File.Delete(Path.Combine(root, "catalog", "catalog", "workshop00.md"));
            string output = Path.Combine(root, "preview");
            CliHarness.Outcome failed = CliHarness.Run(["--json", .. Arguments("generate", root, output)]);
            Assert.Equal(1, failed.Code);
            Assert.Contains(failed.Json.GetProperty("findings").EnumerateArray(), f => f.GetProperty("entry").GetString() == "provider.workshop00.group");
            Assert.False(Directory.Exists(Path.Combine(output, "guides")));
            using JsonDocument coverage = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(output, "coverage-manifest.json")));
            Assert.False(coverage.RootElement.GetProperty("complete").GetBoolean());
            Assert.NotEqual(0, CliHarness.Run(Arguments("generate", root, Path.Combine(root, "catalog", "output"))).Code);
            Assert.False(Directory.Exists(Path.Combine(root, "catalog", "output")));
        }
        finally { CliHarness.Delete(root); }
    }

    [Fact]
    public void ParentLinksAndUnquotedExamplesAllowACompletePrivatePreview()
    {
        string root = CliHarness.TempDirectory("guide-source-prose");
        try
        {
            GuideFixture fixture = GuideFixture.Create();
            string family = System.Text.Encoding.UTF8.GetString(fixture.Sources["family00"]);
            fixture.Replace("family00", family, family + "\n[Catalog](../catalog.md)\n");
            fixture.Replace("contracts", "# Synthetic role contracts", "# Synthetic role contracts: `<!--` is an example delimiter");
            fixture.Replace("family00", "Reserved acts: the designated person approves every delivery.", "Reserved acts: review [the notes](notes.md) before delivery.");
            fixture.WriteInputs(root);
            string output = Path.Combine(root, "preview");
            CliHarness.Outcome generated = CliHarness.Ok(["--json", .. Arguments("generate", root, output)]);
            Assert.True(generated.Json.GetProperty("ok").GetBoolean());
            Assert.Equal("private-preview", generated.Json.GetProperty("publicationState").GetString());
            Assert.Equal(0, CliHarness.Run(Arguments("check", root, output)).Code);
            using JsonDocument guide = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(output, "guides", "provider.workshop00.group.json")));
            Assert.Single(guide.RootElement.GetProperty("sections").EnumerateArray().SelectMany(s => s.GetProperty("quotes").EnumerateArray()),
                q => q.GetProperty("id").GetString() == "role.provider");
            string html = File.ReadAllText(Path.Combine(output, "guides", "provider.workshop00.group.html"));
            Assert.Contains("[the notes](notes.md)", html, StringComparison.Ordinal);
            Assert.DoesNotContain("<a ", html, StringComparison.Ordinal);
            Assert.False(File.Exists(Path.Combine(root, "catalog", "catalog", "notes.md")));
        }
        finally { CliHarness.Delete(root); }
    }

    [Theory]
    [InlineData("block", "malicious-markup")]
    [InlineData("cell", "malicious-markup")]
    [InlineData("table", "malformed-table")]
    public void InvalidQuotedContentAndMalformedOwningTablesFailTheCommand(string fault, string code)
    {
        string root = CliHarness.TempDirectory("guide-source-findings");
        try
        {
            GuideFixture fixture = GuideFixture.Create();
            if (fault == "table")
            {
                string family = System.Text.Encoding.UTF8.GetString(fixture.Sources["family00"]);
                fixture.Replace("family00", family, family + "\n## Additional routes\n| Provider | Role | Works with | Caveat |\n|---|---|---|\n| Extra route | Observe. | Sketch. | No delivery. |\n");
            }
            else fixture.Replace("family00", fault == "block" ? "Reserved acts: the designated person approves every delivery." : "Builders prepare measured sketches",
                "[action](javascript:run())");
            fixture.WriteInputs(root);
            string output = Path.Combine(root, "preview");
            CliHarness.Outcome failed = CliHarness.Run(["--json", .. Arguments("generate", root, output)]);
            Assert.Equal(1, failed.Code);
            Assert.False(failed.Json.GetProperty("ok").GetBoolean());
            Assert.Contains(failed.Json.GetProperty("findings").EnumerateArray(), f => f.GetProperty("code").GetString() == code);
            Assert.False(Directory.Exists(Path.Combine(output, "guides")));
            using JsonDocument coverage = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(output, "coverage-manifest.json")));
            Assert.False(coverage.RootElement.GetProperty("complete").GetBoolean());
        }
        finally { CliHarness.Delete(root); }
    }

    private static string[] Arguments(string action, string root, string output) =>
        ["guides", action, "--catalog", Path.Combine(root, "catalog"), "--contracts", Path.Combine(root, "contracts"),
            "--index", Path.Combine(root, "index.json"), "--out", output];
}
