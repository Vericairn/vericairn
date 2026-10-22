// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.CommandLine;
using System.Reflection;
using Vericairn.Records.Estate;

namespace Vericairn.Cli.Tests;

[Collection("cli")]
public sealed class EstateInitCommandTests
{
    private const string Commit = "0123456789abcdef0123456789abcdef01234567";
    private const string OverrideCommit = "abcdef0123456789abcdef0123456789abcdef01";

    [Theory]
    [InlineData("repository-synced")]
    [InlineData("local-only")]
    public void PublishedBuildPinsItsRecordedCommitWithoutAGitRepository(string profile)
    {
        string root = CliHarness.TempDirectory("init-published");
        try
        {
            CliHarness.Outcome outcome = WithVersion("0.8.3+" + Commit, Arguments(root, profile));
            AssertPin(root, outcome, Commit, profile);
            Assert.False(Directory.Exists(Path.Combine(root, ".git")));
        }
        finally { CliHarness.Delete(root); }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void TheEstatesGitCommitNeverSuppliesTheDefaultPin(bool recordedCommit)
    {
        string root = CliHarness.TempDirectory("init-git");
        try
        {
            ContextJourneyTests.Git(root, "init", "-q", "-b", "main");
            ContextJourneyTests.Git(root, "config", "core.autocrlf", "false");
            ContextJourneyTests.Git(root, "commit", "-q", "--allow-empty", "-m", "Estate fixture");
            byte[] head = File.ReadAllBytes(Path.Combine(root, ".git", "refs", "heads", "main"));
            Assert.NotEqual(Commit, System.Text.Encoding.UTF8.GetString(head).Trim());

            string version = recordedCommit ? "0.8.3+" + Commit : "0.8.3-dev";
            CliHarness.Outcome outcome = WithVersion(version, Arguments(root));
            if (recordedCommit)
            {
                AssertPin(root, outcome, Commit);
            }
            else
            {
                Assert.Equal(2, outcome.Code);
                Assert.Empty(outcome.Out);
                Assert.Contains("no recorded 40-digit source commit", outcome.Err, StringComparison.Ordinal);
                Assert.Contains("--commit <40-hex-commit>", outcome.Err, StringComparison.Ordinal);
                Assert.Equal([".git"], Directory.EnumerateFileSystemEntries(root).Select(Path.GetFileName));
            }

            Assert.Equal(head, File.ReadAllBytes(Path.Combine(root, ".git", "refs", "heads", "main")));
        }
        finally { CliHarness.Delete(root); }
    }

    [Fact]
    public void DefaultPinMatchesTheRunningBuildsVersionReport()
    {
        string root = CliHarness.TempDirectory("init-running");
        try
        {
            string version = CliHarness.Ok("--version").Out.Trim();
            Assert.Equal(typeof(CliApp).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion, version);
            Assert.Equal(version, CliHarness.Ok("version").Out.Trim());
            string commit = version[(version.IndexOf('+') + 1)..];
            Assert.Matches("^[0-9a-f]{40}$", commit);
            AssertPin(root, CliHarness.Run(Arguments(root)), commit);
        }
        finally { CliHarness.Delete(root); }
    }

    [Theory]
    [InlineData("repository-synced")]
    [InlineData("local-only")]
    public void BuildWithoutARecordedCommitRefusesBeforeWritingAndExplainsTheOverride(string profile)
    {
        string root = CliHarness.TempDirectory("init-no-commit");
        try
        {
            CliHarness.Outcome outcome = WithVersion("0.8.3-dev", Arguments(root, profile));
            AssertRefusal(root, outcome);
        }
        finally { CliHarness.Delete(root); }
    }

    [Theory]
    [InlineData("unknown")]
    [InlineData("0.8.3+")]
    [InlineData("0.8.3+0123456")]
    [InlineData("0.8.3+0123456789abcdef0123456789abcdef0123456g")]
    [InlineData("0.8.3+0123456789abcdef0123456789abcdef012345678")]
    [InlineData("0.8.3+0123456789abcdef0123456789abcdef01234567.dirty")]
    public void InexactVersionMetadataCannotBecomeAPin(string version)
    {
        string root = CliHarness.TempDirectory("init-inexact");
        try { AssertRefusal(root, WithVersion(version, Arguments(root))); }
        finally { CliHarness.Delete(root); }
    }

    [Theory]
    [InlineData("0.8.3+" + Commit, "repository-synced")]
    [InlineData("0.8.3+" + Commit, "local-only")]
    [InlineData("0.8.3-dev", "repository-synced")]
    [InlineData("0.8.3-dev", "local-only")]
    public void ExplicitCommitOverridesTheBuildAndKeepsTheChosenRepository(string version, string profile)
    {
        string root = CliHarness.TempDirectory("init-override");
        try
        {
            CliHarness.Outcome outcome = WithVersion(version,
                [.. Arguments(root, profile), "--commit", OverrideCommit, "--repository", "Example/vericairn"]);
            AssertPin(root, outcome, OverrideCommit, profile, "Example/vericairn");
        }
        finally { CliHarness.Delete(root); }
    }

    [Fact]
    public void CommitOptionWithoutAValueDoesNotFallBackToTheBuild()
    {
        string root = CliHarness.TempDirectory("init-missing-value");
        try
        {
            CliHarness.Outcome outcome = CliHarness.Run([.. Arguments(root), "--commit"]);
            Assert.NotEqual(0, outcome.Code);
            Assert.Contains("--commit", outcome.Err, StringComparison.Ordinal);
            Assert.Empty(Directory.EnumerateFileSystemEntries(root));
        }
        finally { CliHarness.Delete(root); }
    }

    private static string[] Arguments(string root, string profile = "repository-synced") =>
        ["--estate", root, "--json", "estate", "init", "--name", "Example", "--slug", "example",
            "--purpose", "Prove initialization.", "--operator-name", "Operator", "--organization", "Example", "--profile", profile];

    private static CliHarness.Outcome WithVersion(string version, string[] arguments)
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        var context = new CommandContext(output, error);
        var tree = new VericairnCommand(context);
        var estate = new Command("estate");
        estate.Subcommands.Add(new EstateInitCommand(context, version));
        tree.Subcommands.Add(estate);
        var parsed = tree.Parse(arguments);
        Assert.Empty(parsed.Errors);
        int code = parsed.Invoke();
        return new(code, output.ToString(), error.ToString());
    }

