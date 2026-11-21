// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.CommandLine;
using Vericairn.Cli;
using Vericairn.Records.Estate;
using Vericairn.Records.Model;
using Vericairn.Runtime.Tests;

namespace Vericairn.Cli.Tests;

/// <summary>OPS-24 (0.9.4): vericairn operator act --ready lists the ready items and signs the chosen ones in one reviewed act.</summary>
[Collection("cli")]
public sealed class OperatorActCommandTests
{
    public OperatorActCommandTests() => _ = CliHarness.BuiltExecutable;

    [Fact]
    public void ActIsAReadOnlyListingWithExplicitChoices()
    {
        RootCommand root = CliApp.Build(cancellationToken: TestContext.Current.CancellationToken);
        Command act = Assert.IsType<OperatorActCommand>(root.Subcommands.Single(c => c.Name == "operator").Subcommands.Single(c => c.Name == "act"));
        Assert.Equal(["--ready", "--all", "--select", "--skip", "--dismiss", "--by", "--stake", "--from"], act.Options.Select(o => o.Name));
        Assert.Empty(root.Parse(["operator", "act", "--ready"]).Errors);
        Assert.Empty(root.Parse(["operator", "act", "--ready", "--all", "--skip", "a", "--dismiss", "b=gone", "--by", "operator"]).Errors);
    }

    [Fact]
    public void TheListingShowsWhatEachSignatureRecordsAndWritesNothing()
    {
        using var f = new OperatorCloseFixture(device: false);
        var step = f.Add("STEP-1");
        RecordEnvelope request = OperatorActTests.Request(f, "action");
        int before = f.Estate.Load().Records.Count;

        CliHarness.Outcome human = Run(f, "--ready");

        Assert.Equal(0, human.Code);
        Assert.Contains("Ready for " + f.Estate.OperatorId + ": 2 items", human.Out, StringComparison.Ordinal);
        Assert.Contains("close:STEP-1  [close] STEP-1 result", human.Out, StringComparison.Ordinal);
        Assert.Contains("  Signs: an acceptance of result " + step.Result.Id, human.Out, StringComparison.Ordinal);
        Assert.Contains("  Basis: " + step.Review.Id + " at " + step.Review.ComputedHash, human.Out, StringComparison.Ordinal);
        Assert.Contains("request:" + request.Id, human.Out, StringComparison.Ordinal);
        Assert.Contains("  May be dismissed with a reason.", human.Out, StringComparison.Ordinal);
        Assert.Equal(2, Run(f, "--ready", "--json").Json.GetProperty("items").GetArrayLength());
        Assert.Equal(2, Run(f).Code);
        Assert.Equal(before, f.Estate.Load().Records.Count);
    }

    [Fact]
    public void AllSignsEveryItemButTheSkippedAndDismissesTheNamedRequest()
    {
        using var f = new OperatorCloseFixture();
        f.Add("STEP-1");
        RecordEnvelope ask = OperatorAnswerTests.Ask(f, "Ship it?", "Ship it now.");
        RecordEnvelope request = OperatorActTests.Request(f, "action");

        CliHarness.Outcome outcome = Run(f, "--ready", "--all", "--by", f.Estate.OperatorId, "--skip", "answer:" + ask.Id, "--dismiss", "request:" + request.Id + "=No longer needed.", "--json");

        Assert.Equal(0, outcome.Code);
        Assert.Equal(["close:STEP-1", "request:" + request.Id], outcome.Json.GetProperty("written").EnumerateArray().Select(s => s.GetProperty("item").GetString()));
        Assert.Contains("signs 1 and dismisses 1 of 3 ready items", outcome.Err, StringComparison.Ordinal);
        Assert.Contains("answer:" + ask.Id + "  [answer] Ship it?  -> wait", outcome.Err, StringComparison.Ordinal);
        EstateModel model = f.Estate.Load();
        Assert.Equal("open", Asks.State(model, ask).State);
        Assert.Equal("No longer needed.", EstateModel.BodyString(Assert.Single(model.OfType("operator-request-close")), "reason"));
    }

    [Fact]
    public void AChoiceThatCannotBeMadeRefusesWithoutAnyRecord()
    {
        using var f = new OperatorCloseFixture();
        f.Add("STEP-1");

        Assert.Equal(1, Run(f, "--ready", "--dismiss", "close:STEP-1=Not this.", "--by", f.Estate.OperatorId).Code);
        Assert.Equal(1, Run(f, "--ready", "--select", "close:NONE", "--by", f.Estate.OperatorId).Code);
        Assert.Equal(2, Run(f, "--ready", "--select", "close:STEP-1").Code);
        Assert.Equal(2, Run(f, "--ready", "--skip", "close:STEP-1", "--by", f.Estate.OperatorId).Code);
        Assert.Equal(2, Run(f, "--ready", "--dismiss", "close:STEP-1", "--by", f.Estate.OperatorId).Code);
        Assert.Empty(f.Estate.Load().OfType("acceptance"));
    }

    private static CliHarness.Outcome Run(OperatorCloseFixture fixture, params string[] arguments) =>
        CliHarness.RunWithApi(fixture.Api, null, ["--estate", fixture.Estate.Root, "operator", "act", .. arguments]);
}
