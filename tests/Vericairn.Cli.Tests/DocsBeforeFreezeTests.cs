// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using Vericairn.Cli;
using Vericairn.Protocol;
using Vericairn.Records.Estate;
using Vericairn.Records.Model;
using Vericairn.Records.Tests;
using Vericairn.Runtime.Tests;

namespace Vericairn.Cli.Tests;

/// <summary>
/// docs pass --before (the estate's design/release-procedure.md § 7, step docs-before-freeze): the version-tracking guides a set declares,
/// read before freezing for a token of an earlier release standing as the current one and for preparation wording, and with --record
/// each check's outcome as a verification of the product at the guides' clean commit, superseding the earlier one.
/// </summary>
[Collection("cli")]
public sealed class DocsBeforeFreezeTests
{
    private static readonly (string, System.Text.RegularExpressions.Regex)[] Earlier = [("0.9.1", GuidesBeforeFreeze.Token("0.9.1")), ("0.9.0", GuidesBeforeFreeze.Token("0.9.0"))];

    [Theory]
    [InlineData("# Installing Vericairn 0.9.1", true)]
    [InlineData("Unpack `vericairn-0.9.1-<platform>.tar.gz`.", true)]
    [InlineData("dotnet tool install dotnet-vc --version 0.9.1", true)]
    [InlineData("Download from https://github.com/Vericairn/vericairn/releases/tag/v0.9.1 first.", true)]
    [InlineData("dotnet add package Vericairn.Records.0.9.1.nupkg", true)]
    [InlineData("Its exclusive start follows 0.9.0's recorded end.", true)]
    [InlineData("This guide describes the 0.9.1 command line.", true)]
    [InlineData("From 0.9.1, a workspace setup command runs first.", false)]
    [InlineData("A runtime replaces an older one (0.9.1).", false)]
    [InlineData("It is checked (FIX-90, 0.9.1) as it was.", false)]
    [InlineData("Fixed in 0.9.0, and kept since.", false)]
    [InlineData("A basis 0.9.1 or earlier retained it.", false)]
    [InlineData("An estate upgrades from 0.8.0, 0.8.2, 0.8.4 or 0.9.0 without a format step.", false)]
    [InlineData("See [for 0.9.0](api-compatibility-0.9.0.md) and docs/releases/0.9.1/README.md.", false)]
    [InlineData("Version 0.9.10 and 10.9.1 are other versions.", false)]
    [InlineData("Designs move during the 0.9.0 cycle.", false)]
    [InlineData("Revision 11, November 7, 2026. Add a command in 0.9.1.", false)]
    [InlineData("This guide describes the current release (0.9.1).", true)]
    [InlineData("Unpack the archive (`vericairn-0.9.1-osx-arm64.tar.gz`) first.", true)]
    [InlineData("Download it (the current release, 0.9.1) from the page.", true)]
    [InlineData("A runtime replaces an older one (from 0.9.1).", false)]
    public void APriorVersionTokenIsFoundOnlyWhereItStandsAsTheCurrentRelease(string line, bool found)
    {
        List<GuidesBeforeFreeze.Finding> findings = GuidesBeforeFreeze.ScanText("docs/guide.md", line + "\n", Earlier).ToList();
        Assert.Equal(found, findings.Any(f => f.Check == GuidesBeforeFreeze.PriorVersionMethod));
        if (found)
        {
            Assert.All(findings, f => Assert.Equal(1, f.Line));
        }
    }

