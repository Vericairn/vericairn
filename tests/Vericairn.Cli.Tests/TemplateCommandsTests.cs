// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.CommandLine;
using System.Text;
using Vericairn.Cli;
using Vericairn.Records.Estate;
using Vericairn.Records.Hashing;
using Vericairn.Records.Tests;
using Vericairn.Records.Templates;
using Vericairn.Runtime.Service;

namespace Vericairn.Cli.Tests;

[Collection("cli")]
public sealed class TemplateCommandsTests
{
    [Fact]
    public void TemplateCommandsHaveConcreteClassesAndExplicitCreationAndReviewOptions()
    {
        RootCommand root = CliApp.Build(cancellationToken: TestContext.Current.CancellationToken);
        Command templates = root.Subcommands.Single(c => c.Name == "templates");
        Assert.IsType<TemplatesCommand>(templates);
        Assert.IsType<TemplatesListCommand>(templates.Subcommands.Single(c => c.Name == "list"));
        Assert.IsType<TemplatesShowCommand>(templates.Subcommands.Single(c => c.Name == "show"));
        Assert.IsType<TemplatesRenderCommand>(templates.Subcommands.Single(c => c.Name == "render"));
        Assert.IsType<TemplatesCheckCommand>(templates.Subcommands.Single(c => c.Name == "check"));
        Command docs = root.Subcommands.Single(c => c.Name == "docs");
        Command create = Assert.IsType<DocsNewCommand>(docs.Subcommands.Single(c => c.Name == "new"));
        foreach (string name in new[] { "--by", "--version", "--title" }) Assert.True(create.Options.Single(o => o.Name == name).Required);
        Command adopt = Assert.IsType<DocsAdoptCommand>(docs.Subcommands.Single(c => c.Name == "adopt"));
        foreach (string name in new[] { "--by", "--version", "--expected-content" }) Assert.True(adopt.Options.Single(o => o.Name == name).Required);
        foreach (string name in new[] { "--expected-declaration", "--preview", "--with-regions", "--configuration" }) Assert.False(adopt.Options.Single(o => o.Name == name).Required);
        Assert.IsType<DocsSectionsCommand>(docs.Subcommands.Single(c => c.Name == "sections"));
        Assert.IsType<DocsBriefCommand>(docs.Subcommands.Single(c => c.Name == "brief"));
        Command review = docs.Subcommands.Single(c => c.Name == "review");
        foreach (string name in new[] { "--section", "--fidelity", "--judgments" }) Assert.False(review.Options.Single(o => o.Name == name).Required);
    }

    [Fact]
    public void AdoptionPreviewsAndAttachesWithoutReplacingTheExistingRoadmap()
    {
        using var estate = new TestEstate(3);
        const string text = "# Roadmap\n\nRevision 2: preserve this history.\n\n## Direction\n\nKeep the authored narrative.\n\n| Release | Work |\n|---|---|\n| Later | EXAMPLE-1 |\n";
        TemplateFixture.Revision(estate, "design/roadmap.md", text);
        using EstateService api = EstateService.Open(estate.Root);
        string[] arguments = ["docs", "adopt", "vericairn.estate/roadmap", "design/roadmap.md", "--version", "1.0.0",
            "--by", estate.OperatorId, "--expected-content", ContentHash.Sha256(Encoding.UTF8.GetBytes(text))];
        CliHarness.Outcome Run(params string[] extra) => CliHarness.RunWithApi(api, null, ["--estate", estate.Root, "--json", .. arguments, .. extra]);
        Assert.Equal(0, Run("--preview").Code);
        Assert.Empty(api.Session.Load().OfType("living-document"));
        Assert.Equal(0, Run().Code);
        Assert.Equal(text, File.ReadAllText(Path.Combine(estate.Root, "design", "roadmap.md")));
        Assert.Single(api.Session.Load().OfType("document-revision"));
        Assert.Single(api.Session.Load().Current("living-document"));
        Assert.Contains(api.DocumentSections("design/roadmap.md").Sections, s => s.Id == "releases" && s.State == "missing");
        Assert.Equal(2, Run().Code);
    }

    [Fact]
    public void InterruptedAdoptionPrintsTheStoredPinEvenWhenNoDeclarationWasStored()
    {
        using var estate = new TestEstate(3);
        const string path = "design/roadmap.md";
        string text = "# Roadmap\n\n## Direction\n\nRetain this narrative.\n\n## Releases\n\n"
            + GeneratedRegions.Start("releases") + "\nOld rows.\n" + GeneratedRegions.End("releases") + "\n";
        estate.WriteFile(path, text);
        using EstateService api = EstateService.Open(estate.Root);
        api.BeforeStoringTogether = record =>
        {
            if (record.Envelope.Type == "living-document") throw new IOException("Injected interruption after the pin.");
        };
        CliHarness.Outcome result = CliHarness.RunWithApi(api, null, "--estate", estate.Root, "docs", "adopt", "vericairn.estate/roadmap", path,
            "--version", "1.0.0", "--by", estate.OperatorId, "--expected-content", ContentHash.Sha256(Encoding.UTF8.GetBytes(text)), "--with-regions");
        Assert.Equal(2, result.Code);
        var pin = Assert.Single(api.Session.Load().OfType("document-revision"));
        Assert.Empty(api.Session.Load().OfType("living-document"));
        Assert.Contains(pin.Id, result.Out, StringComparison.Ordinal);
        Assert.Contains(pin.ComputedHash, result.Out, StringComparison.Ordinal);
        Assert.Contains("Reconcile the original batch", result.Out, StringComparison.Ordinal);
        Assert.Equal(text, File.ReadAllText(Path.Combine(estate.Root, path)));
    }