    private static void AssertPin(string root, CliHarness.Outcome outcome, string commit,
        string profile = "repository-synced", string repository = "Vericairn/vericairn")
    {
        Assert.Equal(0, outcome.Code);
        Assert.Empty(outcome.Err);
        EstateModel estate = CommandContext.LoadCopy(root);
        Assert.False(estate.Findings.HasErrors, string.Join("\n", estate.Findings));
        Assert.False(EstateChecks.Run(estate).HasErrors);
        Assert.Equal(2, estate.Records.Count);
        Assert.Equal("source", estate.Manifest!.Mode);
        Assert.Equal(commit, estate.Manifest.Commit);
        Assert.Equal(repository, estate.Manifest.Repository);
        Assert.Equal(profile, estate.Manifest.Profile);
        Assert.Equal(estate.Manifest.Estate, outcome.Json.GetProperty("estate").GetString());
        Assert.Contains(estate.Records, r => r.Id == outcome.Json.GetProperty("operator").GetString() && r.Type == "principal");
    }

    private static void AssertRefusal(string root, CliHarness.Outcome outcome)
    {
        Assert.Equal(2, outcome.Code);
        Assert.Empty(outcome.Out);
        Assert.Contains("no recorded 40-digit source commit", outcome.Err, StringComparison.Ordinal);
        Assert.Contains("--commit <40-hex-commit>", outcome.Err, StringComparison.Ordinal);
        Assert.Empty(Directory.EnumerateFileSystemEntries(root));
    }
}
