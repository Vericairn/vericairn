// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.CommandLine;
using Vericairn.Cli;
using Vericairn.Records.Model;
using Vericairn.Runtime.Tests;

namespace Vericairn.Cli.Tests;

/// <summary>OPS-16 (0.9.4): vericairn operator answer lists the asks with their recommendations by hash and answers a selection, or all, in one act.</summary>
[Collection("cli")]
public sealed class OperatorAnswerCommandTests
{
    public OperatorAnswerCommandTests() => _ = CliHarness.BuiltExecutable;

    [Fact]
    public void AnswerIsAReadOnlyListingByDefaultWithExplicitSelectionOptions()
    {
        RootCommand root = CliApp.Build(cancellationToken: TestContext.Current.CancellationToken);
        Command answer = Assert.IsType<OperatorAnswerCommand>(root.Subcommands.Single(c => c.Name == "operator").Subcommands.Single(c => c.Name == "answer"));
        Assert.Equal(["--select", "--all", "--by", "--stake", "--from"], answer.Options.Select(o => o.Name));
        Assert.Empty(root.Parse(["operator", "answer"]).Errors);
        Assert.Empty(root.Parse(["operator", "answer", "--select", "A", "--select", "B", "--by", "operator", "--stake", "high"]).Errors);
        Assert.NotEmpty(root.Parse(["operator", "answer", "--all", "--by", "operator", "--stake", "huge"]).Errors);
    }

    [Fact]
    public void TheListingShowsEachAskAndRecommendationByHashFirstAndWritesNothing()
    {
        using var f = new OperatorCloseFixture(device: false);
        RecordEnvelope ask = OperatorAnswerTests.Ask(f, "Ship it?", "Ship it now.");
        int before = f.Estate.Load().Records.Count;

        CliHarness.Outcome human = Run(f);

        Assert.Equal(0, human.Code);
        string[] lines = human.Out.Split('\n');
        int first = Array.FindIndex(lines, l => l.StartsWith("Ask " + ask.Id + " at " + ask.ComputedHash, StringComparison.Ordinal));
        Assert.True(first >= 0, human.Out);
        Assert.StartsWith("  Recommendation sha256:", lines[first + 1], StringComparison.Ordinal);
        Assert.EndsWith(": Ship it now.", lines[first + 1], StringComparison.Ordinal);
        Assert.Equal("  Question: Ship it?", lines[first + 2]);
        CliHarness.Outcome json = Run(f, "--json");
        Assert.Equal(ask.Id, Assert.Single(json.Json.GetProperty("items").EnumerateArray()).GetProperty("ask").GetProperty("id").GetString());
        Assert.Equal(before, f.Estate.Load().Records.Count);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ASelectionOrAllAnswersOnlyTheShownAsksInOneAct(bool all)
    {
        using var f = new OperatorCloseFixture();
        RecordEnvelope first = OperatorAnswerTests.Ask(f, "First?", "Take the first.");
        OperatorAnswerTests.Ask(f, "Second?", "Take the second.");
        RecordEnvelope third = OperatorAnswerTests.Ask(f, "Third?", "Take the third.");
        string[] selection = all ? ["--all"] : ["--select", first.Id, "--select", third.Id];

        CliHarness.Outcome outcome = Run(f, ["--json", "--by", f.Estate.OperatorId, .. selection]);

        Assert.Equal(0, outcome.Code);
        Assert.True(outcome.Json.GetProperty("ok").GetBoolean());
        Assert.False(string.IsNullOrEmpty(outcome.Json.GetProperty("act").GetString()));
        Assert.Equal(all ? 3 : 2, outcome.Json.GetProperty("written").GetArrayLength());
        // The act is shown, by hash, before signing.
        Assert.Contains(first.ComputedHash, outcome.Err, StringComparison.Ordinal);
        Assert.Contains("One act answers " + (all ? 3 : 2) + " asks with their recommendations", outcome.Err, StringComparison.Ordinal);
        Assert.Equal(all ? 0 : 1, f.Api.OperatorAnswerCandidates().Items.Count);
    }

    [Fact]
    public void ASavedListingRefusesAnAskThatChangedWithoutAnyDecision()
    {
        using var f = new OperatorCloseFixture();
        RecordEnvelope ask = OperatorAnswerTests.Ask(f, "First?", "Take the first.");
        string saved = Path.Combine(f.Estate.Root, "answers.json");
        File.WriteAllText(saved, Run(f, "--json").Out);
        f.Estate.Add("ask", ask.By, ask.Body.GetRawText().Replace("\"open\"", "\"notified\"", StringComparison.Ordinal), supersedes: ask.Id);

        CliHarness.Outcome refused = Run(f, "--from", saved, "--all", "--by", f.Estate.OperatorId);

        Assert.Equal(1, refused.Code);
        Assert.Empty(f.Estate.Load().OfType("decision"));
    }

    [Fact]
    public void ASelectionNeedsTheOperatorAndAsksPutToThem()
    {
        using var f = new OperatorCloseFixture();
        RecordEnvelope ask = OperatorAnswerTests.Ask(f, "First?", "Take the first.");

        Assert.Equal(2, Run(f, "--select", ask.Id).Code);
        Assert.Equal(2, Run(f, "--all", "--select", ask.Id, "--by", f.Estate.OperatorId).Code);
        Assert.Equal(1, Run(f, "--select", ask.Id, "--by", f.Estate.BuilderId).Code);
        Assert.Empty(f.Estate.Load().OfType("decision"));
    }

    private static CliHarness.Outcome Run(OperatorCloseFixture fixture, params string[] arguments) =>
        CliHarness.RunWithApi(fixture.Api, null, ["--estate", fixture.Estate.Root, "operator", "answer", .. arguments]);
}
