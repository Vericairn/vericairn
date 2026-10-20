// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

namespace Vericairn.Cli.Tests;

public sealed class EstateLayoutJourneyTests
{
    [Theory]
    [InlineData(true, true, false, 0)]
    [InlineData(true, false, false, 1)]
    [InlineData(true, true, true, 1)]
    [InlineData(false, true, false, 1)]
    public void EstateCheckExcludesOnlyIgnoredUntrackedTopLevelEntries(bool git, bool ignored, bool tracked, int exit)
    {
        string root = CliHarness.TempDirectory("layout-check");
        try
        {
            CliHarness.Ok("--estate", root, "estate", "init", "--name", "Example", "--slug", "example",
                "--purpose", "Layout checks.", "--operator-name", "Operator", "--organization", "Example",
                "--commit", new string('a', 40));
            if (git) ContextJourneyTests.Git(root, "init", "-q");
            File.WriteAllText(Path.Combine(root, ".gitignore"), ignored ? ".DS_Store\n" : string.Empty);
            File.WriteAllText(Path.Combine(root, ".DS_Store"), "residue");
            if (tracked) ContextJourneyTests.Git(root, "add", "-f", "--", ".DS_Store");
            CliHarness.Outcome check = CliHarness.Run("--estate", root, "estate", "check");
            Assert.Equal(exit, check.Code);
            if (exit == 0) Assert.DoesNotContain(".DS_Store", check.Out, StringComparison.Ordinal);
            else Assert.Contains("layout.unexpected", check.Out, StringComparison.Ordinal);
            Assert.Equal(exit, CliHarness.Run("--estate", root, "--json", "estate", "check").Code);
        }
        finally
        {
            CliHarness.Delete(root);
        }
    }
}
