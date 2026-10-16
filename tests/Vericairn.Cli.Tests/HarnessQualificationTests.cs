// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

namespace Vericairn.Cli.Tests;

[Collection("cli")]
public class HarnessQualificationTests
{
    [Fact]
    public void QualifyCommandExplainsExactLocalProbesAndHumanAuthority()
    {
        CliHarness.Outcome result = CliHarness.Run("worker", "qualify", "--help");
        Assert.Equal(0, result.Code);
        foreach (string word in new[] { "--launcher", "--by", "human", "exact", "probe", "Remote" })
            Assert.Contains(word, result.Out, StringComparison.Ordinal);
    }
}