    [Fact]
    public void ExplicitTemplatePinRefreshesTheRecordedRevisionAndKeepsTheAuthoredAnswer()
    {
        using var estate = new TestEstate(3);
        using EstateService api = EstateService.Open(estate.Root);
        CliHarness.Outcome Run(params string[] args) => CliHarness.RunWithApi(api, null, ["--estate", estate.Root, "--json", .. args]);
        Assert.Equal(0, Run("docs", "new", "vericairn.estate/design-document", "design/example.md", "--version", "1.0.0", "--by", estate.OperatorId, "--title", "Example").Code);
        var definition = FirstPartyTemplates.Definitions.Single(d => d.Id == "vericairn.estate/design-document");
        string file = Path.Combine(estate.Root, "design", "example.md");
        File.WriteAllText(file, TemplateFixture.Fill(File.ReadAllText(file), definition, "answer", "Keep every authored word.\n"));
        CliHarness.Outcome pinned = Run("docs", "pin", "design/example.md", "--by", estate.OperatorId, "--summary", "Record the fresh template.", "--render-template");
        Assert.Equal(0, pinned.Code);
        string content = File.ReadAllText(file);
        Assert.Equal("Keep every authored word.\n", TemplateSections.Read(content, definition.Elements)["answer"].Content);
        Assert.StartsWith("Revision 1, ", TemplateSections.Read(content, definition.Elements)["revision"].Content, StringComparison.Ordinal);
        Assert.Equal(ContentHash.Sha256File(file), api.Session.Load().ById[pinned.Id].Body.GetProperty("hash").GetString());
        Assert.Equal(2, Run("docs", "pin", "design/example.md", "--by", estate.OperatorId, "--summary", "Ordinary pins retain their default.").Code);
        Assert.Single(api.Session.Load().OfType("document-revision"));
    }

    [Fact]
    public void CommandsCreateInspectBriefAndReviewOneSectionWithoutClearingItsSibling()
    {
        using var estate = new TestEstate(3);
        TemplateDefinition definition = TemplateFixture.Install(estate, TemplateFixture.Contract(TemplateFixture.Section("first"), TemplateFixture.Section("second")));
        using EstateService api = EstateService.Open(estate.Root);
        CliHarness.Outcome Run(params string[] args) => CliHarness.RunWithApi(api, null, ["--estate", estate.Root, "--json", .. args]);
        Assert.Equal(0, Run("templates", "list").Code);
        Assert.Equal(0, Run("templates", "show", definition.Id, "--version", definition.Version).Code);
        Assert.Equal(2, Run("templates", "show", definition.Id, "--version", "9.0.0").Code);
        CliHarness.Outcome created = Run("docs", "new", definition.Id, "docs/example.md", "--version", definition.Version, "--by", estate.OperatorId, "--title", "Example");
        Assert.Equal(0, created.Code);
        Assert.Equal("docs/example.md", created.Json.GetProperty("path").GetString());
        Assert.All(Run("docs", "sections", "docs/example.md").Json.GetProperty("sections").EnumerateArray(), s => Assert.Equal("missing", s.GetProperty("state").GetString()));
        string text = File.ReadAllText(Path.Combine(estate.Root, "docs", "example.md"));
        estate.SetClock(api.Session.Load().Records.Max(r => r.AtTime).AddMinutes(-1));
        TemplateFixture.Revision(estate, "docs/example.md", TemplateFixture.Fill(text, definition, "first", "Exact authored section.\n"));
        CliHarness.Outcome brief = Run("docs", "brief", "docs/example.md", "--section", "first");
        Assert.Equal(0, brief.Code);
        Assert.True(brief.Json.GetProperty("brief").GetProperty("complete").GetBoolean());
        Assert.Equal(2, Run("docs", "brief", "docs/example.md", "--section", "first", "--max-bytes", "1").Code);
        int before = api.Session.Load().Records.Count;
        Assert.Equal(2, Run("docs", "review", "docs/example.md", "--by", estate.OperatorId, "--outcome", "current", "--summary", "Missing fidelity.", "--section", "first").Code);
        Assert.Equal(before, api.Session.Load().Records.Count);
        Assert.Equal(0, Run("docs", "review", "docs/example.md", "--by", estate.OperatorId, "--outcome", "current", "--summary", "Read exact evidence.", "--section", "first", "--fidelity", "draft").Code);
        Assert.Equal(["reviewed", "missing"], api.DocumentSections("docs/example.md").Sections.Select(s => s.State));
        Assert.Empty(api.Session.Load().OfType("acceptance"));
        Assert.Equal(2, Run("docs", "new", definition.Id, "docs/example.md", "--version", definition.Version, "--by", estate.OperatorId, "--title", "Example").Code);
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("{\"permission\":\"operator\"}")]
    public void InvalidConfigurationRefusesWithoutCreatingADeclaration(string configuration)
    {
        using var estate = new TestEstate(3);
        TemplateDefinition definition = TemplateFixture.Install(estate, TemplateFixture.Contract(TemplateFixture.Section("answer")));
        using EstateService api = EstateService.Open(estate.Root);
        estate.WriteFile("docs/configuration.json", configuration);
        CliHarness.Outcome result = CliHarness.RunWithApi(api, null, "--estate", estate.Root, "docs", "new", definition.Id, "docs/example.md",
            "--version", definition.Version, "--by", estate.OperatorId, "--title", "Example", "--configuration", Path.Combine(estate.Root, "docs", "configuration.json"));
        Assert.Equal(2, result.Code);
        Assert.Contains("Configuration must be an object", result.Err, StringComparison.Ordinal);
        Assert.Empty(api.Session.Load().OfType("living-document"));
        Assert.False(File.Exists(Path.Combine(estate.Root, "docs", "example.md")));
    }
}