    [Fact]
    public void HistorySectionsAndMarkedSectionsKeepTheirTokensAndPreparationWordingIsFoundEverywhere()
    {
        string text = string.Join('\n',
            "# Guide",
            "Install 0.9.2.",
            "## Withdrawal and moving planned work (0.8.3)",
            "With 0.9.1 the command changed.",
            "### Details",
            "0.9.1 still applies here.",
            "## Revision history",
            "0.9.0 added the notes.",
            "## Current behaviour",
            "The 0.9.1 command line is current.",
            "## Older notes",
            GuidesBeforeFreeze.HistoryMarker,
            "0.9.0 was published in October.",
            "## Notes",
            "The release notes are a placeholder until the set is rendered.",
            "```sh",
            "## not a heading 0.9.1",
            "```",
            "TODO: name the release.");
        List<GuidesBeforeFreeze.Finding> findings = GuidesBeforeFreeze.ScanText("docs/guide.md", text, Earlier).ToList();
        // A heading labelled with a release, as "(0.8.3)", describes current behaviour and is read like any other section.
        Assert.Equal([4, 6, 10, 17], findings.Where(f => f.Check == GuidesBeforeFreeze.PriorVersionMethod).Select(f => f.Line).ToArray());
        Assert.Equal([(15, "are a placeholder"), (19, "TODO")], findings.Where(f => f.Check == GuidesBeforeFreeze.PreparationMethod).Select(f => (f.Line, f.Found)).ToArray());
        Assert.Equal("docs/guide.md:10: prior-version token 0.9.1 outside a history section: The 0.9.1 command line is current.", findings.Single(f => f.Line == 10).ToString());

        // A marker before the first heading marks nothing: no guide exempts itself whole.
        List<GuidesBeforeFreeze.Finding> marked = GuidesBeforeFreeze.ScanText("docs/guide.md", GuidesBeforeFreeze.HistoryMarker + "\n# Guide\nInstall 0.9.1.\n", Earlier).ToList();
        Assert.Equal(3, Assert.Single(marked).Line);
    }

    [Fact]
    public void AVersionedPageIsReadOnlyForPreparationWordingAndAGeneratedDocumentIsSkipped()
    {
        using var guides = new TestEstate();
        guides.WriteFile("docs/install.md", "Install Vericairn 0.9.1.\n");
        guides.WriteFile("docs/api-compatibility-0.9.1.md", "0.9.1 adds members.\nThe notes are a placeholder.\n");
        GuidesBeforeFreeze.Scan scan = GuidesBeforeFreeze.Run(Path.Combine(guides.Root, "docs"), "docs", ["install.md", "api-compatibility-0.9.1.md", "cli-reference.md"], ["0.9.1"]);
        Assert.Equal(["install.md"], scan.Read);
        Assert.Equal(["api-compatibility-0.9.1.md"], scan.History);
        Assert.Equal(["cli-reference.md"], scan.Skipped);
        Assert.Equal([("docs/install.md", GuidesBeforeFreeze.PriorVersionMethod), ("docs/api-compatibility-0.9.1.md", GuidesBeforeFreeze.PreparationMethod)], scan.Findings.Select(f => (f.Path, f.Check)).ToArray());
        Assert.Throws<InvalidOperationException>(() => GuidesBeforeFreeze.Run(Path.Combine(guides.Root, "docs"), "docs", ["../outside.md"], ["0.9.1"]));
    }

