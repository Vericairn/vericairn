// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using Vericairn.Records.Estate;
using Vericairn.Runtime.Providers;

namespace Vericairn.Runtime.Tests;

public sealed class ClaudeSecretShapeTests
{
    [Theory]
    [InlineData("space-after-prefix")]
    [InlineData("space-inside-prefix")]
    [InlineData("terminal")]
    [InlineData("terminal-escaped")]
    [InlineData("wrapped-after-prefix")]
    [InlineData("wrapped-inside-prefix")]
    [InlineData("wrapped-inside-escape")]
    [InlineData("escaped-newline")]
    [InlineData("double-unicode")]
    [InlineData("hexadecimal")]
    [InlineData("percent")]
    [InlineData("html")]
    public void RedactsChangedOrWrappedSpellingsWithoutLeavingAnyFragments(string form)
    {
        string token = FakeHarness.NewClaudeSetupToken();
        string changed = form switch
        {
            "space-after-prefix" => token.Insert(60, " \t"),
            "space-inside-prefix" => token.Insert(9, " \t"),
            "terminal" => token.Insert(60, "\u001b[1;31m"),
            "terminal-escaped" => token.Insert(8, "\\u001b[1;31m"),
            "wrapped-after-prefix" => token.Insert(100, "\r\n"),
            "wrapped-inside-prefix" => token.Insert(10, "\n"),
            "wrapped-inside-escape" => token.Replace("-", "\\u00\n2d", StringComparison.Ordinal),
            "escaped-newline" => token.Insert(100, "\\n"),
            "double-unicode" => token.Replace("-", "\\\\u002d", StringComparison.Ordinal),
            "hexadecimal" => token.Replace("-", "\\x2d", StringComparison.Ordinal),
            "percent" => token.Replace("-", "%2D", StringComparison.Ordinal),
            "html" => token.Replace("-", "&#45;", StringComparison.Ordinal),
            _ => throw new ArgumentOutOfRangeException(nameof(form)),
        };
        string text = "diagnostic: " + changed;
        Assert.True(SecretShapes.HasClaudeSetupToken(text));
        Assert.True(SecretShapes.RedactOutput(text) == "<secret>");
    }

    [Theory]
    [InlineData(1)]
    [InlineData(10)]
    [InlineData(60)]
    [InlineData(100)]
    public void WithholdsAllContinuationLinesEvenAcrossBlankLines(int split)
    {
        string token = FakeHarness.NewClaudeSetupToken();
        var redactor = new SecretShapes.OutputRedactor();
        string[] lines = ["unrelated event", token[..split], "", token[split..110], token[110..] + "!", "ordinary error"];
        string[] safe = [.. lines.SelectMany(redactor.Push).Concat(redactor.Complete()).Select(line => line.Text)];
        Assert.Equal(6, safe.Length);
        Assert.True(safe[0] == lines[0]);
        Assert.True(safe[1] == "<secret>");
        Assert.True(safe[3] == "<secret>");
        Assert.True(safe[4] == "<secret>");
        Assert.True(safe[5] == lines[5]);
    }

    [Fact]
    public void ALineBreakInsideAnEscapeStillHidesBothLines()
    {
        string token = FakeHarness.NewClaudeSetupToken();
        var redactor = new SecretShapes.OutputRedactor();
        string[] lines = [token[..2] + "\\u00", "2d" + token[3..] + "!", "ordinary event"];
        string[] safe = [.. lines.SelectMany(redactor.Push).Concat(redactor.Complete()).Select(line => line.Text)];
        Assert.True(safe.SequenceEqual(["<secret>", "<secret>", "ordinary event"]));
    }

    [Fact]
    public void AWrapInsideTerminalDecorationStillWithholdsTheFollowingPayload()
    {
        string token = FakeHarness.NewClaudeSetupToken();
        var redactor = new SecretShapes.OutputRedactor();
        string[] lines = [token[..60] + "\u001b[", "1;31m" + token[60..100], token[100..] + "!", "{ordinary event}"];
        string[] safe = [.. lines.SelectMany(redactor.Push).Concat(redactor.Complete()).Select(line => line.Text)];
        Assert.True(safe.SequenceEqual(["<secret>", "<secret>", "<secret>", "{ordinary event}"]));
    }

    [Fact]
    public void ATruncatedDistinctivePrefixIsWithheldAtEndOfStream()
    {
        string token = FakeHarness.NewClaudeSetupToken();
        var redactor = new SecretShapes.OutputRedactor();
        Assert.False(redactor.Push("possible incomplete sign-in: " + token[..10]).Any());
        Assert.True(redactor.Complete().Select(line => line.Text).SequenceEqual(["<secret>"]));
    }

    [Fact]
    public void UnrelatedOutputAndThePrefixOfAnAdjacentPlainTokenAreRetained()
    {
        string token = FakeHarness.NewClaudeSetupToken();
        Assert.True(SecretShapes.RedactOutput("diagnostic: adjacent" + token) == "diagnostic: adjacent<secret>");
        Assert.Equal("ordinary messages", SecretShapes.RedactOutput("ordinary messages"));
        var redactor = new SecretShapes.OutputRedactor();
        string[] lines = ["ordinary messages", "kept on a second line", "one remaining message ends"];
        string[] safe = [.. lines.SelectMany(redactor.Push).Concat(redactor.Complete()).Select(line => line.Text)];
        Assert.Equal(lines, safe);
    }

    [Fact]
    public void ProbeRefusesWrappedSetupTokenOutputWithoutQuotingTheCredential()
    {
        using var fixture = new NativeProbe("claude");
        string token = FakeHarness.NewClaudeSetupToken();
        fixture.Behavior("helpSuffix", token.Insert(10, "\n"));
        var refusal = Assert.Throws<ProviderRefusedException>(() => fixture.Launcher.Preflight(fixture.Prepare()));
        Assert.Contains("inventory probe failed", refusal.Message, StringComparison.Ordinal);
        Assert.False(refusal.Message.Contains(token[10..], StringComparison.Ordinal));
    }

    [Fact]
    public void ProbeRefusesPrefixFreeProvisionOutputBeforeItEntersEvidence()
    {
        string token = FakeHarness.NewClaudeSetupToken();
        using var fixture = new NativeProbe("claude", setupToken: token);
        fixture.Behavior("helpSuffix", "diagnostic: (" + token[13..] + ")");
        var refusal = Record.Exception(() => fixture.Launcher.Preflight(fixture.Prepare()));
        Assert.True(refusal is ProviderRefusedException && refusal.Message.Contains("inventory probe failed", StringComparison.Ordinal));
        Assert.False(refusal!.Message.Contains(token[13..], StringComparison.Ordinal));
    }
}
