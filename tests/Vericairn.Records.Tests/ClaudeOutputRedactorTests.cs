// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Security.Cryptography;
using Vericairn.Records.Estate;

namespace Vericairn.Records.Tests;

public sealed class ClaudeOutputRedactorTests
{
    [Theory]
    [InlineData("unicode-box")]
    [InlineData("ascii-box")]
    [InlineData("timestamp")]
    [InlineData("quote")]
    [InlineData("concatenated-json")]
    [InlineData("soft-break")]
    [InlineData("intervening-json")]
    [InlineData("/")]
    [InlineData(".")]
    [InlineData(",")]
    [InlineData("|")]
    [InlineData("…")]
    [InlineData("url-path")]
    [InlineData("body")]
    public void TheProvisionIsWithheldDespiteDecorationOrMissingPrefix(string form)
    {
        string token = Token();
        string[] input = form switch
        {
            "unicode-box" => Wrap(70, "│", "│"),
            "ascii-box" => Wrap(70, "|", "|"),
            "timestamp" => Wrap(80, "2026-11-27T21:00:00Z ", ""),
            "quote" => Wrap(80, "> ", ""),
            "concatenated-json" => ["\"diagnostic: " + token[..80] + "\" +", "\"" + token[80..] + "\""],
            "soft-break" => Wrap(75, "", "="),
            "intervening-json" => [token[..100], """{"type":"status"}""", token[100..]],
            "body" => ["status(" + token[13..] + ")"],
            "url-path" => ["https://example.invalid/" + token[..60] + "/path/" + token[60..]],
            _ => [token[..60] + form + token[60..]],
        };
        var redactor = new SecretShapes.OutputRedactor(containsProvision: line => SecretShapes.ContainsProvisionFragment(line, token));
        string[] safe = Redact(redactor, ["{unrelated}", .. input, "{complete}"]);

        Assert.Equal(input.Length + 2, safe.Length);
        Assert.Equal("{unrelated}", safe[0]);
        Assert.Equal("{complete}", safe[^1]);
        AssertNoFragment(token, safe);

        string[] Wrap(int width, string start, string end) => [.. token.Chunk(width).Select(part => start + new string(part) + end)];
    }

    [Theory]
    [InlineData(4)]
    [InlineData(9)]
    [InlineData(12)]
    public void ABackslashBreakInThePrefixIsWithheldByShapeAlone(int split)
    {
        string token = Token();
        var redactor = new SecretShapes.OutputRedactor();
        string[] safe = Redact(redactor, [token[..split] + "\\", token[split..], "{complete}"]);

        Assert.True(safe.SequenceEqual(["<secret>", "<secret>", "{complete}"]));
    }

    [Theory]
    [InlineData("\\\\")]
    [InlineData("^")]
    [InlineData("`")]
    public void ADecoratedBreakInThePrefixIsWithheldByShapeAlone(string marker)
    {
        string[] ordinary = ["task ends" + marker, "{complete}"];
        Assert.Equal(ordinary, Redact(new SecretShapes.OutputRedactor(), ordinary));

        string token = Token();
        for (int split = 1; split < 13; split++)
        {
            string first = token[..split] + marker;
            string second = token[split..];
            var redactor = new SecretShapes.OutputRedactor();
            string[] safe = Redact(redactor, [first, second, "{complete}"]);

            bool safeStream = safe.SequenceEqual(["<secret>", "<secret>", "{complete}"]);
            Assert.True(safeStream, $"Streaming redaction failed for a prefix split at {split}.");
            string text = first + "\r\n" + second;
            Assert.True(SecretShapes.HasClaudeSetupToken(text), $"Shape detection failed for a prefix split at {split}.");
            Assert.True(SecretShapes.RedactOutput(text) == "<secret>", $"Whole-text redaction failed for a prefix split at {split}.");
        }
    }

    [Fact]
    public void ABackslashCanStillStartAnEscapeSplitAcrossLines()
    {
        string token = Token();
        var redactor = new SecretShapes.OutputRedactor();
        string[] safe = Redact(redactor, [token[..2] + "\\", "u002d" + token[3..], "{complete}"]);

        Assert.True(safe.SequenceEqual(["<secret>", "<secret>", "{complete}"]));
    }

    [Fact]
    public void EveryTwelveCharacterWindowIsWithheldWhileElevenCharactersAlonePass()
    {
        string token = Token();
        var redactor = new SecretShapes.OutputRedactor(containsProvision: line => SecretShapes.ContainsProvisionFragment(line, token));
        string shortText = "(" + token[40..51] + ")";
        string[] input = [shortText, .. Enumerable.Range(0, token.Length - 11).Select(i => "before" + token[i..(i + 12)] + "after!")];
        string[] safe = Redact(redactor, input);

        Assert.Equal(input.Length, safe.Length);
        Assert.True(safe[0] == shortText);
        Assert.All(safe.Skip(1), line => Assert.True(line == "<secret>"));
    }

    [Fact]
    public void LineParitySurvivesRandomEscapeFragmentsAndMegabyteLines()
    {
        string token = Token();
        var redactor = new SecretShapes.OutputRedactor(containsProvision: line => SecretShapes.ContainsProvisionFragment(line, token));
        var random = new Random(167);
        string[] fragments = ["\\", "u002", "2d", "%", "%2d", "&", "&#45;", "\u001b[", "1;31m", " ", "suffix", token[50..62]];
        string[] input = [.. Enumerable.Range(0, 3000).Select(_ => string.Concat(Enumerable.Range(0, random.Next(1, 12))
            .Select(_ => fragments[random.Next(fragments.Length)]))), new string('X', 1_000_000), new string('Y', 2_000_000), "{complete}"];
        string[] safe = Redact(redactor, input);

        Assert.Equal(input.Length, safe.Length);
        Assert.Equal("{complete}", safe[^1]);
        AssertNoFragment(token, safe);
    }

    private static string Token() => "sk-ant-" + "oat01-" + RandomNumberGenerator.GetHexString(202);

    private static string[] Redact(SecretShapes.OutputRedactor redactor, string[] input) =>
        [.. input.SelectMany(redactor.Push).Concat(redactor.Complete()).Select(line => line.Text)];

    private static void AssertNoFragment(string token, IEnumerable<string> lines) =>
        Assert.All(lines, line => Assert.DoesNotContain(Enumerable.Range(0, token.Length - 11), offset =>
            line.AsSpan().Contains(token.AsSpan(offset, 12), StringComparison.Ordinal)));
}