    [Theory]
    [InlineData("sub/secret.md")]
    [InlineData("sub\\secret.md")]
    public void AGuideUnderALinkedDirectoryIsRefusedRatherThanFollowedOut(string declared)
    {
        using var guides = new TestEstate();
        guides.WriteFile("outside/secret.md", "Install Vericairn 0.9.1.\n");
        Directory.CreateDirectory(Path.Combine(guides.Root, "docs"));
        try
        {
            Directory.CreateSymbolicLink(Path.Combine(guides.Root, "docs", "sub"), Path.Combine(guides.Root, "outside"));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return;
        }

        // Either separator divides the name, so a backslash, Windows' own separator, never carries a name past the link check.
        var refusal = Assert.Throws<InvalidOperationException>(() => GuidesBeforeFreeze.Run(Path.Combine(guides.Root, "docs"), "docs", [declared], ["0.9.1"]));
        Assert.Contains("docs/sub is a link", refusal.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ALinkedGuideIsRefusedRatherThanFollowed()
    {
        using var guides = new TestEstate();
        guides.WriteFile("elsewhere.md", "Install Vericairn 0.9.1.\n");
        Directory.CreateDirectory(Path.Combine(guides.Root, "docs"));
        try
        {
            File.CreateSymbolicLink(Path.Combine(guides.Root, "docs", "install.md"), Path.Combine(guides.Root, "elsewhere.md"));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return;
        }

        var refusal = Assert.Throws<InvalidOperationException>(() => GuidesBeforeFreeze.Run(Path.Combine(guides.Root, "docs"), "docs", ["install.md"], ["0.9.1"]));
        Assert.Contains("docs/install.md is a link", refusal.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TheEstatesEarlierReleasesAreCheckedAndEachChecksOutcomeIsRecordedAtTheGuidesCleanCommit()
    {
        using var f = new DocumentPassFixture();
        CliHarness.Outcome Run(params string[] args) => CliHarness.RunWithApi(f.Service, null, ["--estate", f.Estate.Root, .. args]);
        string declaration = Path.Combine(f.Repository.Root, "docs", "releases", "documents.json");
        f.Repository.WriteFile("docs/releases/documents.json", """["install.md", "guide.md", "cli-reference.md"]""");
        f.Repository.WriteFile("docs/install.md", "# Installing Widget 0.2.0\n\nRun `widget --version 0.2.0`, installed under C:\\Widget\\bin or /opt/widget/bin.\n");
        f.Repository.WriteFile("docs/guide.md", "# Guide\n\nFrom 0.1.0, the guide explains the command.\n");
        int before = f.Estate.Load().Records.Count;

        // Read only: the two earlier releases come from the estate, each location is listed, and the exit code says a trap sprang.
        CliHarness.Outcome checking = Run("docs", "pass", "0.3.0", "--before", "--documents", declaration, "--product", "widget");
        Assert.Equal(1, checking.Code);
        Assert.Contains("read for 0.2.0, 0.1.0", checking.Out, StringComparison.Ordinal);
        Assert.Contains("docs/install.md:1: prior-version token 0.2.0 outside a history section: # Installing Widget 0.2.0", checking.Out, StringComparison.Ordinal);
        Assert.Contains("docs/install.md:3: prior-version token 0.2.0", checking.Out, StringComparison.Ordinal);
        Assert.DoesNotContain("guide.md:3", checking.Out, StringComparison.Ordinal);
        Assert.Contains($"{GuidesBeforeFreeze.PriorVersionMethod}: failed, 2 locations", checking.Out, StringComparison.Ordinal);
        Assert.Contains($"{GuidesBeforeFreeze.PreparationMethod}: passed", checking.Out, StringComparison.Ordinal);
        Assert.Equal(before, f.Estate.Load().Records.Count);

        // Recording needs the guides committed: a dirty clone records nothing.
        CliHarness.Outcome dirty = Run("docs", "pass", "0.3.0", "--before", "--documents", declaration, "--product", "widget", "--record", "--by", f.Estate.OperatorId);
        Assert.NotEqual(0, dirty.Code);
        Assert.Contains("commit the guides first", dirty.Err, StringComparison.Ordinal);
        Assert.Equal(before, f.Estate.Load().Records.Count);

        f.Git("add", "--all");
        f.Git("commit", "-m", "Guides for 0.3.0, not yet revised");
        string failedCommit = f.Git("rev-parse", "HEAD").Trim();
        CliHarness.Outcome failed = Run("--json", "docs", "pass", "0.3.0", "--before", "--documents", declaration, "--product", "widget", "--record", "--by", f.Estate.OperatorId);
        Assert.Equal(1, failed.Code);
        using (JsonDocument json = JsonDocument.Parse(failed.Out))
        {
            Assert.Equal(failedCommit, json.RootElement.GetProperty("commit").GetString());
            Assert.Equal(2, json.RootElement.GetProperty("recorded").GetArrayLength());
        }

        EstateModel model = f.Estate.Load();
        RecordEnvelope prior = Assert.Single(model.Current("verification"), v => EstateModel.BodyString(v, "method") == GuidesBeforeFreeze.PriorVersionMethod);
        Assert.Equal(("failed", failedCommit, "0.3.0", f.Product.Id), (EstateModel.BodyString(prior, "outcome"), EstateModel.BodyString(prior, "commit"), GoalPredicates.FieldString(prior, "environment.release"), prior.Subject!.Id));
        Assert.Equal("0.2.0 0.1.0", GoalPredicates.FieldString(prior, "environment.previous"));
        Assert.Contains("docs/install.md:1: prior-version token 0.2.0", EstateModel.BodyString(prior, "summary"), StringComparison.Ordinal);

        // A guide's line is quoted without the machine paths it names, so the record is portable on every platform.
        Assert.Contains("installed under <path> or <path>", EstateModel.BodyString(prior, "summary"), StringComparison.Ordinal);
        foreach (RecordEnvelope recorded in model.Current("verification"))
        {
            string text = File.ReadAllText(Path.Combine(f.Estate.Root, recorded.ExpectedPath));
            Assert.DoesNotContain(text.Split('\n'), line => Vericairn.Records.Estate.SecretShapes.AbsolutePath().IsMatch(line));
            Assert.DoesNotContain(Path.GetFullPath(f.Repository.Root), text, StringComparison.OrdinalIgnoreCase);
            Assert.False(PortableText.HasMachinePath(recorded.Body.GetRawText().Replace(recorded.Body.GetProperty("environment").GetRawText(), "{}", StringComparison.Ordinal)), recorded.Body.GetRawText());
        }
        Assert.Equal("passed", EstateModel.BodyString(Assert.Single(model.Current("verification"), v => EstateModel.BodyString(v, "method") == GuidesBeforeFreeze.PreparationMethod), "outcome"));

        // Revised and committed, the next check supersedes each earlier one: the latest outcome is the current one.
        f.Repository.WriteFile("docs/install.md", "# Installing Widget 0.3.0\n\nRun `widget --version 0.3.0`.\n");
        f.Git("add", "--all");
        f.Git("commit", "-m", "Guides for 0.3.0");
        CliHarness.Outcome passed = Run("docs", "pass", "0.3.0", "--before", "--documents", declaration, "--product", "widget", "--record", "--by", f.Estate.OperatorId);
        Assert.Equal(0, passed.Code);
        model = f.Estate.Load();
        RecordEnvelope current = Assert.Single(model.Current("verification"), v => EstateModel.BodyString(v, "method") == GuidesBeforeFreeze.PriorVersionMethod);
        Assert.Equal(("passed", prior.Id), (EstateModel.BodyString(current, "outcome"), current.Supersedes));
        Assert.Equal(2, model.Current("verification").Count());
        Assert.False(model.Findings.HasErrors, string.Join('\n', model.Findings));
    }

    [Fact]
    public void BeforeOptionsAreRefusedOutsideTheirModeAndExplicitPreviousVersionsNeedNoEstate()
    {
        using var guides = new TestEstate();
        guides.WriteFile("docs/releases/documents.json", """["install.md"]""");
        guides.WriteFile("docs/install.md", "Install 1.0.0.\n");
        string declaration = Path.Combine(guides.Root, "docs", "releases", "documents.json");
        CliHarness.Outcome offline = CliHarness.Run("--estate", Path.Combine(guides.Root, "no-estate"), "docs", "pass", "1.1.0", "--before", "--documents", declaration, "--previous", "1.0.0");
        Assert.Equal(1, offline.Code);
        Assert.Contains("prior-version token 1.0.0", offline.Out, StringComparison.Ordinal);
        Assert.NotEqual(0, CliHarness.Run("docs", "pass", "1.1.0", "--before").Code);
        Assert.NotEqual(0, CliHarness.Run("docs", "pass", "1.1.0", "--documents", declaration).Code);
        Assert.NotEqual(0, CliHarness.Run("docs", "pass", "1.1.0", "--before", "--documents", declaration, "--record").Code);
        Assert.NotEqual(0, CliHarness.Run("docs", "pass", "1.1.0", "--before", "--documents", declaration, "--assign-to", "x").Code);
        CliHarness.Outcome given = CliHarness.Run("docs", "pass", "1.1.0", "--before", "--documents", declaration, "--previous", "1.0.0", "--record", "--by", "x");
        Assert.NotEqual(0, given.Code);
        Assert.Contains("--previous goes only with a check that records nothing", given.Err, StringComparison.Ordinal);
        CliHarness.Outcome help = CliHarness.Run("docs", "pass", "--help");
        Assert.Contains("--before", help.Out, StringComparison.Ordinal);
        Assert.Contains("--previous", help.Out, StringComparison.Ordinal);
    }
}
